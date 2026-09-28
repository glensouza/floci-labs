#:package AWSSDK.SimpleWorkflow@4.0.100.12

// Simple Workflow (SWF) against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.
// Warning for real AWS: this lab permanently spends one of your 100 domain names per run.

using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SimpleWorkflow;
using Amazon.SimpleWorkflow.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonSimpleWorkflowClient swf = new AmazonSimpleWorkflowClient(credentials, new AmazonSimpleWorkflowConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Fresh name per run. It has to be: the name this run picks can never be used again.
string domain = $"flocilab-lab-{Guid.NewGuid():N}";

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

// 1. An empty task list. Real SWF holds this call open for up to 60 seconds.
await swf.RegisterDomainAsync(new RegisterDomainRequest { Name = domain, WorkflowExecutionRetentionPeriodInDays = "1" });
Console.WriteLine($"RegisterDomain {domain} -> ok");

Stopwatch stopwatch = Stopwatch.StartNew();
PollForDecisionTaskResponse poll = await swf.PollForDecisionTaskAsync(new PollForDecisionTaskRequest { Domain = domain, TaskList = new TaskList { Name = "nothing-here" } });
stopwatch.Stop();
Console.WriteLine($"PollForDecisionTask on an empty task list -> returned after {stopwatch.Elapsed.TotalSeconds:F1} s, token \"{poll.DecisionTask.TaskToken}\"");
Console.WriteLine(stopwatch.Elapsed < TimeSpan.FromSeconds(5)
    ? "  Real SWF long-polls for up to 60 s and then returns an empty token. A worker loop that looks fine\n  here becomes a hot loop against an instant empty answer, and behaves differently on AWS."
    : "  This build long-polls like real SWF.");
Console.WriteLine();

// 2. There is no DeleteDomain. Deprecate is the whole teardown story.
await swf.DeprecateDomainAsync(new DeprecateDomainRequest { Name = domain });
Console.WriteLine("DeprecateDomain -> ok (the only teardown SWF has)");

try
{
    await swf.RegisterDomainAsync(new RegisterDomainRequest { Name = domain, WorkflowExecutionRetentionPeriodInDays = "1" });
    Console.WriteLine("RegisterDomain with the same name again -> accepted");
    Console.WriteLine("  Real SWF answers DomainAlreadyExistsFault: a deprecated name is spent forever.");
}
catch (DomainAlreadyExistsException ex)
{
    Console.WriteLine($"RegisterDomain with the same name again -> {ex.ErrorCode}");
    Console.WriteLine("  Same as real SWF: a deprecated name is spent forever, and the per-account quota is 100 domains.");
}

ListDomainsResponse deprecated = await swf.ListDomainsAsync(new ListDomainsRequest { RegistrationStatus = RegistrationStatus.DEPRECATED });
bool listed = (deprecated.DomainInfos.Infos ?? []).Any(d => d.Name == domain);
Console.WriteLine($"ListDomains DEPRECATED -> {(listed ? "still lists" : "does not list")} {domain}");
