#:package AWSSDK.S3Tables@4.0.100.16

// Amazon S3 Tables against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates a table bucket, a namespace and an Iceberg table, keeps the table's ARN the way code
// does when it writes one into a policy or a config file, renames the table, and asks whether that
// ARN still names it. AWS documents a table ARN as ending in the table's id, which a rename does
// not touch. The lab prints what this Floci answered and says which way it went, so it tells you
// if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.Runtime;
using Amazon.S3Tables;
using Amazon.S3Tables.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
using AmazonS3TablesClient client = new AmazonS3TablesClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonS3TablesConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

const string Namespace = "sales";
string name = $"lab-{Guid.NewGuid().ToString("N")[..12]}";
string? bucketArn = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    bucketArn = (await client.CreateTableBucketAsync(new CreateTableBucketRequest { Name = name })).Arn;
    await client.CreateNamespaceAsync(new CreateNamespaceRequest { TableBucketARN = bucketArn, Namespace = [Namespace] });

    // The ARN as CreateTable hands it back: what code saves into a policy, a catalog or a config file.
    string saved = (await client.CreateTableAsync(new CreateTableRequest
    {
        TableBucketARN = bucketArn,
        Namespace = Namespace,
        Name = "orders",
        Format = OpenTableFormat.ICEBERG,
    })).TableARN;

    Console.WriteLine($"{"Table bucket",-40} -> {name}");
    Console.WriteLine($"{"CreateTable orders, ARN saved",-40} -> {saved}");

    await client.RenameTableAsync(new RenameTableRequest { TableBucketARN = bucketArn, Namespace = Namespace, Name = "orders", NewName = "orders_v2" });

    string now = (await client.GetTableAsync(new GetTableRequest { TableBucketARN = bucketArn, Namespace = Namespace, Name = "orders_v2" })).TableARN;

    // ListTables across the bucket: does any table still carry the ARN that was saved?
    List<TableSummary> tables = (await client.ListTablesAsync(new ListTablesRequest { TableBucketARN = bucketArn })).Tables ?? [];
    TableSummary? match = tables.FirstOrDefault(t => t.TableARN == saved);

    Console.WriteLine($"{"RenameTable orders -> orders_v2",-40} -> {now}");
    Console.WriteLine($"{"A table with the saved ARN",-40} -> {match?.Name ?? "none"}");
    Console.WriteLine();

    if (now != saved)
    {
        Console.WriteLine("The rename changed the table's ARN: Floci builds it from the table's name.");
        Console.WriteLine("  AWS documents a table ARN as .../table/{table-id}, which a rename leaves alone, so a policy or");
        Console.WriteLine("  config that saved the old ARN still names the table there. Here it names a table that no longer exists.");
    }
    else
    {
        Console.WriteLine("The rename kept the table's ARN, as AWS documents. Floci has changed: this lab's gotcha is gone.");
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

// Inside out, because S3 Tables refuses to delete a namespace or a bucket that still has children.
async Task<string> CleanupAsync()
{
    if (bucketArn is null)
    {
        return "nothing was created";
    }

    try
    {
        int removed = 0;

        foreach (TableSummary table in (await client.ListTablesAsync(new ListTablesRequest { TableBucketARN = bucketArn })).Tables ?? [])
        {
            await client.DeleteTableAsync(new DeleteTableRequest { TableBucketARN = bucketArn, Namespace = string.Join(".", table.Namespace ?? []), Name = table.Name });
            removed++;
        }

        await client.DeleteNamespaceAsync(new DeleteNamespaceRequest { TableBucketARN = bucketArn, Namespace = Namespace });
        await client.DeleteTableBucketAsync(new DeleteTableBucketRequest { TableBucketARN = bucketArn });

        List<TableBucketSummary> left = (await client.ListTableBucketsAsync(new ListTableBucketsRequest { Prefix = name })).TableBuckets ?? [];

        return $"removed {removed} table(s), the namespace, the table bucket; bucket still listed: {(left.Count > 0 ? "yes" : "no")}";
    }
    catch (AmazonServiceException ex)
    {
        return $"HTTP {(int)ex.StatusCode} {ex.ErrorCode}: {ex.Message}";
    }
}
