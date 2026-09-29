# Application Load Balancer in .NET: subnets without EC2, and the one bad request Floci lets through

> Create an AWS Application Load Balancer from a single C# file. An ALB needs subnets in two Availability Zones, and subnet ids normally come from EC2, which is a second SDK package just to look up two strings. Floci pre-creates a default VPC with deterministic subnet ids, so the lab names them directly. Floci checks those subnets the way AWS does, with one exception: it accepts a load balancer with no subnets at all.

## What it shows

- Elastic Load Balancing v2 from .NET with the official `AWSSDK.ElasticLoadBalancingV2` package, and no Floci-specific library.
- **Floci's default VPC has fixed ids.** `vpc-default-<region>`, with `subnet-default-<region>-a`, `-b` and `-c` in zones `a`, `b` and `c`. The lab passes `-a` and `-b` straight to `CreateLoadBalancer`, with no `DescribeSubnets` call and no EC2 package. None of these ids exist on AWS: there, pass your own two subnets.
- **Floci validates the subnet list like AWS in most cases.** A made-up subnet is refused with `SubnetNotFound`. One subnet is refused with `InvalidConfigurationRequest`, because an ALB needs two zones.
- **An empty subnet list gets through.** A load balancer with no subnets is created, in no VPC and no zones. AWS refuses that request. The lab tries all four cases and prints what happened to each, so it tells you if any of them changes.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Elastic Load Balancing v2 (Application Load Balancer)
- NuGet: `AWSSDK.ElasticLoadBalancingV2` 4.0.101.11, pinned in the `#:package` line at the top of `lab.cs`
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
CreateLoadBalancer (default subnets) -> provisioning, VPC 'vpc-default-us-east-1', zones [us-east-1a, us-east-1b]
CreateLoadBalancer (made-up subnets) -> refused: SubnetNotFound: The subnet ID 'subnet-0000000000000dead' does not exist
CreateLoadBalancer (one subnet     ) -> refused: InvalidConfigurationRequest: Application Load Balancers must be attached to subnets in at least two Availability Zones.
CreateLoadBalancer (no subnets     ) -> provisioning, VPC '', zones []

Floci's default subnets work as they are: no EC2 package needed to find them.
Floci validates subnets like AWS: an unknown id and a single zone are both refused.
A load balancer with NO subnets was accepted, with no VPC. AWS refuses this: an ALB needs
  subnets in two zones. Floci will not catch a missing subnet list.

Cleanup -> 2 load balancer(s) deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Then there's the subnet list, which is Floci's by necessity. That's the part to swap when the same code moves to AWS.

**Why not just call EC2?** You could: Floci's EC2 answers `DescribeSubnets` with the same three default subnets. In a real application that is the right move, and it costs one more package, `AWSSDK.EC2`. This lab keeps to one package to show that the load balancer itself needs nothing but ids, and to make it obvious which lines change for AWS.

**State is `provisioning`, then `active`.** `CreateLoadBalancer` answers `provisioning`, as AWS does, and a later `DescribeLoadBalancers` on Floci already says `active`. On AWS that takes minutes, so code that needs an active load balancer should wait with `DescribeLoadBalancers` rather than assume.

## Try changing...

- Use `subnet-default-us-east-1-c` in place of `-b`. (Still two zones, so it is accepted.)
- Pass `-a` twice. (Refused with `InvalidConfigurationRequest`: Floci counts zones, not ids.)
- Set `Type = LoadBalancerTypeEnum.Network` on the one-subnet request. (Accepted: a Network Load Balancer can live in a single zone.)

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a listener, a listener rule, a target group and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
