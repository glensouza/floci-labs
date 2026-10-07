#:package AWSSDK.Lightsail@4.0.102.1

// Amazon Lightsail against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates an instance and waits for it to run, the way production code has to, then tries to reach
// it. Real Lightsail boots a virtual machine: the instance is "pending" for a minute or so, then
// answers on its public address. The lab prints what this Floci answered, so it tells you if that
// ever changes. The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using System.Diagnostics;
using System.Net.Sockets;
using Amazon.Lightsail;
using Amazon.Lightsail.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonLightsailClient lightsail = new AmazonLightsailClient(credentials, new AmazonLightsailConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A fresh name per run means a crashed run never collides with the next one.
string run = Guid.NewGuid().ToString("N")[..12];
string instance = $"lab-{run}";
string odd = $"lab-{run}-odd";
List<string> created = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    created.Add(instance);
    CreateInstancesResponse create = await lightsail.CreateInstancesAsync(new CreateInstancesRequest
    {
        InstanceNames = [instance],
        AvailabilityZone = "us-east-1a",
        BlueprintId = "amazon_linux_2023",
        BundleId = "nano_3_0",
    });

    foreach (Operation operation in create.Operations ?? [])
    {
        Console.WriteLine($"CreateInstances      -> {operation.OperationType} {operation.ResourceName}: {operation.Status}");
    }

    // This is the wait production code needs: real Lightsail answers "pending" while the VM boots,
    // and anything that connects before "running" finds nothing there.
    Stopwatch waited = Stopwatch.StartNew();
    string state = (await lightsail.GetInstanceStateAsync(new GetInstanceStateRequest { InstanceName = instance })).State.Name;
    string first = state;
    int polls = 1;
    while (state != "running" && waited.Elapsed < TimeSpan.FromMinutes(3))
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        state = (await lightsail.GetInstanceStateAsync(new GetInstanceStateRequest { InstanceName = instance })).State.Name;
        polls++;
    }

    Console.WriteLine($"Wait for running     -> {state} after {polls} poll(s), {waited.Elapsed.TotalSeconds:0.0} s (first read: {first})");

    Instance found = (await lightsail.GetInstanceAsync(new GetInstanceRequest { InstanceName = instance })).Instance;
    Console.WriteLine($"GetInstance          -> public {found.PublicIpAddress}, private {found.PrivateIpAddress}, login {found.Username}");

    // Port 22 is open by default on a Lightsail Linux instance; on AWS, sshd answers here once it runs.
    string ssh;
    using (TcpClient tcp = new TcpClient())
    {
        try
        {
            await tcp.ConnectAsync(found.PublicIpAddress, 22).WaitAsync(TimeSpan.FromSeconds(3));
            ssh = "connected";
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException)
        {
            ssh = $"no answer ({ex.GetType().Name})";
        }
    }

    Console.WriteLine($"TCP {found.PublicIpAddress}:22 -> {ssh}");

    // Real Lightsail refuses a blueprint id that GetBlueprints does not list.
    try
    {
        created.Add(odd);
        await lightsail.CreateInstancesAsync(new CreateInstancesRequest { InstanceNames = [odd], AvailabilityZone = "us-east-1a", BlueprintId = "no-such-blueprint", BundleId = "nano_3_0" });
        Console.WriteLine("Blueprint \"no-such-blueprint\" -> accepted");
    }
    catch (AmazonLightsailException ex)
    {
        Console.WriteLine($"Blueprint \"no-such-blueprint\" -> {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine();
    Console.WriteLine(first == "running" && ssh != "connected"
        ? "The instance was running on the first read, and nothing answers on its address. Floci keeps\n  the record, not a machine: code that skips the wait, or that needs to reach the instance,\n  passes here and is untested."
        : $"The instance started {first} and port 22 says {ssh}. Floci may now boot something: re-read this lab.");
}
finally
{
    int deleted = 0;
    foreach (string name in created)
    {
        try
        {
            await lightsail.DeleteInstanceAsync(new DeleteInstanceRequest { InstanceName = name });
            deleted++;
        }
        catch (NotFoundException)
        {
            // Never created: the create above was refused.
        }
    }

    if (deleted != 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Cleanup -> {deleted} instance(s) deleted");
    }
}
