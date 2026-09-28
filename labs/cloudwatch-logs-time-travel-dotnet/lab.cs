#:package AWSSDK.CloudWatchLogs@4.0.104.1

// CloudWatch Logs against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.
// Delete those and this is the code you would run against real AWS.

using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

AmazonCloudWatchLogsConfig config = new AmazonCloudWatchLogsConfig
{
    ServiceURL = endpoint,
    AuthenticationRegion = "us-east-1",
    // The SDK default is 4 retries with backoff, so a stopped emulator takes ~8 s per call to
    // say so. Against real AWS you want the retries back.
    MaxErrorRetry = 0,
};

// Floci parses SigV4 but does not verify it, so any well-formed pair works.
using AmazonCloudWatchLogsClient client = new AmazonCloudWatchLogsClient(new BasicAWSCredentials("test", "test"), config);

// A fresh log group per run, so a crashed run never breaks the next one.
string group = $"/floci-labs/{Guid.NewGuid():N}";
const string stream = "app";
bool groupCreated = false;

DateTime now = DateTime.UtcNow;
DateTime longAgo = new DateTime(2001, 9, 1, 12, 0, 0, DateTimeKind.Utc);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine($"Log group:      {group}");
Console.WriteLine();

try
{
    groupCreated = true;
    await client.CreateLogGroupAsync(new CreateLogGroupRequest { LogGroupName = group });
    await client.CreateLogStreamAsync(new CreateLogStreamRequest { LogGroupName = group, LogStreamName = stream });
    Console.WriteLine("CreateLogGroup + CreateLogStream -> done");

    // Real CloudWatch Logs refuses events older than 14 days (or more than 2 h in the future) and
    // reports them in RejectedLogEventsInfo rather than failing the call. Events in a batch must
    // be in timestamp order.
    PutLogEventsResponse put = await client.PutLogEventsAsync(new PutLogEventsRequest
    {
        LogGroupName = group,
        LogStreamName = stream,
        LogEvents =
        [
            new InputLogEvent { Timestamp = longAgo, Message = "an event from September 2001" },
            new InputLogEvent { Timestamp = now, Message = "an event from right now" },
        ],
    });

    RejectedLogEventsInfo? rejected = put.RejectedLogEventsInfo;
    Console.WriteLine($"PutLogEvents -> RejectedLogEventsInfo: {(rejected is null ? "none" : $"too old up to index {rejected.TooOldLogEventEndIndex}")}");

    GetLogEventsResponse read = await client.GetLogEventsAsync(new GetLogEventsRequest { LogGroupName = group, LogStreamName = stream, StartFromHead = true });
    List<OutputLogEvent> events = read.Events ?? [];

    Console.WriteLine($"GetLogEvents -> {events.Count} event(s)");
    foreach (OutputLogEvent e in events)
    {
        Console.WriteLine($"  {e.Timestamp:yyyy-MM-dd HH:mm:ss}  {e.Message}");
    }

    Console.WriteLine();
    bool keptOldEvent = events.Any(e => e.Timestamp?.Year == 2001);
    Console.WriteLine(keptOldEvent
        ? "The 2001 event was stored. Real CloudWatch Logs rejects it, so any code that reads\nRejectedLogEventsInfo never runs locally, and will start running in production."
        : "The 2001 event was rejected, as real CloudWatch Logs does. This Floci build enforces the time window.");
}
finally
{
    // Runs even when a step above throws, so no log group is left behind.
    if (groupCreated)
    {
        try
        {
            await client.DeleteLogGroupAsync(new DeleteLogGroupRequest { LogGroupName = group });
            Console.WriteLine();
            Console.WriteLine("DeleteLogGroup -> done");
        }
        catch (ResourceNotFoundException)
        {
            // CreateLogGroup never landed, so there is nothing to remove.
        }
    }
}
