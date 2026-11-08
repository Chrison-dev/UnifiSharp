using System.Net;
using System.Text.Json.Nodes;
using UnifiSharp.Legacy;

namespace UnifiSharp.Tests;

#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

public class UnifiNatRuleTests
{
    private const string ConsumerId = "69449ac60a8b134de4bd2333";

    private static readonly UnifiNatNames Names = new(new Dictionary<string, string> { ["Consumer"] = ConsumerId });

    // The two rules as the UCG (Network 10.6.106) returns them, per the Homelab Blocky session.
    private const string LiveDnat = """
        {"_id":"6addb2ecc60ad991b79a5e63","type":"DNAT","description":"consumer-dns-redirect","enabled":true,
         "ip_version":"IPV4","protocol":"tcp_udp","in_interface":"69449ac60a8b134de4bd2333","ip_address":"10.20.0.53",
         "port":"53","rule_index":1,
         "source_filter":{"filter_type":"ADDRESS_AND_PORT","address":"10.20.0.53-10.20.0.54","invert_address":true,"invert_port":false,"firewall_group_ids":[]},
         "destination_filter":{"filter_type":"ADDRESS_AND_PORT","address":"10.20.0.53-10.20.0.54","port":"53","invert_address":true,"invert_port":false,"firewall_group_ids":[]},
         "logging":false,"exclude":false,"is_predefined":false,"pppoe_use_base_interface":false,"setting_preference":"manual"}
        """;

    private const string LiveMasquerade = """
        {"_id":"6addb2d9c60ad991b79a5e5f","type":"MASQUERADE","description":"consumer-dns-redirect-masq","enabled":true,
         "ip_version":"IPV4","protocol":"tcp_udp","out_interface":"69449ac60a8b134de4bd2333","rule_index":2,
         "source_filter":{"filter_type":"ADDRESS_AND_PORT","address":"10.20.0.0/16","invert_address":false,"invert_port":false,"firewall_group_ids":[]},
         "destination_filter":{"filter_type":"ADDRESS_AND_PORT","address":"10.20.0.53","port":"53","invert_address":false,"invert_port":false,"firewall_group_ids":[]},
         "logging":false,"exclude":false,"is_predefined":false,"pppoe_use_base_interface":false,"setting_preference":"manual"}
        """;

    private static readonly UnifiNatRuleSpec DnatSpec = new()
    {
        Name = "consumer-dns-redirect",
        Type = UnifiNatType.Dnat,
        Protocol = "tcp_udp",
        InNetwork = "Consumer",
        TranslatedAddress = "10.20.0.53",
        TranslatedPort = "53",
        Source = new() { Address = "10.20.0.53-10.20.0.54", InvertAddress = true },
        Destination = new() { Address = "10.20.0.53-10.20.0.54", Port = "53", InvertAddress = true },
        RuleIndex = 1,
    };

    private static readonly UnifiNatRuleSpec MasqSpec = new()
    {
        Name = "consumer-dns-redirect-masq",
        Type = UnifiNatType.Masquerade,
        Protocol = "tcp_udp",
        OutNetwork = "Consumer",
        Source = new() { Address = "10.20.0.0/16" },
        Destination = new() { Address = "10.20.0.53", Port = "53" },
        RuleIndex = 2,
    };

    [Fact]
    public void FromJson_reads_the_live_dnat_rule()
        => Assert.Equal(DnatSpec, UnifiNatRuleJson.FromJson(JsonNode.Parse(LiveDnat)!.AsObject(), Names));

    [Fact]
    public void FromJson_reads_the_live_masquerade_rule()
        => Assert.Equal(MasqSpec, UnifiNatRuleJson.FromJson(JsonNode.Parse(LiveMasquerade)!.AsObject(), Names));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToJson_then_FromJson_round_trips(bool dnat)
    {
        var spec = dnat ? DnatSpec : MasqSpec;
        Assert.Equal(spec, UnifiNatRuleJson.FromJson(UnifiNatRuleJson.ToJson(spec, Names), Names));
    }

    [Fact]
    public void ToJson_resolves_network_names_to_ids_and_omits_what_masquerade_has_no_use_for()
    {
        var body = UnifiNatRuleJson.ToJson(MasqSpec, Names);
        Assert.Equal(ConsumerId, body["out_interface"]!.GetValue<string>());
        Assert.Null(body["in_interface"]);
        Assert.Null(body["ip_address"]);
        Assert.Null(body["port"]);
        Assert.Equal("consumer-dns-redirect-masq", body["description"]!.GetValue<string>());
        Assert.Equal("MASQUERADE", body["type"]!.GetValue<string>());
    }

    [Fact]
    public void ToJson_writes_a_filter_with_no_address_or_port_as_NONE()
    {
        var body = UnifiNatRuleJson.ToJson(DnatSpec with { Source = new() }, Names);
        Assert.Equal("NONE", body["source_filter"]!["filter_type"]!.GetValue<string>());
        Assert.Null(body["source_filter"]!["address"]);
    }

    [Fact]
    public void ToJson_refuses_an_unknown_network()
        => Assert.Throws<ArgumentException>(() => UnifiNatRuleJson.ToJson(DnatSpec with { InNetwork = "Nope" }, Names));

    [Fact]
    public void FromJson_refuses_a_filter_it_cannot_write_back()
    {
        var rule = JsonNode.Parse(LiveDnat)!.AsObject();
        rule["source_filter"]!["filter_type"] = "FIREWALL_GROUPS";
        Assert.Throws<ArgumentException>(() => UnifiNatRuleJson.FromJson(rule, Names));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Live_to_spec_to_json_keeps_every_field_a_live_rule_carries(bool dnat)
    {
        // Spec -> JSON -> spec can't see a field ToJson drops; live -> spec -> JSON can.
        var live = JsonNode.Parse(dnat ? LiveDnat : LiveMasquerade)!.AsObject();
        var back = UnifiNatRuleJson.ToJson(UnifiNatRuleJson.FromJson(live, Names), Names);
        live.Remove("_id");
        Assert.Equal(Sorted(live).ToJsonString(), Sorted(back).ToJsonString());
    }

    [Theory]
    [InlineData("ip_version", "IPV6")]
    [InlineData("setting_preference", "auto")]
    public void FromJson_refuses_a_field_it_would_otherwise_rewrite(string key, string value)
    {
        var rule = JsonNode.Parse(LiveDnat)!.AsObject();
        rule[key] = value;
        Assert.Throws<ArgumentException>(() => UnifiNatRuleJson.FromJson(rule, Names));
    }

    [Theory]
    [InlineData("exclude")]
    [InlineData("pppoe_use_base_interface")]
    public void FromJson_refuses_a_flag_it_would_otherwise_rewrite(string key)
    {
        var rule = JsonNode.Parse(LiveDnat)!.AsObject();
        rule[key] = true;
        Assert.Throws<ArgumentException>(() => UnifiNatRuleJson.FromJson(rule, Names));
    }

    [Fact]
    public void An_unknown_network_id_round_trips_as_itself()
    {
        var rule = JsonNode.Parse(LiveDnat)!.AsObject();
        rule["in_interface"] = "abc";
        // known only by value elsewhere in the lookup: the id passes through, an unknown one still throws
        var names = new UnifiNatNames(new Dictionary<string, string> { ["Consumer"] = ConsumerId, ["Other"] = "abc" });
        Assert.Equal("Other", UnifiNatRuleJson.FromJson(rule, names).InNetwork);
        Assert.Equal("abc", UnifiNatRuleJson.ToJson(UnifiNatRuleJson.FromJson(rule, names), names)["in_interface"]!.GetValue<string>());
        Assert.Throws<ArgumentException>(() => UnifiNatRuleJson.ToJson(DnatSpec with { InNetwork = "zzz" }, names));
    }

    [Fact]
    public async Task Update_keeps_the_live_rule_index_when_the_spec_has_none()
    {
        var h = new NatHandler($"[{LiveDnat}]");
        using var c = h.Client();
        await c.UpdateNatRuleAsync("6addb2ecc60ad991b79a5e63", DnatSpec with { RuleIndex = null }, Names);
        var put = h.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Equal(1, JsonNode.Parse(put.Body!)!["rule_index"]!.GetValue<int>());
    }

    private static JsonObject Sorted(JsonObject o) => new(o.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => KeyValuePair.Create(kv.Key, kv.Value is JsonObject c ? Sorted(c) : kv.Value?.DeepClone())));

    [Fact]
    public void Equality_notices_an_inverted_match_flipping()
        => Assert.NotEqual(DnatSpec, DnatSpec with { Source = DnatSpec.Source with { InvertAddress = false } });

    [Fact]
    public async Task List_returns_rules_and_flags_predefined_ones()
    {
        var h = new NatHandler($"[{LiveDnat},{LiveMasquerade.Replace("\"is_predefined\":false", "\"is_predefined\":true")}]");
        using var c = h.Client();
        var rules = await c.ListNatRulesAsync();
        Assert.Equal(["consumer-dns-redirect", "consumer-dns-redirect-masq"], rules.Select(r => r.Name));
        Assert.Equal([false, true], rules.Select(r => r.IsPredefined));
        Assert.EndsWith("/proxy/network/v2/api/site/default/nat", h.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Create_picks_the_next_free_rule_index_when_the_spec_has_none()
    {
        var h = new NatHandler($"[{LiveDnat},{LiveMasquerade}]");
        using var c = h.Client();
        await c.CreateNatRuleAsync(DnatSpec with { Name = "third", RuleIndex = null }, Names);
        var post = h.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal(3, JsonNode.Parse(post.Body!)!["rule_index"]!.GetValue<int>());
    }

    [Fact]
    public async Task Create_keeps_a_declared_rule_index_and_does_not_list_first()
    {
        var h = new NatHandler("[]");
        using var c = h.Client();
        await c.CreateNatRuleAsync(DnatSpec, Names);
        Assert.Equal([HttpMethod.Post], h.Requests.Select(r => r.Method));
        Assert.Equal(1, JsonNode.Parse(h.Requests[0].Body!)!["rule_index"]!.GetValue<int>());
    }

    [Fact]
    public async Task Update_sends_the_whole_rule_with_its_id()
    {
        var h = new NatHandler($"[{LiveDnat}]");
        using var c = h.Client();
        await c.UpdateNatRuleAsync("6addb2ecc60ad991b79a5e63", DnatSpec with { Enabled = false }, Names);
        var put = h.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.EndsWith("/nat/6addb2ecc60ad991b79a5e63", put.Uri.AbsolutePath);
        var body = JsonNode.Parse(put.Body!)!.AsObject();
        Assert.Equal("6addb2ecc60ad991b79a5e63", body["_id"]!.GetValue<string>());
        Assert.False(body["enabled"]!.GetValue<bool>());
        Assert.Equal("10.20.0.53", body["ip_address"]!.GetValue<string>());
    }

    [Fact]
    public async Task Delete_removes_a_user_rule()
    {
        var h = new NatHandler($"[{LiveDnat}]");
        using var c = h.Client();
        await c.DeleteNatRuleAsync("6addb2ecc60ad991b79a5e63");
        Assert.Contains(h.Requests, r => r.Method == HttpMethod.Delete && r.Uri.AbsolutePath.EndsWith("/nat/6addb2ecc60ad991b79a5e63"));
    }

    [Fact]
    public async Task Delete_and_update_refuse_a_predefined_rule_without_writing()
    {
        var predefined = LiveDnat.Replace("\"is_predefined\":false", "\"is_predefined\":true");
        var h = new NatHandler($"[{predefined}]");
        using var c = h.Client();
        await Assert.ThrowsAsync<UnifiLegacyException>(() => c.DeleteNatRuleAsync("6addb2ecc60ad991b79a5e63"));
        await Assert.ThrowsAsync<UnifiLegacyException>(() => c.UpdateNatRuleAsync("6addb2ecc60ad991b79a5e63", DnatSpec, Names));
        Assert.All(h.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task Delete_and_update_refuse_an_id_that_is_not_on_the_controller()
    {
        var h = new NatHandler("[]");
        using var c = h.Client();
        await Assert.ThrowsAsync<UnifiLegacyException>(() => c.DeleteNatRuleAsync("deadbeef"));
        await Assert.ThrowsAsync<UnifiLegacyException>(() => c.UpdateNatRuleAsync("deadbeef", DnatSpec, Names));
        Assert.All(h.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a?b")]
    public async Task Delete_refuses_an_id_that_is_not_a_plain_path_segment(string id)
    {
        var h = new NatHandler("[]");
        using var c = h.Client();
        await Assert.ThrowsAsync<ArgumentException>(() => c.DeleteNatRuleAsync(id));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Create_surfaces_the_controllers_message_on_a_400()
    {
        var h = new NatHandler("[]", postStatus: HttpStatusCode.BadRequest, postBody: """{"message":"api.err.NatRuleInvalidParameters: duplicate rule_index"}""");
        using var c = h.Client();
        var ex = await Assert.ThrowsAsync<UnifiLegacyException>(() => c.CreateNatRuleAsync(DnatSpec, Names));
        Assert.Contains("duplicate rule_index", ex.Message);
    }

    // GET nat answers the canned list; POST/PUT echo the body (POST adds an _id); DELETE is 200 empty.
    private sealed class NatHandler(string list, HttpStatusCode postStatus = HttpStatusCode.OK, string? postBody = null) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

        public UnifiLegacyClient Client() => new(new UnifiLegacySession(
            new UnifiLegacyOptions { BaseUrl = new Uri("https://gw.lan:11443/proxy/network/api/s/default"), ApiKey = "k" }, this), ownsSession: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!, body));
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent(list) };
            if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.OK) { Content = new StringContent("") };
            if (request.Method == HttpMethod.Post && postStatus != HttpStatusCode.OK)
                return new(postStatus) { Content = new StringContent(postBody ?? "") };
            var echo = JsonNode.Parse(body!)!.AsObject();
            echo["_id"] ??= "newid0000000000000000aa";
            return new(HttpStatusCode.OK) { Content = new StringContent(echo.ToJsonString()) };
        }
    }
}
