using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiSharp.Legacy;

/// <summary>
/// The DHCP DNS servers a <c>rest/networkconf</c> object hands out, kept as API knowledge
/// in one place: <c>dhcpd_dns_enabled</c> plus four fixed slots <c>dhcpd_dns_1..4</c>.
/// Works on the raw object from <see cref="UnifiLegacyClient.GetNetworkRawAsync"/> so a
/// change touches these keys and nothing else.
/// </summary>
public static class NetworkDhcpDns
{
    public const int MaxServers = 4;

    private const string EnabledKey = "dhcpd_dns_enabled";

    private static string SlotKey(int slot) => $"dhcpd_dns_{slot}";

    /// <summary>
    /// The servers handed out, slots 1..4 in order with blanks dropped; or <c>null</c> when
    /// <c>dhcpd_dns_enabled</c> is false or absent ("auto": the gateway hands out itself, and
    /// any leftover slot values are not live). Throws <see cref="UnifiLegacyException"/> when a
    /// key is present but not the shape this reads — unknown is reported, never guessed.
    /// </summary>
    public static IReadOnlyList<string>? Read(JsonObject network)
    {
        ArgumentNullException.ThrowIfNull(network);

        // Absent is treated as false: without the flag the controller does not apply the
        // slots, so "auto" is the live behaviour. (Present-but-not-a-bool throws below.)
        if (!network.TryGetPropertyValue(EnabledKey, out var enabled))
        {
            return null;
        }

        if (enabled?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new UnifiLegacyException($"{EnabledKey} is {Describe(enabled)}, expected a boolean");
        }

        if (!enabled.GetValue<bool>())
        {
            return null;
        }

        var servers = new List<string>(MaxServers);
        for (var slot = 1; slot <= MaxServers; slot++)
        {
            // An absent slot carries no server, the same as a blank one; With always writes all four.
            if (!network.TryGetPropertyValue(SlotKey(slot), out var value))
            {
                continue;
            }

            if (value?.GetValueKind() is not JsonValueKind.String)
            {
                throw new UnifiLegacyException($"{SlotKey(slot)} is {Describe(value)}, expected a string");
            }

            var server = value.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(server))
            {
                servers.Add(server);
            }
        }

        // Enabled with every slot blank comes back as an empty list, not null: it is a
        // different live state from "auto", and the caller should see it as such.
        return servers;
    }

    /// <summary>
    /// A deep clone of <paramref name="network"/> with <c>dhcpd_dns_enabled</c> true, slots
    /// 1..n set to <paramref name="servers"/> in order and the remaining slots blanked to
    /// <c>""</c> (blanked, not removed: the PUT is a full replacement and an absent key is not
    /// a cleared one). No other key is touched and the input is never mutated.
    /// </summary>
    /// <exception cref="ArgumentException">0 or more than <see cref="MaxServers"/> servers, or a blank entry.</exception>
    public static JsonObject With(JsonObject network, IReadOnlyList<string> servers)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(servers);

        // Zero would mean "clear DNS", which no declaration should do by accident; reverting to
        // auto is a separate, explicit operation this helper does not offer.
        if (servers.Count is 0 or > MaxServers)
        {
            throw new ArgumentException(
                $"A network hands out 1 to {MaxServers} DHCP DNS servers; got {servers.Count}.", nameof(servers));
        }

        if (servers.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A DHCP DNS server cannot be blank.", nameof(servers));
        }

        var copy = network.DeepClone().AsObject();
        copy[EnabledKey] = true;
        for (var slot = 1; slot <= MaxServers; slot++)
        {
            copy[SlotKey(slot)] = slot <= servers.Count ? servers[slot - 1] : "";
        }

        return copy;
    }

    private static string Describe(JsonNode? node) =>
        node is null ? "null" : $"{node.GetValueKind()} {node.ToJsonString()}";
}
