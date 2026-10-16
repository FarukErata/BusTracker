# Kaç Kaldı

**How many stops until my bus?** A small, self-hosted tracker for İETT buses in Istanbul. Pick a stop or a line and it shows where each bus is, how many stops away it is, and roughly how many minutes until it arrives.

It is a personal project for me and a few friends: invite-only, no accounts, no ads. It is not affiliated with İETT or İBB.

## What it does

- **Stops:** every bus heading to a stop, across all lines that serve it, sorted by arrival, with stops away and an ETA.
- **Lines:** a map with live buses on the real road route, in either direction. Pin a stop to see the buses approaching it.
- **Favourites:** save stops and lines; they follow you to your other devices with a one-time link code.
- **Android app:** installable APK, released from GitHub (notifications are next).

## How it works

İBB's web services allow 100 requests an hour, so the server fetches the position of every bus in the city once a minute (one call), and learns which bus runs on which line from occasional per-line calls. It remembers that, places each bus on the real route geometry, and works out stops away and ETA itself. Details: [docs/server-design.md](docs/server-design.md).

## Repository

| Folder | What |
|---|---|
| `src/BusTracker.Api` | Server: .NET 10 minimal API, SQLite. Also serves the web app |
| `client` | Web app: React, Vite, Tailwind, Leaflet |
| `android` | Android app: Kotlin, Jetpack Compose |
| `tests` | Server tests (xUnit) |
| `deploy` | Docker Compose file, nginx site, update script and timer for the server |
| `docs` | Design notes, the İBB API reference, setup and deploy guides |

## Getting started

```bash
dotnet run --project src/BusTracker.Api --launch-profile http
npm ci --prefix client && npm run dev --prefix client
```

Then open http://localhost:5173. Requirements and the Android build: [docs/setup.md](docs/setup.md).

## Releases

- **Server and web app:** every push to `main` that changes them builds a Docker image (`ghcr.io/farukerata/bustracker`). The server picks it up by itself within five minutes. See [docs/deploy.md](docs/deploy.md).
- **Android app:** Actions → Android release → Run workflow → `prepare`, review the draft release, then run it again with `publish`. The newest APK is always at [releases/latest/download/kackaldi.apk](https://github.com/FarukErata/BusTracker/releases/latest/download/kackaldi.apk).

Commit titles follow `Area - Feature|Fix|Chore: what changed`; Android versions and release notes are built from them.

## Data and credits

- Live bus positions, stops and line data: [İBB Açık Veri Portalı](https://data.ibb.gov.tr) (İETT web services and the İETT route geometry dataset), used under the portal's terms.
- Map tiles: © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.
