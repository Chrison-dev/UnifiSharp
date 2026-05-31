namespace UnifiSharp;

/// <summary>
/// Connection options for a UniFi Network controller (UniFi OS console).
/// Authenticates with a local API key (Settings → Integrations) sent as the
/// <c>X-API-KEY</c> header. A <b>read-only</b> key is recommended.
/// </summary>
public sealed record UnifiClientOptions
{
    /// <summary>
    /// Base URL of the Network integration API, e.g.
    /// <c>https://192.168.1.1/proxy/network/integration/v1</c>.
    /// </summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>The local API key (Settings → Integrations on the console).</summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Verify the console's TLS certificate. UniFi OS consoles commonly use a
    /// self-signed cert on the LAN, so this can be turned off — defaults to on.
    /// </summary>
    public bool VerifyTls { get; init; } = true;
}
