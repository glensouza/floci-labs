# CloudFront in .NET: a distribution on localhost, and the delete that has to wait

> Create a CloudFront distribution from a single C# file, then tear it down the only way CloudFront allows: disable it, wait for the change to deploy, then delete it. The catch: on Floci every change is already `Deployed` in the response that makes it, so the wait you must write for AWS never waits here, and nothing tells you if it's missing.

## What it shows

- CloudFront from .NET with the official `AWSSDK.CloudFront` package, and no Floci-specific library.
- **The delete rules work like AWS:** deleting an enabled distribution is refused with `409 DistributionNotDisabled`, and every update and delete carries the distribution's current ETag in `If-Match`.
- **Changes are never `InProgress`.** Real CloudFront answers `InProgress` after a create or an update and takes minutes to reach `Deployed`, and it refuses to delete a disabled distribution until it has. Floci answers `Deployed` in the create and update responses themselves, so the wait loop exits on the first poll. The lab prints which way it went, so it tells you if Floci starts modelling deployment.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: CloudFront
- NuGet: `AWSSDK.CloudFront` 4.0.101.4, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (ids change every run):

```text
CreateDistribution        -> EZU1G2XRZVV1IP, Deployed, EZU1G2XRZVV1IP.cloudfront.net
DeleteDistribution (on)   -> 409 DistributionNotDisabled
UpdateDistribution (off)  -> Deployed
GetDistribution           -> Deployed after 1 poll(s), 0.0 s
DeleteDistribution (off)  -> EZU1G2XRZVV1IP deleted

Every change came back Deployed in the response itself. Real CloudFront answers InProgress
  here for minutes, so code that deletes straight after disabling passes on this Floci and
  is refused with DistributionNotDisabled on AWS.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The `Deployed` wait loop is production code, not a lab workaround. Keep it when you point this at AWS, where it polls every 15 seconds for up to 30 minutes.

**An update replaces the whole config.** Disabling means fetching the config and its ETag with `GetDistributionConfig`, flipping `Enabled`, and sending the whole thing back with that ETag in `If-Match`. The delete then needs the ETag the distribution has *now*, so the lab reads it again after the wait.

**The origin is a bucket nobody owns** (`lab-<run>.s3.amazonaws.com`). CloudFront checks that the domain has the right shape, not that the bucket exists, so the lab needs no second service. The cache behaviour names the AWS-managed `CachingOptimized` policy, whose id is the same in every account.

## Try changing...

- Delete the wait loop and point the lab at a real AWS account. (The delete fails with `DistributionNotDisabled`, because the disabled distribution is still `InProgress`.)
- Send the update with the ETag from *before* an earlier update. (Floci refuses with `InvalidIfMatchVersion`; AWS says `PreconditionFailed`.)
- Create two distributions with the same `CallerReference` and different comments. (Floci 2.1.0 makes two. AWS refuses the second with `DistributionAlreadyExists`.)

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
