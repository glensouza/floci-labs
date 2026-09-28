# Simple Workflow (SWF) in .NET: the service with no delete, and a poll that doesn't wait

> Register an SWF domain from a single C# file, deprecate it, and try to register the same name again. Then poll an empty task list and time how long Floci makes you wait.

## What it shows

- SWF from .NET using the official `AWSSDK.SimpleWorkflow` package, and no Floci-specific library.
- **SWF has no delete.** `DeprecateDomain` is the whole teardown story, and a deprecated domain keeps its name: registering it again returns `DomainAlreadyExistsFault`, and `ListDomains` with `DEPRECATED` still lists it. Every run permanently spends one name. On real AWS that counts against a per-account quota of 100 domains, so this is the service to try out on an emulator first.
- **An empty poll returns instantly.** `PollForDecisionTask` on a task list with nothing on it comes back in well under a second with an empty task token. Real SWF long-polls, holding the connection for up to 60 seconds first. A worker loop that looks fine here becomes a hot loop against an instant empty answer, so put a delay in your own loop rather than relying on the server to provide one.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Simple Workflow Service (SWF)
- NuGet: `AWSSDK.SimpleWorkflow` 4.0.100.12, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output:

```text
RegisterDomain flocilab-lab-... -> ok
PollForDecisionTask on an empty task list -> returned after 0.0 s, token ""
  Real SWF long-polls for up to 60 s and then returns an empty token. ...

DeprecateDomain -> ok (the only teardown SWF has)
RegisterDomain with the same name again -> DomainAlreadyExistsFault
  Same as real SWF: a deprecated name is spent forever, and the per-account quota is 100 domains.
ListDomains DEPRECATED -> still lists flocilab-lab-...
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**There is no cleanup step, on purpose.** Nothing can be deleted. The domain name carries a fresh GUID, so re-runs never collide, and each run leaves one deprecated domain behind. Restart the Floci container to clear them locally. Against real AWS, don't run this lab.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials, and `MaxErrorRetry = 0`.

## Try changing...

- Start a workflow execution on the domain before deprecating it, then poll for its decision task and answer it with `CompleteWorkflowExecution`.
- Poll a task list that has an execution scheduled on it, and compare the timing with the empty one.
- Deprecate a domain that is already deprecated and see what SWF says.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
