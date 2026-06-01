using System.Text.Json;
using UnifiSharp;
using Xunit;

namespace UnifiSharp.Tests;

/// <summary>
/// Pure unit tests for the enriched <see cref="UnifiSnapshot"/> record graph —
/// shape and JSON serialization, no controller required.
/// </summary>
public class UnifiSnapshotTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static UnifiSnapshot Sample() => new()
    {
        Sites =
        [
            new UnifiSiteSnapshot
            {
                Site = "Default",
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                DeviceCount = 2,
                ClientCount = 3,
                Networks =
                [
                    new UnifiNetworkInfo { Name = "Homelab", VlanId = 1010, Enabled = true, Purpose = "routed" },
                ],
                Wlans =
                [
                    new UnifiWlanInfo { Ssid = "HomeWiFi", Enabled = true, Security = "wpa3", NetworkType = "DEFAULT" },
                ],
                Firewall = new UnifiFirewallSnapshot
                {
                    Zones = [new UnifiFirewallZoneInfo { Name = "Internal", NetworkIds = [Guid.NewGuid()] }],
                    Policies = [new UnifiFirewallPolicyInfo { Name = "Allow LAN", Index = 1, Enabled = true, Action = "ALLOW" }],
                    AclRules = [new UnifiAclRuleInfo { Name = "Block IoT", Enabled = true, Type = "L3", Action = "BLOCK" }],
                },
                Devices =
                [
                    new UnifiDeviceInfo { Name = "UDM", Model = "UDMPRO", IpAddress = "10.10.0.1", MacAddress = "aa:bb", FirmwareVersion = "4.0", State = "ONLINE" },
                ],
                Clients =
                [
                    new UnifiClientInfo { Name = "laptop", Type = "WIRELESS", IpAddress = "10.10.0.50" },
                ],
            },
        ],
    };

    [Fact]
    public void Snapshot_carries_enriched_sections()
    {
        var snapshot = Sample();
        var site = Assert.Single(snapshot.Sites);

        Assert.Equal(2, site.DeviceCount);
        Assert.Equal(3, site.ClientCount);
        Assert.Equal(1010, Assert.Single(site.Networks).VlanId);
        Assert.Equal("HomeWiFi", Assert.Single(site.Wlans).Ssid);
        Assert.Equal("wpa3", site.Wlans[0].Security);
        Assert.Single(site.Firewall.Zones);
        Assert.Equal("ALLOW", Assert.Single(site.Firewall.Policies).Action);
        Assert.Equal("BLOCK", Assert.Single(site.Firewall.AclRules).Action);
        Assert.Equal("UDM", Assert.Single(site.Devices).Name);
        Assert.Equal("laptop", Assert.Single(site.Clients).Name);
    }

    [Fact]
    public void Snapshot_round_trips_through_json()
    {
        var snapshot = Sample();

        var serialized = JsonSerializer.Serialize(snapshot, Json);
        var restored = JsonSerializer.Deserialize<UnifiSnapshot>(serialized, Json);

        Assert.NotNull(restored);
        var site = Assert.Single(restored!.Sites);
        Assert.Equal("Default", site.Site);
        Assert.Equal("Homelab", Assert.Single(site.Networks).Name);
        Assert.Equal("routed", site.Networks[0].Purpose);
        Assert.Equal("Internal", Assert.Single(site.Firewall.Zones).Name);
        Assert.Equal("UDMPRO", Assert.Single(site.Devices).Model);
    }

    [Fact]
    public void Empty_sections_default_to_empty_not_null()
    {
        var site = new UnifiSiteSnapshot { Site = "Empty" };

        Assert.Empty(site.Networks);
        Assert.Empty(site.Wlans);
        Assert.Empty(site.Devices);
        Assert.Empty(site.Clients);
        Assert.NotNull(site.Firewall);
        Assert.Empty(site.Firewall.Zones);
        Assert.Empty(site.Firewall.Policies);
        Assert.Empty(site.Firewall.AclRules);
    }
}
