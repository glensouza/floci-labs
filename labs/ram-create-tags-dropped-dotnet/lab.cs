#:package AWSSDK.RAM@4.0.100.14

// AWS Resource Access Manager against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// CreateResourceShare takes a Tags list, and on AWS those tags are on the share the moment it
// exists. This lab creates a share with two tags, reads the share back, then tags it again with
// TagResource and reads it back once more, and prints what Floci kept at each point.

using Amazon.RAM;
using Amazon.RAM.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonRAMConfig config = new AmazonRAMConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonRAMClient ram = new AmazonRAMClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

List<Tag> tags = [new Tag { Key = "env", Value = "lab" }, new Tag { Key = "owner", Value = "floci-labs" }];
string name = $"lab-share-{Guid.NewGuid().ToString("N")[..8]}";

CreateResourceShareResponse created = await ram.CreateResourceShareAsync(new CreateResourceShareRequest { Name = name, Tags = tags });
string arn = created.ResourceShare.ResourceShareArn;
Console.WriteLine($"CreateResourceShare (2 tags)  -> {created.ResourceShare.Status}, tags in the response: {Describe(created.ResourceShare.Tags)}");

try
{
    List<Tag> afterCreate = await ReadTags(ram, arn);
    Console.WriteLine($"GetResourceShares             -> tags: {Describe(afterCreate)}");

    await ram.TagResourceAsync(new TagResourceRequest { ResourceShareArn = arn, Tags = tags });
    Console.WriteLine("TagResource (same 2 tags)     -> done");

    List<Tag> afterTag = await ReadTags(ram, arn);
    Console.WriteLine($"GetResourceShares             -> tags: {Describe(afterTag)}");
    Console.WriteLine();

    Console.WriteLine(afterCreate.Count == tags.Count
        ? "Floci now stores the tags sent with CreateResourceShare, as AWS does. Tagging at create is enough."
        : $"Floci kept {afterCreate.Count} of the {tags.Count} tags sent with CreateResourceShare, and all of them once\n  TagResource sent them again. On AWS the create stores them. Code that tags at create and\n  reads the tags back (a cost report, a cleanup by owner) sees none of them on Floci.");
}
finally
{
    // A deleted share stays listed with status DELETED, as on AWS, so there is nothing else to remove.
    await ram.DeleteResourceShareAsync(new DeleteResourceShareRequest { ResourceShareArn = arn });
    Console.WriteLine($"DeleteResourceShare           -> {name} cleaned up");
}

static async Task<List<Tag>> ReadTags(IAmazonRAM ram, string arn)
{
    GetResourceSharesResponse response = await ram.GetResourceSharesAsync(new GetResourceSharesRequest { ResourceOwner = ResourceOwner.SELF, ResourceShareArns = [arn] });

    return response.ResourceShares.Single().Tags ?? [];
}

static string Describe(List<Tag>? tags) => tags is null || tags.Count == 0 ? "[]" : $"[{string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))}]";
