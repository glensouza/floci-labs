#:package AWSSDK.AppConfigData@4.0.100.17
#:package AWSSDK.AppConfig@4.0.101.14

// AWS AppConfigData against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Deploys a feature-flag configuration, then reads it the way an application does: open a
// session asking for a 60-second poll interval, poll, and poll again straight away. The second
// poll comes back empty, because nothing changed, so a cache that stores every body ends up
// holding nothing. The lab also reuses a spent token. It prints what this Floci answered and
// says which way each one went, so it tells you if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using System.Globalization;
using System.Text;
using Amazon.AppConfig;
using Amazon.AppConfig.Model;
using Amazon.AppConfigData;
using Amazon.AppConfigData.Model;
using Amazon.Runtime;

// System.Environment: Amazon.AppConfig.Model has an Environment of its own.
// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (System.Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");

// AWSSDK.AppConfig writes the configuration; AWSSDK.AppConfigData is what an application reads it with.
using AmazonAppConfigClient appConfig = new AmazonAppConfigClient(credentials, new AmazonAppConfigConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
using AmazonAppConfigDataClient data = new AmazonAppConfigDataClient(credentials, new AmazonAppConfigDataConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

const string Flags = """{"newCheckout":true}""";
const int PollInterval = 60;
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

    using MemoryStream content = new(Encoding.UTF8.GetBytes(Flags));
    int version = (await appConfig.CreateHostedConfigurationVersionAsync(new CreateHostedConfigurationVersionRequest
    {
        ApplicationId = applicationId,
        ConfigurationProfileId = profileId,
        Content = content,
        ContentType = "application/json",
    })).VersionNumber.GetValueOrDefault();

    StartDeploymentResponse deployment = await appConfig.StartDeploymentAsync(new StartDeploymentRequest
    {
        ApplicationId = applicationId,
        EnvironmentId = environmentId,
        ConfigurationProfileId = profileId,
        ConfigurationVersion = version.ToString(CultureInfo.InvariantCulture),
        DeploymentStrategyId = "AppConfig.AllAtOnce",
    });
    Console.WriteLine($"Application {name}: version {version} deployed to prod   -> {deployment.State?.Value}");

    string firstToken = (await data.StartConfigurationSessionAsync(new StartConfigurationSessionRequest
    {
        ApplicationIdentifier = applicationId,
        EnvironmentIdentifier = environmentId,
        ConfigurationProfileIdentifier = profileId,
        RequiredMinimumPollIntervalInSeconds = PollInterval,
    })).InitialConfigurationToken;
    Console.WriteLine($"StartConfigurationSession, {PollInterval} s minimum interval -> token");

    GetLatestConfigurationResponse first = await data.GetLatestConfigurationAsync(new GetLatestConfigurationRequest { ConfigurationToken = firstToken });
    string firstBody = await ReadAsync(first);
    Console.WriteLine($"Poll 1                                   -> {Encoding.UTF8.GetByteCount(firstBody)} bytes, version '{first.VersionLabel}', next poll in {first.NextPollIntervalInSeconds} s");

    // The naive cache: whatever the last poll returned.
    string cache = firstBody;

    // Straight away, not after the 60 s the session asked for.
    string secondOutcome;
    bool intervalEnforced;

    try
    {
        GetLatestConfigurationResponse second = await data.GetLatestConfigurationAsync(new GetLatestConfigurationRequest { ConfigurationToken = first.NextPollConfigurationToken });
        string secondBody = await ReadAsync(second);

        cache = secondBody;
        secondOutcome = $"{Encoding.UTF8.GetByteCount(secondBody)} bytes, version '{second.VersionLabel}'";
        intervalEnforced = false;
    }
    catch (AmazonAppConfigDataException ex) when (ex.StatusCode != 0)
    {
        secondOutcome = $"HTTP {(int)ex.StatusCode} {ex.ErrorCode}: {ex.Message}";
        intervalEnforced = true;
    }

    Console.WriteLine($"Poll 2, 0 s later, nothing changed       -> {secondOutcome}");
    Console.WriteLine($"A cache that stores every body now holds -> '{cache}'");

    // The token poll 1 already used. The docs: each token is valid for one call.
    string reuse;

    try
    {
        GetLatestConfigurationResponse again = await data.GetLatestConfigurationAsync(new GetLatestConfigurationRequest { ConfigurationToken = firstToken });
        reuse = $"accepted, {Encoding.UTF8.GetByteCount(await ReadAsync(again))} bytes";
    }
    catch (AmazonAppConfigDataException ex) when (ex.StatusCode != 0)
    {
        reuse = $"HTTP {(int)ex.StatusCode} {ex.ErrorCode}";
    }

    Console.WriteLine($"Poll with the token poll 1 already used  -> {reuse}");

    Console.WriteLine();
    Console.WriteLine(cache.Length == 0
        ? "An unchanged configuration comes back as an empty body, as AWS documents: it means \"you already have it\".\n  Keep the last non-empty body; overwriting it with this one blanks your flags."
        : intervalEnforced
            ? "Poll 2 was refused, so the cache was never overwritten. Read the line above."
            : "Poll 2 returned the configuration again rather than an empty body. Re-read this lab.");
    Console.WriteLine(intervalEnforced
        ? "Floci refused a poll inside the session's minimum interval: it may enforce it now. Re-read this lab."
        : $"Floci answered a poll 0 s into a {PollInterval} s minimum interval. AWS documents that a session can't poll more often than that,\n  so a tight polling loop that works here does not carry over.");
}
finally
{
    List<string> removed = [];

    if (applicationId is not null)
    {
        if (profileId is not null)
        {
            List<HostedConfigurationVersionSummary> versions = (await appConfig.ListHostedConfigurationVersionsAsync(new ListHostedConfigurationVersionsRequest { ApplicationId = applicationId, ConfigurationProfileId = profileId })).Items ?? [];

            foreach (HostedConfigurationVersionSummary summary in versions)
            {
                await appConfig.DeleteHostedConfigurationVersionAsync(new DeleteHostedConfigurationVersionRequest { ApplicationId = applicationId, ConfigurationProfileId = profileId, VersionNumber = summary.VersionNumber.GetValueOrDefault() });
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
            // Floci has not built DeleteEnvironment; it stays until the container goes.
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

// An empty stream, or none, when the poll brought nothing new.
static async Task<string> ReadAsync(GetLatestConfigurationResponse response)
{
    if (response.Configuration is null)
    {
        return string.Empty;
    }

    using StreamReader reader = new(response.Configuration, Encoding.UTF8);

    return await reader.ReadToEndAsync();
}
