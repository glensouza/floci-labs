# AWS Account in .NET: Floci can set an alternate contact but not delete it

> Set your account's SECURITY alternate contact from a single C# file, read it back, then delete it. Floci 2.1.0 stores the contact but answers `DeleteAlternateContact` with a 404 `UnknownOperationException`, and the contact stays. It's the same status code a missing contact gets, so code that treats any 404 on a delete as "already gone" reports success and leaves the contact in place.

## What it shows

- AWS Account Management from .NET with the official `AWSSDK.Account` package, and no Floci-specific library.
- **Put and get work.** `PutAlternateContact` stores the contact and `GetAlternateContact` reads it straight back. An account with no contact of that type answers `ResourceNotFoundException`, as on AWS.
- **Delete isn't there.** `DeleteAlternateContact` answers `HTTP 404 UnknownOperationException: Unknown operation: POST /deleteAlternateContact`, and the contact is still there afterwards. It's a 404, not a 501.
- The lab says which way Floci went, so it tells you when Floci adds the delete.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AWS Account Management
- NuGet: `AWSSDK.Account` 4.0.101.7, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output on a fresh Floci (the contact name differs on every run):

```text
GetAlternateContact (before)    -> none (ResourceNotFoundException)
PutAlternateContact             -> lab-contact-22c66d04
GetAlternateContact             -> lab-contact-22c66d04 <lab-contact-22c66d04@example.com>
DeleteAlternateContact          -> HTTP 404 UnknownOperationException: Unknown operation: POST /deleteAlternateContact
GetAlternateContact (after)     -> lab-contact-22c66d04 <lab-contact-22c66d04@example.com>

Floci has no DeleteAlternateContact: the contact this lab set is still there. On AWS the delete
  works. The 404 is UnknownOperationException, not ResourceNotFoundException, so code that reads
  any 404 on a delete as "already gone" reports success here and leaves the contact behind.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**Read it back after the delete.** The delete throws, but the read afterwards is what tells you whether the contact went. It didn't.

**Cleanup is the best Floci allows.** The contact belongs to the account rather than to the run, so the lab snapshots it first. If there was one, the lab writes it back at the end. If there wasn't, the lab can't remove its own, so a second run starts by finding the first run's contact and puts that back instead.

**Why this matters.** An alternate contact is one value per account and type, the person AWS writes to about security, billing or operations. Code that sets one in a test needs to take it off again, and it's easy to write that cleanup as "delete, and a 404 means it was already gone". On Floci that cleanup passes and the contact stays. The error type, not the status code, tells the two cases apart.

## Try changing...

- Put the contact again with a different name. There's one contact per type, so the second put replaces the first.
- Put a contact with `EmailAddress = ""`. Floci refuses it with `ValidationException`.
- Ask for the `BILLING` contact on a fresh Floci. It answers `ResourceNotFoundException`, the 404 that does mean "not there".

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with the contact snapshotted, set, overwritten, refused without an email and put back in a `finally`, plus a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/account). (The gallery has no account-contact sample for the other three clouds yet, so there's no other-cloud version of this one.)
