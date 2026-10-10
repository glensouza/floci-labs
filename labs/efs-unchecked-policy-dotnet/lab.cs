#:package AWSSDK.ElasticFileSystem@4.0.100.16

// Amazon EFS against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates an encrypted file system, gives it a resource policy and a mount target, and checks the
// rules Floci enforces the way EFS does: a replayed creation token, and a delete refused while a
// mount target remains. Then it sends three things real EFS refuses: a resource policy that is not
// JSON, provisioned throughput with no figure, and a second mount target in the same Availability
// Zone. The lab prints what it saw and says which way it went, so it tells you when that changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.ElasticFileSystem;
using Amazon.ElasticFileSystem.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
using AmazonElasticFileSystemClient client = new AmazonElasticFileSystemClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonElasticFileSystemConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Made-up subnets: Floci never looks them up. Real EFS needs real subnets in one VPC.
const string SubnetA = "subnet-aaaaaaaa";
const string SubnetB = "subnet-bbbbbbbb";

string token = $"lab-{Guid.NewGuid().ToString("N")[..12]}";
List<string> created = [];
int accepted = 0;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    CreateFileSystemResponse fileSystem = await client.CreateFileSystemAsync(new CreateFileSystemRequest { CreationToken = token, Encrypted = true });
    string id = fileSystem.FileSystemId;
    created.Add(id);

    Console.WriteLine($"{"File system",-40} -> {id}, {fileSystem.LifeCycleState}, encrypted {fileSystem.Encrypted}");

    // Faithful: the creation token is EFS's idempotency key, and the refusal carries the id.
    try
    {
        await client.CreateFileSystemAsync(new CreateFileSystemRequest { CreationToken = token });
        Console.WriteLine($"{"Same creation token again",-40} -> a second file system (EFS refuses this)");
    }
    catch (FileSystemAlreadyExistsException ex)
    {
        Console.WriteLine($"{"Same creation token again",-40} -> {ex.ErrorCode}, naming {ex.FileSystemId}");
    }

    // The gotcha: real EFS answers InvalidPolicyException for a malformed policy.
    string policyAnswer;

    try
    {
        await client.PutFileSystemPolicyAsync(new PutFileSystemPolicyRequest { FileSystemId = id, Policy = "not json" });
        DescribeFileSystemPolicyResponse read = await client.DescribeFileSystemPolicyAsync(new DescribeFileSystemPolicyRequest { FileSystemId = id });
        policyAnswer = $"accepted, reads back \"{read.Policy}\"";
        accepted++;
    }
    catch (InvalidPolicyException ex)
    {
        policyAnswer = $"refused, {ex.ErrorCode}";
    }

    Console.WriteLine($"{"PutFileSystemPolicy \"not json\"",-40} -> {policyAnswer}");

    // Real EFS: ProvisionedThroughputInMibps is "Required if ThroughputMode is set to provisioned".
    string throughputAnswer;

    try
    {
        CreateFileSystemResponse provisioned = await client.CreateFileSystemAsync(new CreateFileSystemRequest { CreationToken = $"{token}-p", ThroughputMode = ThroughputMode.Provisioned });
        created.Add(provisioned.FileSystemId);
        throughputAnswer = $"created {provisioned.FileSystemId}, {provisioned.ThroughputMode} at {provisioned.ProvisionedThroughputInMibps?.ToString() ?? "no"} MiB/s";
        accepted++;
    }
    catch (AmazonElasticFileSystemException ex)
    {
        throughputAnswer = $"refused, {ex.ErrorCode}";
    }

    Console.WriteLine($"{"Provisioned throughput, no figure",-40} -> {throughputAnswer}");

    // Real EFS: "You can create one mount target in each Availability Zone in your VPC."
    CreateMountTargetResponse first = await client.CreateMountTargetAsync(new CreateMountTargetRequest { FileSystemId = id, SubnetId = SubnetA });
    Console.WriteLine($"{"Mount target in " + SubnetA,-40} -> {first.MountTargetId}, {first.AvailabilityZoneName}, {first.IpAddress}");

    try
    {
        CreateMountTargetResponse second = await client.CreateMountTargetAsync(new CreateMountTargetRequest { FileSystemId = id, SubnetId = SubnetB });
        string same = second.AvailabilityZoneName == first.AvailabilityZoneName ? "the same zone" : "a different zone";
        Console.WriteLine($"{"Mount target in " + SubnetB,-40} -> {second.MountTargetId}, {second.AvailabilityZoneName}, {second.IpAddress} ({same})");

        if (second.AvailabilityZoneName == first.AvailabilityZoneName)
        {
            accepted++;
        }
    }
    catch (MountTargetConflictException ex)
    {
        Console.WriteLine($"{"Mount target in " + SubnetB,-40} -> refused, {ex.ErrorCode}");
    }

    // Faithful: a file system with a mount target is in use.
    try
    {
        await client.DeleteFileSystemAsync(new DeleteFileSystemRequest { FileSystemId = id });
        created.Remove(id);
        Console.WriteLine($"{"DeleteFileSystem, mount targets in place",-40} -> deleted (EFS refuses this)");
    }
    catch (FileSystemInUseException ex)
    {
        Console.WriteLine($"{"DeleteFileSystem, mount targets in place",-40} -> {ex.ErrorCode}");
    }

    Console.WriteLine();
    Console.WriteLine(accepted == 0
        ? "Floci refused all three, as EFS does. This lab's gotcha is fixed: the emulator now checks them."
        : $"Floci accepted {(accepted == 3 ? "all three" : $"{accepted} of the three")} that real EFS refuses. A policy Floci\n"
            + "  stores, EFS answers with InvalidPolicyException, so parse the JSON yourself before you\n"
            + "  send it, and don't let the emulator's \"yes\" stand in for the service's rules.");
}
finally
{
    Console.WriteLine();

    foreach (string fileSystemId in created)
    {
        DescribeMountTargetsResponse targets = await client.DescribeMountTargetsAsync(new DescribeMountTargetsRequest { FileSystemId = fileSystemId });

        foreach (MountTargetDescription target in targets.MountTargets ?? [])
        {
            await client.DeleteMountTargetAsync(new DeleteMountTargetRequest { MountTargetId = target.MountTargetId });
        }

        await client.DeleteFileSystemAsync(new DeleteFileSystemRequest { FileSystemId = fileSystemId });
    }

    DescribeFileSystemsResponse left = await client.DescribeFileSystemsAsync(new DescribeFileSystemsRequest());
    int mine = (left.FileSystems ?? []).Count(f => f.CreationToken.StartsWith(token, StringComparison.Ordinal));
    Console.WriteLine($"Cleanup -> removed {created.Count} file system(s) and their mount targets; still listed: {mine}");
}
