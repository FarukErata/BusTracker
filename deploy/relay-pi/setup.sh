#!/usr/bin/env bash
# Set up the BusTracker Turkey relay on a Raspberry Pi (ethernet).
# Idempotent: safe to run again. Run as root, from this folder:  sudo ./setup.sh
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Run with sudo: sudo ./setup.sh" >&2
  exit 1
fi

here="$(cd "$(dirname "$0")" && pwd)"
RELAY_USER=relay
RELAY_HOME=/home/relay
KEY="$RELAY_HOME/.ssh/relay_tunnel"

echo "==> Installing autossh and the ssh client"
apt-get update -qq
apt-get install -y autossh openssh-client

echo "==> Creating the '$RELAY_USER' user (if missing)"
if ! id "$RELAY_USER" >/dev/null 2>&1; then
  useradd --system --create-home --home-dir "$RELAY_HOME" --shell /usr/sbin/nologin "$RELAY_USER"
fi
install -d -o "$RELAY_USER" -g "$RELAY_USER" -m 700 "$RELAY_HOME/.ssh"

echo "==> Generating the tunnel key (if missing)"
if [ ! -f "$KEY" ]; then
  sudo -u "$RELAY_USER" ssh-keygen -t ed25519 -N "" -C "bustracker-relay-pi" -f "$KEY"
fi
touch "$RELAY_HOME/.ssh/known_hosts"
chown "$RELAY_USER:$RELAY_USER" "$RELAY_HOME/.ssh/known_hosts"
chmod 644 "$RELAY_HOME/.ssh/known_hosts"

echo "==> Installing the config file (if missing)"
if [ ! -f /etc/relay-tunnel.env ]; then
  install -m 644 "$here/relay-tunnel.env.example" /etc/relay-tunnel.env
  echo "    Created /etc/relay-tunnel.env from the example — EDIT IT and set VPS_HOST."
else
  echo "    /etc/relay-tunnel.env already exists — left as is."
fi

echo "==> Installing the systemd service"
install -m 644 "$here/relay-tunnel.service" /etc/systemd/system/relay-tunnel.service
systemctl daemon-reload
systemctl enable relay-tunnel.service   # start on boot; we do NOT start it here yet

cat <<EOF

Done. The service is enabled but not started yet. Next:

  1. Add this Pi's PUBLIC key to the VPS tunnel user. On the VPS, append the line
     below to /home/tunnel/.ssh/authorized_keys (the restrict/permitlisten prefix
     locks this key to only opening the one SOCKS port — nothing else):

       restrict,port-forwarding,permitlisten="172.17.0.1:1080" $(cat "$KEY.pub")

  2. Edit /etc/relay-tunnel.env and set VPS_HOST (and VPS_PORT if not 22).

  3. Start it:   sudo systemctl start relay-tunnel
     Watch it:   systemctl status relay-tunnel   journalctl -u relay-tunnel -f

  4. Verify ON THE VPS (should print a Turkish IP):
       curl -x socks5h://172.17.0.1:1080 https://ifconfig.me
EOF
