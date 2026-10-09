# AppConfigData in .NET: the poll that comes back empty

> Deploy a feature flag with AWS AppConfig, then read it from a single C# file the way an application does: open a session, poll, poll again. The second poll is 0 bytes, because nothing changed, so a cache that stores every body ends up holding nothing. Floci gets that right, and answers the second poll at once even though the session asked for 60 seconds between polls.

## What it shows

- AppConfigData from .NET with the official `AWSSDK.AppConfigData` package, and no Floci-specific library: `StartConfigurationSession`, then `GetLatestConfiguration` with the token each poll hands back.
- **An unchanged configuration is an empty body.** The first poll returns the 20-byte flag document and version `1`. The next poll returns 0 bytes and an empty version label, as AWS documents: it means "you already have it". Code that writes every poll's body into its cache blanks its flags. Keep the last non-empty one.
- **Floci does not enforce the poll interval.** The session asks for `RequiredMinimumPollIntervalInSeconds = 60`, the first poll answers `next poll in 60 s`, and the second poll, 0 s later, is answered anyway. AWS documents that a session can't poll more often than its interval, so a tight polling loop that works here does not carry over.
- **A spent token is refused.** Reusing the token the first poll already used answers HTTP 400 `BadRequestException`. Each token is valid for one call, so always keep the newest.
- The lab prints what it saw and says which way it went, so it tells you if Floci changes.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AppConfig, AppConfigData
- NuGet: `AWSSDK.AppConfigData` 4.0.100.17 and `AWSSDK.AppConfig` 4.0.101.14, pinned in the `#:package` lines at the top of `lab.cs`. Two packages because AppConfigData only reads a configuration that has been deployed, and deploying one is AppConfig, a separate SDK.
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

Expected output (the application name changes every run):

```text
Application lab-fd6c93f30990: version 1 deployed to prod   -> COMPLETE
StartConfigurationSession, 60 s minimum interval -> token
Poll 1                                   -> 20 bytes, version '1', next poll in 60 s
Poll 2, 0 s later, nothing changed       -> 0 bytes, version ''
A cache that stores every body now holds -> ''
Poll with the token poll 1 already used  -> HTTP 400 BadRequestException

An unchanged configuration comes back as an empty body, as AWS documents: it means "you already have it".
  Keep the last non-empty body; overwriting it with this one blanks your flags.
Floci answered a poll 0 s into a 60 s minimum interval. AWS documents that a session can't poll more often than that,
  so a tight polling loop that works here does not carry over.

Cleanup -> removed 1 version(s), the profile, not the environment (HTTP 404), the application; application still listed: no
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines per client know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**`System.Environment`, spelled out.** `Amazon.AppConfig.Model` has a type called `Environment` too, so the plain name is ambiguous in a file that imports it.

**Cleanup leaves the environment behind.** Floci has not built `DeleteEnvironment` (HTTP 404, `UnknownOperationException`). It is harmless, and it stays until the container goes.

**Don't run this against a real account as written.** It creates and deploys real resources, an `AllAtOnce` deployment bakes for ten minutes on AWS, and the second poll comes inside the session's interval.

## Try changing...

- Publish and deploy a version 2 between the two polls. The second poll then returns the new document and version `2`, which is the only time a poll after the first has a body.
- Lower `PollInterval` to `14`. AWS documents 15 as the minimum, and the SDK sends whatever you give it, so see what Floci makes of it.
- Keep polling with the newest token in a loop, with no delay. Floci keeps answering.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
