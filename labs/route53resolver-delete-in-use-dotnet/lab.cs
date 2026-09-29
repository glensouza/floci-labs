#:package AWSSDK.Route53Resolver@4.0.100.15

// AWS Route 53 Resolver against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates a SYSTEM forwarding rule, associates it with a VPC, then tries to delete the rule while
// the VPC still uses it. Real AWS refuses that with ResourceInUseException, and after you
// disassociate, the association sits in DELETING for a while before the delete is allowed. The lab
// prints what this Floci answered, so it tells you if that ever changes. The Floci-specific lines
// are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.Route53Resolver;
using Amazon.Route53Resolver.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonRoute53ResolverClient resolver = new AmazonRoute53ResolverClient(credentials, new AmazonRoute53ResolverConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Floci does not check that the VPC exists; real AWS does, so against a real account put one of
// yours here. .test is reserved (RFC 2606), so the rule can never shadow a real domain.
const string VpcId = "vpc-0123456789abcdef0";
string run = Guid.NewGuid().ToString("N")[..12];

string? ruleId = null;
bool associated = false;
bool ruleDeleted = false;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    CreateResolverRuleResponse rule = await resolver.CreateResolverRuleAsync(new CreateResolverRuleRequest
    {
        CreatorRequestId = run,
        Name = $"lab-{run}",
        RuleType = RuleTypeOption.SYSTEM,
        DomainName = $"corp-{run}.lab.test",
    });
    ruleId = rule.ResolverRule.Id;
    Console.WriteLine($"CreateResolverRule       -> {ruleId} {rule.ResolverRule.RuleType} {rule.ResolverRule.DomainName}, {rule.ResolverRule.Status}");

    associated = true;
    AssociateResolverRuleResponse association = await resolver.AssociateResolverRuleAsync(new AssociateResolverRuleRequest { ResolverRuleId = ruleId, VPCId = VpcId });
    Console.WriteLine($"AssociateResolverRule    -> {association.ResolverRuleAssociation.Id} {VpcId}, {association.ResolverRuleAssociation.Status}");

    // The call production code must never make: the VPC is still using the rule.
    try
    {
        DeleteResolverRuleResponse deleted = await resolver.DeleteResolverRuleAsync(new DeleteResolverRuleRequest { ResolverRuleId = ruleId });
        ruleDeleted = true;
        Console.WriteLine($"DeleteResolverRule       -> {deleted.ResolverRule.Status}  (while the VPC is still associated)");

        List<ResolverRuleAssociation> left = await AssociationsAsync(ruleId);
        Console.WriteLine($"  Associations still listed for the deleted rule: {left.Count}");
        Console.WriteLine();
        Console.WriteLine("The rule was deleted while a VPC still used it. Real AWS refuses this with\n  ResourceInUseException, so code that deletes before disassociating passes on this Floci and\n  fails on AWS.");
    }
    catch (ResourceInUseException ex)
    {
        Console.WriteLine($"DeleteResolverRule       -> refused: {ex.ErrorCode}");
        Console.WriteLine();
        Console.WriteLine("Refused while a VPC still uses the rule, as real AWS does.");
    }
}
finally
{
    // The order production code needs: disassociate, wait until the association is really gone
    // (real AWS keeps it DELETING for a while), and only then delete the rule.
    Console.WriteLine();

    if (ruleId is not null && associated)
    {
        DisassociateResolverRuleResponse gone = await resolver.DisassociateResolverRuleAsync(new DisassociateResolverRuleRequest { ResolverRuleId = ruleId, VPCId = VpcId });
        Console.WriteLine($"DisassociateResolverRule -> {gone.ResolverRuleAssociation.Status}");

        DateTime started = DateTime.UtcNow;
        int polls = 1;
        while ((await AssociationsAsync(ruleId)).Count != 0 && DateTime.UtcNow - started < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            polls++;
        }

        Console.WriteLine($"  Association gone after {polls} poll(s), {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
    }

    if (ruleId is not null && !ruleDeleted)
    {
        DeleteResolverRuleResponse deleted = await resolver.DeleteResolverRuleAsync(new DeleteResolverRuleRequest { ResolverRuleId = ruleId });
        Console.WriteLine($"DeleteResolverRule       -> {deleted.ResolverRule.Status}");
    }

    Console.WriteLine("Cleanup -> done");
}

async Task<List<ResolverRuleAssociation>> AssociationsAsync(string id)
{
    ListResolverRuleAssociationsResponse response = await resolver.ListResolverRuleAssociationsAsync(new ListResolverRuleAssociationsRequest
    {
        Filters = [new Filter { Name = "ResolverRuleId", Values = [id] }],
    });

    return (response.ResolverRuleAssociations ?? []).FindAll(a => a.VPCId == VpcId);
}
