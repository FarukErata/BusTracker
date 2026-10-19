# Deploying the server

The API and the web app ship as one Docker image, `ghcr.io/farukerata/bustracker`. GitHub Actions builds it on every push to `main` that changes the server or the web app, starts it once to check that `/health` and the web app load, and publishes it with two tags: `latest` and `sha-<commit>`. Nothing on GitHub can reach the server; the server pulls new images itself.

The server needs Docker with Compose v2 (`docker compose`, with a space), nginx and curl.

Compose v2 and the old standalone `docker-compose` 1.29 can be installed side by side. 1.29 starts the container the first time but fails on every update with `KeyError: 'ContainerConfig'`, so use v2 for this app (`sudo apt install docker-compose-v2`, or `docker-compose-plugin` when Docker comes from Docker's own repository). Projects that already run on 1.29 can stay on it; don't run v2 in their folders, because v1 and v2 name containers differently.

## Folder

Everything for the app lives in one folder, for example `~/docker/kackaldi`:

```
kackaldi/
├── compose.yaml     from deploy/compose.yaml
├── .env             settings, server only
├── update.sh        from deploy/update.sh
└── data/            the database; back this up
```

## First-time setup

1. Create the folder and download the files:

   ```bash
   mkdir -p ~/docker/kackaldi && cd ~/docker/kackaldi
   curl -fsSLO https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/compose.yaml
   curl -fsSLO https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/update.sh && chmod +x update.sh
   curl -fsSL -o .env https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/.env.example
   ```

2. Edit `.env` and set a real invite code. People need it to create a user. More codes go on further lines (`BusTracker__Access__InviteCodes__1=…`). Other settings follow the same pattern, `BusTracker__Section__Key=value`. To keep the log quiet, add `Logging__LogLevel__System.Net.Http.HttpClient=Warning`.

3. Create the data folder. The container runs as user 1654, which must be able to write it:

   ```bash
   mkdir -p data && sudo chown 1654:1654 data
   ```

4. Start it:

   ```bash
   docker compose up -d
   curl -s http://127.0.0.1:5080/health
   ```

   On the first start it downloads the route shapes and the stop index from İBB, which takes a minute or two. Watch with `docker compose logs -f`.

5. nginx and HTTPS. Point a DNS record (for example `kackaldi.example.com`) at the server, then:

   ```bash
   D=kackaldi.example.com
   sudo curl -fsSL -o /etc/nginx/sites-available/kackaldi https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/nginx-kackaldi.conf
   sudo sed -i "s/kackaldi.example.com/$D/" /etc/nginx/sites-available/kackaldi
   sudo ln -s /etc/nginx/sites-available/kackaldi /etc/nginx/sites-enabled/kackaldi
   sudo nginx -t && sudo systemctl reload nginx
   sudo certbot --nginx -d $D
   ```

6. Automatic updates (see below).

## Automatic updates

`update.sh` checks for a new image and switches to it:

- It pulls `latest`. If that is the image already running, it stops there and logs nothing.
- Otherwise it starts the new image and waits up to two minutes for `/health` to report the new commit.
- If the new build never becomes healthy, it switches back to the previous image and writes the bad image's id to `.failed-image`, so it isn't tried again on every run. The next good build clears it.
- After a successful update it deletes unused old images.

A systemd timer runs it every five minutes. Install it once, replacing `CHANGE_ME` with your user name (the user must be allowed to run `docker`):

```bash
cd /tmp
curl -fsSLO https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/kackaldi-update.service
curl -fsSLO https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/kackaldi-update.timer
sed -i "s/CHANGE_ME/$USER/g" kackaldi-update.service
sudo mv kackaldi-update.service kackaldi-update.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now kackaldi-update.timer
```

Check it:

```bash
systemctl list-timers kackaldi-update.timer
journalctl -u kackaldi-update.service -n 20
```

Run an update right away with `sudo systemctl start kackaldi-update.service`. Stop automatic updates with `sudo systemctl disable --now kackaldi-update.timer`.

Updates restart the container. The server keeps its İBB call count and any rate-limit block in the database, so frequent restarts don't push it over İBB's hourly limit.

## Updating by hand

```bash
cd ~/docker/kackaldi
docker compose pull && docker compose up -d
curl -s http://127.0.0.1:5080/health
```

`commit` in `/health` shows which build is running.

## Rolling back

Pin an earlier build in `compose.yaml` (`image: ghcr.io/farukerata/bustracker:sha-<commit>`), then `docker compose up -d`. While it is pinned, automatic updates keep that build. Switch back to `latest` when the fix is out.

## Request log

Every `/api` request is written as one JSON line to `data/logs/requests-<UTC date>.jsonl`, kept for 14 days (`BusTracker__RequestLog__RetentionDays`; `BusTracker__RequestLog__Enabled=false` turns it off). Each line has the user and device, the route, the status, the total time, and where the time went: `waits` adds up İBB calls (`ibb`), database calls (`db`) and time spent waiting for another request's work (`wait`), and `spans` lists the individual steps. Stop requests also record how many of their lines were still waiting for İBB (`waitingLines`). Work done in the background, such as roster and fleet refreshes, gets its own lines with `"kind":"background"`.

```bash
tail -f data/logs/requests-$(date -u +%F).jsonl
```

With `jq` installed, the slow requests of today:

```bash
jq -c 'select(.kind == "request" and .ms > 1000) | {at, path, ms, waits, waitingLines}' data/logs/requests-$(date -u +%F).jsonl
```

## ETA accuracy

Signed in, open `https://<your domain>/api/stats/eta?days=7` to compare the two ETA models against real arrivals (see the ETA section of [server-design.md](server-design.md)). To show the newer model, set `BusTracker__Tracking__EtaModel=hops` in `.env` and restart.

## Notes

- The container listens on `127.0.0.1:5080` only; nginx is the only way in.
- Memory is capped at 384 MB (`mem_limit` in `compose.yaml`). .NET keeps its own heap below about 75 % of that.
- Back up `data/bustracker.db`. It holds users, devices, favourites, the bus-to-line memory and the İBB call log. Everything else is downloaded again.
- Logs rotate at 10 MB, three files.
