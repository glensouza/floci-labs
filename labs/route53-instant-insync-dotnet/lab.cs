#:package AWSSDK.Route53@4.0.100.14

// AWS Route 53 against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// Creates a hosted zone, UPSERTs an A record, and reports the change status at each step. Real
// Route 53 answers PENDING and takes up to about a minute to reach INSYNC; the lab prints what
// this Floci answered, so it tells you if that ever changes. The Floci-specific lines are the
// endpoint, the dummy credentials and MaxErrorRetry.

using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonRoute53Client route53 = new AmazonRoute53Client(credentials, new AmazonRoute53Config { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// .test is reserved (RFC 2606) and 192.0.2.0/24 is TEST-NET-1 (RFC 5737): nothing here can ever
// point at a real host. A fresh name per run means a crashed run never blocks the next one.
string run = Guid.NewGuid().ToString("N");
string zoneName = $"{run}.lab.test.";
ResourceRecordSet record = new ResourceRecordSet
{
    Name = $"www.{zoneName}",
    Type = RRType.A,
    TTL = 60,
    ResourceRecords = [new ResourceRecord { Value = "192.0.2.10" }],
};

string? zoneId = null;
bool recordWritten = false;
List<ChangeStatus> seen = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    CreateHostedZoneResponse zone = await route53.CreateHostedZoneAsync(new CreateHostedZoneRequest { Name = zoneName, CallerReference = run });
    zoneId = zone.HostedZone.Id;
    seen.Add(zone.ChangeInfo.Status);
    Console.WriteLine($"CreateHostedZone         -> {zoneId}, change {zone.ChangeInfo.Status}");
    Console.WriteLine($"  Name servers: {string.Join(", ", zone.DelegationSet.NameServers)}");

    ChangeResourceRecordSetsResponse upsert = await route53.ChangeResourceRecordSetsAsync(Change(ChangeAction.UPSERT));
    recordWritten = true;
    seen.Add(upsert.ChangeInfo.Status);
    Console.WriteLine($"UPSERT {record.Name} A -> change {upsert.ChangeInfo.Id} {upsert.ChangeInfo.Status}");

    // This is the wait production code needs: real Route 53 keeps the change PENDING while it
    // propagates to its name servers, and anything that reads the record straight away races it.
    DateTime started = DateTime.UtcNow;
    GetChangeResponse change = await route53.GetChangeAsync(new GetChangeRequest { Id = upsert.ChangeInfo.Id });
    int polls = 1;
    while (change.ChangeInfo.Status != ChangeStatus.INSYNC && DateTime.UtcNow - started < TimeSpan.FromSeconds(90))
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        change = await route53.GetChangeAsync(new GetChangeRequest { Id = upsert.ChangeInfo.Id });
        polls++;
    }

    Console.WriteLine($"GetChange                -> {change.ChangeInfo.Status} after {polls} poll(s), {(DateTime.UtcNow - started).TotalSeconds:0.0} s");

    ListResourceRecordSetsResponse list = await route53.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest { HostedZoneId = zoneId });
    Console.WriteLine($"ListResourceRecordSets   -> {string.Join(", ", list.ResourceRecordSets.Select(r => $"{r.Type} {r.Name}"))}");
    Console.WriteLine();

    Console.WriteLine(seen.All(s => s == ChangeStatus.INSYNC)
        ? "Every change came back INSYNC in the response itself. Real Route 53 answers PENDING here, so\n  code that skips the GetChange wait passes on this Floci and races on AWS."
        : "A change came back PENDING, as it does on real Route 53. The GetChange wait above is doing real work.");
}
finally
{
    // DELETE has to repeat the record exactly as it was written (name, type, TTL and value), and a
    // zone can't be deleted while it holds anything beyond its own NS and SOA records.
    if (zoneId is not null)
    {
        if (recordWritten)
        {
            await route53.ChangeResourceRecordSetsAsync(Change(ChangeAction.DELETE));
        }

        await route53.DeleteHostedZoneAsync(new DeleteHostedZoneRequest { Id = zoneId });
        Console.WriteLine();
        Console.WriteLine("Cleanup -> record and hosted zone deleted");
    }
}

ChangeResourceRecordSetsRequest Change(ChangeAction action) => new ChangeResourceRecordSetsRequest
{
    HostedZoneId = zoneId,
    ChangeBatch = new ChangeBatch { Changes = [new Change { Action = action, ResourceRecordSet = record }] },
};
