# AWS Account in .NET: deleting an alternate contact twice

> Set your account's SECURITY alternate contact from a single C# file, delete it, read it back, then delete it again. Floci 2.4.0 deletes the contact and answers the second delete with a 404 `ResourceNotFoundException`, the same exception a read gives for a contact nobody set. A 404 filter in your cleanup would also swallow a different 404, so catch the exception type.

## What it shows

- AWS Account Management from .NET with the official `AWSSDK.Account` package, and no Floci-specific library.
- **Put, get and delete work.** `PutAlternateContact` stores the contact, `GetAlternateContact` reads it straight back, and `DeleteAlternateContact` answers `HTTP 200` and removes it.
- **A second delete is a 404.** `DeleteAlternateContact` on a contact that is not there answers `HTTP 404 ResourceNotFoundException`, and so does a read of a contact nobody set.
- The lab says which way Floci went, so it tells you if Floci changes either answer.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AWS Account Management
- NuGet: `AWSSDK.Account` 4.0.102.2, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.4.0 (`floci/floci:latest`, November 2026)

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
PutAlternateContact             -> lab-contact-a27f803a
GetAlternateContact             -> lab-contact-a27f803a <lab-contact-a27f803a@example.com>
DeleteAlternateContact          -> HTTP 200
GetAlternateContact (after)     -> none (ResourceNotFoundException)
DeleteAlternateContact (again)  -> HTTP 404 ResourceNotFoundException: The alternate contact does not exist for the specified account and contact type.

The delete works, and deleting again is a 404 ResourceNotFoundException, the same exception a
  read gives for a contact nobody set. Cleanup code should catch that exception type and nothing wider.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**Read it back after the delete.** The delete returning `HTTP 200` is the service's answer; the read afterwards is what shows the contact went.

**Catch the type, not the status.** Both 404s here arrive as an exception with `StatusCode == NotFound`. Only the error type says what is missing: `ResourceNotFoundException` is the contact. A filter on the status alone would also swallow any other 404, such as one for an operation the server does not know.

**Why this matters.** An alternate contact is one value per account and type, the person AWS writes to about security, billing or operations. Code that sets one in a test needs to take it off again, and cleanup is the code that runs twice: after a retry, after a crash, after a cancelled run. Deleting what is already gone should end quietly. Anything else should fail the run.

## Try changing...

- Put the contact again with a different name. There's one contact per type, so the second put replaces the first.
- Put a contact with `EmailAddress = ""`. Floci refuses it with `ValidationException`.
- Run the lab twice in a row. It leaves the account as it found it, so both runs print the same answers.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with the contact snapshotted, set, overwritten, refused without an email, deleted and put back in a `finally`, plus a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/account). (The gallery has no account-contact sample for the other three clouds yet, so there's no other-cloud version of this one.)
