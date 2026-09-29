#:package AWSSDK.ElasticLoadBalancing@4.0.100.14

// A Classic Load Balancer against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// A classic load balancer names Availability Zones and registers EC2 instances by id, so a lab can
// run the whole thing without the EC2 package. The catch is what Floci does with those inputs: the
// lab creates one load balancer properly, then hands Floci three things AWS refuses (no zone, a zone
// that does not exist, and instance ids with no instance behind them) and prints what each one did,
// so it tells you if Floci's validation ever changes.

using Amazon.ElasticLoadBalancing;
using Amazon.ElasticLoadBalancing.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');
string region = "us-east-1";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
using AmazonElasticLoadBalancingClient elb = new AmazonElasticLoadBalancingClient(new BasicAWSCredentials("test", "test"), new AmazonElasticLoadBalancingConfig { ServiceURL = endpoint, AuthenticationRegion = region, MaxErrorRetry = 0 });

// Unique per run, so a crashed run never collides with the next (names max out at 32 characters).
string run = Guid.NewGuid().ToString("N")[..12];
List<string> created = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    string lbName = $"lab-ok-{run}";
    string? ok = await TryCreateAsync("zone us-east-1a  ", lbName, [$"{region}a"]);
    string? noZone = await TryCreateAsync("no zone          ", $"lab-none-{run}", []);
    string? badZone = await TryCreateAsync("zone mars-1z     ", $"lab-mars-{run}", ["mars-1z"]);
    Console.WriteLine();

    // An id in the right shape with nothing behind it, and a string that is not an id at all.
    // AWS answers InvalidInstance for both.
    string? badInstances = null;
    if (ok is null)
    {
        try
        {
            await elb.RegisterInstancesWithLoadBalancerAsync(new RegisterInstancesWithLoadBalancerRequest { LoadBalancerName = lbName, Instances = [new Instance { InstanceId = "i-0123456789abcdef0" }, new Instance { InstanceId = "not-an-instance" }] });
            Console.WriteLine("RegisterInstancesWithLoadBalancer (no such instances) -> accepted");

            DescribeInstanceHealthResponse health = await elb.DescribeInstanceHealthAsync(new DescribeInstanceHealthRequest { LoadBalancerName = lbName });
            foreach (InstanceState state in health.InstanceStates ?? [])
            {
                Console.WriteLine($"  {state.InstanceId}: {state.State} ({state.Description})");
            }
        }
        catch (AmazonElasticLoadBalancingException ex)
        {
            badInstances = ex.ErrorCode;
            Console.WriteLine($"RegisterInstancesWithLoadBalancer (no such instances) -> refused: {ex.ErrorCode}: {ex.Message}");
        }

        Console.WriteLine();
    }

    Console.WriteLine(ok is null
        ? "A load balancer in us-east-1a works: no EC2 package needed, the zone name is enough."
        : "A load balancer in us-east-1a was refused. Floci's zones may have changed.");

    Console.WriteLine(noZone is not null && badZone is not null
        ? "Floci validates zones like AWS: a missing zone and an unknown one are both refused."
        : "Floci accepted a load balancer with no zone or an unknown zone. AWS refuses both: do not\n  rely on Floci to catch a bad zone list.");

    Console.WriteLine(badInstances is not null
        ? "Floci refused instance ids with no instance behind them, as AWS does."
        : "Floci registered instance ids with no instance behind them. AWS answers InvalidInstance:\n  on AWS, register instances that exist.");
}
finally
{
    // Every load balancer the run made, including any Floci accepted that AWS would not have.
    foreach (string name in created)
    {
        await elb.DeleteLoadBalancerAsync(new DeleteLoadBalancerRequest { LoadBalancerName = name });
    }

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> DeleteLoadBalancer sent for {created.Count} name(s)");
}

// Returns the error code when the create is refused, or null when it succeeds.
async Task<string?> TryCreateAsync(string label, string name, List<string> zones)
{
    // Claimed before the call: DeleteLoadBalancer on a name that never existed succeeds, on Floci
    // and AWS alike, so cleaning up a refused create is harmless and a lost response cannot leak.
    created.Add(name);

    try
    {
        CreateLoadBalancerResponse response = await elb.CreateLoadBalancerAsync(new CreateLoadBalancerRequest
        {
            LoadBalancerName = name,
            Listeners = [new Listener { Protocol = "HTTP", LoadBalancerPort = 80, InstanceProtocol = "HTTP", InstancePort = 8080 }],
            AvailabilityZones = zones,
        });
        Console.WriteLine($"CreateLoadBalancer ({label}) -> {response.DNSName}");
        return null;
    }
    catch (AmazonElasticLoadBalancingException ex)
    {
        Console.WriteLine($"CreateLoadBalancer ({label}) -> refused: {ex.ErrorCode}: {ex.Message}");
        return ex.ErrorCode;
    }
}
