# Monsterdoku Ads Setup

Monsterdoku uses AppLovin MAX as its only advertising runtime. Google AdMob participates only through the MAX Google Bidding and Google AdMob adapters. Do not install the standalone Google Mobile Ads Unity plugin and do not call `MobileAds.Initialize`.

## Installed dependencies

- AppLovin MAX Unity plugin: `com.applovin.mediation.ads` 8.6.4
- MAX Google AdMob Android adapter: `com.applovin.mediation.adapters.google.android` 25040000.0.0
- MAX Google AdMob iOS adapter: `com.applovin.mediation.adapters.google.ios` 13070000.0.0
- External Dependency Manager is pulled transitively by MAX.

These versions were resolved from AppLovin's official scoped registry for Unity 6000.0.42f1. Upgrade them through **AppLovin > Integration Manager** and re-run device/build verification together; do not update only one Google platform adapter without checking compatibility.

## Release blockers and safe defaults

`Assets/Game/Ads/Resources/AdsConfig.asset` deliberately ships with placeholder ad-unit IDs and `generalAudienceAdsApproved` disabled. MAX does not initialize in this state, and the offline game remains fully playable. A non-development Android or iOS build with ads enabled fails validation until the following work is complete:

1. Classify the app/store audience and confirm it is permitted to use MAX. AppLovin currently prohibits MAX use in child-directed apps and for users who qualify as children. Only the publisher/legal owner can make this decision.
2. Complete the privacy and consent design. Google demand in the EEA/UK requires a Google-certified CMP that integrates the applicable IAB TCF requirements.
3. Publish real HTTPS privacy-policy and terms pages with AppLovin and mediated-advertising disclosures.
4. Supply every dashboard/app/ad-unit identifier below.
5. Validate store privacy/data-safety declarations for both platforms.

This checklist is an engineering release gate, not legal advice.

## AppLovin and AdMob dashboards

1. Register the exact Android application ID and iOS bundle ID in both AppLovin and AdMob. Confirm the identifiers in Unity Player Settings before creating dashboard records.
2. In MAX, create separate Android and iOS ad units for each format:
   - rewarded;
   - interstitial;
   - banner.
3. In the MAX dashboard, connect Google bidding/AdMob demand and map the matching AdMob inventory to every MAX ad unit. MAX remains the mediator.
4. Enter the six resulting MAX ad-unit IDs in `AdsConfig.asset`. Never put an AdMob ad-unit ID in those fields.
5. Add or update the `app-ads.txt` entries required by both AppLovin and Google and verify the hosted file.
6. Configure MAX test mode/test devices and the corresponding AdMob test-device setup before requesting development ads. Never click a live ad.

## Unity Integration Manager

Open **AppLovin > Integration Manager** and complete all of the following:

1. Enter the AppLovin SDK key.
2. Confirm **Google Bidding and Google AdMob** is installed for Android and iOS. Enter the real Android and iOS AdMob App IDs in the adapter fields.
3. Enable the current MAX terms/privacy policy flow (or use an approved Google-certified CMP architecture). Enter the real HTTPS privacy-policy and terms URLs. Keep those URLs consistent with `AdsConfig.asset`.
4. Resolve Android and iOS dependencies using the supported resolver. Do not copy arbitrary AARs, frameworks, CocoaPods output, Gradle caches, or signing material into the repository.
5. Use the MAX Mediation Debugger on a test device to confirm the Google adapter initializes and all three formats map to the intended MAX ad units.

The **Tools > Monsterdoku > Validate Ads Configuration** menu reports missing source/package/Integration Manager values. Release mobile builds run the same checks automatically.

## Runtime policy

- Rewarded ads are explicit opt-ins for a hint, one correctly placed villain, or one extra heart.
- Rewards are granted only after MAX sends the reward-earned callback and the ad closes. Early close, no fill, load/display failure, stale callbacks, and shutdown grant nothing.
- The banner unlocks only after `campaign-003` is complete (or a migrated save has `highestUnlocked >= 4`). It is hidden on loading, results, settings, unlock, out-of-hearts, and fullscreen-ad states.
- Starting with completion of level 10, each genuine completion creates one already-loaded interstitial opportunity at the result break. The game never waits for an interstitial to load.
- MAX/network creatives control playable format, duration, and close controls. The client does not simulate playable-only ads or force a ten-second duration.
- The timer, input, game audio, banner, and navigation remain paused/blocked only for the fullscreen presentation flow and restore to their prior state afterward.

## Platform checks

### Android

- Keep the target SDK on **Automatic (highest installed)** or a value that satisfies both the installed Google Mobile Ads SDK and the current Google Play requirement. The validator rejects an explicit target below API 34; current store requirements may be higher.
- Keep Jetifier enabled if the resolved dependency set requires it.
- Run the External Dependency Manager Android resolver and inspect the exported Gradle dependency graph for duplicates.

### iOS

- Preserve the existing deployment target unless the installed SDK explicitly requires a higher one.
- Export the Xcode project and run the supported CocoaPods resolution on macOS.
- Inspect the plugin-generated privacy/SKAdNetwork configuration. Do not maintain a copied, stale SKAdNetwork list manually.

## Required manual device verification

Use MAX test mode and Mediation Debugger on an Android phone, an iPhone with a home indicator/notch, and one tablet-sized layout if supported. Verify:

- rewarded success, early close, no fill, and display failure for all three rewards;
- revive resumes the same board with exactly one heart;
- level 9 has no interstitial and level 10+ has one result-break opportunity per completion;
- interstitial close/display failure exposes result navigation immediately;
- app background/resume does not duplicate rewards or callbacks;
- a pre-muted player remains muted after fullscreen ads;
- banner safe area, rotation, adaptive height, and separation from every tap target;
- Google appears as mediated demand through MAX for rewarded, interstitial, and banner inventory.

Do not use live ad requests in automated tests. The edit-mode suite uses a deterministic fake service and makes no network calls.

## Official references

- [MAX Unity integration](https://support.applovin.com/en/max/unity/overview/integration)
- [Preparing mediated networks](https://support.applovin.com/en/max/unity/preparing-mediated-networks)
- [Rewarded ads](https://support.applovin.com/en/max/unity/ad-formats/rewarded-ads)
- [Interstitial ads](https://support.applovin.com/en/max/unity/ad-formats/interstitial-ads)
- [Banner and MREC ads](https://support.applovin.com/en/max/unity/ad-formats/banner-and-mrec-ads)
- [MAX terms and privacy policy flow](https://support.applovin.com/en/max/unity/overview/terms-and-privacy-policy-flow)
- [MAX privacy requirements](https://support.applovin.com/en/max/unity/overview/privacy)
