#:package AWSSDK.AppConfig@4.0.101.14

// AWS AppConfig against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Publishes one version of a hosted configuration, checks that version 9 does not exist, then
// asks AppConfig to deploy version 9 anyway. Real AppConfig refuses a version the profile does not
// have; the lab prints what this Floci answered, and says which way it went, so it tells you if
// that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using System.Text;
using Amazon.AppConfig;
using Amazon.AppConfig.Model;
using Amazon.Runtime;

// System.Environment: Amazon.AppConfig.Model has an Environment of its own.
// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (System.Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonAppConfigClient appConfig = new AmazonAppConfigClient(credentials, new AmazonAppConfigConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A strategy AppConfig ships with: no rollout steps, then a ten-minute bake on real AWS.
const string Strategy = "AppConfig.AllAtOnce";
string name = $"lab-{Guid.NewGuid().ToString("N")[..12]}";
string? applicationId = null;
string? environmentId = null;
string? profileId = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    applicationId = (await appConfig.CreateApplicationAsync(new CreateApplicationRequest { Name = name })).Id;
    environmentId = (await appConfig.CreateEnvironmentAsync(new CreateEnvironmentRequest { ApplicationId = applicationId, Name = "prod" })).Id;
    profileId = (await appConfig.CreateConfigurationProfileAsync(new CreateConfigurationProfileRequest { ApplicationId = applicationId, Name = "flags", LocationUri = "hosted" })).Id;
    Console.WriteLine($"Application {name}, environment prod, hosted profile flags");

    using MemoryStream content = new(Encoding.UTF8.GetBytes("""{"newCheckout":true}"""));
    int? published = (await appConfig.CreateHostedConfigurationVersionAsync(new CreateHostedConfigurationVersionRequest
    {
        ApplicationId = applicationId,
        ConfigurationProfileId = profileId,
        Content = content,
        ContentType = "application/json",
    })).VersionNumber;
    Console.WriteLine($"Published hosted version                -> {published}");

    string lookup;

    try
    {
        await appConfig.GetHostedConfigurationVersionAsync(new GetHostedConfigurationVersionRequest { ApplicationId = applicationId, ConfigurationProfileId = profileId, VersionNumber = 9 });
        lookup = "found";
    }
    catch (ResourceNotFoundException ex)
    {
        lookup = ex.ErrorCode;
    }

    Console.WriteLine($"GetHostedConfigurationVersion 9         -> {lookup}");

    // AWS: refused, since the profile has no version 9.
    string outcome;
    bool accepted;

    try
    {
        StartDeploymentResponse started = await appConfig.StartDeploymentAsync(new StartDeploymentRequest
        {
            ApplicationId = applicationId,
            EnvironmentId = environmentId,
            ConfigurationProfileId = profileId,
            ConfigurationVersion = "9",
            DeploymentStrategyId = Strategy,
        });

        GetDeploymentResponse deployment = await appConfig.GetDeploymentAsync(new GetDeploymentRequest
        {
            ApplicationId = applicationId,
            EnvironmentId = environmentId,
            DeploymentNumber = started.DeploymentNumber.GetValueOrDefault(),
        });

        outcome = $"deployment {deployment.DeploymentNumber}, version {deployment.ConfigurationVersion}, {deployment.State?.Value}";
        accepted = true;
    }
    catch (AmazonAppConfigException ex) when (ex.StatusCode != 0)
    {
        outcome = $"HTTP {(int)ex.StatusCode} {ex.ErrorCode}: {ex.Message}";
        accepted = false;
    }

    Console.WriteLine($"StartDeployment of version 9            -> {outcome}");

    Console.WriteLine();
    Console.WriteLine(accepted
        ? "Floci deployed a configuration version that does not exist, and reported the deployment done.\n  Code that relies on AppConfig refusing a bad version number never sees that refusal here."
        : "Floci refused the version that does not exist: it may validate it now. Re-read this lab.");
}
finally
{
    List<string> removed = [];

    if (applicationId is not null)
    {
        if (profileId is not null)
        {
            List<HostedConfigurationVersionSummary> versions = (await appConfig.ListHostedConfigurationVersionsAsync(new ListHostedConfigurationVersionsRequest { ApplicationId = applicationId, ConfigurationProfileId = profileId })).Items ?? [];

            foreach (HostedConfigurationVersionSummary version in versions)
            {
                await appConfig.DeleteHostedConfigurationVersionAsync(new DeleteHostedConfigurationVersionRequest { ApplicationId = applicationId, ConfigurationProfileId = profileId, VersionNumber = version.VersionNumber.GetValueOrDefault() });
            }

            await appConfig.DeleteConfigurationProfileAsync(new DeleteConfigurationProfileRequest { ApplicationId = applicationId, ConfigurationProfileId = profileId });
            removed.Add($"{versions.Count} version(s)");
            removed.Add("the profile");
        }

        if (environmentId is not null)
        {
            try
            {
                await appConfig.DeleteEnvironmentAsync(new DeleteEnvironmentRequest { ApplicationId = applicationId, EnvironmentId = environmentId });
                removed.Add("the environment");
            }
            // Floci has not built DeleteEnvironment; it goes when the application does.
            catch (AmazonAppConfigException ex) when (ex.StatusCode != 0)
            {
                removed.Add($"not the environment (HTTP {(int)ex.StatusCode})");
            }
        }

        await appConfig.DeleteApplicationAsync(new DeleteApplicationRequest { ApplicationId = applicationId });
        removed.Add("the application");
    }

    bool left = ((await appConfig.ListApplicationsAsync(new ListApplicationsRequest())).Items ?? []).Any(a => a.Name == name);

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> removed {(removed.Count == 0 ? "nothing" : string.Join(", ", removed))}; application still listed: {(left ? "yes" : "no")}");
}
