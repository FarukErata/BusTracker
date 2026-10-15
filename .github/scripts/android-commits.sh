#!/usr/bin/env bash
set -euo pipefail

since="${1:-}"
until="${2:-HEAD}"
range="$until"
[ -n "$since" ] && range="$since..$until"

git log --no-merges --reverse --format='%H' "$range" -- android .github/workflows/android-release.yml .github/scripts
