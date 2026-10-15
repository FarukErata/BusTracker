#!/usr/bin/env bash
set -euo pipefail

until_ref="$1"
repo="$2"
new_tag="$3"
dir="$(dirname "$0")"

previous=$(git describe --tags --abbrev=0 --match 'android-v*' --exclude "$new_tag" "$until_ref" 2>/dev/null || true)

body=""
if git rev-parse -q --verify "refs/tags/$until_ref" >/dev/null && [ "$(git cat-file -t "refs/tags/$until_ref")" = "tag" ]; then
  body=$(git tag -l --format='%(contents:body)' "$until_ref" | sed '/^-----BEGIN/,$d')
fi

section() {
  local title="$1" type="$2" sha subject line printed=""
  for sha in $commits; do
    subject=$(git log -1 --format='%s' "$sha")
    if [ "$type" = "Other" ]; then
      echo "$subject" | grep -qE ' - (Feature|Fix|Chore): ' && continue
      line="$subject"
    else
      echo "$subject" | grep -qE " - $type: " || continue
      line="${subject#* - $type: }"
    fi
    [ -z "$printed" ] && printf '### %s\n' "$title" && printed=1
    echo "- $line"
    git log -1 --format='%b' "$sha" | grep -E '^- ' | sed 's/^/  /' || true
  done
  [ -n "$printed" ] && echo
  return 0
}

if [ -n "$(printf '%s' "$body" | tr -d '[:space:]')" ]; then
  printf '%s\n' "$body"
else
  commits=$(bash "$dir/android-commits.sh" "$previous" "$until_ref")
  notes=$(section New Feature; section Fixes Fix; section Other Other)
  if [ -n "$(printf '%s' "$notes" | tr -d '[:space:]')" ]; then
    printf '%s\n' "$notes"
  else
    echo "- Maintenance release"
  fi
fi

printf '\n### Install\n'
printf '<img src="https://github.com/%s/raw/main/.github/assets/install-qr.png" width="160" alt="QR code to install the latest Kaç Kaldı">\n\n' "$repo"
printf 'Scan with your Android phone to install the latest version, or [download this version](https://github.com/%s/releases/download/%s/kackaldi.apk).\n' "$repo" "$new_tag"

if [ -n "$previous" ]; then
  printf '\n**Full changelog:** https://github.com/%s/compare/%s...%s\n' "$repo" "$previous" "$new_tag"
fi
