#:package AWSSDK.Account@4.0.101.7

// AWS Account Management against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// An alternate contact is one value per account and type, so code that sets one has to be able
// to take it off again. This lab sets the SECURITY contact, reads it back, then tries
// DeleteAlternateContact and reads it back once more, and prints what Floci did at each point.

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

string deleteOutcome;

try
{
    await account.DeleteAlternateContactAsync(new DeleteAlternateContactRequest { AlternateContactType = AlternateContactType.SECURITY });
    deleteOutcome = "deleted";
}
// The interesting answer. It is a 404, the same status a missing contact gets, but the error type
// says the *operation* is unknown, not the contact.
catch (AmazonAccountException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    deleteOutcome = $"HTTP 404 {ex.ErrorCode}: {ex.Message}";
}

Console.WriteLine($"DeleteAlternateContact          -> {deleteOutcome}");

AlternateContact? after = await ReadContact(account);
Console.WriteLine($"GetAlternateContact (after)     -> {Describe(after)}");
Console.WriteLine();

Console.WriteLine(after is null
    ? "Floci now deletes an alternate contact, as AWS does. Code that sets one can take it off again."
    : "Floci has no DeleteAlternateContact: the contact this lab set is still there. On AWS the delete\n  works. The 404 is UnknownOperationException, not ResourceNotFoundException, so code that reads\n  any 404 on a delete as \"already gone\" reports success here and leaves the contact behind.");

// Floci cannot delete, so the best a re-run can do is leave what was there before. A contact that
// existed before this lab is written back; one this lab added stays, and the next run reads it as "before".
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
