# Unity LevelPlay Ads Release Plan

**Status:** The Unity project migration is complete; production advertising remains blocked until the publisher completes dashboard, privacy, and device-validation work.

## 1. Approve the product and privacy design

1. Confirm the target audience with the publisher and privacy owner.
2. Publish final HTTPS privacy-policy and terms-of-service pages that accurately describe advertising, mediation, consent, and data use.
3. Choose and configure the required consent-management approach for applicable regions.
4. Decide whether users need a privacy-choices revisit flow and integrate the selected CMP when required.
5. Complete `app-ads.txt`, store privacy disclosures, and Google Play data-safety information.
6. Approve `generalAudienceAdsApproved` only after these decisions are complete.

## 2. Configure Unity LevelPlay

1. Register Android and iOS applications for `com.zemolabs.monsterlogic` in the Unity LevelPlay dashboard.
2. Create rewarded, interstitial, and banner ad units for each platform.
3. Configure intended mediation networks and their required account, privacy, and test-inventory settings.
4. Capture the two app keys and six ad-unit IDs outside source control.
5. Configure test devices/inventory in the dashboard and mediated networks; do not commit identifiers.

## 3. Configure the Unity project

1. Update `Assets/Game/Ads/Resources/AdsConfig.asset` with real LevelPlay app keys and platform-specific ad-unit IDs.
2. Replace legal URL placeholders with the approved HTTPS URLs.
3. Set `generalAudienceAdsApproved: true` only after the review in step 1.
4. Keep `developmentTestMode: false` by default. Enable it only for a direct Unity Development device test that needs the LevelPlay test suite.
5. Run **Tools > Monsterdoku > Validate Ads Configuration** for Android and iOS. Fix every reported error before a non-development build.

## 4. Verify on physical devices

1. Create a Unity Development build with recognized test inventory active.
2. Use **Settings > Open Ad Diagnostics** to launch the LevelPlay test suite when diagnostics are needed.
3. Verify rewarded extra-heart, hint, and villain-reveal behavior; rewards must require both earning and closure.
4. Verify no-fill, close, load failure, display failure, lifecycle, and duplicate-callback paths.
5. Verify interstitial cadence, adaptive banner safe-area layout, fullscreen audio restoration, session persistence, and offline fallback.
6. Restore `developmentTestMode: false` before preparing the release candidate.

## 5. Release criteria

- The configuration validator passes with real platform values.
- All intended formats pass with test inventory on representative Android and iOS devices.
- The release candidate has verified native output, signing, privacy/store disclosures, and correct bundle identifiers.
- Publisher and privacy owners have approved the final configuration.
