# UnifiSharp

[![NuGet](https://img.shields.io/nuget/v/Chrison.UnifiSharp.svg)](https://www.nuget.org/packages/Chrison.UnifiSharp/)
[![Downloads](https://img.shields.io/nuget/dt/Chrison.UnifiSharp.svg)](https://www.nuget.org/packages/Chrison.UnifiSharp/)
[![ci](https://github.com/Chrison-dev/UnifiSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/Chrison-dev/UnifiSharp/actions/workflows/ci.yml)
[![built with Fallout](https://img.shields.io/badge/built%20with-Fallout-F5C800?logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI%2BPGNpcmNsZSBjeD0iMTIiIGN5PSIxMiIgcj0iMTIiIGZpbGw9IiNGNUM4MDAiLz48ZyBmaWxsPSIjMTExIj48Y2lyY2xlIGN4PSIxMiIgY3k9IjEyIiByPSIzLjEiLz48cGF0aCBkPSJNOS45OSA5LjAyQTMuNiAzLjYgMCAwIDEgMTQuMDEgOS4wMkwxOC40OSAyLjM4QTExLjYgMTEuNiAwIDAgMCA1LjUxIDIuMzhaTTE1LjU5IDExLjc1QTMuNiAzLjYgMCAwIDEgMTMuNTggMTUuMjRMMTcuMDkgMjIuNDNBMTEuNiAxMS42IDAgMCAwIDIzLjU3IDExLjE5Wk0xMC40MiAxNS4yNEEzLjYgMy42IDAgMCAxIDguNDEgMTEuNzVMMC40MyAxMS4xOUExMS42IDExLjYgMCAwIDAgNi45MSAyMi40M1oiLz48L2c%2BPC9zdmc%2B)](https://github.com/Fallout-build/Fallout)
[![License: MIT](https://img.shields.io/github/license/Chrison-dev/UnifiSharp.svg)](LICENSE)

A C# client for the **UniFi Network API** — **mostly code-generated** from
Ubiquiti's official OpenAPI spec, with a thin hand-written runtime for auth and
transport. Sibling to [ProxmoxSharp](https://github.com/Chrison-dev/ProxmoxSharp);
built to bring the UniFi-managed network under the homelab's C#-native IaC. See
[ADR-0003](https://github.com/Chrison-Homelab/Homelab/blob/main/docs/adr/ADR-0003-unifisharp.md).

```sh
dotnet add package Chrison.UnifiSharp
```

## Approach

```mermaid
flowchart LR
  SPEC["📜 UniFi OpenAPI 3.1<br/>(console / beezly mirror)"] --> KIOTA["⚙️ Kiota (pinned tool)<br/>generate C# client"]
  KIOTA --> API["📦 UnifiSharp.Api<br/>generated · tracks UniFi release"]
  API --> RT["✍️ UnifiSharp<br/>hand-written runtime (X-API-KEY)"]
  RT --> LEG["🧩 UnifiSharp.Legacy<br/>write adapter · API-key or session<br/>(port-forwards · firewall · networks<br/>clients · static DNS)"]
  classDef gen fill:#e0e7ff,stroke:#4f46e5;
  class API gen;
```

The UniFi spec is already OpenAPI 3.1, so — unlike ProxmoxSharp — there's **no
converter**; Kiota consumes it directly. The generated client is **regenerated
on build** (only when the spec changes) and **not committed**.

> **Write coverage caveat (per ADR-0003):** the official API is read-mostly today
> (firewall rules / port profiles not yet exposed; full write rolling out through
> 2026). Those will be filled by a thin, deletable **legacy adapter** later. This
> repo starts **read-only**.

## Projects & versioning

| Project | What | Version |
| --- | --- | --- |
| `src/UnifiSharp.Api/` | Kiota-generated client. `Generated/` produced on build (gitignored). | **Tracks the UniFi Network API release** (e.g. `10.4.57`). |
| `src/UnifiSharp/` | Hand-written runtime (`UnifiApi`, X-API-KEY auth, options) over `.Api`. | **Independent SemVer** (`0.1.0`). |
| `tests/UnifiSharp.Tests/` | Unit tests. | — |

## Build

Built with [Fallout](https://github.com/Fallout-build/Fallout) (Chris's C#/.NET
build system, a NUKE successor). Targets: `Compile` → `Test` → `Pack` → `Publish`.

```bash
./build.sh              # default: Test (Compile regenerates UnifiSharp.Api from the spec, then compiles)
./build.sh Pack         # produce the Chrison.* nupkgs into artifacts/
```

Requires the .NET 10 SDK (see `global.json`); everything, including `Fallout.*`,
restores from nuget.org with no credentials. CI runs `./build.sh Test` on push/PR.

## Use it

```csharp
var client = UnifiApi.Create(new UnifiClientOptions
{
    // NOTE: no /v1 — the generated client appends the version segment itself.
    // UniFi OS (Cloud Gateway/UDM/UniFi OS Server):  https://<host>/proxy/network/integration
    // Standalone Network Application:                 https://<host>:8443/integration
    BaseUrl = new Uri("https://192.168.1.1/proxy/network/integration"),
    ApiKey  = "<local API key — Settings → Integrations>",
    VerifyTls = false,   // self-signed LAN cert
});
// read endpoints: sites, networks, WLANs, firewall zones, devices, clients …
```

> **Base-URL note (verified 2026-05-31 against the `.containers/unifi` UniFi OS
> Server container):** set `BaseUrl` to the integration root **without** `/v1` —
> the client adds `/v1/…`. UniFi OS exposes it under `/proxy/network/integration`;
> a standalone Network Application omits that prefix (`/integration`).

## Packages

Published to **nuget.org** (public) under the `Chrison.*` prefix, via **Trusted
Publishing** (OIDC — no stored API key): `Chrison.UnifiSharp`, `Chrison.UnifiSharp.Api`,
`Chrison.UnifiSharp.Cli`. Prerelease on push to `main` (`…-preview.N`), stable on a
`v*` tag. `.Api` tracks the UniFi API release; the library its own SemVer. IDs use the
`Chrison.*` prefix because the bare `UnifiSharp` ID is taken on nuget.org by an unrelated
project; assembly names and namespaces stay `UnifiSharp`, so `using UnifiSharp;` is unchanged.

## Refresh the spec (new UniFi release)

Pull the OpenAPI for the controller's version from the console (Settings →
Integrations) or the [`beezly/unifi-apis`](https://github.com/beezly/unifi-apis)
mirror into `src/UnifiSharp.Api/schema/`, update the filename + `VersionPrefix`,
rebuild.

## CLI (`unifisharp`)

A `dotnet` global tool over the library:

```bash
export UNIFI_BASE_URL="https://localhost:8443/proxy/network/integration/v1"
export UNIFI_API_KEY="…"  UNIFI_VERIFY_TLS=false
unifisharp sites       # list sites
unifisharp discover    # JSON snapshot: sites + networks/WLANs/firewall/devices/clients
unifisharp networks    # networks/VLANs per site (name, vlan id, purpose, enabled)
unifisharp wlans       # WLANs/SSIDs per site (ssid, enabled, security)
unifisharp firewall    # firewall zones, policies, and ACL rules per site
unifisharp devices     # adopted devices per site (name, model, ip, mac, firmware, state)
unifisharp clients     # connected clients per site (name, type, ip, connectedAt)
```

## Legacy adapter auth

`UnifiLegacyOptions` takes either credential, and `TryFromEnvironment()` prefers the key:

| Mode | Set | Notes |
|---|---|---|
| **API key** | `UNIFI_API_KEY` + (`UNIFI_LEGACY_BASE_URL` or `UNIFI_LOCAL_HOST`) | `X-API-KEY` on every request — no login, cookie or CSRF token. Preferred against a real gateway; the same key the integration API uses. |
| **Session** | `UNIFI_USERNAME` + `UNIFI_PASSWORD` + base URL | `POST /api/auth/login`. The only mode the `.containers/unifi` test container supports, since it can't mint API keys. |

With `UNIFI_LOCAL_HOST` alone the site URL is derived as
`https://<host>/proxy/network/api/s/default`.

> The **destructive** live tests never read `UNIFI_*`; they run only against a throwaway
> container the tests start themselves (below). Read-only live checks
> (`UnifiLegacyReadOnlyLiveTests`) take their controller from the environment, in either mode.

### Write tests run against their own container

`UnifiLegacyLiveTests` (create and delete a port-forward, a firewall group and a VLAN) and
`UnifiLegacyNetworkRawLiveTests` (a raw DHCP-DNS write on a scratch network, then a
byte-for-byte restore) share `UnifiContainerFixture`: Testcontainers starts a UniFi OS
Server (`hieutq/unifi-os-server:5.0.8`, pinned by digest, with the settings of the Homelab
repo's `.containers/unifi/compose.yml`), runs first-run setup, logs in once, and removes the
container when the run ends. The URL comes from the container's mapped port, so these tests
cannot reach a real console whatever the environment says.

```sh
dotnet test                                  # starts the container when a runtime is reachable
UNIFISHARP_SKIP_CONTAINER_TESTS=1 dotnet test   # skip them on purpose (1/true/yes)
UNIFISHARP_CONTAINER_STARTUP_MINUTES=40 dotnet test   # slow link (default 20)
```

- **Needs** a Docker-compatible runtime that allows `cgroup: host`, the `SYS_ADMIN` set of
  capabilities and a read-write `/sys/fs/cgroup` bind (OrbStack and Docker Desktop do).
  With no runtime reachable the tests **skip** and say why; any other startup failure fails.
- **Local daemon only:** a `DOCKER_HOST` that is not a local socket or loopback address, or a
  `TESTCONTAINERS_HOST_OVERRIDE` / `tc.host` that is not loopback, **fails** the tests rather
  than start a `SYS_ADMIN` container elsewhere and send writes to it.
- **The image is pinned** (Testcontainers only pulls a missing image, so a floating tag would
  keep whatever was pulled first). Bump the tag and digest deliberately.
- **First boot is slow:** every fresh container downloads Ubiquiti's installer (minutes).
- It gets its own name (`unifisharp-tests-…`), a random host port and only port 443, so it
  runs beside a manually bootstrapped `unifi-os-server` container without touching it.
- Testcontainers finds the runtime through `DOCKER_HOST`, `~/.testcontainers.properties` or
  the Docker context. A stale `tc.host` / `docker.host` in that properties file (say, from
  an uninstalled Testcontainers Desktop) makes the tests skip;
  `DOCKER_HOST=unix://$HOME/.orbstack/run/docker.sock` overrides it for OrbStack.
- **CI is untested.** A runner needs a privileged-enough Docker daemon and network access to
  Ubiquiti's download servers; budget for the installer download on every run.

### Two surfaces, two sets of rules

The adapter spans the legacy site API **and** the v2 site API, and they do not behave
alike. Assuming otherwise fails only at runtime, against a real controller:

| | legacy `…/api/s/<site>` | v2 `…/v2/api/site/<site>` |
|---|---|---|
| response | `{ "meta": {...}, "data": [...] }` | bare JSON array / object |
| update | **partial** — send only changed fields | **full replacement** — a partial PUT is `400 Validation failed` |
| covers | port-forwards, firewall groups, networks, clients | static DNS |

That is why `UnifiStaticDnsRecord` is non-nullable throughout while the legacy DTOs are
nullable: on v2 a partial is not something you can send, so the type should not suggest it.
Static-DNS **wildcards are supported** and match arbitrary labels
(`*.lab.example.com` answers `anything.lab.example.com`).

**Networks are the exception on the legacy side.** `UnifiNetwork` models about a dozen of
a live network's ~63 fields, so there is no typed network update. Change a network as raw
JSON instead: read it, change named keys, put the whole object back.

```csharp
var net = await legacy.GetNetworkRawAsync(id);                      // throws unless exactly one row
var dns = NetworkDhcpDns.Read(net);                                  // null = auto
await legacy.ReplaceNetworkRawAsync(id, NetworkDhcpDns.With(net, ["10.20.0.53", "10.20.0.54"]));
```

**Custom NAT rules** (DNAT, MASQUERADE, SNAT) are on the v2 side too (`…/v2/api/site/<site>/nat`),
so the update is a full replacement. A rule has no name, so `UnifiNatRuleSpec.Name` is its
`description`, and callers reconcile by it. `in_interface` / `out_interface` are network
`_id`s; the spec takes network names and `GetNatNamesAsync` resolves them. The controller
rejects a duplicate `rule_index`, so `CreateNatRuleAsync` takes the next free one unless the spec
declares it. `UpdateNatRuleAsync` and `DeleteNatRuleAsync` refuse predefined rules and ids that
aren't on the controller. A filter matches one address, range or CIDR, with `InvertAddress` for
"everything except". The shape comes from the UCG (Network 10.6.106); a throwaway container has
no gateway and takes no NAT rule, so writes are unit-tested rather than container-tested.

```csharp
var names = await legacy.GetNatNamesAsync();
await legacy.CreateNatRuleAsync(new UnifiNatRuleSpec
{
    Name = "consumer-dns-redirect", Type = UnifiNatType.Dnat, Protocol = "tcp_udp",
    InNetwork = "Consumer", TranslatedAddress = "10.20.0.53", TranslatedPort = "53",
    Source = new() { Address = "10.20.0.53-10.20.0.54", InvertAddress = true },
    Destination = new() { Address = "10.20.0.53-10.20.0.54", Port = "53", InvertAddress = true },
}, names);
```

## Firewall policies (`UnifiSharp.Firewall`)

Zone-based firewall policies (UniFi Network 9+) go through the **official** integration API,
`{UNIFI_BASE_URL}/v1/sites/{site}/firewall/policies`, which has full CRUD. They don't use the
legacy adapter.

```csharp
using var fw = new UnifiFirewallClient(UnifiClientOptions.TryFromEnvironment()!);
var names = await fw.GetNamesAsync();                 // zone + network name → id
var spec = new FirewallPolicySpec
{
    Name = "qbittorrent-peers-v6",
    Source = new() { Zone = "External" },
    Destination = new() { Zone = "Homelab", Ipv6InterfaceId = "::6342:9", Ports = ["63429"] },
    IpVersion = FirewallIpVersion.IPv6,
    Protocol = "tcp_udp",
};
await fw.CreateAsync(spec, names);
```

- **`FirewallPolicySpec`** is the supported subset: zones, addresses (IP, CIDR, `a-b` range), networks, an IPv6 interface identifier, ports, IP version, protocol, action, logging and enabled.
- **Equality is semantic**, so `live == desired` is the drift check.
- **`FirewallPolicyJson.FromJson`** returns a reason instead of a spec for anything outside the subset: domain/app/region filters, schedules, matching lists and the like. A caller can never mistake "can't compare" for "matches".
- **`Ipv6InterfaceId`** is written with the `/::ffff:ffff:ffff:ffff` mask, so it matches the host suffix under *any* delegated prefix. Pinning a full IPv6 address breaks the day the ISP re-delegates.
  ⚠ **The controller accepts this but the gateway may not enforce it.** On a UCG running UniFi OS 5.1.33 / Network 10.6.106, an External → LAN allow matched by IID was stored correctly (`matching_target: IID`) and then matched **nothing**: the hit counter froze and inbound traffic was dropped. Check the policy's hit counter after switching a rule to an IID match, and fall back to the full address if it stays flat.
- **Updates are PUT (full replacement).** Only ever write `USER_DEFINED` policies (`LiveFirewallPolicy.IsUserDefined`).
- **Ordering is not managed.** New user policies land at index 10000, ahead of the predefined allow/block defaults.
- Generated against the 10.4.57 spec and checked live against Network 10.6.106.

## Status

Read client + `discover` + `unifisharp` CLI building from the 10.4.57 spec.
Live read tests skip without `UNIFI_*` — run them against the
[`.containers/unifi`](https://github.com/Chrison-dev/Homelab/tree/main/.containers/unifi)
test controller (never the live network). Legacy write tests start their own container. **Next:** the legacy write adapter
(firewall rules / port profiles) once verified against the container.
