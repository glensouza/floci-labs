# Elastic Beanstalk in .NET: an environment that is Ready before it launched

> Create an Elastic Beanstalk application, a version and an environment from a single C# file, and read the environment straight back. The catch: real Elastic Beanstalk spends minutes in `Launching` while it starts instances. Floci launches nothing, so the environment reads `Ready` and `Green` the moment the create returns. Configuration templates answer `UnsupportedOperation`.

## What it shows

- Elastic Beanstalk from .NET with the official `AWSSDK.ElasticBeanstalk` package, and no Floci-specific library.
- **The resource model works like AWS:** `CreateApplication`, `CreateApplicationVersion`, `ListAvailableSolutionStacks` (four stacks on Floci 2.2.0), `CreateEnvironment`, `TerminateEnvironment` and `DeleteApplication` all work. Deleting an application that still has an environment is refused with `InvalidParameterValue`, as on AWS.
- **Nothing launches.** The environment reads `Ready`, `Green` on the first read after `CreateEnvironment`. On AWS it reads `Launching` (health `Grey`) for several minutes, so code that polls until the launch is done never polls here.
- **Configuration templates are not built.** `CreateConfigurationTemplate` answers HTTP 400 `UnsupportedOperation`, not a 501. Real AWS accepts the call. `DescribeEvents`, `ListTagsForResource` and `RestartAppServer` answer the same way.
- The lab prints what it saw for each one and says which way it went, so it tells you if Floci starts modelling the launch or builds templates.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Elastic Beanstalk
- NuGet: `AWSSDK.ElasticBeanstalk` 4.0.101.2, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (names change every run):

```text
Solution stacks on offer             -> 4, using 64bit Amazon Linux 2023 v6.5.0 running Node.js 22
The environment, right after create  -> Ready, Green
Save a configuration template        -> UnsupportedOperation: Operation CreateConfigurationTemplate is not supported.
Delete the app with the env running  -> InvalidParameterValue: Application lab-afe524183448 has active environments.

Floci launched nothing: the environment read Ready the moment it was created.
  Code that waits for Launching to end never waits here, and is untested.
Configuration templates are not built yet: Floci answers UnsupportedOperation.

Cleanup -> removed environment and application; 0 application(s) of this run left, the environment reads Terminated
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**No source bundle.** The version is only a label for the environment to point at. Floci stores it without one. On AWS, a version with no bundle deploys the platform's sample application.

**A terminated environment stays listed.** After `TerminateEnvironment`, `DescribeEnvironments` still returns the environment, with status `Terminated`, as AWS does for a while. The lab skips those when it looks for a live one.

**Don't run this against a real account as written.** On AWS the environment starts real EC2 capacity, and the `finally` terminates it without waiting, so the `DeleteApplication` that follows can be refused while the termination is still running. Wait for `Terminated` first.

## Try changing...

- Poll `DescribeEnvironments` until the status leaves `Launching`, the way a deploy script does, and count how many polls Floci needs.
- Call `UpdateEnvironment` with a second version label and read back which version the environment runs.
- Ask `DescribeConfigurationSettings` for a template name that was never saved, and see what Floci answers.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
