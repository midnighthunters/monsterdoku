# Ads integration plan

**Status: blocked until publisher, privacy, and iOS release configuration are completed.** This plan is based on `ADS_PRODUCTION_CHECKLIST.md`, `ADS_INTEGRATION_AUDIT.md`, `AdsConfig.asset`, and `AdsBuildValidator.cs`. The game already uses AppLovin MAX with Google/AdMob mediated through MAX; do not add the standalone Google Mobile Ads Unity SDK.

## Current baseline

The gameplay integration, reward protection, privacy-settings entry point, and pre-build validator already exist. Ads are deliberately fail-closed because `AdsConfig.asset` still has placeholder MAX IDs and legal URLs, `generalAudienceAdsApproved` is off, and the MAX Integration Manager lacks the SDK key, iOS AdMob app ID, and enabled consent flow. `ProjectSettings/ProjectSettings.asset` also has no Apple team/profile and iPhone build number `0`.

## Step-by-step completion list

### 1. Approve the product and privacy design

1. Confirm the audience classification with the publisher and legal/privacy owner.
2. Publish final HTTPS Privacy Policy and Terms of Service pages. They must accurately cover ads, MAX mediation, Google demand, consent, and data use.
3. Choose and approve one consent architecture for applicable regions: MAX’s terms/privacy consent flow or an approved Google-certified CMP.
4. Determine whether ATT is required for the approved design. If it is, approve accurate `NSUserTrackingUsageDescription` copy; do not add an ATT prompt without that decision.
5. Complete `app-ads.txt` for the authorized publisher domain and prepare App Store privacy disclosures.
6. Only after these approvals, allow `generalAudienceAdsApproved` to become true.

### 2. Create and connect publisher inventory

1. Register iOS app ID `com.zemolabs.monsterlogic` in AppLovin MAX and AdMob. Register Android separately before Android release.
2. In MAX, create three **iOS MAX** ad units: rewarded, interstitial, and banner.
3. Connect Google/AdMob demand through MAX and map the correct AdMob inventory to each MAX unit.
4. Record the AppLovin SDK key, iOS AdMob **App ID**, and the three iOS **MAX unit IDs**. MAX unit IDs belong in the game config; do not put AdMob ad-unit IDs there.
5. Configure MAX and mediated-network test inventory for the physical test device. Keep test device identifiers out of the repository.

### 3. Complete Unity and MAX configuration

1. In **AppLovin > Integration Manager**, enter the AppLovin SDK key and iOS AdMob App ID.
2. Enable the selected consent flow and enter the exact same HTTPS privacy and terms URLs selected in step 1.
3. Configure the approved ATT usage text when applicable.
4. Update `Assets/Game/Ads/Resources/AdsConfig.asset` with the three real iOS MAX IDs and both HTTPS legal URLs.
5. Set `generalAudienceAdsApproved: 1` only after step 1 is approved. Keep `developmentTestMode: 0` by default; set it to `1` only in a direct Unity Development build that needs verbose logs and the Mediation Debugger.
6. Resolve the duplicate MAX/EDM4U installations reported by Unity through the supported AppLovin Integration Manager migration path. Do not delete `Assets/MaxSdk` or the UPM package blindly.
7. Run the supported iOS dependency resolver after package changes and confirm only the MAX-mediated Google adapters remain installed.
8. In Unity, run **Tools > Monsterdoku > Validate Ads Configuration** with iOS selected. Fix every error; a non-development mobile build must not rely on the validator’s development-build warning exception.

### 4. Test direct on an iPhone before TestFlight

1. Create a signed Unity **Development** build with device/network test mode active. The Unity Editor uses `NoOpAdService`, so Editor Play Mode cannot validate MAX.
2. If needed, temporarily set `developmentTestMode: 1`, install on the registered iPhone, and open **Settings > Open Ad Diagnostics**.
3. Use the MAX Mediation Debugger to verify MAX, the Google iOS adapter, and the expected ad units. Confirm recognizably test inventory and never click a live ad.
4. Validate rewarded hint, villain reveal, and extra-heart flows. Rewards must occur only after an earned reward and ad close; close/no-fill/display failure must grant nothing.
5. Validate policy behavior: no interstitial before level 10, one result opportunity at level 10+, and banner eligibility after the configured campaign threshold.
6. Validate privacy choices, legal links, no-fill/offline behavior, lifecycle callbacks, audio restoration, and banner safe-area layout.
7. Restore `developmentTestMode: 0` before creating the non-development TestFlight candidate.

### 5. Prepare the iOS archive

1. Set up Apple Developer/App Store Connect for `com.zemolabs.monsterlogic` and choose automatic signing or valid manual distribution profiles.
2. Choose a monotonically increasing iPhone build number that is greater than every existing App Store Connect build.
3. Export from Unity on macOS, run CocoaPods when a `Podfile` is generated, and open the generated `.xcworkspace` when present.
4. Inspect the generated project/archive: AppLovin SDK setting, `GADApplicationIdentifier`, consent/ATT data when applicable, `SKAdNetworkItems`, privacy manifests, required pods/frameworks, and absence of duplicate AppLovin/Google frameworks.
5. Verify the final bundle ID, version, build number, signing, and entitlements on a physical device.

### 6. Use the included GitHub Actions workflows

Both workflows run on a self-hosted macOS runner labeled `unity`; the TestFlight workflow additionally uses the protected `testflight` environment. The runner must have Unity **6000.0.42f1** with iOS Build Support, Xcode, CocoaPods, an activated Unity license, and access to Apple’s signing services. The workflows intentionally do not place credentials in source control.

| Workflow | Use | Inputs | Required GitHub configuration |
| --- | --- | --- | --- |
| `.github/workflows/ios-ipa.yml` | Build, sign, export, and retain an IPA. | Version, positive build number, development toggle, export method. | Repository variable `MONSTERDOKU_UNITY_PATH`; secrets `APPLE_TEAM_ID`, `APP_STORE_CONNECT_KEY_ID`, `APP_STORE_CONNECT_ISSUER_ID`, `APP_STORE_CONNECT_PRIVATE_KEY`. |
| `.github/workflows/ios-testflight.yml` | Build a release IPA and upload it to App Store Connect/TestFlight. | Version and positive build number. | Same variable/secrets, stored in the protected `testflight` environment. |

Run the simple IPA workflow for internal/device validation. It can produce a Development archive when configuration is intentionally incomplete, but that archive keeps MAX fail-closed. Run the TestFlight workflow only after the validator and direct-device test both pass; it always makes a non-development `app-store` archive and uploads with Xcode’s iTMSTransporter. Uploading does not assign tester groups, request external beta review, or submit an App Store release—complete those actions in App Store Connect.

### 7. Release exit criteria

- The iOS validator reports no errors for the committed release configuration.
- Test inventory passed on a real device for all three formats and every product behavior above.
- The non-development IPA has the verified native plist, privacy, mediation, signing, and entitlement output.
- Publisher/privacy/legal owners approved audience classification, consent behavior, URLs, `app-ads.txt`, and App Store disclosures.
- The TestFlight build is uploaded, installed from TestFlight, and tested once more on a physical device.
