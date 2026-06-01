using Xunit;

namespace UnifiSharp.Tests;

/// <summary>
/// Read-only integration test against a UniFi controller — the live console or
/// the <c>.containers/unifi</c> test container. Runs only when UNIFI_* env vars
/// are set; otherwise skips.
/// </summary>
public class UnifiLiveTests
{
    [SkippableFact]
    public async Task Discover_returns_a_snapshot()
    {
        var options = UnifiClientOptions.TryFromEnvironment();
        Skip.If(options is null, "No UNIFI_BASE_URL / UNIFI_API_KEY — skipping live UniFi discovery.");

        var snapshot = await new UnifiDiscovery(UnifiApi.Create(options!)).DiscoverAsync();

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Sites);
    }

    [SkippableFact]
    public async Task Discover_populates_enriched_sections()
    {
        var options = UnifiClientOptions.TryFromEnvironment();
        Skip.If(options is null, "No UNIFI_BASE_URL / UNIFI_API_KEY — skipping live UniFi discovery.");

        var snapshot = await new UnifiDiscovery(UnifiApi.Create(options!)).DiscoverAsync();

        Skip.If(snapshot.Sites.Count == 0, "No sites returned by the controller.");

        // Every enriched section must be non-null (records default to empty lists);
        // at least one site should expose networks and devices on a real controller.
        foreach (var site in snapshot.Sites)
        {
            Assert.NotNull(site.Networks);
            Assert.NotNull(site.Wlans);
            Assert.NotNull(site.Devices);
            Assert.NotNull(site.Clients);
            Assert.NotNull(site.Firewall);
            Assert.NotNull(site.Firewall.Zones);
            Assert.NotNull(site.Firewall.Policies);
            Assert.NotNull(site.Firewall.AclRules);
        }

        Assert.Contains(snapshot.Sites, s => s.Networks.Count > 0);
    }
}
