#:package AWSSDK.CertificateManager@4.0.102.6

// AWS Certificate Manager against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Requests a DNS-validated certificate and waits for it the way production code has to. Real ACM
// leaves it PENDING_VALIDATION until the validation CNAME it hands you is in DNS; the lab prints
// what this Floci answered, so it tells you if that ever changes. The Floci-specific lines are the
// endpoint, the dummy credentials and MaxErrorRetry.

using System.Security.Cryptography.X509Certificates;
using Amazon.CertificateManager;
using Amazon.CertificateManager.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonCertificateManagerClient acm = new AmazonCertificateManagerClient(credentials, new AmazonCertificateManagerConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// example.com is reserved (RFC 2606), and a fresh name per run means a crashed run never collides
// with the next one.
string run = Guid.NewGuid().ToString("N")[..12];
string domain = $"lab-{run}.example.com";
string notADomain = $"not a domain {run}!";
List<string> arns = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    RequestCertificateResponse request = await acm.RequestCertificateAsync(new RequestCertificateRequest { DomainName = domain, ValidationMethod = ValidationMethod.DNS });
    arns.Add(request.CertificateArn);
    Console.WriteLine($"RequestCertificate   -> {request.CertificateArn}");

    CertificateDetail detail = (await acm.DescribeCertificateAsync(new DescribeCertificateRequest { CertificateArn = request.CertificateArn })).Certificate;
    CertificateStatus first = detail.Status;
    Console.WriteLine($"DescribeCertificate  -> {detail.Status}, issuer {detail.Issuer}");

    foreach (DomainValidation option in detail.DomainValidationOptions ?? [])
    {
        Console.WriteLine($"  Validate {option.DomainName}: {option.ValidationStatus}, add {option.ResourceRecord?.Type} {option.ResourceRecord?.Name} -> {option.ResourceRecord?.Value}");
    }

    // This is the wait production code needs: real ACM issues the certificate only once it sees
    // the CNAME above in DNS, and anything that attaches the certificate before then fails.
    DateTime started = DateTime.UtcNow;
    int polls = 1;
    while (detail.Status == CertificateStatus.PENDING_VALIDATION && DateTime.UtcNow - started < TimeSpan.FromSeconds(30))
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        detail = (await acm.DescribeCertificateAsync(new DescribeCertificateRequest { CertificateArn = request.CertificateArn })).Certificate;
        polls++;
    }

    Console.WriteLine($"Wait for ISSUED      -> {detail.Status} after {polls} poll(s), {(DateTime.UtcNow - started).TotalSeconds:0.0} s");

    try
    {
        GetCertificateResponse pem = await acm.GetCertificateAsync(new GetCertificateRequest { CertificateArn = request.CertificateArn });
        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(pem.Certificate);
        Console.WriteLine($"GetCertificate       -> {certificate.Subject}, issued by {certificate.Issuer}, valid to {certificate.NotAfter:yyyy-MM-dd}");
    }
    catch (RequestInProgressException ex)
    {
        Console.WriteLine($"GetCertificate       -> {ex.GetType().Name}: {ex.Message}");
    }

    // Real ACM refuses a DomainName that is not a fully qualified domain name.
    try
    {
        RequestCertificateResponse bad = await acm.RequestCertificateAsync(new RequestCertificateRequest { DomainName = notADomain });
        arns.Add(bad.CertificateArn);
        Console.WriteLine($"Request \"{notADomain}\" -> accepted: {bad.CertificateArn}");
    }
    catch (AmazonCertificateManagerException ex)
    {
        Console.WriteLine($"Request \"{notADomain}\" -> {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine();
    Console.WriteLine(first == CertificateStatus.ISSUED
        ? "The certificate was ISSUED before anything validated it. Real ACM answers PENDING_VALIDATION\n  until the CNAME is in DNS, so code that skips the wait, or never creates the record, passes\n  on this Floci and fails on AWS."
        : $"The certificate started {first}, as it does on real ACM. The wait above is doing real work.");
}
finally
{
    foreach (string arn in arns)
    {
        await acm.DeleteCertificateAsync(new DeleteCertificateRequest { CertificateArn = arn });
    }

    if (arns.Count != 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Cleanup -> {arns.Count} certificate(s) deleted");
    }
}
