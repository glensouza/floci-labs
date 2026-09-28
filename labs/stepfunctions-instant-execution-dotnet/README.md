# Step Functions in .NET: an execution that is already SUCCEEDED when you first look

> Start a one-state Step Functions execution from a single C# file and read its status straight away. Then start one with a 3-second `Wait` state and compare.

## What it shows

- Step Functions from .NET using the official `AWSSDK.StepFunctions` package, and no Floci-specific library.
- **A quick execution is finished before you can look.** With a single `Pass` state, the first `DescribeExecution` after `StartExecution` already says `SUCCEEDED`. Real Step Functions returns from `StartExecution` while the execution is still `RUNNING`, so code that reads the status once and stops passes here and fails on AWS. Always poll to a terminal status.
- **A `Wait` state makes it asynchronous.** With a 3-second `Wait`, the first read says `RUNNING` and the execution settles as `SUCCEEDED` about 3 seconds later. Use a `Wait` in your own tests when you want the poll loop exercised.
- **What create checks, and what it doesn't.** `RoleArn = "not-an-arn"` comes back `InvalidArn`, and a malformed or schema-invalid definition comes back `InvalidDefinition`. But a well-formed ARN for a role that has never existed is accepted. Real Step Functions rejects a role it cannot assume, so a definition that runs here can still fail on deploy, purely on IAM.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Step Functions
- NuGet: `AWSSDK.StepFunctions` 4.0.100.13, pinned in the `#:package` line at the top of `lab.cs`
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
StartExecution (one Pass state), then DescribeExecution straight away
  Status: SUCCEEDED, Output: "ok"
  Already finished. Real Step Functions returns from StartExecution while the execution is still
  RUNNING, so code that reads one status here and stops will pass locally and fail on AWS.

StartExecution (a 3 s Wait state, then Pass), then DescribeExecution straight away
  First read: RUNNING
  Settled as SUCCEEDED after 3.1 s
  So Floci is asynchronous when the definition takes time; only work that finishes instantly beats the read.

CreateStateMachine with RoleArn "not-an-arn" -> InvalidArn
CreateStateMachine with a malformed definition -> InvalidDefinition: ...
CreateStateMachine with a role that does not exist -> accepted (that is the RoleArn every call above used)
  ...

Cleanup -> state machines deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**The role ARN is made up.** Floci checks its shape but not that the role exists. Against AWS it has to exist and trust `states.amazonaws.com`.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials, and `MaxErrorRetry = 0`, plus the made-up role ARN. Against AWS, drop the credentials and keep the retries.

**Names are unique per run**, and the `finally` deletes both state machines, so re-runs are idempotent.

## Try changing...

- Lengthen the `Wait` to 20 seconds and watch the poll loop run for real.
- Add a `Task` state with a `Retry` block that points at a Lambda you make fail, and see what `DescribeExecution` reports.
- Start two executions with the same `Name` and compare the error with real Step Functions' `ExecutionAlreadyExists`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
