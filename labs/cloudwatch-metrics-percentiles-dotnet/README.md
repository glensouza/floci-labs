# CloudWatch metrics in .NET, and the p99 with no number in it

> Publish five latency datapoints from a single C# file using the official AWS SDK, read back correct averages, then ask for p99 and get a datapoint with no value inside it.

## What it shows

- CloudWatch metrics from .NET with **one official package** (`AWSSDK.CloudWatch`) and no Floci-specific library.
- **The standard statistics are right.** Five values (10, 20, 30, 40, 500 ms) come back as average 120, sample count 5, min 10 and max 500, aggregated correctly.
- **The gotcha:** ask for `p99` through `ExtendedStatistics` and Floci doesn't return an error or an empty list. It returns **a datapoint** with a timestamp and a unit, and **no p99 value** in it. If your code checks "did any datapoints come back?" before reading the value, and that's the obvious way to write it, the check passes and you read nothing.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: CloudWatch (metrics)
- NuGet: `AWSSDK.CloudWatch` 4.0.104.2, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.1.0 (`floci/floci:latest`, September 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then:

```bash
dotnet run lab.cs
```

Expected output:

```text
PutMetricData -> 10, 20, 30, 40, 500 ms
Standard stats -> avg 120, count 5, min 10, max 500
p99 request    -> 1 datapoint(s)
  timestamp 22:51, unit Milliseconds, p99 = (missing)

A datapoint came back with no p99 in it. Code that checks "did datapoints come back?"
before reading the value passes that check and then reads nothing.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials (Floci parses SigV4 but doesn't verify it), and `MaxErrorRetry = 0`. Delete those three lines and this is production code.

**The read-back polls.** Real CloudWatch takes a while to aggregate a publish, so production code needs a retry loop. Floci aggregates instantly, so the loop exits on its first pass locally. Your waiting path never gets exercised here, which is worth knowing too.

**There is no cleanup step.** CloudWatch has no `DeleteMetric`, and metrics simply age out. Each run publishes under a fresh namespace (`FlociLabs/<guid>`), so runs never mix.

## Try changing...

- Ask for `GetMetricData` with a `MetricStat` using `Stat = "p99"`. Is the batch API any different?
- Add a `Dimensions` entry on publish, then query without it. Real CloudWatch treats those as different metrics.
- Create an alarm with `PutMetricAlarm` (threshold 100, `GreaterThanThreshold`) and poll `DescribeAlarms`. Floci evaluates alarms, so it should move to `ALARM` within a minute or two.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
