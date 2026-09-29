# Global Accelerator in .NET: the deployment wait Floci never makes you do

> Create an AWS Global Accelerator with a listener and an endpoint group from a single C# file. On AWS, every change leaves the accelerator `IN_PROGRESS` for minutes, and the next change is refused until it settles, so real code waits after every call. Floci reports `DEPLOYED` straight away, every time, so a wait loop that AWS depends on never runs here.

## What it shows

- Global Accelerator from .NET with the official `AWSSDK.GlobalAccelerator` package, and no Floci-specific library.
- **Five changes, no waiting.** The lab creates an accelerator, a TCP listener and an endpoint group, updates the endpoint group's traffic dial and disables the accelerator, back to back.
- **What the very next call sees.** After each change it reads the accelerator's status once, with no polling, and prints it. On Floci 2.1.0 that is `DEPLOYED` every time, and every change is accepted.
- **On AWS it isn't.** Each of those changes leaves the accelerator `IN_PROGRESS`, and a change sent before it is `DEPLOYED` again is refused with `TransactionInProgressException`. The lab says which way it went, so it tells you if Floci ever starts simulating the delay.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Global Accelerator
- NuGet: `AWSSDK.GlobalAccelerator` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the name suffix differs on every run):

```text
CreateAccelerator         -> status DEPLOYED
CreateListener            -> status DEPLOYED
CreateEndpointGroup       -> status DEPLOYED
UpdateEndpointGroup       -> status DEPLOYED
UpdateAccelerator disable -> status DEPLOYED

Floci reported DEPLOYED straight after all 5 changes, and accepted each one without
  waiting. On AWS each of these leaves the accelerator IN_PROGRESS for minutes, and a change sent
  before it settles is refused with TransactionInProgressException. A wait loop in your code never
  waits on Floci: test it against AWS, or with a fake that returns IN_PROGRESS.

Cleanup -> deleted lab-ga-6a9e7dde4869
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The region is `us-west-2` because that is the only region Global Accelerator's control plane lives in, on AWS too. The endpoint in the endpoint group is a load balancer ARN with nothing behind it; Floci accepts it, and on AWS you would use a real ALB, NLB, EC2 instance or Elastic IP.

**Cleanup is bottom-up.** Global Accelerator refuses to delete a listener that still has an endpoint group, or an accelerator that is enabled or still has listeners, and Floci enforces that order just as AWS does. So the `finally` removes endpoint groups, then listeners, then disables and deletes the accelerator.

**Why this matters.** Code that manages accelerators has to wait for `DEPLOYED` after every change, including cleanup code. Forget one wait and it works perfectly against Floci, then fails partway through on AWS, which for a cleanup path means a leaked accelerator that bills by the hour. A green test run against Floci can't tell you the wait is there. The [FlociLab sample](https://github.com/glensouza/flocilab/tree/main/samples/aws/globalaccelerator) had exactly this bug until code review caught it.

## Try changing...

- Send `UpdateAccelerator` twice in a row with different names, and see whether Floci ever refuses the second.
- Replay `CreateAccelerator` with the same `IdempotencyToken`. AWS returns the first accelerator; see what Floci does.
- Add a second listener whose port range overlaps the first (`80-90` beside `80-80`). AWS answers `InvalidPortRangeException`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with listener and endpoint-group updates, tags, the two deletion refusals and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/globalaccelerator). (The gallery has no Global Accelerator counterpart on Floci's Azure, GCP or OCI emulators, so there's no other-cloud version of this one.)
