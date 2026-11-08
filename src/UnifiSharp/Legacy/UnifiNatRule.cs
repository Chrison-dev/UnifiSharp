using System.Text.Json.Nodes;

namespace UnifiSharp.Legacy;

public enum UnifiNatType { Dnat, Masquerade, Snat }

/// <summary>
/// One side of a NAT match: a single IP, a range "a-b" or a CIDR, optionally with a port, and
/// optionally inverted ("everything except"). No address and no port means no filter.
/// </summary>
public sealed record UnifiNatFilter
{
    /// <summary>A single IP, a range written "first-last", or a CIDR. Null = any.</summary>
    public string? Address { get; init; }

    /// <summary>A port or range, e.g. "53". Null = any.</summary>
    public string? Port { get; init; }

    public bool InvertAddress { get; init; }
    public bool InvertPort { get; init; }
}

/// <summary>
/// Desired state of a custom NAT rule on the gateway (v2 site API: <c>nat</c>). Rules have no
/// name of their own, so <see cref="Name"/> is the controller's <c>description</c> and is what
/// callers reconcile by. Record equality is the drift check; leave <see cref="RuleIndex"/> null
/// on both sides when the order is not declared.
/// </summary>
public sealed record UnifiNatRuleSpec
{
    /// <summary>Written to <c>description</c>; the key a caller matches live rules by.</summary>
    public required string Name { get; init; }
    public required UnifiNatType Type { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>"all", "tcp_udp", "tcp" or "udp".</summary>
    public string Protocol { get; init; } = "all";

    /// <summary>Network NAME the traffic arrives on (<c>in_interface</c>); DNAT and SNAT.</summary>
    public string? InNetwork { get; init; }

    /// <summary>Network NAME the traffic leaves on (<c>out_interface</c>); MASQUERADE.</summary>
    public string? OutNetwork { get; init; }

    /// <summary>The address traffic is translated to (<c>ip_address</c>); DNAT and SNAT.</summary>
    public string? TranslatedAddress { get; init; }

    /// <summary>The port traffic is translated to (<c>port</c>); DNAT and SNAT.</summary>
    public string? TranslatedPort { get; init; }

    public UnifiNatFilter Source { get; init; } = new();
    public UnifiNatFilter Destination { get; init; } = new();
    public bool Logging { get; init; }

    /// <summary>
    /// Position among NAT rules; the controller rejects a duplicate. Null = next free on create,
    /// and the live rule's own index on update.
    /// </summary>
    public int? RuleIndex { get; init; }
}

/// <summary>A NAT rule as the controller holds it. Predefined rules are the gateway's own.</summary>
public sealed record UnifiLiveNatRule(string Id, string Name, bool IsPredefined, JsonObject Raw);

/// <summary>Network NAME to <c>_id</c> lookups for <c>in_interface</c> / <c>out_interface</c>.</summary>
public sealed record UnifiNatNames(IReadOnlyDictionary<string, string> NetworkIds)
{
    // An id passes through: NetworkName hands back the raw id for a network it doesn't know by name.
    internal string NetworkId(string name) => NetworkIds.TryGetValue(name, out var id) ? id
        : NetworkIds.Values.Contains(name) ? name
        : throw new ArgumentException($"unknown network '{name}' (known: {string.Join(", ", NetworkIds.Keys)})");

    internal string NetworkName(string id) => NetworkIds.FirstOrDefault(kv => kv.Value == id).Key ?? id;
}
