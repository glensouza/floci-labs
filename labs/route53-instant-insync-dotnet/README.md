# Route 53 in .NET: a hosted zone on localhost, and the change that's never PENDING

> Create a Route 53 hosted zone and an `A` record from a single C# file, then wait for the change the way production code has to. The catch: on Floci every change is already `INSYNC` in the response that creates it, so the wait you must write for AWS never waits here, and nothing tells you if it's missing.

## What it shows

- Route 53 from .NET with the official `AWSSDK.Route53` package, and no Floci-specific library.
- **The control plane works like AWS:** `CreateHostedZone`, `ChangeResourceRecordSets` (`UPSERT`, then `DELETE` repeating the record exactly), `ListResourceRecordSets` returning the zone's own `NS` and `SOA` records plus yours, and `DeleteHostedZone`.
- **Changes are never `PENDING`.** Real Route 53 answers `PENDING` while a change propagates, and it can take up to about a minute to reach `INSYNC`. Floci answers `INSYNC` in the create and `UPSERT` responses themselves, so the `GetChange` loop exits on the first poll. The lab prints which way it went, so it tells you if Floci starts modelling propagation.
- **Every zone gets the same four name servers**, placeholder `ns-N.awsdns-0N.*` names. On AWS each zone gets its own delegation set.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Route 53
- NuGet: `AWSSDK.Route53` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (ids and names change every run):

```text
CreateHostedZone         -> /hostedzone/ZBBIEZ4OZFIZ662, change INSYNC
  Name servers: ns-1.awsdns-01.org, ns-2.awsdns-02.net, ns-3.awsdns-03.com, ns-4.awsdns-04.co.uk
UPSERT www.e8d015a51fb34c818d69264190c0f879.lab.test. A -> change /change/C0Y1XA17T6H6ID INSYNC
GetChange                -> INSYNC after 1 poll(s), 0.0 s
ListResourceRecordSets   -> NS e8d015a51fb34c818d69264190c0f879.lab.test., SOA e8d015a51fb34c818d69264190c0f879.lab.test., A www.e8d015a51fb34c818d69264190c0f879.lab.test.

Every change came back INSYNC in the response itself. Real Route 53 answers PENDING here, so
  code that skips the GetChange wait passes on this Floci and races on AWS.

Cleanup -> record and hosted zone deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The `GetChange` loop is production code, not a lab workaround. Keep it when you point this at AWS.

**The zone gets a fresh name each run** under the reserved `.test` TLD, and the record points at `192.0.2.10` (TEST-NET-1), so nothing here can reach a real host. The `finally` deletes the record, then the zone, because a zone that still holds a record can't be deleted.

## Try changing...

- Change the `DELETE`'s TTL from 60 to 300 and see what Floci says. (It refuses with `InvalidChangeBatch`, as AWS does.)
- Run `CreateHostedZone` twice with the same `CallerReference` and see what the second call returns.
- Look the record up with DNS. Floci runs a DNS server on port 53 of its container, but it answers only for its own `*.localhost.floci.io` names. From a container on the same Docker network, `nslookup www.<zone> <floci-container-ip>` answers `NXDOMAIN`: the record is stored, but nothing serves it.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
