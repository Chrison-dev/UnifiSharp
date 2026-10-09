using System.Text.Json.Nodes;

namespace UnifiSharp.Firewall;

/// <summary>Name ↔ id lookups the mapper needs. Built from the zone and network listings.</summary>
public sealed record FirewallNames(
    IReadOnlyDictionary<string, Guid> ZoneIds,
    IReadOnlyDictionary<string, Guid> NetworkIds)
{
    internal string ZoneName(Guid id) => ZoneIds.FirstOrDefault(kv => kv.Value == id).Key ?? id.ToString();
    internal string NetworkName(Guid id) => NetworkIds.FirstOrDefault(kv => kv.Value == id).Key ?? id.ToString();

    internal Guid ZoneId(string name) => ZoneIds.TryGetValue(name, out var id) ? id
        : throw new ArgumentException($"unknown firewall zone '{name}' (known: {string.Join(", ", ZoneIds.Keys)})");

    internal Guid NetworkId(string name) => NetworkIds.TryGetValue(name, out var id) ? id
        : throw new ArgumentException($"unknown network '{name}' (known: {string.Join(", ", NetworkIds.Keys)})");
}

/// <summary>
/// Maps <see cref="FirewallPolicySpec"/> to and from the integration API's JSON
/// (<c>/v1/sites/{site}/firewall/policies</c>). Hand-written rather than via the generated
/// Kiota models: those fan out into a separate class per IP version × protocol style × filter
/// kind, and a round-trippable subset is far clearer as the wire shape itself.
/// </summary>
public static class FirewallPolicyJson
{
    // The interface-identifier mask: the low 64 bits are the IID.
    internal const string IidMask = "::ffff:ffff:ffff:ffff";

    public static JsonObject ToJson(FirewallPolicySpec p, FirewallNames names)
    {
        var action = new JsonObject { ["type"] = p.Action.ToString().ToUpperInvariant() };
        if (p.Action == FirewallAction.Allow) action["allowReturnTraffic"] = p.AllowReturnTraffic;

        var scope = new JsonObject
        {
            ["ipVersion"] = p.IpVersion switch
            {
                FirewallIpVersion.IPv4 => "IPV4",
                FirewallIpVersion.IPv6 => "IPV6",
                _ => "IPV4_AND_IPV6",
            },
        };
        var proto = p.Protocol.Trim().ToLowerInvariant();
        if (proto == "tcp_udp")
            scope["protocolFilter"] = new JsonObject { ["type"] = "PRESET", ["preset"] = new JsonObject { ["name"] = "TCP_UDP" } };
        else if (proto != "all")
            scope["protocolFilter"] = new JsonObject
            {
                ["type"] = "NAMED_PROTOCOL",
                ["protocol"] = new JsonObject { ["name"] = proto.ToUpperInvariant() },
                ["matchOpposite"] = false,
            };

        var o = new JsonObject
        {
            ["enabled"] = p.Enabled,
            ["name"] = p.Name,
            ["action"] = action,
            ["source"] = Endpoint(p.Source, names),
            ["destination"] = Endpoint(p.Destination, names),
            ["ipProtocolScope"] = scope,
            ["loggingEnabled"] = p.Logging,
        };
        if (!string.IsNullOrEmpty(p.Description)) o["description"] = p.Description;
        return o;
    }

    private static JsonObject Endpoint(FirewallEndpoint e, FirewallNames names)
    {
        var o = new JsonObject { ["zoneId"] = names.ZoneId(e.Zone).ToString() };
        var narrowings = (e.Addresses.Count > 0 ? 1 : 0) + (e.Networks.Count > 0 ? 1 : 0) + (e.Ipv6InterfaceId is null ? 0 : 1);
        if (narrowings > 1)
            throw new ArgumentException($"zone '{e.Zone}': pick ONE of addresses, networks or ipv6InterfaceId");

        JsonObject? f = null;
        if (e.Addresses.Count > 0)
            f = new JsonObject
            {
                ["type"] = "IP_ADDRESS",
                ["ipAddressFilter"] = new JsonObject
                {
                    ["type"] = "IP_ADDRESSES",
                    ["matchOpposite"] = e.MatchOpposite,
                    ["items"] = new JsonArray(e.Addresses.Select(IpItem).ToArray<JsonNode?>()),
                },
            };
        else if (e.Networks.Count > 0)
            f = new JsonObject
            {
                ["type"] = "NETWORK",
                ["networkFilter"] = new JsonObject
                {
                    ["networkIds"] = new JsonArray(e.Networks.Select(n => (JsonNode)names.NetworkId(n).ToString()).ToArray<JsonNode?>()),
                    ["matchOpposite"] = e.MatchOpposite,
                },
            };
        else if (e.Ipv6InterfaceId is { } iid)
            f = new JsonObject
            {
                ["type"] = "IPV6_IID",
                ["ipv6IidFilter"] = new JsonObject { ["ipv6Iid"] = $"{iid}/{IidMask}", ["matchOpposite"] = e.MatchOpposite },
            };

        if (e.Ports.Count > 0)
        {
            var ports = new JsonObject
            {
                ["type"] = "PORTS",
                ["matchOpposite"] = e.MatchOppositePorts,
                ["items"] = new JsonArray(e.Ports.Select(PortItem).ToArray<JsonNode?>()),
            };
            f ??= new JsonObject { ["type"] = "PORT" };
            f["portFilter"] = ports;
        }
        if (f is not null) o["trafficFilter"] = f;
        return o;
    }

    private static JsonNode IpItem(string a)
    {
        a = a.Trim();
        if (a.Contains('/')) return new JsonObject { ["type"] = "SUBNET", ["value"] = a };
        var dash = a.IndexOf('-');
        if (dash > 0) return new JsonObject { ["type"] = "IP_ADDRESS_RANGE", ["start"] = a[..dash].Trim(), ["stop"] = a[(dash + 1)..].Trim() };
        return new JsonObject { ["type"] = "IP_ADDRESS", ["value"] = a };
    }

    private static JsonNode PortItem(string p)
    {
        p = p.Trim();
        var dash = p.IndexOf('-');
        if (dash > 0)
            return new JsonObject { ["type"] = "PORT_NUMBER_RANGE", ["start"] = int.Parse(p[..dash]), ["stop"] = int.Parse(p[(dash + 1)..]) };
        return new JsonObject { ["type"] = "PORT_NUMBER", ["value"] = int.Parse(p) };
    }

    /// <summary>
    /// Read a live policy back into a spec. Returns <c>Spec = null</c> with a reason when the
    /// policy uses something outside the supported subset (domain/app/region filters, schedules,
    /// connection-state or IPsec filters, protocol numbers…), so a caller never mistakes
    /// "can't compare" for "matches".
    /// </summary>
    public static (FirewallPolicySpec? Spec, string? Unsupported) FromJson(JsonObject live, FirewallNames names)
    {
        foreach (var k in new[] { "schedule", "connectionStateFilter", "ipsecFilter" })
            if (live[k] is { } n && n.GetValueKind() is not System.Text.Json.JsonValueKind.Null
                && !(n is JsonArray { Count: 0 }) && !(k == "schedule" && n["mode"]?.GetValue<string>() == "ALWAYS"))
                return (null, $"uses {k}");

        var actionType = live["action"]?["type"]?.GetValue<string>() ?? "";
        if (!Enum.TryParse<FirewallAction>(actionType, ignoreCase: true, out var action)) return (null, $"action {actionType}");

        var scope = live["ipProtocolScope"];
        var ipVersion = scope?["ipVersion"]?.GetValue<string>() switch
        {
            "IPV4" => FirewallIpVersion.IPv4,
            "IPV6" => FirewallIpVersion.IPv6,
            _ => FirewallIpVersion.Both,
        };
        var protocol = "all";
        if (scope?["protocolFilter"] is JsonObject pf)
        {
            switch (pf["type"]?.GetValue<string>())
            {
                case "PRESET": protocol = (pf["preset"]?["name"]?.GetValue<string>() ?? "").ToLowerInvariant(); break;
                case "NAMED_PROTOCOL" when pf["matchOpposite"]?.GetValue<bool>() != true:
                    protocol = (pf["protocol"]?["name"]?.GetValue<string>() ?? "").ToLowerInvariant(); break;
                default: return (null, $"protocol filter {pf["type"]}");
            }
        }

        var (src, srcWhy) = ReadEndpoint(live["source"] as JsonObject, names);
        if (src is null) return (null, $"source {srcWhy}");
        var (dst, dstWhy) = ReadEndpoint(live["destination"] as JsonObject, names);
        if (dst is null) return (null, $"destination {dstWhy}");

        var desc = live["description"]?.GetValue<string>();
        return (new FirewallPolicySpec
        {
            Name = live["name"]?.GetValue<string>() ?? "",
            Description = string.IsNullOrEmpty(desc) ? null : desc,
            Enabled = live["enabled"]?.GetValue<bool>() ?? true,
            Action = action,
            AllowReturnTraffic = action != FirewallAction.Allow || (live["action"]?["allowReturnTraffic"]?.GetValue<bool>() ?? false),
            Source = src,
            Destination = dst,
            IpVersion = ipVersion,
            Protocol = protocol,
            Logging = live["loggingEnabled"]?.GetValue<bool>() ?? false,
        }, null);
    }

    private static (FirewallEndpoint? E, string? Why) ReadEndpoint(JsonObject? o, FirewallNames names)
    {
        if (o?["zoneId"]?.GetValue<string>() is not { } zid || !Guid.TryParse(zid, out var zoneId)) return (null, "has no zone");
        var e = new FirewallEndpoint { Zone = names.ZoneName(zoneId) };
        if (o["trafficFilter"] is not JsonObject f) return (e, null);

        IReadOnlyList<string> ports = [];
        var oppPorts = false;
        if (f["portFilter"] is JsonObject pf)
        {
            if (pf["type"]?.GetValue<string>() != "PORTS") return (null, "uses a port matching list");
            oppPorts = pf["matchOpposite"]?.GetValue<bool>() ?? false;
            ports = (pf["items"] as JsonArray ?? []).Select(i => i!["type"]?.GetValue<string>() == "PORT_NUMBER_RANGE"
                ? $"{i["start"]}-{i["stop"]}" : $"{i["value"]}").ToList();
        }
        if (f["macAddressFilter"] is { } mac && mac.GetValueKind() != System.Text.Json.JsonValueKind.Null) return (null, "uses a MAC filter");

        switch (f["type"]?.GetValue<string>())
        {
            case "PORT":
                return (e with { Ports = ports, MatchOppositePorts = oppPorts }, null);
            case "IP_ADDRESS":
                var af = f["ipAddressFilter"];
                if (af?["type"]?.GetValue<string>() != "IP_ADDRESSES") return (null, "uses an IP matching list");
                var addrs = (af["items"] as JsonArray ?? []).Select(i => i!["type"]?.GetValue<string>() switch
                {
                    "IP_ADDRESS_RANGE" => $"{i["start"]}-{i["stop"]}",
                    _ => i["value"]?.GetValue<string>() ?? "",
                }).ToList();
                return (e with { Addresses = addrs, MatchOpposite = af["matchOpposite"]?.GetValue<bool>() ?? false, Ports = ports, MatchOppositePorts = oppPorts }, null);
            case "NETWORK":
                var nf = f["networkFilter"];
                var nets = (nf?["networkIds"] as JsonArray ?? []).Select(n => Guid.TryParse(n?.GetValue<string>(), out var g) ? names.NetworkName(g) : "").ToList();
                return (e with { Networks = nets, MatchOpposite = nf?["matchOpposite"]?.GetValue<bool>() ?? false, Ports = ports, MatchOppositePorts = oppPorts }, null);
            case "IPV6_IID":
                var raw = f["ipv6IidFilter"]?["ipv6Iid"]?.GetValue<string>() ?? "";
                var slash = raw.IndexOf('/');
                if (slash >= 0 && raw[(slash + 1)..] != IidMask) return (null, $"uses a non-/64 IID mask ({raw})");
                return (e with
                {
                    Ipv6InterfaceId = slash >= 0 ? raw[..slash] : raw,
                    MatchOpposite = f["ipv6IidFilter"]?["matchOpposite"]?.GetValue<bool>() ?? false,
                    Ports = ports, MatchOppositePorts = oppPorts,
                }, null);
            default:
                return (null, $"uses a {f["type"]} filter");
        }
    }
}
