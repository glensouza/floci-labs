# Organizations in .NET: Floci stores an SCP it never reads

> Create an AWS organization from a single C# file, switch service control policies on, and create two SCPs: one valid, and one whose content is the string `not json`. AWS refuses the second with `MalformedPolicyDocumentException`. Floci 2.2.0 stores it.

## What it shows

- AWS Organizations from .NET with the official `AWSSDK.Organizations` package, and no Floci-specific library.
- **SCPs start switched off.** A new root lists no policy types, and `EnablePolicyType` turns SCPs on, as AWS documents. Floci 2.1.0 had them on already; the lab says which way it went.
- **A valid SCP is created.** A policy denying `s3:DeleteBucket`, as AWS would accept it.
- **So is one that isn't a policy at all.** `CreatePolicy` with `Content = "not json"` succeeds and reads back unchanged. AWS answers `MalformedPolicyDocumentException`. If you build policy documents in code, a test against Floci won't catch a broken one. The lab says which way it went, so it tells you if Floci starts validating.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Organizations
- NuGet: `AWSSDK.Organizations` 4.0.101.6, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the ids differ on every run):

```text
CreateOrganization            -> o-81xqxd4xr5
ListRoots                     -> r-8s4f, policy types: []
EnablePolicyType              -> SCPs enabled (a new root has none, as on AWS)
CreatePolicy, a valid SCP     -> p-b7253z81
CreatePolicy, "not json"      -> p-8201e2k3, stored content: not json

Floci stored a policy that is not a policy.
  AWS answers MalformedPolicyDocumentException here. If you build SCPs in code,
  a test against Floci will not catch a broken one: validate the JSON yourself.
DeleteOrganization            -> o-81xqxd4xr5 removed
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**It cleans up.** An account belongs to at most one organization, so the lab creates one, deletes its policies, then deletes the organization in a `finally`. Running it twice gives the same result. Don't run it against an account that is already in an organization: `CreateOrganization` answers `AlreadyInOrganizationException`.

## Try changing...

- Make the content valid JSON but not a valid policy, such as `{}` or a statement with no `Effect`. AWS refuses both; see what Floci does.
- Attach the `not json` policy to the root with `AttachPolicy`. On AWS you never get that far.
- Skip `EnablePolicyType` and go straight to `CreatePolicy`. Floci refuses it with `PolicyTypeNotEnabledException`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with an OU, attach and detach, tags, duplicate and in-use refusals, and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/organizations). (None of Floci's other emulators has an organizations service, so there's no other-cloud version of this one.)
