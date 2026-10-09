using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace UnifiSharp.Firewall;

public sealed record FirewallZone(Guid Id, string Name, string Origin);

/// <summary>A policy as the controller holds it. <see cref="Origin"/> is USER_DEFINED,
/// SYSTEM_DEFINED or DERIVED; only USER_DEFINED policies are ever written by this library's callers.</summary>
public sealed record LiveFirewallPolicy(Guid Id, string Name, string Origin, JsonObject Raw)
{
    public bool IsUserDefined => Origin == "USER_DEFINED";
}

/// <summary>
/// Zone-based firewall policies through the OFFICIAL integration API
/// (<c>{BaseUrl}/v1/sites/{site}/firewall/…</c>, X-API-KEY). Not the legacy adapter: the
/// integration API has full CRUD for policies (UniFi Network 9+), so there's no reason to
/// go through the undocumented surface (ADR-0003).
/// </summary>
public sealed class UnifiFirewallClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;
    private readonly string? _siteName;
    private Guid? _siteId;

    /// <param name="options">BaseUrl is the integration root, e.g. https://gw/proxy/network/integration.</param>
    /// <param name="siteName">Site to use by name; null = the controller's first (usually only) site.</param>
    public UnifiFirewallClient(UnifiClientOptions options, string? siteName = null, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        handler ??= options.VerifyTls ? new HttpClientHandler()
            : new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Add("X-API-KEY", options.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _base = options.BaseUrl.AbsoluteUri.TrimEnd('/');
        _siteName = siteName;
    }

    public async Task<Guid> GetSiteIdAsync(CancellationToken ct = default)
    {
        if (_siteId is { } id) return id;
        var sites = await ListAllAsync($"{_base}/v1/sites", ct).ConfigureAwait(false);
        var site = _siteName is null ? sites.FirstOrDefault()
            : sites.FirstOrDefault(s => string.Equals(s["name"]?.GetValue<string>(), _siteName, StringComparison.OrdinalIgnoreCase));
        _siteId = site?["id"]?.GetValue<string>() is { } sid ? Guid.Parse(sid)
            : throw new UnifiFirewallException($"site '{_siteName ?? "(first)"}' not found");
        return _siteId.Value;
    }

    public async Task<IReadOnlyList<FirewallZone>> ListZonesAsync(CancellationToken ct = default)
    {
        var rows = await ListAllAsync($"{await SiteAsync(ct)}/firewall/zones", ct).ConfigureAwait(false);
        return rows.Select(r => new FirewallZone(Guid.Parse(r["id"]!.GetValue<string>()), r["name"]!.GetValue<string>(),
            r["metadata"]?["origin"]?.GetValue<string>() ?? "")).ToList();
    }

    public async Task<FirewallNames> GetNamesAsync(CancellationToken ct = default)
    {
        var zones = await ListZonesAsync(ct).ConfigureAwait(false);
        var nets = await ListAllAsync($"{await SiteAsync(ct)}/networks", ct).ConfigureAwait(false);
        return new FirewallNames(
            zones.ToDictionary(z => z.Name, z => z.Id, StringComparer.OrdinalIgnoreCase),
            nets.Where(n => n["name"] is not null && n["id"] is not null)
                .GroupBy(n => n["name"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Guid.Parse(g.First()["id"]!.GetValue<string>()), StringComparer.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<LiveFirewallPolicy>> ListPoliciesAsync(CancellationToken ct = default)
    {
        var rows = await ListAllAsync($"{await SiteAsync(ct)}/firewall/policies", ct).ConfigureAwait(false);
        return rows.Select(r => new LiveFirewallPolicy(Guid.Parse(r["id"]!.GetValue<string>()),
            r["name"]?.GetValue<string>() ?? "", r["metadata"]?["origin"]?.GetValue<string>() ?? "", r)).ToList();
    }

    public async Task<LiveFirewallPolicy> CreateAsync(FirewallPolicySpec spec, FirewallNames names, CancellationToken ct = default)
        => Parse(await SendAsync(HttpMethod.Post, $"{await SiteAsync(ct)}/firewall/policies", FirewallPolicyJson.ToJson(spec, names), ct).ConfigureAwait(false));

    /// <summary>Replace a policy wholesale (PUT): the controller keeps nothing the spec doesn't carry.</summary>
    public async Task<LiveFirewallPolicy> UpdateAsync(Guid id, FirewallPolicySpec spec, FirewallNames names, CancellationToken ct = default)
        => Parse(await SendAsync(HttpMethod.Put, $"{await SiteAsync(ct)}/firewall/policies/{id}", FirewallPolicyJson.ToJson(spec, names), ct).ConfigureAwait(false));

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Delete, $"{await SiteAsync(ct)}/firewall/policies/{id}", null, ct).ConfigureAwait(false);

    private static LiveFirewallPolicy Parse(JsonNode? n) => n is JsonObject o
        ? new LiveFirewallPolicy(Guid.Parse(o["id"]!.GetValue<string>()), o["name"]?.GetValue<string>() ?? "", o["metadata"]?["origin"]?.GetValue<string>() ?? "", o)
        : throw new UnifiFirewallException("controller returned no policy object");

    private async Task<string> SiteAsync(CancellationToken ct) => $"{_base}/v1/sites/{await GetSiteIdAsync(ct).ConfigureAwait(false)}";

    // The integration API pages every list: {offset, limit, count, totalCount, data}.
    private async Task<List<JsonObject>> ListAllAsync(string url, CancellationToken ct)
    {
        var all = new List<JsonObject>();
        for (var offset = 0; ;)
        {
            var page = await SendAsync(HttpMethod.Get, $"{url}?offset={offset}&limit=200", null, ct).ConfigureAwait(false);
            var data = page?["data"] as JsonArray ?? [];
            all.AddRange(data.OfType<JsonObject>());
            var total = page?["totalCount"]?.GetValue<int>() ?? all.Count;
            offset += data.Count;
            if (data.Count == 0 || offset >= total) return all;
        }
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string url, JsonObject? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new UnifiFirewallException($"{method} {url} → {(int)resp.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public void Dispose() => _http.Dispose();
}

public sealed class UnifiFirewallException(string message) : Exception(message);
