# UnifiSharp

A C# client for the **UniFi Network API** — **mostly code-generated** from
Ubiquiti's official OpenAPI spec, with a thin hand-written runtime for auth and
transport. Sibling to [ProxmoxSharp](https://github.com/ChrisonSimtian/ProxmoxSharp);
built to bring the UniFi-managed network under the homelab's C#-native IaC. See
[ADR-0003](https://github.com/ChrisonSimtian/Homelab/blob/main/docs/adr/ADR-0003-unifisharp.md).

## Approach

```
UniFi Network OpenAPI spec (console Settings → Integrations / beezly mirror)
   │  Kiota (pinned dotnet tool)   → generated C# client  (UnifiSharp.Api)
   ▼
UnifiSharp                         = hand-written runtime over it (X-API-KEY auth)
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

```bash
dotnet tool restore     # restore Kiota (the build invokes it to regenerate)
dotnet build            # regenerates UnifiSharp.Api from the spec if it changed, then compiles
dotnet test
```

## Use it

```csharp
var client = UnifiApi.Create(new UnifiClientOptions
{
    BaseUrl = new Uri("https://192.168.1.1/proxy/network/integration/v1"),
    ApiKey  = "<local API key — Settings → Integrations>",
    VerifyTls = false,   // UniFi OS consoles often use a self-signed LAN cert
});
// read endpoints: sites, networks, WLANs, firewall zones, devices, clients …
```

## Packages

Published to GitHub Packages like ProxmoxSharp: prerelease on push to `main`
(`…-preview.N`), stable on a `v*` tag. `UnifiSharp.Api` tracks the UniFi API
release; `UnifiSharp` its own SemVer.

## Refresh the spec (new UniFi release)

Pull the OpenAPI for the controller's version from the console (Settings →
Integrations) or the [`beezly/unifi-apis`](https://github.com/beezly/unifi-apis)
mirror into `src/UnifiSharp.Api/schema/`, update the filename + `VersionPrefix`,
rebuild.

## Status

Scaffold + read client building from the 10.4.57 spec (455 generated files).
**Next:** `discover` + a CLI (`unifisharp` dotnet tool, replaces the MCP),
then the legacy write adapter. Tests against a Dockerised controller
(`lscr.io/linuxserver/unifi-network-application`) — never the live network.
