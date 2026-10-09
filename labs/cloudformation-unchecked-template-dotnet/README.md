# CloudFormation in .NET: a stack built from a resource type that does not exist

> Create a CloudFormation stack from a single C# file, and read it straight back. The catch: real CloudFormation refuses a template whose resource type it does not recognise, before it creates anything. Floci does not check resource types, so a stack whose one resource is `AWS::Nope::Thing` reads `CREATE_COMPLETE` the moment the call returns, with a physical ID to show for it. `ValidateTemplate` does not read the body either: text that is not a template passes.

## What it shows

- CloudFormation from .NET with the official `AWSSDK.CloudFormation` package, and no Floci-specific library.
- **Resource types are never checked.** A template whose one resource has the type `AWS::Nope::Thing` creates a stack that reads `CREATE_COMPLETE` on the first `DescribeStacks`, and `DescribeStackResources` lists the resource as `CREATE_COMPLETE` with a physical ID. On AWS, `CreateStack` answers `ValidationError` (Unrecognized resource types) and no stack exists, so a typo in a resource type passes here and fails on AWS.
- **`ValidateTemplate` does not read the body.** The text `this is not a template` validates, with zero parameters. On AWS it draws `ValidationError`.
- **No waiting.** Floci provisions a stack before `CreateStack` returns. Real CloudFormation reads `CREATE_IN_PROGRESS` for as long as the resources take.
- The lab prints what it saw for each one and says which way it went, so it tells you if Floci starts checking templates.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: CloudFormation
- NuGet: `AWSSDK.CloudFormation` 4.0.103, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the physical ID changes every run):

```text
Validate a body that is not a template  -> valid, 0 parameter(s)
A stack with type AWS::Nope::Thing      -> CREATE_COMPLETE
What the stack says it provisioned      -> Thing AWS::Nope::Thing CREATE_COMPLETE -> Thing-c952b120

Floci accepted a body that is not a template: ValidateTemplate does not read it.
Floci created a stack from a resource type that does not exist, and it read CREATE_COMPLETE at once.
  A typo in a resource type passes here and fails on AWS.

Cleanup -> removed the stack; still alive afterwards: none
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**A deleted stack stays in `ListStacks`.** It reads `DELETE_COMPLETE`, as on AWS, so the cleanup line counts only stacks in any other state.

**Don't run this against a real account as written.** On AWS, `CreateStack` refuses the template, so the lab prints the error. That part is harmless. But the lab never waits for a stack to settle, and real CloudFormation takes minutes.

## Try changing...

- Give the stack a real `AWS::SQS::Queue` beside the made-up resource, and check whether the queue exists afterwards with the SQS SDK.
- Call `DetectStackDrift` on the stack. Floci answers HTTP 400 `UnknownAction`, not a 501.
- Create a second stack under the same name. Floci answers `AlreadyExistsException`, as AWS does.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
