#:package AWSSDK.Pipes@4.0.100.12
#:package AWSSDK.SQS@4.0.100.11

// EventBridge Pipes against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials, MaxErrorRetry and the
// made-up role ARN. Against real AWS the role has to exist and be assumable by pipes.amazonaws.com.

using System.Diagnostics;
using System.Text.Json;
using Amazon.Pipes;
using Amazon.Pipes.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonPipesClient pipes = new AmazonPipesClient(credentials, new AmazonPipesConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
using AmazonSQSClient sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Fresh names per run, so a crashed run never breaks the next one.
string suffix = Guid.NewGuid().ToString("N")[..12];
string pipeName = $"orders-{suffix}";
bool pipeCreated = false;
string? sourceUrl = null;
string? targetUrl = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    sourceUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"pipe-source-{suffix}" })).QueueUrl;
    targetUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"pipe-target-{suffix}" })).QueueUrl;
    string sourceArn = await QueueArnAsync(sourceUrl);
    string targetArn = await QueueArnAsync(targetUrl);

    // Real Pipes answers CREATING and moves through STARTING to RUNNING over seconds, and rejects
    // a stop or delete mid-transition with ConflictException. Watch what state comes back here.
    pipeCreated = true;
    CreatePipeResponse created = await pipes.CreatePipeAsync(new CreatePipeRequest
    {
        Name = pipeName,
        Source = sourceArn,
        Target = targetArn,
        RoleArn = "arn:aws:iam::000000000000:role/pipe-role-that-does-not-exist",
    });
    Console.WriteLine($"CreatePipe   -> CurrentState {created.CurrentState} (real AWS: CREATING)");

    // Does the pipe actually move a message from the source queue to the target queue?
    await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = sourceUrl, MessageBody = """{ "orderId": 1001 }""" });
    Stopwatch stopwatch = Stopwatch.StartNew();
    Message? delivered = null;
    while (delivered is null && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
    {
        ReceiveMessageResponse batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = targetUrl, WaitTimeSeconds = 5 });
        delivered = batch.Messages?.FirstOrDefault();
    }

    if (delivered is null)
    {
        Console.WriteLine("Send to source -> nothing reached the target within 30 s");
    }
    else
    {
        // The pipe forwards the whole SQS event record, as real Pipes does; the payload is its "body".
        using JsonDocument record = JsonDocument.Parse(delivered.Body);
        string body = record.RootElement.GetProperty("body").GetString() ?? "?";
        string source = record.RootElement.GetProperty("eventSource").GetString() ?? "?";
        Console.WriteLine($"Send to source -> reached the target after {stopwatch.Elapsed.TotalSeconds:F1} s, as an {source} record with body {body}");
    }

    StopPipeResponse stopped = await pipes.StopPipeAsync(new StopPipeRequest { Name = pipeName });
    Console.WriteLine($"StopPipe     -> CurrentState {stopped.CurrentState} (real AWS: STOPPING, then STOPPED)");

    StartPipeResponse started = await pipes.StartPipeAsync(new StartPipeRequest { Name = pipeName });
    Console.WriteLine($"StartPipe    -> CurrentState {started.CurrentState} (real AWS: STARTING, then RUNNING)");

    Console.WriteLine();
    bool synchronous = created.CurrentState == PipeState.RUNNING && stopped.CurrentState == PipeState.STOPPED;
    Console.WriteLine(synchronous
        ? "Every transition settled inside its own response. Code that runs create -> stop -> delete\nback to back is green here and gets ConflictException from real Pipes. Assert a set of\nstates (STOPPED or STOPPING), not one value, so the same check is honest against both."
        : "Transitions came back in progress, as real Pipes reports them.");
}
finally
{
    // Runs even when a step above throws.
    if (pipeCreated)
    {
        try
        {
            await pipes.DeletePipeAsync(new DeletePipeRequest { Name = pipeName });
        }
        catch (NotFoundException)
        {
            // CreatePipe never landed, so there is no pipe to remove.
        }
    }

    foreach (string? url in new[] { sourceUrl, targetUrl })
    {
        if (url is not null)
        {
            await sqs.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = url });
        }
    }

    Console.WriteLine();
    Console.WriteLine("Cleanup -> pipe and queues deleted");
}

async Task<string> QueueArnAsync(string url)
{
    GetQueueAttributesResponse attributes = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = url, AttributeNames = ["QueueArn"] });
    return attributes.Attributes["QueueArn"];
}
