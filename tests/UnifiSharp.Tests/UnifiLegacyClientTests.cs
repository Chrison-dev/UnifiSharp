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
}
