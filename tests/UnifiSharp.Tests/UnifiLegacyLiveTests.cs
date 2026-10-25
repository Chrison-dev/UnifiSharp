using System.Text.Json.Nodes;
using UnifiSharp.Legacy;
using Xunit;

namespace UnifiSharp.Tests;

// Live tests against a UniFi controller, in two groups with different targets:
//
// • WRITE tests (UnifiLegacyLiveTests, UnifiLegacyNetworkRawLiveTests) CREATE AND DELETE
//   real objects — port-forwards, firewall groups, VLANs, and a whole-network PUT. They
//   run only against the throwaway container UnifiContainerFixture starts, and read no
//   UNIFI_* variable at all: once UnifiLegacyOptions learned API-key auth, a plain
//   `secrets.env` started satisfying TryFromEnvironment, so an env-gated write test could
//   silently point at the live home gateway and create a VLAN on it. No env, no risk.
// • READ-ONLY tests (UnifiLegacyReadOnlyLiveTests) use LegacyReadOnlyFixture, which does
//   take its controller from the environment — safe to point at a real gateway.
#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

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

    [SkippableFact]
    public async Task Static_dns_reads_from_the_v2_site_api()
    {
        Skip.If(_fixture.Client is null, "No UniFi env — skipping live read test.");

        // Exercises the whole v2 path: a different base URL from the legacy surface, a bare
        // array instead of {meta,data}, reached through SendAbsoluteAsync.
        var records = await _fixture.Client!.ListStaticDnsAsync();

        Assert.All(records, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.Key));
            Assert.False(string.IsNullOrEmpty(r.RecordType));
        });
    }

    [SkippableFact]
    public async Task Network_reads_raw_and_its_dhcp_dns_is_readable()
    {
        Skip.If(_fixture.Client is null, "No UniFi env — skipping live read test.");

        // Read only: proves the raw path and the dhcpd_dns_N naming against a real controller.
        var networks = await _fixture.Client!.ListNetworksRawAsync();
        Assert.NotEmpty(networks);
        var first = networks[0];
        var net = await _fixture.Client.GetNetworkRawAsync((string)first["_id"]!);

        Assert.Equal((string?)first["_id"], (string?)net["_id"]);
        _ = NetworkDhcpDns.Read(net);   // null (auto) or a list — either way, no throw
    }
}

[Collection(UnifiContainerCollection.Name)]
public class UnifiLegacyLiveTests
{
    private readonly UnifiContainerFixture _fixture;
    public UnifiLegacyLiveTests(UnifiContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Nat_list_reaches_the_v2_endpoint_and_names_come_from_the_networks()
    {
        // The container has no gateway behind it, so it accepts no NAT rule (POST is a bare 500):
        // the write path is covered by UnifiNatRuleTests against the shape the UCG returns.
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);
        var client = _fixture.Client;

        Assert.NotNull(await client.ListNatRulesAsync());
        Assert.NotEmpty((await client.GetNatNamesAsync()).NetworkIds);
    }

    [SkippableFact]
    public async Task PortForward_create_list_delete_roundtrip()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);
        var client = _fixture.Client;

        var created = await client.CreatePortForwardAsync(new UnifiPortForward
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
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);
        var client = _fixture.Client;

        var created = await client.CreateFirewallGroupAsync(new UnifiFirewallGroup
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
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);
        var client = _fixture.Client;

        var created = await client.CreateNetworkAsync(new UnifiNetwork
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

/// <summary>
/// Proves a field-scoped raw network write is reversible: setting DHCP DNS touches only the
/// DNS keys, and putting back the saved object restores the network exactly.
/// <para>Runs against <see cref="UnifiContainerFixture"/>'s container, in the same collection as
/// the other write tests so none of them runs alongside it: it asserts every OTHER network is
/// untouched afterwards.</para>
/// </summary>
[Collection(UnifiContainerCollection.Name)]
public class UnifiLegacyNetworkRawLiveTests
{
    private readonly UnifiContainerFixture _fixture;
    public UnifiLegacyNetworkRawLiveTests(UnifiContainerFixture fixture) => _fixture = fixture;

    private static readonly string[] Servers = ["10.20.0.53", "10.20.0.54"];

    private static readonly HashSet<string> DnsKeys =
        ["dhcpd_dns_enabled", "dhcpd_dns_1", "dhcpd_dns_2", "dhcpd_dns_3", "dhcpd_dns_4"];

    // Keys the controller rewrites on its own between reads, excluded from every comparison
    // below. Empty until a run shows one — add each by name with the reason it moves, so a
    // real difference can never hide here unannounced.
    private static readonly HashSet<string> VolatileKeys = [];

    [SkippableFact]
    public async Task Network_dhcp_dns_write_is_reversible_byte_for_byte()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);
        var client = _fixture.Client;

        var before = await client.ListNetworksRawAsync();
        var (vlan, octet) = PickUnusedVlanAndSubnet(before);

        var created = await client.CreateNetworkAsync(new UnifiNetwork
        {
            Name = $"scratch-dhcpdns-{Guid.NewGuid().ToString("N")[..8]}",
            Purpose = "corporate",
            VlanEnabled = true,
            Vlan = vlan.ToString(),
            IpSubnet = $"10.232.{octet}.1/24",
            DhcpdEnabled = true,
            DhcpdStart = $"10.232.{octet}.6",
            DhcpdStop = $"10.232.{octet}.254",
        });
        Assert.False(string.IsNullOrEmpty(created.Id));
        var id = created.Id!;

        try
        {
            // Found by this test: the controller treats a PUT as a MERGE for omitted keys. A body
            // without dhcpd_dns_enabled does not switch it off again, so "restore" cannot be done by
            // replaying a body that lacks the key. Real LAN networks carry the key explicitly
            // (dhcpd_dns_enabled=false), so the baseline is made explicit here, as on the live console.
            var fresh = await client.GetNetworkRawAsync(id);
            Assert.True(NetworkDhcpDns.Read(fresh) is null,
                $"precondition: a new network should have DHCP DNS off (auto); dhcpd_dns_enabled is {fresh["dhcpd_dns_enabled"]?.ToJsonString() ?? "absent"}");
            var explicitOff = Parse(fresh.ToJsonString());
            explicitOff["dhcpd_dns_enabled"] = false;
            for (var slot = 1; slot <= NetworkDhcpDns.MaxServers; slot++)
                explicitOff[$"dhcpd_dns_{slot}"] = ""; // as on Consumer: all four keys present, blank when unused
            await client.ReplaceNetworkRawAsync(id, explicitOff);

            // Kept as text so nothing below can mutate the baseline.
            var saved = (await client.GetNetworkRawAsync(id)).ToJsonString();
            var savedObj = Parse(saved);
            Assert.True(NetworkDhcpDns.Read(savedObj) is null && (bool?)savedObj["dhcpd_dns_enabled"] == false,
                "precondition: the baseline carries dhcpd_dns_enabled=false explicitly");

            await client.ReplaceNetworkRawAsync(id, NetworkDhcpDns.With(Parse(saved), Servers));
            var changed = await client.GetNetworkRawAsync(id);

            Assert.Equal(Servers, NetworkDhcpDns.Read(changed));
            AssertSameKeys(savedObj, changed, ignore: DnsKeys, what: "after the DNS write, a non-DNS key");
            Assert.Equal((string?)savedObj["_id"], (string?)changed["_id"]);
            Assert.Equal((string?)savedObj["site_id"], (string?)changed["site_id"]);

            await client.ReplaceNetworkRawAsync(id, Parse(saved));
            var restored = await client.GetNetworkRawAsync(id);

            AssertSameKeys(savedObj, restored, ignore: [], what: "after restoring the saved object, key");
        }
        finally
        {
            await client.DeleteNetworkAsync(id);
        }

        // Every other network, whole: the scratch network is gone and nothing else moved.
        var after = await client.ListNetworksRawAsync();
        var beforeById = ById(before);
        var afterById = ById(after);
        Assert.Equal(beforeById.Keys.Order(), afterById.Keys.Order());
        foreach (var (otherId, was) in beforeById)
        {
            AssertSameKeys(was, afterById[otherId], ignore: [], what: $"network {otherId} ('{was["name"]}') changed: key");
        }
    }

    private static (int Vlan, int Octet) PickUnusedVlanAndSubnet(IReadOnlyList<JsonObject> networks)
    {
        // node.ToString() gives the bare text for both shapes the vlan arrives in (number or string).
        var vlans = networks.Select(n => n["vlan"]?.ToString()).ToHashSet();
        var subnets = networks.Select(n => n["ip_subnet"]?.ToString() ?? "").ToList();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var vlan = Random.Shared.Next(3900, 4000);         // inside UniFi's valid 1–4009
            var octet = Random.Shared.Next(0, 256);
            if (!vlans.Contains(vlan.ToString()) && !subnets.Any(s => s.StartsWith($"10.232.{octet}.", StringComparison.Ordinal)))
            {
                return (vlan, octet);
            }
        }

        throw new InvalidOperationException("No free VLAN 3900–3999 / 10.232.x.0/24 found for the scratch network.");
    }

    // Compared as each value's JSON text, not semantically: the claim is byte-for-byte, so 1
    // vs 1.0 or a re-ordered nested object is a difference. The union of keys is walked, so a
    // key the controller added or dropped is reported too.
    private static void AssertSameKeys(JsonObject expected, JsonObject actual, IReadOnlyCollection<string> ignore, string what)
    {
        foreach (var key in expected.Select(p => p.Key).Union(actual.Select(p => p.Key)))
        {
            if (ignore.Contains(key) || VolatileKeys.Contains(key))
            {
                continue;
            }

            var want = expected.TryGetPropertyValue(key, out var e) ? e?.ToJsonString() ?? "null" : "<absent>";
            var got = actual.TryGetPropertyValue(key, out var a) ? a?.ToJsonString() ?? "null" : "<absent>";
            Assert.True(want == got, $"{what} '{key}' differs: expected {want}, got {got}");
        }
    }

    private static Dictionary<string, JsonObject> ById(IReadOnlyList<JsonObject> networks) =>
        networks.ToDictionary(n => (string?)n["_id"] ?? throw new InvalidOperationException($"network without a string _id: {n.ToJsonString()}"));

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
