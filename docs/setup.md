# Development setup

What each part of the repo needs, and how to build it on a fresh machine.

## Tools

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 10.0 | API (`src/`) and its tests (`tests/`) |
| Node.js + npm | 22 | Web app (`client/`) |
| JDK | 21 | Android app only (`android/`). Gradle needs 17 or newer |
| Android SDK | platform 37, build-tools 36 | Android app only |
| Git | any recent | Everything |

Gradle itself is downloaded by the wrapper (`android/gradlew`); nothing to install.
The server needs none of these: it runs the Docker image (see [deploy.md](deploy.md)).

## API

```bash
dotnet run --project src/BusTracker.Api --launch-profile http
```

Listens on `http://localhost:5080`. Data lives in `src/BusTracker.Api/App_Data/` (gitignored). On first start it downloads the route shapes and the stop index from İBB.

```bash
dotnet test tests/BusTracker.Tests
```

## Web app

```bash
npm ci --prefix client
npm run dev --prefix client
```

Vite on `http://localhost:5173`, forwarding `/api` and `/health` to the API. `npm run build --prefix client` writes the production build into `src/BusTracker.Api/wwwroot/` (gitignored), where the API serves it.

## Android app

Point the build at a JDK 21 (it does not have to be on `PATH`), and tell Gradle where the Android SDK is in `android/local.properties` (gitignored, forward slashes):

```properties
sdk.dir=C\:/Users/<you>/AppData/Local/Android/Sdk
```

```bash
cd android
JAVA_HOME=/path/to/jdk-21 ./gradlew assembleDebug
```

The app talks to `https://kackaldi.farukerata.com` by default. To try it against a local API in the Android emulator (which reaches the PC at `10.0.2.2`), build with `-PserverUrl=http://10.0.2.2:5080 -PallowCleartext=true -Pabis=x86_64`. Never use `allowCleartext` for a published build. Published builds contain only `arm64-v8a` code, which is what phones need; `abis` adds `x86_64` for the emulator.

Release builds are signed. Locally the key and its password are read from `~/.android-keys/bustracker/keystore.properties`; in GitHub Actions from the secrets `BT_KEYSTORE_BASE64` and `BT_KEYSTORE_PASSWORD`. The key is never committed, and every release must be signed with the same one.

## Releases and images

| What | How |
|---|---|
| Android app | Actions → Android release → Run workflow → `prepare` (builds the APK and drafts the release), review the draft, then `publish`. Publishing reuses the prepared APK; it is rebuilt only if the version was raised on the draft |
| Server image | Built and published to `ghcr.io/farukerata/bustracker` on every push to `main` that touches the server or web app. The server picks it up within five minutes (see [deploy.md](deploy.md)) |

Commit titles follow `Area - Feature|Fix|Chore: what changed`; Android release versions and notes are built from them.
