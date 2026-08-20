# Monsterdoku Unity LevelPlay Production Checklist

## Current status: not ready for production advertising

The project is intentionally fail-closed. `AdsConfig.asset` contains placeholder LevelPlay app keys and ad-unit IDs, `generalAudienceAdsApproved` is `false`, and no publisher credentials or dashboard test configuration are stored in the repository.

Do not ship an ad-enabled Android or iOS build until every applicable item below is complete.

## 1. Publisher, audience, and privacy

- [ ] Confirm the audience classification with the publisher and privacy owner.
- [ ] Complete the privacy review before enabling `generalAudienceAdsApproved`.
- [ ] Publish real HTTPS privacy-policy and terms-of-service pages.
- [ ] Ensure the pages accurately describe advertising, mediation, consent, and data use.
- [ ] Select and configure the required consent-management approach for applicable regions.
- [ ] Implement a user-facing privacy-choices flow if the selected consent design requires one.
- [ ] Complete required `app-ads.txt`, App Store privacy, and Google Play data-safety work.

## 2. Unity LevelPlay dashboard

- [ ] Register `com.zemolabs.monsterlogic` for Android and iOS in the Unity LevelPlay dashboard.
- [ ] Create rewarded, interstitial, and banner ad units for each platform.
- [ ] Configure the intended mediation networks and verify each network’s account and privacy requirements.
- [ ] Record the Android and iOS app keys.
- [ ] Record all six platform/format ad-unit IDs.
- [ ] Configure physical-device test inventory without committing device identifiers to the repository.

## 3. Unity project configuration

Update `Assets/Game/Ads/Resources/AdsConfig.asset`:

- [ ] Replace both LevelPlay app-key placeholders.
- [ ] Replace all rewarded, interstitial, and banner ad-unit placeholders.
- [ ] Replace both legal-URL placeholders with real HTTPS URLs.
- [ ] Set `generalAudienceAdsApproved: true` only after the completed review.
- [ ] Keep `developmentTestMode: false` except for a direct Unity Development device test.

Then:

- [ ] Run **Tools > Monsterdoku > Validate Ads Configuration** for each target platform.
- [ ] Confirm `com.unity.services.levelplay` is installed at the pinned project version.
- [ ] Build a non-development mobile player only after the validator has no errors.

## 4. Physical-device test procedure

The Unity Editor uses `NoOpAdService`; validate the SDK only in a device build.

- [ ] Configure dashboard and mediated-network test inventory first.
- [ ] Create a Unity Development build and temporarily enable `developmentTestMode` if adapter diagnostics are needed.
- [ ] Open **Settings > Open Ad Diagnostics** and use the LevelPlay test suite to inspect initialization and ad-unit setup.
- [ ] Verify the rewarded extra-heart, hint, and villain-reveal flows.
- [ ] Confirm a reward requires both a reward event and ad closure.
- [ ] Confirm close, no-fill, and display-failure paths grant no reward and recover safely.
- [ ] Confirm level 9 has no interstitial opportunity and level 10+ follows the configured cadence.
- [ ] Confirm the adaptive banner respects safe areas, hides for overlays/fullscreen ads, and does not overlap tap targets.
- [ ] Confirm pause/resume, pre-muted audio, no-network behavior, and repeated callbacks are safe.
- [ ] Restore `developmentTestMode: false` before the release candidate.

## 5. Release exit criteria

- [ ] The mobile validator passes with real configuration.
- [ ] All intended advertising formats passed with recognized test inventory on real devices.
- [ ] The release candidate’s native output, privacy disclosures, signing, store metadata, and bundle identifiers were verified.
- [ ] The publisher/privacy owner approved the final configuration.
