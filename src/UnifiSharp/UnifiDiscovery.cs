using UnifiSharp.Api;

namespace UnifiSharp;

/// <summary>
/// Read-only discovery: walks the UniFi controller via the generated client and
/// produces a structured <see cref="UnifiSnapshot"/> — for each site the
/// networks/VLANs, WLANs, firewall (zones + policies + ACL rules), and the
/// devices/clients it contains. The in-code, repeatable read sweep, sibling to
/// ProxmoxSharp's discovery.
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
            var wlans = (await builder.Wifi.Broadcasts.GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var zones = (await builder.Firewall.Zones.GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var policies = (await builder.Firewall.Policies.GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var aclRules = (await builder.AclRules.GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];

            snapshots.Add(new UnifiSiteSnapshot
            {
                Site = site.Name ?? id.ToString(),
                Id = site.Id,
                DeviceCount = devices.Count,
                ClientCount = clients.Count,
                Networks = networks.Select(n => new UnifiNetworkInfo
                {
                    Name = n.Name ?? string.Empty,
                    Id = n.Id,
                    VlanId = n.VlanId,
                    Enabled = n.Enabled,
                    Purpose = n.Management,
                    IsDefault = n.Default,
                }).ToList(),
                Wlans = wlans.Select(w => new UnifiWlanInfo
                {
                    Ssid = w.Name ?? string.Empty,
                    Id = w.Id,
                    Enabled = w.Enabled,
                    Security = w.SecurityConfiguration?.Type,
                    NetworkType = w.Network?.Type,
                }).ToList(),
                Firewall = new UnifiFirewallSnapshot
                {
                    Zones = zones.Select(z => new UnifiFirewallZoneInfo
                    {
                        Name = z.Name ?? string.Empty,
                        Id = z.Id,
                        NetworkIds = (z.NetworkIds ?? [])
                            .Where(g => g.HasValue)
                            .Select(g => g!.Value)
                            .ToList(),
                    }).ToList(),
                    Policies = policies.Select(p => new UnifiFirewallPolicyInfo
                    {
                        Name = p.Name ?? string.Empty,
                        Id = p.Id,
                        Index = p.Index,
                        Enabled = p.Enabled,
                        Action = p.Action?.Type,
                        SourceZoneId = p.Source?.ZoneId,
                        DestinationZoneId = p.Destination?.ZoneId,
                    }).ToList(),
                    AclRules = aclRules.Select(a => new UnifiAclRuleInfo
                    {
                        Name = a.Name ?? string.Empty,
                        Id = a.Id,
                        Index = a.Index,
                        Enabled = a.Enabled,
                        Type = a.Type,
                        Action = a.Action?.ToString(),
                    }).ToList(),
                },
                Devices = devices.Select(d => new UnifiDeviceInfo
                {
                    Name = d.Name ?? string.Empty,
                    Id = d.Id,
                    Model = d.Model,
                    MacAddress = d.MacAddress,
                    IpAddress = d.IpAddress,
                    FirmwareVersion = d.FirmwareVersion,
                    State = d.State?.ToString(),
                }).ToList(),
                Clients = clients.Select(c => new UnifiClientInfo
                {
                    Name = c.Name ?? string.Empty,
                    Id = c.Id,
                    Type = c.Type,
                    IpAddress = c.IpAddress,
                    ConnectedAt = c.ConnectedAt,
                }).ToList(),
            });
        }

        return new UnifiSnapshot { Sites = snapshots };
    }
}
