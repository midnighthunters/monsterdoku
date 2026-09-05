# Deploying Monsterdoku Directly to TestFlight

This repository provides two ways to build and deploy **Monsterdoku** directly to TestFlight:

1. **GitHub Actions**: [`.github/workflows/ios-testflight.yml`](../.github/workflows/ios-testflight.yml)
2. **CircleCI**: [`.circleci/config.yml`](../.circleci/config.yml) using [`ci/ios/deploy-testflight.sh`](../ci/ios/deploy-testflight.sh) (patterned after AnimalBlast)

---

## 1. Secrets & Environment Variables

Depending on whether you use **Automatic Signing** (via App Store Connect API Key) or **Manual Signing** (via `.p12` Distribution Certificate and Provisioning Profile), configure the following secrets in GitHub Secrets or CircleCI Environment Variables:

### Option A: App Store Connect API Key (Automatic Signing)
| Variable / Secret | Description |
| --- | --- |
| `APPLE_TEAM_ID` | Your 10-character Apple Developer Team ID |
| `APP_STORE_CONNECT_KEY_ID` | App Store Connect API Key ID |
| `APP_STORE_CONNECT_ISSUER_ID` | App Store Connect Issuer ID |
| `APP_STORE_CONNECT_PRIVATE_KEY` | Contents of `AuthKey_<KEY_ID>.p8` |

### Option B: Manual Signing (Distribution `.p12` + Profile)
| Variable / Secret | Description |
| --- | --- |
| `APPLE_TEAM_ID` | Your 10-character Apple Developer Team ID |
| `IOS_BUNDLE_ID` | `com.zemolabs.monsterdoku` |
| `IOS_CERTIFICATE_BASE64` | Base64-encoded Apple Distribution `.p12` certificate |
| `IOS_CERTIFICATE_PASSWORD` | Password for the `.p12` file |
| `IOS_PROVISIONING_PROFILE_BASE64` | Base64-encoded `monsterdoku_prod_profile.mobileprovision` |
| `APP_STORE_CONNECT_KEY_ID` | App Store Connect API Key ID (required for upload) |
| `APP_STORE_CONNECT_ISSUER_ID` | App Store Connect Issuer ID (required for upload) |
| `APP_STORE_CONNECT_PRIVATE_KEY` | Contents of `AuthKey_<KEY_ID>.p8` (required for upload) |

### Creating Base64 Values in Windows PowerShell:
```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('AppleDistribution.p12'))
[Convert]::ToBase64String([IO.File]::ReadAllBytes('monsterdoku_prod_profile.mobileprovision'))
```

---

## 2. Triggering TestFlight Deployments

### Via GitHub Actions (gh CLI)
```bash
# Trigger directly with optional marketing version and build number
gh workflow run ios-testflight.yml -f version=1.0.1 -f build_number=11

# Or trigger by pushing a version tag
git tag v1.0.1
git push origin v1.0.1
```

### Via CircleCI
Trigger the `testflight_deploy` workflow or push a git tag starting with `v*` (e.g. `git push origin v1.0.1`).

---

## 3. How the Direct Upload Works

1. The committed Unity Xcode project at `Build/ios` is validated and configured.
2. CocoaPods dependencies (`Podfile`) are resolved.
3. The app is archived with the Release configuration and signed for App Store distribution (`method: app-store`).
4. An App Store `.ipa` is exported and validated.
5. The IPA is uploaded directly to App Store Connect / TestFlight using Apple's `xcrun altool --upload-app` (with automatic fallback to `xcrun iTMSTransporter`).
