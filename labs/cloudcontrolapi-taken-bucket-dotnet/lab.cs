#:package AWSSDK.CloudControlApi@4.0.100.16

// Cloud Control API against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates a bucket through Cloud Control, which knows nothing about S3's own API, follows the
// request to its end, then asks for the same bucket again. Real Cloud Control reports the second
// request FAILED with AlreadyExists; the lab prints what this Floci answered, and says which way it
// went, so it tells you if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.CloudControlApi;
using Amazon.CloudControlApi.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonCloudControlApiClient cloudControl = new AmazonCloudControlApiClient(credentials, new AmazonCloudControlApiConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

const string BucketType = "AWS::S3::Bucket";
string bucketName = $"lab-{Guid.NewGuid().ToString("N")[..12]}";
string desiredState = $$"""{"BucketName":"{{bucketName}}"}""";
bool created = false;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

// A Cloud Control create answers with a token; GetResourceRequestStatus says when it is done.
async Task<ProgressEvent> CreateAndWaitAsync()
{
    ProgressEvent progress = (await cloudControl.CreateResourceAsync(new CreateResourceRequest { TypeName = BucketType, DesiredState = desiredState })).ProgressEvent;
    string first = progress.OperationStatus?.Value ?? "no status";

    for (int i = 0; i < 50 && progress.OperationStatus?.Value is not ("SUCCESS" or "FAILED"); i++)
    {
        await Task.Delay(200);
        progress = (await cloudControl.GetResourceRequestStatusAsync(new GetResourceRequestStatusRequest { RequestToken = progress.RequestToken })).ProgressEvent;
    }

    Console.WriteLine($"  first answer {first}, then {progress.OperationStatus?.Value}{(progress.ErrorCode is null ? string.Empty : $" {progress.ErrorCode.Value}")} -> {progress.Identifier ?? "no identifier"}");
    return progress;
}

try
{
    Console.WriteLine($"Create bucket {bucketName}");
    ProgressEvent first = await CreateAndWaitAsync();
    created = first.OperationStatus?.Value == "SUCCESS";

    // AWS: FAILED, with ErrorCode AlreadyExists.
    Console.WriteLine($"Create bucket {bucketName} again");
    ProgressEvent second = await CreateAndWaitAsync();

    // Floci lists buckets through Cloud Control; count how many carry this name.
    int listed = ((await cloudControl.ListResourcesAsync(new ListResourcesRequest { TypeName = BucketType })).ResourceDescriptions ?? [])
        .Count(d => d.Identifier == bucketName);
    Console.WriteLine($"Buckets listed under that name          -> {listed}");

    Console.WriteLine();
    Console.WriteLine(second.OperationStatus?.Value == "SUCCESS"
        ? "Floci reported SUCCESS for a bucket that already exists, and created nothing new.\n  Code that treats FAILED/AlreadyExists as \"someone else has this name\" never sees it here."
        : $"Floci answered {second.OperationStatus?.Value} {second.ErrorCode?.Value}: it may refuse a taken name now. Re-read this lab.");
}
finally
{
    string removed = "nothing";

    if (created)
    {
        ProgressEvent deleting = (await cloudControl.DeleteResourceAsync(new DeleteResourceRequest { TypeName = BucketType, Identifier = bucketName })).ProgressEvent;

        for (int i = 0; i < 50 && deleting.OperationStatus?.Value is not ("SUCCESS" or "FAILED"); i++)
        {
            await Task.Delay(200);
            deleting = (await cloudControl.GetResourceRequestStatusAsync(new GetResourceRequestStatusRequest { RequestToken = deleting.RequestToken })).ProgressEvent;
        }

        removed = $"the bucket ({deleting.OperationStatus?.Value})";
    }

    string left;

    try
    {
        await cloudControl.GetResourceAsync(new GetResourceRequest { TypeName = BucketType, Identifier = bucketName });
        left = "the bucket";
    }
    catch (ResourceNotFoundException)
    {
        left = "none";
    }

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> removed {removed}; still there afterwards: {left}");
}
