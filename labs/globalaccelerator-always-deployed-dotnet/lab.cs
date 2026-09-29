#:package AWSSDK.GlobalAccelerator@4.0.100.14

// An AWS Global Accelerator against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// On AWS, every change to an accelerator (or to a listener or endpoint group under it) leaves it
// IN_PROGRESS for minutes, and the next change is refused with TransactionInProgressException until
// it is DEPLOYED again. So real code waits after every change. This lab makes five changes back to
// back with no waiting, reads the status straight after each one, and prints what Floci said, so it
// tells you if Floci ever starts simulating the delay.

using Amazon.GlobalAccelerator;
using Amazon.GlobalAccelerator.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Global Accelerator's control plane lives in us-west-2 only. Floci parses SigV4 but does not
// verify it, so any well-formed pair works. MaxErrorRetry = 0 makes a failure show at once instead
// of after ~8 s of retries; against real AWS, keep retries.
using AmazonGlobalAcceleratorClient ga = new AmazonGlobalAcceleratorClient(new BasicAWSCredentials("test", "test"), new AmazonGlobalAcceleratorConfig { ServiceURL = endpoint, AuthenticationRegion = "us-west-2", MaxErrorRetry = 0 });

// An endpoint id in the right shape with nothing behind it. Floci accepts it; on AWS, use a real
// ALB, NLB, EC2 instance or Elastic IP in the endpoint group's region.
const string EndpointId = "arn:aws:elasticloadbalancing:us-east-1:000000000000:loadbalancer/app/lab/0123456789abcdef";

string name = $"lab-ga-{Guid.NewGuid().ToString("N")[..12]}";
string? acceleratorArn = null;
int deployedAtOnce = 0;
int changes = 0;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    CreateAcceleratorResponse accelerator = await ga.CreateAcceleratorAsync(new CreateAcceleratorRequest { Name = name, Enabled = true, IdempotencyToken = Guid.NewGuid().ToString() });
    acceleratorArn = accelerator.Accelerator.AcceleratorArn;
    await ReportAsync("CreateAccelerator        ");

    CreateListenerResponse listener = await ga.CreateListenerAsync(new CreateListenerRequest { AcceleratorArn = acceleratorArn, Protocol = Protocol.TCP, PortRanges = [new PortRange { FromPort = 80, ToPort = 80 }], IdempotencyToken = Guid.NewGuid().ToString() });
    await ReportAsync("CreateListener           ");

    CreateEndpointGroupResponse group = await ga.CreateEndpointGroupAsync(new CreateEndpointGroupRequest { ListenerArn = listener.Listener.ListenerArn, EndpointGroupRegion = "us-east-1", EndpointConfigurations = [new EndpointConfiguration { EndpointId = EndpointId, Weight = 10 }], IdempotencyToken = Guid.NewGuid().ToString() });
    await ReportAsync("CreateEndpointGroup      ");

    await ga.UpdateEndpointGroupAsync(new UpdateEndpointGroupRequest { EndpointGroupArn = group.EndpointGroup.EndpointGroupArn, TrafficDialPercentage = 25 });
    await ReportAsync("UpdateEndpointGroup      ");

    await ga.UpdateAcceleratorAsync(new UpdateAcceleratorRequest { AcceleratorArn = acceleratorArn, Enabled = false });
    await ReportAsync("UpdateAccelerator disable");

    Console.WriteLine();
    Console.WriteLine(deployedAtOnce == changes
        ? $"Floci reported DEPLOYED straight after all {changes} changes, and accepted each one without\n  waiting. On AWS each of these leaves the accelerator IN_PROGRESS for minutes, and a change sent\n  before it settles is refused with TransactionInProgressException. A wait loop in your code never\n  waits on Floci: test it against AWS, or with a fake that returns IN_PROGRESS."
        : $"Floci reported IN_PROGRESS after {changes - deployedAtOnce} of {changes} changes. Floci now simulates the\n  deployment delay: your wait loop runs here too.");
}
catch (TransactionInProgressException ex)
{
    Console.WriteLine($"Refused: TransactionInProgressException: {ex.Message}");
    Console.WriteLine("Floci now refuses a change while the last one deploys, as AWS does: wait for DEPLOYED.");
}
finally
{
    // Bottom-up, as Global Accelerator insists: endpoint groups, listeners, then the accelerator
    // once it is disabled. The ARN is server-minted, so a lost create response would leave nothing
    // to delete by; a real app would find the accelerator by its unique name instead.
    if (acceleratorArn is not null)
    {
        foreach (Listener l in (await ga.ListListenersAsync(new ListListenersRequest { AcceleratorArn = acceleratorArn })).Listeners ?? [])
        {
            foreach (EndpointGroup g in (await ga.ListEndpointGroupsAsync(new ListEndpointGroupsRequest { ListenerArn = l.ListenerArn })).EndpointGroups ?? [])
            {
                await ga.DeleteEndpointGroupAsync(new DeleteEndpointGroupRequest { EndpointGroupArn = g.EndpointGroupArn });
            }

            await ga.DeleteListenerAsync(new DeleteListenerRequest { ListenerArn = l.ListenerArn });
        }

        await ga.UpdateAcceleratorAsync(new UpdateAcceleratorRequest { AcceleratorArn = acceleratorArn, Enabled = false });
        await ga.DeleteAcceleratorAsync(new DeleteAcceleratorRequest { AcceleratorArn = acceleratorArn });

        Console.WriteLine();
        Console.WriteLine($"Cleanup -> deleted {name}");
    }
}

// Reads the status once, immediately, with no polling: the point is what the very next call sees.
async Task ReportAsync(string label)
{
    changes++;
    DescribeAcceleratorResponse response = await ga.DescribeAcceleratorAsync(new DescribeAcceleratorRequest { AcceleratorArn = acceleratorArn });
    if (response.Accelerator.Status == AcceleratorStatus.DEPLOYED)
    {
        deployedAtOnce++;
    }

    Console.WriteLine($"{label} -> status {response.Accelerator.Status}");
}
