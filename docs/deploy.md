# Deploying the server

The API and the web app ship as one Docker image, `ghcr.io/farukerata/bustracker`. GitHub Actions builds it on every push to `main` that changes the server or the web app, starts it once to check `/health`, and publishes it with two tags: `latest` and `sha-<commit>`.

The server only needs Docker with Compose v2 (`docker compose`, with a space) and nginx. The old standalone `docker-compose` 1.29 starts the container the first time but fails on every update with `KeyError: 'ContainerConfig'`. Install v2 next to it (`sudo apt install docker-compose-v2`, or `docker-compose-plugin`); other projects keep working with the old command. If you are stuck on 1.29, update with `docker-compose down` before `docker-compose up -d`.

## First-time setup

1. Create a folder and copy the two files from `deploy/` into it:

   ```bash
   mkdir -p ~/kackaldi && cd ~/kackaldi
   curl -fsSLO https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/compose.yaml
   curl -fsSL -o .env https://raw.githubusercontent.com/FarukErata/BusTracker/main/deploy/.env.example
   ```

2. Edit `.env` and set a real invite code (people need it to create a user). More settings can go here as `BusTracker__Section__Key=value`. This file stays on the server only.

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

5. nginx: copy `deploy/nginx-kackaldi.conf` to `/etc/nginx/sites-available/`, set `server_name` to your domain, enable it, then get HTTPS:

   ```bash
   sudo ln -s /etc/nginx/sites-available/nginx-kackaldi.conf /etc/nginx/sites-enabled/
   sudo nginx -t && sudo systemctl reload nginx
   sudo certbot --nginx -d your.domain
   ```

## Updating

```bash
cd ~/kackaldi
docker compose pull && docker compose up -d
curl -s http://127.0.0.1:5080/health
```

`commit` in `/health` shows which build is running.

## Rolling back

Pin an earlier build in `compose.yaml` (`image: ghcr.io/farukerata/bustracker:sha-<commit>`), then `docker compose up -d`. Switch back to `latest` when the fix is out.

## Notes

- The container listens on `127.0.0.1:5080` only; nginx is the only way in.
- Memory is capped at 384 MB (`mem_limit` in `compose.yaml`). .NET keeps its own heap below about 75 % of that.
- Back up `data/bustracker.db`; it holds users, devices and favourites. Everything else is downloaded again.
- Logs rotate at 10 MB, three files.
