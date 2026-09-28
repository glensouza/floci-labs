#:package AWSSDK.SimpleSystemsManagement@4.0.102

// SSM Parameter Store against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.
// Delete those and this is the code you would run against real AWS.

using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

AmazonSimpleSystemsManagementConfig config = new AmazonSimpleSystemsManagementConfig
{
    ServiceURL = endpoint,
    AuthenticationRegion = "us-east-1",
    // The SDK default is 4 retries with backoff, so a stopped emulator takes ~8 s per call to
    // say so. Against real AWS you want the retries back.
    MaxErrorRetry = 0,
};

// Floci parses SigV4 but does not verify it, so any well-formed pair works.
using AmazonSimpleSystemsManagementClient client = new AmazonSimpleSystemsManagementClient(new BasicAWSCredentials("test", "test"), config);

// A fresh path per run, so a crashed run never breaks the next one.
string prefix = $"/floci-labs/{Guid.NewGuid():N}";
string plainName = $"{prefix}/greeting";
string secretName = $"{prefix}/db-password";

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine($"Parameter path: {prefix}");
Console.WriteLine();

try
{
    // 1. Create. The first put needs no Overwrite flag.
    PutParameterResponse v1 = await client.PutParameterAsync(new PutParameterRequest { Name = plainName, Value = "hello", Type = ParameterType.String });
    Console.WriteLine($"PutParameter            -> version {v1.Version}");

    // 2. Put is not an upsert. Without Overwrite, a second put on the same name is refused.
    try
    {
        await client.PutParameterAsync(new PutParameterRequest { Name = plainName, Value = "hello again", Type = ParameterType.String });
        Console.WriteLine("PutParameter (no flag)  -> accepted?! Real SSM refuses this.");
    }
    catch (ParameterAlreadyExistsException)
    {
        Console.WriteLine("PutParameter (no flag)  -> ParameterAlreadyExists, as real SSM does");
    }

    // 3. With Overwrite it becomes a new version rather than a replacement.
    PutParameterResponse v2 = await client.PutParameterAsync(new PutParameterRequest { Name = plainName, Value = "hello, v2", Type = ParameterType.String, Overwrite = true });
    Console.WriteLine($"PutParameter Overwrite  -> version {v2.Version}");

    GetParameterResponse current = await client.GetParameterAsync(new GetParameterRequest { Name = plainName });

    // Check the value, not just the HTTP 200. A read that returns the wrong thing did not round-trip.
    if (current.Parameter.Value != "hello, v2")
    {
        throw new InvalidOperationException($"Read back \"{current.Parameter.Value}\", not the value just written.");
    }

    Console.WriteLine($"GetParameter            -> \"{current.Parameter.Value}\" (version {current.Parameter.Version})");
    Console.WriteLine();

    // 4. The gotcha. Real SSM encrypts a SecureString with KMS and, without WithDecryption,
    //    hands back ciphertext. Watch what Floci hands back.
    const string secret = "hunter2";
    await client.PutParameterAsync(new PutParameterRequest { Name = secretName, Value = secret, Type = ParameterType.SecureString });
    GetParameterResponse encrypted = await client.GetParameterAsync(new GetParameterRequest { Name = secretName, WithDecryption = false });
    string returned = encrypted.Parameter.Value;

    Console.WriteLine($"SecureString, WithDecryption = false -> \"{returned}\"");
    Console.WriteLine(returned == secret
        ? "  Plaintext. On Floci a SecureString is not encrypted, so don't use it to test your secrets handling or KMS policy."
        : "  Ciphertext, as real SSM returns. This Floci build encrypts SecureStrings.");
}
finally
{
    // Runs even when a step above throws, so the account is clean for the next run. Deleting a
    // name that was never created is not an error worth stopping for.
    DeleteParametersResponse deleted = await client.DeleteParametersAsync(new DeleteParametersRequest { Names = [plainName, secretName] });
    Console.WriteLine();
    Console.WriteLine($"DeleteParameters        -> removed {deleted.DeletedParameters?.Count ?? 0}");
}
