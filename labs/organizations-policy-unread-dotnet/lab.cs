#:package AWSSDK.Organizations@4.0.101.6

// AWS Organizations against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// A service control policy is a JSON document, and AWS parses it: CreatePolicy with content that is
// not a valid policy answers MalformedPolicyDocumentException. This lab creates an organization,
// switches SCPs on the way AWS requires, then creates one well-formed SCP and one whose content is
// "not json", and prints what Floci said about each and which way it went.

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

List<string> policyIds = [];

try
{
    Root root = (await organizations.ListRootsAsync(new ListRootsRequest())).Roots.Single();
    Console.WriteLine($"ListRoots                     -> {root.Id}, policy types: [{string.Join(", ", (root.PolicyTypes ?? []).Select(t => $"{t.Type}={t.Status}"))}]");

    // AWS documents a new root as having no policy types; SCPs have to be switched on first.
    // Floci 2.1.0 had them on already and answered PolicyTypeAlreadyEnabledException here.
    try
    {
        await organizations.EnablePolicyTypeAsync(new EnablePolicyTypeRequest { RootId = root.Id, PolicyType = PolicyType.SERVICE_CONTROL_POLICY });
        Console.WriteLine("EnablePolicyType              -> SCPs enabled (a new root has none, as on AWS)");
    }
    catch (PolicyTypeAlreadyEnabledException)
    {
        Console.WriteLine("EnablePolicyType              -> already enabled (a new root came with SCPs on; AWS's has none)");
    }

    const string Good = """{"Version":"2012-10-17","Statement":[{"Effect":"Deny","Action":"s3:DeleteBucket","Resource":"*"}]}""";
    CreatePolicyResponse good = await organizations.CreatePolicyAsync(new CreatePolicyRequest { Name = $"lab-good-{Guid.NewGuid().ToString("N")[..8]}", Description = "valid SCP", Type = PolicyType.SERVICE_CONTROL_POLICY, Content = Good });
    policyIds.Add(good.Policy.PolicySummary.Id);
    Console.WriteLine($"CreatePolicy, a valid SCP     -> {good.Policy.PolicySummary.Id}");

    try
    {
        CreatePolicyResponse bad = await organizations.CreatePolicyAsync(new CreatePolicyRequest { Name = $"lab-bad-{Guid.NewGuid().ToString("N")[..8]}", Description = "not a policy", Type = PolicyType.SERVICE_CONTROL_POLICY, Content = "not json" });
        policyIds.Add(bad.Policy.PolicySummary.Id);
        Console.WriteLine($"CreatePolicy, \"not json\"      -> {bad.Policy.PolicySummary.Id}, stored content: {bad.Policy.Content}");
        Console.WriteLine();
        Console.WriteLine("Floci stored a policy that is not a policy.\n  AWS answers MalformedPolicyDocumentException here. If you build SCPs in code,\n  a test against Floci will not catch a broken one: validate the JSON yourself.");
    }
    catch (MalformedPolicyDocumentException ex)
    {
        Console.WriteLine($"CreatePolicy, \"not json\"      -> MalformedPolicyDocumentException: {ex.Message}");
        Console.WriteLine();
        Console.WriteLine("Floci refused the malformed policy, as AWS does.");
    }
}
finally
{
    // Policies first: an organization that still holds one is not empty on AWS.
    foreach (string id in policyIds)
    {
        await organizations.DeletePolicyAsync(new DeletePolicyRequest { PolicyId = id });
    }

    await organizations.DeleteOrganizationAsync(new DeleteOrganizationRequest());
    Console.WriteLine($"DeleteOrganization            -> {created.Organization.Id} removed");
}
