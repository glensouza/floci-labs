# IAM Access Analyzer in .NET: on Floci, a missing operation and a missing analyzer are the same 404

> Create an IAM Access Analyzer analyzer from a single C# file, ask for it with `GetAnalyzer`, then delete it twice. Floci 2.1.0 implements only `CreateAnalyzer`, `ListAnalyzers` and `DeleteAnalyzer`, and it answers the operations it doesn't have with `404 UnknownOperationException`, not `501`. Deleting an analyzer that doesn't exist is also a `404`. Only the error code tells them apart.

## What it shows

- IAM Access Analyzer from .NET with the official `AWSSDK.AccessAnalyzer` package, and no Floci-specific library.
- **What Floci implements.** `CreateAnalyzer`, `ListAnalyzers` and `DeleteAnalyzer`. The analyzer is `ACTIVE` as soon as it is created.
- **What it doesn't, and how it says so.** `GetAnalyzer` answers HTTP `404` with `UnknownOperationException`. A second `DeleteAnalyzer` answers HTTP `404` with `ResourceNotFoundException`. Code that treats a 404 as "not found" can't tell a feature Floci lacks from a resource that isn't there.
- The lab prints both answers and says whether they still collide, so it tells you when Floci ships `GetAnalyzer` or changes how it reports a missing operation.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: IAM Access Analyzer
- NuGet: `AWSSDK.AccessAnalyzer` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.1.0 (`floci/floci:latest`, October 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then:

```bash
dotnet run lab.cs
```

Expected output (the analyzer name differs on every run):

```text
CreateAnalyzer         -> arn:aws:access-analyzer:us-east-1:000000000000:analyzer/lab-aa-dc34bf2a
ListAnalyzers          -> lab-aa-dc34bf2a, ACTIVE
GetAnalyzer            -> 404 UnknownOperationException
DeleteAnalyzer         -> deleted
DeleteAnalyzer again   -> 404 ResourceNotFoundException

Floci answered GetAnalyzer, an operation it does not implement, with 404,
  the same status as deleting an analyzer that does not exist. Not 501.
  Code that maps 404 to "not found" reads a missing feature as a missing resource:
  check the error code (UnknownOperationException vs ResourceNotFoundException), not the status.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**`FailureOf` reads the status and the code.** Both come off `AmazonAccessAnalyzerException`: `StatusCode` is the HTTP status, and `ErrorCode` is the `__type` from the response body. The lab compares the statuses and prints the codes.

**Cleanup is one call.** A fresh analyzer name per run, and a `finally` that deletes it if the run stopped before its own delete, so running it twice gives the same result.

**Why this matters.** An emulator that doesn't implement something has to say so somehow, and the obvious convention is `501 Not Implemented`. If your tests or a coverage check treat `501` as "not supported here" and `404` as "the thing isn't there", Floci's Access Analyzer gets both wrong: an unsupported `GetAnalyzer` looks like an analyzer that vanished. To read an analyzer back on Floci, use `ListAnalyzers` and pick it out by name, which also works on AWS.

## Try changing...

- Call `ListArchiveRules` or `ListFindingsV2`. Both answer `404 UnknownOperationException` too.
- Call `TagResource` with the ARN `CreateAnalyzer` returned. Floci answers `400 BadRequestException: Invalid resource ARN` for that ARN. Set tags through `CreateAnalyzer`'s `Tags` instead; `ListAnalyzers` returns them.
- Create an analyzer with a `Type` that isn't valid. Floci refuses it with `ValidationException: type must be a valid analyzer type.`, so some inputs are checked.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with tags, a duplicate-name check, read-back through `ListAnalyzers`, delete-and-confirm, cleanup by name and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/accessanalyzer). (The gallery has no Access Analyzer-style sample for the other three clouds yet, so there's no other-cloud version of this one.)
