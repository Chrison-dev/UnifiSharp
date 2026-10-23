using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UnifiSharp.Legacy;

/// <summary>
/// Write client for the <b>legacy</b> UniFi controller REST API — port-forwards,
/// firewall groups, and networks/VLANs. The official integration API
/// (<see cref="UnifiApi"/>) has no endpoints for these yet (rolling out through
/// 2026), so this is the stopgap write path.
/// <para><b>Thin, isolated, deletion-ready</b> (ADR-0003): the legacy API is
/// undocumented and version-brittle. When Ubiquiti ships the official write
/// endpoints, delete this whole namespace and switch callers to the generated
/// client.</para>
/// </summary>
[Obsolete("Legacy undocumented UniFi controller API. Replace with the official integration API " +
          "write endpoints once Ubiquiti ships them (rolling out through 2026); see ADR-0003. " +
          "Isolated + deletion-ready by design.")]
public sealed class UnifiLegacyClient : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // create sends only set fields
    };

    private readonly UnifiLegacySession _session;
    private readonly bool _ownsSession;

    public UnifiLegacyClient(UnifiLegacyOptions options)
        : this(new UnifiLegacySession(options), ownsSession: true) { }

    public UnifiLegacyClient(UnifiLegacySession session, bool ownsSession = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _ownsSession = ownsSession;
    }

    // ── Port-forwards (rest/portforward) ──────────────────────────────────────
    public Task<IReadOnlyList<UnifiPortForward>> ListPortForwardsAsync(CancellationToken ct = default)
        => ListAsync<UnifiPortForward>("portforward", ct);

    public Task<UnifiPortForward> CreatePortForwardAsync(UnifiPortForward spec, CancellationToken ct = default)
        => CreateAsync("portforward", spec, ct);

    /// <summary>
    /// Partial-update a port-forward. Only non-null properties are sent, so a caller
    /// can correct one drifted field without restating the rule.
    /// </summary>
    public Task<UnifiPortForward> UpdatePortForwardAsync(string id, UnifiPortForward spec, CancellationToken ct = default)
        => UpdateAsync("portforward", id, spec, ct);

    public Task DeletePortForwardAsync(string id, CancellationToken ct = default)
        => DeleteAsync("portforward", id, ct);

    // ── Firewall groups (rest/firewallgroup) ──────────────────────────────────
    public Task<IReadOnlyList<UnifiFirewallGroup>> ListFirewallGroupsAsync(CancellationToken ct = default)
        => ListAsync<UnifiFirewallGroup>("firewallgroup", ct);

    public Task<UnifiFirewallGroup> CreateFirewallGroupAsync(UnifiFirewallGroup spec, CancellationToken ct = default)
        => CreateAsync("firewallgroup", spec, ct);

    public Task DeleteFirewallGroupAsync(string id, CancellationToken ct = default)
        => DeleteAsync("firewallgroup", id, ct);

    // ── Networks / VLANs (rest/networkconf) ───────────────────────────────────
    public Task<IReadOnlyList<UnifiNetwork>> ListNetworksAsync(CancellationToken ct = default)
        => ListAsync<UnifiNetwork>("networkconf", ct);

    public Task<UnifiNetwork> CreateNetworkAsync(UnifiNetwork spec, CancellationToken ct = default)
        => CreateAsync("networkconf", spec, ct);

    public Task DeleteNetworkAsync(string id, CancellationToken ct = default)
        => DeleteAsync("networkconf", id, ct);

    // NOTE there is no UpdateNetworkAsync(UnifiNetwork), deliberately. UnifiNetwork models
    // ~12 of a live network's ~63 fields and rest/networkconf/{id} is a full replacement,
    // so a typed update would silently reset the rest. Updates go through the raw trio
    // below: read the whole object, change named keys (e.g. NetworkDhcpDns.With), put it back.

    /// <summary>Every network as the controller sent it — no field dropped, no number re-formatted.</summary>
    public Task<IReadOnlyList<JsonObject>> ListNetworksRawAsync(CancellationToken ct = default)
        => ListRawAsync("networkconf", ct);

    /// <summary>
    /// One network as the controller sent it. Throws unless the controller returns exactly
    /// one row — zero or several is not "the" network, and a write built on it would be a guess.
    /// </summary>
    public async Task<JsonObject> GetNetworkRawAsync(string id, CancellationToken ct = default)
    {
        ThrowUnlessPlainSegment(id);
        using var resp = await _session.SendAsync(
            HttpMethod.Get, $"rest/networkconf/{id}", content: null, ct).ConfigureAwait(false);
        var rows = RawRows(await ReadEnvelopeAsync<JsonObject?>(resp, "networkconf", ct).ConfigureAwait(false), "networkconf");
        return rows.Count == 1
            ? rows[0]
            : throw new UnifiLegacyException($"networkconf/{id}: expected exactly one network, got {rows.Count}");
    }

    /// <summary>
    /// Replace a network wholesale with <paramref name="body"/>, sent verbatim. Pass an object
    /// read by <see cref="GetNetworkRawAsync"/> and changed in place of named keys only; the
    /// caller's object is not modified. Returns the controller's echo of the network, or a copy
    /// of <paramref name="body"/> when the controller answers ok with no row — re-read to confirm.
    /// <para>"Verbatim" is per value, not per byte, for strings: numbers keep their original text
    /// and keys their order, but a string is re-escaped on write, so <c>"caf\u00e9"</c> read in
    /// may go out as <c>"café"</c> (same value, different bytes).</para>
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a plain path segment, or
    /// <paramref name="body"/>'s <c>_id</c> is missing, not a string, or not <paramref name="id"/>.</exception>
    public async Task<JsonObject> ReplaceNetworkRawAsync(string id, JsonObject body, CancellationToken ct = default)
    {
        ThrowUnlessPlainSegment(id);
        ArgumentNullException.ThrowIfNull(body);
        // The PUT replaces the network at {id} with whatever body says, so a body read from
        // another network would silently overwrite this one with it. Missing counts as mismatch.
        var bodyId = body.TryGetPropertyValue("_id", out var idNode) && idNode?.GetValueKind() is JsonValueKind.String
            ? idNode.GetValue<string>()
            : null;
        if (bodyId != id)
        {
            throw new ArgumentException(
                $"body._id is {(bodyId is null ? "missing or not a string" : $"'{bodyId}'")}, expected '{id}'.", nameof(body));
        }

        // ToJsonString writes parsed numbers from their original text, so 1.50 stays 1.50.
        using var resp = await _session.SendAsync(
            HttpMethod.Put, $"rest/networkconf/{id}", UnifiLegacySession.JsonBody(body.ToJsonString()), ct).ConfigureAwait(false);
        var rows = RawRows(await ReadEnvelopeAsync<JsonObject?>(resp, "networkconf", ct).ConfigureAwait(false), "networkconf");
        // Same echo-on-empty as UpdateAsync, but cloned so the caller's object stays theirs.
        return rows.Count > 0 ? rows[0] : body.DeepClone().AsObject();
    }

    // ── Known clients / DHCP reservations (rest/user) ─────────────────────────
    // NOTE there is no Delete here, deliberately. Removing a known client discards
    // its name and history along with the reservation; retiring a reservation is
    // UpdateUserAsync with UseFixedIp=false, which is reversible.

    public Task<IReadOnlyList<UnifiUser>> ListUsersAsync(CancellationToken ct = default)
        => ListAsync<UnifiUser>("user", ct);

    /// <summary>
    /// Partial-update a known client. Only the non-null properties of
    /// <paramref name="spec"/> are sent, so untouched fields keep their server values.
    /// </summary>
    public Task<UnifiUser> UpdateUserAsync(string id, UnifiUser spec, CancellationToken ct = default)
        => UpdateAsync("user", id, spec, ct);

    /// <summary>
    /// Register a client the controller has never seen. Rarely needed — a guest that
    /// has ever taken a lease already has a row, and <see cref="UpdateUserAsync"/> is
    /// the path for it.
    /// </summary>
    public Task<UnifiUser> CreateUserAsync(UnifiUser spec, CancellationToken ct = default)
        => CreateAsync("user", spec, ct);

    // ── Static DNS (v2 site API: static-dns) ──────────────────────────────────
    // A DIFFERENT surface from everything above: bare JSON arrays instead of the
    // {meta,data} envelope, and updates are full replacements — a PUT carrying only the
    // changed field returns 400 Validation failed, where rest/user accepts exactly that.
    // Hence UnifiStaticDnsRecord is non-nullable throughout and Update takes a whole record.

    public async Task<IReadOnlyList<UnifiStaticDnsRecord>> ListStaticDnsAsync(CancellationToken ct = default)
    {
        using var resp = await _session.SendAbsoluteAsync(HttpMethod.Get, StaticDnsUrl(), null, ct).ConfigureAwait(false);
        return await ReadV2Async<IReadOnlyList<UnifiStaticDnsRecord>>(resp, "static-dns", ct).ConfigureAwait(false) ?? [];
    }

    public async Task<UnifiStaticDnsRecord> CreateStaticDnsAsync(UnifiStaticDnsRecord spec, CancellationToken ct = default)
    {
        var body = UnifiLegacySession.JsonBody(JsonSerializer.Serialize(spec with { Id = null }, SerializerOptions));
        using var resp = await _session.SendAbsoluteAsync(HttpMethod.Post, StaticDnsUrl(), body, ct).ConfigureAwait(false);
        return await ReadV2Async<UnifiStaticDnsRecord>(resp, "static-dns", ct).ConfigureAwait(false)
               ?? throw new UnifiLegacyException("create static-dns: ok but no object returned");
    }

    /// <summary>Replace a record wholesale. Pass the full desired state — partials are rejected.</summary>
    public async Task<UnifiStaticDnsRecord> UpdateStaticDnsAsync(string id, UnifiStaticDnsRecord spec, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var body = UnifiLegacySession.JsonBody(JsonSerializer.Serialize(spec with { Id = id }, SerializerOptions));
        using var resp = await _session.SendAbsoluteAsync(HttpMethod.Put, StaticDnsUrl(id), body, ct).ConfigureAwait(false);
        return await ReadV2Async<UnifiStaticDnsRecord>(resp, "static-dns", ct).ConfigureAwait(false) ?? spec;
    }

    public async Task DeleteStaticDnsAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var resp = await _session.SendAbsoluteAsync(HttpMethod.Delete, StaticDnsUrl(id), null, ct).ConfigureAwait(false);
        await ReadV2Async<JsonElement?>(resp, "static-dns", ct).ConfigureAwait(false);   // throws on failure
    }

    private Uri StaticDnsUrl(string? id = null)
    {
        var b = _session.Options.SiteV2Url.AbsoluteUri.TrimEnd('/');
        return new Uri(id is null ? $"{b}/static-dns" : $"{b}/static-dns/{id}");
    }

    // v2 has no envelope, so success is the status code and the body is the payload
    // (empty on DELETE). Surface the server's message on failure rather than a bare code.
    private static async Task<T?> ReadV2Async<T>(HttpResponseMessage resp, string resource, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new UnifiLegacyException($"{resource}: HTTP {(int)resp.StatusCode} — {Truncate(body)}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new UnifiLegacyException($"{resource}: unparseable response — {ex.Message}: {Truncate(body)}");
        }
    }

    // ── Generic REST verbs over the {meta,data} envelope ──────────────────────

    private async Task<IReadOnlyList<T>> ListAsync<T>(string resource, CancellationToken ct)
    {
        using var resp = await _session.SendAsync(HttpMethod.Get, $"rest/{resource}", content: null, ct).ConfigureAwait(false);
        return (await ReadEnvelopeAsync<T>(resp, resource, ct).ConfigureAwait(false)).Data;
    }

    private async Task<IReadOnlyList<JsonObject>> ListRawAsync(string resource, CancellationToken ct)
    {
        using var resp = await _session.SendAsync(HttpMethod.Get, $"rest/{resource}", content: null, ct).ConfigureAwait(false);
        return RawRows(await ReadEnvelopeAsync<JsonObject?>(resp, resource, ct).ConfigureAwait(false), resource);
    }

    // A null row is a shape we don't recognise, not an empty object — refuse it rather
    // than hand back something a caller could write over a real network. Likewise "data":null:
    // the deserializer leaves Data null despite its type, and that is not "no rows".
    private static IReadOnlyList<JsonObject> RawRows(UnifiLegacyEnvelope<JsonObject?> env, string resource) =>
        (env.Data ?? throw new UnifiLegacyException($"{resource}: response data is null"))
            .Select(r => r ?? throw new UnifiLegacyException($"{resource}: response row is null"))
            .ToList();

    // The id is interpolated into the path, so "a/../x" or "a?b" would address something else.
    // Controller ids are hex ObjectIds; anything percent-encoding would change is refused, not escaped.
    private static void ThrowUnlessPlainSegment(string id, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(id))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id, paramName);
        if (id is "." or ".." || Uri.EscapeDataString(id) != id)
        {
            throw new ArgumentException($"'{id}' is not a plain path segment.", paramName);
        }
    }

    private async Task<T> CreateAsync<T>(string resource, T spec, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(spec, SerializerOptions);
        using var resp = await _session.SendAsync(
            HttpMethod.Post, $"rest/{resource}", UnifiLegacySession.JsonBody(json), ct).ConfigureAwait(false);
        var env = await ReadEnvelopeAsync<T>(resp, resource, ct).ConfigureAwait(false);
        return env.Data.Count > 0
            ? env.Data[0]
            : throw new UnifiLegacyException($"create {resource}: ok but no object returned");
    }

    private async Task<T> UpdateAsync<T>(string resource, string id, T spec, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var json = JsonSerializer.Serialize(spec, SerializerOptions);
        using var resp = await _session.SendAsync(
            HttpMethod.Put, $"rest/{resource}/{id}", UnifiLegacySession.JsonBody(json), ct).ConfigureAwait(false);
        var env = await ReadEnvelopeAsync<T>(resp, resource, ct).ConfigureAwait(false);
        // A PUT is ok-with-empty-data on some resources; echo the request back so
        // callers always get an object rather than having to null-check a success.
        return env.Data.Count > 0 ? env.Data[0] : spec;
    }

    private async Task DeleteAsync(string resource, string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var resp = await _session.SendAsync(
            HttpMethod.Delete, $"rest/{resource}/{id}", content: null, ct).ConfigureAwait(false);
        await ReadEnvelopeAsync<JsonElement>(resp, resource, ct).ConfigureAwait(false); // throws on rc != ok
    }

    // Parse + validate the standard envelope. The legacy API returns HTTP 400 with
    // a {meta:{rc:"error",msg:…}} body on validation failures, so surface the msg
    // rather than the bare status code.
    private static async Task<UnifiLegacyEnvelope<T>> ReadEnvelopeAsync<T>(
        HttpResponseMessage resp, string resource, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        UnifiLegacyEnvelope<T>? env = null;
        try
        {
            env = JsonSerializer.Deserialize<UnifiLegacyEnvelope<T>>(body, SerializerOptions);
        }
        catch (JsonException)
        {
            // fall through to the status/body error below
        }

        if (env is null)
        {
            throw new UnifiLegacyException(
                $"{resource}: unparseable response ({(int)resp.StatusCode}): {Truncate(body)}");
        }

        if (!env.Meta.IsOk)
        {
            throw new UnifiLegacyException($"{resource}: API error '{env.Meta.Msg ?? "unknown"}' (HTTP {(int)resp.StatusCode})");
        }

        return env;
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] : s;

    public void Dispose()
    {
        if (_ownsSession)
        {
            _session.Dispose();
        }
    }
}
