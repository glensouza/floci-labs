# Route 53 Resolver in .NET: a forwarding rule on localhost, and the delete that should have failed

> Create a Route 53 Resolver rule and associate it with a VPC from a single C# file. Then try to delete the rule while the VPC still uses it. Real AWS refuses. Floci deletes it, so code that gets the teardown order wrong passes locally and fails on AWS.

## What it shows

- Route 53 Resolver from .NET with the official `AWSSDK.Route53Resolver` package, and no Floci-specific library.
- **Rules and associations work:** `CreateResolverRule` (a `SYSTEM` rule, which needs no resolver endpoint), `AssociateResolverRule`, `ListResolverRuleAssociations`, `DisassociateResolverRule` and `DeleteResolverRule`.
- **A rule in use can be deleted.** Real AWS answers `ResourceInUseException` while a VPC is still associated with the rule. Floci answers `DELETING`, and the association is still listed afterwards, pointing at a rule that's gone. The lab prints which way it went, so it tells you if Floci starts enforcing this.
- **Disassociation is instant.** Floci's `DisassociateResolverRule` response says `DELETING`, but the association has already disappeared by the first poll. On AWS it stays `DELETING` for a while, and the rule can't be deleted until it goes. So the lab polls before it deletes the rule, the way production code has to.

## Stack

- Language / runtime: C# on .NET 10, as a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) (`dotnet run lab.cs`, no `.csproj`)
- AWS services used: Route 53 Resolver
- NuGet: `AWSSDK.Route53Resolver` 4.0.100.15, pinned in the `#:package` line at the top of `lab.cs`
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

Expected output (ids and names change every run):

```text
CreateResolverRule       -> rslvr-rr-7c410f8797984 SYSTEM corp-f97359ac9a65.lab.test, COMPLETE
AssociateResolverRule    -> rslvr-rrassoc-d728218d7ac24 vpc-0123456789abcdef0, COMPLETE
DeleteResolverRule       -> DELETING  (while the VPC is still associated)
  Associations still listed for the deleted rule: 1

The rule was deleted while a VPC still used it. Real AWS refuses this with
  ResourceInUseException, so code that deletes before disassociating passes on this Floci and
  fails on AWS.

DisassociateResolverRule -> DELETING
  Association gone after 1 poll(s), 0.0 s
Cleanup -> done
```

Set `FLOCI_ENDPOINT` if your Floci is somewhere else.

## How it works

**The endpoint is `127.0.0.1`, not `localhost`.** `localhost` also resolves to IPv6 `::1`, and a published Docker port only binds IPv4. On Windows, every new connection waits out the dead IPv6 attempt first, which costs about 2 seconds per connection pool.

**Only three lines know about Floci:** `ServiceURL`, the `test`/`test` credentials and `MaxErrorRetry = 0`. The disassociate-then-poll-then-delete order in the `finally` is production code, not a lab workaround. Keep it when you point this at AWS.

**The VPC id is a placeholder.** Floci doesn't check that it exists. Against a real account, put one of your own VPC ids in `VpcId`, or the association step fails.

**It's a `SYSTEM` rule, not a `FORWARD` rule.** A `FORWARD` rule needs an outbound resolver endpoint, and on Floci 2.1.0 no SDK can create one: `CreateResolverEndpoint` answers `InvalidParameterException: IpAddressRequests is required`, but the real API and the SDK send the addresses as `IpAddresses`.

## Try changing...

- Move the `DeleteResolverRule` call after the `finally`'s disassociation and see that the lab still passes on Floci. Then think about what the poll is for.
- Associate the same rule with the same VPC twice. (Floci accepts both; AWS answers `ResourceExistsException`.)
- Create a DNS Firewall domain list with `CreateFirewallDomainList`, then try to add a domain with `UpdateFirewallDomains`. (Floci 2.1.0 answers `UnknownOperationException`.)

## Author

Glen Souza · built for the **FlociLab** series (Floci is pronounced "floss-see")

- Video: _link to come_
- Blog post: _link to come_
- Full sample, with a Blazor demo page and an integration test, in a .NET Aspire gallery covering Floci's AWS, Azure, GCP and OCI emulators: [github.com/glensouza/flocilab](https://github.com/glensouza/flocilab)
