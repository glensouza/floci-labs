# S3 Tables in .NET: the rename that changes the ARN

> Create an Apache Iceberg table in an S3 table bucket from a single C# file, keep its ARN the way code does when it writes one into a policy or a config file, then rename the table. AWS documents a table ARN as ending in the table's id, which a rename leaves alone. Floci builds the ARN from the table's name, so the rename changes it, and the saved ARN names a table that no longer exists.

## What it shows

- S3 Tables from .NET with the official `AWSSDK.S3Tables` package, and no Floci-specific library: `CreateTableBucket`, `CreateNamespace`, `CreateTable` (Iceberg), `RenameTable`, `GetTable`, `ListTables`.
- **A rename changes the table's ARN on Floci.** `CreateTable` answers `…/bucket/<name>/table/orders`. After `RenameTable` to `orders_v2`, `GetTable` answers `…/table/orders_v2`, and no table in the bucket carries the ARN that was saved.
- **On AWS it doesn't.** The S3 user guide gives the table ARN format as `arn:aws:s3tables:{region}:{owner-account-id}:bucket/{bucket-name}/table/{table-id}`, and says that you can rename tables, "but each table has its own unique Amazon Resource Name (ARN) and unique table ID". So code that saved an ARN before a rename still names the table on AWS. Test anything that holds a table ARN across a rename (a table policy, a Lake Formation grant, a catalog entry) against AWS, not here.
- The lab prints what it saw and says which way it went, so it tells you if Floci changes.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: S3 Tables
- NuGet: `AWSSDK.S3Tables` 4.0.100.16, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (the bucket name changes every run):

```text
Table bucket                             -> lab-dc870bf8f6cd
CreateTable orders, ARN saved            -> arn:aws:s3tables:us-east-1:000000000000:bucket/lab-dc870bf8f6cd/table/orders
RenameTable orders -> orders_v2          -> arn:aws:s3tables:us-east-1:000000000000:bucket/lab-dc870bf8f6cd/table/orders_v2
A table with the saved ARN               -> none

The rename changed the table's ARN: Floci builds it from the table's name.
  AWS documents a table ARN as .../table/{table-id}, which a rename leaves alone, so a policy or
  config that saved the old ARN still names the table there. Here it names a table that no longer exists.

Cleanup -> removed 1 table(s), the namespace, the table bucket; bucket still listed: no
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`.

**The table has no schema.** `CreateTable` takes an Iceberg schema optionally, and the ARN question doesn't need one. The full sample in the gallery passes a two-column schema.

**Cleanup works inside out:** tables, then the namespace, then the bucket, because S3 Tables won't delete a namespace or a bucket that still has children.

**Don't run this against a real account as written.** It creates real resources. It cleans up after itself, but it was verified against Floci only.

## Try changing...

- Look the table up by its old name after the rename. What does `GetTable` for `orders` answer?
- Rename it back to `orders`. Does the ARN go back too?
- Tag the table bucket with `TagResource`. Floci 2.2.0 hasn't built tagging for S3 Tables yet, and answers HTTP 404 `UnknownOperationException` rather than 501.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
