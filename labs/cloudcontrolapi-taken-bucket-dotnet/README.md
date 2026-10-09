# Cloud Control API in .NET: a taken bucket name that reads SUCCESS

> Create an S3 bucket from a single C# file through Cloud Control API, the one AWS API that creates any resource type from a JSON description, and follow the request to its end. Then ask for the same bucket again. Real Cloud Control reports that second request `FAILED` with `AlreadyExists`. Floci reports `SUCCESS`, hands back the existing bucket's name, and creates nothing new.

## What it shows

- Cloud Control API from .NET with the official `AWSSDK.CloudControlApi` package, and no Floci-specific library. No S3 SDK: Cloud Control creates the bucket from `{"BucketName": "..."}`.
- **A Cloud Control call is a request you come back for.** `CreateResource` answers `IN_PROGRESS` with a request token, and `GetResourceRequestStatus` says when it is done and what the resource is called. Floci follows the same shape, and finishes within moments.
- **A taken name is not refused.** The second `CreateResource` for the same `BucketName` reads `IN_PROGRESS`, then `SUCCESS`, with the bucket's name as its identifier. `ListResources` still lists one bucket under that name. On AWS the request reads `FAILED` with error code `AlreadyExists`, so code that handles that case is never exercised here.
- The lab prints what it saw and says which way it went, so it tells you if Floci starts refusing a taken name.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Cloud Control API (creating an S3 bucket)
- NuGet: `AWSSDK.CloudControlApi` 4.0.100.16, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.2.0 (`floci/floci:latest`, October 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then:

```bash
dotnet run lab.cs
```

Expected output (the bucket name changes every run):

```text
Create bucket lab-1f43ecc43dd6
  first answer IN_PROGRESS, then SUCCESS -> lab-1f43ecc43dd6
Create bucket lab-1f43ecc43dd6 again
  first answer IN_PROGRESS, then SUCCESS -> lab-1f43ecc43dd6
Buckets listed under that name          -> 1

Floci reported SUCCESS for a bucket that already exists, and created nothing new.
  Code that treats FAILED/AlreadyExists as "someone else has this name" never sees it here.

Cleanup -> removed the bucket (SUCCESS); still there afterwards: none
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**A failed request is not an exception.** Cloud Control reports a failure inside the request's status, as `OperationStatus` `FAILED` and an `ErrorCode`, rather than throwing from `CreateResource`. That is why the lab reads the status rather than catching an exception.

**Don't run this against a real account as written.** It creates a real bucket, and polls for only ten seconds, where real Cloud Control can take longer.

## Try changing...

- List queues: `ListResources` with `TypeName = "AWS::SQS::Queue"`. Floci answers `UnsupportedActionException` (HTTP 400); it lists buckets and roles only.
- Add a tag with `UpdateResource` and a JSON Patch. Floci answers `UnsupportedOperation` (HTTP 400), not a 501.
- Send `DesiredState = "not json"`. Floci refuses it with `InvalidRequestException`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
