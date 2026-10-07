#:package AWSSDK.VerifiedPermissions@4.0.100.14

// Amazon Verified Permissions against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Floci evaluates real Cedar, but in a sidecar container it starts itself through the Docker
// socket. Without the socket, the policy store calls answer and the first policy fails. This lab
// creates a store, adds a permit, asks IsAuthorized, adds a forbid and asks again, and prints which
// way Floci went.

using Amazon.Runtime;
using Amazon.VerifiedPermissions;
using Amazon.VerifiedPermissions.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonVerifiedPermissionsConfig config = new AmazonVerifiedPermissionsConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonVerifiedPermissionsClient avp = new AmazonVerifiedPermissionsClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

// OFF: no schema is registered, so STRICT validation would refuse every policy.
CreatePolicyStoreResponse store = await avp.CreatePolicyStoreAsync(new CreatePolicyStoreRequest { ValidationSettings = new ValidationSettings { Mode = ValidationMode.OFF } });
Console.WriteLine($"CreatePolicyStore               -> {store.PolicyStoreId}");

try
{
    string? permitId;

    try
    {
        permitId = await AddPolicy(avp, store.PolicyStoreId, "permit(principal == User::\"alice\", action == Action::\"view\", resource);");
    }
    // The interesting answer. The store call above worked; this is the first call that needs Cedar.
    catch (InternalServerException ex) when (ex.Message.Contains("Cedar sidecar", StringComparison.Ordinal))
    {
        Console.WriteLine($"CreatePolicy (permit)           -> HTTP {(int)ex.StatusCode} {ex.ErrorCode}: {ex.Message}");
        Console.WriteLine();
        Console.WriteLine("Floci could not start its Cedar sidecar. The policy store calls work without it, but every\n  call that parses or evaluates a policy fails. Restart Floci with the Docker socket mounted:\n  -v /var/run/docker.sock:/var/run/docker.sock");

        return;
    }

    Console.WriteLine($"CreatePolicy (permit)           -> {permitId}");
    Console.WriteLine($"IsAuthorized alice              -> {await Decide(avp, store.PolicyStoreId, "alice")}");
    Console.WriteLine($"IsAuthorized bob                -> {await Decide(avp, store.PolicyStoreId, "bob")}");

    string forbidId = await AddPolicy(avp, store.PolicyStoreId, "forbid(principal == User::\"alice\", action, resource);");
    Console.WriteLine($"CreatePolicy (forbid)           -> {forbidId}");

    string after = await Decide(avp, store.PolicyStoreId, "alice");
    Console.WriteLine($"IsAuthorized alice              -> {after}");
    Console.WriteLine();

    Console.WriteLine(after.StartsWith("DENY", StringComparison.Ordinal) && after.Contains(forbidId, StringComparison.Ordinal)
        ? "The Cedar sidecar is up, and the decisions are Cedar's own: bob is denied because nothing\n  permits him, and once a forbid exists it beats alice's permit."
        : "Floci's decision no longer matches Cedar: an applicable forbid should beat any permit.");
}
finally
{
    // Deleting the store takes its policies with it, so a re-run starts clean.
    await avp.DeletePolicyStoreAsync(new DeletePolicyStoreRequest { PolicyStoreId = store.PolicyStoreId });
    Console.WriteLine($"DeletePolicyStore               -> {store.PolicyStoreId}");
}

static async Task<string> AddPolicy(IAmazonVerifiedPermissions avp, string storeId, string statement)
{
    CreatePolicyResponse response = await avp.CreatePolicyAsync(new CreatePolicyRequest
    {
        PolicyStoreId = storeId,
        Definition = new PolicyDefinition { Static = new StaticPolicyDefinition { Statement = statement } },
    });

    return response.PolicyId;
}

static async Task<string> Decide(IAmazonVerifiedPermissions avp, string storeId, string user)
{
    IsAuthorizedResponse response = await avp.IsAuthorizedAsync(new IsAuthorizedRequest
    {
        PolicyStoreId = storeId,
        Principal = new EntityIdentifier { EntityType = "User", EntityId = user },
        Action = new ActionIdentifier { ActionType = "Action", ActionId = "view" },
        Resource = new EntityIdentifier { EntityType = "Photo", EntityId = "holiday" },
    });

    string determining = response.DeterminingPolicies.Count == 0 ? "no policy applies" : $"determined by {string.Join(", ", response.DeterminingPolicies.Select(p => p.PolicyId))}";

    return $"{response.Decision.Value} ({determining})";
}
