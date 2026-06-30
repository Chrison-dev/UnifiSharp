using System.Text.Json;
using UnifiSharp;

// unifisharp — a thin read-only CLI over the UnifiSharp library.
//
// Commands: sites | discover | devices | clients | networks | firewall | wlans
// Config (env): UNIFI_BASE_URL (…/proxy/network/integration/v1), UNIFI_API_KEY,
//               UNIFI_VERIFY_TLS (optional, 'false' for self-signed consoles)

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine(
        """
        unifisharp — read-only UniFi Network client

        Usage: unifisharp <command>
          sites      List sites (id + name)
          discover   Dump a UnifiSnapshot (sites + networks/WLANs/firewall/devices/clients) as JSON
          devices    List adopted devices per site (name, model, ip, mac, firmware, state) as JSON
          clients    List connected clients per site (name, type, ip, connectedAt) as JSON
          networks   List networks/VLANs per site (name, vlan id, purpose, enabled) as JSON
          firewall   List firewall zones, policies, and ACL rules per site as JSON
          wlans      List WLANs/SSIDs per site (ssid, enabled, security) as JSON

          legacy <list|create|delete> <resource> [id] [--json '<body>']
                     Legacy controller write API (port-forwards, firewall, VLANs).
                     resource: portforward | firewallgroup | networkconf
                     e.g. unifisharp legacy create portforward --json '{"name":"x","enabled":true,
                          "pfwd_interface":"wan","src":"any","dst_port":"443","fwd":"10.10.0.13",
                          "fwd_port":"443","proto":"tcp"}'

        Config (env): UNIFI_BASE_URL (…/proxy/network/integration/v1), UNIFI_API_KEY,
                      UNIFI_VERIFY_TLS (optional, 'false' for self-signed)
        Legacy config (env): UNIFI_LEGACY_BASE_URL (…/proxy/network/api/s/default),
                      UNIFI_USERNAME, UNIFI_PASSWORD, UNIFI_VERIFY_TLS
        """);
    return 0;
}

// The legacy write commands authenticate by session (user/pass), not X-API-KEY,
// and dispatch before the integration-client setup below.
if (command == "legacy")
{
    return await LegacyCli.RunAsync(args[1..]);
}

var options = UnifiClientOptions.TryFromEnvironment();
if (options is null)
{
    Console.Error.WriteLine("Missing config. Set UNIFI_BASE_URL and UNIFI_API_KEY (UNIFI_VERIFY_TLS=false for self-signed).");
    return 2;
}

var client = UnifiApi.Create(options);

var json = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

void Dump(object value) => Console.WriteLine(JsonSerializer.Serialize(value, json));

switch (command)
{
    case "sites":
        var sites = (await client.V1.Sites.GetAsync())?.Data ?? [];
        foreach (var site in sites)
        {
            Console.WriteLine($"{site.Id}  {site.Name}");
        }
        return 0;

    case "discover":
        Dump(await new UnifiDiscovery(client).DiscoverAsync());
        return 0;

    case "devices":
        Dump((await new UnifiDiscovery(client).DiscoverAsync())
            .Sites.Select(s => new { s.Site, s.Id, s.Devices }));
        return 0;

    case "clients":
        Dump((await new UnifiDiscovery(client).DiscoverAsync())
            .Sites.Select(s => new { s.Site, s.Id, s.Clients }));
        return 0;

    case "networks":
        Dump((await new UnifiDiscovery(client).DiscoverAsync())
            .Sites.Select(s => new { s.Site, s.Id, s.Networks }));
        return 0;

    case "firewall":
        Dump((await new UnifiDiscovery(client).DiscoverAsync())
            .Sites.Select(s => new { s.Site, s.Id, s.Firewall }));
        return 0;

    case "wlans":
        Dump((await new UnifiDiscovery(client).DiscoverAsync())
            .Sites.Select(s => new { s.Site, s.Id, s.Wlans }));
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Try: sites | discover | devices | clients | networks | firewall | wlans");
        return 1;
}
