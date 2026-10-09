#:package AWSSDK.CloudFormation@4.0.103

// CloudFormation against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Validates a body that is not a template, then creates a stack whose one resource has a type that
// does not exist, reads it straight back, and lists what the stack says it provisioned. Real
// CloudFormation refuses both before it creates anything; the lab prints what this Floci answered.
// It says which way each went, so it tells you if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonCloudFormationClient cloudFormation = new AmazonCloudFormationClient(credentials, new AmazonCloudFormationConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A resource type that exists nowhere. A fresh stack name per run.
const string UnknownTypeTemplate = """{ "Resources": { "Thing": { "Type": "AWS::Nope::Thing" } } }""";
string stackName = $"lab-{Guid.NewGuid().ToString("N")[..12]}";
bool created = false;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

async Task<string> AnswerAsync(Func<Task<string>> call)
{
    try
    {
        return await call();
    }
    catch (AmazonCloudFormationException ex)
    {
        return $"{ex.ErrorCode} (HTTP {(int)ex.StatusCode}): {ex.Message}";
    }
}

try
{
    // AWS: ValidationError, because the body is not JSON or YAML of a template.
    string validated = await AnswerAsync(async () => $"valid, {((await cloudFormation.ValidateTemplateAsync(new ValidateTemplateRequest { TemplateBody = "this is not a template" })).Parameters ?? []).Count} parameter(s)");
    Console.WriteLine($"Validate a body that is not a template  -> {validated}");

    // AWS: ValidationError (Unrecognized resource types), and no stack.
    string status = await AnswerAsync(async () =>
    {
        await cloudFormation.CreateStackAsync(new CreateStackRequest { StackName = stackName, TemplateBody = UnknownTypeTemplate });
        created = true;

        // Read straight back, with no wait.
        Stack stack = (await cloudFormation.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName })).Stacks[0];
        return stack.StackStatus?.Value ?? "no status";
    });
    Console.WriteLine($"A stack with type AWS::Nope::Thing      -> {status}");

    string resources = created
        ? string.Join(", ", ((await cloudFormation.DescribeStackResourcesAsync(new DescribeStackResourcesRequest { StackName = stackName })).StackResources ?? [])
            .Select(r => $"{r.LogicalResourceId} {r.ResourceType} {r.ResourceStatus?.Value} -> {r.PhysicalResourceId}"))
        : "no stack";
    Console.WriteLine($"What the stack says it provisioned      -> {(resources.Length == 0 ? "nothing" : resources)}");

    Console.WriteLine();
    Console.WriteLine(validated.StartsWith("valid", StringComparison.Ordinal)
        ? "Floci accepted a body that is not a template: ValidateTemplate does not read it."
        : "Floci refused a body that is not a template: it may validate templates now. Re-read this lab.");
    Console.WriteLine(status == "CREATE_COMPLETE"
        ? "Floci created a stack from a resource type that does not exist, and it read CREATE_COMPLETE at once.\n  A typo in a resource type passes here and fails on AWS."
        : $"Floci answered {status}: it may check resource types now. Re-read this lab.");
}
finally
{
    string removed = "nothing";

    if (created)
    {
        await cloudFormation.DeleteStackAsync(new DeleteStackRequest { StackName = stackName });
        removed = "the stack";
    }

    // ListStacks keeps deleted stacks as DELETE_COMPLETE history; anything else is still alive.
    List<StackSummary> left = ((await cloudFormation.ListStacksAsync(new ListStacksRequest())).StackSummaries ?? [])
        .Where(s => s.StackName == stackName && s.StackStatus != StackStatus.DELETE_COMPLETE)
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> removed {removed}; still alive afterwards: {(left.Count == 0 ? "none" : string.Join(", ", left.Select(s => s.StackStatus?.Value)))}");
}
