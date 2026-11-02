#:package AWSSDK.AccessAnalyzer@4.0.100.18

// AWS IAM Access Analyzer against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Floci serves CreateAnalyzer, ListAnalyzers, GetAnalyzer and DeleteAnalyzer, but not archive
// rules or findings. This lab creates an analyzer, reads it with GetAnalyzer, asks for its archive
// rules, and deletes it twice. It prints the HTTP status and error code of both failures, because
// they are the same status: a missing operation and a missing analyzer both answer 404, and only
// the error code tells them apart.

using System.Net;
using Amazon.AccessAnalyzer;
using Amazon.AccessAnalyzer.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonAccessAnalyzerConfig config = new AmazonAccessAnalyzerConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonAccessAnalyzerClient analyzer = new AmazonAccessAnalyzerClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

string name = $"lab-aa-{Guid.NewGuid().ToString("N")[..8]}";
bool deleted = false;

CreateAnalyzerResponse created = await analyzer.CreateAnalyzerAsync(new CreateAnalyzerRequest { AnalyzerName = name, Type = Amazon.AccessAnalyzer.Type.ACCOUNT });
Console.WriteLine($"CreateAnalyzer         -> {created.Arn}");

try
{
    GetAnalyzerResponse got = await analyzer.GetAnalyzerAsync(new GetAnalyzerRequest { AnalyzerName = name });
    Console.WriteLine($"GetAnalyzer            -> {got.Analyzer.Status}");

    // The call you would reach for next to see what the analyzer has archived.
    (HttpStatusCode rulesStatus, string rulesCode) = await FailureOf(() => analyzer.ListArchiveRulesAsync(new ListArchiveRulesRequest { AnalyzerName = name }));
    Console.WriteLine($"ListArchiveRules       -> {(int)rulesStatus} {rulesCode}");

    await analyzer.DeleteAnalyzerAsync(new DeleteAnalyzerRequest { AnalyzerName = name });
    deleted = true;
    Console.WriteLine("DeleteAnalyzer         -> deleted");

    (HttpStatusCode againStatus, string againCode) = await FailureOf(() => analyzer.DeleteAnalyzerAsync(new DeleteAnalyzerRequest { AnalyzerName = name }));
    Console.WriteLine($"DeleteAnalyzer again   -> {(int)againStatus} {againCode}");
    Console.WriteLine();

    Console.WriteLine(rulesStatus == againStatus && rulesCode != againCode
        ? $"Floci answered ListArchiveRules, an operation it does not serve, with {(int)rulesStatus},\n  the same status as deleting an analyzer that does not exist. Not 501.\n  Code that maps 404 to \"not found\" reads a missing feature as a missing resource:\n  check the error code ({rulesCode} vs {againCode}), not the status."
        : rulesCode == "(succeeded)"
            ? "Floci now serves ListArchiveRules."
            : $"ListArchiveRules answered {(int)rulesStatus} {rulesCode} and the second delete {(int)againStatus} {againCode}, which no longer matches what this lab was written against.");
}
finally
{
    // A fresh name per run and a delete in a finally, so a second run starts clean.
    if (!deleted)
    {
        await analyzer.DeleteAnalyzerAsync(new DeleteAnalyzerRequest { AnalyzerName = name });
        Console.WriteLine($"DeleteAnalyzer {name} -> cleaned up");
    }
}

// Runs a call that is expected to fail and returns what the service said.
static async Task<(HttpStatusCode Status, string Code)> FailureOf(Func<Task> call)
{
    try
    {
        await call();
        return (HttpStatusCode.OK, "(succeeded)");
    }
    catch (AmazonAccessAnalyzerException ex)
    {
        return (ex.StatusCode, ex.ErrorCode);
    }
}
