using System.Text.Json.Nodes;

namespace UnifiSharp.Legacy;

/// <summary>
/// Maps <see cref="UnifiNatRuleSpec"/> to and from the v2 <c>nat</c> JSON. The body is always
/// complete because a PUT replaces the whole rule.
/// </summary>
public static class UnifiNatRuleJson
{
    public static JsonObject ToJson(UnifiNatRuleSpec s, UnifiNatNames names)
    {
        var body = new JsonObject
        {
            ["type"] = s.Type switch { UnifiNatType.Dnat => "DNAT", UnifiNatType.Masquerade => "MASQUERADE", _ => "SNAT" },
            ["description"] = s.Name,
            ["enabled"] = s.Enabled,
            ["ip_version"] = "IPV4",
            ["protocol"] = s.Protocol,
            ["source_filter"] = FilterJson(s.Source),
            ["destination_filter"] = FilterJson(s.Destination),
            ["logging"] = s.Logging,
            ["exclude"] = false,
            ["is_predefined"] = false,
            ["pppoe_use_base_interface"] = false,
            ["setting_preference"] = "manual",
        };
        if (s.InNetwork is not null) body["in_interface"] = names.NetworkId(s.InNetwork);
        if (s.OutNetwork is not null) body["out_interface"] = names.NetworkId(s.OutNetwork);
        if (s.TranslatedAddress is not null) body["ip_address"] = s.TranslatedAddress;
        if (s.TranslatedPort is not null) body["port"] = s.TranslatedPort;
        if (s.RuleIndex is { } i) body["rule_index"] = i;
        return body;
    }

    public static UnifiNatRuleSpec FromJson(JsonObject r, UnifiNatNames names)
    {
        // ToJson writes these four at fixed values, so a live rule that differs would be rewritten
        // by an update and read as "no drift". Refuse it rather than lose the difference.
        RequireDefault(r, "ip_version", "IPV4");
        RequireDefault(r, "setting_preference", "manual");
        RequireFalse(r, "exclude");
        RequireFalse(r, "pppoe_use_base_interface");
        return Map(r, names);
    }

    private static UnifiNatRuleSpec Map(JsonObject r, UnifiNatNames names) => new()
    {
        Name = Str(r, "description") ?? "",
        Type = Str(r, "type") switch
        {
            "DNAT" => UnifiNatType.Dnat,
            "MASQUERADE" => UnifiNatType.Masquerade,
            "SNAT" => UnifiNatType.Snat,
            var t => throw new ArgumentException($"unsupported NAT rule type '{t}'"),
        },
        Enabled = r["enabled"]?.GetValue<bool>() ?? true,
        Protocol = Str(r, "protocol") ?? "all",
        InNetwork = Str(r, "in_interface") is { } i ? names.NetworkName(i) : null,
        OutNetwork = Str(r, "out_interface") is { } o ? names.NetworkName(o) : null,
        TranslatedAddress = Str(r, "ip_address"),
        TranslatedPort = Str(r, "port"),
        Source = FilterFrom(r["source_filter"] as JsonObject),
        Destination = FilterFrom(r["destination_filter"] as JsonObject),
        Logging = r["logging"]?.GetValue<bool>() ?? false,
        RuleIndex = r["rule_index"] is { } n ? n.GetValue<int>() : null,
    };

    private static JsonObject FilterJson(UnifiNatFilter f)
    {
        var any = f.Address is not null || f.Port is not null;
        var o = new JsonObject
        {
            ["filter_type"] = any ? "ADDRESS_AND_PORT" : "NONE",
            ["firewall_group_ids"] = new JsonArray(),
            ["invert_address"] = f.InvertAddress,
            ["invert_port"] = f.InvertPort,
        };
        if (f.Address is not null) o["address"] = f.Address;
        if (f.Port is not null) o["port"] = f.Port;
        return o;
    }

    private static UnifiNatFilter FilterFrom(JsonObject? f)
    {
        if (f is null) return new();
        // Only ADDRESS_AND_PORT is modelled; a filter on firewall groups or networks is not one
        // this library can write back, so refuse it rather than report it as "no filter".
        return Str(f, "filter_type") switch
        {
            "NONE" or null => new(),
            "ADDRESS_AND_PORT" => new()
            {
                Address = NullIfEmpty(Str(f, "address")),
                Port = NullIfEmpty(Str(f, "port")),
                InvertAddress = f["invert_address"]?.GetValue<bool>() ?? false,
                InvertPort = f["invert_port"]?.GetValue<bool>() ?? false,
            },
            var t => throw new ArgumentException($"unsupported NAT filter type '{t}'"),
        };
    }

    private static void RequireDefault(JsonObject r, string key, string expected)
    {
        if (Str(r, key) is { } v && v != expected)
            throw new ArgumentException($"unsupported NAT rule {key} '{v}' (this library writes '{expected}')");
    }

    private static void RequireFalse(JsonObject r, string key)
    {
        if (r[key]?.GetValue<bool>() == true)
            throw new ArgumentException($"unsupported NAT rule: {key} is true (this library writes false)");
    }

    private static string? Str(JsonObject o, string key) => o[key]?.GetValue<string>();
    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
