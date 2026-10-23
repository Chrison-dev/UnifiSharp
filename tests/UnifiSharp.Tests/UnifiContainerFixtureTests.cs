namespace UnifiSharp.Tests;

// The fixture's decisions that need no container: the opt-out switch, the refusal to run
// the privileged container anywhere but this machine, and the login backoff schedule.
public class UnifiContainerFixtureTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData(" yes ")]
    public void Skip_switch_accepts_the_usual_yes_spellings(string value) =>
        Assert.True(UnifiContainerFixture.SkipRequested(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("No")]
    public void Skip_switch_unset_or_no_runs_the_tests(string? value) =>
        Assert.False(UnifiContainerFixture.SkipRequested(value));

    [Theory]
    [InlineData("y")]
    [InlineData("on")]
    [InlineData("2")]
    public void Skip_switch_with_an_unrecognised_value_fails_loudly(string value)
    {
        // A typo must not silently run (or silently skip) the write tests.
        var ex = Assert.Throws<InvalidOperationException>(() => UnifiContainerFixture.SkipRequested(value));
        Assert.Contains("UNIFISHARP_SKIP_CONTAINER_TESTS", ex.Message);
    }

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("unix:///Users/me/.orbstack/run/docker.sock")]
    [InlineData("npipe://./pipe/docker_engine")]
    [InlineData("tcp://localhost:2375")]
    [InlineData("tcp://127.0.0.1:2375")]
    [InlineData("tcp://[::1]:2375")]
    [InlineData("http://LOCALHOST:2375")]
    public void Local_docker_endpoints_are_accepted(string endpoint) =>
        Assert.Null(UnifiContainerFixture.NonLocalDockerReason(new Uri(endpoint), hostOverride: null));

    [Theory]
    [InlineData("tcp://10.0.0.14:2375")]
    [InlineData("tcp://docker.homelab.internal:2376")]
    [InlineData("https://192.168.1.20:2376")]
    [InlineData("ssh://root@hpe-01")]
    public void Remote_or_unrecognised_docker_endpoints_are_refused(string endpoint)
    {
        var reason = UnifiContainerFixture.NonLocalDockerReason(new Uri(endpoint), hostOverride: null);

        Assert.NotNull(reason);
        Assert.Contains(endpoint.TrimEnd('/'), reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_docker_endpoint_is_refused_not_assumed_local() =>
        Assert.NotNull(UnifiContainerFixture.NonLocalDockerReason(endpoint: null, hostOverride: null));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void A_loopback_host_override_is_accepted(string host) =>
        Assert.Null(UnifiContainerFixture.NonLocalDockerReason(new Uri("unix:///var/run/docker.sock"), host));

    [Theory]
    [InlineData("10.0.0.14")]
    [InlineData("hpe-01.homelab.chrison.internal")]
    [InlineData("host.docker.internal")]
    public void A_remote_host_override_is_refused_even_with_a_local_socket(string host)
    {
        // The override is where the tests send their writes, so it alone can redirect them.
        var reason = UnifiContainerFixture.NonLocalDockerReason(new Uri("unix:///var/run/docker.sock"), host);

        Assert.NotNull(reason);
        Assert.Contains(host, reason);
    }

    [Fact]
    public void Login_backoff_starts_at_15s_doubles_and_is_capped_at_two_minutes()
    {
        var delays = Enumerable.Range(1, 6).Select(UnifiContainerFixture.LoginBackoff.Delay).ToArray();

        Assert.Equal(
            [
                TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
                TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2),
            ],
            delays);
    }
}
