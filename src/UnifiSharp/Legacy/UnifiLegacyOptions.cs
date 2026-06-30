namespace UnifiSharp.Legacy;

/// <summary>
/// Connection options for the <b>legacy</b> UniFi controller API
/// (<c>/proxy/network/api/s/&lt;site&gt;/rest/…</c>). Unlike the official
/// integration API (X-API-KEY), the legacy API uses the classic controller
/// <b>session</b>: <c>POST /api/auth/login</c> with a username + password yields
/// a <c>TOKEN</c> cookie + <c>X-CSRF-Token</c>, reused on subsequent calls.
/// <para>This is intentionally session-based: it works against the
/// <c>.containers/unifi</c> UniFi OS Server test container (which exposes no
/// scriptable API-key mint) and any UniFi OS gateway. See ADR-0003.</para>
/// </summary>
public sealed record UnifiLegacyOptions
{
    /// <summary>
    /// Base URL of the legacy site API, e.g.
    /// <c>https://&lt;host&gt;/proxy/network/api/s/default</c>. The controller root
    /// (used for <c>/api/auth/login</c>) is derived from this URL's authority.
    /// </summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>Local admin username for the session login.</summary>
    public required string Username { get; init; }

    /// <summary>Local admin password for the session login.</summary>
    public required string Password { get; init; }

    /// <summary>
    /// Verify the console's TLS certificate. UniFi OS consoles commonly use a
    /// self-signed cert on the LAN, so this can be turned off — defaults to on.
    /// </summary>
    public bool VerifyTls { get; init; } = true;

    /// <summary>The controller root (scheme + authority), where <c>/api/auth/login</c> lives.</summary>
    public Uri ControllerRoot => new(BaseUrl.GetLeftPart(UriPartial.Authority));

    /// <summary>
    /// Build options from <c>UNIFI_LEGACY_BASE_URL</c> / <c>UNIFI_USERNAME</c> /
    /// <c>UNIFI_PASSWORD</c> / <c>UNIFI_VERIFY_TLS</c>; null if any required value is missing.
    /// Matches the env emitted by <c>.containers/unifi/bootstrap.sh</c>.
    /// </summary>
    public static UnifiLegacyOptions? TryFromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable("UNIFI_LEGACY_BASE_URL");
        var username = Environment.GetEnvironmentVariable("UNIFI_USERNAME");
        var password = Environment.GetEnvironmentVariable("UNIFI_PASSWORD");
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var verifyTls = !string.Equals(
            Environment.GetEnvironmentVariable("UNIFI_VERIFY_TLS"), "false", StringComparison.OrdinalIgnoreCase);

        return new UnifiLegacyOptions
        {
            BaseUrl = new Uri(baseUrl),
            Username = username,
            Password = password,
            VerifyTls = verifyTls,
        };
    }

    /// <summary>Redacted representation — never emits the password.</summary>
    public override string ToString() =>
        $"UnifiLegacyOptions {{ BaseUrl = {BaseUrl}, Username = {Username}, Password = ***, VerifyTls = {VerifyTls} }}";
}
