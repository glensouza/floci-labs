# S3 Vectors in .NET: the prefix that filters nothing

> Build a tiny semantic search in Amazon S3 Vectors from a single C# file: a vector bucket, a cosine index, four films as points in space, a nearest-neighbour query and a metadata filter. Floci answers all of that the way S3 Vectors does. Then list vector buckets by prefix, the way code finds its own bucket by name. AWS filters by the prefix. Floci ignores it and answers every bucket.

## What it shows

- S3 Vectors from .NET with the official `AWSSDK.S3Vectors` package, and no Floci-specific library: `CreateVectorBucket`, `CreateIndex` (float32, 3 dimensions, cosine), `PutVectors` with metadata, `QueryVectors` with `TopK`, `ReturnDistance` and a `Filter`, `ListVectorBuckets`, `DeleteIndex`, `DeleteVectorBucket`.
- **The search works.** The two films nearest `[1, 0.1, 0]` are `alien 0.005` and `blade-runner 0.025`. That number is a cosine distance, so lower is closer. Filtered to `romance`, the same query still returns two films, `amelie 0.900` and `notting-hill 0.911`, neither of which made the unfiltered top two.
- **`ListVectorBuckets` ignores `Prefix` on Floci.** Asked for buckets starting `no-such-prefix-`, it lists the lab's own `lab-…` bucket. The SDK documents `Prefix` as "Limits the response to vector buckets that begin with the specified prefix", so on AWS that list is empty. Code that takes the first bucket a prefix lists, or reads "listed" as "mine and still there", works on AWS and picks up someone else's bucket here. Match the name yourself.
- The lab prints what it saw and says which way it went, so it tells you if Floci changes.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: S3 Vectors
- NuGet: `AWSSDK.S3Vectors` 4.0.101.1, pinned in the `#:package` line at the top of `lab.cs`
- Last verified against: Floci 2.2.0 (`floci/floci:latest`, October 2026)

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The lab assumes Floci is running on port 4566. If it isn't:

```bash
docker run -d --name floci -p 4566:4566 floci/floci:latest
```

Then:

```bash
dotnet run lab.cs
```

Expected output on a fresh Floci (the bucket name changes every run; on a Floci with other vector buckets, the prefix line lists those too):

```text
Vector bucket                        -> lab-685bb1fce6cb
Index films, 3 dimensions, cosine    -> 4 vectors put
Two nearest to [1, 0.1, 0]           -> alien 0.005, blade-runner 0.025
The same, filtered to romance        -> amelie 0.900, notting-hill 0.911
ListVectorBuckets, Prefix no-such-prefix- -> 1 bucket(s): lab-685bb1fce6cb

Floci ignored the prefix and answered every bucket. AWS filters by it, so code that takes
  the first bucket a prefix lists, or treats "listed" as "mine and still there", works on AWS
  and picks up someone else's bucket here. Match the name yourself; it costs one Where.

Cleanup -> removed the index and the vector bucket; bucket still listed: no
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**`Filter` is a `Document`, which is a struct.** You can't assign `null` to it for "no filter"; leave it unset.

**The cleanup check matches the name exactly**, for the reason this lab exists. That's how this gotcha turned up: a "bucket still listed" check that trusted the prefix said yes for a bucket that was already gone.

**Cleanup deletes the index first,** because S3 Vectors refuses to delete a bucket that still holds one. Floci enforces that too: HTTP 409 `ConflictException`.

**Don't run this against a real account as written.** It creates real resources. It cleans up after itself, but it was verified against Floci only.

## Try changing...

- Pass `MaxResults = 1` to `ListVectorBuckets` with two buckets in the account. Floci 2.2.0 ignores that too: both come back, with no `NextToken`.
- Put a vector of two dimensions into the three-dimensional index. Floci refuses it with `ValidationException`, as AWS does.
- Put a bucket policy on the bucket with `PutVectorBucketPolicy`. Floci 2.2.0 hasn't built vector bucket policies, and answers HTTP 404 `UnknownOperationException` rather than 501.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
