using UnifiSharp.Legacy;
using Xunit;

namespace UnifiSharp.Tests;

// Live write round-trips against a UniFi controller — the .containers/unifi test
// container (run ./bootstrap.sh, then `set -a && . ./credentials.env`). Every test
// cleans up what it creates (add-only on the controller).
//
// ⚠ These tests CREATE AND DELETE real objects — port-forwards, firewall groups and
// a VLAN. They are gated on SESSION auth (UNIFI_USERNAME + UNIFI_PASSWORD) on purpose,
// which is the only mode the test container offers. That gate is load-bearing: once
// UnifiLegacyOptions learned API-key auth, a plain `secrets.env` (UNIFI_API_KEY +
// UNIFI_LOCAL_HOST) started satisfying TryFromEnvironment — so keying these off
// TryFromEnvironment alone would silently point them at the live home gateway and
// create a VLAN on it. Read-only live checks use LegacyReadOnlyFixture instead.
//
// One shared session for the whole class (IClassFixture): the controller
// rate-limits logins (HTTP 429), so we log in once and reuse the cookie.
#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

public sealed class LegacyContainerFixture : IDisposable
{
    public UnifiLegacyClient? Client { get; } =
        UnifiLegacyOptions.TryFromEnvironment() is { UsesApiKey: false } o ? new UnifiLegacyClient(o) : null;

    public void Dispose() => Client?.Dispose();
}

/// <summary>
/// Any configured controller, in either auth mode — for checks that only READ.
/// Safe to point at a real gateway.
/// </summary>
public sealed class LegacyReadOnlyFixture : IDisposable
{
    public UnifiLegacyClient? Client { get; } =
        UnifiLegacyOptions.TryFromEnvironment() is { } o ? new UnifiLegacyClient(o) : null;

    public bool UsesApiKey { get; } = UnifiLegacyOptions.TryFromEnvironment()?.UsesApiKey ?? false;

    public void Dispose() => Client?.Dispose();
}

/// <summary>Read-only live checks — no object is created, modified or deleted.</summary>
public class UnifiLegacyReadOnlyLiveTests : IClassFixture<LegacyReadOnlyFixture>
{
    private readonly LegacyReadOnlyFixture _fixture;
    public UnifiLegacyReadOnlyLiveTests(LegacyReadOnlyFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ApiKey_auth_can_read_networks_without_a_session_login()
    {
        Skip.If(_fixture.Client is null, "No UniFi env — skipping live read test.");
        Skip.IfNot(_fixture.UsesApiKey, "Not in API-key mode — this test covers the X-API-KEY path.");

        var networks = await _fixture.Client!.ListNetworksAsync();

        // Proves the whole X-API-KEY path: no /api/auth/login, no cookie, no CSRF token.
        Assert.NotEmpty(networks);
        Assert.Contains(networks, n => !string.IsNullOrEmpty(n.Id));
    }

    [SkippableFact]
    public async Task ApiKey_auth_can_read_known_clients_and_their_reservations()
    {
        Skip.If(_fixture.Client is null, "No UniFi env — skipping live read test.");
        Skip.IfNot(_fixture.UsesApiKey, "Not in API-key mode — this test covers the X-API-KEY path.");

        var users = await _fixture.Client!.ListUsersAsync();

        Assert.NotEmpty(users);
        Assert.All(users.Where(u => u.UseFixedIp == true),
            u => Assert.False(string.IsNullOrEmpty(u.FixedIp)));  // a reservation always carries an address
    }
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
