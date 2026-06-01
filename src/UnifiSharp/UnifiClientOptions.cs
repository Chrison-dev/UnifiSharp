namespace UnifiSharp;

/// <summary>
/// Connection options for a UniFi Network controller (UniFi OS console).
/// Authenticates with a local API key (Settings → Integrations) sent as the
/// <c>X-API-KEY</c> header. A <b>read-only</b> key is recommended.
/// </summary>
public sealed record UnifiClientOptions
{
    /// <summary>
    /// Base URL of the Network integration API — <b>without</b> the version
    /// segment (the generated client appends <c>/v1/…</c> itself). The path
    /// differs by deployment:
    /// <list type="bullet">
    /// <item>UniFi OS (Cloud Gateway / UDM / UniFi OS Server): <c>https://&lt;host&gt;/proxy/network/integration</c></item>
    /// <item>Standalone Network Application: <c>https://&lt;host&gt;:8443/integration</c> (no <c>/proxy/network</c> prefix)</item>
    /// </list>
    /// Verified against the <c>.containers/unifi</c> UniFi OS Server container.
    /// </summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>The local API key (Settings → Integrations on the console).</summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Verify the console's TLS certificate. UniFi OS consoles commonly use a
    /// self-signed cert on the LAN, so this can be turned off — defaults to on.
    /// </summary>
    public bool VerifyTls { get; init; } = true;

    /// <summary>
    /// Build options from <c>UNIFI_BASE_URL</c> / <c>UNIFI_API_KEY</c> /
    /// <c>UNIFI_VERIFY_TLS</c> environment variables; null if base URL or key is missing.
    /// </summary>
    public static UnifiClientOptions? TryFromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable("UNIFI_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("UNIFI_API_KEY");
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var verifyTls = !string.Equals(
            Environment.GetEnvironmentVariable("UNIFI_VERIFY_TLS"), "false", StringComparison.OrdinalIgnoreCase);

        return new UnifiClientOptions { BaseUrl = new Uri(baseUrl), ApiKey = apiKey, VerifyTls = verifyTls };
    }

    /// <summary>
    /// Redacted representation. The synthesized record <c>ToString()</c> would
    /// otherwise print <see cref="ApiKey"/>, leaking it into any log or
    /// interpolated string. The key is never emitted.
    /// </summary>
    public override string ToString() =>
        $"UnifiClientOptions {{ BaseUrl = {BaseUrl}, ApiKey = ***, VerifyTls = {VerifyTls} }}";
}
