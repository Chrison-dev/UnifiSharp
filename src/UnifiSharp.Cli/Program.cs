using System.Text.Json;
using UnifiSharp;

// unifisharp — a thin read-only CLI over the UnifiSharp library.
//
// Commands: sites | discover
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
          discover   Dump a UnifiSnapshot (sites + device/client/network counts) as JSON

        Config (env): UNIFI_BASE_URL (…/proxy/network/integration/v1), UNIFI_API_KEY,
                      UNIFI_VERIFY_TLS (optional, 'false' for self-signed)
        """);
    return 0;
}

var options = UnifiClientOptions.TryFromEnvironment();
if (options is null)
{
    Console.Error.WriteLine("Missing config. Set UNIFI_BASE_URL and UNIFI_API_KEY (UNIFI_VERIFY_TLS=false for self-signed).");
    return 2;
}

var client = UnifiApi.Create(options);

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
        var snapshot = await new UnifiDiscovery(client).DiscoverAsync();
        Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Try: sites | discover");
        return 1;
}
