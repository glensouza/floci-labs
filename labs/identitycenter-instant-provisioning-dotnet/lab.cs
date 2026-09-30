#:package AWSSDK.SSOAdmin@4.0.103

// AWS IAM Identity Center permission sets against Floci, with the official AWS SDK for .NET and
// nothing else. Run it with: dotnet run lab.cs
//
// On AWS, ProvisionPermissionSet is asynchronous: it answers IN_PROGRESS with a request id, and
// you poll DescribePermissionSetProvisioningStatus until it says SUCCEEDED or FAILED. This lab
// provisions a fresh permission set to an account id that exists nowhere and prints what Floci
// answered, so it tells you if Floci ever starts behaving like AWS.

using Amazon.Runtime;
using Amazon.SSOAdmin;
using Amazon.SSOAdmin.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonSSOAdminConfig config = new AmazonSSOAdminConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonSSOAdminClient sso = new AmazonSSOAdminClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

// An account id that belongs to nobody, so there is nothing real to provision to.
const string NoSuchAccount = "999999999999";

InstanceMetadata instance = (await sso.ListInstancesAsync(new ListInstancesRequest())).Instances[0];
Console.WriteLine($"ListInstances          -> {instance.Name}, {instance.InstanceArn}");

string name = $"lab-ps-{Guid.NewGuid().ToString("N")[..8]}";
string permissionSetArn = (await sso.CreatePermissionSetAsync(new CreatePermissionSetRequest { InstanceArn = instance.InstanceArn, Name = name })).PermissionSet.PermissionSetArn;
Console.WriteLine($"CreatePermissionSet    -> {name}");

try
{
    ProvisionPermissionSetResponse provisioned = await sso.ProvisionPermissionSetAsync(new ProvisionPermissionSetRequest
    {
        InstanceArn = instance.InstanceArn,
        PermissionSetArn = permissionSetArn,
        TargetType = ProvisionTargetType.AWS_ACCOUNT,
        TargetId = NoSuchAccount,
    });

    PermissionSetProvisioningStatus first = provisioned.PermissionSetProvisioningStatus;
    Console.WriteLine($"ProvisionPermissionSet -> {first.Status} (account {NoSuchAccount}, request {first.RequestId})");

    // What production code has to do anyway: read the request back until it settles.
    PermissionSetProvisioningStatus status = first;
    int polls = 0;
    while (status.Status == StatusValues.IN_PROGRESS && polls < 30)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        status = (await sso.DescribePermissionSetProvisioningStatusAsync(new DescribePermissionSetProvisioningStatusRequest { InstanceArn = instance.InstanceArn, ProvisionPermissionSetRequestId = first.RequestId })).PermissionSetProvisioningStatus;
        polls++;
    }

    Console.WriteLine($"after {polls} poll(s)       -> {status.Status}{(status.FailureReason is null ? string.Empty : $": {status.FailureReason}")}");
    Console.WriteLine();

    Console.WriteLine(first.Status == StatusValues.SUCCEEDED
        ? $"Floci answered SUCCEEDED on the first call, for an account that does not exist.\n  On AWS provisioning is asynchronous: the call answers IN_PROGRESS, and the outcome, FAILED\n  included, arrives only through DescribePermissionSetProvisioningStatus.\n  Code that reads the first status and moves on passes here and races in production: poll\n  DescribePermissionSetProvisioningStatus until the request settles."
        : "Floci now answers provisioning asynchronously, as AWS does.");
}
finally
{
    // A fresh name per run and a delete in a finally, so a second run starts clean.
    await sso.DeletePermissionSetAsync(new DeletePermissionSetRequest { InstanceArn = instance.InstanceArn, PermissionSetArn = permissionSetArn });
    Console.WriteLine();
    Console.WriteLine($"DeletePermissionSet {name} -> done");
}
