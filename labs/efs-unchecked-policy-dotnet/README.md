# EFS in .NET: the policy that isn't JSON

> Create an encrypted Amazon EFS file system from a single C# file, give it a resource policy and mount targets, and check the rules a file system lives by. Floci enforces the two that bite in day-to-day code: a replayed creation token, and a delete refused while a mount target remains. Then send three things real EFS refuses: a resource policy that isn't JSON, provisioned throughput with no figure, and two mount targets in one Availability Zone. Floci accepts all three.

## What it shows

- EFS from .NET with the official `AWSSDK.ElasticFileSystem` package, and no Floci-specific library: `CreateFileSystem`, `PutFileSystemPolicy`, `DescribeFileSystemPolicy`, `CreateMountTarget`, `DeleteMountTarget`, `DeleteFileSystem`.
- **The rules Floci enforces.** A second `CreateFileSystem` with the same `CreationToken` answers `FileSystemAlreadyExists`, naming the existing file system's id, which is how a caller that lost its response finds what it made. `DeleteFileSystem` with a mount target in place answers `FileSystemInUse`.
- **A policy that isn't JSON is stored.** `PutFileSystemPolicy` with the string `not json` succeeds, and `DescribeFileSystemPolicy` reads back `not json`. The SDK documents `InvalidPolicyException` as "Returned if the FileSystemPolicy is malformed", so on AWS that call fails. A typo in a policy template passes every local run; AWS's own documentation says it would not pass there.
- **Provisioned throughput with no figure is created.** The SDK documents `ProvisionedThroughputInMibps` as "Required if ThroughputMode is set to provisioned". Floci creates the file system with none.
- **Two mount targets land in one Availability Zone.** EFS allows "one mount target in each Availability Zone in your VPC". Floci puts both made-up subnets in `us-east-1a`, at the same address, `10.0.0.10`. It does refuse a second mount target in the *same subnet* (`MountTargetConflict`).
- The lab prints what it saw and says which way it went, so it tells you if Floci changes.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: EFS
- NuGet: `AWSSDK.ElasticFileSystem` 4.0.100.16, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the ids change every run):

```text
File system                              -> fs-59d817776d0545629, available, encrypted True
Same creation token again                -> FileSystemAlreadyExists, naming fs-59d817776d0545629
PutFileSystemPolicy "not json"           -> accepted, reads back "not json"
Provisioned throughput, no figure        -> created fs-28f806b00bdb4aefb, provisioned at no MiB/s
Mount target in subnet-aaaaaaaa          -> fsmt-d5a24c35d4454e569, us-east-1a, 10.0.0.10
Mount target in subnet-bbbbbbbb          -> fsmt-e0c3a33d88c44711b, us-east-1a, 10.0.0.10 (the same zone)
DeleteFileSystem, mount targets in place -> FileSystemInUse

Floci accepted all three that real EFS refuses. A policy Floci
  stores, EFS answers with InvalidPolicyException, so parse the JSON yourself before you
  send it, and don't let the emulator's "yes" stand in for the service's rules.

Cleanup -> removed 2 file system(s) and their mount targets; still listed: 0
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**The subnets are made up.** Floci never looks them up, which is what lets this lab run with no VPC. Real EFS needs real subnet ids in one VPC.

**Everything is `available` at once.** Real EFS holds a file system in `creating` and a mount target in `creating` or `deleting` for a while, and refuses the next call until they settle. Code that works here without waiting will not work on AWS; poll `DescribeFileSystems` and `DescribeMountTargets` for the state.

**Don't run this against a real account as written.** It creates real resources and, because Floci answers at once, it doesn't wait between steps. It was verified against Floci only.

## Try changing...

- Put a second mount target in `subnet-aaaaaaaa`. Floci refuses it with `MountTargetConflict`, as EFS does for one subnet.
- Put a valid policy instead of `not json` (a `Deny` on `aws:SecureTransport` = `false` is the one AWS recommends first), and read it back.
- Delete the file system after the mount targets but with an access point still in place. Floci 2.2.0 deletes it.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
