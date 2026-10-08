#:package AWSSDK.ApplicationAutoScaling@4.0.100.16

// Application Auto Scaling against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Registers the write capacity of a DynamoDB table that does not exist as a scalable target, gives
// it a target-tracking policy and reads both back. Then it sends two requests real Application Auto
// Scaling has a firm answer to, a minimum above the maximum and the deregistration of a target that
// was never registered, and asks for a scheduled action. The lab prints what this Floci answered,
// so it tells you if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.ApplicationAutoScaling;
using Amazon.ApplicationAutoScaling.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonApplicationAutoScalingClient scaling = new AmazonApplicationAutoScalingClient(credentials, new AmazonApplicationAutoScalingConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A fresh name per run means a crashed run never collides with the next one. No table by either
// name is ever created.
string run = Guid.NewGuid().ToString("N")[..12];
string table = $"table/lab-{run}";
string range = $"table/lab-{run}-range";
string gone = $"table/lab-{run}-gone";
string policy = "write-70";
List<string> targets = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

async Task<string> AnswerAsync(Func<Task> call)
{
    try
    {
        await call();
        return "accepted";
    }
    catch (AmazonApplicationAutoScalingException ex)
    {
        return $"{ex.ErrorCode}: {ex.Message}";
    }
}

Task RegisterAsync(string resourceId, int min, int max)
{
    targets.Add(resourceId);
    return scaling.RegisterScalableTargetAsync(new RegisterScalableTargetRequest
    {
        ServiceNamespace = ServiceNamespace.Dynamodb,
        ResourceId = resourceId,
        ScalableDimension = ScalableDimension.DynamodbTableWriteCapacityUnits,
        MinCapacity = min,
        MaxCapacity = max,
    });
}

try
{
    // AWS: ValidationException, because it looks the table up and there is none.
    string phantom = await AnswerAsync(() => RegisterAsync(table, 1, 10));
    Console.WriteLine($"Register a table that does not exist -> {phantom}");

    if (phantom == "accepted")
    {
        ScalableTarget found = (await scaling.DescribeScalableTargetsAsync(new DescribeScalableTargetsRequest { ServiceNamespace = ServiceNamespace.Dynamodb, ResourceIds = [table] })).ScalableTargets[0];
        Console.WriteLine($"Read it back                         -> {found.ResourceId}, {found.MinCapacity}..{found.MaxCapacity}");

        PutScalingPolicyResponse put = await scaling.PutScalingPolicyAsync(new PutScalingPolicyRequest
        {
            PolicyName = policy,
            ServiceNamespace = ServiceNamespace.Dynamodb,
            ResourceId = table,
            ScalableDimension = ScalableDimension.DynamodbTableWriteCapacityUnits,
            PolicyType = PolicyType.TargetTrackingScaling,
            TargetTrackingScalingPolicyConfiguration = new TargetTrackingScalingPolicyConfiguration
            {
                PredefinedMetricSpecification = new PredefinedMetricSpecification { PredefinedMetricType = MetricType.DynamoDBWriteCapacityUtilization },
                TargetValue = 70,
            },
        });
        Console.WriteLine($"Target-tracking policy at 70%        -> {put.Alarms?.Count ?? 0} CloudWatch alarm(s) created with it");
    }

    // AWS: ValidationException, because the minimum is above the maximum.
    string inverted = await AnswerAsync(() => RegisterAsync(range, 10, 1));
    Console.WriteLine($"MinCapacity 10, MaxCapacity 1        -> {inverted}");

    // AWS: ObjectNotFoundException.
    string deregister = await AnswerAsync(() => scaling.DeregisterScalableTargetAsync(new DeregisterScalableTargetRequest { ServiceNamespace = ServiceNamespace.Dynamodb, ResourceId = gone, ScalableDimension = ScalableDimension.DynamodbTableWriteCapacityUnits }));
    Console.WriteLine($"Deregister a target never registered -> {deregister}");

    // AWS: accepted. Floci answers HTTP 400 UnsupportedOperation, not 501.
    string scheduled = await AnswerAsync(() => scaling.PutScheduledActionAsync(new PutScheduledActionRequest
    {
        ScheduledActionName = $"lab-{run}-nightly",
        ServiceNamespace = ServiceNamespace.Dynamodb,
        ResourceId = table,
        ScalableDimension = ScalableDimension.DynamodbTableWriteCapacityUnits,
        Schedule = "cron(0 2 * * ? *)",
        ScalableTargetAction = new ScalableTargetAction { MinCapacity = 1, MaxCapacity = 2 },
    }));
    Console.WriteLine($"A nightly scheduled action           -> {scheduled}");

    int refusalsMissed = (phantom == "accepted" ? 1 : 0) + (inverted == "accepted" ? 1 : 0);

    Console.WriteLine();
    Console.WriteLine(refusalsMissed == 2
        ? "Floci registered a table that does not exist and a minimum above the maximum; AWS refuses both.\n  Code that relies on Application Auto Scaling to catch these passes here and is untested."
        : $"Floci accepted {refusalsMissed} of the two registrations AWS refuses. Its validation changed: re-read this lab.");
    Console.WriteLine(scheduled.StartsWith("UnsupportedOperation", StringComparison.Ordinal)
        ? "Scheduled actions are not built yet: Floci answers UnsupportedOperation."
        : "Floci answered the scheduled action differently: it may have built them. Re-read this lab.");
}
finally
{
    int deleted = 0;
    foreach (string resourceId in targets)
    {
        try
        {
            // Deregistering a target also deletes its policies (and its scheduled actions, on AWS).
            await scaling.DeregisterScalableTargetAsync(new DeregisterScalableTargetRequest { ServiceNamespace = ServiceNamespace.Dynamodb, ResourceId = resourceId, ScalableDimension = ScalableDimension.DynamodbTableWriteCapacityUnits });
            deleted++;
        }
        catch (ObjectNotFoundException)
        {
            // Never registered: the register above was refused.
        }
    }

    int policiesLeft = (await scaling.DescribeScalingPoliciesAsync(new DescribeScalingPoliciesRequest { ServiceNamespace = ServiceNamespace.Dynamodb, ResourceId = table })).ScalingPolicies?.Count ?? 0;

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> {deleted} target(s) deregistered, {policiesLeft} policy(ies) left on the table");
}
