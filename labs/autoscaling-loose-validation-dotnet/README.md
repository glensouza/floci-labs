# EC2 Auto Scaling in .NET: the requests Floci never refuses

> Build an EC2 Auto Scaling launch configuration and a group from a single C# file, then send four requests AWS has a firm answer to. The catch: Floci refuses the duplicate name like AWS does, but accepts a minimum above the maximum, a launch configuration that does not exist, and deleting a launch configuration a group still uses, which leaves the group pointing at nothing.

## What it shows

- EC2 Auto Scaling from .NET with the official `AWSSDK.AutoScaling` package, and no Floci-specific library.
- **The API works like AWS:** `CreateLaunchConfiguration` and `CreateAutoScalingGroup` store what you send, `DescribeAutoScalingGroups` reads it back, and a second group with a name that already exists is refused with `AlreadyExists`, as on AWS.
- **Size ranges are not checked.** Real Auto Scaling answers `ValidationError` when `MinSize` is above `MaxSize`. Floci accepts `MinSize 1, MaxSize 0`.
- **Launch configurations are not looked up.** Real Auto Scaling answers `ValidationError` for a group that names a launch configuration it cannot find. Floci accepts `no-such-launch-configuration`.
- **A launch configuration in use can be deleted.** Real Auto Scaling answers `ResourceInUse` while any group names it. Floci deletes it, and the group still names a launch configuration that no longer exists.
- The lab prints what it saw for each one and says which way it went, so it tells you if Floci starts validating.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: EC2 Auto Scaling
- NuGet: `AWSSDK.AutoScaling` 4.0.104.3, pinned in the `#:package` line at the top of `lab.cs`
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
CreateLaunchConfiguration          -> lab-0a860841c60d-lc
CreateAutoScalingGroup 0..2        -> accepted
Same name again                    -> AlreadyExists: Auto Scaling group 'lab-0a860841c60d' already exists.
MinSize 1, MaxSize 0               -> accepted
Launch configuration that is gone  -> accepted
Delete the launch config in use    -> accepted
The group's launch configuration   -> lab-0a860841c60d-lc (no longer exists)

Floci accepted all three requests AWS refuses: the size range, the missing launch configuration
  and the delete of one in use. Code that relies on Auto Scaling to catch these passes here and is untested.

Cleanup -> 3 resource(s) deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**Every group wants zero instances, on purpose.** Floci's Auto Scaling is not just a record: a reconciler checks each group every 10 seconds and launches a real EC2 instance, a Docker container started through the Docker socket, for every instance the group wants. That is also why the invalid range here is `1..0` with a desired capacity of zero. Floci accepts `3..2` too, then sets the desired capacity to 3 and starts three containers.

**Launch configurations, not launch templates.** Launch configurations are what Floci documents, and they need only the Auto Scaling package. AWS no longer lets accounts created after 2024-10-01 create them, so on a new real account this lab's first call is refused. A launch template is an EC2 call (`AWSSDK.EC2`).

**Each run uses fresh names.** The `finally` force-deletes every group the run created, including the two AWS would have refused, and the launch configuration if it is still there.

## Try changing...

- Give the main group a `DesiredCapacity` of 1 and mount the Docker socket (`-v /var/run/docker.sock:/var/run/docker.sock`). Within about twenty seconds `DescribeAutoScalingGroups` lists an `InService` instance, and `docker ps` shows its container. The force delete in the `finally` removes it.
- Add a `PutScalingPolicy` with a target-tracking configuration and read it back with `DescribePolicies`.
- Suspend `AZRebalance` with `SuspendProcesses`, and read the group's `SuspendedProcesses`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
