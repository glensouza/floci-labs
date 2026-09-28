# CloudWatch Logs in .NET, and the log event from 2001

> Write log events from a single C# file using the official AWS SDK, including one timestamped September 2001, and watch Floci accept an event real CloudWatch Logs would reject.

## What it shows

- CloudWatch Logs from .NET with **one official package** (`AWSSDK.CloudWatchLogs`) and no Floci-specific library: create a group and stream, put events, read them back.
- **The gotcha:** real CloudWatch Logs rejects events older than 14 days, or more than 2 hours in the future. It doesn't fail the call. It reports the rejected events in `RejectedLogEventsInfo` on the response. Floci stores the 2001 event and returns no `RejectedLogEventsInfo` at all. If your code batches events and handles rejections, that path never runs locally, and it will start running in production.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: CloudWatch Logs
- NuGet: `AWSSDK.CloudWatchLogs` 4.0.104.1, pinned in the `#:package` line at the top of `lab.cs`
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
CreateLogGroup + CreateLogStream -> done
PutLogEvents -> RejectedLogEventsInfo: none
GetLogEvents -> 2 event(s)
  2001-09-01 12:00:00  an event from September 2001
  2026-09-28 22:51:27  an event from right now

The 2001 event was stored. Real CloudWatch Logs rejects it, so any code that reads
RejectedLogEventsInfo never runs locally, and will start running in production.

DeleteLogGroup -> done
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials (Floci parses SigV4 but doesn't verify it), and `MaxErrorRetry = 0`. Delete those three lines and this is production code.

**The events in one `PutLogEvents` batch are in timestamp order.** Real CloudWatch Logs requires that, so the 2001 event goes first.

**The lab reports whichever result it gets.** If a later Floci release starts enforcing the time window, the last line changes to say so. It never claims a behaviour it didn't see.

## Try changing...

- Move the old event to 2 hours and 1 minute **in the future** instead. Real CloudWatch Logs reports it in `TooNewLogEventStartIndex`. What does Floci do?
- Call `FilterLogEvents` with `FilterPattern = "2001"` and check that filtering really filters.
- Call `PutRetentionPolicy` with 1 day and read it back with `DescribeLogGroups`.
- Try `PutMetricFilter` and look at what comes back.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
