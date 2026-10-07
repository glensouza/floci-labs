#:package AWSSDK.SecurityToken@4.0.101
#:package AWSSDK.IdentityManagement@4.0.103.4

// AWS STS against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// On AWS, AssumeRole only works for a role that exists and whose trust policy lets you in. This lab
// asks for a role that was never created, then creates one through IAM and assumes it straight
// away. It prints what Floci said and which way each went, so it tells you if Floci's behaviour
// changes — it did once already: Floci 2.1.0 handed out credentials for any role ARN.

using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
AmazonSecurityTokenServiceConfig stsConfig = new AmazonSecurityTokenServiceConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonSecurityTokenServiceClient sts = new AmazonSecurityTokenServiceClient(credentials, stsConfig);
using AmazonIdentityManagementServiceClient iam = new AmazonIdentityManagementServiceClient(
    credentials, new AmazonIdentityManagementServiceConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

GetCallerIdentityResponse me = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest());
Console.WriteLine($"GetCallerIdentity              -> {me.Arn}");

// 1. A role nobody created: no IAM call, no trust policy.
string ghost = $"lab-never-created-{Guid.NewGuid().ToString("N")[..8]}";

try
{
    AssumeRoleResponse assumed = await sts.AssumeRoleAsync(new AssumeRoleRequest { RoleArn = $"arn:aws:iam::{me.Account}:role/{ghost}", RoleSessionName = "lab-session" });
    Console.WriteLine($"AssumeRole {ghost} -> {assumed.AssumedRoleUser.Arn}");
    Console.WriteLine("  Floci handed out credentials for a role that does not exist. AWS answers AccessDenied here.");
}
catch (AmazonSecurityTokenServiceException ex) when (ex.ErrorCode == "AccessDenied")
{
    Console.WriteLine($"AssumeRole {ghost} -> AccessDenied");
    Console.WriteLine("  Refused, as AWS refuses it: a role has to exist and trust the caller.");
}

Console.WriteLine();

// 2. A real role whose trust policy names this account's root, assumed the moment it exists.
string roleName = $"lab-role-{Guid.NewGuid().ToString("N")[..8]}";
string trustPolicy = $$"""{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"AWS":"arn:aws:iam::{{me.Account}}:root"},"Action":"sts:AssumeRole"}]}""";

CreateRoleResponse created = await iam.CreateRoleAsync(new CreateRoleRequest { RoleName = roleName, AssumeRolePolicyDocument = trustPolicy });
Console.WriteLine($"CreateRole                     -> {created.Role.Arn}");

try
{
    AssumeRoleResponse assumed = await sts.AssumeRoleAsync(new AssumeRoleRequest { RoleArn = created.Role.Arn, RoleSessionName = "lab-session", DurationSeconds = 900 });

    // The credentials that came back, used like any others. The secret and session token are live
    // on AWS, so they are never printed.
    Credentials c = assumed.Credentials;
    using AmazonSecurityTokenServiceClient asRole = new AmazonSecurityTokenServiceClient(new SessionAWSCredentials(c.AccessKeyId, c.SecretAccessKey, c.SessionToken), stsConfig);
    GetCallerIdentityResponse who = await asRole.GetCallerIdentityAsync(new GetCallerIdentityRequest());
    Console.WriteLine($"AssumeRole, 0 s after create   -> {assumed.AssumedRoleUser.Arn}");
    Console.WriteLine($"GetCallerIdentity as it        -> {who.Arn}");
    Console.WriteLine();
    Console.WriteLine("Floci let you assume the role the instant it existed.\n  On AWS, IAM is eventually consistent: a role created a moment ago can be refused with\n  AccessDenied for a few seconds. Code that assumes a fresh role needs a short, bounded retry there.");
}
catch (AmazonSecurityTokenServiceException ex) when (ex.ErrorCode == "AccessDenied")
{
    Console.WriteLine($"AssumeRole, 0 s after create   -> AccessDenied: {ex.Message}");
    Console.WriteLine("  Floci refused a role it had just created. That is the propagation delay AWS has; retry briefly.");
}
finally
{
    // The one thing this lab creates, removed whatever happened above, so a second run starts clean.
    await iam.DeleteRoleAsync(new DeleteRoleRequest { RoleName = roleName });
    Console.WriteLine($"DeleteRole                     -> {roleName} removed");
}
