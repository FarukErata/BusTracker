# Relayed İBB connection (Turkey relay)

Branch: `feature/relayed-connection`. This document is the design and the handoff
notes for the feature. It is committed so another session (the main chat on the
other laptop) can pick the work up with full context. **Do not merge to `main`
or touch `main` until the server test below passes.**

Status: design frozen, API implementation in progress on this branch. See the
[checklist](#status-checklist) at the end.

## Problem

BusTracker runs on a Hetzner VPS in Germany. İBB / İETT web services
(`api.ibb.gov.tr`) are not reachable from a German IP, so every İBB call has to
leave from a Turkish IP.

This is **not** an attempt to get around the İBB rate limit. It stays one relay
with one IP, inside the existing 90-calls/hour budget. The design doc's rule
against "proxies to get around the limit" ([server-design.md](server-design.md),
"İBB rate limit strategy") is about multiplying IPs to dodge the quota; this is
the opposite — a single fixed Turkish exit so the calls work at all.

## The relay (host side)

A small always-on box at home in Turkey keeps an outbound SSH reverse tunnel to
the VPS, exposing a **SOCKS5** proxy on the VPS. The chosen box is a **Raspberry
Pi on ethernet**; the ready-to-run setup (systemd service, hardening, step by
step) is in [../deploy/relay-pi/](../deploy/relay-pi/). An Android phone with
Termux + autossh works too and was the original stop-gap.

```
autossh -M 0 -N -R 127.0.0.1:1080 \
  -o ServerAliveInterval=30 -o ServerAliveCountMax=3 \
  -o ExitOnForwardFailure=yes tunnel@<vps>
```

On the VPS this gives `socks5://127.0.0.1:1080`, whose traffic exits from the
Turkish home IP. Verify on the VPS:

```
curl -x socks5h://127.0.0.1:1080 https://ifconfig.me   # must print a Turkish IP
```

Because the app runs in Docker, the container must be able to reach the host's
tunnel. See [Deployment](#deployment-host-side) below.

## What goes through the relay, and what does not

| Client | Target host | Through relay? | Why |
|---|---|---|---|
| `IbbClient` (bus SOAP calls) | `api.ibb.gov.tr/iett` | **Yes** | The whole point — fleet, roster, timetable, announcements |
| `MetroClient` (rail) | `api.ibb.gov.tr/MetroIstanbul` | **Yes** | Same host as İETT; if İETT is geo-blocked from Germany, rail is too. Decided 2026-10-30 |
| `OpenDataClient` (route-shapes GeoJSON, 257 MB) | `data.ibb.gov.tr` | **Yes** | `data.ibb.gov.tr` is geo-blocked from Germany too (confirmed 2026-11-05: direct call times out). The daily check is a HEAD, so normal days cost almost nothing; the 257 MB download only runs when İBB changes the file (unchanged since 2026-03-18), and then it goes over the home uplink once |
| Everything else (map tiles, etc.) | various | **No (direct)** | Unaffected |

`data.ibb.gov.tr` was confirmed blocked from Germany on 2026-11-05, so
`OpenDataClient` now routes through the relay as well. The daily HEAD check is
cheap; the full 257 MB download only happens when the file actually changes.

## Configuration

A single optional key. Empty / unset = direct (the default; local dev and the
current production image are unaffected until the key is set).

| Key | Env var | Example | Default |
|---|---|---|---|
| `BusTracker:Ibb:Proxy` | `BusTracker__Ibb__Proxy` | `socks5://host.docker.internal:1080` | empty (direct) |

- `.NET`'s `SocketsHttpHandler` / `WebProxy` supports `socks5://` (and `socks5h://`)
  since .NET 6, so no extra package is needed on .NET 10.
- Set it only in the VPS `.env`. Never commit a real proxy address; dev stays empty.

## API changes (this branch)

File-by-file. All additive and gated on the proxy being configured, so with an
empty proxy the behaviour is byte-for-byte the same as today.

1. **`Configuration/BusTrackerOptions.cs`** — add to `IbbOptions`:
   ```csharp
   public string? Proxy { get; init; }   // empty/null = direct
   ```
   Validated as a URL only when non-empty.

2. **`Program.cs`** — when `Ibb.Proxy` is set, give `IbbClient` **and**
   `MetroClient` a primary handler that proxies:
   ```csharp
   static SocketsHttpHandler IbbHandler(string? proxy) =>
       string.IsNullOrWhiteSpace(proxy)
           ? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }
           : new SocketsHttpHandler
           {
               Proxy = new WebProxy(proxy),
               UseProxy = true,
               AutomaticDecompression = DecompressionMethods.All,
           };
   ```
   Wired via `.ConfigurePrimaryHttpMessageHandler(...)` on both clients.
   `OpenDataClient` also uses `IbbHandler` (data.ibb.gov.tr is blocked too).

3. **Relay state (`Ibb/IbbRelayState.cs`, new, singleton)** — records, for
   `/health`:
   - whether a proxy is configured (and the redacted proxy URL),
   - the time of the last İBB call that succeeded through the relay,
   - the time and short reason of the last proxy / connection error.
   Updated from the İBB success/failure paths (see `OutboundGate` /
   `FleetFeed` / `LineDataService`). It observes; it does not change control flow.

4. **`Endpoints/HealthEndpoints.cs`** — add a `Relay` block to the health
   response: `{ proxied, proxy, lastOkAt, lastErrorAt, lastError }`. It stays
   **distinct** from the existing `FleetBlockedUntil` (İBB rate-limit backoff)
   and `OutboundPausedUntil` (repeated timeouts). "Tunnel down" must read
   differently from "İBB rate-limited".

5. **Stale / backoff behaviour — verify, no logic change expected.** A proxy or
   tunnel failure surfaces as `HttpRequestException`, which is already caught in
   `FleetFeed.RefreshAsync` and `LineDataService` as "keep the previous snapshot,
   mark stale" — **not** as `IbbRateLimitedException`. So a dead tunnel does
   **not** trip the rate-limit backoff (`FleetCallGate.ReportRateLimited`). The
   `OutboundGate` independently pauses outbound calls after 3 failures, which is
   the correct "relay down" degradation. We only make this visible in `/health`;
   we do not change when backoff happens.

6. **Tests** — the repo currently has **no test project** (the `tests/` folder
   referenced in the README/design is not present in this checkout, and no CI
   workflow runs tests). Rather than add an unexercised, locally-unverifiable
   project, the relay is verified by the server `docker build` (compile) and the
   parallel-container end-to-end test below. If a test project is reinstated
   later, add: (a) the proxy handler is attached only when `Ibb.Proxy` is set,
   (b) a transport failure routes through stale handling and does **not** call
   `ReportRateLimited`.

## Deployment (host side)

The container runs on Docker's default bridge network with
`ports: 127.0.0.1:5080:8080`. The tunnel's SOCKS proxy is on the **host**
loopback, which is not the container's loopback, so a bridge is needed. Two
options from the setup notes:

### Option B — bridge gateway + `host.docker.internal` (recommended, used in production)

Repo change (this branch): on the `kackaldi` service in `deploy/compose.yaml`:
```yaml
network_mode: bridge              # put the container on docker0 (see gotcha 0)
extra_hosts:
  - "host.docker.internal:host-gateway"
```
Host config on the VPS (not in the repo):
- sshd: `GatewayPorts clientspecified`
- tunnel: `autossh … -R 172.17.0.1:1080` (bind to the bridge gateway)
- `.env`: `BusTracker__Ibb__Proxy=socks5://host.docker.internal:1080`
- **ufw: allow the Docker bridge to reach the tunnel port** — see the gotcha below.

Ports mapping, nginx and `TrustedProxyNetworks` all stay unchanged.

### Three deployment gotchas (all hit and fixed live on 2026-11-04)

These are non-obvious and cost real debugging time. None is in the app code.

0. **The container must be on the default `docker0` bridge.** A compose service
   gets its own project network (e.g. `kackaldi_default`, gateway `172.19.0.1`),
   but the tunnel's SOCKS proxy is bound to `172.17.0.1` (docker0) and the ufw
   rule below only allows docker0. On a project network, `host.docker.internal`
   resolves to the wrong gateway where nothing listens, so the relay silently
   falls back to a **direct** call that hangs. Fix: put the service on docker0 in
   `compose.yaml` (already set on this branch):
   ```yaml
   network_mode: bridge
   ```

1. **ufw blocks the container from reaching the tunnel.** With `ufw` active
   (default `deny incoming`), traffic from a container (via `docker0`) to the
   host's `172.17.0.1:1080` is **dropped** — even though the host itself reaches
   it fine. The symptom is nasty: .NET silently falls back to a **direct** call,
   which from Germany just hangs until the HttpClient timeout, with **no error**.
   `curl -x socks5h://172.17.0.1:1080 …` works from the host but times out from
   inside a container. Fix (persists across reboot):
   ```bash
   sudo ufw allow in on docker0 to any port 1080 proto tcp
   ```

2. **Stale reverse port after an ungraceful Pi disconnect.** If the Pi reboots or
   drops without closing the SSH session, the VPS sshd keeps `172.17.0.1:1080`
   bound to the dead session, and the Pi's new autossh loops on
   `Error: remote port forwarding failed for listen port 1080`. Make sshd detect
   and drop dead tunnel clients so the port frees itself (~90s). In
   `/etc/ssh/sshd_config.d/tunnel.conf` (alongside `GatewayPorts clientspecified`):
   ```
   ClientAliveInterval 30
   ClientAliveCountMax 3
   ```
   then `sudo systemctl restart ssh`. To clear a stuck one by hand:
   `sudo pkill -u tunnel` (frees the port; autossh rebinds).

A quick sanity check that proves the whole path before touching the app:
`docker run --rm curlimages/curl -s -x socks5h://172.17.0.1:1080 https://ifconfig.me`
from the VPS should print the **Turkish** relay IP. If that works from a
container, .NET will too.

### Option A — `network_mode: host` (more invasive, not recommended)

Set `network_mode: host`, remove the `ports:` mapping, make the app listen on
`127.0.0.1:5080` (`Urls`/`ASPNETCORE_URLS`), and re-check the
`TrustedProxyNetworks` assumption. Then `socks5://127.0.0.1:1080` works directly.
More moving parts; avoid unless Option B can't be made to work.

The app itself is indifferent: it only reads `BusTracker__Ibb__Proxy`. The
choice above is only about how the container reaches the tunnel.

## Testing on the server without merging to main

The release pipeline builds `ghcr.io/farukerata/bustracker:latest` only on push
to `main`, and the VPS `deploy/update.sh` auto-pulls `:latest` every 5 minutes.
So we must **not** push this branch to `main`, and must **not** run the
`Server image` workflow in a way that overwrites `:latest`.

**Recommended: a parallel test container built from this branch on the VPS,
on a spare port, with the real tunnel — production untouched.**

On the VPS, in a scratch checkout (not the deploy dir):
```bash
git fetch origin feature/relayed-connection
git checkout feature/relayed-connection
docker build --build-arg COMMIT="$(git rev-parse --short HEAD)" -t bustracker:relay-test .

mkdir -p /tmp/relay-test-data && sudo chown 1654:1654 /tmp/relay-test-data
docker run -d --name relay-test -p 127.0.0.1:5081:8080 \
  -v /tmp/relay-test-data:/data \
  --add-host host.docker.internal:host-gateway \
  -e BusTracker__Ibb__Proxy=socks5://host.docker.internal:1080 \
  -e BusTracker__Access__InviteCodes__0=relaytest \
  bustracker:relay-test
```
Checks:
```bash
curl -s http://127.0.0.1:5081/health | jq .          # relay block: proxied=true, lastOkAt recent, lastError null
# register a device with invite 'relaytest', open a hot line, confirm live buses appear
```
Then tear it down:
```bash
docker rm -f relay-test && docker rmi bustracker:relay-test && rm -rf /tmp/relay-test-data
```

Why this and not "merge main + image update":
- **No `main` change, no `:latest` overwrite, no auto-deploy.** The production
  `kackaldi` container keeps running the current image the whole time.
- The test container uses the **same** tunnel and the **same** İBB budget, so it
  is a true end-to-end test. (Note: it shares the 90/hour budget with production,
  so keep the test short.)
- If it fails, nothing to roll back.

Only after this passes: merge `feature/relayed-connection` → `main`, which builds
and publishes the image; the VPS picks it up within 5 minutes via `update.sh`;
the `.env` on the VPS must already carry `BusTracker__Ibb__Proxy` and the tunnel
must be up, or the fleet will just read stale (safe, but no live data).

### Alternative (not recommended): branch-tagged CI image

Edit `server-image.yml` to also build on this branch and push a **different**
tag (e.g. `:relay`), never `:latest`, then pull `:relay` into the test container.
Cleaner logs but edits the workflow and spends an Actions run; the parallel build
on the VPS avoids both.

## Budget note

İBB fleet limit is 100 req/hour per IP; our budget is 90/hour. All İBB traffic
now exits the one home IP, so **any dev script or local run at home shares the
same budget** as production. Traffic is small: ~1.5 req/min max, fleet calls
~1.1 MB each (~66 MB/hour while viewing), plus the ~35 MB daily static refresh.
Do not add more proxies or rotate IPs to widen the quota — one relay, one IP.

## Status checklist

- [x] Branch `feature/relayed-connection` created off `main`
- [x] CI confirmed: no workflow fires on this branch (server image = `main` only; android = tags/manual)
- [x] Design documented (this file)
- [x] `IbbOptions.Proxy` option added (+ `UsesProxy`), `appsettings.json` key `"Proxy": ""`
- [x] `Program.cs` proxy handler (`IbbHandler`) on `IbbClient` + `MetroClient` + `OpenDataClient` (data.ibb.gov.tr blocked too, 2026-11-05)
- [x] `IbbRelayState` added, wired through `OutboundGate`, surfaced as `/health` → `relay`
- [x] Stale-vs-rate-limit behaviour verified by review: proxy failure = `HttpRequestException` → stale path, not `ReportRateLimited`
- [x] `deploy/compose.yaml` `extra_hosts` (Option B) added
- [x] `docker build` compiles — built on the VPS (no .NET SDK/Docker on the dev laptop)
- [x] Server parallel-container test passes — validated live 2026-11-04: Pi relay up, container reaches the proxy, app pulled real data through it (`Loaded 18 rail lines and 248 stations`), `/health` → `relay.lastOkAt` populated, `lastError` null
- [x] Deployment gotchas found and fixed: `network_mode: bridge`, ufw `docker0`→1080 rule, sshd `ClientAlive`
- [x] Merged to `main`, production `.env` set, **live on 2026-11-04**: commit `c0437e2`, `relay.lastOkAt` populated, ~6,900 fleet vehicles tracked through the Pi relay

### For the main chat / next session

- All code is on `feature/relayed-connection`, committed. `main` is untouched.
- Local laptop has **no .NET SDK and no Docker**, so nothing was compiled here.
  First action on a machine with Docker: `docker build .` from the repo root to
  confirm it compiles (warnings are errors in `Directory.Build.props`).
- Then run the parallel-container test with the real tunnel before any merge.
- Files changed: `src/BusTracker.Api/Configuration/BusTrackerOptions.cs`,
  `src/BusTracker.Api/Ibb/IbbRelayState.cs` (new),
  `src/BusTracker.Api/Ibb/OutboundGate.cs`,
  `src/BusTracker.Api/Program.cs`,
  `src/BusTracker.Api/Endpoints/HealthEndpoints.cs`,
  `src/BusTracker.Api/appsettings.json`, `deploy/compose.yaml`, this doc.
