namespace UnifiSharp.Firewall;

public enum FirewallAction { Allow, Block, Reject }

public enum FirewallIpVersion { IPv4, IPv6, Both }

/// <summary>
/// One end of a zone-based firewall policy: the zone, plus at most one way of narrowing it
/// (addresses, networks, or an IPv6 interface identifier) and optionally ports.
/// </summary>
public sealed record FirewallEndpoint
{
    /// <summary>Zone NAME as the controller shows it (e.g. "External", "Homelab"); resolved to an id on write.</summary>
    public required string Zone { get; init; }

    /// <summary>IPs, CIDR subnets (contain '/'), or ranges written "first-last".</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <summary>Network NAMES (e.g. "Consumer"); resolved to ids on write.</summary>
    public IReadOnlyList<string> Networks { get; init; } = [];

    /// <summary>
    /// IPv6 interface identifier, e.g. "::6342:9". Matches that suffix under ANY prefix, so a
    /// rule survives the ISP re-delegating the prefix. Written to the API with the /64 IID mask.
    /// </summary>
    public string? Ipv6InterfaceId { get; init; }

    /// <summary>Ports or ranges, e.g. "443", "63429", "1000-2000". Empty = all ports.</summary>
    public IReadOnlyList<string> Ports { get; init; } = [];

    /// <summary>Invert the address/network/IID match ("everything except").</summary>
    public bool MatchOpposite { get; init; }

    /// <summary>Invert the port match.</summary>
    public bool MatchOppositePorts { get; init; }

    public bool Equals(FirewallEndpoint? other) =>
        other is not null && Zone == other.Zone && Ipv6InterfaceId == other.Ipv6InterfaceId
        && MatchOpposite == other.MatchOpposite && MatchOppositePorts == other.MatchOppositePorts
        && SetEq(Addresses, other.Addresses) && SetEq(Networks, other.Networks) && SetEq(Ports, other.Ports);

    public override int GetHashCode() => HashCode.Combine(Zone, Ipv6InterfaceId, Addresses.Count, Ports.Count);

    internal static bool SetEq(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(b.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Desired state of a zone-based firewall policy (UniFi Network 9+), in the subset of the
/// integration API's model this library can round-trip. Equality is semantic (list order and
/// case don't matter), so <c>live == desired</c> is the drift check.
/// </summary>
public sealed record FirewallPolicySpec
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool Enabled { get; init; } = true;
    public FirewallAction Action { get; init; } = FirewallAction.Allow;

    /// <summary>Allow only: also create the mirrored return-traffic policy (the UI default).</summary>
    public bool AllowReturnTraffic { get; init; } = true;

    public required FirewallEndpoint Source { get; init; }
    public required FirewallEndpoint Destination { get; init; }
    public FirewallIpVersion IpVersion { get; init; } = FirewallIpVersion.Both;

    /// <summary>"all" (no filter), "tcp_udp" (preset), or a named protocol such as "tcp", "udp", "gre".</summary>
    public string Protocol { get; init; } = "all";

    public bool Logging { get; init; }
}
