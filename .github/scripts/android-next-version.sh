#!/usr/bin/env bash
set -euo pipefail

bump="$1"
dir="$(dirname "$0")"

latest=$(git tag --list 'android-v*' --sort=-v:refname | head -1)
current="${latest#android-v}"
[ -z "$latest" ] && current="0.0.0"
IFS=. read -r major minor patch <<< "$current"

commits=$(bash "$dir/android-commits.sh" "$latest")
if [ -z "$commits" ]; then
  echo "No app changes since $latest, nothing to release" >&2
  exit 1
fi

if [ "$bump" = "auto" ]; then
  bump="patch"
  for sha in $commits; do
    if git log -1 --format='%s' "$sha" | grep -qE ' - Feature: '; then
      bump="minor"
      break
    fi
  done
fi

case "$bump" in
  major) echo "$((major + 1)).0.0" ;;
  minor) echo "$major.$((minor + 1)).0" ;;
  patch) echo "$major.$minor.$((patch + 1))" ;;
  *) echo "Unknown bump: $bump" >&2; exit 1 ;;
esac
