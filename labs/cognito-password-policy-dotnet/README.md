# Cognito in .NET: Floci accepts a password AWS would refuse

> Create a Cognito user pool from a single C# file, give a user the password `abc`, and sign in with it. A default pool on AWS refuses that password with `InvalidPasswordException`. Floci 2.2.0 accepts it, and signs the user in. It does now check the app client's auth flows, as AWS does.

## What it shows

- Cognito user pools from .NET with the official `AWSSDK.CognitoIdentityProvider` package, and no Floci-specific library.
- **The password policy isn't enforced.** `AdminSetUserPassword` with `abc` succeeds, and `InitiateAuth` with `USER_PASSWORD_AUTH` signs the user in with it. AWS's default policy wants 8+ characters with upper, lower, digit and symbol. A test fixture with a short hard-coded password passes against Floci and fails against AWS. The lab says which way it went, so it tells you if Floci starts enforcing it.
- **The client's auth flows are.** The same sign-in through a client with no `ExplicitAuthFlows` is refused with `USER_PASSWORD_AUTH flow not enabled for this client`, the AWS answer. Floci 2.1.0 signed you in anyway.
- **The token's issuer is fixed.** The access token's `iss` claim is `http://localhost:4566/<pool id>` whatever host and port you called. Only the claim is printed; the token itself is live on AWS.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Cognito user pools
- NuGet: `AWSSDK.CognitoIdentityProvider` 4.0.105.3, pinned in the `#:package` line at the top of `lab.cs`
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
CreateUserPool lab-pool-a32dbea7 -> us-east-1_af64c2450 (default password policy)
CreateUserPoolClient x2      -> 941e55f3930244fa9a0982ab70 (ALLOW_USER_PASSWORD_AUTH), 617330f9e5fc4042bc6d0f9f6d (no flows)
AdminSetUserPassword "abc"   -> accepted
InitiateAuth, flow allowed   -> signed in with "abc", access token iss: http://localhost:4566/us-east-1_af64c2450
InitiateAuth, no flows        -> refused: USER_PASSWORD_AUTH flow not enabled for this client

Floci accepted "abc" as a password and signed alice in with it. On AWS the default policy
  refuses it with InvalidPasswordException. A fixture with a short hard-coded password passes here
  and fails against AWS: generate one that satisfies the real policy.

DeleteUserPool us-east-1_af64c2450 -> done
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**It cleans up.** Deleting the pool takes the clients and the user with it, in a `finally`, so running the lab twice gives the same result.

**About the issuer.** If an API validates these tokens with JWT bearer auth, set its authority from the token's `iss` claim, not from your configured endpoint.

## Try changing...

- Set an explicit `Policies.PasswordPolicy` on `CreateUserPool` with `MinimumLength = 12` and try `abc` again.
- Use `SignUp` instead of `AdminSetUserPassword` with a one-character password.
- Sign in with the wrong password. Floci answers `NotAuthorizedException`, as AWS does.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with token checks, sign-up, groups and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/cognito). (Floci's GCP emulator has Firebase Auth; the gallery doesn't have a sample for it yet, so there's no other-cloud version of this one.)
