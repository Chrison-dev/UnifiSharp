using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace UnifiSharp.Legacy;

/// <summary>
/// Holds a classic UniFi controller session for the legacy API: logs in via
/// <c>POST /api/auth/login</c> (capturing the <c>TOKEN</c> cookie + CSRF token)
/// and sends authenticated requests, attaching the CSRF token to mutating calls
/// and transparently re-authenticating once on a 401. Disposable — owns its
/// <see cref="HttpClient"/>.
/// </summary>
public sealed class UnifiLegacySession : IDisposable
{
    private readonly UnifiLegacyOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _loginUrl;
    private string? _csrfToken;
    private bool _loggedIn;

    public UnifiLegacySession(UnifiLegacyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _loginUrl = new Uri(_options.ControllerRoot, "/api/auth/login");

        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        if (!options.VerifyTls)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _http = new HttpClient(handler);
    }

    /// <summary>Ensure a valid session, logging in if needed.</summary>
    public async Task LoginAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _loginUrl)
        {
            Content = JsonContent.Create(new { username = _options.Username, password = _options.Password }),
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new UnifiLegacyException($"login failed ({(int)resp.StatusCode}): {Truncate(body)}");
        }

        CaptureCsrf(resp);
        _loggedIn = true;
    }

    /// <summary>
    /// Send a request to a path relative to the legacy site base
    /// (e.g. <c>rest/portforward</c>), returning the raw response. Attaches the
    /// CSRF token; re-logs-in once on a 401.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string relativePath, HttpContent? content, CancellationToken ct = default)
    {
        if (!_loggedIn)
        {
            await LoginAsync(ct).ConfigureAwait(false);
        }

        var resp = await SendOnceAsync(method, relativePath, content, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Session expired — re-auth once and retry.
            resp.Dispose();
            await LoginAsync(ct).ConfigureAwait(false);
            resp = await SendOnceAsync(method, relativePath, content, ct).ConfigureAwait(false);
        }

        return resp;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, string relativePath, HttpContent? content, CancellationToken ct)
    {
        // BaseUrl is the site base (…/api/s/<site>); ensure a trailing slash so the
        // relative path appends rather than replaces the last segment.
        var baseWithSlash = _options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/";
        using var req = new HttpRequestMessage(method, new Uri(new Uri(baseWithSlash), relativePath));
        if (content is not null)
        {
            req.Content = content;
        }

        if (_csrfToken is not null)
        {
            req.Headers.TryAddWithoutValidation("X-CSRF-Token", _csrfToken);
        }

        var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        CaptureCsrf(resp); // the controller rotates the token via x-updated-csrf-token
        return resp;
    }

    // UniFi returns the CSRF token on login and rotates it via x-updated-csrf-token.
    private void CaptureCsrf(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("x-updated-csrf-token", out var rotated))
        {
            _csrfToken = rotated.FirstOrDefault() ?? _csrfToken;
        }
        else if (resp.Headers.TryGetValues("x-csrf-token", out var current))
        {
            _csrfToken = current.FirstOrDefault() ?? _csrfToken;
        }
    }

    internal static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    private static string Truncate(string s) => s.Length > 300 ? s[..300] : s;

    public void Dispose() => _http.Dispose();
}

/// <summary>Thrown when the legacy controller API returns a non-ok result.</summary>
public sealed class UnifiLegacyException : Exception
{
    public UnifiLegacyException(string message) : base(message) { }
}
