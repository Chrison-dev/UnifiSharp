using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiSharp.Legacy;

/// <summary>
/// Reads a JSON value that the controller types inconsistently — a string on some
/// builds, a bare number on others — into a <see cref="string"/>. Writes always emit
/// a string, matching what create calls have historically sent.
/// <para>Needed because a real UniFi OS gateway returns <c>"vlan": 1010</c> while the
/// <c>.containers/unifi</c> test container returns <c>"vlan": "1010"</c>. Typing the
/// property as <c>string</c> alone made <c>ListNetworksAsync</c> throw against real
/// hardware while passing every container test — exactly the version-brittleness
/// ADR-0003 warns about.</para>
/// </summary>
internal sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"expected string or number, got {reader.TokenType}"),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}

// DTOs for the legacy controller REST API. Each record doubles as the create
// request body and the response row: null properties are omitted on serialize
// (so a create sends only the fields you set), while the server echoes back
// _id / site_id on the response. Property names match the API's snake_case
// exactly — these shapes were captured from real round-trips against the
// .containers/unifi UniFi OS Server (see PR #224).

/// <summary>The standard legacy envelope: <c>{ "meta": { "rc": "ok" }, "data": [ … ] }</c>.</summary>
public sealed record UnifiLegacyEnvelope<T>
{
    [JsonPropertyName("meta")] public UnifiLegacyMeta Meta { get; init; } = new();
    [JsonPropertyName("data")] public IReadOnlyList<T> Data { get; init; } = [];
}

/// <summary>Envelope meta. <c>rc</c> is <c>"ok"</c> or <c>"error"</c>; <c>msg</c> carries the API error code.</summary>
public sealed record UnifiLegacyMeta
{
    [JsonPropertyName("rc")] public string Rc { get; init; } = "";
    [JsonPropertyName("msg")] public string? Msg { get; init; }
    public bool IsOk => string.Equals(Rc, "ok", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A WAN→LAN port-forward (<c>rest/portforward</c>). Mirrors the homelab's
/// <c>pangolin-https</c> rule (WAN :443 → 10.10.0.13:443).
/// </summary>
public sealed record UnifiPortForward
{
    [JsonPropertyName("_id")] public string? Id { get; init; }
    [JsonPropertyName("site_id")] public string? SiteId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; init; }
    /// <summary>The WAN the rule binds to (e.g. <c>wan</c>, <c>wan2</c>, <c>both</c>).</summary>
    [JsonPropertyName("pfwd_interface")] public string? PfwdInterface { get; init; }
    /// <summary>Permitted source — <c>any</c> or a CIDR/IP.</summary>
    [JsonPropertyName("src")] public string? Src { get; init; }
    /// <summary>Destination (WAN) port or range, as a string (e.g. <c>443</c>, <c>8080-8090</c>).</summary>
    [JsonPropertyName("dst_port")] public string? DstPort { get; init; }
    /// <summary>Forward-to LAN IP.</summary>
    [JsonPropertyName("fwd")] public string? Fwd { get; init; }
    /// <summary>Forward-to LAN port or range, as a string.</summary>
    [JsonPropertyName("fwd_port")] public string? FwdPort { get; init; }
    /// <summary><c>tcp</c>, <c>udp</c>, or <c>tcp_udp</c>.</summary>
    [JsonPropertyName("proto")] public string? Proto { get; init; }
    [JsonPropertyName("log")] public bool? Log { get; init; }
}

/// <summary>
/// A firewall group (<c>rest/firewallgroup</c>) — a reusable set of ports or
/// addresses referenced by firewall rules.
/// </summary>
public sealed record UnifiFirewallGroup
{
    [JsonPropertyName("_id")] public string? Id { get; init; }
    [JsonPropertyName("site_id")] public string? SiteId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    /// <summary><c>port-group</c>, <c>address-group</c>, or <c>ipv6-address-group</c>.</summary>
    [JsonPropertyName("group_type")] public string? GroupType { get; init; }
    /// <summary>Members — ports (for a port-group) or addresses/CIDRs (for an address-group).</summary>
    [JsonPropertyName("group_members")] public IReadOnlyList<string>? GroupMembers { get; init; }
}

/// <summary>
/// A known client (<c>rest/user</c>) — the object a <b>DHCP reservation</b> lives on.
/// The controller keeps one entry per MAC it has ever seen; a reservation is not a
/// separate resource but these fields set on that entry, so "creating" one is a
/// <c>PUT</c> onto an existing row far more often than a <c>POST</c>.
/// <para>Only null properties are omitted on serialize, so a <c>PUT</c> built from a
/// fresh instance with two fields set is a genuine partial update and leaves the
/// controller's fingerprinting, naming and history columns alone.</para>
/// </summary>
public sealed record UnifiUser
{
    [JsonPropertyName("_id")] public string? Id { get; init; }
    [JsonPropertyName("site_id")] public string? SiteId { get; init; }
    /// <summary>MAC address, lowercase colon-separated — the client's identity.</summary>
    [JsonPropertyName("mac")] public string? Mac { get; init; }
    /// <summary>Operator-assigned alias, e.g. <c>shell (CT 3003)</c>. Distinct from <c>hostname</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
    /// <summary>DHCP-reported hostname; read-only in practice.</summary>
    [JsonPropertyName("hostname")] public string? Hostname { get; init; }
    /// <summary>Whether the fixed IP is in force. Setting this false retires a reservation without losing the entry.</summary>
    [JsonPropertyName("use_fixedip")] public bool? UseFixedIp { get; init; }
    /// <summary>The reserved address. Ignored by the controller unless <see cref="UseFixedIp"/> is true.</summary>
    [JsonPropertyName("fixed_ip")] public string? FixedIp { get; init; }
    /// <summary>_id of the network the reservation belongs to (see <see cref="UnifiNetwork"/>).</summary>
    [JsonPropertyName("network_id")] public string? NetworkId { get; init; }
    /// <summary>
    /// Per-client local DNS name, e.g. <c>shell.devops.chrison.internal</c> — how a name
    /// resolves outside its own network's domain. Requires <see cref="LocalDnsRecordEnabled"/>.
    /// </summary>
    [JsonPropertyName("local_dns_record")] public string? LocalDnsRecord { get; init; }
    [JsonPropertyName("local_dns_record_enabled")] public bool? LocalDnsRecordEnabled { get; init; }
    /// <summary>Last address the controller saw this client on — diagnostic, not the reservation.</summary>
    [JsonPropertyName("last_ip")] public string? LastIp { get; init; }
}

/// <summary>
/// A network / VLAN (<c>rest/networkconf</c>). Only the commonly-managed fields
/// are typed; the server fills the rest with defaults on create.
/// </summary>
public sealed record UnifiNetwork
{
    [JsonPropertyName("_id")] public string? Id { get; init; }
    [JsonPropertyName("site_id")] public string? SiteId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    /// <summary>Network role — typically <c>corporate</c> (a standard L3 network) or <c>guest</c>.</summary>
    [JsonPropertyName("purpose")] public string? Purpose { get; init; }
    [JsonPropertyName("vlan_enabled")] public bool? VlanEnabled { get; init; }
    /// <summary>
    /// VLAN id, as a string (e.g. <c>1010</c>). Read leniently: a real gateway sends a
    /// number here, the test container a string — see <see cref="FlexibleStringConverter"/>.
    /// </summary>
    [JsonPropertyName("vlan")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Vlan { get; init; }
    /// <summary>Gateway/CIDR, e.g. <c>10.10.0.1/16</c>.</summary>
    [JsonPropertyName("ip_subnet")] public string? IpSubnet { get; init; }
    [JsonPropertyName("dhcpd_enabled")] public bool? DhcpdEnabled { get; init; }
    [JsonPropertyName("dhcpd_start")] public string? DhcpdStart { get; init; }
    [JsonPropertyName("dhcpd_stop")] public string? DhcpdStop { get; init; }
    [JsonPropertyName("is_nat")] public bool? IsNat { get; init; }
}
