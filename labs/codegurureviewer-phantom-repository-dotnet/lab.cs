#:package AWSSDK.CodeGuruReviewer@4.0.100.17

// CodeGuru Reviewer against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Associates a CodeCommit repository that does not exist, reads the association straight back,
// then asks for the code reviews CodeGuru Reviewer has run and for an association that was never
// made, and disassociates. Real CodeGuru Reviewer reads Associating while it checks the repository,
// then Failed for one that is not there; the lab prints what this Floci answered. It says which way
// each went, so it tells you if that ever changes.
// The Floci-specific lines are the endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.CodeGuruReviewer;
using Amazon.CodeGuruReviewer.Model;
using Amazon.Runtime;
using ReviewType = Amazon.CodeGuruReviewer.Type;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonCodeGuruReviewerClient reviewer = new AmazonCodeGuruReviewerClient(credentials, new AmazonCodeGuruReviewerConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// A fresh name per run, and no CodeCommit repository of that name exists anywhere.
string repository = $"lab-{Guid.NewGuid().ToString("N")[..12]}";
string? arn = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

async Task<string> AnswerAsync(Func<Task<string>> call)
{
    try
    {
        return await call();
    }
    catch (AmazonCodeGuruReviewerException ex)
    {
        return $"{ex.ErrorCode} (HTTP {(int)ex.StatusCode}): {ex.Message}";
    }
}

try
{
    arn = (await reviewer.AssociateRepositoryAsync(new AssociateRepositoryRequest
    {
        Repository = new Repository { CodeCommit = new CodeCommitRepository { Name = repository } },
    })).RepositoryAssociation.AssociationArn;

    // Read straight back, with no wait. AWS: Associating, then Failed, because the repository is not there.
    RepositoryAssociation association = (await reviewer.DescribeRepositoryAssociationAsync(new DescribeRepositoryAssociationRequest { AssociationArn = arn })).RepositoryAssociation;
    Console.WriteLine($"A repository that does not exist     -> {association.State?.Value}");

    // AWS: the list of reviews. Floci answers UnknownOperationException, HTTP 404, not 501.
    string reviews = await AnswerAsync(async () => $"{((await reviewer.ListCodeReviewsAsync(new ListCodeReviewsRequest { Type = ReviewType.RepositoryAnalysis })).CodeReviewSummaries ?? []).Count} code review(s)");
    Console.WriteLine($"List the code reviews                -> {reviews}");

    // AWS: NotFoundException.
    string unknown = arn[..arn.LastIndexOf(':')] + ":" + Guid.NewGuid();
    string missing = await AnswerAsync(async () => (await reviewer.DescribeRepositoryAssociationAsync(new DescribeRepositoryAssociationRequest { AssociationArn = unknown })).RepositoryAssociation.State?.Value ?? "accepted");
    Console.WriteLine($"An association that was never made   -> {missing}");

    Console.WriteLine();
    Console.WriteLine(association.State == RepositoryAssociationState.Associated
        ? "Floci never read the repository: the association read Associated the moment it was made.\n  Code that waits for Associating to end, or handles Failed, never runs here."
        : $"Floci answered {association.State?.Value} right after associating: it may check repositories now. Re-read this lab.");
    Console.WriteLine(reviews.StartsWith("UnknownOperationException", StringComparison.Ordinal)
        ? "Code reviews are not built yet: Floci answers UnknownOperationException."
        : "Floci answered ListCodeReviews: it may have built code reviews. Re-read this lab.");
}
finally
{
    string removed = "nothing";

    if (arn is not null)
    {
        await reviewer.DisassociateRepositoryAsync(new DisassociateRepositoryRequest { AssociationArn = arn });
        removed = "the association";
    }

    List<RepositoryAssociationSummary> left = (await reviewer.ListRepositoryAssociationsAsync(new ListRepositoryAssociationsRequest { Names = [repository] })).RepositoryAssociationSummaries ?? [];

    Console.WriteLine();
    Console.WriteLine($"Cleanup -> removed {removed}; listed afterwards: {(left.Count == 0 ? "none" : string.Join(", ", left.Select(a => a.State?.Value)))}");
}
