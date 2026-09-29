# AppSync in .NET: a GraphQL API on localhost, and the resolver that returns null

> Build an AWS AppSync GraphQL API from a single C# file, attach a resolver, and query it over plain HTTP with an API key. The catch: on Floci today, the schema loads and the key is enforced, but the resolver's field comes back `null`.

## What it shows

- AppSync from .NET with the official `AWSSDK.AppSync` package, and no Floci-specific library. The SDK builds the API; the query is a plain `HttpClient` POST, which is how a production client reaches AppSync too.
- **The whole setup works:** `CreateGraphqlApi` with `API_KEY` auth, `CreateApiKey`, `StartSchemaCreation` (polled until it leaves `PROCESSING`, as on AWS), a `NONE` data source, and a unit VTL resolver on `Query.echo` that passes its argument through.
- **The API key is enforced.** The same query without `x-api-key` gets `401` and an `UnauthorizedException`.
- **The resolver doesn't run.** With the key, Floci validates the query against the schema and answers `200 {"data":{"echo":null}}`. On AWS the same resolver returns the string you sent. The lab prints which way it went, so it tells you when Floci starts running resolvers.
- **The reported URL is wrong.** `Uris["GRAPHQL"]` comes back as `http://localhost:4566/...`: `localhost`, and Floci's in-container port even when you published it elsewhere. The lab builds the same path from its own endpoint instead.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: AppSync
- NuGet: `AWSSDK.AppSync` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
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
Created GraphQL API 59ab9c2a861d443082ed475b6f with an API key
Schema -> SUCCESS
Resolver Query.echo -> NONE data source, VTL pass-through

Reported GRAPHQL URI: http://localhost:4566/v1/apis/59ab9c2a861d443082ed475b6f/graphql
Posting to:           http://127.0.0.1:4566/v1/apis/59ab9c2a861d443082ed475b6f/graphql

POST with x-api-key    -> HTTP 200 {"data":{"echo":null}}
  The query was accepted and validated, but the resolver returned null. This Floci does not run resolvers yet.

POST without a key     -> HTTP 401 {"errors":[{"errorType":"UnauthorizedException","message":"Missing authorization header"}]}
  The API key is enforced.

Cleanup -> GraphQL API deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool. That is also why the lab ignores the `localhost` URI Floci reports.

**The GraphQL URL differs from AWS.** Real AppSync hands out `https://{id}.appsync-api.{region}.amazonaws.com/graphql`. Floci serves it at `/v1/apis/{apiId}/graphql` on its own port.

**Only a few lines know about Floci:** `ServiceURL`, the `test`/`test` credentials, `MaxErrorRetry = 0`, and that URL.

**The API gets a fresh name each run**, and the `finally` deletes it, which takes the key, schema, data source and resolver with it.

## Try changing...

- Drop the resolver and query `echo` again. Is the answer any different from the one with a resolver?
- Send `{ nope }` and see what error Floci returns for a field the schema doesn't have.
- Switch the resolver to `APPSYNC_JS` (`Runtime` and `Code` instead of the two templates) and see whether it behaves differently.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
