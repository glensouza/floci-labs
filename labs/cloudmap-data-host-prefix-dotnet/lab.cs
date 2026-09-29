#:package AWSSDK.ServiceDiscovery@4.0.101.10

// AWS Cloud Map against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates an HTTP namespace, a service and one instance, then asks for the instance back with
// DiscoverInstances — twice. Once with the SDK's defaults, and once with the one extra setting
// Cloud Map needs against an emulator. DiscoverInstances is Cloud Map's data-plane call, and the
// SDK sends it to a "data-" prefixed host, which works for servicediscovery.<region>.amazonaws.com
// and cannot work for 127.0.0.1. The lab prints what each attempt did, so it tells you if that
// ever changes.

using Amazon.Runtime;
using Amazon.ServiceDiscovery;
using Amazon.ServiceDiscovery.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonServiceDiscoveryClient cloudMap = new AmazonServiceDiscoveryClient(credentials, new AmazonServiceDiscoveryConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// The only difference between the two clients: DisableHostPrefixInjection. Real AWS needs the
// prefix, so this setting belongs on the emulator path only.
using AmazonServiceDiscoveryClient unprefixed = new AmazonServiceDiscoveryClient(credentials, new AmazonServiceDiscoveryConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0, DisableHostPrefixInjection = true });

// A fresh namespace per run, so a crashed run never collides with the next. .test is reserved
// (RFC 2606) and 192.0.2.10 is TEST-NET-1 (RFC 5737): nothing here can point at a real host.
string run = Guid.NewGuid().ToString("N")[..12];
string namespaceName = $"lab-{run}.test";
string? namespaceId = null;
string? serviceId = null;
bool registered = false;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    // Namespaces, instances and deregistration are asynchronous in Cloud Map: the answer is an
    // operation id, and the work is done when GetOperation says SUCCESS. On AWS that takes seconds
    // to a minute, so production code waits; the lab waits too and reports how long it took.
    CreateHttpNamespaceResponse createdNamespace = await cloudMap.CreateHttpNamespaceAsync(new CreateHttpNamespaceRequest { Name = namespaceName, CreatorRequestId = run });
    (Operation namespaceOperation, string namespaceWait) = await WaitAsync(createdNamespace.OperationId);
    namespaceId = namespaceOperation.Targets[OperationTargetType.NAMESPACE.Value];
    Console.WriteLine($"CreateHttpNamespace           -> {namespaceId}, {namespaceWait}");

    CreateServiceResponse createdService = await cloudMap.CreateServiceAsync(new CreateServiceRequest { Name = "web", NamespaceId = namespaceId, CreatorRequestId = run });
    serviceId = createdService.Service.Id;
    Console.WriteLine($"CreateService                 -> {serviceId}");

    RegisterInstanceResponse registeredInstance = await cloudMap.RegisterInstanceAsync(new RegisterInstanceRequest
    {
        ServiceId = serviceId,
        InstanceId = "web-1",
        Attributes = new Dictionary<string, string> { ["AWS_INSTANCE_IPV4"] = "192.0.2.10", ["AWS_INSTANCE_PORT"] = "8080" },
    });
    registered = true;
    (_, string registerWait) = await WaitAsync(registeredInstance.OperationId);
    Console.WriteLine($"RegisterInstance              -> web-1, {registerWait}");

    // Every call so far is control plane and went to the endpoint as given. This one is data plane.
    DiscoverInstancesRequest discover = new DiscoverInstancesRequest { NamespaceName = namespaceName, ServiceName = "web" };
    string? prefixedError = null;
    try
    {
        DiscoverInstancesResponse found = await cloudMap.DiscoverInstancesAsync(discover);
        Console.WriteLine($"DiscoverInstances (default)   -> {found.Instances.Count} instance(s)");
    }
    catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException)
    {
        // The message names the host the SDK tried, which is the whole story.
        prefixedError = ex.Message;
        Console.WriteLine($"DiscoverInstances (default)   -> failed: {prefixedError}");
    }

    DiscoverInstancesResponse discovered = await unprefixed.DiscoverInstancesAsync(discover);
    Console.WriteLine($"DiscoverInstances (no prefix) -> {discovered.Instances.Count} instance(s): {string.Join(", ", discovered.Instances.Select(i => $"{i.InstanceId} {i.Attributes["AWS_INSTANCE_IPV4"]}:{i.Attributes["AWS_INSTANCE_PORT"]} {i.HealthStatus}"))}");
    Console.WriteLine();

    Console.WriteLine(prefixedError is not null
        ? "With the SDK's defaults, the one data-plane call failed while every control-plane call\n  worked: the SDK sent DiscoverInstances to \"data-\" + the endpoint's host. Against an\n  emulator, set DisableHostPrefixInjection = true on the client config; against AWS, leave it."
        : "DiscoverInstances worked with the SDK's defaults. The host prefix no longer gets in the way,\n  so DisableHostPrefixInjection is not needed against this Floci and SDK.");
}
finally
{
    // Deregister, then delete the service, then the namespace: Cloud Map refuses each delete while
    // the thing inside it still exists. Deregistration is asynchronous too, so it is waited on.
    if (registered && serviceId is not null)
    {
        DeregisterInstanceResponse deregistered = await cloudMap.DeregisterInstanceAsync(new DeregisterInstanceRequest { ServiceId = serviceId, InstanceId = "web-1" });
        await WaitAsync(deregistered.OperationId);
    }

    if (serviceId is not null)
    {
        await cloudMap.DeleteServiceAsync(new DeleteServiceRequest { Id = serviceId });
    }

    if (namespaceId is not null)
    {
        await cloudMap.DeleteNamespaceAsync(new DeleteNamespaceRequest { Id = namespaceId });
        Console.WriteLine();
        Console.WriteLine("Cleanup -> instance deregistered, service and namespace deleted");
    }
}

// Polls every 5 s for up to 90 s, which is sized for real Cloud Map, and says how long it waited.
async Task<(Operation Operation, string Wait)> WaitAsync(string operationId)
{
    DateTime started = DateTime.UtcNow;
    int polls = 1;
    GetOperationResponse response = await cloudMap.GetOperationAsync(new GetOperationRequest { OperationId = operationId });
    while (response.Operation.Status != OperationStatus.SUCCESS && response.Operation.Status != OperationStatus.FAIL && DateTime.UtcNow - started < TimeSpan.FromSeconds(90))
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        response = await cloudMap.GetOperationAsync(new GetOperationRequest { OperationId = operationId });
        polls++;
    }

    if (response.Operation.Status != OperationStatus.SUCCESS)
    {
        throw new InvalidOperationException($"{response.Operation.Type} was {response.Operation.Status}: {response.Operation.ErrorMessage}");
    }

    return (response.Operation, $"operation SUCCESS after {polls} poll(s), {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
}
