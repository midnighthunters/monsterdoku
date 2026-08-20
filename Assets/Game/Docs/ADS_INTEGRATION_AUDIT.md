# Ads Integration Audit and TestFlight Validation Plan

**Audit date:** 2026-08-20  
**Scope:** Unity runtime, AppLovin MAX/Google mediation packages, release gates, privacy/consent paths, iOS export logic, and TestFlight readiness.

## Executive verdict

The project has a deliberately **fail-closed** AppLovin MAX integration, not a partially active or unsafe one. Its gameplay-side integration is structurally sound, but an ad-enabled iOS/TestFlight IPA cannot be produced from the current configuration: the app has placeholder MAX IDs, the audience/privacy gate is closed, MAX/AdMob credentials are empty, consent settings are incomplete, and iOS signing/export artifacts have not been generated or inspected.

No publisher IDs, legal decisions, consent copy, test-device identifiers, signing identities, or dashboard values were invented during this audit. Ads will therefore remain disabled until the owner completes the external setup listed below.

## What the project uses

| Area | Verified implementation |
| --- | --- |
| Ad runtime | AppLovin MAX Unity plugin `com.applovin.mediation.ads` 8.6.4 |
| Google demand | MAX Google Android adapter `25040000.0.0` and MAX Google iOS adapter `13070000.0.0`; no standalone Google Mobile Ads runtime is used |
| Formats | Rewarded, interstitial, and adaptive bottom banner |
| Reward model | A reward is granted only after MAX reports a reward and the ad closes; dismissals, failures, and stale callbacks grant nothing |
| Runtime fallback | `NoOpAdService` keeps the offline game playable whenever configuration is incomplete or the platform is unsupported |
| iOS export | MAX postprocessors generate/modify Xcode project, AppLovin settings, Google app ID, consent/ATT data, Swift support, and SKAdNetwork items at export time |

The intended architecture is correct: Google/AdMob must remain mediated by MAX. Do not install or initialize the standalone Google Mobile Ads Unity plugin alongside this stack.

## Changes made in this audit

### 1. Revisit privacy choices from Settings

**Changed:** `Assets/Game/Scripts/UI/MonsterLogicApp.cs`

Settings now includes **MANAGE PRIVACY CHOICES**. It calls the existing `IAdService.ShowPrivacyOptions` abstraction, which delegates to MAX CMP's `ShowCmpForExistingUser` when the CMP supports it. If the chosen CMP is not ready or does not support revisiting choices, the user receives a safe in-app status message instead of a silent no-op.

This resolves the previously missing UI path, but it does not replace the need to configure a compliant CMP and consent flow in the AppLovin Integration Manager.

### 2. Keep Settings legal links aligned with ad configuration

**Changed:** `Assets/Game/Scripts/UI/MonsterLogicApp.cs`

The Settings screen now uses HTTPS privacy-policy and terms URLs from `AdsConfig` when they are valid. It falls back to the existing Zemo Labs links for placeholder or malformed configuration. The release validator now rejects a configuration whose normalized `AdsConfig` legal URLs differ from the MAX consent-flow URLs, preventing the two surfaces from silently drifting apart.

### 3. Add an explicit test-build diagnostics entry point

**Changed:**

- `Assets/Game/Scripts/Ads/MaxAdService.cs`
- `Assets/Game/Scripts/Ads/AdsConfig.cs`
- `Assets/Game/Scripts/UI/MonsterLogicApp.cs`

Only in a Unity Development build, when `developmentTestMode` is deliberately enabled and MAX has initialized, Settings exposes **OPEN AD DIAGNOSTICS**. It calls `MaxSdk.ShowMediationDebugger()` after the required initialization point. Nondevelopment player builds compile out both the verbose SDK logging enabled by this flag and the in-app diagnostics entry point.

Important limitations:

- This button is only a diagnostics/test-ad workflow entry point.
- It does **not** make an ad request safe, mark a device as a test device, or enable network test mode.
- Configure test mode/test devices in MAX and the mediated network before requesting ads.
- Use it during a direct Unity Development device test before uploading the normal TestFlight release candidate.

### 4. Make release validation platform-aware

**Changed:** `Assets/Game/Scripts/Ads/Editor/AdsBuildValidator.cs`

An iOS build now requires the iOS AdMob App ID, while an Android build requires the Android AdMob App ID. Previously the validator required both values for every mobile build, which could unnecessarily block an iOS-only TestFlight candidate even though only `GADApplicationIdentifier` for iOS is generated into that export.

### 5. Correct the existing production checklist

**Changed:** `Assets/Game/Docs/ADS_PRODUCTION_CHECKLIST.md`

The checklist now directs release testing to the new **Settings > Manage Privacy Choices** path rather than incorrectly stating that no UI caller existed.

## Current hard blockers for an ad-enabled TestFlight build

These are source-verified blockers. They require publisher/dashboard/legal/macOS actions, not guessed code changes.

| Priority | Blocker | Evidence / required action |
| --- | --- | --- |
| P0 | MAX runtime configuration is incomplete | `Assets/Game/Ads/Resources/AdsConfig.asset` has all iOS MAX unit IDs as `REPLACE_ME_*`; replace the rewarded, interstitial, and banner values with real **iOS MAX ad-unit IDs**. Do not use AdMob unit IDs in these fields. |
| P0 | Audience/privacy gate is closed | `generalAudienceAdsApproved` is `false`. Leave it false until the publisher has completed audience classification and privacy review. |
| P0 | SDK and AdMob credentials are missing | AppLovin SDK key and AdMob app IDs are blank in MAX Integration Manager settings. For an iOS TestFlight candidate, enter the SDK key and real iOS AdMob App ID; configure Android values before Android distribution. |
| P0 | Consent setup is incomplete | MAX consent flow is disabled and privacy/terms URLs are blank in AppLovin internal settings. Choose the consent architecture, configure valid HTTPS URLs, verify CMP support, and make the wording/legal behavior accurate for applicable regions. |
| P0 | iOS signing/distribution is unconfigured | `ProjectSettings/ProjectSettings.asset` has no Apple team/profile and iPhone build number is `0`. Configure signing in Unity/Xcode and choose a build number greater than any existing App Store Connect build. |
| P0 | Final iOS output is unverified | No generated Xcode project, `Info.plist`, `Podfile`, workspace, privacy manifest, or signed archive was available in this Windows checkout. Export and inspect them on macOS. |
| P1 | Test inventory is not configured | In a Unity Development build, `developmentTestMode` enables logs and the diagnostics entry point only; it does not configure test devices or mediated-network test mode. Nondevelopment players compile those diagnostics out. Use MAX/AdMob test configuration before testing. |
| P1 | ATT decision is unresolved | The game has no app-owned ATT prompt, and `NSUserTrackingUsageDescription` is generated only through the selected MAX consent flow. Decide whether the chosen privacy design needs ATT; do not add a prompt without the required product/legal basis and accurate text. |
| P1 | Duplicate MAX installation / GUID conflicts | Unity import reports duplicate GUIDs for `Packages/com.applovin.mediation.ads/*` and `Assets/MaxSdk/*`, with the immutable UPM assets ignored. Resolve the project to one supported MAX installation through the AppLovin migration/Integration Manager workflow before an iOS build; do not delete either copy blindly. |
| P1 | Resolver/version risk | The package lock resolves EDM4U 1.2.182 while `Assets/ExternalDependencyManager` contains 1.2.186. Resolve this to one supported installation in Unity before relying on CocoaPods output. |
| P1 | Store/domain work remains | Register the exact bundle ID `com.zemolabs.monsterlogic`, configure MAX/AdMob mappings, host required `app-ads.txt` records, and complete App Store privacy disclosures. |

## TestFlight plan for this IPA

### A. Configure a dedicated internal test build

1. In AppLovin MAX, register `com.zemolabs.monsterlogic` as the iOS app.
2. Create three iOS **MAX** ad units: rewarded, interstitial, and banner. Map Google/AdMob mediation demand to each intended unit.
3. In `Assets/Game/Ads/Resources/AdsConfig.asset`, enter the three iOS MAX IDs, real HTTPS legal URLs, and only after approval set `generalAudienceAdsApproved: 1`.
4. In **AppLovin > Integration Manager**, enter the AppLovin SDK key and the real iOS AdMob App ID. Configure the selected terms/privacy consent flow with the exact same legal URLs used by `AdsConfig`; the release validator rejects a mismatch.
5. Enable MAX/mediated-network test ads for the test iPhone. Do not use live ads or click a live creative. The checked-in SDK includes the test-device API, but the game intentionally does not hardcode IDFAs in source; use the MAX dashboard/diagnostics and network-supported test setup instead.
6. For a **direct Unity Development device test** only, set `developmentTestMode: 1`. This gives verbose MAX diagnostics and displays **OPEN AD DIAGNOSTICS** after MAX initializes. Use it to validate integration before creating the nondevelopment TestFlight candidate; it is not a test-mode substitute.
7. Run **Tools > Monsterdoku > Validate Ads Configuration** with iOS selected. A TestFlight candidate should pass the normal release gate; do not rely on the validator's Development-build warning exception to bypass missing configuration.

### B. Export and archive on macOS

1. Export the Unity iOS project and run the supported dependency/CocoaPods resolution.
2. Open the generated `.xcworkspace` when CocoaPods is used, rather than only the `.xcodeproj`.
3. Verify the generated output before archive:
   - `GADApplicationIdentifier` contains the real iOS AdMob **app** ID.
   - AppLovin settings include the SDK key.
   - `NSUserTrackingUsageDescription` is present and accurate if the selected consent design uses ATT.
   - `SKAdNetworkItems` contains the generated IDs for the installed mediation networks.
   - required privacy manifests, frameworks, and Pods are present without duplicate AppLovin/Google SDKs.
   - signing, entitlements, bundle ID, marketing version, and incremented build number are correct.
4. Install/run once from Xcode on the physical iPhone. Use a signed Distribution/Release archive with valid configuration for the TestFlight candidate.
5. Upload the archive to App Store Connect, add it to an internal TestFlight group first, then install it through the TestFlight app. Apple states that testers install beta builds through TestFlight; internal and external distribution are managed from the TestFlight tab in App Store Connect.

### C. Test on the TestFlight-installed app

1. Delete any older test copy if a clean-install test is needed, install the TestFlight build, and cold-launch it on the physical iPhone.
2. Complete/decline the configured consent path as applicable. Open **Settings > Manage Privacy Choices** and verify that it reopens the CMP choices view when supported.
3. The normal nondevelopment TestFlight player intentionally does not expose **OPEN AD DIAGNOSTICS**. Before upload, use a direct Unity Development device build to open the MAX Mediation Debugger and confirm the AppLovin SDK, Google adapter, and expected ad units are integrated.
4. In the TestFlight-installed build, validate that MAX/Google test-device configuration is active using device/Xcode logs and recognizably test inventory. Follow any MAX Test Ads restart instruction during the prior direct-development diagnostic pass; do not click a live creative.
5. Exercise every user-facing path below, recording device logs, screenshot, TestFlight build number, and MAX dashboard/debugger evidence for each failure.

| Scenario | Expected result |
| --- | --- |
| Hint rewarded ad | A hint changes only after the earned reward callback and ad close; early close/no fill/display failure grants no hint. |
| Villain-reveal rewarded ad | Exactly one valid unrevealed villain is placed only on an earned result. |
| Extra-heart rewarded ad | The paused board resumes with exactly one additional heart after earned-and-closed; retries and dismissal do not duplicate lives. |
| Level 9 completion | No interstitial opportunity. |
| Level 10+ completion | At most one already-loaded interstitial opportunity per genuine completion; closing/failure reveals result navigation immediately. |
| Banner eligibility | Banner begins only after the documented campaign/progress threshold; it stays outside touch targets and hides for overlays/fullscreen ads. |
| Consent/privacy | Legal links open the configured HTTPS URLs; privacy choices reopen if CMP supports them. |
| No-fill/offline path | Gameplay, reward alternatives, result navigation, and Settings remain usable. |
| Lifecycle | Background/resume and repeated taps do not duplicate callbacks/rewards; music and previous audio state restore after fullscreen ads. |
| Device layout | Verify safe area and adaptive banner height on a notch/home-indicator iPhone and a tablet-sized layout if supported. |

### D. Exit criteria before production

Do not move this TestFlight configuration to production until all of the following are true:

- MAX Mediation Debugger has no unresolved integration issue for the intended networks.
- All three iOS formats load **test** inventory on the TestFlight device.
- Every test-matrix row behaves as expected, including no-fill and early-close cases.
- Generated Xcode/Pod/plist/privacy-manifest checks pass on the archived build, not merely in source.
- Audience classification, consent/CMP behavior, legal URLs, app-ads.txt, and App Store privacy disclosures have owner approval.
- `developmentTestMode` is returned to `0` before the production build, and device/network test mode is disabled or separated from production traffic.

## Existing behavior assessed as sound

- Ads do not initialize in the Unity Editor or on unsupported platforms.
- Invalid config falls back to `NoOpAdService`; gameplay remains playable.
- MAX subscribes callbacks before SDK initialization, avoids duplicate active instances, retries rewarded/interstitial loads with backoff, and times out safely.
- Rewarded callbacks are fail-closed and session/level checks prevent stale callbacks from rewarding a different puzzle.
- Interstitial opportunities never block the results UI waiting for a load.
- Banner visibility is tied to gameplay/overlay/fullscreen state, and banner height is passed to the UI layout.
- Fullscreen ad handling preserves and restores prior `AudioListener` pause/volume state.

## Validation performed in this workspace

- Static diagnostics reported no C# errors in the modified ad/configuration/UI files.
- `git diff --check` reported no whitespace errors.
- Existing editor tests were inspected; they validate deterministic ad policy and reward state-machine behavior but cannot prove native iOS SDK initialization, CocoaPods resolution, real consent screens, or TestFlight delivery.
- The finite Unity EditMode suite was invoked twice but no test-results XML was produced. The rerun was blocked because another Unity instance already had this project open, so no test pass is claimed. Close the active Unity editor and rerun the suite before release.
- The first batch import also reported duplicate GUID conflicts between the UPM AppLovin package and `Assets/MaxSdk`; the UPM copy was ignored because it is immutable. This is a release risk that must be resolved through the supported AppLovin migration workflow.
- A real iOS export/archive and TestFlight run could not be performed here because the required Apple credentials, MAX/AdMob dashboard configuration, macOS/Xcode environment, and generated iOS artifacts are intentionally not present in the workspace.

## Primary files

- `Assets/Game/Scripts/Ads/MaxAdService.cs`
- `Assets/Game/Scripts/Ads/AdsConfig.cs`
- `Assets/Game/Scripts/Ads/Editor/AdsBuildValidator.cs`
- `Assets/Game/Scripts/UI/MonsterLogicApp.cs`
- `Assets/Game/Ads/Resources/AdsConfig.asset`
- `Assets/Game/Docs/ADS_PRODUCTION_CHECKLIST.md`
- `ProjectSettings/ProjectSettings.asset`

## References

- [AppLovin MAX Mediation Debugger](https://developers.applovin.com/en/ios/testing-networks/mediation-debugger) - use it after SDK initialization to inspect integrations and enable mediated-network test ads.
- [Apple TestFlight](https://developer.apple.com/testflight/) - App Store Connect workflow for uploading beta builds and distributing them to internal/external testers.

External documentation above is summarized rather than quoted. Content was rephrased for compliance with licensing restrictions.
