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
/// real console.</para>
/// <para>Skips (with the reason in <see cref="SkipReason"/>) when no container runtime is
/// reachable or <c>UNIFISHARP_SKIP_CONTAINER_TESTS=1</c>. Any other startup failure —
/// a timeout, a setup that never completes — is a failure, not a skip.</para>
/// </summary>
public sealed class UnifiContainerFixture : IAsyncLifetime
{
    public const string Image = "hieutq/unifi-os-server:latest";
    public const string Username = "labadmin";
    public const string Password = "LabAdmin!2026";   // 8-64 chars (UniFi OS policy); throwaway, container-only
    public const string Label = "dev.chrison.unifisharp.tests";

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
        if (Environment.GetEnvironmentVariable("UNIFISHARP_SKIP_CONTAINER_TESTS") == "1")
        {
            SkipReason = "UNIFISHARP_SKIP_CONTAINER_TESTS=1 — skipping the UniFi container write tests.";
            return;
        }

        var timeout = StartupTimeout();
        using var cts = new CancellationTokenSource(timeout);
        var stopwatch = Stopwatch.StartNew();

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
            await _container.StartAsync(cts.Token);
        }
        catch (DockerUnavailableException ex)
        {
            // Thrown before anything is created, from Build() or StartAsync().
            if (_container is not null)
            {
                await _container.DisposeAsync();
                _container = null;
            }

            SkipReason = $"No container runtime reachable — skipping the UniFi container write tests. ({ex.Message.Split('\n')[0]})";
            return;
        }

        try
        {
            var root = new UriBuilder("https", _container.Hostname, _container.GetMappedPublicPort(443)).Uri;
            await Bootstrap.RunAsync(root, cts.Token);

            var session = new UnifiLegacySession(new UnifiLegacyOptions
            {
                BaseUrl = new Uri(root, "/proxy/network/api/s/default"),
                Username = Username,
                Password = Password,
                VerifyTls = false,   // the container serves a self-signed cert
            });
            _client = new UnifiLegacyClient(session, ownsSession: true);

            // One login for the whole run, so the tests never add to the login rate limit.
            await Bootstrap.WithLoginBackoffAsync(() => session.LoginAsync(cts.Token), cts.Token);
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
                    $"UniFi OS container {name} was not ready within {timeout} " +
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

    /// <summary>
    /// bootstrap.sh's steps 1-3, against one controller root: wait for UniFi OS, run first-run
    /// setup, then wait until a legacy-API read answers 200.
    /// </summary>
    private static class Bootstrap
    {
        private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

        public static async Task RunAsync(Uri root, CancellationToken ct)
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                // Throwaway container, self-signed cert.
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                UseCookies = false,
            })
            { Timeout = TimeSpan.FromSeconds(15) };

            // 1) UniFi OS answers /api/system with a deviceState once it is up.
            await UntilAsync(async () =>
            {
                var body = await http.GetStringAsync(new Uri(root, "/api/system"), ct);
                return body.Contains("\"deviceState\"", StringComparison.Ordinal);
            }, ct);

            // 2) First-run setup. A fresh container is never set up, but the setup endpoint
            //    answers 400/500 to an empty body only once it accepts input; until then it
            //    is not ready.
            await UntilAsync(async () =>
            {
                using var probe = await http.PostAsJsonAsync(new Uri(root, "/api/setup"), new { }, ct);
                return probe.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError;
            }, ct);
            try
            {
                using var _ = await http.PostAsJsonAsync(new Uri(root, "/api/setup"),
                    new { name = "UnifiSharp test controller", username = Username, password = Password }, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException
                || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                // The connection often resets, or hangs until HttpClient.Timeout, while services
                // restart after setup — expected.
                // Whether setup took is decided by the login below, not by this response.
            }

            // 3) The admin can log in and the legacy API answers. Its own token, not the
            //    shared session's: this is a readiness probe, retried until it holds.
            await UntilAsync(async () =>
            {
                using var login = await http.PostAsJsonAsync(new Uri(root, "/api/auth/login"),
                    new { username = Username, password = Password }, ct);
                if (login.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return false;
                }

                if (!login.IsSuccessStatusCode)
                {
                    return false;
                }

                using var read = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "/proxy/network/api/s/default/rest/portforward"));
                foreach (var cookie in login.Headers.TryGetValues("Set-Cookie", out var c) ? c : [])
                {
                    read.Headers.TryAddWithoutValidation("Cookie", cookie.Split(';')[0]);
                }

                using var resp = await http.SendAsync(read, ct);
                return resp.StatusCode == HttpStatusCode.OK;
            }, ct);
        }

        /// <summary>
        /// Retry a session login while the controller answers 429. The library surfaces a
        /// failed login as <see cref="UnifiLegacyException"/> carrying the status in its
        /// message ("login failed (429): …"); anything other than 429 is rethrown.
        /// </summary>
        public static async Task WithLoginBackoffAsync(Func<Task> login, CancellationToken ct)
        {
            for (var delay = TimeSpan.FromSeconds(15); ; delay *= 2)
            {
                try
                {
                    await login();
                    return;
                }
                catch (UnifiLegacyException ex) when (ex.Message.StartsWith("login failed (429)", StringComparison.Ordinal))
                {
                    await Task.Delay(delay, ct);
                }
            }
        }

        private static async Task UntilAsync(Func<Task<bool>> ready, CancellationToken ct)
        {
            while (true)
            {
                try
                {
                    if (await ready())
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet, or restarting a service — keep polling.
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    // A single request timed out (HttpClient.Timeout), not the overall deadline.
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
