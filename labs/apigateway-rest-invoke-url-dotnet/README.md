# API Gateway REST APIs in .NET: build one, deploy it, and find the URL that actually answers

> Create a REST API with a MOCK integration from a single C# file, deploy it, and call it over plain HTTP. The interesting part is which URL works: it isn't the one AWS gives you.

## What it shows

- API Gateway (REST APIs) from .NET using the official `AWSSDK.APIGateway` package, and no Floci-specific library. A `GET /probe` method with a MOCK integration answers without a Lambda or any backend, so the whole round trip needs one package.
- **Configuring an API isn't the same as calling it.** Every SDK call in the lab succeeds whether or not the deployed stage answers anything, so the lab finishes by making the request and checking the mock response body came back.
- **The invoke URL is the one place your code branches.** Real API Gateway serves a deployed stage from its own `execute-api` domain: `https://{id}.execute-api.{region}.amazonaws.com/{stage}/{path}`. Floci serves it from its single port under a special segment: `{endpoint}/restapis/{id}/{stage}/_user_request_/{path}`.
- **The quiet one:** the AWS-shaped path on Floci's port (`{endpoint}/demo/probe`) doesn't say "no such API". It answers `404` with an S3 `NoSuchBucket` error, because that port also serves S3 and that handler picks the request up. If you see `NoSuchBucket` from an API Gateway call, check the URL first.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: API Gateway (REST APIs)
- NuGet: `AWSSDK.APIGateway` 4.0.101, pinned in the `#:package` line at the top of `lab.cs`
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
Created and deployed REST API 5507d0cca9, stage "demo", GET /probe (MOCK integration)

Real AWS serves it here: https://5507d0cca9.execute-api.us-east-1.amazonaws.com/demo/probe
  (a separate execute-api domain; the SDK never calls it, you call it with plain HTTP)
Same shape on Floci's port: GET http://127.0.0.1:4566/demo/probe
  -> 404 NotFound: <?xml version="1.0" encoding="UTF-8"?><Error><Code>NoSuchBucket</Code>...
Floci's invoke URL: GET http://127.0.0.1:4566/restapis/5507d0cca9/demo/_user_request_/probe
  -> 200 OK: {"message": "hello from Floci"}
  The mock response you wired up came back: the deployed API answers requests.

Cleanup -> REST API deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**A MOCK integration needs both halves.** A method and a method response, and an integration and an integration response. The integration response's template is what produces the body.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials, and `MaxErrorRetry = 0`, plus the invoke URL. Everything else is what you'd write against AWS.

**The API gets a fresh name each run**, and the `finally` deletes it, so re-runs are idempotent.

## Try changing...

- Call a path that doesn't exist under the working invoke URL, or use `POST`, and compare the status codes with real API Gateway.
- Change the integration response's status code to `201` and add a response header.
- Deploy a second stage and check that both answer.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
