#:package AWSSDK.AutoScaling@4.0.104.3

// EC2 Auto Scaling against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Builds a launch configuration and a group of zero instances, then sends four requests that real
// Auto Scaling has a firm answer to: a duplicate group name, a minimum above the maximum, a launch
// configuration that does not exist, and deleting a launch configuration a group still uses. AWS
// refuses all four. The lab prints what this Floci answered, so it tells you if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.AutoScaling;
using Amazon.AutoScaling.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonAutoScalingClient autoscaling = new AmazonAutoScalingClient(credentials, new AmazonAutoScalingConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A fresh name per run means a crashed run never collides with the next one.
string run = Guid.NewGuid().ToString("N")[..12];
string launchConfig = $"lab-{run}-lc";
string group = $"lab-{run}";
string oddRange = $"lab-{run}-range";
string oddLaunch = $"lab-{run}-nolc";
List<string> groups = [];
int accepted = 0;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

// Every group here keeps its desired capacity at zero. Floci runs a real reconciler: a group that
// wants instances gets real EC2 containers, started through the Docker socket.
async Task<string> TryCreateGroupAsync(string name, string lc, int min, int max)
{
    groups.Add(name);
    try
    {
        await autoscaling.CreateAutoScalingGroupAsync(new CreateAutoScalingGroupRequest
        {
            AutoScalingGroupName = name,
            LaunchConfigurationName = lc,
            MinSize = min,
            MaxSize = max,
            DesiredCapacity = 0,
            AvailabilityZones = ["us-east-1a"],
        });
        return "accepted";
    }
    catch (AmazonAutoScalingException ex)
    {
        return $"{ex.ErrorCode}: {ex.Message}";
    }
}

try
{
    await autoscaling.CreateLaunchConfigurationAsync(new CreateLaunchConfigurationRequest { LaunchConfigurationName = launchConfig, ImageId = "ami-12345678", InstanceType = "t3.micro" });
    Console.WriteLine($"CreateLaunchConfiguration          -> {launchConfig}");

    Console.WriteLine($"CreateAutoScalingGroup 0..2        -> {await TryCreateGroupAsync(group, launchConfig, 0, 2)}");

    // AWS: AlreadyExists. Taken off the cleanup list first, since the attempt puts the name back on it.
    groups.Remove(group);
    string duplicate = await TryCreateGroupAsync(group, launchConfig, 0, 2);
    Console.WriteLine($"Same name again                    -> {duplicate}");

    // AWS: ValidationError. 1..0 rather than, say, 3..2: Floci would accept 3..2 as well, set the
    // desired capacity to 3 and start three containers.
    string range = await TryCreateGroupAsync(oddRange, launchConfig, 1, 0);
    Console.WriteLine($"MinSize 1, MaxSize 0               -> {range}");

    // AWS: ValidationError, because the launch configuration cannot be found.
    string missing = await TryCreateGroupAsync(oddLaunch, "no-such-launch-configuration", 0, 2);
    Console.WriteLine($"Launch configuration that is gone  -> {missing}");

    // AWS: ResourceInUse, while any group names it.
    string inUse;
    try
    {
        await autoscaling.DeleteLaunchConfigurationAsync(new DeleteLaunchConfigurationRequest { LaunchConfigurationName = launchConfig });
        inUse = "accepted";
    }
    catch (AmazonAutoScalingException ex)
    {
        inUse = $"{ex.ErrorCode}: {ex.Message}";
    }

    Console.WriteLine($"Delete the launch config in use    -> {inUse}");

    AutoScalingGroup found = (await autoscaling.DescribeAutoScalingGroupsAsync(new DescribeAutoScalingGroupsRequest { AutoScalingGroupNames = [group] })).AutoScalingGroups[0];
    int left = (await autoscaling.DescribeLaunchConfigurationsAsync(new DescribeLaunchConfigurationsRequest { LaunchConfigurationNames = [found.LaunchConfigurationName] })).LaunchConfigurations?.Count ?? 0;
    Console.WriteLine($"The group's launch configuration   -> {found.LaunchConfigurationName} ({(left == 0 ? "no longer exists" : "still exists")})");

    string[] refusedByAws = [range, missing, inUse];
    accepted = refusedByAws.Count(r => r == "accepted");

    Console.WriteLine();
    Console.WriteLine(accepted == 3
        ? "Floci accepted all three requests AWS refuses: the size range, the missing launch configuration\n  and the delete of one in use. Code that relies on Auto Scaling to catch these passes here and is untested."
        : $"Floci accepted {accepted} of the three requests AWS refuses. Its validation changed: re-read this lab.");
}
finally
{
    int deleted = 0;
    foreach (string name in groups)
    {
        try
        {
            await autoscaling.DeleteAutoScalingGroupAsync(new DeleteAutoScalingGroupRequest { AutoScalingGroupName = name, ForceDelete = true });
            deleted++;
        }
        catch (AmazonAutoScalingException)
        {
            // Never created: the create above was refused.
        }
    }

    try
    {
        await autoscaling.DeleteLaunchConfigurationAsync(new DeleteLaunchConfigurationRequest { LaunchConfigurationName = launchConfig });
        deleted++;
    }
    catch (AmazonAutoScalingException)
    {
        // Already deleted above, while the group still used it.
    }

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> {deleted} resource(s) deleted");
}
