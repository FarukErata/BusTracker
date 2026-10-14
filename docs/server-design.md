# BusTracker server — design (v1 draft)

Based on the tested API behaviour in [api-endpoints.md](api-endpoints.md).

## Goals and constraints

- Tell me, for my favourite line + stop pairs, **how many stops away** the next buses are, and **notify** me when one is close or has passed.
- Runs on a small shared server, so it has to stay light: low idle memory and near-zero CPU.
- Must stay well inside İBB's rate limit on the fleet service (tripped at ~80 calls / ~4 min from one IP).
- Each bus position updates about **once every 60 s**. Polling faster gets nothing.
- Notification channel is still open; the server has to support it being swapped later.

## Stack

| Choice | Why |
|---|---|
| **.NET 10 (LTS), ASP.NET Core minimal API** | One process for the API and the background poller. LTS until Nov 2028 |
| **SQLite** via `Microsoft.Data.Sqlite` + **Dapper** | No DB server to run, one file to back up. Dapper is far lighter than EF Core for ~6 tables |
| `HttpClient` + hand-written SOAP envelopes | Four methods, fixed shapes. No WCF / connected-service code (IBB.Api not used) |
| `BackgroundService` + `PeriodicTimer` | The poller and the daily static-data refresh, without a job library |
| Workstation GC, non-concurrent | `<ServerGarbageCollector>false</ServerGarbageCollector>`, `<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>` → much lower idle memory than the server GC |
| xUnit tests on the tracking engine | Fed from the saved samples in `docs/api-samples/`, so tests never hit İBB |

Native AOT is not planned for v1. It would save some memory but complicates Dapper and reflection-based JSON; we can revisit it if memory turns out tight.

## Architecture

```
                 ┌──────────────────────── BusTracker.Api (one process) ─────────────────────────┐
 phone / web ──► │ Minimal API endpoints ──► LiveCache (in-memory, per line) ◄── LinePoller       │
                 │        │                       ▲                              (BackgroundService)
                 │        ▼                       │                                   │           │
                 │   SQLite (favourites,     TrackingEngine ──► AlertEvaluator ──► INotifier     │──► ntfy / FCM / Web Push / Telegram
                 │   watches, alerts, cache)  (pure, tested)                         (pluggable)  │
                 │        ▲                                                          │           │
                 │   StaticDataRefresher (daily) ──────────► İBB ibb.asmx / HatDurakGuzergah     │
                 └──────────────────────────────────────────────────────────────────┼───────────┘
                                                                                    ▼
                                                               İBB SeferGerceklesme.asmx (rate-limited)
```

| Component | Responsibility |
|---|---|
| `IbbClient` | One method per SOAP call. Parses the JSON-in-XML and DataSet answers into typed records, converts the string numbers and the `+03:00` timestamps. Turns `Rate limit exceeded` into a typed `IbbRateLimitedException` |
| `RouteShapeSync` | Real road geometry for every route variant, from İBB's open-data GeoJSON (257 MB). Once a day a `HEAD` request compares the file's `ETag` with the stored one; the server downloads only when it changed or the database is empty (the portal has no version number and ignores conditional GET). It reads the file one feature at a time, simplifies each line to within 5 m (5.9 M → 1.2 M points), and stores it in SQLite as an encoded polyline (4.3 MB). A truncated download, or a file with fewer than half the previous shapes, is rejected and the old shapes stay |
| `StaticDataRefresher` | Once a day (and on startup if the cache is older than 24 h) loads all lines (`HatServisi_GYY`, 186 KB), all stops (`GetDurak_json`, 3.7 MB) and the stop order **for lines in use only** (`DurakDetay_GYY_wYonAdi` per line). The all-lines stop-order call is 28.5 MB / 15 s, so we skip it |
| `LinePoller` | Decides which lines are "hot", polls each at most once per 60 s, and respects a global call budget (see below) |
| `TrackingEngine` | Pure functions: given a line's stop list and two consecutive snapshots, compute each bus's position along the route, stops away from a target stop, and "passed" events |
| `AlertEvaluator` | Matches engine output against active watches, de-duplicates, and hands messages to `INotifier` |
| `LiveCache` | Latest computed state per hot line, served to clients without any İBB call |

## Polling: rate-limit-safe scheduling

A line is **hot** when either:
1. a watch on it is inside its time window right now, or
2. a client requested its live view in the last 5 minutes.

Rules:
- Each hot line is polled **once per 60 s**. Calls are spread evenly across the minute (line *i* of *n* at second `i × 60 / n`) instead of bursting.
- **Global budget: 10 calls/min** to `SeferGerceklesme.asmx` (≈ half the rate that tripped the limit). With more than 10 hot lines, the interval stretches (12 lines → every 72 s), and watched lines take priority over viewed-only lines.
- On `Rate limit exceeded` → pause all fleet calls for 2 min, then 4, then 8 (max 15). Reset after 10 clean calls. The current backoff state shows up in `/health`.
- No hot lines → no calls at all.
- On `IbbRateLimitedException` or network error, `LiveCache` keeps the last snapshot and marks it stale, so clients can show "last updated 3 min ago".

## Tracking engine

Input per line: the stop list for each direction (from the cache) and the latest `GetHatOtoKonum_json` snapshot.

Per bus:
1. **Direction** from `guzergahkodu` (`500T_G_D0` → `G`). **Variant** = the last part. `D0` is the main route.
2. **Discard** if `son_konum_zamani` is older than 3 min (stale), or the bus is more than 500 m from every stop on its direction (garage run / GPS fault).
3. **Position along the route**: project the GPS point onto the real road geometry of the bus's own variant (`route_shapes`, keyed by `guzergahkodu`), and project the stops onto the same line once. Fall back to straight stop-to-stop segments only if a variant has no geometry. Result: a fractional index like `13.4` = 40% of the way from stop 13 to stop 14. This fixes the ±1 error of "nearest stop", and it's how we know the bus has *passed* a stop, not just "is near" it.
4. **Stops away** from target stop *t*: `ceil(t − position)`. Negative = already passed.
5. **Trip continuity**: a bus whose position jumps backwards by > 3 stops, or changes direction, has started a new trip. It gets a new trip key `(kapino, direction, start time)`, which keeps alerts from repeating.
6. **Alternate variants** (not `D0`) whose nearest stop isn't on the main list are shown as "alternate route" and never counted for alerts in v1.
7. Position ≤ 0.2 on its direction = **waiting at the terminal**, not departed.

**Implemented (2026-10-14)** in `src/BusTracker.Api/Tracking/`: `RouteGeometry` (projection in metres), `RouteTrack`/`LineTracks` (stops placed on each variant's road line), `LineTracker` (state across polls: passed stops, trip id, observed speed), and `Arrivals` (stops away, distance and ETA per stop). Exposed as `GET /api/lines/{code}/live` and `GET /api/lines/{code}/arrivals?stop=&dir=`.

**ETA v1:** remaining road distance ÷ (60% observed speed over the last ~6 min + 40% scheduled speed from the variant's `UZUNLUK`/`SURE`), with a minimum of 2 m/s. Buses waiting at the first stop get no ETA ("not departed").

Optional for v1.1: **ETA** from the median speed between snapshots along the route, falling back to `SEFER_SURESI / stop count`.

## Data model (SQLite)

```sql
users           (id, name, api_key_hash, created_at)                                -- me + maybe 1–2 friends
favourites      (id, user_id → users, line_code, direction, stop_code, label, sort_order, created_at)
watches         (id, favourite_id → favourites, days_mask, start_time, end_time,      -- e.g. Mon–Fri 07:30–08:15
                 notify_stops_before INTEGER,                                         -- e.g. 3
                 notify_on_passed BOOLEAN, enabled, created_at)
notify_targets  (id, user_id → users, channel, address, enabled, created_at)                           -- channel: ntfy | fcm | webpush | telegram
alerts_sent     (id, watch_id, trip_key, stage, sent_at)                              -- stage: approaching | passed; unique(watch_id, trip_key, stage)

route_shapes    (route_code PK, line_code, direction, variant, length_m, duration_s, point_count, polyline)
data_sources    (name PK, etag, last_modified, size, checked_at, imported_at, item_count)        -- change detection per source
lines_cache     (line_code PK, name, full_name, status, region, trip_minutes, fetched_at)
stops_cache     (stop_code PK, name, lat, lng, district, direction_label, fetched_at)
route_stops     (line_code, direction, seq, stop_code, PRIMARY KEY (line_code, direction, seq), fetched_at)
```

Personal, with room for 1–2 friends: a tiny `users` table from day one. Each user has one API key (stored hashed, created with a CLI command, no sign-up flow). Favourites, watches and notify targets belong to a user. Live data and the poller are shared, so two people watching the same line cost one İBB call.

## Users and devices (implemented 2026-10-14)

No login or registration form; one model for web and Android.

- **Anonymous user + per-device key.** `POST /api/devices {platform, name?, invite?, linkCode?}` creates a user and returns a random device key (shown once, stored hashed). Every client sends `Authorization: Bearer <key>`: browser storage on web, encrypted storage on Android.
- **Invite codes** (`BusTracker:Access:InviteCodes`) are required to create a *new* user. Empty list = open; that's fine on a dev PC, and the server logs a warning when it starts in production that way. Share `https://…/?invite=CODE` once.
- **Link codes** (`POST /api/me/link-codes` → e.g. `XRE-D3Z`, 10 min, single use) add another device to the same user without an invite. They're also the recovery path.
- Registration is rate-limited per IP (10 per 10 min). All other `/api` routes need a device key; `/health` is public.
- Devices are separate records, so each can later carry its own notification target (FCM token, web push, ntfy).
- Favourites belong to the user and come in two kinds: a **line** with an optional direction (no route variant), or a **stop** on its own. A stop favourite covers every line serving it. `GET /api/favorites` makes no İBB calls; live data loads only when an item is opened (on demand, to save the call budget).

## Stop index and stop-wide arrivals (implemented 2026-10-14)

- Built daily from one bulk `DurakDetay_GYY_wYonAdi` call with an empty line code: ~31 MB, ~16 s, **81,786 rows, 1,102 line codes, 13,685 stops**. Read as a stream (one `<Table>` at a time) into `stops` + `stop_lines`. Per-line stop lists now come from this index instead of one İBB call per line.
- Lines per stop: median 3, 2,300 stops have ≥10, the busiest has 71.
- `GET /api/stops?q=`: name search, ignoring case and Turkish letters (`sehit sahinbey` matches `ŞEHİT ŞAHİNBEY`); returns district and lines because many stops share a name (20 × "FABRİKALAR").
- `GET /api/stops/{code}` lists the lines serving the stop; `GET /api/stops/{code}/arrivals` merges arrivals across those lines. Lines are fetched oldest data first within the call budget, and lines left out report "no data yet" until the next refresh. Example: FABRİKALAR (12 lines) had 9/12 lines on first open and 12/12 a minute later.
- **Scaling path for busy stops:** one whole-fleet call per minute (`GetFiloAracKonum_json`, all ~7,000 buses), plus a door number → line map learned from per-line calls. That would cover every line at every stop for ~1 call/min.



## HTTP API (v1)

All under `/api`, header `Authorization: Bearer <key>` (the user's key) except `/health`. The web client is developed separately (own chat/tests) and consumes this API; the server can host its built files from `wwwroot` so everything is same-origin and needs no CORS.

| Method & path | Purpose |
|---|---|
| `GET /lines?q=50` | Search the cached line list (code or name, Turkish-aware case folding) |
| `GET /lines/{code}` | Line info + both directions with their destination names |
| `GET /lines/{code}/stops?dir=G` | Ordered stops for a direction |
| `GET /lines/{code}/live` | Buses with position, stops away to each favourite stop on that line, and data age. Marks the line hot for 5 min |
| `GET /stops?q=` / `GET /stops/near?lat=&lng=` | Stop search by name / nearest stops (for adding a favourite) |
| `GET /board` | **Home screen in one call**: every favourite with its next 2–3 buses (stops away, ETA when available, stale flag) |
| `GET/POST/PUT/DELETE /favourites` | Favourites CRUD |
| `GET/POST/PUT/DELETE /watches` | Watches CRUD |
| `GET/POST/DELETE /notify-targets`, `POST /notify-targets/{id}/test` | Notification destinations + test message |
| `GET /health` | Uptime, hot lines, calls in the last minute, backoff state, cache ages |

Clients poll `/board` or `/live` every 30 s. That's cheap, because it reads from `LiveCache` and never triggers an İBB call by itself. Server-Sent Events can come later if needed.

## Notifications (pluggable, decided later)

```csharp
public interface INotifier
{
    string Channel { get; }
    Task SendAsync(NotifyTarget target, Alert alert, CancellationToken ct);
}
```

Candidates, all reachable from the same interface:

| Channel | Fit for Android + this server |
|---|---|
| **ntfy** (self-hosted or ntfy.sh) | One HTTP POST, no Firebase, an Android app with priority/sound settings. The ntfy server itself is ~20 MB. Easiest to start with |
| FCM direct (HTTP v1 API) | Needs our own Android app; the most native result |
| Web Push (VAPID) | If the client becomes a PWA; works well on Android Chrome |
| Telegram bot | Zero client work, but it's a chat message rather than a real notification |

Alert stages per watch and trip: **approaching** (stops away ≤ N, once), **passed** (optional, once). The message carries the line, the bus door number, stops away, and the data age.

## Project layout

```
src/BusTracker.Api/
  Program.cs                 composition, endpoints mapping
  Ibb/                       IbbClient, SOAP parsing, DTOs
  Tracking/                  TrackingEngine, geometry, trip continuity (no I/O)
  Polling/                   LinePoller, StaticDataRefresher, CallBudget, LiveCache
  Alerts/                    AlertEvaluator, INotifier + implementations
  Data/                      SQLite schema/migrations, repositories (Dapper)
  Endpoints/                 one file per resource group
tests/BusTracker.Tests/      engine + parser tests using docs/api-samples
```

One API project + one test project. The engine stays free of I/O, so it can move to its own library if a native app ever needs it.

## Deployment

- `dotnet publish -c Release -r linux-x64 --self-contained` → one folder, nothing to install on the server.
- systemd unit with a memory cap, `Restart=on-failure`, environment file for the API key and DB path.
- Ubuntu + nginx. The app listens on `127.0.0.1:5080` only; nginx terminates HTTPS and proxies:
  ```nginx
  location / {
      proxy_pass         http://127.0.0.1:5080;
      proxy_set_header   Host $host;
      proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header   X-Forwarded-Proto $scheme;
  }
  ```
  The app enables `UseForwardedHeaders` so it sees the real scheme and client IP.
- Add a user: `BusTracker.Api add-user <name>` prints the new API key once.
- Backup = copy one SQLite file.

## Decisions so far

- Server: Ubuntu + nginx, systemd service (2026-10-14).
- Client: web, designed and built separately, consuming this API.
- Users: personal, possibly 1–2 friends → per-user API keys, shared polling.
- Alerts: "N stops before" and "passed my stop" for now; the watch shape may still change.
- Notification channel: still open (ntfy suggested to start).

## Later optimizations

Parked until the core features are settled:

- **ETA for buses not yet departed.** İETT's own site appears to use the timetable here (e.g. "(14:41) 13 dk" for a stop near the terminal). Use the next scheduled departure (GTFS `stop_times`) plus travel time.
- **İETT's own estimate as a second opinion.** `https://iett.istanbul/tr/RouteStation/GetStationInfo?dcode={stop}&langid=1` returns an HTML snippet with "N dk" per line. It's undocumented, has no bus ID, and is not a public API. Fetch it at most once a minute for watched stops, show it next to ours, and use it to calibrate our estimate.

- **Lower position lag (currently ~30–90 s, worst ~2.5 min).** Time each line's once-a-minute İBB call to land a few seconds after İBB's own refresh, which happens at about the same second each minute for all buses. Let clients poll our cache every 10–15 s, which costs no extra İBB calls. Expected typical lag: ~15–40 s.
- **Smooth marker movement** in the UI: buses glide to their new positions instead of jumping once a minute.
