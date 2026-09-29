#:package AWSSDK.SecurityToken@4.0.101

// AWS STS against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// On AWS, AssumeRole only works for a role that exists and whose trust policy lets you in; anything
// else answers AccessDenied. This lab asks for a role that was never created, signs a second client
// with whatever comes back, and asks STS who that client is. It prints what Floci said and which way
// it went, so it tells you if Floci ever starts checking the role.

using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonSecurityTokenServiceConfig config = new AmazonSecurityTokenServiceConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonSecurityTokenServiceClient sts = new AmazonSecurityTokenServiceClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

GetCallerIdentityResponse me = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest());
Console.WriteLine($"GetCallerIdentity           -> {me.Arn}");

// Nothing ever creates this role: no IAM call, no trust policy.
string roleName = $"lab-never-created-{Guid.NewGuid().ToString("N")[..8]}";
string roleArn = $"arn:aws:iam::{me.Account}:role/{roleName}";
const string SessionName = "lab-session";

try
{
    AssumeRoleResponse assumed = await sts.AssumeRoleAsync(new AssumeRoleRequest { RoleArn = roleArn, RoleSessionName = SessionName, DurationSeconds = 900 });
    Console.WriteLine($"AssumeRole {roleName} -> {assumed.AssumedRoleUser.Arn}");

    // The credentials that came back, used like any others. The secret and session token are live
    // on AWS, so they are never printed.
    Credentials c = assumed.Credentials;
    using AmazonSecurityTokenServiceClient asRole = new AmazonSecurityTokenServiceClient(new SessionAWSCredentials(c.AccessKeyId, c.SecretAccessKey, c.SessionToken), config);
    GetCallerIdentityResponse who = await asRole.GetCallerIdentityAsync(new GetCallerIdentityRequest());
    Console.WriteLine($"GetCallerIdentity as it     -> {who.Arn}");

    Console.WriteLine();
    Console.WriteLine($"Floci handed out working credentials for {roleName}, a role that does not exist.\n  On AWS this AssumeRole answers AccessDenied: the role has to exist and trust the caller.\n  A green run here proves the call shape, not your role or its trust policy.");

    string expected = $":assumed-role/{roleName}/{SessionName}";
    Console.WriteLine(who.Arn.EndsWith(expected, StringComparison.Ordinal)
        ? $"The assumed identity carries the session name you asked for ({SessionName}), as AWS does."
        : $"The assumed identity's session name is \"{who.Arn[(who.Arn.LastIndexOf('/') + 1)..]}\", not the \"{SessionName}\" you asked for.\n  On AWS it is always yours, so don't match on the session name against Floci.");
}
catch (AmazonSecurityTokenServiceException ex) when (ex.ErrorCode == "AccessDenied")
{
    Console.WriteLine($"AssumeRole {roleName} -> refused: AccessDenied: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Floci now refuses a role that does not exist, as AWS does: create the role, with a trust policy, first.");
}

// Nothing to clean up: STS creates nothing that persists. Every credential simply expires.
