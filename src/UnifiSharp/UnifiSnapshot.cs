namespace UnifiSharp;

/// <summary>A structured, read-only snapshot of the UniFi network (discover output).</summary>
public sealed record UnifiSnapshot
{
    public required IReadOnlyList<UnifiSiteSnapshot> Sites { get; init; }
}

/// <summary>A site and the detail it contains. Mirrors the ProxmoxSharp discover style.</summary>
public sealed record UnifiSiteSnapshot
{
    public required string Site { get; init; }
    public Guid? Id { get; init; }

    /// <summary>Total counts — kept for quick at-a-glance summaries.</summary>
    public int DeviceCount { get; init; }
    public int ClientCount { get; init; }

    public IReadOnlyList<UnifiNetworkInfo> Networks { get; init; } = [];
    public IReadOnlyList<UnifiWlanInfo> Wlans { get; init; } = [];
    public UnifiFirewallSnapshot Firewall { get; init; } = new();
    public IReadOnlyList<UnifiDeviceInfo> Devices { get; init; } = [];
    public IReadOnlyList<UnifiClientInfo> Clients { get; init; } = [];
}

/// <summary>A network / VLAN. Subnet is not exposed by the integration list API; <see cref="Purpose"/> carries the network's management role.</summary>
public sealed record UnifiNetworkInfo
{
    public required string Name { get; init; }
    public Guid? Id { get; init; }
    public int? VlanId { get; init; }
    public bool? Enabled { get; init; }

    /// <summary>The network's management/purpose role (the API's <c>management</c> field, e.g. routed/switched).</summary>
    public string? Purpose { get; init; }
    public bool? IsDefault { get; init; }
}

/// <summary>A WLAN / SSID broadcast. Security type and the bound network are surfaced; band is not exposed in the overview API.</summary>
public sealed record UnifiWlanInfo
{
    public required string Ssid { get; init; }
    public Guid? Id { get; init; }
    public bool? Enabled { get; init; }

    /// <summary>Security configuration type (e.g. wpa2, wpa3, open) where the API reports it.</summary>
    public string? Security { get; init; }

    /// <summary>The bound network reference type (the API exposes only the reference kind, not its id).</summary>
    public string? NetworkType { get; init; }
}

/// <summary>Firewall read surface: zones, policies, and legacy ACL rules.</summary>
public sealed record UnifiFirewallSnapshot
{
    public IReadOnlyList<UnifiFirewallZoneInfo> Zones { get; init; } = [];
    public IReadOnlyList<UnifiFirewallPolicyInfo> Policies { get; init; } = [];
    public IReadOnlyList<UnifiAclRuleInfo> AclRules { get; init; } = [];
}

/// <summary>A firewall zone and the networks it groups.</summary>
public sealed record UnifiFirewallZoneInfo
{
    public required string Name { get; init; }
    public Guid? Id { get; init; }
    public IReadOnlyList<Guid> NetworkIds { get; init; } = [];
}

/// <summary>A zone-based firewall policy.</summary>
public sealed record UnifiFirewallPolicyInfo
{
    public required string Name { get; init; }
    public Guid? Id { get; init; }
    public int? Index { get; init; }
    public bool? Enabled { get; init; }
    public string? Action { get; init; }
    public Guid? SourceZoneId { get; init; }
    public Guid? DestinationZoneId { get; init; }
}

/// <summary>A (legacy) ACL rule.</summary>
public sealed record UnifiAclRuleInfo
{
    public required string Name { get; init; }
    public Guid? Id { get; init; }
    public int? Index { get; init; }
    public bool? Enabled { get; init; }
    public string? Type { get; init; }
    public string? Action { get; init; }
}

/// <summary>An adopted infrastructure device (AP / switch / gateway).</summary>
public sealed record UnifiDeviceInfo
{
    public required string Name { get; init; }
    public Guid? Id { get; init; }
    public string? Model { get; init; }
    public string? MacAddress { get; init; }
    public string? IpAddress { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? State { get; init; }
}

/// <summary>A connected client (wired or wireless).</summary>
public sealed record UnifiClientInfo
{
    public required string Name { get; init; }
    public Guid? Id { get; init; }
    public string? Type { get; init; }
    public string? IpAddress { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
}
