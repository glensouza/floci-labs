#:package AWSSDK.APIGateway@4.0.101

// API Gateway (REST APIs) against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The only Floci-specific lines are the endpoint, the dummy credentials, MaxErrorRetry and the
// invoke URL. Everything before the invoke is ordinary AWSSDK.APIGateway code.

using System.Net;
using Amazon.APIGateway;
using Amazon.APIGateway.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonAPIGatewayClient apigateway = new AmazonAPIGatewayClient(credentials, new AmazonAPIGatewayConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
using HttpClient http = new HttpClient();

const string stage = "demo";
const string message = "hello from Floci";
string? restApiId = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    // A GET /probe that answers from a MOCK integration, so no Lambda or backend is needed.
    CreateRestApiResponse api = await apigateway.CreateRestApiAsync(new CreateRestApiRequest { Name = $"lab-{Guid.NewGuid():N}" });
    restApiId = api.Id;
    CreateResourceResponse resource = await apigateway.CreateResourceAsync(new CreateResourceRequest { RestApiId = restApiId, ParentId = api.RootResourceId, PathPart = "probe" });
    await apigateway.PutMethodAsync(new PutMethodRequest { RestApiId = restApiId, ResourceId = resource.Id, HttpMethod = "GET", AuthorizationType = "NONE" });
    await apigateway.PutIntegrationAsync(new PutIntegrationRequest
    {
        RestApiId = restApiId,
        ResourceId = resource.Id,
        HttpMethod = "GET",
        Type = IntegrationType.MOCK,
        RequestTemplates = new Dictionary<string, string> { ["application/json"] = """{"statusCode": 200}""" },
    });
    await apigateway.PutMethodResponseAsync(new PutMethodResponseRequest { RestApiId = restApiId, ResourceId = resource.Id, HttpMethod = "GET", StatusCode = "200" });
    await apigateway.PutIntegrationResponseAsync(new PutIntegrationResponseRequest
    {
        RestApiId = restApiId,
        ResourceId = resource.Id,
        HttpMethod = "GET",
        StatusCode = "200",
        ResponseTemplates = new Dictionary<string, string> { ["application/json"] = $$"""{"message": "{{message}}"}""" },
    });
    await apigateway.CreateDeploymentAsync(new CreateDeploymentRequest { RestApiId = restApiId, StageName = stage });
    Console.WriteLine($"Created and deployed REST API {restApiId}, stage \"{stage}\", GET /probe (MOCK integration)");
    Console.WriteLine();

    // 1. The URL real API Gateway would give you. Floci has nothing to serve it from.
    string awsUrl = $"https://{restApiId}.execute-api.us-east-1.amazonaws.com/{stage}/probe";
    Console.WriteLine($"Real AWS serves it here: {awsUrl}");
    Console.WriteLine("  (a separate execute-api domain; the SDK never calls it, you call it with plain HTTP)");

    // 2. The same path on Floci's own port, without the special segment.
    await ShowAsync("Same shape on Floci's port", $"{endpoint}/{stage}/probe");

    // 3. Floci serves deployed stages from its one port, under _user_request_.
    string flociUrl = $"{endpoint}/restapis/{restApiId}/{stage}/_user_request_/probe";
    await ShowAsync("Floci's invoke URL", flociUrl);
}
finally
{
    // Runs even when a step above throws.
    if (restApiId is not null)
    {
        await apigateway.DeleteRestApiAsync(new DeleteRestApiRequest { RestApiId = restApiId });
    }

    Console.WriteLine();
    Console.WriteLine("Cleanup -> REST API deleted");
}

async Task ShowAsync(string label, string url)
{
    using HttpResponseMessage response = await http.GetAsync(url);
    string body = (await response.Content.ReadAsStringAsync()).Trim();
    Console.WriteLine($"{label}: GET {url}");
    Console.WriteLine($"  -> {(int)response.StatusCode} {response.StatusCode}: {(body.Length > 120 ? body[..120] + "..." : body)}");
    if (response.StatusCode == HttpStatusCode.OK && body.Contains(message, StringComparison.Ordinal))
    {
        Console.WriteLine("  The mock response you wired up came back: the deployed API answers requests.");
    }
}
