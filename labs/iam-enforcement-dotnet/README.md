# IAM in .NET, and the Deny policy that denies nothing (until you flip one switch)

> Create a user whose only policy is an explicit Deny on everything, call AWS as that user, and watch the call go through. Then restart Floci with enforcement on and watch it get refused.

## What it shows

- IAM from .NET with **one official package** (`AWSSDK.IdentityManagement`) and no Floci-specific library: create a user, attach an inline policy, mint an access key, then **sign requests as that user**.
- **The default:** Floci accepts any credentials and evaluates no policies. A user with `Deny *` on `*` can still call `iam:GetUser`. This is deliberate, and it's the right default for testing provisioning code, meaning the calls, their ordering and the error shapes. But it means a passing local test says nothing about whether your policies grant what you think.
- **The switch:** start Floci with `FLOCI_SERVICES_IAM_ENFORCEMENT_ENABLED=true` and the same run comes back `AccessDenied`. Floci evaluates identity policies with AWS's precedence: an explicit Deny beats any Allow, and no Allow means an implicit deny. At that point you **can** test your policies locally.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: IAM
- NuGet: `AWSSDK.IdentityManagement` 4.0.103.4, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.1.0 (`floci/floci:latest`, September 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

**1. Stock Floci, no enforcement:**

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
dotnet run lab.cs
```

```text
As admin:  created floci-labs-0a156575ee3f4ea4b0b87
           attached an inline policy: Deny * on *
           created access key AKIA...

As floci-labs-0a156575ee3f4ea4b0b87: iam:GetUser -> ALLOWED (arn:aws:iam::000000000000:user/...)

An explicit Deny on everything, and the call went through anyway. ...
```

**2. Same lab, enforcement on:**

```bash
docker rm -f floci
docker run -d --name floci -p 4566:4566 -e FLOCI_SERVICES_IAM_ENFORCEMENT_ENABLED=true floci/floci:latest
dotnet run lab.cs
```

```text
As floci-labs-2433513089d24d4da41a1: iam:GetUser -> DENIED (AccessDenied)

IAM enforcement is on, so the explicit Deny wins, just as it would in real AWS.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**`test`/`test` is the admin.** With enforcement on, Floci still lets the `test` credential through, so the lab uses it to set things up and to clean up. Only the second client, signed with the new user's own key, is subject to policy. See [IAM Enforcement Mode](https://github.com/floci-io/floci/blob/main/docs/services/iam.md#iam-enforcement-mode) for the full bypass rules.

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Cleanup order matters.** IAM won't delete a user that still has access keys or inline policies, so the `finally` removes the key, then the policy, then the user.

## Try changing...

- Replace the Deny with `{"Effect": "Allow", "Action": "s3:*", "Resource": "*"}`. With enforcement on, `iam:GetUser` is still denied: there's no Allow for it, so the result is an implicit deny.
- Add a second statement allowing `iam:GetUser` alongside the Deny. The explicit Deny should still win.
- Attach a [permissions boundary](https://docs.aws.amazon.com/IAM/latest/UserGuide/access_policies_boundaries.html) that allows only `s3:*` on top of an `Allow *` policy.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/floci](https://github.com/glensouza/floci)
