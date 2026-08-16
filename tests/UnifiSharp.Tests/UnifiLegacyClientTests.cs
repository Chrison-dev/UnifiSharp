using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiSharp.Legacy;

namespace UnifiSharp.Tests;

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
}
