# IAM Identity Center in .NET: Floci provisions a permission set instantly, even to an account that doesn't exist

> Create an IAM Identity Center permission set from a single C# file and provision it to account `999999999999`. On AWS, provisioning is asynchronous: the call answers `IN_PROGRESS` and you poll for the outcome. Floci 2.1.0 answers `SUCCEEDED` on the first call, so code that never polls passes here and races in production.

## What it shows

- IAM Identity Center permission sets from .NET with the official `AWSSDK.SSOAdmin` package, and no Floci-specific library.
- **Floci's one built-in instance.** `ListInstances` returns `floci-identity-center` on a fresh container, so there is nothing to enable first. (A second `CreateInstance` is refused with `ServiceQuotaExceededException`, as AWS limits an account to one.)
- **Instant provisioning.** `ProvisionPermissionSet` with `TargetType = AWS_ACCOUNT` and an account id that belongs to nobody comes back `SUCCEEDED` straight away. On AWS it answers `IN_PROGRESS`, and the outcome, `FAILED` included, arrives only through `DescribePermissionSetProvisioningStatus`.
- The lab polls anyway, as production code has to, and says which way the first answer went, so it tells you if Floci ever starts provisioning asynchronously.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: IAM Identity Center (SSO Admin)
- NuGet: `AWSSDK.SSOAdmin` 4.0.103, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the instance, permission set name and request id differ on every run):

```text
ListInstances          -> floci-identity-center, arn:aws:sso:::instance/ssoins-7223b02a5d9f7c8e
CreatePermissionSet    -> lab-ps-18390b52
ProvisionPermissionSet -> SUCCEEDED (account 999999999999, request a87e1468-0877-44b2-b67f-63fbb5fff30d)
after 0 poll(s)       -> SUCCEEDED

Floci answered SUCCEEDED on the first call, for an account that does not exist.
  On AWS provisioning is asynchronous: the call answers IN_PROGRESS, and the outcome, FAILED
  included, arrives only through DescribePermissionSetProvisioningStatus.
  Code that reads the first status and moves on passes here and races in production: poll
  DescribePermissionSetProvisioningStatus until the request settles.

DeletePermissionSet lab-ps-18390b52 -> done
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**The poll is there on purpose.** It runs zero times on Floci today. It is what makes the same file correct against AWS, and it is what the lab would exercise if Floci ever starts answering `IN_PROGRESS`.

**Cleanup is one call.** `DeletePermissionSet` in a `finally`, with a fresh permission set name per run, so running it twice gives the same result.

**Why this matters.** Provisioning is how a permission set's policies reach the accounts it is assigned to. Code that treats the first response as the result, such as a deployment script that provisions and then immediately signs someone in with the new role, works every time against Floci. Against AWS it sometimes finds the role not there yet, and it never sees a failed provisioning at all. Poll `DescribePermissionSetProvisioningStatus` with the request id until the status leaves `IN_PROGRESS`, and treat `FAILED` as an error.

## Try changing...

- Use `TargetType = ALL_PROVISIONED_ACCOUNTS` with no `TargetId`. Floci answers `SUCCEEDED` for that too.
- Create a permission set with `SessionDuration = "PT99H"`. Floci refuses it with `ValidationException: SessionDuration must be between 1 and 12 hours.`, so some inputs are checked.
- Call `CreateInstance` and see the `ServiceQuotaExceededException`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with managed and inline policies, update and read-back, tags, a duplicate-name check, delete-and-confirm, cleanup by name and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/identitycenter). (The gallery has no Identity Center-style sample for the other three clouds yet, so there's no other-cloud version of this one.)
