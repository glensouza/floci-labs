#:package AWSSDK.EventBridge@4.0.100.12
#:package AWSSDK.SQS@4.0.100.11

// EventBridge routing to SQS against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.
// Delete those and this is the code you would run against real AWS.

using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonEventBridgeClient events = new AmazonEventBridgeClient(credentials, new AmazonEventBridgeConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
using AmazonSQSClient sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Fresh names per run, so a crashed run never breaks the next one.
string suffix = Guid.NewGuid().ToString("N")[..12];
string busName = $"orders-{suffix}";
string ruleName = $"order-placed-{suffix}";
const string targetId = "placed-queue";
bool busCreated = false;
bool ruleCreated = false;
string? queueUrl = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    busCreated = true;
    await events.CreateEventBusAsync(new CreateEventBusRequest { Name = busName });
    queueUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"order-placed-{suffix}" })).QueueUrl;
    string queueArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn"] })).Attributes["QueueArn"];

    // Only OrderPlaced events from our source match. (Real AWS also needs a queue policy that
    // lets events.amazonaws.com send to the queue.)
    const string pattern = """{ "source": ["floci.labs"], "detail-type": ["OrderPlaced"] }""";
    ruleCreated = true;
    await events.PutRuleAsync(new PutRuleRequest { Name = ruleName, EventBusName = busName, EventPattern = pattern });
    PutTargetsResponse targets = await events.PutTargetsAsync(new PutTargetsRequest { Rule = ruleName, EventBusName = busName, Targets = [new Target { Id = targetId, Arn = queueArn }] });
    Console.WriteLine($"Rule   -> {pattern}");
    Console.WriteLine($"Target -> {queueArn} (FailedEntryCount: {Show(targets.FailedEntryCount)})");
    Console.WriteLine();

    PutEventsResponse put = await events.PutEventsAsync(new PutEventsRequest
    {
        Entries =
        [
            new PutEventsRequestEntry { EventBusName = busName, Source = "floci.labs", DetailType = "OrderPlaced", Detail = """{ "orderId": 1001 }""" },
            new PutEventsRequestEntry { EventBusName = busName, Source = "floci.labs", DetailType = "OrderCancelled", Detail = """{ "orderId": 1002 }""" },
        ],
    });

    // AWS SDK v4 made FailedEntryCount an int?, so `put.FailedEntryCount != 0` is TRUE when the
    // field is absent from the response, and a fully successful call reads as a failure. Coalesce
    // first. Every batch API has this shape: SendMessageBatch, PublishBatch, BatchWriteItem...
    int failed = put.FailedEntryCount ?? 0;
    Console.WriteLine($"PutEvents OrderPlaced + OrderCancelled -> FailedEntryCount on the wire: {Show(put.FailedEntryCount)}, coalesced: {failed}");

    List<Message> received = [];
    while (true)
    {
        ReceiveMessageResponse batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, WaitTimeSeconds = 3, MaxNumberOfMessages = 10 });
        if (batch.Messages is null || batch.Messages.Count == 0)
        {
            break;
        }

        received.AddRange(batch.Messages);
    }

    Console.WriteLine($"Queue received {received.Count} event(s):");
    foreach (Message message in received)
    {
        using JsonDocument evt = JsonDocument.Parse(message.Body);
        Console.WriteLine($"  {evt.RootElement.GetProperty("detail-type").GetString()} {evt.RootElement.GetProperty("detail").GetRawText()}");
    }

    Console.WriteLine();
    bool onlyPlaced = received.Count == 1 && received[0].Body.Contains("OrderPlaced", StringComparison.Ordinal);
    Console.WriteLine(onlyPlaced
        ? "The pattern routed OrderPlaced to the queue and dropped OrderCancelled."
        : "Unexpected: the queue should hold exactly the OrderPlaced event.");
}
finally
{
    // Runs even when a step above throws. A rule can't be deleted while it has targets, and a
    // bus can't be deleted while it has rules, so the order matters. DeleteRule and
    // DeleteEventBus succeed on things that don't exist, so they can't tell you they removed
    // anything; the flags are what know what was created.
    if (ruleCreated)
    {
        await events.RemoveTargetsAsync(new RemoveTargetsRequest { Rule = ruleName, EventBusName = busName, Ids = [targetId] });
        await events.DeleteRuleAsync(new DeleteRuleRequest { Name = ruleName, EventBusName = busName });
    }

    if (busCreated)
    {
        await events.DeleteEventBusAsync(new DeleteEventBusRequest { Name = busName });
    }

    if (queueUrl is not null)
    {
        await sqs.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = queueUrl });
    }

    Console.WriteLine();
    Console.WriteLine("Cleanup -> target, rule, bus and queue deleted");
}

static string Show(int? value) => value is null ? "(absent)" : value.Value.ToString();
