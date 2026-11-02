#:package AWSSDK.Account@4.0.102.2

// AWS Account Management against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// An alternate contact is one value per account and type, so code that sets one has to take it off
// again, and that cleanup can run twice. This lab sets the SECURITY contact, deletes it, reads it
// back, then deletes it a second time, and prints exactly what Floci answered at each point.

using System.Net;
using Amazon.Account;
using Amazon.Account.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonAccountConfig config = new AmazonAccountConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonAccountClient account = new AmazonAccountClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

string name = $"lab-contact-{Guid.NewGuid().ToString("N")[..8]}";

AlternateContact? before = await ReadContact(account);
Console.WriteLine($"GetAlternateContact (before)    -> {Describe(before)}");

await account.PutAlternateContactAsync(new PutAlternateContactRequest
{
    AlternateContactType = AlternateContactType.SECURITY,
    Name = name,
    Title = "Security lead",
    EmailAddress = $"{name}@example.com",
    PhoneNumber = "+15555550100",
});
Console.WriteLine($"PutAlternateContact             -> {name}");
Console.WriteLine($"GetAlternateContact             -> {Describe(await ReadContact(account))}");

string first = await TryDelete(account);
Console.WriteLine($"DeleteAlternateContact          -> {first}");
Console.WriteLine($"GetAlternateContact (after)     -> {Describe(await ReadContact(account))}");

string second = await TryDelete(account);
Console.WriteLine($"DeleteAlternateContact (again)  -> {second}");
Console.WriteLine();

if (first == "HTTP 200" && second.StartsWith("HTTP 404 ResourceNotFoundException", StringComparison.Ordinal))
{
    Console.WriteLine("The delete works, and deleting again is a 404 ResourceNotFoundException, the same exception a\n  read gives for a contact nobody set. Cleanup code should catch that exception type and nothing wider.");
}
else
{
    Console.WriteLine("Floci did not answer the way this lab was written against (first delete HTTP 200, second HTTP 404\n  ResourceNotFoundException). Read the two DeleteAlternateContact lines above.");
}

// The contact belongs to the account, not to this lab: put back what was there before.
if (before is not null)
{
    await account.PutAlternateContactAsync(new PutAlternateContactRequest
    {
        AlternateContactType = AlternateContactType.SECURITY,
        Name = before.Name,
        Title = before.Title,
        EmailAddress = before.EmailAddress,
        PhoneNumber = before.PhoneNumber,
    });
    Console.WriteLine($"PutAlternateContact (restore)   -> {before.Name} put back");
}

static async Task<string> TryDelete(IAmazonAccount account)
{
    try
    {
        DeleteAlternateContactResponse response = await account.DeleteAlternateContactAsync(new DeleteAlternateContactRequest { AlternateContactType = AlternateContactType.SECURITY });

        return $"HTTP {(int)response.HttpStatusCode}";
    }
    // Both 404s are an exception with a status of 404. Only the error type says what is missing:
    // ResourceNotFoundException is the contact, UnknownOperationException would be the operation.
    catch (AmazonAccountException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        return $"HTTP 404 {ex.ErrorCode}: {ex.Message}";
    }
}

static async Task<AlternateContact?> ReadContact(IAmazonAccount account)
{
    try
    {
        GetAlternateContactResponse response = await account.GetAlternateContactAsync(new GetAlternateContactRequest { AlternateContactType = AlternateContactType.SECURITY });

        return response.AlternateContact;
    }
    catch (ResourceNotFoundException)
    {
        // How the service says "no SECURITY contact": an exception, not an empty response.
        return null;
    }
}

static string Describe(AlternateContact? contact) => contact is null ? "none (ResourceNotFoundException)" : $"{contact.Name} <{contact.EmailAddress}>";
