using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using UnifiSharp.Legacy;
using Xunit;

namespace UnifiSharp.Tests;

#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

/// <summary>
/// A throwaway UniFi OS Server, started by Testcontainers for the legacy WRITE tests and
/// removed when the run ends. Same image and settings as
/// <c>.containers/unifi/compose.yml</c> in the Homelab repo, plus the first-run setup that
/// <c>bootstrap.sh</c> performs.
/// <para>The client only ever points at this fixture's own container: the URL is built
/// from the container's mapped port, never read from the environment. That is the whole
/// safety argument for the write tests — no <c>UNIFI_*</c> variable can redirect them at a
/// real console. For the same reason the fixture refuses a remote Docker daemon or a
/// non-loopback host override: either would send the writes, and the privileged container,
/// somewhere other than this machine.</para>
/// <para>Skips (with the reason in <see cref="SkipReason"/>) when no container runtime is
/// reachable or <c>UNIFISHARP_SKIP_CONTAINER_TESTS</c> is 1/true/yes. Any other startup
/// failure — a timeout, a refused setup, a remote daemon — is a failure, not a skip.</para>
/// </summary>
public sealed class UnifiContainerFixture : IAsyncLifetime
{
    // Pinned by digest: Testcontainers only pulls an image that is missing, so a floating
    // tag would silently keep whatever was pulled first. 5.0.8 is the build the Homelab
    // compose container runs and the README's findings were verified against.
    public const string Image = "hieutq/unifi-os-server:5.0.8@sha256:71393f7afc736e196eeb64a0a98b32a03e094a1f23008bfcfc48d28d2660ef24";
    public const string Username = "labadmin";
    public const string Password = "LabAdmin!2026";   // 8-64 chars (UniFi OS policy); throwaway, container-only
    public const string Label = "dev.chrison.unifisharp.tests";

    private const string SkipVariable = "UNIFISHARP_SKIP_CONTAINER_TESTS";

    // First boot downloads Ubiquiti's installer into the fresh container, which takes
    // minutes on a good line. Generous on purpose; UNIFISHARP_CONTAINER_STARTUP_MINUTES
    // overrides it for slow links (or shortens it when the image is warm).
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromMinutes(20);

    private IContainer? _container;

    /// <summary>Why the write tests are skipped; null when the container is up.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>The logged-in client, shared by every test in the collection.</summary>
    public UnifiLegacyClient Client =>
        _client ?? throw new InvalidOperationException($"UniFi container is not available: {SkipReason}");
    private UnifiLegacyClient? _client;

    /// <summary>How long the container took to become usable — reported, not asserted.</summary>
    public TimeSpan StartupTime { get; private set; }

    public async Task InitializeAsync()
    {
        if (SkipRequested(Environment.GetEnvironmentVariable(SkipVariable)))
        {
            SkipReason = $"{SkipVariable} is set — skipping the UniFi container write tests.";
            return;
        }

        var timeout = StartupTimeout();
        using var cts = new CancellationTokenSource(timeout);
        var stopwatch = Stopwatch.StartNew();
        var progress = new StartupProgress();

        try
        {
            _container = new ContainerBuilder()
                .WithImage(Image)
                // Our own name and label, never the compose container's `unifi-os-server`: a
                // manually bootstrapped one may be running and is not ours to reuse or stop.
                .WithName($"unifisharp-tests-{Guid.NewGuid().ToString("N")[..8]}")
                .WithLabel(Label, "legacy-write")
                // Only the GUI/API port, on a free random host port. 8080/3478/10003 are for
                // real device adoption and would collide with the compose container.
                .WithPortBinding(443, assignRandomHostPort: true)
                .WithBindMount("/sys/fs/cgroup", "/sys/fs/cgroup", AccessMode.ReadWrite)
                .WithCreateParameterModifier(p =>
                {
                    // compose.yml's `cgroup: host`, cap set and tmpfs mounts: UniFi OS Server runs
                    // systemd plus ~10 internal services. No /unifi volume — the data lives in the
                    // container layer and goes with it.
                    var host = p.HostConfig ?? throw new InvalidOperationException("Testcontainers built no HostConfig to modify.");
                    host.CgroupnsMode = "host";
                    host.CapDrop = ["ALL"];
                    host.CapAdd =
                    [
                        "SYS_ADMIN", "NET_ADMIN", "NET_RAW", "NET_BIND_SERVICE", "DAC_OVERRIDE",
                        "DAC_READ_SEARCH", "FOWNER", "CHOWN", "SETUID", "SETGID", "KILL",
                        "SYS_CHROOT", "SYS_PTRACE", "SYS_RESOURCE", "AUDIT_WRITE", "MKNOD",
                    ];
                    host.Tmpfs = new Dictionary<string, string>
                    {
                        ["/run"] = "exec",
                        ["/run/lock"] = "",
                        ["/tmp"] = "exec",
                        ["/var/lib/journal"] = "",
                        ["/var/opt/unifi/tmp"] = "size=64m",
                    };
                })
                .Build();
        }
        catch (DockerUnavailableException ex)
        {
            // Thrown from Build() when no endpoint is configured or reachable; nothing exists yet.
            SkipReason = $"No container runtime reachable — skipping the UniFi container write tests. ({ex.Message.Split('\n')[0]})";
            return;
        }

        // Before anything privileged starts: the daemon, and any host override, must be local.
        RefuseIfNotLocal(TestcontainersSettings.OS.DockerEndpointAuthConfig?.Endpoint, TestcontainersSettings.DockerHostOverride);

        try
        {
            progress.Last = "starting the container";
            await _container.StartAsync(cts.Token);

            // Hostname is what the client will talk to, so it gets the same check: Testcontainers
            // derives it from the endpoint and the override, and that derivation is not ours.
            RefuseIfNotLocal(TestcontainersSettings.OS.DockerEndpointAuthConfig?.Endpoint, _container.Hostname);

            var root = new UriBuilder("https", _container.Hostname, _container.GetMappedPublicPort(443)).Uri;
            await Bootstrap.RunAsync(root, progress, cts.Token);

            var options = new UnifiLegacyOptions
            {
                BaseUrl = new Uri(root, "/proxy/network/api/s/default"),
                Username = Username,
                Password = Password,
                VerifyTls = false,   // the container serves a self-signed cert
            };
            // The session's handler backs off on a 429 from the login endpoint, which covers both
            // the login below and the one the session repeats on a 401 mid-test.
            var session = new UnifiLegacySession(options, new LoginBackoffHandler(new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            }));
            _client = new UnifiLegacyClient(session, ownsSession: true);

            // One login for the whole run, so the tests never add to the login rate limit.
            progress.Last = "logging the shared session in";
            await session.LoginAsync(cts.Token);
        }
        catch (Exception ex)
        {
            // Don't count on xunit disposing a fixture whose initialisation failed: remove the
            // container here, then surface the failure.
            var name = _container.Name;
            await DisposeAsync();
            _client = null;
            _container = null;
            if (ex is OperationCanceledException && cts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"UniFi OS container {name} was not ready within {timeout}; last status: {progress.Last} " +
                    "(first boot downloads the installer; raise UNIFISHARP_CONTAINER_STARTUP_MINUTES on a slow link).", ex);
            }

            throw;
        }

        StartupTime = stopwatch.Elapsed;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Parses <c>UNIFISHARP_SKIP_CONTAINER_TESTS</c>: 1/true/yes skip, unset/0/false/no run,
    /// anything else throws — a typo must not quietly decide whether the write tests run.
    /// </summary>
    internal static bool SkipRequested(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "0" or "false" or "no" => false,
        "1" or "true" or "yes" => true,
        _ => throw new InvalidOperationException($"{SkipVariable} must be 1/true/yes or 0/false/no, got '{value}'."),
    };

    /// <summary>
    /// Why the Docker endpoint or the host the tests will connect to is not this machine;
    /// null when both are local. A socket or named pipe is a local daemon; a TCP/HTTP endpoint
    /// and a host override must be loopback. An unknown endpoint is refused, not assumed local.
    /// </summary>
    internal static string? NonLocalDockerReason(Uri? endpoint, string? hostOverride)
    {
        if (endpoint is null)
        {
            return "the Docker endpoint could not be determined";
        }

        var localDaemon = endpoint.Scheme switch
        {
            "unix" or "npipe" => true,
            "tcp" or "http" or "https" => IsLoopback(endpoint.Host),
            _ => false,
        };
        if (!localDaemon)
        {
            return $"the Docker endpoint {endpoint.OriginalString} is not a local socket or a loopback address";
        }

        if (!string.IsNullOrEmpty(hostOverride) && !IsLoopback(hostOverride))
        {
            return $"the container host '{hostOverride}' (TESTCONTAINERS_HOST_OVERRIDE / tc.host, or derived from DOCKER_HOST) is not a loopback address";
        }

        return null;
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Uri.Host keeps IPv6 brackets ("[::1]"); IPAddress.TryParse does not want them.
        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    private static void RefuseIfNotLocal(Uri? endpoint, string? host)
    {
        if (NonLocalDockerReason(endpoint, host) is { } reason)
        {
            // A failure, not a skip: the run asked for these tests and something is pointing
            // them away from this machine. The container runs with SYS_ADMIN and a rw cgroup mount.
            throw new InvalidOperationException(
                $"Refusing to run the UniFi container write tests: {reason}. They run only against a local Docker daemon; " +
                $"set DOCKER_HOST to a local socket, or {SkipVariable}=1 to skip them.");
        }
    }

    private static TimeSpan StartupTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("UNIFISHARP_CONTAINER_STARTUP_MINUTES");
        if (string.IsNullOrEmpty(raw))
        {
            return DefaultStartupTimeout;
        }

        // A typo here should fail loudly, not quietly fall back to the default.
        return int.TryParse(raw, out var minutes) && minutes > 0
            ? TimeSpan.FromMinutes(minutes)
            : throw new InvalidOperationException($"UNIFISHARP_CONTAINER_STARTUP_MINUTES must be a positive whole number of minutes, got '{raw}'.");
    }

    /// <summary>What startup last saw, for the timeout message.</summary>
    private sealed class StartupProgress
    {
        public string Last { get; set; } = "not started";
    }

    /// <summary>
    /// The controller's login rate limit answers 429 for a while; one schedule for every
    /// login the fixture makes, readiness probe and shared session alike.
    /// </summary>
    internal static class LoginBackoff
    {
        /// <summary>Attempts before a 429 is handed back to the caller.</summary>
        public const int MaxAttempts = 6;

        /// <summary>The wait after the <paramref name="attempt"/>-th consecutive 429 (1-based).</summary>
        public static TimeSpan Delay(int attempt)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
            var seconds = 15 * Math.Pow(2, Math.Min(attempt - 1, 4));
            return TimeSpan.FromSeconds(Math.Min(seconds, 120));
        }
    }

    /// <summary>
    /// Retries <c>POST /api/auth/login</c> on 429 with <see cref="LoginBackoff"/>; every other
    /// request passes straight through. After <see cref="LoginBackoff.MaxAttempts"/> the 429
    /// goes back to the session, which reports it as a failed login.
    /// </summary>
    private sealed class LoginBackoffHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != "/api/auth/login")
            {
                return await base.SendAsync(request, ct);
            }

            for (var attempt = 1; ; attempt++)
            {
                var resp = await base.SendAsync(request, ct);
                if (resp.StatusCode != HttpStatusCode.TooManyRequests || attempt == LoginBackoff.MaxAttempts)
                {
                    return resp;
                }

                resp.Dispose();
                await Task.Delay(LoginBackoff.Delay(attempt), ct);
            }
        }
    }

    /// <summary>
    /// bootstrap.sh's steps 1-3, against one controller root: wait for UniFi OS, run first-run
    /// setup, then wait until a legacy-API read answers 200.
    /// </summary>
    private static class Bootstrap
    {
        private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

        public static async Task RunAsync(Uri root, StartupProgress progress, CancellationToken ct)
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                // Throwaway container, self-signed cert.
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                // The readiness probe logs in once and keeps that session's cookie.
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            })
            { Timeout = TimeSpan.FromSeconds(15) };

            // 1) UniFi OS answers /api/system with a deviceState once it is up.
            await UntilAsync(progress, "waiting for /api/system", async () =>
            {
                var body = await http.GetStringAsync(new Uri(root, "/api/system"), ct);
                progress.Last = "/api/system answered without a deviceState";
                return body.Contains("\"deviceState\"", StringComparison.Ordinal);
            }, ct);

            // 2) First-run setup. A fresh container is never set up, but the setup endpoint
            //    answers 400/500 to an empty body only once it accepts input; until then it
            //    is not ready.
            await UntilAsync(progress, "waiting for /api/setup", async () =>
            {
                using var probe = await http.PostAsJsonAsync(new Uri(root, "/api/setup"), new { }, ct);
                progress.Last = $"/api/setup answered {(int)probe.StatusCode} to an empty body";
                return probe.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError;
            }, ct);
            try
            {
                using var setup = await http.PostAsJsonAsync(new Uri(root, "/api/setup"),
                    new { name = "UnifiSharp test controller", username = Username, password = Password }, ct);
                if (!setup.IsSuccessStatusCode)
                {
                    // An answer that says no is the reason, not something to wait out: without it
                    // the login below would only ever report "not ready".
                    var body = await setup.Content.ReadAsStringAsync(ct);
                    throw new InvalidOperationException(
                        $"UniFi OS first-run setup was refused ({(int)setup.StatusCode}): {(body.Length > 300 ? body[..300] : body)}");
                }

                progress.Last = $"setup accepted ({(int)setup.StatusCode})";
            }
            catch (HttpRequestException ex)
            {
                // The connection often resets while services restart after setup — expected.
                // Whether setup took is decided by the login below, not by this response.
                progress.Last = $"setup sent, response lost ({ex.Message})";
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Or it hangs until HttpClient.Timeout, for the same reason.
                progress.Last = "setup sent, response timed out";
            }

            // 3) The admin can log in and the legacy API answers. Its own session, not the
            //    shared one's: logged in once and reused, re-logged-in only on a 401, and
            //    backed off on a 429 so polling never feeds the login rate limit.
            var loggedIn = false;
            var rateLimited = 0;
            await UntilAsync(progress, "waiting for the admin login and legacy API", async () =>
            {
                if (!loggedIn)
                {
                    using var login = await http.PostAsJsonAsync(new Uri(root, "/api/auth/login"),
                        new { username = Username, password = Password }, ct);
                    if (login.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var delay = LoginBackoff.Delay(++rateLimited);
                        progress.Last = $"login answered 429 ({rateLimited} in a row); backing off {delay}";
                        await Task.Delay(delay, ct);
                        return false;
                    }

                    rateLimited = 0;
                    if (!login.IsSuccessStatusCode)
                    {
                        progress.Last = $"login answered {(int)login.StatusCode}";
                        return false;
                    }

                    loggedIn = true;
                }

                using var read = await http.GetAsync(new Uri(root, "/proxy/network/api/s/default/rest/portforward"), ct);
                progress.Last = $"legacy read answered {(int)read.StatusCode}";
                if (read.StatusCode == HttpStatusCode.Unauthorized)
                {
                    loggedIn = false;   // session gone (services restarting); log in again next poll
                }

                return read.StatusCode == HttpStatusCode.OK;
            }, ct);
        }

        private static async Task UntilAsync(StartupProgress progress, string step, Func<Task<bool>> ready, CancellationToken ct)
        {
            progress.Last = step;
            while (true)
            {
                try
                {
                    if (await ready())
                    {
                        return;
                    }
                }
                catch (HttpRequestException ex)
                {
                    // Not listening yet, or restarting a service — keep polling.
                    progress.Last = $"{step}: {ex.Message}";
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    // A single request timed out (HttpClient.Timeout), not the overall deadline.
                    progress.Last = $"{step}: request timed out";
                }

                await Task.Delay(Poll, ct);
            }
        }
    }
}

/// <summary>
/// Every legacy WRITE test class joins this collection: one container for the run, and the
/// tests in it run one at a time, so a test comparing "every other network" never sees
/// another test's scratch objects.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UnifiContainerCollection : ICollectionFixture<UnifiContainerFixture>
{
    public const string Name = "UniFi OS container";
}
