# AWS Organizations in .NET: Floci turns SCPs on for you, AWS doesn't

> Create an AWS organization from a single C# file, read its root, and call `EnablePolicyType` for service control policies. Floci 2.1.0 enables SCPs on every new root by itself, so the call answers `PolicyTypeAlreadyEnabledException`. AWS documents a new root as having no policy types enabled, so code that never makes the call passes against Floci and fails against AWS.

## What it shows

- AWS Organizations from .NET with the official `AWSSDK.Organizations` package, and no Floci-specific library.
- **SCPs already on.** Straight after `CreateOrganization`, `ListRoots` shows `SERVICE_CONTROL_POLICY=ENABLED`, and `EnablePolicyType` answers `400 PolicyTypeAlreadyEnabledException`.
- **What happens when they're off.** After `DisablePolicyType`, Floci refuses `CreatePolicy` with `400 PolicyTypeNotEnabledException`. The AWS API reference documents that exception on `AttachPolicy`.
- The lab says which way Floci went, so it tells you if Floci starts creating roots with SCPs off.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AWS Organizations
- NuGet: `AWSSDK.Organizations` 4.0.101.6, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.1.0 (`floci/floci:latest`, October 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566 and that its account is not in an organization yet, which is true of a fresh container. If Floci isn't running:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then:

```bash
dotnet run lab.cs
```

Expected output (the organization and root ids differ on every run):

```text
CreateOrganization            -> o-0ejzr2xyc2
ListRoots                     -> r-j1ib, policy types: [SERVICE_CONTROL_POLICY=ENABLED]
EnablePolicyType              -> 400 PolicyTypeAlreadyEnabledException
DisablePolicyType             -> SCPs off
CreatePolicy with SCPs off    -> 400 PolicyTypeNotEnabledException

Floci enabled SCPs on the new root by itself, so EnablePolicyType had nothing to do.
  AWS documents a new root as having no policy types enabled. Code that never calls
  EnablePolicyType passes against Floci and fails against AWS: make the call, and treat
  PolicyTypeAlreadyEnabledException as success.
DeleteOrganization            -> o-0ejzr2xyc2 cleaned up
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**`Outcome` reads the status and the code.** Both come off `AmazonOrganizationsException`: `StatusCode` is the HTTP status, and `ErrorCode` is the `__type` from the response body.

**Cleanup is one call.** `DeleteOrganization` in a `finally` removes the organization and everything in it, so running the lab twice gives the same result. On AWS an organization with member accounts can't be deleted; this one has none.

**Why this matters.** Organizations code is usually written once and run once, against a real management account. If the only place it was ever exercised is Floci, the missing `EnablePolicyType` shows up for the first time in production, as a failed SCP. The fix costs one call, and Floci answers it harmlessly.

## Try changing...

- Create a policy with `Content = "not json"`. Floci 2.1.0 accepts it; AWS answers `MalformedPolicyDocumentException`.
- Create an organizational unit twice under the same parent. Floci refuses the second with `DuplicateOrganizationalUnitException`, as AWS does.
- Attach the policy to an OU and then delete it. Floci refuses with `PolicyInUseException` until you detach it.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a tagged OU, an SCP attached and read back, refusal checks, cleanup by name, reuse of an existing organization and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/organizations). (The gallery has no Organizations-style sample for the other three clouds yet, so there's no other-cloud version of this one.)
