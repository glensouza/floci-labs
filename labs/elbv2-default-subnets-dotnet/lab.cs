#:package AWSSDK.ElasticLoadBalancingV2@4.0.101.11

// An Application Load Balancer against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// An ALB cannot exist without subnets in two Availability Zones, and subnet ids come from EC2 —
// a second SDK package just to look two strings up. Floci pre-creates a default VPC with one
// subnet per zone under deterministic ids, so the lab names them directly. It then asks for a
// load balancer four ways (Floci's default subnets, a made-up subnet, one subnet, no subnets) and
// prints what each attempt did, so it tells you if Floci's validation ever changes.

using Amazon.ElasticLoadBalancingV2;
using Amazon.ElasticLoadBalancingV2.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');
string region = "us-east-1";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
using AmazonElasticLoadBalancingV2Client elb = new AmazonElasticLoadBalancingV2Client(new BasicAWSCredentials("test", "test"), new AmazonElasticLoadBalancingV2Config { ServiceURL = endpoint, AuthenticationRegion = region, MaxErrorRetry = 0 });

// Floci's default VPC is vpc-default-<region>, with subnet-default-<region>-a, -b and -c in zones
// a, b and c. None of these exist on AWS: there, pass your own two subnets.
List<string> defaultSubnets = [$"subnet-default-{region}-a", $"subnet-default-{region}-b"];

// Unique per run, so a crashed run never collides with the next (names max out at 32 characters).
string run = Guid.NewGuid().ToString("N")[..12];
List<string> created = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    string? withDefaults = await TryCreateAsync("default subnets", $"lab-ok-{run}", defaultSubnets);
    string? madeUp = await TryCreateAsync("made-up subnets", $"lab-bad-{run}", ["subnet-0000000000000dead", "subnet-0000000000000beef"]);
    string? oneZone = await TryCreateAsync("one subnet     ", $"lab-one-{run}", [defaultSubnets[0]]);
    string? noSubnets = await TryCreateAsync("no subnets     ", $"lab-none-{run}", []);
    Console.WriteLine();

    Console.WriteLine(withDefaults is not null
        ? "Floci's default subnets were refused. Its default VPC ids may have changed: list them with\n  EC2 DescribeSubnets and update defaultSubnets."
        : "Floci's default subnets work as they are: no EC2 package needed to find them.");

    Console.WriteLine(madeUp is not null && oneZone is not null
        ? "Floci validates subnets like AWS: an unknown id and a single zone are both refused."
        : "Floci accepted a made-up subnet or a single zone, which AWS refuses. Do not rely on Floci\n  to catch a bad subnet list.");

    Console.WriteLine(noSubnets is not null
        ? "A load balancer with no subnets was refused, as AWS refuses it."
        : "A load balancer with NO subnets was accepted, with no VPC. AWS refuses this: an ALB needs\n  subnets in two zones. Floci will not catch a missing subnet list.");
}
finally
{
    // Every load balancer the run made, including any Floci accepted that AWS would not have.
    foreach (string arn in created)
    {
        await elb.DeleteLoadBalancerAsync(new DeleteLoadBalancerRequest { LoadBalancerArn = arn });
    }

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> {created.Count} load balancer(s) deleted");
}

// Returns the error code when the create is refused, or null when it succeeds.
async Task<string?> TryCreateAsync(string label, string name, List<string> subnets)
{
    try
    {
        CreateLoadBalancerResponse response = await elb.CreateLoadBalancerAsync(new CreateLoadBalancerRequest { Name = name, Type = LoadBalancerTypeEnum.Application, Subnets = subnets });
        LoadBalancer lb = response.LoadBalancers[0];
        created.Add(lb.LoadBalancerArn);
        string zones = string.Join(", ", (lb.AvailabilityZones ?? []).Select(z => z.ZoneName));
        Console.WriteLine($"CreateLoadBalancer ({label}) -> {lb.State?.Code}, VPC '{lb.VpcId}', zones [{zones}]");
        return null;
    }
    catch (AmazonElasticLoadBalancingV2Exception ex)
    {
        Console.WriteLine($"CreateLoadBalancer ({label}) -> refused: {ex.ErrorCode}: {ex.Message}");
        return ex.ErrorCode;
    }
}
