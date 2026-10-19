#!/usr/bin/env bash
set -euo pipefail

name="$1"

if ! [[ "$name" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  echo "Version must look like 1.2.3, got $name" >&2
  exit 1
fi
code=$(( BASH_REMATCH[1] * 10000 + BASH_REMATCH[2] * 100 + BASH_REMATCH[3] ))

keystore="$RUNNER_TEMP/release.jks"
echo "$KEYSTORE_BASE64" | base64 -d > "$keystore"

cd android
BT_KEYSTORE_FILE="$keystore" BT_KEY_ALIAS=bustracker BT_KEY_PASSWORD="$BT_KEYSTORE_PASSWORD" \
  ./gradlew assembleRelease -PversionName="$name" -PversionCode="$code"
cp app/build/outputs/apk/release/app-release.apk ../kackaldi.apk
