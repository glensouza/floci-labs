# STS in .NET: Floci assumes a role that doesn't exist

> Assume an IAM role from a single C# file, then sign a second client with the temporary credentials and ask STS who it is. On AWS, `AssumeRole` only works for a role that exists and trusts you. Floci 2.1.0 hands out working credentials for any well-formed role ARN, so a green run proves the call shape, not the role.

## What it shows

- STS from .NET with the official `AWSSDK.SecurityToken` package, and no Floci-specific library.
- **A role nobody created.** The lab builds a role ARN from the caller's account id and a random name, makes no IAM call, and asks STS to assume it.
- **The credentials work.** A second client signed with the returned key, secret and session token calls `GetCallerIdentity`, and Floci resolves it to `assumed-role/<that role>/…`. The secret and session token are never printed.
- **On AWS it doesn't.** The same `AssumeRole` answers `AccessDenied`: the role has to exist and its trust policy has to let you in. The lab says which way it went, so it tells you if Floci ever starts checking.
- **One more thing it notices.** The `AssumeRole` response echoes the session name you asked for, but the identity the credentials resolve to is named `floci-session`. On AWS both carry your session name.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: STS
- NuGet: `AWSSDK.SecurityToken` 4.0.101, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the role name suffix differs on every run):

```text
GetCallerIdentity           -> arn:aws:iam::000000000000:root
AssumeRole lab-never-created-f1056b98 -> arn:aws:sts::000000000000:assumed-role/lab-never-created-f1056b98/lab-session
GetCallerIdentity as it     -> arn:aws:sts::000000000000:assumed-role/lab-never-created-f1056b98/floci-session

Floci handed out working credentials for lab-never-created-f1056b98, a role that does not exist.
  On AWS this AssumeRole answers AccessDenied: the role has to exist and trust the caller.
  A green run here proves the call shape, not your role or its trust policy.
The assumed identity's session name is "floci-session", not the "lab-session" you asked for.
  On AWS it is always yours, so don't match on the session name against Floci.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The second client reuses the same config with `SessionAWSCredentials`, which is exactly how production code uses assumed-role credentials.

**Nothing to clean up.** STS creates nothing that persists; every credential simply expires. So the lab has no `finally`, and running it twice gives the same result.

**Why this matters.** Most `AssumeRole` bugs in real life are not in the call. They are in the role: it doesn't exist in this account, or its trust policy doesn't name the caller, or the caller lacks `sts:AssumeRole`. Floci checks none of that, so a test suite that passes against Floci says nothing about your roles. Test role and trust-policy setup against AWS, or with IAM Access Analyzer's policy checks. And if your code checks who it became, match on the role, not the session name.

## Try changing...

- Set `DurationSeconds` to `1`, or to `99999`. AWS answers `ValidationError` outside 900–43200; see what Floci does, and what `Expiration` comes back.
- Call `GetSessionToken` with the assumed-role client. AWS refuses session credentials there; see whether Floci does.
- Replace the role ARN with one in a different account id. On AWS that needs a cross-account trust policy.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with `GetSessionToken`, `GetFederationToken`, redacted credentials and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/sts). (Floci's GCP emulator has a Security Token Service too; the gallery doesn't have a sample for it yet, so there's no other-cloud version of this one.)
