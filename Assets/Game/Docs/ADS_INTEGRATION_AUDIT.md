# Unity LevelPlay Ads Integration Audit

**Audit date:** 2026-08-21
**Scope:** Unity runtime integration, configuration gates, ad behavior, cleanup, and release readiness.

## Executive verdict

The project now uses Unity LevelPlay Ads Mediation through `com.unity.services.levelplay` `9.5.1`. The migration removes the retired mediation runtime, its package registry, settings, generated metadata, and historical integration documents.

The application remains deliberately fail-closed. No real app keys, ad-unit IDs, test-device identifiers, consent configuration, legal URLs, or signing credentials were fabricated. Until the publisher supplies them and opens the approval gate, the offline game remains playable through `NoOpAdService`.

## Verified implementation

| Area | Current implementation |
| --- | --- |
| Initialization | `LevelPlay.Init(appKey)` after `AdsConfig.IsRuntimeReady` accepts the active mobile platform |
| Ad units | `LevelPlayRewardedAd`, `LevelPlayInterstitialAd`, and `LevelPlayBannerAd` |
| Reward policy | A reward completes only after the earned callback and fullscreen close; failure and dismissal do not reward |
| Banner | Adaptive, bottom-centered, safe-area aware, and hidden while overlays or fullscreen ads are active |
| Interstitial policy | Post-level cadence enforced through the existing vendor-neutral `AdPolicy` |
| Diagnostics | `LevelPlay.LaunchTestSuite()` is exposed only for Unity Development builds with `developmentTestMode` enabled |
| Revenue | Per-unit impression data is queued to the Unity main thread before publication |
| Safe fallback | Incomplete configuration, unsupported platforms, and initialization failures leave ads disabled |

## Migration changes

- Added `LevelPlayAdService` as the concrete implementation of the existing `IAdService` contract.
- Added Android and iOS LevelPlay app-key fields to `AdsConfig`; retained platform-specific ad-unit fields and fail-closed validation.
- Updated the bootstrap, diagnostics UI, and build validator to use LevelPlay.
- Pinned the project package to `com.unity.services.levelplay` `9.5.1`.
- Removed the former SDK folder, package registry, project settings, runtime wrapper, and historical test metadata.
- Replaced setup, production checklist, audit, and release-plan documents with LevelPlay guidance.

## Remaining external work

1. Create the app and ad units in the Unity LevelPlay dashboard for both platforms.
2. Configure selected mediation networks, test inventory, and any network-specific privacy requirements.
3. Supply real app keys, ad-unit IDs, and HTTPS legal URLs to `AdsConfig.asset`.
4. Complete audience, privacy, consent, store-disclosure, signing, and release approval work.
5. Perform a physical-device test for all three ad formats and product behaviors before a release build.

## Privacy limitation

The installed LevelPlay package exposes privacy consent settings but no native privacy-choices screen. The app’s current `IAdService` implementation therefore safely reports privacy choices as unavailable. If the selected consent architecture requires a revisit flow, implement it with the chosen CMP instead of presenting a nonexistent SDK UI.
