# Monsterdoku Ads Production Checklist

## Current status: **NOT READY FOR A PRODUCTION IPA**

The runtime integration is AppLovin MAX with Google AdMob adapters. The current project is intentionally fail-closed: ads will not initialize until the release gates and credentials below are completed.

Current blockers found in the repository:

- `Assets/Game/Ads/Resources/AdsConfig.asset` contains placeholder values for all six MAX ad units.
- `generalAudienceAdsApproved` is `false`.
- AppLovin SDK key is blank in AppLovin Integration Manager settings.
- Android and iOS AdMob App IDs are blank.
- MAX consent flow is disabled and its privacy/terms URLs are blank.
- `developmentTestMode` is `false`; in a Unity Development build it enables verbose MAX logs and the Settings diagnostics entry point—it does **not** enable test inventory. It is disabled in nondevelopment player builds.
- No checked-in MAX/AdMob test-device identifiers were found.
- The generated iOS `Info.plist`, CocoaPods resolution, privacy manifest, and SKAdNetwork entries have not been verified.
- iOS signing/team settings are blank and the iPhone build number is `0`.

Do not ship an IPA until the release validation and physical-device checks pass.

## 1. Publisher, audience, and privacy decisions

- [ ] Confirm the app's audience classification with the publisher/legal owner.
- [ ] Complete the privacy review before setting `generalAudienceAdsApproved: 1`.
- [ ] Publish real HTTPS pages for:
  - [ ] Privacy policy
  - [ ] Terms of service
- [ ] Make the URLs describe advertising, mediation, analytics, consent, and data use accurately.
- [ ] Decide on one consent architecture:
  - [ ] MAX terms/privacy consent flow, or
  - [ ] An approved Google-certified CMP integrated for the applicable regions.
- [ ] For EEA/UK users, verify the CMP/IAB TCF requirements for Google demand.
- [ ] Verify **Settings > Manage Privacy Choices** is visible and opens the configured CMP's choices UI for users who need to revisit consent.
- [ ] Add/verify the required `app-ads.txt` entries for AppLovin and Google at the publisher's authorized domain.
- [ ] Check App Store privacy disclosures and the equivalent Google data-safety declarations.

## 2. AppLovin MAX and AdMob dashboard setup

The installed packages are:

- AppLovin MAX Unity plugin `com.applovin.mediation.ads` `8.6.4`
- MAX Google Android adapter `25040000.0.0`
- MAX Google iOS adapter `13070000.0.0`

- [ ] Confirm the exact Unity application identifiers:
  - Android: `com.zemolabs.monsterlogic`
  - iOS: `com.zemolabs.monsterlogic`
- [ ] Register both app identifiers in the MAX dashboard.
- [ ] Register both app identifiers in AdMob.
- [ ] Create separate MAX ad units for each platform and format:
  - [ ] Android rewarded
  - [ ] Android interstitial
  - [ ] Android banner
  - [ ] iOS rewarded
  - [ ] iOS interstitial
  - [ ] iOS banner
- [ ] Connect Google bidding/AdMob demand in MAX.
- [ ] Map the matching AdMob inventory to every MAX ad unit.
- [ ] Copy the **MAX ad-unit IDs** (not AdMob ad-unit IDs) into `AdsConfig.asset`.
- [ ] Copy the AppLovin SDK key into **AppLovin > Integration Manager**.
- [ ] Copy the real Android and iOS AdMob App IDs into the corresponding Integration Manager fields.
- [ ] Verify MAX dashboard mediation mappings with the MAX Mediation Debugger.

## 3. Unity project configuration

Open **AppLovin > Integration Manager** and complete the following:

- [ ] Enter the AppLovin SDK key.
- [ ] Enter the Android AdMob App ID.
- [ ] Enter the iOS AdMob App ID.
- [ ] Enable the selected MAX consent/privacy flow, if that is the chosen architecture.
- [ ] Enter the same real HTTPS privacy and terms URLs used by the game configuration.
- [ ] Configure the iOS user-tracking usage description if ATT is part of the consent flow.
- [ ] Configure MAX test mode/test devices before any development request using production units.
- [ ] Run the supported Android/iOS dependency resolver after changing mediation packages.
- [ ] Check that only MAX-mediated Google packages are installed; do not add the standalone Google Mobile Ads Unity runtime.
- [ ] Resolve the duplicated MAX installation before a mobile build: Unity reports GUID conflicts between `Assets/MaxSdk` and `Packages/com.applovin.mediation.ads`. Retain only the supported copy through the AppLovin migration/Integration Manager workflow; do not delete either copy blindly.
- [ ] Check the External Dependency Manager version/resolution and remove any duplicate or stale resolver setup.

Update `Assets/Game/Ads/Resources/AdsConfig.asset`:

- [ ] Replace all six `REPLACE_ME_*` values with real MAX IDs.
- [ ] Replace both `REPLACE_ME_HTTPS_*` values with real HTTPS URLs.
- [ ] Set `generalAudienceAdsApproved: 1` only after the publisher/privacy review is complete.
- [ ] Set `developmentTestMode: 1` only for a Unity Development device test; it enables MAX logs and the Settings diagnostics entry point, not test inventory.
- [ ] Do not assume `developmentTestMode` alone makes ads safe; test mode must also be enabled for the device/network in MAX.

## 4. Test-mode procedure (physical iPhone)

The Unity Editor uses `NoOpAdService`; live MAX ads do not run in Editor Play Mode. Use edit-mode tests for deterministic logic and a development build on a physical iPhone for the SDK.

### Safe MAX test mode

- [ ] Use real MAX app/ad-unit IDs and real Integration Manager credentials. Do not use placeholder IDs.
- [ ] Add the test iPhone's advertising identifier to MAX test-device configuration, or use the MAX Mediation Debugger in the app to enable test ads.
- [ ] Enable test mode for the relevant mediated networks, including Google/AdMob, according to the MAX dashboard/network configuration.
- [ ] Set `developmentTestMode: 1` only for a Unity Development device test if verbose logs and **Settings > Open Ad Diagnostics** are required. It does not enable test inventory and is compiled out of a nondevelopment player build.
- [ ] Build as a **Development** iOS build and install on the registered test iPhone.
- [ ] Open the MAX Mediation Debugger and confirm the SDK and Google adapter initialize.
- [ ] Check device/Xcode logs for `Test Mode On: true` during MAX initialization.
- [ ] Confirm rewarded, interstitial, and banner test ads load and show.
- [ ] Never click a live ad. If a creative does not clearly appear to be a test ad, stop testing and verify the device/network test configuration.

Official MAX references:

- [MAX iOS Test Mode](https://support.applovin.com/en/max/ios/testing-networks/test-mode)
- [MAX Mediation Debugger](https://support.applovin.com/en/max/ios/testing-networks/mediation-debugger)

### Product behavior to verify

- [ ] Rewarded extra-heart flow: reward is granted only after the reward callback and ad close.
- [ ] Rewarded hint flow: dismissal/no-fill/display failure grants no hint.
- [ ] Rewarded villain-reveal flow: earned reward places exactly one valid villain.
- [ ] Early rewarded close grants nothing.
- [ ] Rewarded display failure grants nothing and the ad reloads.
- [ ] Level 9: no interstitial opportunity.
- [ ] Level 10 and later: one result-break interstitial opportunity per genuine completion.
- [ ] Interstitial close or display failure returns to result navigation immediately.
- [ ] Banner unlocks only after `campaign-003` completion or the documented migrated-progress condition.
- [ ] Banner hides during fullscreen ads and overlays.
- [ ] Banner safe area, adaptive height, portrait rotation, and tap-target separation are correct.
- [ ] Background/resume does not duplicate rewards or callbacks.
- [ ] Audio resumes to its previous state after fullscreen ads; a previously muted player remains muted.
- [ ] No-fill behavior remains playable and does not block navigation.

## 5. iOS/Xcode production requirements

A signed IPA requires macOS/Xcode for the final archive/signing step.

- [ ] Set a valid iOS Bundle ID matching the MAX/AdMob dashboard records.
- [ ] Set the Apple Developer Team ID.
- [ ] Configure automatic signing or a valid manual distribution provisioning profile.
- [ ] Increment the iPhone build number from `0` for the release archive.
- [ ] Export the iOS Xcode project from Unity.
- [ ] Run the supported CocoaPods resolution on macOS.
- [ ] Open the generated `.xcworkspace` when CocoaPods is used, not only the `.xcodeproj`.
- [ ] Confirm the generated project contains the AppLovin SDK key settings.
- [ ] Confirm `GADApplicationIdentifier` contains the real iOS AdMob App ID.
- [ ] Confirm `NSUserTrackingUsageDescription` is present and accurately describes tracking, if ATT is used.
- [ ] Confirm the generated `SKAdNetworkItems` list contains the required installed-network identifiers.
- [ ] Confirm privacy manifests and required adapter frameworks are present.
- [ ] Check for duplicate AppLovin, Google, or other mediation frameworks/pods.
- [ ] Build and run on a physical iPhone before archiving.
- [ ] Archive with the correct Distribution/Release configuration.
- [ ] Validate signing, entitlements, Bundle ID, version, build number, and App Store Connect upload settings.

The MAX iOS postprocessor can generate/merge Google App ID, consent, and SKAdNetwork data during the build, but this must be verified in the generated Xcode project and final archive; their absence from the repository is not proof that the final IPA is correct.

## 6. Validation gates

Run this Unity menu item after completing the Unity configuration:

**Tools > Monsterdoku > Validate Ads Configuration**

For a non-development Android/iOS build, `AdsBuildValidator` fails the build when it finds missing IDs, URLs, packages, Integration Manager credentials, or consent-flow settings. A Development build may be allowed with warnings, but runtime still remains fail-closed when `AdsConfig.IsRuntimeReady` is false.

- [ ] Validator reports no iOS errors.
- [ ] Development iPhone build loads test ads on the registered device.
- [ ] MAX Mediation Debugger reports the expected adapters and ad units.
- [ ] All product behavior checks above pass.
- [ ] Generated Xcode project/archive checks pass.
- [ ] Only after all checks pass: create the signed production IPA.

## 7. Automated/editor tests

The editor suite uses `FakeAdService` and makes no network requests. Run the edit-mode tests to verify policy and reward behavior, but do not treat them as proof that the iOS SDK, mediation, consent, or generated plist works.

Relevant files:

- `Assets/Game/Scripts/Ads/MaxAdService.cs`
- `Assets/Game/Scripts/Ads/AdsConfig.cs`
- `Assets/Game/Scripts/Ads/Editor/AdsBuildValidator.cs`
- `Assets/Game/Scripts/Core/GameBootstrap.cs`
- `Assets/Game/Tests/Editor/AdsIntegrationTests.cs`
- `Assets/Game/Docs/ADS_SETUP.md`
