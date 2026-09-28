#:package AWSSDK.IdentityManagement@4.0.103.4

// IAM against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates a user whose only policy is an explicit Deny on everything, then calls IAM *as that
// user*. Whether the call is refused depends on one Floci setting, and that is the point.

using System.Net;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

AmazonIdentityManagementServiceConfig NewConfig() => new AmazonIdentityManagementServiceConfig
{
    ServiceURL = endpoint,
    AuthenticationRegion = "us-east-1",
    // The SDK default is 4 retries with backoff, so a stopped emulator takes ~8 s per call to
    // say so. Against real AWS you want the retries back.
    MaxErrorRetry = 0,
};

// test/test is Floci's admin: it always bypasses enforcement, so it can set the stage.
using AmazonIdentityManagementServiceClient admin = new AmazonIdentityManagementServiceClient(new BasicAWSCredentials("test", "test"), NewConfig());

// A fresh user per run, so a crashed run never breaks the next one.
string userName = $"floci-labs-{Guid.NewGuid():N}"[..32];
const string policyName = "deny-everything";
const string denyAll = """
    {
      "Version": "2012-10-17",
      "Statement": [{ "Effect": "Deny", "Action": "*", "Resource": "*" }]
    }
    """;

bool userCreated = false;
string? accessKeyId = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    userCreated = true;
    await admin.CreateUserAsync(new CreateUserRequest { UserName = userName });
    await admin.PutUserPolicyAsync(new PutUserPolicyRequest { UserName = userName, PolicyName = policyName, PolicyDocument = denyAll });
    CreateAccessKeyResponse key = await admin.CreateAccessKeyAsync(new CreateAccessKeyRequest { UserName = userName });
    accessKeyId = key.AccessKey.AccessKeyId;

    Console.WriteLine($"As admin:  created {userName}");
    Console.WriteLine("           attached an inline policy: Deny * on *");
    Console.WriteLine($"           created access key {accessKeyId}");
    Console.WriteLine();

    // Now switch identity: the same SDK, signing with the denied user's own key.
    using AmazonIdentityManagementServiceClient denied = new AmazonIdentityManagementServiceClient(new BasicAWSCredentials(key.AccessKey.AccessKeyId, key.AccessKey.SecretAccessKey), NewConfig());

    try
    {
        GetUserResponse me = await denied.GetUserAsync(new GetUserRequest { UserName = userName });
        Console.WriteLine($"As {userName}: iam:GetUser -> ALLOWED ({me.User.Arn})");
        Console.WriteLine();
        Console.WriteLine("An explicit Deny on everything, and the call went through anyway. Floci accepts any");
        Console.WriteLine("credentials by default and evaluates no policies. Restart it with");
        Console.WriteLine("FLOCI_SERVICES_IAM_ENFORCEMENT_ENABLED=true and run this again.");
    }
    catch (AmazonServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine($"As {userName}: iam:GetUser -> DENIED ({ex.ErrorCode})");
        Console.WriteLine();
        Console.WriteLine("IAM enforcement is on, so the explicit Deny wins, just as it would in real AWS.");
    }
}
finally
{
    // Back to admin for cleanup. IAM refuses to delete a user that still has keys or policies,
    // so they go first, in that order.
    if (userCreated)
    {
        try
        {
            if (accessKeyId is not null)
            {
                await admin.DeleteAccessKeyAsync(new DeleteAccessKeyRequest { UserName = userName, AccessKeyId = accessKeyId });
            }

            await admin.DeleteUserPolicyAsync(new DeleteUserPolicyRequest { UserName = userName, PolicyName = policyName });
            await admin.DeleteUserAsync(new DeleteUserRequest { UserName = userName });
            Console.WriteLine();
            Console.WriteLine("Cleanup -> user, key and policy deleted");
        }
        catch (NoSuchEntityException ex)
        {
            // A step above never landed, so part of what cleanup expected is not there.
            Console.WriteLine($"Cleanup -> partial: {ex.Message}");
        }
    }
}
