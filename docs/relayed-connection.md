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

## The relay (host side, not in this repo)

An Android phone at home in Turkey (Termux + autossh) keeps an outbound SSH
reverse tunnel to the VPS, exposing a **SOCKS5** proxy on the VPS. It may later
be replaced by a Raspberry Pi with the same tunnel.

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
| `OpenDataClient` (route-shapes GeoJSON, 257 MB) | `data.ibb.gov.tr` | **No (direct)** | Different host (open-data portal). Keep it off the home connection unless it turns out to be blocked too. Large daily download should not burden the home uplink |
| Everything else (map tiles, etc.) | various | **No (direct)** | Unaffected |

If `data.ibb.gov.tr` turns out to be blocked from Germany as well, revisit and
route `OpenDataClient` through the relay too — but that is 257 MB over the home
line, so prefer a direct path or a one-off manual seed.

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
   `OpenDataClient` is left exactly as-is (direct).

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

6. **Tests (`tests/BusTracker.Tests`)** — add:
   - proxy handler is attached only when `Ibb.Proxy` is set,
   - a transport failure routes through stale handling and does **not** call
     `ReportRateLimited`.

## Deployment (host side)

The container runs on Docker's default bridge network with
`ports: 127.0.0.1:5080:8080`. The tunnel's SOCKS proxy is on the **host**
loopback, which is not the container's loopback, so a bridge is needed. Two
options from the setup notes:

### Option B — bridge gateway + `host.docker.internal` (recommended, least invasive)

Repo change (this branch): add two lines to `deploy/compose.yaml`:
```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```
Host config on the VPS (not in the repo):
- sshd: `GatewayPorts clientspecified`
- tunnel: `autossh … -R 172.17.0.1:1080` (bind to the bridge gateway)
- `.env`: `BusTracker__Ibb__Proxy=socks5://host.docker.internal:1080`

Ports mapping, nginx and `TrustedProxyNetworks` all stay unchanged.

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
- [ ] `IbbOptions.Proxy` option added
- [ ] `Program.cs` proxy handler on `IbbClient` + `MetroClient`
- [ ] `IbbRelayState` + `/health` relay block
- [ ] Stale-vs-rate-limit behaviour verified
- [ ] Tests added
- [ ] `docker build` compiles (no local .NET SDK on the dev laptop; build via Docker/CI)
- [ ] `deploy/compose.yaml` `extra_hosts` (Option B) — optional, host side
- [ ] Server parallel-container test passes
- [ ] (Separate, deliberate) merge to `main`
