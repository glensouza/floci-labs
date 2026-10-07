# Verified Permissions in .NET: Floci runs real Cedar, in a sidecar it needs the Docker socket for

> Create a Verified Permissions policy store from a single C# file, add a Cedar permit and a forbid, and ask `IsAuthorized` who can view a photo. Floci 2.1.0 evaluates real Cedar, but in a sidecar container it starts through the Docker socket. Without the socket, the policy store calls work and the first `CreatePolicy` fails with an HTTP 500.

## What it shows

- Amazon Verified Permissions from .NET with the official `AWSSDK.VerifiedPermissions` package, and no Floci-specific library.
- **The decisions are Cedar's own.** A permit lets `alice` view the photo, `bob` is denied because no policy mentions him (Cedar is default-deny), and once a `forbid` for `alice` exists it beats her permit. Each answer names the policy that determined it.
- **Cedar runs in a sidecar.** Floci starts a `floci-cedar` container through the Docker socket the first time it has to parse a policy. With no socket mounted, `CreatePolicyStore` still works and `CreatePolicy` answers `HTTP 500 InternalServerException: Failed to call Cedar sidecar for policy parsing: java.net.SocketException: No such file or directory`.
- The lab says which way Floci went: the full permit/forbid run, or what to mount when the sidecar can't start.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Amazon Verified Permissions
- NuGet: `AWSSDK.VerifiedPermissions` 4.0.100.14, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.1.0 (`floci/floci:latest`, October 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566 **with the Docker socket mounted**. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 -v /var/run/docker.sock:/var/run/docker.sock floci/floci:latest
```

On Windows with Git Bash, prefix that with `MSYS_NO_PATHCONV=1`, or Git Bash rewrites the socket path and Docker refuses it.

Then:

```bash
dotnet run lab.cs
```

Expected output with the socket mounted (the ids differ on every run). The first time, Floci also pulls the `floci/floci:latest-cedar` image, so the first `CreatePolicy` waits on that download:

```text
CreatePolicyStore               -> PS7375d0df7fa24f5a8c14
CreatePolicy (permit)           -> SP41739cbc368f4c358250
IsAuthorized alice              -> ALLOW (determined by SP41739cbc368f4c358250)
IsAuthorized bob                -> DENY (no policy applies)
CreatePolicy (forbid)           -> SP1b36559a2366424f8d08
IsAuthorized alice              -> DENY (determined by SP1b36559a2366424f8d08)

The Cedar sidecar is up, and the decisions are Cedar's own: bob is denied because nothing
  permits him, and once a forbid exists it beats alice's permit.
DeletePolicyStore               -> PS7375d0df7fa24f5a8c14
```

And without the socket:

```text
CreatePolicyStore               -> PS3efa0d1119ba4fb49cca
CreatePolicy (permit)           -> HTTP 500 InternalServerException: Failed to call Cedar sidecar for policy parsing: java.net.SocketException: No such file or directory

Floci could not start its Cedar sidecar. The policy store calls work without it, but every
  call that parses or evaluates a policy fails. Restart Floci with the Docker socket mounted:
  -v /var/run/docker.sock:/var/run/docker.sock
DeletePolicyStore               -> PS3efa0d1119ba4fb49cca
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. Everything else is the same code you would run against AWS.

**Validation is off.** The store is created with `ValidationMode.OFF` because the lab registers no schema. With `STRICT` and no schema, every policy would be refused.

**The store is deleted in a `finally`.** Deleting a policy store takes its policies with it, so a re-run starts clean, including the run that stopped at the sidecar error.

**Why this matters.** The failure is easy to misread. The store calls work, so the emulator looks healthy and the service looks supported, and then the first policy is a 500 that mentions a socket. Nothing on AWS works like this. The fix isn't in your code; it's one volume mount on the Floci container.

## Try changing...

- Ask about `alice` doing something other than `view`, before and after the forbid. The permit names one action; the forbid names none.
- Send a statement that isn't Cedar, such as `"not a policy"`. Floci refuses it with `ValidationException`, though the message is the Cedar parser's, not AWS's wording.
- Create the store with `ValidationMode.STRICT` and add the permit again.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with the decisions checked against the determining policy, a refused non-Cedar statement, and the store found by its description and deleted in a `finally`, plus a Blazor demo page, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab/tree/main/samples/aws/verifiedpermissions). (The gallery has no policy-engine sample for the other three clouds yet, so there's no other-cloud version of this one.)
