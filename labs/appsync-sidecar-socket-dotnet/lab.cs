#:package AWSSDK.AppSync@4.0.100.14

// AWS AppSync against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// The SDK builds the GraphQL API; the query itself is a plain HTTP POST, which is how a production
// client reaches AppSync too. Since Floci 2.2.0 the GraphQL engine, resolvers included, runs in a
// sidecar container that Floci starts through the Docker socket the first time a schema loads. This
// lab times the schema load, runs a resolver, and says which way each went.

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.AppSync;
using Amazon.AppSync.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonAppSyncClient appsync = new AmazonAppSyncClient(credentials, new AmazonAppSyncConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
using HttpClient http = new HttpClient();

const string schema = "schema { query: Query }\ntype Query { echo(text: String!): String }";
const string expected = "hello from Floci";
string? apiId = null;

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    CreateGraphqlApiResponse api = await appsync.CreateGraphqlApiAsync(new CreateGraphqlApiRequest { Name = $"lab-{Guid.NewGuid():N}", AuthenticationType = AuthenticationType.API_KEY });
    apiId = api.GraphqlApi.ApiId;
    CreateApiKeyResponse key = await appsync.CreateApiKeyAsync(new CreateApiKeyRequest { ApiId = apiId });
    Console.WriteLine($"Created GraphQL API {apiId} with an API key");

    // Schema creation is asynchronous on AWS, and on Floci 2.2.0 too: poll until it leaves
    // PROCESSING. 90 s covers a first pull of the sidecar image.
    Stopwatch loading = Stopwatch.StartNew();
    await appsync.StartSchemaCreationAsync(new StartSchemaCreationRequest { ApiId = apiId, Definition = new MemoryStream(Encoding.UTF8.GetBytes(schema)) });
    GetSchemaCreationStatusResponse status = await appsync.GetSchemaCreationStatusAsync(new GetSchemaCreationStatusRequest { ApiId = apiId });
    while (status.Status == SchemaStatus.PROCESSING && loading.Elapsed < TimeSpan.FromSeconds(90))
    {
        await Task.Delay(1000);
        status = await appsync.GetSchemaCreationStatusAsync(new GetSchemaCreationStatusRequest { ApiId = apiId });
    }

    Console.WriteLine($"Schema -> {status.Status} after {loading.Elapsed.TotalSeconds:0} s{(string.IsNullOrEmpty(status.Details) ? "" : $" ({status.Details})")}");

    if (status.Status != SchemaStatus.SUCCESS && status.Status != SchemaStatus.ACTIVE)
    {
        Console.WriteLine();
        Console.WriteLine("The schema did not load. Floci runs its GraphQL engine in a sidecar container it starts\n  through the Docker socket, and without one it sits in PROCESSING while it retries, then fails.\n  Restart Floci with -v /var/run/docker.sock:/var/run/docker.sock and run the lab again.");

        return;
    }

    // A NONE data source and a unit VTL resolver: the request template passes the argument through
    // as the payload, the response template returns it. On AWS, echo(text) returns text.
    await appsync.CreateDataSourceAsync(new CreateDataSourceRequest { ApiId = apiId, Name = "local", Type = DataSourceType.NONE });
    await appsync.CreateResolverAsync(new CreateResolverRequest
    {
        ApiId = apiId,
        TypeName = "Query",
        FieldName = "echo",
        DataSourceName = "local",
        RequestMappingTemplate = "{\"version\":\"2018-05-29\",\"payload\":$util.toJson($ctx.args.text)}",
        ResponseMappingTemplate = "$util.toJson($ctx.result)",
    });
    Console.WriteLine("Resolver Query.echo -> NONE data source, VTL pass-through");
    Console.WriteLine();

    // AWS reports the URL in Uris["GRAPHQL"]. Build the same path from the endpoint instead, so it
    // works whatever host and port Floci is published on.
    string reported = api.GraphqlApi.Uris?.GetValueOrDefault("GRAPHQL") ?? "(none)";
    string url = $"{endpoint}/v1/apis/{apiId}/graphql";
    Console.WriteLine($"Reported GRAPHQL URI: {reported}");
    Console.WriteLine($"Posting to:           {url}");
    Console.WriteLine();

    // A literal, not JsonSerializer: file-based apps turn reflection-based serialization off.
    string body = $$"""{"query":"{ echo(text: \"{{expected}}\") }"}""";

    using HttpRequestMessage withKey = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    withKey.Headers.Add("x-api-key", key.ApiKey.Id);
    using HttpResponseMessage answered = await http.SendAsync(withKey);
    string text = await answered.Content.ReadAsStringAsync();
    Console.WriteLine($"POST with x-api-key    -> HTTP {(int)answered.StatusCode} {text}");

    JsonElement echo = JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("echo");
    Console.WriteLine(echo.ValueKind == JsonValueKind.String && echo.GetString() == expected
        ? "  The resolver ran: the argument came back through the mapping templates, as on AWS."
        : "  The query was accepted, but the resolver returned null: this Floci did not run it.");
    Console.WriteLine();

    using HttpResponseMessage refused = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
    Console.WriteLine($"POST without a key     -> HTTP {(int)refused.StatusCode} {await refused.Content.ReadAsStringAsync()}");
    Console.WriteLine(refused.StatusCode == HttpStatusCode.Unauthorized
        ? "  The API key is enforced."
        : "  The API key was NOT enforced.");
}
finally
{
    // Runs even when a step above throws. Deleting the API takes its key, schema and resolver with
    // it. A schema stuck in PROCESSING blocks the delete, and Floci says so.
    if (apiId is not null)
    {
        try
        {
            await appsync.DeleteGraphqlApiAsync(new DeleteGraphqlApiRequest { ApiId = apiId });
            Console.WriteLine();
            Console.WriteLine("Cleanup -> GraphQL API deleted");
        }
        catch (ConcurrentModificationException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Cleanup -> refused while the schema is still loading: {ex.Message}");
        }
    }
}
