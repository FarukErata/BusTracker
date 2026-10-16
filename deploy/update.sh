#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")"

IMAGE=$(docker compose config --images | head -1)
CONTAINER=kackaldi
HEALTH=http://127.0.0.1:5080/health
FAILED_FILE=.failed-image

log() { echo "$(date -Is) $*"; }

commit_of() {
  docker image inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$1" | sed -n 's/^BUSTRACKER_COMMIT=//p'
}

wait_for_commit() {
  local expected=$1
  for _ in $(seq 1 24); do
    if curl -fsS --max-time 5 "$HEALTH" 2>/dev/null | grep -q "\"commit\":\"$expected\""; then return 0; fi
    sleep 5
  done
  return 1
}

docker compose pull --quiet
latest=$(docker image inspect -f '{{.Id}}' "$IMAGE")
running=$(docker inspect -f '{{.Image}}' "$CONTAINER" 2>/dev/null || echo none)

if [ "$latest" = "$running" ]; then
  exit 0
fi
if [ -f "$FAILED_FILE" ] && [ "$(cat "$FAILED_FILE")" = "$latest" ]; then
  exit 0
fi

new_commit=$(commit_of "$latest")
log "updating to $new_commit"
docker compose up -d

if wait_for_commit "$new_commit"; then
  rm -f "$FAILED_FILE"
  docker image prune -f > /dev/null
  log "running $new_commit"
  exit 0
fi

log "$new_commit did not become healthy; rolling back"
echo "$latest" > "$FAILED_FILE"
if [ "$running" != "none" ]; then
  docker tag "$running" "$IMAGE"
  docker compose up -d
  wait_for_commit "$(commit_of "$running")" && log "rolled back to $(commit_of "$running")" || log "rollback did not become healthy either"
fi
exit 1
