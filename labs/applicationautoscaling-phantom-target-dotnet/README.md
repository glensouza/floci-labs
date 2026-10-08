# Application Auto Scaling in .NET: autoscaling a table that does not exist

> Register a DynamoDB table's write capacity with Application Auto Scaling from a single C# file, give it a target-tracking policy, and read both back. The catch: the table does not exist. Floci registers it anyway, accepts a minimum above the maximum, and answers `UnsupportedOperation` for a scheduled action. Real AWS refuses the first two and accepts the third.

## What it shows

- Application Auto Scaling from .NET with the official `AWSSDK.ApplicationAutoScaling` package, and no Floci-specific library.
- **The API works like AWS:** `RegisterScalableTarget` stores the range, `DescribeScalableTargets` reads it back, a target-tracking `PutScalingPolicy` creates its two CloudWatch alarms, and deregistering a target that was never registered is refused with `ObjectNotFoundException`, as on AWS.
- **Resource ids are not looked up.** Real Application Auto Scaling answers `ValidationException` for a table that does not exist. Floci registers `table/lab-…` and lets a policy attach to it.
- **Capacity ranges are not checked.** Real Application Auto Scaling answers `ValidationException` when `MinCapacity` is above `MaxCapacity`. Floci accepts `10..1`.
- **Scheduled actions are not built.** `PutScheduledAction` answers HTTP 400 `UnsupportedOperation`, not a 501. Real AWS accepts the call.
- The lab prints what it saw for each one and says which way it went, so it tells you if Floci starts validating or builds scheduled actions.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Application Auto Scaling (with DynamoDB as the scalable dimension's namespace; no table is created)
- NuGet: `AWSSDK.ApplicationAutoScaling` 4.0.100.16, pinned in the `#:package` line at the top of `lab.cs`
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
Register a table that does not exist -> accepted
Read it back                         -> table/lab-e958b6e27fb4, 1..10
Target-tracking policy at 70%        -> 2 CloudWatch alarm(s) created with it
MinCapacity 10, MaxCapacity 1        -> accepted
Deregister a target never registered -> ObjectNotFoundException: No scalable target found for service namespace: dynamodb, resource ID: table/lab-e958b6e27fb4-gone, scalable dimension: dynamodb:table:WriteCapacityUnits
A nightly scheduled action           -> UnsupportedOperation: Operation PutScheduledAction is not supported.

Floci registered a table that does not exist and a minimum above the maximum; AWS refuses both.
  Code that relies on Application Auto Scaling to catch these passes here and is untested.
Scheduled actions are not built yet: Floci answers UnsupportedOperation.

Cleanup -> 2 target(s) deregistered, 0 policy(ies) left on the table
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**No table, on purpose.** Floci does not check that the resource exists, so the lab needs only the Application Auto Scaling package. Against real AWS the first register is refused; a real run needs the table created first, with `AWSSDK.DynamoDBv2` in provisioned mode.

**Deregistering cleans up the policy.** The `finally` deregisters every target the run registered, and then confirms that no policy is left on the table.

## Try changing...

- Register an ECS service (`service/<cluster>/<name>`, `ecs:service:DesiredCount`) or a Lambda alias instead of a table, and see whether Floci looks that one up.
- Add a step-scaling policy (`PolicyType.StepScaling`) and read it back with `DescribeScalingPolicies`.
- Ask `DescribeScalingActivities` what the policy has done, with no table behind it.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
