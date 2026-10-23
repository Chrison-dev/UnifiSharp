using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UnifiSharp.Legacy;

namespace UnifiSharp.Tests;

#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

// Pure unit tests for the legacy write adapter — no controller required.
public class UnifiLegacyClientTests
{
    // Mirrors UnifiLegacyClient's serializer (create sends only set fields).
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static UnifiLegacyOptions Options(string baseUrl = "https://gw.lan:11443/proxy/network/api/s/default") =>
        new() { BaseUrl = new Uri(baseUrl), Username = "admin", Password = "pw" };

    [Fact]
    public void ControllerRoot_strips_path_to_authority()
    {
        // Login lives at the controller root, not under the site path.
        Assert.Equal(new Uri("https://gw.lan:11443"), Options().ControllerRoot);
    }

    [Fact]
    public void VerifyTls_defaults_to_true()
    {
        Assert.True(Options().VerifyTls);
    }

    [Fact]
    public void ToString_does_not_leak_the_password()
    {
        var s = Options() with { Password = "super-secret" };
        Assert.DoesNotContain("super-secret", s.ToString());
        Assert.Contains("Password = ***", s.ToString());
    }

    [Fact]
    public void PortForward_create_payload_uses_snake_case_and_omits_server_fields()
    {
        var spec = new UnifiPortForward
        {
            Name = "pangolin-https",
            Enabled = true,
            PfwdInterface = "wan",
            Src = "any",
            DstPort = "443",
            Fwd = "10.10.0.13",
            FwdPort = "443",
            Proto = "tcp",
            Log = false,
        };

        var json = JsonSerializer.Serialize(spec, Json);

        Assert.Contains("\"pfwd_interface\":\"wan\"", json);
        Assert.Contains("\"dst_port\":\"443\"", json);
        Assert.Contains("\"fwd\":\"10.10.0.13\"", json);
        Assert.Contains("\"log\":false", json);     // explicit false must be sent, not dropped
        Assert.DoesNotContain("_id", json);          // server-assigned — omitted on create
        Assert.DoesNotContain("site_id", json);
    }

    [Fact]
    public void Envelope_deserializes_meta_and_data()
    {
        const string body =
            """
            { "meta": { "rc": "ok" }, "data": [
              { "_id": "abc", "site_id": "s1", "name": "pf", "enabled": true,
                "pfwd_interface": "wan", "dst_port": "443", "fwd": "10.10.0.13",
                "fwd_port": "443", "proto": "tcp", "log": false } ] }
            """;

        var env = JsonSerializer.Deserialize<UnifiLegacyEnvelope<UnifiPortForward>>(body, Json);

        Assert.NotNull(env);
        Assert.True(env!.Meta.IsOk);
        var pf = Assert.Single(env.Data);
        Assert.Equal("abc", pf.Id);
        Assert.Equal("s1", pf.SiteId);
        Assert.Equal("10.10.0.13", pf.Fwd);
        Assert.False(pf.Log);
    }

    [Fact]
    public void Envelope_error_rc_is_not_ok()
    {
        const string body = """{ "meta": { "rc": "error", "msg": "api.err.InvalidObject" }, "data": [] }""";
        var env = JsonSerializer.Deserialize<UnifiLegacyEnvelope<UnifiPortForward>>(body, Json);

        Assert.NotNull(env);
        Assert.False(env!.Meta.IsOk);
        Assert.Equal("api.err.InvalidObject", env.Meta.Msg);
    }

    [Fact]
    public void FirewallGroup_serializes_members_array()
    {
        var json = JsonSerializer.Serialize(
            new UnifiFirewallGroup { Name = "g", GroupType = "port-group", GroupMembers = ["443", "80"] }, Json);
        Assert.Contains("\"group_type\":\"port-group\"", json);
        Assert.Contains("\"group_members\":[\"443\",\"80\"]", json);
    }

    [Fact]
    public void Network_create_payload_uses_snake_case()
    {
        var json = JsonSerializer.Serialize(
            new UnifiNetwork { Name = "vlan10", Purpose = "corporate", VlanEnabled = true, Vlan = "10", IpSubnet = "10.10.0.1/24" }, Json);
        Assert.Contains("\"vlan_enabled\":true", json);
        Assert.Contains("\"ip_subnet\":\"10.10.0.1/24\"", json);
        Assert.DoesNotContain("dhcpd_enabled", json); // unset → omitted
    }

    [Fact]
    public void Network_vlan_reads_from_a_number_or_a_string()
    {
        // A real UniFi OS gateway sends a number; the test container sends a string.
        // Both must land in the same place, or ListNetworksAsync throws on real hardware
        // while every container test stays green.
        var fromNumber = JsonSerializer.Deserialize<UnifiNetwork>("""{"name":"Homelab","vlan":1010}""", Json);
        var fromString = JsonSerializer.Deserialize<UnifiNetwork>("""{"name":"Homelab","vlan":"1010"}""", Json);

        Assert.Equal("1010", fromNumber!.Vlan);
        Assert.Equal("1010", fromString!.Vlan);
        Assert.Null(JsonSerializer.Deserialize<UnifiNetwork>("""{"vlan":null}""", Json)!.Vlan);
    }

    [Fact]
    public void Network_vlan_still_serializes_as_a_string()
    {
        // Write behaviour is deliberately unchanged — creates have always sent a string.
        Assert.Contains("\"vlan\":\"3990\"", JsonSerializer.Serialize(new UnifiNetwork { Vlan = "3990" }, Json));
    }

    // ---- v2 site API / static DNS ----

    [Fact]
    public void SiteV2Url_is_derived_from_the_legacy_base()
    {
        // Different surface, same controller: …/api/s/<site> -> …/v2/api/site/<site>.
        Assert.Equal(
            new Uri("https://gw.lan:11443/proxy/network/v2/api/site/default"),
            Options().SiteV2Url);
        Assert.Equal("default", Options().Site);
    }

    [Fact]
    public void Site_is_parsed_from_the_base_url_not_assumed()
    {
        var o = Options("https://gw.lan/proxy/network/api/s/otherplace");
        Assert.Equal("otherplace", o.Site);
        Assert.Equal(new Uri("https://gw.lan/proxy/network/v2/api/site/otherplace"), o.SiteV2Url);
    }

    [Fact]
    public void StaticDns_round_trips_the_controller_shape()
    {
        // Captured from a real POST response — note v2 returns a BARE object, no envelope.
        const string body =
            """
            { "_id": "6a2e", "enabled": true, "key": "*.topaz.local.dev", "port": 0,
              "priority": 0, "record_type": "A", "ttl": 300, "value": "10.50.0.10", "weight": 0 }
            """;

        var r = JsonSerializer.Deserialize<UnifiStaticDnsRecord>(body, Json)!;

        Assert.Equal("*.topaz.local.dev", r.Key);      // wildcards are a supported key
        Assert.Equal("A", r.RecordType);
        Assert.Equal("10.50.0.10", r.Value);
        Assert.Equal(300, r.Ttl);
        Assert.True(r.Enabled);
    }

    [Fact]
    public void StaticDns_list_deserializes_a_bare_array()
    {
        // The legacy surface wraps everything in {meta,data}; v2 does not. Getting this
        // wrong fails at runtime only, against a real controller.
        const string body = """[{ "key": "a.example", "value": "10.0.0.1", "record_type": "A" }]""";
        var rows = JsonSerializer.Deserialize<IReadOnlyList<UnifiStaticDnsRecord>>(body, Json)!;
        Assert.Equal("a.example", Assert.Single(rows).Key);
    }

    [Fact]
    public void StaticDns_write_body_is_complete_because_partials_are_rejected()
    {
        // The v2 endpoint answers a partial PUT with 400 Validation failed, so every field
        // must serialize even at its default — the omit-nulls serializer must not thin it out.
        var json = JsonSerializer.Serialize(
            new UnifiStaticDnsRecord { Key = "*.lab.chrison.dev", Value = "10.10.0.13" }, Json);

        foreach (var field in new[] { "key", "value", "record_type", "enabled", "ttl", "port", "priority", "weight" })
        {
            Assert.Contains($"\"{field}\":", json);
        }

        Assert.DoesNotContain("\"_id\"", json);   // server-assigned; omitted on create
    }

    // ---- API-key auth mode ----

    private static UnifiLegacyOptions KeyOptions() =>
        new() { BaseUrl = new Uri("https://gw.lan/proxy/network/api/s/default"), ApiKey = "k" };

    [Fact]
    public void UsesApiKey_is_true_only_when_a_key_is_set()
    {
        Assert.True(KeyOptions().UsesApiKey);
        Assert.False(Options().UsesApiKey);
        Assert.False((KeyOptions() with { ApiKey = "" }).UsesApiKey); // empty is not a credential
    }

    [Fact]
    public void Validate_accepts_either_mode_and_rejects_neither()
    {
        KeyOptions().Validate();
        Options().Validate();

        // Half a session is not a credential — fail on construction, not as a 401 later.
        Assert.Throws<UnifiLegacyException>(() => (Options() with { Password = null }).Validate());
        Assert.Throws<UnifiLegacyException>(() => (Options() with { Username = null }).Validate());
    }

    [Fact]
    public void ToString_does_not_leak_the_api_key()
    {
        var s = (KeyOptions() with { ApiKey = "super-secret-key" }).ToString();
        Assert.DoesNotContain("super-secret-key", s);
        Assert.Contains("ApiKey ***", s);
    }

    [Fact]
    public void SiteUrlFor_builds_the_legacy_site_path()
    {
        Assert.Equal(
            new Uri("https://192.168.178.1/proxy/network/api/s/default"),
            UnifiLegacyOptions.SiteUrlFor("192.168.178.1"));
    }

    // ---- rest/user — the object a DHCP reservation lives on ----

    [Fact]
    public void User_reservation_payload_is_a_partial_update()
    {
        // The point of the partial: a reservation PUT must not blank the controller's
        // fingerprinting/history columns just because we didn't set them.
        var json = JsonSerializer.Serialize(
            new UnifiUser
            {
                UseFixedIp = true,
                FixedIp = "10.10.135.221",
                NetworkId = "68e07cc49da6501d8c970f47",
                LocalDnsRecord = "shell.devops.chrison.internal",
                LocalDnsRecordEnabled = true,
            }, Json);

        Assert.Contains("\"use_fixedip\":true", json);
        Assert.Contains("\"fixed_ip\":\"10.10.135.221\"", json);
        Assert.Contains("\"network_id\":\"68e07cc49da6501d8c970f47\"", json);
        Assert.Contains("\"local_dns_record\":\"shell.devops.chrison.internal\"", json);
        Assert.DoesNotContain("hostname", json);   // untouched → omitted, not nulled
        Assert.DoesNotContain("last_ip", json);
        Assert.DoesNotContain("\"_id\"", json);     // quoted: "network_id" legitimately contains _id
    }

    [Fact]
    public void User_retire_payload_sends_an_explicit_false()
    {
        // Retiring a reservation is use_fixedip:false, NOT deleting the client — an
        // explicit false must survive the omit-nulls serializer or the PUT is a no-op.
        var json = JsonSerializer.Serialize(new UnifiUser { UseFixedIp = false }, Json);
        Assert.Equal("""{"use_fixedip":false}""", json);
    }

    [Fact]
    public void User_envelope_deserializes_reservation_fields()
    {
        const string body =
            """
            { "meta": { "rc": "ok" }, "data": [
              { "_id": "6a7f", "mac": "bc:24:11:f6:9f:ae", "name": "shell (CT 3003)",
                "hostname": "shell", "use_fixedip": true, "fixed_ip": "10.10.135.221",
                "network_id": "68e0", "local_dns_record": "shell.devops.chrison.internal",
                "local_dns_record_enabled": true, "last_ip": "10.10.135.221" } ] }
            """;

        var env = JsonSerializer.Deserialize<UnifiLegacyEnvelope<UnifiUser>>(body, Json);

        Assert.NotNull(env);
        var u = Assert.Single(env!.Data);
        Assert.Equal("bc:24:11:f6:9f:ae", u.Mac);
        Assert.Equal("shell (CT 3003)", u.Name);
        Assert.True(u.UseFixedIp);
        Assert.Equal("10.10.135.221", u.FixedIp);
        Assert.Equal("shell.devops.chrison.internal", u.LocalDnsRecord);
    }

    // ---- rest/networkconf as raw JSON — read-modify-write without the lossy model ----

    // Synthetic, not captured: every JSON kind the controller can send, plus keys no model
    // knows about. A number with a trailing zero and an exponent proves number TEXT survives,
    // not just the value.
    private const string RawNetwork =
        """
        { "_id": "net1", "name": "Consumer", "purpose": "corporate", "vlan": 1010,
          "vlan_enabled": true, "dhcpd_dns_enabled": true,
          "dhcpd_dns_1": "10.20.0.1", "dhcpd_dns_2": "", "dhcpd_dns_3": "", "dhcpd_dns_4": "",
          "dhcpd_leasetime": 86400, "ratio": 1.50, "big": 1e3, "nothing": null,
          "nat_outbound_ip_addresses": [], "ipv6_ra_preferred_lifetime": 14400,
          "dhcp_relay": { "servers": ["10.0.0.1", null], "enabled": false },
          "some_future_field": { "nested": [1, { "deep": true }] } }
        """;

    private static string Envelope(string dataArray, string rc = "ok", string? msg = null) =>
        $$"""{ "meta": { "rc": "{{rc}}"{{(msg is null ? "" : $", \"msg\": \"{msg}\"")}} }, "data": {{dataArray}} }""";

    [Fact]
    public async Task GetNetworkRaw_gets_the_one_row_with_every_key_intact()
    {
        var handler = new FakeHandler(Envelope($"[{RawNetwork}]"));
        using var client = handler.Client();

        var net = await client.GetNetworkRawAsync("net1");

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/api/s/default/rest/networkconf/net1", req.Uri.AbsolutePath);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RawNetwork), net));
        Assert.Equal("1.50", net["ratio"]!.ToJsonString());   // number text, not a re-formatted double
        Assert.True(net.ContainsKey("nothing"));                // an explicit null is a key, not an absence
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{ "_id": "a" }, { "_id": "b" }]""")]
    [InlineData("[null]")]
    public async Task GetNetworkRaw_throws_unless_exactly_one_row(string data)
    {
        // Zero rows or two is not "the" network — a read-modify-write on a guess is the bug.
        using var client = new FakeHandler(Envelope(data)).Client();
        await Assert.ThrowsAsync<UnifiLegacyException>(() => client.GetNetworkRawAsync("net1"));
    }

    [Fact]
    public async Task ListNetworksRaw_returns_every_row_raw()
    {
        var handler = new FakeHandler(Envelope($$"""[{{RawNetwork}}, { "_id": "net2", "x": 1 }]"""));
        using var client = handler.Client();

        var nets = await client.ListNetworksRawAsync();

        Assert.EndsWith("/rest/networkconf", Assert.Single(handler.Requests).Uri.AbsolutePath);
        Assert.Equal(2, nets.Count);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RawNetwork), nets[0]));
    }

    [Fact]
    public async Task ReplaceNetworkRaw_puts_the_body_verbatim()
    {
        var handler = new FakeHandler(Envelope($"[{RawNetwork}]"));
        using var client = handler.Client();
        var read = await client.GetNetworkRawAsync("net1");

        await client.ReplaceNetworkRawAsync("net1", read);

        var put = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.EndsWith("/rest/networkconf/net1", put.Uri.AbsolutePath);
        var sent = JsonNode.Parse(put.Body!)!.AsObject();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RawNetwork), sent));
        // Byte-for-byte on numbers: a double round-trip would print 1.5 and 1000.
        Assert.Contains("\"ratio\":1.50", put.Body);
        Assert.Contains("\"big\":1e3", put.Body);
        Assert.Contains("\"vlan\":1010", put.Body);
        Assert.Contains("\"nothing\":null", put.Body);
    }

    [Fact]
    public async Task ReplaceNetworkRaw_does_not_mutate_the_callers_object()
    {
        using var client = new FakeHandler(Envelope("[]")).Client();
        var body = JsonNode.Parse(RawNetwork)!.AsObject();

        await client.ReplaceNetworkRawAsync("net1", body);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RawNetwork), body));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Raw_network_error_envelope_surfaces_the_api_msg(string verb)
    {
        // The legacy API answers validation failures with HTTP 400 and rc:error.
        using var client = new FakeHandler(
            Envelope("[]", rc: "error", msg: "api.err.InvalidPayload"), HttpStatusCode.BadRequest).Client();

        var ex = await Assert.ThrowsAsync<UnifiLegacyException>(() => verb == "GET"
            ? client.GetNetworkRawAsync("net1")
            : client.ReplaceNetworkRawAsync("net1", JsonNode.Parse(RawNetwork)!.AsObject()));

        Assert.Contains("api.err.InvalidPayload", ex.Message);
    }

    [Theory]
    [InlineData("""{ "_id": "net2", "name": "Other" }""")]
    [InlineData("""{ "name": "no id" }""")]
    [InlineData("""{ "_id": null }""")]
    [InlineData("""{ "_id": 1 }""")]
    public async Task ReplaceNetworkRaw_refuses_a_body_that_is_not_the_named_network(string json)
    {
        // A full replacement of net1 with net2's body would silently turn one network into another.
        var handler = new FakeHandler(Envelope("[]"));
        using var client = handler.Client();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ReplaceNetworkRawAsync("net1", JsonNode.Parse(json)!.AsObject()));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("a/../x")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a?b=c")]
    [InlineData("a#b")]
    [InlineData("a%2Fb")]
    [InlineData("a b")]
    public async Task Raw_network_rejects_an_id_that_is_not_a_plain_path_segment(string id)
    {
        var handler = new FakeHandler(Envelope($"[{RawNetwork}]"));
        using var client = handler.Client();
        var body = JsonNode.Parse(RawNetwork)!.AsObject();
        body["_id"] = id;

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetNetworkRawAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReplaceNetworkRawAsync(id, body));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("""{ "meta": { "rc": "ok" }, "data": null }""")]
    public async Task Raw_network_reports_a_null_data_array_as_an_api_shape_error(string response)
    {
        using var client = new FakeHandler(response).Client();

        await Assert.ThrowsAsync<UnifiLegacyException>(() => client.GetNetworkRawAsync("net1"));
        await Assert.ThrowsAsync<UnifiLegacyException>(() => client.ListNetworksRawAsync());
        await Assert.ThrowsAsync<UnifiLegacyException>(
            () => client.ReplaceNetworkRawAsync("net1", JsonNode.Parse(RawNetwork)!.AsObject()));
    }

    [Fact]
    public async Task ReplaceNetworkRaw_sends_With_output_byte_for_byte()
    {
        // DeepEquals compares values, so it cannot see number text, key order or a dropped
        // null; only the literal wire body can. Expected is RawNetwork minified by hand, with
        // the two DNS slots changed in place.
        var handler = new FakeHandler(Envelope("[]"));
        using var client = handler.Client();

        await client.ReplaceNetworkRawAsync("net1", NetworkDhcpDns.With(Raw(), ["10.20.0.53", "10.20.0.54"]));

        const string expected =
            """{"_id":"net1","name":"Consumer","purpose":"corporate","vlan":1010,"vlan_enabled":true,"dhcpd_dns_enabled":true,"dhcpd_dns_1":"10.20.0.53","dhcpd_dns_2":"10.20.0.54","dhcpd_dns_3":"","dhcpd_dns_4":"","dhcpd_leasetime":86400,"ratio":1.50,"big":1e3,"nothing":null,"nat_outbound_ip_addresses":[],"ipv6_ra_preferred_lifetime":14400,"dhcp_relay":{"servers":["10.0.0.1",null],"enabled":false},"some_future_field":{"nested":[1,{"deep":true}]}}""";
        Assert.Equal(expected, Assert.Single(handler.Requests).Body);
    }

    // ---- NetworkDhcpDns — the dhcpd_dns_N naming, kept out of every caller ----

    private static JsonObject Raw() => JsonNode.Parse(RawNetwork)!.AsObject();

    [Fact]
    public void DhcpDns_With_changes_only_the_dns_keys_and_leaves_the_input_alone()
    {
        var input = Raw();

        var output = NetworkDhcpDns.With(input, ["10.20.0.53", "10.20.0.54"]);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RawNetwork), input));   // input untouched
        Assert.NotSame(input, output);

        var changed = input.Select(kv => kv.Key)
            .Union(output.Select(kv => kv.Key))
            .Where(k => !JsonNode.DeepEquals(input[k], output[k]) || input.ContainsKey(k) != output.ContainsKey(k))
            .ToHashSet();
        // dhcpd_dns_enabled was already true and slots 3-4 already blank, so only 1 and 2 move here;
        // the invariant is that nothing OUTSIDE the DNS key set moves.
        Assert.Subset(
            new HashSet<string> { "dhcpd_dns_enabled", "dhcpd_dns_1", "dhcpd_dns_2", "dhcpd_dns_3", "dhcpd_dns_4" },
            changed);
        Assert.Equal("10.20.0.53", (string?)output["dhcpd_dns_1"]);
        Assert.Equal("10.20.0.54", (string?)output["dhcpd_dns_2"]);
    }

    [Fact]
    public void DhcpDns_With_enables_dns_and_writes_all_four_slots_from_auto()
    {
        var input = JsonNode.Parse("""{ "_id": "n", "dhcpd_dns_enabled": false, "other": 7 }""")!.AsObject();

        var output = NetworkDhcpDns.With(input, ["10.20.0.53"]);

        Assert.True((bool)output["dhcpd_dns_enabled"]!);
        Assert.Equal("10.20.0.53", (string?)output["dhcpd_dns_1"]);
        Assert.Equal("", (string?)output["dhcpd_dns_2"]);
        Assert.Equal("", (string?)output["dhcpd_dns_3"]);
        Assert.Equal("", (string?)output["dhcpd_dns_4"]);
        Assert.Equal(7, (int)output["other"]!);
        Assert.False((bool)input["dhcpd_dns_enabled"]!);
        Assert.False(input.ContainsKey("dhcpd_dns_1"));
    }

    [Fact]
    public void DhcpDns_With_blanks_a_dropped_slot_rather_than_removing_it()
    {
        // Removing the key would leave the controller's old value in place on a full PUT.
        var input = NetworkDhcpDns.With(Raw(), ["1.1.1.1", "8.8.8.8", "9.9.9.9"]);

        var output = NetworkDhcpDns.With(input, ["1.1.1.1", "8.8.8.8"]);

        Assert.True(output.ContainsKey("dhcpd_dns_3"));
        Assert.Equal("", (string?)output["dhcpd_dns_3"]);
        Assert.Equal("9.9.9.9", (string?)input["dhcpd_dns_3"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void DhcpDns_With_rejects_an_empty_or_oversized_list(int count)
    {
        // Empty would mean "clear the DNS" — never something a declaration should do silently.
        var servers = Enumerable.Range(1, count).Select(i => $"10.0.0.{i}").ToList();
        Assert.Throws<ArgumentException>(() => NetworkDhcpDns.With(Raw(), servers));
    }

    [Theory]
    [InlineData("""{ "dhcpd_dns_enabled": false, "dhcpd_dns_1": "1.1.1.1" }""")]
    [InlineData("""{ "dhcpd_dns_1": "1.1.1.1" }""")]
    public void DhcpDns_Read_is_null_when_dns_is_automatic(string json)
    {
        // Disabled means the gateway hands itself out; stale slot values are not the live list.
        Assert.Null(NetworkDhcpDns.Read(JsonNode.Parse(json)!.AsObject()));
    }

    [Fact]
    public void DhcpDns_Read_returns_the_slots_in_order_skipping_blanks()
    {
        var net = JsonNode.Parse(
            """{ "dhcpd_dns_enabled": true, "dhcpd_dns_1": "10.20.0.53", "dhcpd_dns_2": "", "dhcpd_dns_3": "10.20.0.54" }""")!
            .AsObject();

        Assert.Equal(["10.20.0.53", "10.20.0.54"], NetworkDhcpDns.Read(net));
    }

    [Theory]
    [InlineData("""{ "dhcpd_dns_enabled": "yes" }""")]
    [InlineData("""{ "dhcpd_dns_enabled": true, "dhcpd_dns_1": 10 }""")]
    public void DhcpDns_Read_throws_on_a_shape_it_does_not_recognise(string json)
    {
        // A field present but shaped differently is unknown, not "auto" and not empty.
        Assert.Throws<UnifiLegacyException>(() => NetworkDhcpDns.Read(JsonNode.Parse(json)!.AsObject()));
    }

    [Theory]
    [InlineData("""{ "dhcpd_dns_enabled": "yes" }""")]
    [InlineData("""{ "dhcpd_dns_enabled": null }""")]
    [InlineData("""{ "dhcpd_dns_enabled": true, "dhcpd_dns_1": 10 }""")]
    [InlineData("""{ "dhcpd_dns_enabled": false, "dhcpd_dns_2": { "ip": "1.1.1.1" } }""")]
    public void DhcpDns_With_throws_on_a_shape_it_does_not_recognise(string json)
    {
        // Overwriting a malformed key would paper over the very drift Read refuses to guess at.
        Assert.Throws<UnifiLegacyException>(
            () => NetworkDhcpDns.With(JsonNode.Parse(json)!.AsObject(), ["10.20.0.53"]));
    }

    // Records each request (body read eagerly — the client disposes it) and answers with a
    // canned envelope. API-key options, so the session never makes a login request.
    private sealed class FakeHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

        public UnifiLegacyClient Client() =>
            new(new UnifiLegacySession(KeyOptions(), this), ownsSession: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}
