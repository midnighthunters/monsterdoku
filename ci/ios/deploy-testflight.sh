#!/usr/bin/env bash
set -euo pipefail

project_root="$(pwd)"
ipa_path="$project_root/build-artifacts/Monsterdoku.ipa"

echo "=== Deploying Monsterdoku to TestFlight ==="

# Build IPA if not already present
if [[ ! -f "$ipa_path" ]]; then
  echo "IPA not found at $ipa_path, building app-store IPA..."
  export IPA_EXPORT_METHOD="app-store"
  bash "$project_root/ci/ios/build-ipa.sh"
fi

if [[ ! -f "$ipa_path" ]]; then
  echo "Error: Cannot find IPA at $ipa_path" >&2
  exit 1
fi

echo "Found IPA at: $ipa_path ($(ls -lh "$ipa_path" | awk '{print $5}'))"

# Require App Store Connect API credentials for upload
if [[ -z "${APP_STORE_CONNECT_KEY_ID:-}" || -z "${APP_STORE_CONNECT_ISSUER_ID:-}" || -z "${APP_STORE_CONNECT_PRIVATE_KEY:-}" ]]; then
  echo "Error: APP_STORE_CONNECT_KEY_ID, APP_STORE_CONNECT_ISSUER_ID, and APP_STORE_CONNECT_PRIVATE_KEY must be set to upload to TestFlight." >&2
  exit 1
fi

# Configure private key for Apple upload tools
key_dirs=(
  "$HOME/.appstoreconnect/private_keys"
  "$HOME/.private_keys"
)

for kd in "${key_dirs[@]}"; do
  mkdir -p "$kd"
  key_file="$kd/AuthKey_${APP_STORE_CONNECT_KEY_ID}.p8"
  printf '%s' "$APP_STORE_CONNECT_PRIVATE_KEY" > "$key_file"
  chmod 600 "$key_file"
done

cleanup_keys() {
  for kd in "${key_dirs[@]}"; do
    rm -f "$kd/AuthKey_${APP_STORE_CONNECT_KEY_ID}.p8" 2>/dev/null || true
  done
}
trap cleanup_keys EXIT

echo "Uploading IPA to TestFlight via xcrun altool..."
if xcrun altool --upload-app \
  --type ios \
  --file "$ipa_path" \
  --apiKey "$APP_STORE_CONNECT_KEY_ID" \
  --apiIssuer "$APP_STORE_CONNECT_ISSUER_ID"; then
  echo "=== TestFlight upload successful via altool! ==="
else
  echo "Warning: altool upload returned an error; attempting fallback upload with iTMSTransporter..."
  xcrun iTMSTransporter \
    -m upload \
    -assetFile "$ipa_path" \
    -apiKey "$APP_STORE_CONNECT_KEY_ID" \
    -apiIssuer "$APP_STORE_CONNECT_ISSUER_ID"
  echo "=== TestFlight upload successful via iTMSTransporter! ==="
fi
