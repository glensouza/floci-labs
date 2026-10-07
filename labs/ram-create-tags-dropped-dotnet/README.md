# AWS RAM in .NET: the tags you send with CreateResourceShare don't stick on Floci

> Create an AWS Resource Access Manager share from a single C# file, with two tags on the create request, and read it back. Floci 2.1.0 creates the share but stores none of those tags; the same tags sent again with `TagResource` do stick. On AWS the create stores them, so code that tags at create time works there and reads back nothing here.

## What it shows

- AWS RAM from .NET with the official `AWSSDK.RAM` package, and no Floci-specific library.
- **Create-time tags dropped.** `CreateResourceShare` with `Tags = [env=lab, owner=floci-labs]` answers `ACTIVE` with `tags: []`, and `GetResourceShares` reads the share back with `tags: []` too.
- **`TagResource` works.** The same two tags sent with `TagResource` read back straight away.
- The lab says which way Floci went, so it tells you when Floci starts storing the create-time tags.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AWS Resource Access Manager
- NuGet: `AWSSDK.RAM` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the share name differs on every run):

```text
CreateResourceShare (2 tags)  -> ACTIVE, tags in the response: []
GetResourceShares             -> tags: []
TagResource (same 2 tags)     -> done
GetResourceShares             -> tags: [owner=floci-labs, env=lab]

Floci kept 0 of the 2 tags sent with CreateResourceShare, and all of them once
  TagResource sent them again. On AWS the create stores them. Code that tags at create and
  reads the tags back (a cost report, a cleanup by owner) sees none of them on Floci.
DeleteResourceShare           -> lab-share-76ad5b2c cleaned up
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**Two reads, not one.** The response to `CreateResourceShare` could in principle differ from what was stored, so the lab reads the share back with `GetResourceShares` after the create and again after `TagResource`, and compares what it sent with what is there.

**Cleanup is one call.** `DeleteResourceShare` in a `finally` removes the share. A deleted share stays listed with status `DELETED`, as on AWS, so a re-run starts clean.

**Why this matters.** Tags are how shares get found later: by a cost report, by a cleanup job looking for its owner, by an audit. If create-time tagging was only ever exercised against Floci, the tests that read tags back fail here for a reason AWS never shows, and the tempting fix (tag afterwards) is a second call production doesn't need. The lab tells you which behaviour you're looking at.

## Try changing...

- Filter with `GetResourceShares` and `TagFilters = [team=nothing]`. Floci 2.1.0 ignores the filter and returns every share, so a tag filter is no test of your tags here.
- Call `GetResourceShareAssociations`. Floci 2.1.0 answers `UnknownOperationException` with a 404, not a 501; `ListPrincipals` and `ListResources` read the same associations back.
- Delete the share twice. The second `DeleteResourceShare` answers `UnknownResourceException`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a share tagged, renamed, associated with a principal and a resource, read back with `ListPrincipals` and `ListResources`, disassociated, deleted and cleaned up by name, plus a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/ram). (The gallery has no resource-sharing sample for the other three clouds yet, so there's no other-cloud version of this one.)
