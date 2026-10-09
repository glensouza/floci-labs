#:package AWSSDK.ElasticBeanstalk@4.0.101.2

// Elastic Beanstalk against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates an application, a version of it, and an environment on a solution stack, then reads the
// environment's status straight back. Real Elastic Beanstalk spends minutes in Launching while it
// starts instances; the lab prints what this Floci answered the moment the create returned. Then it
// asks for a configuration template and the deletion of an application that still has an
// environment, and cleans up. It says which way each went, so it tells you if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.ElasticBeanstalk;
using Amazon.ElasticBeanstalk.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonElasticBeanstalkClient beanstalk = new AmazonElasticBeanstalkClient(credentials, new AmazonElasticBeanstalkConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A fresh name per run means a crashed run never collides with the next one. Environment names
// are capped at 40 characters, so it gets a shorter prefix.
string run = Guid.NewGuid().ToString("N")[..12];
string application = $"lab-{run}";
string environment = $"lab-{run}-env";
bool environmentCreated = false;
bool applicationCreated = false;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

async Task<string> AnswerAsync(Func<Task> call)
{
    try
    {
        await call();
        return "accepted";
    }
    catch (AmazonElasticBeanstalkException ex)
    {
        return $"{ex.ErrorCode}: {ex.Message}";
    }
}

async Task<EnvironmentDescription?> LiveEnvironmentAsync()
    => (await beanstalk.DescribeEnvironmentsAsync(new DescribeEnvironmentsRequest { EnvironmentNames = [environment] })).Environments?
        .FirstOrDefault(e => e.Status != EnvironmentStatus.Terminated);

try
{
    await beanstalk.CreateApplicationAsync(new CreateApplicationRequest { ApplicationName = application });
    applicationCreated = true;

    // No source bundle: the version is a label, which is all an environment needs to point at.
    await beanstalk.CreateApplicationVersionAsync(new CreateApplicationVersionRequest { ApplicationName = application, VersionLabel = "1.0.0" });

    List<string> stacks = (await beanstalk.ListAvailableSolutionStacksAsync(new ListAvailableSolutionStacksRequest())).SolutionStacks ?? [];
    string stack = stacks.FirstOrDefault(s => s.Contains("Node.js", StringComparison.Ordinal)) ?? stacks[0];
    Console.WriteLine($"Solution stacks on offer             -> {stacks.Count}, using {stack}");

    await beanstalk.CreateEnvironmentAsync(new CreateEnvironmentRequest { ApplicationName = application, EnvironmentName = environment, VersionLabel = "1.0.0", SolutionStackName = stack });
    environmentCreated = true;

    // Read straight back, with no wait. AWS: Launching, health Grey, for several minutes.
    EnvironmentDescription created = await LiveEnvironmentAsync() ?? throw new InvalidOperationException("CreateEnvironment returned, but DescribeEnvironments does not list it.");
    Console.WriteLine($"The environment, right after create  -> {created.Status?.Value}, {created.Health?.Value}");

    // AWS: accepted. Floci answers HTTP 400 UnsupportedOperation, not 501.
    string template = await AnswerAsync(() => beanstalk.CreateConfigurationTemplateAsync(new CreateConfigurationTemplateRequest { ApplicationName = application, TemplateName = $"lab-{run}-template", SolutionStackName = stack }));
    Console.WriteLine($"Save a configuration template        -> {template}");

    // AWS: refused while an environment is running.
    string delete = await AnswerAsync(() => beanstalk.DeleteApplicationAsync(new DeleteApplicationRequest { ApplicationName = application }));
    Console.WriteLine($"Delete the app with the env running  -> {delete}");
    applicationCreated = delete != "accepted";

    Console.WriteLine();
    Console.WriteLine(created.Status == EnvironmentStatus.Ready
        ? "Floci launched nothing: the environment read Ready the moment it was created.\n  Code that waits for Launching to end never waits here, and is untested."
        : $"Floci answered {created.Status?.Value} right after create: it may model the launch now. Re-read this lab.");
    Console.WriteLine(template.StartsWith("UnsupportedOperation", StringComparison.Ordinal)
        ? "Configuration templates are not built yet: Floci answers UnsupportedOperation."
        : "Floci answered the configuration template differently: it may have built them. Re-read this lab.");
}
finally
{
    List<string> removed = [];

    if (environmentCreated && await LiveEnvironmentAsync() is not null)
    {
        await beanstalk.TerminateEnvironmentAsync(new TerminateEnvironmentRequest { EnvironmentName = environment });
        removed.Add("environment");
    }

    if (applicationCreated)
    {
        // Takes its versions with it.
        await beanstalk.DeleteApplicationAsync(new DeleteApplicationRequest { ApplicationName = application });
        removed.Add("application");
    }

    int applicationsLeft = (await beanstalk.DescribeApplicationsAsync(new DescribeApplicationsRequest { ApplicationNames = [application] })).Applications?.Count ?? 0;

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> removed {(removed.Count == 0 ? "nothing" : string.Join(" and ", removed))}; {applicationsLeft} application(s) of this run left, the environment reads {(await LiveEnvironmentAsync())?.Status?.Value ?? "Terminated"}");
}
