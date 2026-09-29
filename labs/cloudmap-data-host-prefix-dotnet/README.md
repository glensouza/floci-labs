# Cloud Map in .NET: service discovery on localhost, and the one call that goes to a different host

> Register an instance in AWS Cloud Map from a single C# file, then discover it by name. The catch: every Cloud Map call works against Floci with the SDK's defaults except `DiscoverInstances`, the one your application actually makes. The SDK sends it to `data-` + your endpoint's host, and `data-127.0.0.1` does not exist.

## What it shows

- Cloud Map from .NET with the official `AWSSDK.ServiceDiscovery` package, and no Floci-specific library.
- **The control plane works as it is:** an HTTP namespace, a service and an instance with attributes, each asynchronous step waited on through `GetOperation`, then deregistered and deleted in the order Cloud Map requires.
- **The data plane needs one more setting.** `DiscoverInstances` is the only data-plane operation in the API. On AWS it is served by `data-servicediscovery.<region>.amazonaws.com`, so the SDK prefixes the host with `data-`. Pointed at `http://127.0.0.1:4566`, that becomes `data-127.0.0.1`, which does not resolve. `DisableHostPrefixInjection = true` sends it to the endpoint as given, where Floci serves both planes. The lab tries it both ways and prints which way it went, so it tells you if that ever changes.
- **Operations are `SUCCESS` at once.** Real Cloud Map takes seconds to a minute to finish creating a namespace or registering an instance. Floci's `GetOperation` says `SUCCESS` on the first poll. Keep the wait anyway; AWS needs it.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Cloud Map (Service Discovery)
- NuGet: `AWSSDK.ServiceDiscovery` 4.0.101.10, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (ids change every run; the "No such host" text comes from the operating system's resolver, and this run was on Windows):

```text
CreateHttpNamespace           -> ns-9wze46v4tkq08fj6g4ti, operation SUCCESS after 1 poll(s), 0.0 s
CreateService                 -> srv-krwjbdkxdn32vfawub5d
RegisterInstance              -> web-1, operation SUCCESS after 1 poll(s), 0.0 s
DiscoverInstances (default)   -> failed: No such host is known. (data-127.0.0.1:4567)
DiscoverInstances (no prefix) -> 1 instance(s): web-1 192.0.2.10:8080 HEALTHY

With the SDK's defaults, the one data-plane call failed while every control-plane call
  worked: the SDK sent DiscoverInstances to "data-" + the endpoint's host. Against an
  emulator, set DisableHostPrefixInjection = true on the client config; against AWS, leave it.

Cleanup -> instance deregistered, service and namespace deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only four lines know about Floci:** `ServiceURL`, the `test`/`test` credentials, `MaxErrorRetry = 0` and `DisableHostPrefixInjection = true`. The last one is what this lab is about, and it belongs on the emulator path only: real AWS serves `DiscoverInstances` from the `data-` host, so a production client keeps the prefix.

**Why `curl` never shows this.** Cloud Map is JSON-RPC: one `POST /` with an `X-Amz-Target` header. A `curl` to `http://127.0.0.1:4566/` with `X-Amz-Target: Route53AutoNaming_v20170314.DiscoverInstances` works fine, because curl sends it where you tell it to. The prefix is added by the SDK, so only SDK code hits it.

**Asynchronous operations are waited on.** `CreateHttpNamespace`, `RegisterInstance` and `DeregisterInstance` answer with an operation id, not a result. The namespace's own id is only in the finished operation's targets. The wait polls every 5 seconds for up to 90, which is sized for AWS; on Floci it returns on the first poll.

**Nothing registered can reach a real host.** The namespace is under `.test` (reserved by RFC 2606) and the instance address is `192.0.2.10` (TEST-NET-1, RFC 5737).

## Try changing...

- Delete `DisableHostPrefixInjection = true` from the second client. (Both `DiscoverInstances` calls fail the same way.)
- Add `QueryParameters = new Dictionary<string, string> { ["stage"] = "green" }` to the discover request, and a `stage` attribute to the instance. (Floci filters on it: a value that doesn't match returns no instances.)
- Delete the namespace before the service. (Refused with `ResourceInUse`, as on AWS.)

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
