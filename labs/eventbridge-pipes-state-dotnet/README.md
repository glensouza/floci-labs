# EventBridge Pipes in .NET, and the state machine the emulator skips

> Pipe one SQS queue into another from a single C# file, watch a message cross in about a second, and see every state transition settle inside its own response, which real Pipes never does.

## What it shows

- EventBridge Pipes from .NET using the official `AWSSDK.Pipes` and `AWSSDK.SQS` packages, and no Floci-specific library. SQS is both the source and the target, so you can see the pipe move a message.
- **The pipe really moves data.** A message sent to the source queue reaches the target in about a second. It arrives as the whole SQS event record, with your payload in its `body` field, which is also what real Pipes delivers.
- **The gotcha:** Floci settles pipe state **synchronously**. `CreatePipe` returns `RUNNING`, and `StopPipe` returns `STOPPED`. Real Pipes returns `CREATING`, then `STARTING`, then `RUNNING`, over seconds. It also rejects a stop or delete mid-transition with `ConflictException`. Code that runs create, stop, delete back to back is green here and fails against AWS.

The fix isn't a poll loop that never loops locally. Assert a **set** of states that is true of both, such as `STOPPED` or `STOPPING`, and rule out only the reading that is a lie either way: a 200 that still says `RUNNING` after you asked it to stop.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: EventBridge Pipes, SQS
- NuGet: `AWSSDK.Pipes` 4.0.100.12 and `AWSSDK.SQS` 4.0.100.11, pinned in the `#:package` lines at the top of `lab.cs`
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
CreatePipe   -> CurrentState RUNNING (real AWS: CREATING)
Send to source -> reached the target after 1.0 s, as an aws:sqs record with body { "orderId": 1001 }
StopPipe     -> CurrentState STOPPED (real AWS: STOPPING, then STOPPED)
StartPipe    -> CurrentState RUNNING (real AWS: STARTING, then RUNNING)

Every transition settled inside its own response. Code that runs create -> stop -> delete
back to back is green here and gets ConflictException from real Pipes. ...

Cleanup -> pipe and queues deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**The role ARN is made up, and Floci doesn't mind.** Real `CreatePipe` resolves the source and assumes the role when the pipe is created. Against AWS, the role has to exist, trust `pipes.amazonaws.com`, and be allowed to read the source and write the target. Otherwise this lab fails on its first call.

**Only three lines per client know about Floci:** `ServiceURL`, the `test`/`test` credentials, and `MaxErrorRetry = 0`, which makes a stopped emulator fail fast.

## Try changing...

- Add a `SourceParameters.FilterCriteria` pattern so that only some messages cross the pipe.
- Add `TargetParameters.InputTemplate` to reshape the record so the target gets just the body.
- Call `DeletePipe` straight after `CreatePipe`. That's fine here; against real Pipes, expect a `ConflictException`.
- **Wire a queue to a subscriber on a different cloud.** Floci also emulates Azure (`floci/floci-az`, port 4577), GCP (`floci/floci-gcp`, port 4588) and OCI (`floci/floci-oci`, port 4599). Pipes has no direct equivalent there, but every one of them has a queue to be the source. FlociLab has working .NET samples to start from: [Azure Service Bus](https://github.com/glensouza/flocilab/tree/main/samples/azure/servicebus), [GCP Pub/Sub](https://github.com/glensouza/flocilab/tree/main/samples/gcp/pubsub) and [OCI Queue](https://github.com/glensouza/flocilab/tree/main/samples/oci/queue).

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
