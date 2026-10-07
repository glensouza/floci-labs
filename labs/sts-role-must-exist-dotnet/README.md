# STS in .NET: the role has to exist, and on Floci it works too soon

> Assume an IAM role from a single C# file: first one that was never created, then one created through IAM a moment earlier. Floci 2.2.0 refuses the first, as AWS does. It accepts the second the instant it exists, which AWS, with IAM's eventual consistency, may not.

## What it shows

- STS and IAM from .NET with the official `AWSSDK.SecurityToken` and `AWSSDK.IdentityManagement` packages, and no Floci-specific library. The second package is there because the role has to be created.
- **A role nobody created is refused.** `AssumeRole` on a role ARN with no role behind it answers `AccessDenied`, the same answer AWS gives. Floci 2.1.0 handed out working credentials for it; this lab says which way it went, so it notices if that changes again.
- **A role created a moment ago works at once.** The lab creates a role whose trust policy names the account root and assumes it in the very next call. A second client signed with the returned credentials resolves to `assumed-role/<role>/lab-session`, the session name it asked for. The secret and session token are never printed.
- **On AWS that second call can fail.** IAM is eventually consistent, so a freshly created role can be refused with `AccessDenied` for a few seconds. Code that creates a role and assumes it straight away needs a short, bounded retry against AWS. Against Floci a refusal is final, so don't retry it there.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: STS, IAM
- NuGet: `AWSSDK.SecurityToken` 4.0.101 and `AWSSDK.IdentityManagement` 4.0.103.4, pinned in the `#:package` lines at the top of `lab.cs`
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

Expected output (the role name suffixes differ on every run):

```text
GetCallerIdentity              -> arn:aws:iam::000000000000:root
AssumeRole lab-never-created-df7dadfa -> AccessDenied
  Refused, as AWS refuses it: a role has to exist and trust the caller.

CreateRole                     -> arn:aws:iam::000000000000:role/lab-role-0783bda0
AssumeRole, 0 s after create   -> arn:aws:sts::000000000000:assumed-role/lab-role-0783bda0/lab-session
GetCallerIdentity as it        -> arn:aws:sts::000000000000:assumed-role/lab-role-0783bda0/lab-session

Floci let you assume the role the instant it existed.
  On AWS, IAM is eventually consistent: a role created a moment ago can be refused with
  AccessDenied for a few seconds. Code that assumes a fresh role needs a short, bounded retry there.
DeleteRole                     -> lab-role-0783bda0 removed
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines per client know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The assumed-role client reuses the STS config with `SessionAWSCredentials`, which is exactly how production code uses assumed-role credentials.

**The trust policy names the account root.** On AWS that means "any principal in this account whose own identity policy allows `sts:AssumeRole`". Floci's caller is the account root, so the second half never comes up here; on AWS it will.

**It cleans up.** The role is deleted in a `finally`, so running the lab twice gives the same result.

## Try changing...

- Wrap the second `AssumeRole` in a retry that only fires on `AccessDenied`, for up to 20 seconds, and only when not talking to Floci. That is what the full sample does.
- Change the trust policy's principal to a different account id. On AWS that is a cross-account trust; see what Floci does.
- Set `DurationSeconds` to `1`. AWS answers a validation error below 900; Floci 2.2.0 does too.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with `GetSessionToken`, `GetFederationToken`, redacted credentials and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/sts). (Floci's GCP emulator has a Security Token Service too; the gallery doesn't have a sample for it yet, so there's no other-cloud version of this one.)
