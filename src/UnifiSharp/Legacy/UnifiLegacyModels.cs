using System.Text.Json.Serialization;

namespace UnifiSharp.Legacy;

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
    /// <summary>VLAN id, as a string (e.g. <c>1010</c>).</summary>
    [JsonPropertyName("vlan")] public string? Vlan { get; init; }
    /// <summary>Gateway/CIDR, e.g. <c>10.10.0.1/16</c>.</summary>
    [JsonPropertyName("ip_subnet")] public string? IpSubnet { get; init; }
    [JsonPropertyName("dhcpd_enabled")] public bool? DhcpdEnabled { get; init; }
    [JsonPropertyName("dhcpd_start")] public string? DhcpdStart { get; init; }
    [JsonPropertyName("dhcpd_stop")] public string? DhcpdStop { get; init; }
    [JsonPropertyName("is_nat")] public bool? IsNat { get; init; }
}
