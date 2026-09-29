# Cognito in .NET: Floci signs you in past two pool settings AWS enforces

> Create a Cognito user pool from a single C# file, then sign a user in through an app client that never allowed password sign-in, with a password of `abc`. On AWS both are refused. Floci 2.1.0 signs you in anyway, so a green run proves the calls, not the pool settings.

## What it shows

- Cognito user pools from .NET with the official `AWSSDK.CognitoIdentityProvider` package, and no Floci-specific library.
- **A client with no auth flows.** The app client is created with no `ExplicitAuthFlows`, and Floci stores it as `[]`. On AWS, `InitiateAuth` with `USER_PASSWORD_AUTH` then answers `InvalidParameterException: USER_PASSWORD_AUTH flow not enabled for this client`. Floci issues tokens.
- **A password the policy refuses.** The pool keeps its default policy (8+ characters, upper, lower, digit and symbol), and `AdminSetUserPassword` sets `abc`. AWS answers `InvalidPasswordException`. Floci accepts it, and the user can sign in with it.
- **One more thing it notices.** The access token's `iss` claim is `http://localhost:4566/<pool id>` whatever host and port you called. On AWS it is `https://cognito-idp.<region>.amazonaws.com/<pool id>`. The token itself is never printed.
- The lab says which way each check went, so it tells you if Floci ever starts enforcing either one.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Cognito user pools
- NuGet: `AWSSDK.CognitoIdentityProvider` 4.0.105.3, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the pool name, pool id and client id differ on every run):

```text
CreateUserPool lab-lax-pool-ba67bb2f -> us-east-1_834d13b0c (default password policy)
CreateUserPoolClient        -> 15ce68b38c2140bf9e150a2b0d, ExplicitAuthFlows: []
AdminSetUserPassword "abc"  -> accepted
InitiateAuth USER_PASSWORD_AUTH -> signed in, access token iss: http://localhost:4566/us-east-1_834d13b0c

Floci signed alice in through a client that never allowed USER_PASSWORD_AUTH.
  On AWS this answers InvalidParameterException: USER_PASSWORD_AUTH flow not enabled for this client.
  Put ALLOW_USER_PASSWORD_AUTH (or the flow you use) in the client's ExplicitAuthFlows.
Floci accepted "abc" as a password. On AWS the default policy refuses it with InvalidPasswordException
  (8+ characters, upper, lower, digit and symbol). A green run here proves your calls, not your pool settings.

DeleteUserPool us-east-1_834d13b0c -> done
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**Each check is tested on its own.** If Floci ever refuses `abc`, the lab sets a policy-compliant password and still tries the sign-in, so the auth-flow check never hides behind the password check.

**Cleanup is one call.** `DeleteUserPool` in a `finally` removes the client and the user with the pool, and every run uses a fresh pool name, so running it twice gives the same result.

**Why this matters.** The Cognito calls are easy to get right. What breaks in production is pool configuration: a client missing the flow your app uses, or test fixtures with a password like `password` that the real policy refuses. Floci checks neither, so a suite that passes against Floci says nothing about your pool settings. Check them against AWS, or read them back from the pool you actually deploy. And if an API validates these tokens, take its expected issuer from the token rather than from the endpoint you configured.

## Try changing...

- Create the client with `ExplicitAuthFlows = ["ALLOW_USER_PASSWORD_AUTH"]`. The output shouldn't change on Floci, and that's the point.
- Give the pool a stricter `Policies.PasswordPolicy` (say `MinimumLength = 12`) and see whether Floci honours an explicit policy any better than the default.
- Create the user without `AdminSetUserPassword` but with a `TemporaryPassword`, then sign in. Floci returns a `NEW_PASSWORD_REQUIRED` challenge, as AWS does.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with sign-up, a wrong-password check, `GetUser` on the token, groups, redacted tokens and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/cognito). (Floci's Azure emulator has Microsoft Entra ID and its GCP emulator has Firebase Auth; the gallery doesn't have samples for them yet, so there's no other-cloud version of this one.)
