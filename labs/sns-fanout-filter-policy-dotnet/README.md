# SNS fan-out to SQS in .NET, with a filter policy that really filters

> Subscribe an SQS queue to an SNS topic with a filter policy, publish a blue order and a red order from a single C# file, and see only the blue one arrive.

## What it shows

- SNS to SQS fan-out from .NET using the official `AWSSDK.SimpleNotificationService` and `AWSSDK.SQS` packages, and no Floci-specific library. A subscriber is the only way to prove a publish went anywhere, and an SQS queue is the easiest subscriber to read back, which is why this lab needs two packages.
- **Filter policies are enforced on SNS's side.** The subscription asks for `{ "color": ["blue"] }`. The red publish is dropped before it ever reaches the queue.
- **The envelope is the real one.** Without `RawMessageDelivery`, the queue receives SNS's JSON notification (`"Type": "Notification"`), and its `MessageId` matches the ID that `Publish` returned. That's how you tie a delivery back to its publish.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: SNS, SQS
- NuGet: `AWSSDK.SimpleNotificationService` 4.0.100.11 and `AWSSDK.SQS` 4.0.100.11, pinned in the `#:package` lines at the top of `lab.cs`
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
CreateTopic + CreateQueue -> arn:aws:sns:us-east-1:000000000000:orders-...
Subscribe (sqs)           -> filter policy { "color": ["blue"] }

Publish color=blue        -> 92d01b67-...
Publish color=red         -> 58dd051b-...

Queue received 1 message(s):
  Notification: "order 1001", MessageId matches the blue publish

Fan-out works and the filter policy is enforced: blue arrived, red was dropped by SNS.

Cleanup -> topic and queue deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines per client know about Floci:** `ServiceURL`, the `test`/`test` credentials (Floci parses SigV4 but doesn't verify it), and `MaxErrorRetry = 0`, which makes a stopped emulator fail fast.

**One thing real AWS needs that Floci doesn't check:** a queue policy that allows `sns.amazonaws.com` to `sqs:SendMessage` to the queue. Without it, real SNS accepts the publish and silently delivers nothing. Add the policy before you point this at AWS.

**The filter policy matches on message attributes.** The lab sends `color` as an SNS message attribute rather than in the body. Filtering on the body needs `FilterPolicyScope = MessageBody`.

**SNS is a query-protocol service, and SQS isn't.** SNS requests are form-encoded with XML responses, and SQS speaks JSON. The SDK hides this, but if you ever read the wire traffic, don't assume two AWS services share a protocol.

## Try changing...

- Set `RawMessageDelivery` to `true` on the subscription. The queue gets `order 1001` directly, with no envelope.
- Change the filter to `{ "color": [{ "anything-but": "red" }] }` and add a green publish.
- Subscribe a second queue with no filter policy, and check that it gets both orders.
- **Fan out on a different cloud.** Floci also emulates Azure (`floci/floci-az`, port 4577) and GCP (`floci/floci-gcp`, port 4588). The closest match to a topic with filtered subscriptions is Azure Service Bus topics with subscription rules, or GCP Pub/Sub subscriptions with filters. FlociLab has working .NET samples to start from: [Azure Service Bus](https://github.com/glensouza/flocilab/tree/main/samples/azure/servicebus) and [GCP Pub/Sub](https://github.com/glensouza/flocilab/tree/main/samples/gcp/pubsub).

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
