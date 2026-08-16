namespace UnifiSharp.Legacy;

/// <summary>
/// Connection options for the <b>legacy</b> UniFi controller API
/// (<c>/proxy/network/api/s/&lt;site&gt;/rest/…</c>), in one of two auth modes:
/// <list type="bullet">
///   <item><b>API key</b> — set <see cref="ApiKey"/>; every request carries
///   <c>X-API-KEY</c> and no login round-trip happens. A UniFi OS gateway accepts
///   the same key the official integration API uses, so one credential covers both.</item>
///   <item><b>Session</b> — set <see cref="Username"/> + <see cref="Password"/>;
///   <c>POST /api/auth/login</c> yields a <c>TOKEN</c> cookie + <c>X-CSRF-Token</c>,
///   reused on subsequent calls.</item>
/// </list>
/// <para>Session auth stays because the <c>.containers/unifi</c> UniFi OS Server test
/// container exposes no scriptable API-key mint, so it is the only mode that works
/// there. API-key auth is preferred against a real gateway: it needs no admin
/// username/password and survives password rotation. See ADR-0003.</para>
/// </summary>
public sealed record UnifiLegacyOptions
{
    /// <summary>
    /// Base URL of the legacy site API, e.g.
    /// <c>https://&lt;host&gt;/proxy/network/api/s/default</c>. The controller root
    /// (used for <c>/api/auth/login</c>) is derived from this URL's authority.
    /// </summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>
    /// API key for <c>X-API-KEY</c> auth. When set, <see cref="Username"/> and
    /// <see cref="Password"/> are ignored and no session login is performed.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>Local admin username for the session login. Unused in API-key mode.</summary>
    public string? Username { get; init; }

    /// <summary>Local admin password for the session login. Unused in API-key mode.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Verify the console's TLS certificate. UniFi OS consoles commonly use a
    /// self-signed cert on the LAN, so this can be turned off — defaults to on.
    /// </summary>
    public bool VerifyTls { get; init; } = true;

    /// <summary>True when <see cref="ApiKey"/> is set — no login round-trip is needed.</summary>
    public bool UsesApiKey => !string.IsNullOrEmpty(ApiKey);

    /// <summary>The controller root (scheme + authority), where <c>/api/auth/login</c> lives.</summary>
    public Uri ControllerRoot => new(BaseUrl.GetLeftPart(UriPartial.Authority));

    /// <summary>
    /// The legacy site-API URL for a host, e.g. host <c>192.168.1.1</c> →
    /// <c>https://192.168.1.1/proxy/network/api/s/default</c>.
    /// </summary>
    public static Uri SiteUrlFor(string host, string site = "default") =>
        new($"https://{host}/proxy/network/api/s/{site}");

    /// <summary>
    /// Throw unless one auth mode is fully configured. The session calls this on
    /// construction so a half-configured options object fails loudly and early
    /// rather than as an opaque 401 on the first request.
    /// </summary>
    public void Validate()
    {
        if (UsesApiKey || (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password)))
        {
            return;
        }

        throw new UnifiLegacyException(
            "UnifiLegacyOptions needs either ApiKey (X-API-KEY auth) or Username + Password (session auth).");
    }

    /// <summary>
    /// Build options from the environment, preferring API-key auth:
    /// <list type="number">
    ///   <item>Base URL from <c>UNIFI_LEGACY_BASE_URL</c>, else derived from
    ///   <c>UNIFI_LOCAL_HOST</c> as <c>https://&lt;host&gt;/proxy/network/api/s/default</c>.</item>
    ///   <item>Auth from <c>UNIFI_API_KEY</c>, else <c>UNIFI_USERNAME</c> + <c>UNIFI_PASSWORD</c>
    ///   (the pair emitted by <c>.containers/unifi/bootstrap.sh</c>).</item>
    /// </list>
    /// <c>UNIFI_VERIFY_TLS=false</c> disables cert validation. Null when there is no
    /// base URL, or no usable credential.
    /// </summary>
    public static UnifiLegacyOptions? TryFromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable("UNIFI_LEGACY_BASE_URL");
        var localHost = Environment.GetEnvironmentVariable("UNIFI_LOCAL_HOST");
        if (string.IsNullOrEmpty(baseUrl) && string.IsNullOrEmpty(localHost))
        {
            return null;
        }

        var apiKey = Environment.GetEnvironmentVariable("UNIFI_API_KEY");
        var username = Environment.GetEnvironmentVariable("UNIFI_USERNAME");
        var password = Environment.GetEnvironmentVariable("UNIFI_PASSWORD");

        var hasApiKey = !string.IsNullOrEmpty(apiKey);
        if (!hasApiKey && (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)))
        {
            return null;
        }

        var verifyTls = !string.Equals(
            Environment.GetEnvironmentVariable("UNIFI_VERIFY_TLS"), "false", StringComparison.OrdinalIgnoreCase);

        return new UnifiLegacyOptions
        {
            BaseUrl = string.IsNullOrEmpty(baseUrl) ? SiteUrlFor(localHost!) : new Uri(baseUrl),
            ApiKey = hasApiKey ? apiKey : null,
            Username = hasApiKey ? null : username,
            Password = hasApiKey ? null : password,
            VerifyTls = verifyTls,
        };
    }

    /// <summary>Redacted representation — never emits the API key or the password.</summary>
    public override string ToString() =>
        $"UnifiLegacyOptions {{ BaseUrl = {BaseUrl}, " +
        $"Auth = {(UsesApiKey ? "ApiKey ***" : $"Username = {Username}, Password = ***")}, " +
        $"VerifyTls = {VerifyTls} }}";
}
