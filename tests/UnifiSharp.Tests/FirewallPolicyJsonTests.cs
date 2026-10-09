using System.Text.Json.Nodes;
using UnifiSharp.Firewall;

namespace UnifiSharp.Tests;

// Pure mapping tests. The fixtures are two REAL policies read from a UniFi Network 10.6
// controller's integration API (ids kept, description shortened).
public class FirewallPolicyJsonTests
{
    private static readonly Guid External = Guid.Parse("904a42ba-6faf-4ab0-b57c-58e9df80e71b");
    private static readonly Guid Homelab = Guid.Parse("0d4143bd-77e6-4da7-a008-1a917b632600");
    private static readonly Guid Internal = Guid.Parse("c4ab6748-92b1-4038-92bc-f2137d896cde");
    private static readonly Guid Azure = Guid.Parse("45f7f6d6-e3da-46a6-b4a9-f812eaf6946c");
    private static readonly Guid Consumer = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly FirewallNames Names = new(
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        { ["External"] = External, ["Homelab"] = Homelab, ["Internal"] = Internal, ["Azure"] = Azure },
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["Consumer"] = Consumer });

    private const string QbitV6 = """
        {"id":"0f7203ae-dd6d-4ddd-81d7-c107b78c5c62","enabled":true,"name":"qbittorrent-peers-v6","description":"qBittorrent peer port over IPv6",
         "index":10000,"action":{"type":"ALLOW","allowReturnTraffic":true},"source":{"zoneId":"904a42ba-6faf-4ab0-b57c-58e9df80e71b"},
         "destination":{"zoneId":"0d4143bd-77e6-4da7-a008-1a917b632600","trafficFilter":{"type":"IP_ADDRESS",
           "ipAddressFilter":{"type":"IP_ADDRESSES","matchOpposite":false,"items":[{"type":"IP_ADDRESS","value":"2407:8b00:116d:e502::6342:9"}]},
           "portFilter":{"type":"PORTS","matchOpposite":false,"items":[{"type":"PORT_NUMBER","value":63429}]}}},
         "ipProtocolScope":{"ipVersion":"IPV6","protocolFilter":{"type":"PRESET","preset":{"name":"TCP_UDP"}}},
         "loggingEnabled":false,"metadata":{"origin":"USER_DEFINED"}}
        """;

    private const string InternalToAzure = """
        {"id":"89b81413-0315-48a8-b00a-f8736ee7fd33","enabled":true,"name":"Internal → Azure","index":10000,
         "action":{"type":"ALLOW","allowReturnTraffic":true},"source":{"zoneId":"c4ab6748-92b1-4038-92bc-f2137d896cde"},
         "destination":{"zoneId":"45f7f6d6-e3da-46a6-b4a9-f812eaf6946c"},"ipProtocolScope":{"ipVersion":"IPV4_AND_IPV6"},
         "loggingEnabled":false,"metadata":{"origin":"USER_DEFINED"}}
        """;

    [Fact]
    public void Reads_a_real_ip_and_port_policy()
    {
        var (spec, why) = FirewallPolicyJson.FromJson(JsonNode.Parse(QbitV6)!.AsObject(), Names);
        Assert.Null(why);
        Assert.NotNull(spec);
        Assert.Equal("External", spec!.Source.Zone);
        Assert.Equal("Homelab", spec.Destination.Zone);
        Assert.Equal(["2407:8b00:116d:e502::6342:9"], spec.Destination.Addresses);
        Assert.Equal(["63429"], spec.Destination.Ports);
        Assert.Equal(FirewallIpVersion.IPv6, spec.IpVersion);
        Assert.Equal("tcp_udp", spec.Protocol);
        Assert.True(spec.AllowReturnTraffic);
    }

    [Theory]
    [InlineData(QbitV6)]
    [InlineData(InternalToAzure)]
    public void Live_policy_round_trips_through_the_spec(string json)
    {
        var (spec, _) = FirewallPolicyJson.FromJson(JsonNode.Parse(json)!.AsObject(), Names);
        var (again, why) = FirewallPolicyJson.FromJson(FirewallPolicyJson.ToJson(spec!, Names), Names);
        Assert.Null(why);
        Assert.Equal(spec, again);
    }

    [Fact]
    public void Equality_ignores_list_order_and_case()
    {
        var a = new FirewallEndpoint { Zone = "Homelab", Addresses = ["10.0.0.1", "10.0.0.2"], Ports = ["443", "80"] };
        var b = new FirewallEndpoint { Zone = "Homelab", Addresses = ["10.0.0.2", "10.0.0.1"], Ports = ["80", "443"] };
        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Ports = ["80"] });
    }

    [Fact]
    public void Writes_an_ipv6_interface_id_with_the_iid_mask_so_it_survives_prefix_changes()
    {
        var spec = new FirewallPolicySpec
        {
            Name = "t", Source = new() { Zone = "External" },
            Destination = new() { Zone = "Homelab", Ipv6InterfaceId = "::6342:9", Ports = ["63429"] },
            IpVersion = FirewallIpVersion.IPv6, Protocol = "tcp_udp",
        };
        var o = FirewallPolicyJson.ToJson(spec, Names);
        var f = o["destination"]!["trafficFilter"]!;
        Assert.Equal("IPV6_IID", f["type"]!.GetValue<string>());
        Assert.Equal("::6342:9/::ffff:ffff:ffff:ffff", f["ipv6IidFilter"]!["ipv6Iid"]!.GetValue<string>());
        Assert.Equal(63429, f["portFilter"]!["items"]![0]!["value"]!.GetValue<int>());
        Assert.Equal(spec, FirewallPolicyJson.FromJson(o, Names).Spec);
    }

    [Fact]
    public void Writes_subnets_ranges_networks_port_ranges_and_named_protocols()
    {
        var spec = new FirewallPolicySpec
        {
            Name = "t", Action = FirewallAction.Block,
            Source = new() { Zone = "Internal", Networks = ["Consumer"] },
            Destination = new() { Zone = "External", Addresses = ["8.8.8.0/24", "1.1.1.1-1.1.1.3"], Ports = ["853", "5000-5010"] },
            Protocol = "udp",
        };
        var o = FirewallPolicyJson.ToJson(spec, Names);
        Assert.Null(o["action"]!["allowReturnTraffic"]);                      // Block has no return-traffic flag
        Assert.Equal(Consumer.ToString(), o["source"]!["trafficFilter"]!["networkFilter"]!["networkIds"]![0]!.GetValue<string>());
        var items = o["destination"]!["trafficFilter"]!["ipAddressFilter"]!["items"]!.AsArray();
        Assert.Equal("SUBNET", items[0]!["type"]!.GetValue<string>());
        Assert.Equal("IP_ADDRESS_RANGE", items[1]!["type"]!.GetValue<string>());
        Assert.Equal("NAMED_PROTOCOL", o["ipProtocolScope"]!["protocolFilter"]!["type"]!.GetValue<string>());
        Assert.Equal(spec, FirewallPolicyJson.FromJson(o, Names).Spec);
    }

    [Fact]
    public void Ports_only_endpoint_uses_the_port_filter_type()
    {
        var spec = new FirewallPolicySpec { Name = "t", Source = new() { Zone = "Internal" }, Destination = new() { Zone = "External", Ports = ["853"] } };
        Assert.Equal("PORT", FirewallPolicyJson.ToJson(spec, Names)["destination"]!["trafficFilter"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Refuses_more_than_one_narrowing_per_endpoint()
    {
        var spec = new FirewallPolicySpec
        {
            Name = "t", Source = new() { Zone = "Internal" },
            Destination = new() { Zone = "Homelab", Addresses = ["10.0.0.1"], Ipv6InterfaceId = "::1" },
        };
        Assert.Throws<ArgumentException>(() => FirewallPolicyJson.ToJson(spec, Names));
    }

    [Fact]
    public void Unknown_zone_names_fail_loudly()
    {
        var spec = new FirewallPolicySpec { Name = "t", Source = new() { Zone = "Nope" }, Destination = new() { Zone = "Homelab" } };
        Assert.Contains("unknown firewall zone 'Nope'", Assert.Throws<ArgumentException>(() => FirewallPolicyJson.ToJson(spec, Names)).Message);
    }

    [Theory]
    [InlineData("""{"type":"DOMAIN","domainFilter":{"domains":["x.com"]}}""", "DOMAIN")]
    [InlineData("""{"type":"IP_ADDRESS","ipAddressFilter":{"type":"TRAFFIC_MATCHING_LIST","trafficMatchingListId":"x"}}""", "matching list")]
    public void Unsupported_filters_are_reported_not_misread(string filter, string expect)
    {
        var live = JsonNode.Parse(InternalToAzure)!.AsObject();
        live["destination"]!["trafficFilter"] = JsonNode.Parse(filter);
        var (spec, why) = FirewallPolicyJson.FromJson(live, Names);
        Assert.Null(spec);
        Assert.Contains(expect, why);
    }

    [Fact]
    public void A_schedule_other_than_always_is_reported()
    {
        var live = JsonNode.Parse(InternalToAzure)!.AsObject();
        live["schedule"] = JsonNode.Parse("""{"mode":"EVERY_DAY"}""");
        Assert.Equal("uses schedule", FirewallPolicyJson.FromJson(live, Names).Unsupported);
    }
}
