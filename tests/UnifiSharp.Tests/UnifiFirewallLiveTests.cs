using UnifiSharp.Firewall;

namespace UnifiSharp.Tests;

// READ-ONLY against a real controller; skipped unless UNIFI_BASE_URL + UNIFI_API_KEY are set.
public class UnifiFirewallLiveTests
{
    [SkippableFact]
    public async Task Lists_zones_and_reads_every_user_defined_policy()
    {
        var options = UnifiClientOptions.TryFromEnvironment();
        Skip.If(options is null, "UNIFI_BASE_URL / UNIFI_API_KEY not set");

        using var fw = new UnifiFirewallClient(options!);
        var names = await fw.GetNamesAsync();
        Assert.Contains("External", names.ZoneIds.Keys);

        var policies = await fw.ListPoliciesAsync();
        Assert.NotEmpty(policies);
        foreach (var p in policies.Where(p => p.IsUserDefined))
        {
            // Every user policy either maps cleanly or says why it can't — never throws.
            var (spec, why) = FirewallPolicyJson.FromJson(p.Raw, names);
            Assert.True(spec is not null || why is not null, p.Name);
        }
    }
}
