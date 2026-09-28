# SSM Parameter Store in .NET, and the SecureString that isn't secure

> Write, version, read and delete SSM parameters from a single C# file, using the official AWS SDK, then catch the one place Floci behaves unlike real AWS.

## What it shows

- Parameter Store from .NET with **one official package** (`AWSSDK.SimpleSystemsManagement`) and no Floci-specific library. Three lines of config are the whole emulator story.
- `PutParameter` is **not an upsert**. A second put without `Overwrite = true` gets `ParameterAlreadyExists`, and Floci gets that right.
- `Overwrite` creates **version 2**. It doesn't replace version 1.
- **The gotcha:** a `SecureString` read with `WithDecryption = false` comes back as **plaintext** on Floci. Real SSM encrypts it with KMS and returns ciphertext. Floci is fine for checking that your code makes the right calls. It can't tell you whether your secrets handling or KMS key policy works.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: SSM Parameter Store
- NuGet: `AWSSDK.SimpleSystemsManagement` 4.0.102, pinned in the `#:package` line at the top of `lab.cs`
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
PutParameter            -> version 1
PutParameter (no flag)  -> ParameterAlreadyExists, as real SSM does
PutParameter Overwrite  -> version 2
GetParameter            -> "hello, v2" (version 2)

SecureString, WithDecryption = false -> "hunter2"
  Plaintext. On Floci a SecureString is not encrypted, so don't use it to test your secrets handling or KMS policy.

DeleteParameters        -> removed 2
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool. Using the literal address avoids that.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials (Floci parses SigV4 but doesn't verify it), and `MaxErrorRetry = 0`. The retry setting isn't needed for correctness. The SDK default is 4 retries with backoff, so a stopped emulator takes about 8 seconds per call to report the failure. Delete those three lines and this is the code you would run against real AWS.

**Every run uses a fresh GUID path** (`/floci-labs/<guid>/...`), and cleanup runs in a `finally`. A crashed run can't break the next one, and you can run the lab repeatedly.

**The read checks the value**, not just the HTTP 200. A demo that goes green on any 200 will call a broken emulator working.

## Try changing...

- Read the plain parameter by version, `plainName + ":1"`. Real SSM returns version 1. On Floci 2.1.0 this returned `ParameterNotFound`, even though the versions are recorded. Labels (`:stable`) behave the same way.
- Replace `GetParameter` with `GetParametersByPath` on `prefix` with `Recursive = true`. This is how most apps load their config at startup.
- Remove the `Overwrite = true` and watch step 3 fail the same way step 2 does.
- Point it at real AWS by deleting the three Floci lines, and see that the `SecureString` comes back as ciphertext.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/floci](https://github.com/glensouza/floci)
