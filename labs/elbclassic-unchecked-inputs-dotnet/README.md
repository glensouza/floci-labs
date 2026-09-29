# Classic Load Balancer in .NET: no EC2 package, and three inputs Floci never checks

> Create an AWS Classic Load Balancer from a single C# file. A classic load balancer names Availability Zones rather than subnets, and registers EC2 instances by id, so the whole thing runs without the EC2 package. The catch is what Floci does with those inputs: it accepts a load balancer with no zone, a zone that doesn't exist, and instance ids with no instance behind them. AWS refuses all three.

## What it shows

- Classic Elastic Load Balancing from .NET with the official `AWSSDK.ElasticLoadBalancing` package, and no Floci-specific library.
- **The zone name is enough.** Unlike an Application Load Balancer, which needs subnet ids from EC2, a classic one takes `us-east-1a` directly. No lookup, no second package.
- **Floci doesn't validate zones.** A load balancer with no zone at all is created, and so is one in `mars-1z`. AWS refuses both.
- **Floci doesn't validate instance ids.** `i-0123456789abcdef0`, which has no instance behind it, is registered, and so is `not-an-instance`. AWS answers `InvalidInstance`. Both then report `OutOfService` with "Instance registration is still in progress." The lab tries every case and prints what happened to each, so it tells you if any of them changes.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Elastic Load Balancing (Classic Load Balancer)
- NuGet: `AWSSDK.ElasticLoadBalancing` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the name suffixes differ on every run):

```text
CreateLoadBalancer (zone us-east-1a  ) -> lab-ok-70a4614faca7-8854cff0de.elb.localhost.floci.io
CreateLoadBalancer (no zone          ) -> lab-none-70a4614faca7-3955e8980a.elb.localhost.floci.io
CreateLoadBalancer (zone mars-1z     ) -> lab-mars-70a4614faca7-5bd9c76460.elb.localhost.floci.io

RegisterInstancesWithLoadBalancer (no such instances) -> accepted
  i-0123456789abcdef0: OutOfService (Instance registration is still in progress.)
  not-an-instance: OutOfService (Instance registration is still in progress.)

A load balancer in us-east-1a works: no EC2 package needed, the zone name is enough.
Floci accepted a load balancer with no zone or an unknown zone. AWS refuses both: do not
  rely on Floci to catch a bad zone list.
Floci registered instance ids with no instance behind them. AWS answers InvalidInstance:
  on AWS, register instances that exist.

Cleanup -> DeleteLoadBalancer sent for 3 name(s)
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the code you would run against AWS, with real instance ids in place of the fake ones.

**Cleanup is claimed before each create.** `DeleteLoadBalancer` on a name that never existed succeeds, on Floci and on AWS, so the lab deletes every name it tried. A create whose response was lost still gets cleaned up, and a refused one costs nothing. The flip side: a successful delete proves nothing on its own. To know whether something was there, ask `DescribeLoadBalancers`, which answers `LoadBalancerNotFound`.

**Why this matters.** A test suite that passes against Floci proves your calls are shaped right. It doesn't prove your inputs are valid. Build the zone list or the instance list from configuration, get it wrong, and Floci won't tell you. AWS will, in production.

## Try changing...

- Register the same instance id twice, and see whether Floci's answer lists it once or twice.
- Call `DescribeLoadBalancers` for the `no zone` load balancer before cleanup, and print its `AvailabilityZones`.
- Call `DescribeInstanceHealth` again after a minute, and see whether the fake instances ever leave `OutOfService`.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a second listener, a health check, attributes, tags and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
- Compare with the gallery's [ELB v2 sample](https://github.com/glensouza/flocilab/tree/main/samples/aws/elbv2), where Floci *does* validate subnets. (The gallery has no load balancer sample on Floci's Azure, GCP or OCI emulators, so there's no other-cloud version of this one.)
