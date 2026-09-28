# EventBridge routing in .NET, and the FailedEntryCount that lies in SDK v4

> Route events from a custom bus to an SQS queue with an event pattern from a single C# file, and handle the nullable `FailedEntryCount` that turns a successful call into a failure.

## What it shows

- EventBridge from .NET using the official `AWSSDK.EventBridge` and `AWSSDK.SQS` packages, and no Floci-specific library. The queue is the target, so you can read back what was routed.
- **The pattern really routes.** `OrderPlaced` reaches the queue and `OrderCancelled` doesn't. Both come from the same `PutEvents` call.
- **The SDK v4 gotcha:** `FailedEntryCount` on `PutEvents` and `PutTargets` is now an `int?`. The obvious check, `response.FailedEntryCount != 0`, is `true` when the field is **absent**, so a fully successful call reads as a failure. Coalesce first: `(response.FailedEntryCount ?? 0) != 0`. Every AWS batch API has this shape (`SendMessageBatch`, `PublishBatch`, `BatchWriteItem`...).

Floci always sends the field (the lab prints `on the wire: 0`), so **a test suite against Floci can't catch this bug.** It only appears against a server that leaves a zero out. Code review has to catch it, and this lab shows the check that's safe either way.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: EventBridge, SQS
- NuGet: `AWSSDK.EventBridge` 4.0.100.12 and `AWSSDK.SQS` 4.0.100.11, pinned in the `#:package` lines at the top of `lab.cs`
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
Rule   -> { "source": ["floci.labs"], "detail-type": ["OrderPlaced"] }
Target -> arn:aws:sqs:us-east-1:000000000000:order-placed-... (FailedEntryCount: 0)

PutEvents OrderPlaced + OrderCancelled -> FailedEntryCount on the wire: 0, coalesced: 0
Queue received 1 event(s):
  OrderPlaced {"orderId":1001}

The pattern routed OrderPlaced to the queue and dropped OrderCancelled.

Cleanup -> target, rule, bus and queue deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines per client know about Floci:** `ServiceURL`, the `test`/`test` credentials (Floci parses SigV4 but doesn't verify it), and `MaxErrorRetry = 0`, which makes a stopped emulator fail fast.

**A custom bus per run** keeps the lab's rule off your `default` bus, and the unique names let you rerun it.

**Cleanup order matters.** A rule can't be deleted while it still has targets, and a bus can't be deleted while it still has rules. `DeleteRule` and `DeleteEventBus` return success even for things that don't exist, as real EventBridge documents, so they can't tell you whether they removed anything. The lab tracks what it created instead.

**One thing real AWS needs that Floci doesn't check:** a queue policy that allows `events.amazonaws.com` to send to the queue.

## Try changing...

- Match on the event body: `{ "detail": { "orderId": [{ "numeric": [">", 1000] }] } }`.
- Add an `InputTransformer` to the target so the queue receives only the `detail`.
- Call `RemoveTargets` with an ID that doesn't exist and print `FailedEntryCount`. It returns HTTP 200 either way, so the count is your only signal.
- **Route events on a different cloud.** Floci also emulates Azure (`floci/floci-az`, port 4577) and GCP (`floci/floci-gcp`, port 4588). The nearest equivalents are Service Bus topics with subscription rules and Pub/Sub subscriptions with filters. FlociLab has working .NET samples to start from: [Azure Service Bus](https://github.com/glensouza/flocilab/tree/main/samples/azure/servicebus) and [GCP Pub/Sub](https://github.com/glensouza/flocilab/tree/main/samples/gcp/pubsub).

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
