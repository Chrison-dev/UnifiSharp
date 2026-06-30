using UnifiSharp.Legacy;
using Xunit;

namespace UnifiSharp.Tests;

// Live write round-trips against a UniFi controller — the .containers/unifi test
// container (run ./bootstrap.sh, then `set -a && . ./credentials.env`). Runs only
// when UNIFI_LEGACY_BASE_URL / UNIFI_USERNAME / UNIFI_PASSWORD are set. Every test
// cleans up what it creates (add-only on the controller).
//
// One shared session for the whole class (IClassFixture): the controller
// rate-limits logins (HTTP 429), so we log in once and reuse the cookie.
#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

public sealed class LegacyContainerFixture : IDisposable
{
    public UnifiLegacyClient? Client { get; } =
        UnifiLegacyOptions.TryFromEnvironment() is { } o ? new UnifiLegacyClient(o) : null;

    public void Dispose() => Client?.Dispose();
}

public class UnifiLegacyLiveTests : IClassFixture<LegacyContainerFixture>
{
    private readonly UnifiLegacyClient? _client;
    public UnifiLegacyLiveTests(LegacyContainerFixture fixture) => _client = fixture.Client;

    [SkippableFact]
    public async Task PortForward_create_list_delete_roundtrip()
    {
        var client = _client;
        Skip.If(client is null, "No UNIFI_LEGACY_* env — skipping live legacy write test.");

        var created = await client!.CreatePortForwardAsync(new UnifiPortForward
        {
            Name = "unifisharp-test-pf",
            Enabled = true,
            PfwdInterface = "wan",
            Src = "any",
            DstPort = "65000",
            Fwd = "10.10.0.13",
            FwdPort = "65000",
            Proto = "tcp",
            Log = false,
        });

        try
        {
            Assert.False(string.IsNullOrEmpty(created.Id));
            Assert.Equal("10.10.0.13", created.Fwd);

            var list = await client.ListPortForwardsAsync();
            Assert.Contains(list, p => p.Id == created.Id && p.Name == "unifisharp-test-pf");
        }
        finally
        {
            await client.DeletePortForwardAsync(created.Id!);
        }

        var after = await client.ListPortForwardsAsync();
        Assert.DoesNotContain(after, p => p.Id == created.Id);
    }

    [SkippableFact]
    public async Task FirewallGroup_create_delete_roundtrip()
    {
        var client = _client;
        Skip.If(client is null, "No UNIFI_LEGACY_* env — skipping live legacy write test.");

        var created = await client!.CreateFirewallGroupAsync(new UnifiFirewallGroup
        {
            Name = "unifisharp-test-grp",
            GroupType = "port-group",
            GroupMembers = ["65000", "65001"],
        });

        Assert.False(string.IsNullOrEmpty(created.Id));
        await client.DeleteFirewallGroupAsync(created.Id!);
    }

    [SkippableFact]
    public async Task Network_create_delete_roundtrip()
    {
        var client = _client;
        Skip.If(client is null, "No UNIFI_LEGACY_* env — skipping live legacy write test.");

        var created = await client!.CreateNetworkAsync(new UnifiNetwork
        {
            Name = "unifisharp-test-vlan",
            Purpose = "corporate",
            VlanEnabled = true,
            Vlan = "3990",                 // within UniFi's valid range (1–4009; 4010+ reserved)
            IpSubnet = "10.231.0.1/24",
            DhcpdEnabled = false,
        });

        Assert.False(string.IsNullOrEmpty(created.Id));
        await client.DeleteNetworkAsync(created.Id!);
    }
}
