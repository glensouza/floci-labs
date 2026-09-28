# EventBridge Scheduler in .NET: a schedule that really fires, and a cron typo that sails through

> Create a one-minute schedule targeting an SQS queue from a single C# file and wait for Floci to actually fire it. Then create a schedule whose expression is `not-a-rate` and watch it get accepted.

## What it shows

- EventBridge Scheduler from .NET using the official `AWSSDK.Scheduler` and `AWSSDK.SQS` packages, and no Floci-specific library. The queue is the target, so you can see the schedule fire.
- **Floci fires schedules for real.** A `rate(1 minute)` schedule delivers its `Input` payload to the queue within about a minute. That also means a leftover schedule isn't inert: it keeps invoking its target on a timer. The cleanup in `finally` matters.
- **It fires with a role that doesn't exist.** Floci performs no assume-role check. Real Scheduler validates the execution role at create time and rejects one it can't assume.
- **The quiet one:** Floci accepts `not-a-rate` as a schedule expression, and `GetSchedule` reads it straight back. Real Scheduler answers `ValidationException`. A typo in a cron expression works perfectly on your laptop and fails at deploy, which is exactly the kind of bug you run an emulator to catch. Test your scheduling code here, but keep a real deployment in the loop for the expression itself.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: EventBridge Scheduler, SQS
- NuGet: `AWSSDK.Scheduler` 4.0.100.12 and `AWSSDK.SQS` 4.0.100.11, pinned in the `#:package` lines at the top of `lab.cs`
- Last verified against: Floci 2.1.0 (`floci/floci:latest`, September 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then run it, and give it about a minute:

```bash
dotnet run lab.cs
```

Expected output:

```text
CreateSchedule rate(1 minute) -> SQS queue; waiting for it to fire (up to 90 s)...
  Fired after 66 s with payload { "job": "nightly-report" }
  (It fired with a role ARN that does not exist. Real Scheduler rejects that at create time.)

CreateSchedule "not-a-rate" -> accepted; GetSchedule reads back "not-a-rate"
  Real Scheduler answers ValidationException. ...

Cleanup -> schedules and queue deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**`FlexibleTimeWindow` is required.** Leave it out and Floci rejects the call, as AWS does. `OFF` means fire on the minute rather than somewhere in a window.

**The first firing time isn't fixed.** It depends on where in the minute you created the schedule, so "about a minute" can mean anything from a few seconds to just over a minute. The lab long-polls the queue for up to 90 seconds.

**Only three lines per client know about Floci:** `ServiceURL`, the `test`/`test` credentials, and `MaxErrorRetry = 0`, plus the made-up role ARN. Against AWS, the role has to trust `scheduler.amazonaws.com` and be allowed to `sqs:SendMessage` to the queue.

## Try changing...

- Use a cron expression: `cron(* * * * ? *)`. Then try a broken one, such as `cron(61 * * * ? *)`.
- Make it a one-shot: `at(2026-12-31T23:59:00)`, with a time a minute or two in the future.
- Set `ActionAfterCompletion = DELETE` on a one-shot schedule and check `ListSchedules` after it fires.
- Comment out the `finally` block, run the lab, and count the messages in the queue a few minutes later. (Then clean up by hand.)

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
