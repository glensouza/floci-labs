# API Gateway WebSockets in .NET: push to a live client, and the trick for learning its connection id

> Build a WebSocket API from a single C# file, connect to it with `ClientWebSocket`, and push a message down the open socket from the server side. The catch: no API call tells you the connection's id.

## What it shows

- API Gateway v2 WebSocket APIs from .NET using the official `AWSSDK.ApiGatewayV2` and `AWSSDK.ApiGatewayManagementApi` packages, and no Floci-specific library. **That is two packages**: AWS splits the service, one to build the API and one to talk to the clients connected to it. The client end of the socket is the BCL's `ClientWebSocket`.
- **There is no "what is my connection id" call.** A real backend learns it from the `$connect` event. This API has no `$connect` route to read it from, so the lab sends a frame no route matches (`{"action":"whoami"}`, when the only route is `ping`) and Floci answers with an error that carries it: `{"message":"No route found","connectionId":"...","requestId":"..."}`. Read the `connectionId` and don't depend on the message text.
- **The round trip.** `GetConnection` returns the connection's `connectedAt` and source IP. `PostToConnection` from the SDK arrives as a frame on the socket the client opened, and the lab reads it back to prove it.
- The API carries one MOCK-backed `ping` route that nothing calls. Real API Gateway won't deploy a WebSocket API with no routes, so a lab that skips it would work here and fail on AWS.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: API Gateway v2 (WebSocket), API Gateway Management API
- NuGet: `AWSSDK.ApiGatewayV2` 4.0.100.14 and `AWSSDK.ApiGatewayManagementApi` 4.0.100.15, pinned in the `#:package` lines at the top of `lab.cs`
- Last verified against: Floci 2.1.0 (`floci/floci:latest`, September 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then:

```bash
dotnet run lab.cs
```

Expected output:

```text
Created WebSocket API 1ef1ec3059, route "ping", stage "demo"
Connected to ws://127.0.0.1:4566/ws/1ef1ec3059/demo

SEND {"action":"whoami"}   (no route called "whoami")
RECEIVE {"message":"No route found","connectionId":"xlcE8YPU7jHF","requestId":"..."}
  connectionId = xlcE8YPU7jHF
  The message text is Floci's wording; read the connectionId and nothing else.

GetConnection -> connectedAt 2026-09-28T23:49:31.9020000Z, sourceIp 172.17.0.1
PostToConnection "pushed by Floci" -> the client received: pushed by Floci

Cleanup -> WebSocket API deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Two URLs differ from AWS.** Real API Gateway hands out `wss://{apiId}.execute-api.{region}.amazonaws.com/{stage}` for the socket. Floci serves it on its own port at `/ws/{apiId}/{stage}`, and the management API at `/execute-api/{apiId}/{stage}`. The management client is scoped to one API and stage, which is how AWS shapes it too.

**Only a few lines know about Floci:** `ServiceURL`, the `test`/`test` credentials, `MaxErrorRetry = 0`, and those two URLs.

**The API gets a fresh name each run**, and the `finally` deletes it, which drops the stage and the connection with it.

## Try changing...

- Add a `$connect` route with a MOCK integration, and see whether the handshake still works.
- Send `{"action":"ping"}` and see what the MOCK route sends back.
- Call `DeleteConnection` and read the socket until the server closes it.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
