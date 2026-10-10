#:package AWSSDK.S3Vectors@4.0.101.1

// Amazon S3 Vectors against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates a vector bucket and a three-dimensional cosine index, puts four films in it, and asks for
// the two nearest to a query vector, filtered and unfiltered. Floci answers the search the way S3
// Vectors does. Then it lists vector buckets with a prefix that matches nothing, the way code finds
// its own bucket by name. AWS filters by that prefix; this Floci answers every bucket. The lab
// prints what it saw and says which way it went, so it tells you when that changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using System.Globalization;
using Amazon.Runtime;
using Amazon.Runtime.Documents;
using Amazon.S3Vectors;
using Amazon.S3Vectors.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
using AmazonS3VectorsClient client = new AmazonS3VectorsClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonS3VectorsConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

const string Index = "films";
const string NoMatch = "no-such-prefix-";
string name = $"lab-{Guid.NewGuid().ToString("N")[..12]}";
bool created = false;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    await client.CreateVectorBucketAsync(new CreateVectorBucketRequest { VectorBucketName = name });
    created = true;

    await client.CreateIndexAsync(new CreateIndexRequest { VectorBucketName = name, IndexName = Index, DataType = DataType.Float32, Dimension = 3, DistanceMetric = DistanceMetric.Cosine });

    await client.PutVectorsAsync(new PutVectorsRequest
    {
        VectorBucketName = name,
        IndexName = Index,
        Vectors =
        [
            Film("alien", [1f, 0f, 0f], "scifi"),
            Film("blade-runner", [0.9f, 0.3f, 0f], "scifi"),
            Film("amelie", [0f, 1f, 0f], "romance"),
            Film("notting-hill", [0f, 0.8f, 0.4f], "romance"),
        ],
    });

    Console.WriteLine($"{"Vector bucket",-36} -> {name}");
    Console.WriteLine($"{"Index films, 3 dimensions, cosine",-36} -> 4 vectors put");
    Console.WriteLine($"{"Two nearest to [1, 0.1, 0]",-36} -> {await QueryAsync(null)}");
    Console.WriteLine($"{"The same, filtered to romance",-36} -> {await QueryAsync("romance")}");

    // How code finds its own bucket: list with its name as the prefix. This prefix matches no bucket.
    List<VectorBucketSummary> listed = (await client.ListVectorBucketsAsync(new ListVectorBucketsRequest { Prefix = NoMatch })).VectorBuckets ?? [];

    Console.WriteLine($"{$"ListVectorBuckets, Prefix {NoMatch}",-36} -> {listed.Count} bucket(s): {string.Join(", ", listed.Select(b => b.VectorBucketName))}");
    Console.WriteLine();

    if (listed.Any(b => !b.VectorBucketName.StartsWith(NoMatch, StringComparison.Ordinal)))
    {
        Console.WriteLine("Floci ignored the prefix and answered every bucket. AWS filters by it, so code that takes");
        Console.WriteLine("  the first bucket a prefix lists, or treats \"listed\" as \"mine and still there\", works on AWS");
        Console.WriteLine("  and picks up someone else's bucket here. Match the name yourself; it costs one Where.");
    }
    else
    {
        Console.WriteLine("Floci honoured the prefix, as AWS does. Floci has changed: this lab's gotcha is gone.");
    }
}
catch (AmazonServiceException ex)
{
    Console.WriteLine($"Floci answered HTTP {(int)ex.StatusCode} {ex.ErrorCode}: {ex.Message}");
}
finally
{
    Console.WriteLine();
    Console.WriteLine($"Cleanup -> {await CleanupAsync()}");
}

static PutInputVector Film(string key, List<float> data, string genre)
    => new()
    {
        Key = key,
        Data = new VectorData { Float32 = data },
        Metadata = new Document(new Dictionary<string, Document> { ["genre"] = genre }),
    };

// Nearest first, with the cosine distance: 0 is the same direction, so lower is closer.
async Task<string> QueryAsync(string? genre)
{
    QueryVectorsRequest request = new QueryVectorsRequest
    {
        VectorBucketName = name,
        IndexName = Index,
        TopK = 2,
        QueryVector = new VectorData { Float32 = [1f, 0.1f, 0f] },
        ReturnDistance = true,
    };

    // Filter is a Document, a struct: leave it unset for no filter rather than assigning null.
    if (genre is not null)
    {
        request.Filter = new Document(new Dictionary<string, Document> { ["genre"] = genre });
    }

    QueryVectorsResponse response = await client.QueryVectorsAsync(request);

    return string.Join(", ", (response.Vectors ?? []).Select(v => $"{v.Key} {v.Distance?.ToString("0.000", CultureInfo.InvariantCulture)}"));
}

// Every index first, because S3 Vectors refuses to delete a bucket that still holds one. The check
// afterwards matches the name exactly, for the reason this lab exists.
async Task<string> CleanupAsync()
{
    if (!created)
    {
        return "nothing was created";
    }

    try
    {
        foreach (IndexSummary index in (await client.ListIndexesAsync(new ListIndexesRequest { VectorBucketName = name })).Indexes ?? [])
        {
            await client.DeleteIndexAsync(new DeleteIndexRequest { VectorBucketName = name, IndexName = index.IndexName });
        }

        await client.DeleteVectorBucketAsync(new DeleteVectorBucketRequest { VectorBucketName = name });

        bool left = ((await client.ListVectorBucketsAsync(new ListVectorBucketsRequest { Prefix = name })).VectorBuckets ?? []).Any(b => b.VectorBucketName == name);

        return $"removed the index and the vector bucket; bucket still listed: {(left ? "yes" : "no")}";
    }
    catch (AmazonServiceException ex)
    {
        return $"HTTP {(int)ex.StatusCode} {ex.ErrorCode}: {ex.Message}";
    }
}
