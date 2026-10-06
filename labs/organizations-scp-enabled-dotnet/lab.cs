#:package AWSSDK.Organizations@4.0.101.6

// AWS Organizations against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Service control policies are a policy type that has to be enabled on the organization's root
// (EnablePolicyType) before you can use them. AWS documents a new root as having none enabled.
// This lab creates an organization, reads the root's policy types, calls EnablePolicyType, then
// turns SCPs off and tries to create one, and prints what Floci said at each point.

using System.Net;
using Amazon.Organizations;
using Amazon.Organizations.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonOrganizationsConfig config = new AmazonOrganizationsConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonOrganizationsClient organizations = new AmazonOrganizationsClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

// An account is in at most one organization. The lab creates one and deletes only that one.
CreateOrganizationResponse created = await organizations.CreateOrganizationAsync(new CreateOrganizationRequest { FeatureSet = OrganizationFeatureSet.ALL });
Console.WriteLine($"CreateOrganization            -> {created.Organization.Id}");

try
{
    Root root = (await organizations.ListRootsAsync(new ListRootsRequest())).Roots.Single();
    bool enabledAtBirth = (root.PolicyTypes ?? []).Exists(t => t.Type == PolicyType.SERVICE_CONTROL_POLICY && t.Status == PolicyTypeStatus.ENABLED);
    Console.WriteLine($"ListRoots                     -> {root.Id}, policy types: [{string.Join(", ", (root.PolicyTypes ?? []).Select(t => $"{t.Type}={t.Status}"))}]");

    // The call code written for AWS makes before it creates or attaches an SCP.
    (HttpStatusCode enableStatus, string enableCode) = await Outcome(() => organizations.EnablePolicyTypeAsync(new EnablePolicyTypeRequest { RootId = root.Id, PolicyType = PolicyType.SERVICE_CONTROL_POLICY }));
    Console.WriteLine($"EnablePolicyType              -> {(int)enableStatus} {enableCode}");

    // Now the state AWS documents for a new root: SCPs off.
    await organizations.DisablePolicyTypeAsync(new DisablePolicyTypeRequest { RootId = root.Id, PolicyType = PolicyType.SERVICE_CONTROL_POLICY });
    Console.WriteLine("DisablePolicyType             -> SCPs off");

    (HttpStatusCode createStatus, string createCode) = await Outcome(() => organizations.CreatePolicyAsync(new CreatePolicyRequest
    {
        Name = $"lab-scp-{Guid.NewGuid().ToString("N")[..8]}",
        Description = "Created by the floci-labs Organizations lab.",
        Type = PolicyType.SERVICE_CONTROL_POLICY,
        Content = """{"Version":"2012-10-17","Statement":[{"Effect":"Deny","Action":"s3:DeleteBucket","Resource":"*"}]}""",
    }));
    Console.WriteLine($"CreatePolicy with SCPs off    -> {(int)createStatus} {createCode}");
    Console.WriteLine();

    Console.WriteLine(enabledAtBirth
        ? "Floci enabled SCPs on the new root by itself, so EnablePolicyType had nothing to do.\n  AWS documents a new root as having no policy types enabled. Code that never calls\n  EnablePolicyType passes against Floci and fails against AWS: make the call, and treat\n  PolicyTypeAlreadyEnabledException as success."
        : "Floci now creates the root with SCPs off, as AWS documents. EnablePolicyType is required here too.");
}
finally
{
    // Deleting the organization takes everything in it, including a policy created above.
    await organizations.DeleteOrganizationAsync(new DeleteOrganizationRequest());
    Console.WriteLine($"DeleteOrganization            -> {created.Organization.Id} cleaned up");
}

// Runs a call and returns what the service said: 200 on success, or the failure's status and code.
static async Task<(HttpStatusCode Status, string Code)> Outcome(Func<Task> call)
{
    try
    {
        await call();
        return (HttpStatusCode.OK, "(succeeded)");
    }
    catch (AmazonOrganizationsException ex)
    {
        return (ex.StatusCode, ex.ErrorCode);
    }
}
