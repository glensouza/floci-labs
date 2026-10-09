# CodeGuru Reviewer in .NET: associating a repository that does not exist

> Associate a CodeCommit repository with CodeGuru Reviewer from a single C# file, and read the association straight back. The catch: real CodeGuru Reviewer reads `Associating` while it checks the repository, then `Failed` for one that is not there. Floci never reads the repository, so a made-up name reads `Associated` the moment the call returns. Code reviews are not built: `ListCodeReviews` answers `UnknownOperationException`.

## What it shows

- CodeGuru Reviewer from .NET with the official `AWSSDK.CodeGuruReviewer` package, and no Floci-specific library.
- **The association record works like AWS:** `AssociateRepository`, `DescribeRepositoryAssociation`, `ListRepositoryAssociations` and `DisassociateRepository` all work, and an association ARN that was never made draws `NotFoundException`, as on AWS.
- **The repository is never read.** A CodeCommit repository name that exists nowhere reads `Associated` on the first read after `AssociateRepository`. On AWS it reads `Associating` and then `Failed`, so code that waits for the association to settle, or handles a failed one, never runs here.
- **Code reviews are not built.** `ListCodeReviews` answers HTTP 404 `UnknownOperationException`, not a 501. Real AWS lists the reviews it has run.
- The lab prints what it saw for each one and says which way it went, so it tells you if Floci starts checking repositories or builds code reviews.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: CodeGuru Reviewer
- NuGet: `AWSSDK.CodeGuruReviewer` 4.0.100.17, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (ARNs change every run):

```text
A repository that does not exist     -> Associated
List the code reviews                -> UnknownOperationException (HTTP 404): Unknown operation: GET /codereviews
An association that was never made   -> NotFoundException (HTTP 404): Repository association arn:aws:codeguru-reviewer:us-east-1:000000000000:association:4e68cc1f-2453-4bc9-a0c6-fb0964f6b2c8 does not exist.

Floci never read the repository: the association read Associated the moment it was made.
  Code that waits for Associating to end, or handles Failed, never runs here.
Code reviews are not built yet: Floci answers UnknownOperationException.

Cleanup -> removed the association; listed afterwards: none
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**A 404, not a 501.** Floci answers an operation it has not built with HTTP 404 and the error code `UnknownOperationException`. Code that treats "not implemented" as a 501 reads it as "not found" instead, so the lab matches on the error code.

**A disassociated association drops out of the list at once.** Real CodeGuru Reviewer reads `Disassociating` for a while first.

**Don't run this against a real account as written.** On AWS the association starts as `Associating`, and the `finally` disassociates without waiting for it to settle.

## Try changing...

- Poll `DescribeRepositoryAssociation` until the state leaves `Associating`, the way a setup script does, and count how many polls Floci needs.
- Tag the association with `TagResource` and read it back with `ListTagsForResource`.
- Call `CreateCodeReview` against the association and see what Floci answers.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
