using Xunit;

namespace UnifiSharp.Tests;

/// <summary>
/// Read-only integration test against a UniFi controller — the live console or
/// the <c>.containers/unifi</c> test container. Runs only when UNIFI_* env vars
/// are set; otherwise skips.
/// </summary>
public class UnifiLiveTests
{
    [SkippableFact]
    public async Task Discover_returns_a_snapshot()
    {
        var options = UnifiClientOptions.TryFromEnvironment();
        Skip.If(options is null, "No UNIFI_BASE_URL / UNIFI_API_KEY — skipping live UniFi discovery.");

        var snapshot = await new UnifiDiscovery(UnifiApi.Create(options!)).DiscoverAsync();

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Sites);
    }
}
