#:package AWSSDK.SimpleNotificationService@4.0.100.11
#:package AWSSDK.SQS@4.0.100.11

// SNS fan-out to SQS against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.
// Delete those and this is the code you would run against real AWS.

using System.Text.Json;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using SnsMessageAttribute = Amazon.SimpleNotificationService.Model.MessageAttributeValue;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonSimpleNotificationServiceClient sns = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
using AmazonSQSClient sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Fresh names per run, so a crashed run never breaks the next one.
string suffix = Guid.NewGuid().ToString("N")[..12];
string? topicArn = null;
string? queueUrl = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    topicArn = (await sns.CreateTopicAsync(new CreateTopicRequest { Name = $"orders-{suffix}" })).TopicArn;
    queueUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"blue-orders-{suffix}" })).QueueUrl;
    GetQueueAttributesResponse attributes = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn"] });
    string queueArn = attributes.Attributes["QueueArn"];
    Console.WriteLine($"CreateTopic + CreateQueue -> {topicArn}");

    // The queue only wants blue orders. The filter policy runs on SNS's side: a red order is
    // never delivered to this queue at all. (Real AWS also needs a queue policy that lets SNS
    // send to the queue; Floci doesn't check it.)
    const string filterPolicy = """{ "color": ["blue"] }""";
    await sns.SubscribeAsync(new SubscribeRequest
    {
        TopicArn = topicArn,
        Protocol = "sqs",
        Endpoint = queueArn,
        Attributes = new Dictionary<string, string> { ["FilterPolicy"] = filterPolicy },
    });
    Console.WriteLine($"Subscribe (sqs)           -> filter policy {filterPolicy}");
    Console.WriteLine();

    string blueId = await PublishAsync("blue", "order 1001");
    string redId = await PublishAsync("red", "order 1002");
    Console.WriteLine($"Publish color=blue        -> {blueId}");
    Console.WriteLine($"Publish color=red         -> {redId}");
    Console.WriteLine();

    // Drain the queue with long polling until a receive comes back empty.
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

    Console.WriteLine($"Queue received {received.Count} message(s):");
    foreach (Message message in received)
    {
        // Without RawMessageDelivery, SNS wraps the payload in its notification envelope.
        using JsonDocument envelope = JsonDocument.Parse(message.Body);
        string type = envelope.RootElement.GetProperty("Type").GetString() ?? "?";
        string id = envelope.RootElement.GetProperty("MessageId").GetString() ?? "?";
        string text = envelope.RootElement.GetProperty("Message").GetString() ?? "?";
        string matches = id == blueId ? "the blue publish" : id == redId ? "the RED publish" : "an unknown publish";
        Console.WriteLine($"  {type}: \"{text}\", MessageId matches {matches}");
    }

    Console.WriteLine();
    bool onlyBlue = received.Count == 1 && received[0].Body.Contains(blueId, StringComparison.Ordinal);
    Console.WriteLine(onlyBlue
        ? "Fan-out works and the filter policy is enforced: blue arrived, red was dropped by SNS."
        : "Unexpected: the queue should hold exactly the blue order.");
}
finally
{
    // Runs even when a step above throws. Deleting the topic removes its subscriptions too.
    if (topicArn is not null)
    {
        await sns.DeleteTopicAsync(new DeleteTopicRequest { TopicArn = topicArn });
    }

    if (queueUrl is not null)
    {
        await sqs.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = queueUrl });
    }

    Console.WriteLine();
    Console.WriteLine("Cleanup -> topic and queue deleted");
}

async Task<string> PublishAsync(string color, string text)
{
    PublishResponse response = await sns.PublishAsync(new PublishRequest
    {
        TopicArn = topicArn,
        Message = text,
        MessageAttributes = new Dictionary<string, SnsMessageAttribute> { ["color"] = new SnsMessageAttribute { DataType = "String", StringValue = color } },
    });

    return response.MessageId;
}
