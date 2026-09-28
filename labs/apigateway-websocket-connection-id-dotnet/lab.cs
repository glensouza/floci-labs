#:package AWSSDK.ApiGatewayV2@4.0.100.14
#:package AWSSDK.ApiGatewayManagementApi@4.0.100.15

// API Gateway WebSocket APIs against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Two packages, both official, because AWS splits the service: ApiGatewayV2 builds the API and
// ApiGatewayManagementApi talks to the clients connected to it. The Floci-specific lines are the
// endpoint, the dummy credentials, MaxErrorRetry and the two URLs.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.ApiGatewayV2;
using Amazon.ApiGatewayV2.Model;
using Amazon.Runtime;
using ProtocolType = Amazon.ApiGatewayV2.ProtocolType;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonApiGatewayV2Client apigateway = new AmazonApiGatewayV2Client(credentials, new AmazonApiGatewayV2Config { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

const string stage = "demo";
const string pushed = "pushed by Floci";
string? apiId = null;
using ClientWebSocket socket = new ClientWebSocket();

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    // Real API Gateway won't deploy a WebSocket API with no routes, so give it one (MOCK, never called).
    CreateApiResponse api = await apigateway.CreateApiAsync(new CreateApiRequest { Name = $"lab-{Guid.NewGuid():N}", ProtocolType = ProtocolType.WEBSOCKET, RouteSelectionExpression = "$request.body.action" });
    apiId = api.ApiId;
    CreateIntegrationResponse integration = await apigateway.CreateIntegrationAsync(new CreateIntegrationRequest { ApiId = apiId, IntegrationType = IntegrationType.MOCK });
    await apigateway.CreateRouteAsync(new CreateRouteRequest { ApiId = apiId, RouteKey = "ping", Target = $"integrations/{integration.IntegrationId}" });
    await apigateway.CreateStageAsync(new CreateStageRequest { ApiId = apiId, StageName = stage, AutoDeploy = true });
    Console.WriteLine($"Created WebSocket API {apiId}, route \"ping\", stage \"{stage}\"");

    // Real AWS hands out wss://{apiId}.execute-api.{region}.amazonaws.com/{stage}; Floci serves the
    // socket on its own port instead.
    Uri webSocketUrl = new Uri($"{endpoint.Replace("http", "ws")}/ws/{apiId}/{stage}");
    await socket.ConnectAsync(webSocketUrl, CancellationToken.None);
    Console.WriteLine($"Connected to {webSocketUrl}");
    Console.WriteLine();

    // No API call returns a connection's id: a real backend reads it from the $connect event. With no
    // $connect route, send a frame no route matches and read the id out of the error that comes back.
    await socket.SendAsync(Encoding.UTF8.GetBytes("""{"action":"whoami"}"""), WebSocketMessageType.Text, true, CancellationToken.None);
    string frame = await ReceiveAsync();
    Console.WriteLine("SEND {\"action\":\"whoami\"}   (no route called \"whoami\")");
    Console.WriteLine($"RECEIVE {frame}");
    string connectionId = JsonDocument.Parse(frame).RootElement.GetProperty("connectionId").GetString()!;
    Console.WriteLine($"  connectionId = {connectionId}");
    Console.WriteLine("  The message text is Floci's wording; read the connectionId and nothing else.");
    Console.WriteLine();

    // The management API is scoped to one API and stage, which is how AWS shapes it too.
    using AmazonApiGatewayManagementApiClient management = new AmazonApiGatewayManagementApiClient(credentials, new AmazonApiGatewayManagementApiConfig { ServiceURL = $"{endpoint}/execute-api/{apiId}/{stage}", AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });
    GetConnectionResponse connection = await management.GetConnectionAsync(new GetConnectionRequest { ConnectionId = connectionId });
    Console.WriteLine($"GetConnection -> connectedAt {connection.ConnectedAt:O}, sourceIp {connection.Identity?.SourceIp}");

    using MemoryStream data = new MemoryStream(Encoding.UTF8.GetBytes(pushed));
    await management.PostToConnectionAsync(new PostToConnectionRequest { ConnectionId = connectionId, Data = data });
    Console.WriteLine($"PostToConnection \"{pushed}\" -> the client received: {await ReceiveAsync()}");
}
finally
{
    // Runs even when a step above throws. Deleting the API drops its stage and connections with it.
    if (apiId is not null)
    {
        await apigateway.DeleteApiAsync(new DeleteApiRequest { ApiId = apiId });
    }

    Console.WriteLine();
    Console.WriteLine("Cleanup -> WebSocket API deleted");
}

async Task<string> ReceiveAsync()
{
    using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    byte[] buffer = new byte[8192];
    ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
    return Encoding.UTF8.GetString(buffer, 0, result.Count);
}
