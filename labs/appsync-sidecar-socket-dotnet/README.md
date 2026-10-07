# AppSync in .NET: resolvers run, in a sidecar that needs the Docker socket

> Build a GraphQL API on AWS AppSync from a single C# file, attach a VTL resolver, and query it over HTTP with and without the API key. Floci 2.2.0 runs the resolver and echoes the argument back, as AWS does. It does that in a sidecar container it starts through the Docker socket, so a Floci started without the socket can't load a schema at all.

## What it shows

- AppSync from .NET with the official `AWSSDK.AppSync` package, and no Floci-specific library. The SDK is AppSync's management plane only; the query is a plain HTTP POST, as it is in production.
- **The resolver runs.** A unit VTL resolver over a `NONE` data source passes `echo(text)` through, and the query returns `{"data":{"echo":"hello from Floci"}}`. Floci 2.1.0 answered `null` for the same field; the lab says which way it went.
- **The key is enforced.** The same query without `x-api-key` gets `401 UnauthorizedException`.
- **Schema loading needs the socket.** Floci 2.2.0 starts its GraphQL engine (`floci/floci-sidecar-graphql`, container `floci-aws-graphql`) through `/var/run/docker.sock` the first time a schema loads. Without the socket the schema sits in `PROCESSING` for 30–50 seconds while Floci retries, then goes `FAILED`. The lab prints Floci's reason and how to fix it.
- **Don't use the URI Floci reports.** `Uris["GRAPHQL"]` says `http://localhost:4566/...` whatever host and port Floci is published on, so the lab builds the URL from its own endpoint.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AppSync
- NuGet: `AWSSDK.AppSync` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.2.0 (`floci/floci:latest`, October 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Docker. The lab assumes Floci is running on port 4566 **with the Docker socket mounted**. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 -v /var/run/docker.sock:/var/run/docker.sock floci/floci:latest
```

Then:

```bash
dotnet run lab.cs
```

Expected output (the ids differ on every run; the first run on a machine also pulls the sidecar image, which takes longer):

```text
Created GraphQL API dbd6999a58ba4ab0969143a085 with an API key
Schema -> SUCCESS after 1 s
Resolver Query.echo -> NONE data source, VTL pass-through

Reported GRAPHQL URI: http://localhost:4566/v1/apis/dbd6999a58ba4ab0969143a085/graphql
Posting to:           http://127.0.0.1:4567/v1/apis/dbd6999a58ba4ab0969143a085/graphql

POST with x-api-key    -> HTTP 200 {"data":{"echo":"hello from Floci"}}
  The resolver ran: the argument came back through the mapping templates, as on AWS.

POST without a key     -> HTTP 401 {"errors":[{"errorType":"UnauthorizedException","message":"Missing authorization header"}]}
  The API key is enforced.

Cleanup -> GraphQL API deleted
```

Without the socket:

```text
Created GraphQL API 1f3bd5ce387f420bb5399f8303 with an API key
Schema -> FAILED after 31 s (Failed to call GraphQL sidecar for schema validation: java.net.SocketException: No such file or directory)

The schema did not load. Floci runs its GraphQL engine in a sidecar container it starts
  through the Docker socket, and without one it sits in PROCESSING while it retries, then fails.
  Restart Floci with -v /var/run/docker.sock:/var/run/docker.sock and run the lab again.
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only a few lines know about Floci:** `ServiceURL`, the `test`/`test` credentials, `MaxErrorRetry = 0` and the GraphQL URL built from the endpoint.

**Schema creation is polled.** It's asynchronous on AWS, and now on Floci too. The lab waits up to 90 seconds, which covers a first pull of the sidecar image.

**The sidecar outlives Floci.** `floci-aws-graphql` keeps running after you stop Floci, and is shared by every Floci on the machine. Remove it with `docker rm -f floci-aws-graphql` when you're done.

**It cleans up.** Deleting the API takes its key, schema, data source and resolver with it, in a `finally`, so running the lab twice gives the same result.

## Try changing...

- Swap the VTL templates for an `APPSYNC_JS` resolver (`Runtime = { Name = "APPSYNC_JS", RuntimeVersion = "1.0.0" }` and a `Code` string). Floci 2.2.0's release notes say it runs those through a Node sidecar; this lab hasn't tried it.
- Put a deliberate error in the request template. GraphQL reports it in an `errors` array inside an HTTP 200, so a status check alone won't see it.
- Query a field that doesn't exist in the schema and look at the status code.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with the GraphQL `errors` check, by-name cleanup and a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/appsync). (None of Floci's other emulators has a GraphQL service, so there's no other-cloud version of this one.)
