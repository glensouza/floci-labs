#:package AWSSDK.SQS@4.0.100.11

// SQS against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.
// Delete those and this is the code you would run against real AWS.

using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

AmazonSQSConfig config = new AmazonSQSConfig
{
    ServiceURL = endpoint,
    AuthenticationRegion = "us-east-1",
    // The SDK default is 4 retries with backoff, so a stopped emulator takes ~8 s per call to
    // say so. Against real AWS you want the retries back.
    MaxErrorRetry = 0,
};

// Floci parses SigV4 but does not verify it, so any well-formed pair works.
using AmazonSQSClient client = new AmazonSQSClient(new BasicAWSCredentials("test", "test"), config);

// A fresh queue per run, so a crashed run never breaks the next one.
string queueName = $"floci-labs-{Guid.NewGuid():N}";
string? queueUrl = null;
string body = $"hello at {DateTimeOffset.UtcNow:O}";

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    CreateQueueResponse created = await client.CreateQueueAsync(new CreateQueueRequest { QueueName = queueName });
    queueUrl = created.QueueUrl;
    Console.WriteLine($"CreateQueue          -> {queueUrl}");

    SendMessageResponse sent = await client.SendMessageAsync(new SendMessageRequest { QueueUrl = queueUrl, MessageBody = body });
    Console.WriteLine($"SendMessage          -> id {sent.MessageId}");

    // Long polling: wait up to 5 s for a message instead of returning empty straight away.
    ReceiveMessageResponse received = await client.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, WaitTimeSeconds = 5, MaxNumberOfMessages = 1 });

    // AWS SDK v4 leaves an absent collection null rather than empty, so an empty receive is
    // Messages == null. Treat it as the failure it is: a receive that got nothing is not a
    // successful step, whatever the HTTP status says.
    Message? message = received.Messages?.FirstOrDefault();
    if (message is null)
    {
        throw new InvalidOperationException("ReceiveMessage returned no message. The send did not round-trip.");
    }

    if (message.Body != body)
    {
        throw new InvalidOperationException($"Received \"{message.Body}\", not the body that was sent.");
    }

    Console.WriteLine($"ReceiveMessage       -> \"{message.Body}\"");

    await client.DeleteMessageAsync(new DeleteMessageRequest { QueueUrl = queueUrl, ReceiptHandle = message.ReceiptHandle });
    Console.WriteLine("DeleteMessage        -> done");
    Console.WriteLine();

    // The queue is now empty. A long poll should block for the full wait, not return early.
    Stopwatch stopwatch = Stopwatch.StartNew();
    ReceiveMessageResponse empty = await client.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, WaitTimeSeconds = 5 });
    stopwatch.Stop();

    Console.WriteLine($"ReceiveMessage again -> {empty.Messages?.Count ?? 0} message(s) after {stopwatch.Elapsed.TotalSeconds:F1} s");
    Console.WriteLine(stopwatch.Elapsed.TotalSeconds >= 4.5
        ? "  Long polling is real: the empty receive waited out the full 5 s."
        : "  Returned early: this build does not implement the long-poll wait.");
}
finally
{
    // Runs even when a step above throws, so no queue is left behind.
    if (queueUrl is not null)
    {
        await client.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = queueUrl });
        Console.WriteLine();
        Console.WriteLine("DeleteQueue          -> done");
    }
}
