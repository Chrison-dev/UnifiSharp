using UnifiSharp.Api;

namespace UnifiSharp;

/// <summary>
/// Read-only discovery: walks the UniFi controller via the generated client and
/// produces a structured <see cref="UnifiSnapshot"/> — sites and the
/// devices/clients/networks each contains. The in-code, repeatable read sweep,
/// sibling to ProxmoxSharp's discovery.
/// </summary>
public sealed class UnifiDiscovery
{
    private readonly UnifiApiClient _client;

    public UnifiDiscovery(UnifiApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<UnifiSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var sites = (await _client.V1.Sites.GetAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))?.Data ?? [];

        var snapshots = new List<UnifiSiteSnapshot>(sites.Count);
        foreach (var site in sites)
        {
            if (site.Id is not Guid id)
            {
                continue;
            }

            var builder = _client.V1.Sites[id];
            var devices = (await builder.Devices.GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var clients = (await builder.Clients.GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var networks = (await builder.Networks.GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];

            snapshots.Add(new UnifiSiteSnapshot
            {
                Site = site.Name ?? id.ToString(),
                Id = site.Id,
                Devices = devices.Count,
                Clients = clients.Count,
                Networks = networks
                    .Select(n => n.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList(),
            });
        }

        return new UnifiSnapshot { Sites = snapshots };
    }
}
