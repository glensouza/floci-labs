# Lightsail in .NET: an instance that is running before it boots

> Create an Amazon Lightsail instance from a single C# file, wait for it to run the way production code has to, then try to reach it. The catch: on Floci the instance is already `running` the moment you create it, and nothing answers on its public address, because Floci keeps the record of a virtual machine, not the machine.

## What it shows

- Lightsail from .NET with the official `AWSSDK.Lightsail` package, and no Floci-specific library.
- **The API works like AWS:** `CreateInstances` returns a `CreateInstance` operation, `GetInstanceState` and `GetInstance` read the instance back with a public and a private address and the blueprint's login user, and `DeleteInstance` removes it. A second instance with a name that already exists is refused with `InvalidInputException`, as on AWS.
- **Instances are never `pending`.** Real Lightsail answers `pending` while the VM boots, typically for a minute or so. Floci answers `running` on the first `GetInstanceState`, so the wait loop exits on its first poll. The lab prints which way it went, so it tells you if Floci starts modelling boot.
- **Nothing is behind the address.** The public address is in `203.0.113.0/24`, a range reserved for documentation (RFC 5737), and a TCP connection to port 22 gets no answer. You can test the code that manages instances, not the code that talks to them.
- **The blueprint id is not checked.** Real Lightsail refuses a `BlueprintId` that `GetBlueprints` does not list. Floci accepts `no-such-blueprint` and creates the instance.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Lightsail
- NuGet: `AWSSDK.Lightsail` 4.0.102.1, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (names and addresses change every run; the TCP check takes 3 seconds to give up):

```text
CreateInstances      -> CreateInstance lab-71d852bdb480: Succeeded
Wait for running     -> running after 1 poll(s), 0.0 s (first read: running)
GetInstance          -> public 203.0.113.83, private 10.0.0.83, login ec2-user
TCP 203.0.113.83:22 -> no answer (TimeoutException)
Blueprint "no-such-blueprint" -> accepted

The instance was running on the first read, and nothing answers on its address. Floci keeps
  the record, not a machine: code that skips the wait, or that needs to reach the instance,
  passes here and is untested.

Cleanup -> 2 instance(s) deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The wait for `running` is production code, not a lab workaround. Keep it when you point this at AWS, where the instance also costs money from the moment it is created.

**Each run uses fresh instance names.** The `finally` deletes every instance the run created, including the one made from a blueprint that does not exist.

## Try changing...

- Remove the wait loop entirely. The lab still passes on Floci, which is the point: on AWS, an SSH or HTTP call made straight after `CreateInstances` finds a machine that is still booting.
- Create the same instance name twice and read the error. (Floci refuses it: `InvalidInputException: Instance … already exists`.)
- Call `CreateInstanceSnapshot`. (Floci answers HTTP 400 `UnsupportedOperation`: recognised, not implemented.)

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
