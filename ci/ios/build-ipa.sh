#!/usr/bin/env bash
set -euo pipefail

project_root="$(pwd)"
IOS_BUILD_PATH="${IOS_BUILD_PATH:-Build/ios}"
IPA_EXPORT_METHOD="${IPA_EXPORT_METHOD:-development}"
IOS_BUNDLE_ID="${IOS_BUNDLE_ID:-com.zemolabs.monsterdoku}"
APPLE_TEAM_ID="${APPLE_TEAM_ID:-}"
build_number="${IOS_BUILD_NUMBER:-${CIRCLE_BUILD_NUM:-${GITHUB_RUN_NUMBER:-1}}}"
marketing_version="${IOS_MARKETING_VERSION:-${MARKETING_VERSION:-}}"

echo "=== Building iOS IPA for $IOS_BUNDLE_ID ==="
echo "Export method: $IPA_EXPORT_METHOD"
echo "Build number:  $build_number"

temp_directory="$(mktemp -d)"
keychain_path="${HOME}/Library/Keychains/ci-ios-build.keychain-db"
profile_directory="${HOME}/Library/MobileDevice/Provisioning Profiles"

cleanup() {
  rm -rf "$temp_directory"
  if [[ -f "$keychain_path" ]]; then
    security delete-keychain "$keychain_path" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

# 1. Prepare committed Xcode project for macOS
if [[ -f "$IOS_BUILD_PATH/Unity-iPhone.xcodeproj/project.pbxproj" ]]; then
  python3 - <<'PY'
from pathlib import Path
import re, sys

project = Path("Build/ios/Unity-iPhone.xcodeproj/project.pbxproj")
if project.is_file():
    source = project.read_text(encoding="utf-8")
    patched, count = re.subn(r'\s+--usymtool-path=\\".*?\\"', "", source)
    if count > 0:
        project.write_text(patched, encoding="utf-8")
        print(f"Patched {count} Windows usymtool path(s) in {project}")
PY
fi

# Set version & build number in Info.plist
if [[ -f "$IOS_BUILD_PATH/Info.plist" ]]; then
  if [[ -n "$marketing_version" ]]; then
    /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $marketing_version" "$IOS_BUILD_PATH/Info.plist" || true
  fi
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion $build_number" "$IOS_BUILD_PATH/Info.plist" || true
fi

# Resolve CocoaPods if present
if [[ -f "$IOS_BUILD_PATH/Podfile" ]]; then
  echo "Installing CocoaPods dependencies..."
  pod install --project-directory="$IOS_BUILD_PATH" --repo-update
fi

# Locate workspace or project
if [[ -d "${IOS_BUILD_PATH}/Unity-iPhone.xcworkspace" ]]; then
  xcode_container=(-workspace "${IOS_BUILD_PATH}/Unity-iPhone.xcworkspace")
elif [[ -d "${IOS_BUILD_PATH}/Unity-iPhone.xcodeproj" ]]; then
  xcode_container=(-project "${IOS_BUILD_PATH}/Unity-iPhone.xcodeproj")
else
  echo "Error: No Unity-generated Xcode project found at ${IOS_BUILD_PATH}." >&2
  exit 1
fi

mkdir -p "$project_root/build-artifacts/export"
archive_path="$project_root/build-artifacts/Monsterdoku.xcarchive"
export_path="$project_root/build-artifacts/export"

# 2. Configure Code Signing (Manual with .p12 OR Automatic with App Store Connect API)
if [[ -n "${IOS_CERTIFICATE_BASE64:-}" && -n "${IOS_PROVISIONING_PROFILE_BASE64:-}" ]]; then
  echo "Using Manual Signing with provided .p12 certificate and provisioning profile..."
  cert_password="${IOS_CERTIFICATE_PASSWORD:-}"
  certificate_path="$temp_directory/ios-signing.p12"
  profile_path="$temp_directory/profile.mobileprovision"

  printf '%s' "$IOS_CERTIFICATE_BASE64" | base64 -D > "$certificate_path"
  printf '%s' "$IOS_PROVISIONING_PROFILE_BASE64" | base64 -D > "$profile_path"

  security delete-keychain "$keychain_path" 2>/dev/null || true
  security create-keychain -p "$cert_password" "$keychain_path"
  security set-keychain-settings -lut 21600 "$keychain_path"
  security unlock-keychain -p "$cert_password" "$keychain_path"
  security import "$certificate_path" -k "$keychain_path" -P "$cert_password" \
    -T /usr/bin/codesign -T /usr/bin/security
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s \
    -k "$cert_password" "$keychain_path"
  security list-keychains -d user -s "$keychain_path" "${HOME}/Library/Keychains/login.keychain-db"

  profile_plist="$temp_directory/profile.plist"
  security cms -D -i "$profile_path" > "$profile_plist"
  profile_uuid="$(/usr/libexec/PlistBuddy -c 'Print :UUID' "$profile_plist")"
  profile_name="$(/usr/libexec/PlistBuddy -c 'Print :Name' "$profile_plist")"
  mkdir -p "$profile_directory"
  cp "$profile_path" "${profile_directory}/${profile_uuid}.mobileprovision"

  echo "Archiving project..."
  xcodebuild "${xcode_container[@]}" -scheme Unity-iPhone -configuration Release -sdk iphoneos \
    -archivePath "$archive_path" archive \
    CODE_SIGN_STYLE=Manual \
    DEVELOPMENT_TEAM="$APPLE_TEAM_ID" \
    PROVISIONING_PROFILE="$profile_uuid" \
    PROVISIONING_PROFILE_SPECIFIER="$profile_name" \
    CURRENT_PROJECT_VERSION="$build_number" \
    TARGETED_DEVICE_FAMILY="1,2"

  export_options="$temp_directory/ExportOptions.plist"
  cat > "$export_options" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>method</key><string>${IPA_EXPORT_METHOD}</string>
  <key>signingStyle</key><string>manual</string>
  <key>teamID</key><string>${APPLE_TEAM_ID}</string>
  <key>provisioningProfiles</key><dict>
    <key>${IOS_BUNDLE_ID}</key><string>${profile_name}</string>
  </dict>
</dict></plist>
PLIST

  echo "Exporting IPA..."
  xcodebuild -exportArchive -archivePath "$archive_path" -exportPath "$export_path" \
    -exportOptionsPlist "$export_options"

elif [[ -n "${APP_STORE_CONNECT_PRIVATE_KEY:-}" && -n "${APP_STORE_CONNECT_KEY_ID:-}" && -n "${APP_STORE_CONNECT_ISSUER_ID:-}" ]]; then
  echo "Using Automatic Signing via App Store Connect API Key..."
  key_path="$temp_directory/AuthKey_${APP_STORE_CONNECT_KEY_ID}.p8"
  printf '%s' "$APP_STORE_CONNECT_PRIVATE_KEY" > "$key_path"

  echo "Archiving project..."
  xcodebuild archive \
    "${xcode_container[@]}" \
    -scheme Unity-iPhone \
    -configuration Release \
    -sdk iphoneos \
    -destination generic/platform=iOS \
    -archivePath "$archive_path" \
    -allowProvisioningUpdates \
    -authenticationKeyPath "$key_path" \
    -authenticationKeyID "$APP_STORE_CONNECT_KEY_ID" \
    -authenticationKeyIssuerID "$APP_STORE_CONNECT_ISSUER_ID" \
    CODE_SIGN_STYLE=Automatic \
    DEVELOPMENT_TEAM="$APPLE_TEAM_ID" \
    CURRENT_PROJECT_VERSION="$build_number" \
    TARGETED_DEVICE_FAMILY="1,2"

  export_options="$temp_directory/ExportOptions.plist"
  plutil -create xml1 "$export_options"
  plutil -insert method -string "$IPA_EXPORT_METHOD" "$export_options"
  plutil -insert signingStyle -string automatic "$export_options"
  plutil -insert teamID -string "$APPLE_TEAM_ID" "$export_options"

  echo "Exporting archive with method '$IPA_EXPORT_METHOD'..."
  if ! xcodebuild -exportArchive \
    -archivePath "$archive_path" \
    -exportOptionsPlist "$export_options" \
    -exportPath "$export_path" \
    -allowProvisioningUpdates \
    -authenticationKeyPath "$key_path" \
    -authenticationKeyID "$APP_STORE_CONNECT_KEY_ID" \
    -authenticationKeyIssuerID "$APP_STORE_CONNECT_ISSUER_ID"; then
    echo "Warning: xcodebuild -exportArchive failed with method '$IPA_EXPORT_METHOD'."
    if [[ "$IPA_EXPORT_METHOD" != "development" ]]; then
      echo "Attempting fallback export with method 'development'..."
      plutil -replace method -string development "$export_options"
      xcodebuild -exportArchive \
        -archivePath "$archive_path" \
        -exportOptionsPlist "$export_options" \
        -exportPath "$export_path" \
        -allowProvisioningUpdates \
        -authenticationKeyPath "$key_path" \
        -authenticationKeyID "$APP_STORE_CONNECT_KEY_ID" \
        -authenticationKeyIssuerID "$APP_STORE_CONNECT_ISSUER_ID" || true
    fi
  fi
else
  echo "Error: Neither (.p12 / profile) manual signing credentials nor (App Store Connect API key) credentials are set." >&2
  exit 1
fi

# Locate or package IPA
ipa_path="$(find "$export_path" -maxdepth 1 -type f -name '*.ipa' -print -quit 2>/dev/null || true)"
if [[ -z "$ipa_path" && -d "$archive_path/Products/Applications" ]]; then
  echo "Packaging IPA directly from signed archive..."
  app_dir="$(find "$archive_path/Products/Applications" -maxdepth 1 -type d -name '*.app' -print -quit 2>/dev/null || true)"
  if [[ -n "$app_dir" ]]; then
    mkdir -p "$export_path/Payload"
    cp -R "$app_dir" "$export_path/Payload/"
    (cd "$export_path" && zip -r -q "Monsterdoku.ipa" Payload)
    rm -rf "$export_path/Payload"
    ipa_path="$export_path/Monsterdoku.ipa"
  fi
fi

if [[ -z "$ipa_path" || ! -f "$ipa_path" ]]; then
  echo "Error: No IPA was exported or generated." >&2
  exit 1
fi

cp "$ipa_path" "$project_root/build-artifacts/Monsterdoku.ipa"
mkdir -p "$project_root/Builds/ipa"
cp "$ipa_path" "$project_root/Builds/ipa/Monsterdoku.ipa"

echo "=== Successfully built IPA: $project_root/build-artifacts/Monsterdoku.ipa ==="
ls -lh "$project_root/build-artifacts/Monsterdoku.ipa"
unzip -t "$project_root/build-artifacts/Monsterdoku.ipa"
