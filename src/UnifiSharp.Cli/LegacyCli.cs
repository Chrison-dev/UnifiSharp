using System.Text.Json;
using UnifiSharp.Legacy;

// Legacy controller write commands for the CLI. Isolated here so the one
// CS0618 suppression (the legacy client is intentionally [Obsolete]) doesn't
// spread across Program.cs.
#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

internal static class LegacyCli
{
    private static readonly string[] Resources = ["portforward", "firewallgroup", "networkconf"];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine(
                "Usage: unifisharp legacy <list|create|delete> <portforward|firewallgroup|networkconf> [id] [--json '<body>']");
            return args.Length == 0 ? 2 : 0;
        }

        var verb = args[0].ToLowerInvariant();
        var resource = args[1].ToLowerInvariant();
        if (!Resources.Contains(resource))
        {
            Console.Error.WriteLine($"Unknown resource '{resource}'. Expected: {string.Join(" | ", Resources)}");
            return 1;
        }

        var options = UnifiLegacyOptions.TryFromEnvironment();
        if (options is null)
        {
            Console.Error.WriteLine(
                "Missing legacy config. Set UNIFI_LEGACY_BASE_URL, UNIFI_USERNAME, UNIFI_PASSWORD (UNIFI_VERIFY_TLS=false for self-signed).");
            return 2;
        }

        using var client = new UnifiLegacyClient(options);

        switch (verb)
        {
            case "list":
                Dump(await ListAsync(client, resource));
                return 0;

            case "create":
                var body = JsonArg(args);
                if (body is null)
                {
                    Console.Error.WriteLine("create requires --json '<body>'.");
                    return 2;
                }
                Dump(await CreateAsync(client, resource, body));
                return 0;

            case "delete":
                var id = args.ElementAtOrDefault(2);
                if (string.IsNullOrEmpty(id) || id.StartsWith('-'))
                {
                    Console.Error.WriteLine("delete requires an <id>.");
                    return 2;
                }
                await DeleteAsync(client, resource, id);
                Console.WriteLine($"deleted {resource}/{id}");
                return 0;

            default:
                Console.Error.WriteLine($"Unknown verb '{verb}'. Expected: list | create | delete");
                return 1;
        }
    }

    private static async Task<object> ListAsync(UnifiLegacyClient c, string resource) => resource switch
    {
        "portforward" => await c.ListPortForwardsAsync(),
        "firewallgroup" => await c.ListFirewallGroupsAsync(),
        _ => await c.ListNetworksAsync(),
    };

    private static async Task<object> CreateAsync(UnifiLegacyClient c, string resource, string json) => resource switch
    {
        "portforward" => await c.CreatePortForwardAsync(Parse<UnifiPortForward>(json)),
        "firewallgroup" => await c.CreateFirewallGroupAsync(Parse<UnifiFirewallGroup>(json)),
        _ => await c.CreateNetworkAsync(Parse<UnifiNetwork>(json)),
    };

    private static Task DeleteAsync(UnifiLegacyClient c, string resource, string id) => resource switch
    {
        "portforward" => c.DeletePortForwardAsync(id),
        "firewallgroup" => c.DeleteFirewallGroupAsync(id),
        _ => c.DeleteNetworkAsync(id),
    };

    private static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json) ?? throw new ArgumentException("--json did not parse to an object");

    private static string? JsonArg(string[] args)
    {
        var i = Array.IndexOf(args, "--json");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Dump(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
}
