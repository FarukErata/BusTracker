#!/usr/bin/env bash
set -euo pipefail

version="$1"
minimum="$2"

if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must look like 1.2.3, got '$version'" >&2
  exit 1
fi

lowest=$(printf '%s\n%s\n' "$version" "$minimum" | sort -V | head -1)
if [ "$lowest" != "$minimum" ]; then
  echo "Version $version is lower than the prepared version $minimum; versions can only go up" >&2
  exit 1
fi

latest=$(git tag --list 'android-v*' --sort=-v:refname | head -1)
if [ -n "$latest" ]; then
  published="${latest#android-v}"
  if [ "$(printf '%s\n%s\n' "$version" "$published" | sort -V | tail -1)" != "$version" ] || [ "$version" = "$published" ]; then
    echo "Version $version must be higher than the latest release $published" >&2
    exit 1
  fi
fi

echo "Version $version is OK (prepared $minimum, latest ${latest:-none})"
