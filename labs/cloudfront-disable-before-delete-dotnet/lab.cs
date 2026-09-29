#:package AWSSDK.CloudFront@4.0.101.4

// AWS CloudFront against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates a distribution, tries to delete it while it is enabled, disables it, waits for it to
// deploy, and deletes it. Real CloudFront keeps a distribution InProgress for minutes after every
// change and refuses the delete until it is Deployed again; the lab prints what this Floci
// answered at each step, so it tells you if that ever changes. The Floci-specific lines are the
// endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonCloudFrontClient cloudFront = new AmazonCloudFrontClient(credentials, new AmazonCloudFrontConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A fresh CallerReference per run, so a crashed run never collides with the next. The S3 origin is
// a bucket nobody owns: CloudFront checks the domain's shape, not that the bucket exists.
string run = Guid.NewGuid().ToString("N")[..12];
string? distributionId = null;
List<string> statuses = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    CreateDistributionResponse created = await cloudFront.CreateDistributionAsync(new CreateDistributionRequest
    {
        DistributionConfig = new DistributionConfig
        {
            CallerReference = run,
            Comment = $"lab-{run}",
            Enabled = true,
            Origins = new Origins
            {
                Quantity = 1,
                Items = [new Origin { Id = "s3", DomainName = $"lab-{run}.s3.amazonaws.com", S3OriginConfig = new S3OriginConfig { OriginAccessIdentity = string.Empty } }],
            },
            // The AWS-managed CachingOptimized policy; its id is the same in every account.
            DefaultCacheBehavior = new DefaultCacheBehavior { TargetOriginId = "s3", ViewerProtocolPolicy = ViewerProtocolPolicy.RedirectToHttps, CachePolicyId = "658327ea-f89d-4fab-a63d-7e88639e58f6" },
        },
    });
    distributionId = created.Distribution.Id;
    statuses.Add(created.Distribution.Status);
    Console.WriteLine($"CreateDistribution        -> {distributionId}, {created.Distribution.Status}, {created.Distribution.DomainName}");

    // Deleting an enabled distribution has to be refused, on AWS and on Floci alike.
    GetDistributionResponse current = await cloudFront.GetDistributionAsync(new GetDistributionRequest { Id = distributionId });
    try
    {
        await cloudFront.DeleteDistributionAsync(new DeleteDistributionRequest { Id = distributionId, IfMatch = current.ETag });
        distributionId = null;
        Console.WriteLine("DeleteDistribution (on)   -> deleted! Real CloudFront refuses this.");
        return;
    }
    catch (DistributionNotDisabledException ex)
    {
        Console.WriteLine($"DeleteDistribution (on)   -> {(int)ex.StatusCode} {ex.ErrorCode}");
    }

    // Disable: fetch the config and its ETag, flip Enabled, send the whole config back under If-Match.
    GetDistributionConfigResponse config = await cloudFront.GetDistributionConfigAsync(new GetDistributionConfigRequest { Id = distributionId });
    config.DistributionConfig.Enabled = false;
    UpdateDistributionResponse disabled = await cloudFront.UpdateDistributionAsync(new UpdateDistributionRequest { Id = distributionId, IfMatch = config.ETag, DistributionConfig = config.DistributionConfig });
    statuses.Add(disabled.Distribution.Status);
    Console.WriteLine($"UpdateDistribution (off)  -> {disabled.Distribution.Status}");

    // This is the wait production code needs: disabled is not deletable until the change deploys,
    // and on real AWS that takes minutes.
    DateTime started = DateTime.UtcNow;
    current = await cloudFront.GetDistributionAsync(new GetDistributionRequest { Id = distributionId });
    int polls = 1;
    while (current.Distribution.Status != "Deployed" && DateTime.UtcNow - started < TimeSpan.FromMinutes(30))
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        current = await cloudFront.GetDistributionAsync(new GetDistributionRequest { Id = distributionId });
        polls++;
    }

    Console.WriteLine($"GetDistribution           -> {current.Distribution.Status} after {polls} poll(s), {(DateTime.UtcNow - started).TotalSeconds:0.0} s");

    await cloudFront.DeleteDistributionAsync(new DeleteDistributionRequest { Id = distributionId, IfMatch = current.ETag });
    Console.WriteLine($"DeleteDistribution (off)  -> {distributionId} deleted");
    distributionId = null;
    Console.WriteLine();

    Console.WriteLine(statuses.All(s => s == "Deployed")
        ? "Every change came back Deployed in the response itself. Real CloudFront answers InProgress\n  here for minutes, so code that deletes straight after disabling passes on this Floci and\n  is refused with DistributionNotDisabled on AWS."
        : "A change came back InProgress, as it does on real CloudFront. The wait above is doing real work.");
}
finally
{
    // Only reached with an id if something above failed: disable (if needed) and delete. On real
    // AWS this delete also needs the Deployed wait above; on Floci it goes through at once.
    if (distributionId is not null)
    {
        GetDistributionConfigResponse config = await cloudFront.GetDistributionConfigAsync(new GetDistributionConfigRequest { Id = distributionId });
        string etag = config.ETag;
        if (config.DistributionConfig.Enabled == true)
        {
            config.DistributionConfig.Enabled = false;
            UpdateDistributionResponse off = await cloudFront.UpdateDistributionAsync(new UpdateDistributionRequest { Id = distributionId, IfMatch = etag, DistributionConfig = config.DistributionConfig });
            etag = off.ETag;
        }

        await cloudFront.DeleteDistributionAsync(new DeleteDistributionRequest { Id = distributionId, IfMatch = etag });
        Console.WriteLine();
        Console.WriteLine("Cleanup -> distribution disabled and deleted");
    }
}
