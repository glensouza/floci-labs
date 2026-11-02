# EventBridge Scheduler in .NET: a schedule that fires with a role that doesn't exist

> Create a `rate(1 minute)` schedule targeting an SQS queue from a single C# file and wait for Floci to actually fire it. The execution role in the target is a made-up ARN, and the schedule fires anyway: Floci performs no assume-role check, where real Scheduler rejects a role it can't assume at create time. Floci 2.4.0 does refuse a schedule expression that isn't one, `not-a-rate`, with `ValidationException`.

## What it shows

- EventBridge Scheduler from .NET using the official `AWSSDK.Scheduler` and `AWSSDK.SQS` packages, and no Floci-specific library. The queue is the target, so you can see the schedule fire.
- **Floci fires schedules for real.** A `rate(1 minute)` schedule delivered its `Input` payload to the queue within 9 and 10 seconds on the two runs below.
- **It fires with a role that doesn't exist.** The lab's role ARN is made up, and the message arrived. Real Scheduler validates the execution role at create time and rejects one it can't assume.
- **A bad expression is refused.** `not-a-rate` answers `ValidationException` ("Invalid ScheduleExpression: Unsupported schedule expression: not-a-rate"), as real Scheduler does. An earlier version of this lab, run on Floci 2.1.0, saw it accepted and read straight back by `GetSchedule`.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: EventBridge Scheduler, SQS
- NuGet: `AWSSDK.Scheduler` 4.0.100.12 and `AWSSDK.SQS` 4.0.100.11, pinned in the `#:package` lines at the top of `lab.cs`
- Last verified against: Floci 2.4.0 (`floci/floci:latest`, November 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then run it. It waits for the schedule to fire, for up to 90 seconds:

```bash
dotnet run lab.cs
```

Expected output (the time to fire differs per run):

```text
CreateSchedule rate(1 minute) -> SQS queue; waiting for it to fire (up to 90 s)...
  Fired after 10 s with payload { "job": "nightly-report" }
  (It fired with a role ARN that does not exist. Real Scheduler rejects that at create time.)

CreateSchedule "not-a-rate" -> ValidationException, as real Scheduler does: Invalid ScheduleExpression: Unsupported schedule expression: not-a-rate

Cleanup -> schedules and queue deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines per client know about Floci:** `ServiceURL`, the `test`/`test` credentials, and `MaxErrorRetry = 0`, plus the made-up role ARN. Against AWS, the role has to trust `scheduler.amazonaws.com` and be allowed to `sqs:SendMessage` to the queue.

**Cleanup is in a `finally`.** The schedules and the queue are deleted whether or not the schedule fired.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
