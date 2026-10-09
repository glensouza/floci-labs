# AppConfig in .NET: deploying a configuration version that does not exist

> Publish a feature-flag configuration to AWS AppConfig from a single C# file, check that version 9 does not exist, and then deploy version 9 anyway. Real AppConfig refuses a version the configuration profile does not have. Floci starts the deployment, records version 9, and reports it `COMPLETE`.

## What it shows

- AppConfig from .NET with the official `AWSSDK.AppConfig` package, and no Floci-specific library: an application, an environment, a hosted configuration profile and one hosted version, deployed with `AppConfig.AllAtOnce`, a strategy AppConfig ships with.
- **The version really is missing.** `GetHostedConfigurationVersion` for version 9 answers `ResourceNotFoundException`, as it should.
- **The deployment goes through anyway.** `StartDeployment` with `ConfigurationVersion = "9"` is accepted, and `GetDeployment` reads `version 9, COMPLETE`. Code that relies on AppConfig refusing a bad version number, such as a release script that passes the wrong number, never sees that refusal here.
- The lab prints what it saw and says which way it went, so it tells you if Floci starts validating the version.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AppConfig
- NuGet: `AWSSDK.AppConfig` 4.0.101.14, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the application name changes every run, and the deployment number counts up on each run against the same Floci):

```text
Application lab-4c7a9800f5ea, environment prod, hosted profile flags
Published hosted version                -> 1
GetHostedConfigurationVersion 9         -> ResourceNotFoundException
StartDeployment of version 9            -> deployment 1, version 9, COMPLETE

Floci deployed a configuration version that does not exist, and reported the deployment done.
  Code that relies on AppConfig refusing a bad version number never sees that refusal here.

Cleanup -> removed 1 version(s), the profile, not the environment (HTTP 404), the application; application still listed: no
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**`System.Environment`, spelled out.** `Amazon.AppConfig.Model` has a type called `Environment` too, so the plain name is ambiguous in a file that imports it.

**Cleanup leaves the environment behind.** Floci has not built `DeleteEnvironment` (HTTP 404), and deleting the application does not take it with it. Real AppConfig refuses to delete an application that still has environments, so on AWS the order matters; on Floci the leftover environment is harmless, but it stays until the container goes.

**Don't run this against a real account as written.** On AWS the deployment of version 9 is refused, so nothing is deployed, but the lab creates real resources and has no wait for a deployment still baking.

## Try changing...

- Deploy version 1, the one that exists. Floci reads `COMPLETE` at once. Real AppConfig reads `DEPLOYING`, then `BAKING` for `AllAtOnce`'s ten-minute bake.
- Run the lab twice against the same Floci and watch the deployment number. Each run makes a new environment, and on AWS each environment's deployments start at 1. On Floci 2.2.0 the second run's first deployment was number 2.
- Change the application's description with `UpdateApplication`. Floci answers HTTP 405 with no body, not a 501.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
