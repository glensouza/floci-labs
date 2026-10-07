# ACM in .NET: a DNS-validated certificate on localhost, issued before you validate it

> Request an AWS Certificate Manager certificate from a single C# file, then wait for it to be issued the way production code has to. The catch: on Floci the certificate is already `ISSUED` the moment you ask for it, so the validation CNAME you must create for AWS is never needed here, and nothing tells you if your code forgets it.

## What it shows

- ACM from .NET with the official `AWSSDK.CertificateManager` package, and no Floci-specific library.
- **The API works like AWS:** `RequestCertificate` with DNS validation, `DescribeCertificate` returning the validation `CNAME` record, `GetCertificate` returning a PEM that .NET's `X509Certificate2` parses, and `DeleteCertificate`. An unknown validation method is refused with `ValidationException`, as on AWS.
- **Certificates are never `PENDING_VALIDATION`.** Real ACM keeps a DNS-validated certificate pending until it finds the `CNAME` in your domain's DNS. Floci answers `ISSUED`, with the domain's validation status already `SUCCESS`, on the first `DescribeCertificate`. The wait loop exits on its first poll. The lab prints which way it went, so it tells you if Floci starts modelling validation.
- **The certificate is real, but the issuer is not Amazon.** The PEM is a parseable X.509 certificate for your domain, issued by `CN=Floci Local CA`, with a chain.
- **The domain name is not checked.** Real ACM refuses a `DomainName` that is not a valid domain. Floci accepts `not a domain …!` and issues a certificate for it.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Certificate Manager (ACM)
- NuGet: `AWSSDK.CertificateManager` 4.0.102.6, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (ARNs, names and dates change every run):

```text
RequestCertificate   -> arn:aws:acm:us-east-1:000000000000:certificate/7800b444-2246-418a-9a16-36f9cdf40ede
DescribeCertificate  -> ISSUED, issuer CN=Floci Local CA
  Validate lab-716c60a68721.example.com: SUCCESS, add CNAME _b6b86d2eb6986d7a8ebc1122b1fd2347.lab-716c60a68721.example.com. -> _9ae2aef41d52c13747fdbe96426e8cac.acm-validations.aws.
Wait for ISSUED      -> ISSUED after 1 poll(s), 0.0 s
GetCertificate       -> CN=lab-716c60a68721.example.com, issued by CN=Floci Local CA, valid to 2027-10-07
Request "not a domain 716c60a68721!" -> accepted: arn:aws:acm:us-east-1:000000000000:certificate/bd06f8d9-ad55-4705-8a6f-0a61b1130eed

The certificate was ISSUED before anything validated it. Real ACM answers PENDING_VALIDATION
  until the CNAME is in DNS, so code that skips the wait, or never creates the record, passes
  on this Floci and fails on AWS.

Cleanup -> 2 certificate(s) deleted
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The wait for `ISSUED` is production code, not a lab workaround. Keep it when you point this at AWS, and create the `CNAME` that `DescribeCertificate` hands you, or the wait times out.

**Each run uses a fresh name** under the reserved `example.com` domain. The `finally` deletes every certificate the run requested, including the one for the name that is not a domain.

## Try changing...

- Remove the wait loop entirely. The lab still passes on Floci, which is the point: on AWS, the next call that uses the certificate (an HTTPS listener, a CloudFront distribution) would fail while it is still pending.
- Set `ValidationMethod` to `"BOGUS"` and see what Floci says. (It refuses with `ValidationException: Invalid validation method: BOGUS. Must be DNS or EMAIL.`)
- Write the PEM and chain from `GetCertificate` to a file and open it with `openssl x509 -text`. Look at the issuer and the validity period.

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
