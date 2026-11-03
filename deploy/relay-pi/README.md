# Turkey relay on a Raspberry Pi

The always-on box in Turkey that holds the reverse SSH tunnel, so the German VPS
can reach İBB from a Turkish IP. This replaces the earlier phone + Termux setup.
See [../../docs/relayed-connection.md](../../docs/relayed-connection.md) for how
the relay fits into the server.

Tested target: **Raspberry Pi 2 Model B** (ARMv7, 32-bit, no WiFi) on **ethernet**.
Any Pi works; a Pi 2 is plenty for a tunnel.

## What it does

The Pi runs one `autossh` process as a systemd service (`relay-tunnel`). It dials
**out** to the VPS and opens a remote **SOCKS5** proxy there
(`socks5://172.17.0.1:1080`, on the Docker bridge gateway). The VPS never connects
to the Pi, so your home network needs no public IP, no port forwarding, no static
IP — just a working internet connection. autossh reconnects automatically if the
link drops or the Pi reboots.

## 1. Flash the SD card

Use **Raspberry Pi Imager** on your laptop:

- OS: **Raspberry Pi OS Lite (32-bit)** — the Pi 2 is ARMv7, so 32-bit.
- Open the settings (gear icon / `Ctrl+Shift+X`) before writing:
  - Set a hostname, e.g. `relay-pi`.
  - **Enable SSH** and add your laptop's public key (or set a password).
  - Set locale/timezone.
  - WiFi: skip it — the Pi 2 has none; you'll use ethernet.
- Write the card, put it in the Pi, connect **ethernet**, power on.

## 2. SSH into the Pi

```bash
ssh pi@relay-pi.local      # or the Pi's IP from your router
```

## 3. Run the setup

Copy this folder to the Pi and run the script:

```bash
# from your laptop, or clone the repo on the Pi
scp -r deploy/relay-pi pi@relay-pi.local:~/relay-pi
ssh pi@relay-pi.local
cd ~/relay-pi
chmod +x setup.sh
sudo ./setup.sh
```

The script installs `autossh`, creates a locked-down `relay` user, generates the
tunnel key, installs the systemd service (enabled, not started), and prints the
Pi's **public key** plus the exact next steps.

## 4. Authorize the Pi on the VPS

The VPS needs the one-time tunnel setup from
[../../docs/relayed-connection.md](../../docs/relayed-connection.md) (a `tunnel`
user and `GatewayPorts clientspecified` in sshd). Then add the Pi's public key
(printed by `setup.sh`) to `/home/tunnel/.ssh/authorized_keys` on the VPS, with
the hardening prefix so this key can open **only** the one SOCKS port:

```
restrict,port-forwarding,permitlisten="172.17.0.1:1080" ssh-ed25519 AAAA... bustracker-relay-pi
```

## 5. Configure and start

On the Pi:

```bash
sudo nano /etc/relay-tunnel.env     # set VPS_HOST (and VPS_PORT if not 22)
sudo systemctl start relay-tunnel
systemctl status relay-tunnel
journalctl -u relay-tunnel -f       # watch it connect
```

## 6. Verify (on the VPS)

```bash
curl -x socks5h://172.17.0.1:1080 https://ifconfig.me          # a Turkish IP
curl -x socks5h://172.17.0.1:1080 -s -o /dev/null -w '%{http_code}\n' https://api.ibb.gov.tr/iett/
```

Then the BusTracker container (with `BusTracker__Ibb__Proxy=socks5://host.docker.internal:1080`)
will show the relay healthy at `/health` → `relay.lastOkAt` once a line is opened.

## Troubleshooting

- `systemctl status relay-tunnel` shows the autossh exit reason; `journalctl -u
  relay-tunnel -f` streams it.
- "Warning: remote port forwarding failed for listen port 1080" → the VPS already
  has something on `172.17.0.1:1080` (an old tunnel), or `GatewayPorts
  clientspecified` is not set / sshd not restarted.
- Permission denied (publickey) → the Pi's public key is not in the VPS tunnel
  user's `authorized_keys`, or the `restrict` line is malformed.
- Nothing on the VPS side → confirm the Pi has internet and can reach the VPS:
  `ssh -i /home/relay/.ssh/relay_tunnel -p <port> tunnel@<host>` (as the relay
  user) will show the connection/auth result.
