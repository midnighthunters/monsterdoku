# Monsterdoku Unity LevelPlay Setup

Monsterdoku uses Unity LevelPlay Ads Mediation through `com.unity.services.levelplay` version `9.5.1`. The game uses direct LevelPlay ad-unit APIs and keeps gameplay available when advertising is not configured.

## Current safe default

`Assets/Game/Ads/Resources/AdsConfig.asset` deliberately contains placeholders and sets `generalAudienceAdsApproved` to `false`. Runtime ads therefore remain disabled and `NoOpAdService` keeps the game playable. Do not enable ads until the publisher has completed audience, privacy, and store-review work.

## Dashboard setup

1. Register the exact Android application ID and iOS bundle ID in the Unity LevelPlay dashboard.
2. Create separate rewarded, interstitial, and banner ad units for Android and iOS.
3. Copy each platform app key and its three ad-unit IDs from the dashboard.
4. Configure the intended mediated networks, test devices, and test inventory in the dashboard and each network’s required console.
5. Publish accurate HTTPS privacy-policy and terms-of-service pages, complete the consent design, and prepare required store disclosures and `app-ads.txt` entries.

Do not invent identifiers, legal copy, consent choices, or test-device IDs in source control.

## Unity configuration

Update `Assets/Game/Ads/Resources/AdsConfig.asset` with:

- `androidAppKey` and `iosAppKey`;
- rewarded, interstitial, and banner ad-unit IDs for both platforms;
- real HTTPS privacy-policy and terms-of-service URLs;
- `generalAudienceAdsApproved: true` only after the publisher/privacy review is complete.

Leave `developmentTestMode` disabled for normal builds. For a direct Unity Development device test, enable it only after test inventory is configured; it enables LevelPlay adapter diagnostics and the in-app LevelPlay test suite, not test inventory itself.

`Tools > Monsterdoku > Validate Ads Configuration` checks the active mobile platform’s configuration and the installed LevelPlay package. A non-development Android or iOS build fails when the required configuration is incomplete.

## Runtime behavior

- Rewarded ads are opt-in rewards for one extra heart, a hint, or a villain reveal. A reward is granted only after LevelPlay reports it and the fullscreen ad closes.
- A post-level interstitial is offered only at the configured cadence after a completed level. The game never waits for an interstitial to load.
- The banner is adaptive, bottom-centered, safe-area aware, and shown only on eligible gameplay/navigation screens.
- Fullscreen ads hide the banner, preserve and restore audio state, block duplicate reward requests, and persist the game session before presentation.
- Impression-level revenue callbacks are queued onto the main thread before the game publishes them.

## Privacy and testing

The installed LevelPlay package exposes privacy consent flags but does not provide a built-in privacy-choices UI. The current service safely reports that action as unavailable. If the product requires users to revisit consent, integrate the selected CMP and implement that user-facing flow deliberately.

Use a physical Android device and iPhone for testing. In a Unity Development build with dashboard test inventory configured, open **Settings > Open Ad Diagnostics** to launch the LevelPlay test suite. Never exercise live advertising inventory as a test workflow.

## Required device verification

Verify rewarded success, early close, no fill, and display failure for all three rewards; verify the level-10-and-later interstitial policy; verify banner safe-area placement; verify background/resume, audio restoration, and duplicate-callback protection; and verify the offline/no-configuration fallback. Repeat the checks in a release candidate after all real dashboard values are present.
