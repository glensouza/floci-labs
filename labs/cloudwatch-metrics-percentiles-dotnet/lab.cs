#:package AWSSDK.CloudWatch@4.0.104.2

// CloudWatch metrics against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.
// Delete those and this is the code you would run against real AWS.

using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

AmazonCloudWatchConfig config = new AmazonCloudWatchConfig
{
    ServiceURL = endpoint,
    AuthenticationRegion = "us-east-1",
    // The SDK default is 4 retries with backoff, so a stopped emulator takes ~8 s per call to
    // say so. Against real AWS you want the retries back.
    MaxErrorRetry = 0,
};

// Floci parses SigV4 but does not verify it, so any well-formed pair works.
using AmazonCloudWatchClient client = new AmazonCloudWatchClient(new BasicAWSCredentials("test", "test"), config);

// CloudWatch has no DeleteMetric: metrics simply age out. A fresh namespace per run keeps this
// run's datapoints from mixing with the last one's.
string ns = $"FlociLabs/{Guid.NewGuid():N}";
const string metricName = "RequestLatency";

// Start of the current minute, so all five datapoints land in one 60 s period.
DateTime minute = DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerMinute));
double[] values = [10, 20, 30, 40, 500];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine($"Namespace:      {ns}");
Console.WriteLine();

await client.PutMetricDataAsync(new PutMetricDataRequest
{
    Namespace = ns,
    MetricData = [.. values.Select(v => new MetricDatum { MetricName = metricName, Value = v, Unit = StandardUnit.Milliseconds, Timestamp = minute })],
});
Console.WriteLine($"PutMetricData -> {string.Join(", ", values)} ms");

// Real CloudWatch takes a while to aggregate a publish, so production code polls. Floci
// aggregates instantly, so this loop normally exits on its first pass here.
Datapoint? standard = null;
for (int attempt = 0; attempt < 30 && standard is null; attempt++)
{
    if (attempt > 0)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    GetMetricStatisticsResponse stats = await client.GetMetricStatisticsAsync(new GetMetricStatisticsRequest
    {
        Namespace = ns,
        MetricName = metricName,
        StartTime = minute.AddMinutes(-5),
        EndTime = minute.AddMinutes(5),
        Period = 60,
        Statistics = ["Average", "SampleCount", "Minimum", "Maximum"],
    });
    standard = stats.Datapoints?.FirstOrDefault();
}

if (standard is null)
{
    throw new InvalidOperationException("No datapoint came back within 60 s.");
}

Console.WriteLine($"Standard stats -> avg {standard.Average}, count {standard.SampleCount}, min {standard.Minimum}, max {standard.Maximum}");

// Now the same metric, asking for p99. Real CloudWatch puts the number in the datapoint's
// ExtendedStatistics dictionary.
GetMetricStatisticsResponse extended = await client.GetMetricStatisticsAsync(new GetMetricStatisticsRequest
{
    Namespace = ns,
    MetricName = metricName,
    StartTime = minute.AddMinutes(-5),
    EndTime = minute.AddMinutes(5),
    Period = 60,
    ExtendedStatistics = ["p99"],
});

List<Datapoint> p99Points = extended.Datapoints ?? [];
Console.WriteLine($"p99 request    -> {p99Points.Count} datapoint(s)");

foreach (Datapoint point in p99Points)
{
    string p99 = point.ExtendedStatistics is not null && point.ExtendedStatistics.TryGetValue("p99", out double value) ? value.ToString() : "(missing)";
    Console.WriteLine($"  timestamp {point.Timestamp:HH:mm}, unit {point.Unit}, p99 = {p99}");
}

Console.WriteLine();
bool anyMissing = p99Points.Count != 0 && p99Points.Any(p => p.ExtendedStatistics is null || !p.ExtendedStatistics.ContainsKey("p99"));
Console.WriteLine(anyMissing
    ? "A datapoint came back with no p99 in it. Code that checks \"did datapoints come back?\"\nbefore reading the value passes that check and then reads nothing."
    : p99Points.Count == 0
        ? "No datapoints for p99 at all."
        : "p99 came back with a value. This Floci build computes percentiles.");
