# SQS long polling in .NET, and the empty receive that looks like success

> Send, receive and delete an SQS message from a single C# file using the official AWS SDK, then time an empty long poll to prove Floci really waits.

## What it shows

- SQS from .NET with **one official package** (`AWSSDK.SQS`) and no Floci-specific library.
- **An empty receive is not a success.** `ReceiveMessage` returns HTTP 200 whether or not a message came back. AWS SDK v4 leaves an absent `Messages` list `null`, not empty. A demo that goes green on the status code alone looks exactly the same when the message never arrived, so this lab treats "no message" as a failure and checks the body too.
- **Long polling is real on Floci.** With the queue empty, `WaitTimeSeconds = 5` blocks for the full 5 seconds. It doesn't return early. That's what you want before you rely on it to cut your empty-receive bill in production.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: SQS
- NuGet: `AWSSDK.SQS` 4.0.100.11, pinned in the `#:package` line at the top of `lab.cs`
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
CreateQueue          -> http://localhost:4566/000000000000/floci-labs-...
SendMessage          -> id 741bea69-...
ReceiveMessage       -> "hello at 2026-09-28T22:51:17..."
DeleteMessage        -> done

ReceiveMessage again -> 0 message(s) after 5.0 s
  Long polling is real: the empty receive waited out the full 5 s.

DeleteQueue          -> done
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**The queue URL says `localhost:4566` anyway.** Floci builds queue URLs from its own configured hostname, not from the address you called. That doesn't matter here, because the SDK sends every request to `ServiceURL`. If you hand the URL to something else, like a browser, a script or another container, check which host it names.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials (Floci parses SigV4 but doesn't verify it), and `MaxErrorRetry = 0`. The retry setting keeps a stopped emulator from taking ~8 seconds per call to fail. Delete those three lines and this is production code.

**Every run creates its own queue** and deletes it in a `finally`, so a crashed run leaves nothing behind.

## Try changing...

- Set `WaitTimeSeconds = 0` on the second receive (short polling) and compare the timing.
- Don't delete the message, then receive again with `VisibilityTimeout = 2` on the first receive. Does it come back after 2 seconds?
- Send 15 messages and receive with `MaxNumberOfMessages = 10`. How many do you get per call?
- Add a `MessageAttributes` entry on send and ask for it back with `MessageAttributeNames = ["All"]`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/floci](https://github.com/glensouza/floci)
