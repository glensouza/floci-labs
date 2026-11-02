# Application Load Balancer in .NET: subnets without EC2

> Create an AWS Application Load Balancer from a single C# file. An ALB needs subnets in two Availability Zones, and subnet ids normally come from EC2, which is a second SDK package just to look up two strings. Floci pre-creates a default VPC with deterministic subnet ids, so the lab names them directly. Floci 2.4.0 checks the subnet list the way AWS does: a made-up subnet, a single subnet and an empty list are all refused.

## What it shows

- Elastic Load Balancing v2 from .NET with the official `AWSSDK.ElasticLoadBalancingV2` package, and no Floci-specific library.
- **Floci's default VPC has fixed ids.** `vpc-default-us-east-1`, with `subnet-default-us-east-1-a` and `-b` in zones `us-east-1a` and `us-east-1b`. The lab passes those two straight to `CreateLoadBalancer`, with no `DescribeSubnets` call and no EC2 package. None of these ids exist on AWS: there, pass your own two subnets.
- **Floci validates the subnet list like AWS.** A made-up subnet is refused with `SubnetNotFound`. One subnet, and no subnets at all, are both refused with `ValidationError`: "At least two subnets in two different Availability Zones must be specified".
- The lab tries all four cases and prints what happened to each, so it tells you if any of them changes. An earlier version of this lab, run on Floci 2.1.0, saw a load balancer with no subnets accepted; Floci 2.4.0 refuses it.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Elastic Load Balancing v2 (Application Load Balancer)
- NuGet: `AWSSDK.ElasticLoadBalancingV2` 4.0.101.11, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.4.0 (`floci/floci:latest`, November 2026)

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
CreateLoadBalancer (one subnet     ) -> refused: ValidationError: At least two subnets in two different Availability Zones must be specified
CreateLoadBalancer (no subnets     ) -> refused: ValidationError: At least two subnets in two different Availability Zones must be specified

Floci's default subnets work as they are: no EC2 package needed to find them.
Floci validates subnets like AWS: an unknown id and a single zone are both refused.
A load balancer with no subnets was refused, as AWS refuses it.

Cleanup -> 1 load balancer(s) deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Then there's the subnet list, which is Floci's by necessity. That's the part to swap when the same code moves to AWS.

**Why not just call EC2?** In a real application that is the right move, and it costs one more package, `AWSSDK.EC2`. This lab keeps to one package to show that the load balancer itself needs nothing but ids, and to make it obvious which lines change for AWS.

**Cleanup is in a `finally`.** Every load balancer the run created is deleted, whichever cases succeeded.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a listener, a listener rule, a target group and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
