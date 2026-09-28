#:package AWSSDK.Scheduler@4.0.100.12
#:package AWSSDK.SQS@4.0.100.11

// EventBridge Scheduler against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs   (takes about a minute: it waits for the schedule to fire)
//
// The only Floci-specific lines are the endpoint, the dummy credentials, MaxErrorRetry and the
// made-up role ARN. Against real AWS the role has to exist and be assumable by scheduler.amazonaws.com.

using System.Diagnostics;
using Amazon.Runtime;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using ScheduleNotFoundException = Amazon.Scheduler.Model.ResourceNotFoundException;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonSchedulerClient scheduler = new AmazonSchedulerClient(credentials, new AmazonSchedulerConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
using AmazonSQSClient sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Fresh names per run, so a crashed run never breaks the next one.
string suffix = Guid.NewGuid().ToString("N")[..12];
string scheduleName = $"every-minute-{suffix}";
string badScheduleName = $"typo-{suffix}";
const string roleArn = "arn:aws:iam::000000000000:role/scheduler-role-that-does-not-exist";
List<string> createdSchedules = [];
string? queueUrl = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    queueUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"scheduled-{suffix}" })).QueueUrl;
    string queueArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn"] })).Attributes["QueueArn"];

    // 1. A real schedule. The target is live: once this exists, Floci invokes it on a timer, so
    //    the finally below is what stops it firing forever.
    createdSchedules.Add(scheduleName);
    await scheduler.CreateScheduleAsync(new CreateScheduleRequest
    {
        Name = scheduleName,
        ScheduleExpression = "rate(1 minute)",
        FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
        Target = new Target { Arn = queueArn, RoleArn = roleArn, Input = """{ "job": "nightly-report" }""" },
    });
    Console.WriteLine("CreateSchedule rate(1 minute) -> SQS queue; waiting for it to fire (up to 90 s)...");

    Stopwatch stopwatch = Stopwatch.StartNew();
    Message? fired = null;
    while (fired is null && stopwatch.Elapsed < TimeSpan.FromSeconds(90))
    {
        ReceiveMessageResponse batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, WaitTimeSeconds = 10 });
        fired = batch.Messages?.FirstOrDefault();
    }

    Console.WriteLine(fired is null
        ? "  Nothing arrived within 90 s: this build stores schedules but does not fire them."
        : $"  Fired after {stopwatch.Elapsed.TotalSeconds:F0} s with payload {fired.Body}");
    Console.WriteLine("  (It fired with a role ARN that does not exist. Real Scheduler rejects that at create time.)");
    Console.WriteLine();

    // 2. The quiet one: an expression that is not an expression.
    try
    {
        createdSchedules.Add(badScheduleName);
        await scheduler.CreateScheduleAsync(new CreateScheduleRequest
        {
            Name = badScheduleName,
            ScheduleExpression = "not-a-rate",
            FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
            Target = new Target { Arn = queueArn, RoleArn = roleArn },
        });

        GetScheduleResponse readBack = await scheduler.GetScheduleAsync(new GetScheduleRequest { Name = badScheduleName });
        Console.WriteLine($"CreateSchedule \"not-a-rate\" -> accepted; GetSchedule reads back \"{readBack.ScheduleExpression}\"");
        Console.WriteLine("  Real Scheduler answers ValidationException. A typo in a cron expression round-trips\n  perfectly here and fails at deploy, so keep a real deployment in the loop for expressions.");
    }
    catch (ValidationException ex)
    {
        Console.WriteLine($"CreateSchedule \"not-a-rate\" -> ValidationException, as real Scheduler does: {ex.Message}");
    }
}
finally
{
    // Runs even when a step above throws. Don't remove it: a leftover schedule keeps firing.
    foreach (string name in createdSchedules)
    {
        try
        {
            await scheduler.DeleteScheduleAsync(new DeleteScheduleRequest { Name = name });
        }
        catch (ScheduleNotFoundException)
        {
            // The create never landed (the "not-a-rate" one, on a build that validates).
        }
    }

    if (queueUrl is not null)
    {
        await sqs.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = queueUrl });
    }

    Console.WriteLine();
    Console.WriteLine("Cleanup -> schedules and queue deleted");
}
