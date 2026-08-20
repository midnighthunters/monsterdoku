using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterLogic.Ads
{
    [CreateAssetMenu(fileName = "AdsConfig", menuName = "Monster Logic/Ads Configuration")]
    public sealed class AdsConfig : ScriptableObject
    {
        [Header("Release gates")]
        [Tooltip("Master publisher switch. The audience approval gate below must also be enabled.")]
        public bool adsEnabled = true;
        [Tooltip("Keep false until the publisher confirms this is a general-audience app and completes the privacy review.")]
        public bool generalAudienceAdsApproved;
        [Tooltip("Enables adapter diagnostics and the in-app LevelPlay test suite only in a Unity Development build. Test devices and test inventory must still be configured in the LevelPlay dashboard and mediated networks.")]
        public bool developmentTestMode;

        [Header("Android LevelPlay")]
        public string androidAppKey = "REPLACE_ME_ANDROID_LEVELPLAY_APP_KEY";
        public string androidRewardedAdUnitId = "REPLACE_ME_ANDROID_LEVELPLAY_REWARDED";
        public string androidInterstitialAdUnitId = "REPLACE_ME_ANDROID_LEVELPLAY_INTERSTITIAL";
        public string androidBannerAdUnitId = "REPLACE_ME_ANDROID_LEVELPLAY_BANNER";

        [Header("iOS LevelPlay")]
        public string iosAppKey = "REPLACE_ME_IOS_LEVELPLAY_APP_KEY";
        public string iosRewardedAdUnitId = "REPLACE_ME_IOS_LEVELPLAY_REWARDED";
        public string iosInterstitialAdUnitId = "REPLACE_ME_IOS_LEVELPLAY_INTERSTITIAL";
        public string iosBannerAdUnitId = "REPLACE_ME_IOS_LEVELPLAY_BANNER";

        [Header("Progression")]
        [Min(1)] public int bannerUnlockCompletedLevel = 3;
        [Min(1)] public int interstitialStartCompletedLevel = 10;
        [Min(1)] public int interstitialEveryNLevelCompletions = 1;

        [Header("Privacy links")]
        public string privacyPolicyUrl = "REPLACE_ME_HTTPS_PRIVACY_POLICY";
        public string termsOfServiceUrl = "REPLACE_ME_HTTPS_TERMS";

        public string LevelPlayAppKey => SelectPlatform(androidAppKey, iosAppKey);
        public string RewardedAdUnitId => SelectPlatform(androidRewardedAdUnitId, iosRewardedAdUnitId);
        public string InterstitialAdUnitId => SelectPlatform(androidInterstitialAdUnitId, iosInterstitialAdUnitId);
        public string BannerAdUnitId => SelectPlatform(androidBannerAdUnitId, iosBannerAdUnitId);

        public bool IsRuntimeReady(out string reason)
        {
            if (!adsEnabled) { reason = "Ads are disabled in AdsConfig."; return false; }
            if (!generalAudienceAdsApproved) { reason = "The general-audience advertising approval gate is closed."; return false; }
#if UNITY_ANDROID && !UNITY_EDITOR
            return FinishRuntimeValidation(GetValidationErrors(true), out reason);
#elif UNITY_IOS && !UNITY_EDITOR
            return FinishRuntimeValidation(GetValidationErrors(false), out reason);
#else
            reason = "LevelPlay runtime ads are disabled on this platform.";
            return false;
#endif
        }

        private static bool FinishRuntimeValidation(List<string> errors, out string reason)
        {
            if (errors.Count > 0) { reason = string.Join(" ", errors); return false; }
            reason = string.Empty;
            return true;
        }

        public List<string> GetValidationErrors(bool android)
        {
            var errors = new List<string>();
            if (!adsEnabled) return errors;
            if (!generalAudienceAdsApproved) errors.Add("generalAudienceAdsApproved is false; publisher audience/privacy review is required.");
            ValidateValue(android ? androidAppKey : iosAppKey, android ? "Android LevelPlay app key" : "iOS LevelPlay app key", errors);
            ValidateValue(android ? androidRewardedAdUnitId : iosRewardedAdUnitId, android ? "Android rewarded ad unit ID" : "iOS rewarded ad unit ID", errors);
            ValidateValue(android ? androidInterstitialAdUnitId : iosInterstitialAdUnitId, android ? "Android interstitial ad unit ID" : "iOS interstitial ad unit ID", errors);
            ValidateValue(android ? androidBannerAdUnitId : iosBannerAdUnitId, android ? "Android banner ad unit ID" : "iOS banner ad unit ID", errors);
            ValidateUrl(privacyPolicyUrl, "Privacy policy", errors);
            ValidateUrl(termsOfServiceUrl, "Terms of service", errors);
            if (bannerUnlockCompletedLevel < 1) errors.Add("Banner unlock level must be at least 1.");
            if (interstitialStartCompletedLevel < 1) errors.Add("Interstitial start level must be at least 1.");
            if (interstitialEveryNLevelCompletions < 1) errors.Add("Interstitial cadence must be at least 1.");
            return errors;
        }

        private static string SelectPlatform(string android, string ios)
        {
#if UNITY_ANDROID
            return android;
#elif UNITY_IOS
            return ios;
#else
            return string.Empty;
#endif
        }

        private static void ValidateValue(string value, string label, ICollection<string> errors)
        {
            if (IsPlaceholder(value)) errors.Add(label + " is missing or a placeholder.");
        }

        private static void ValidateUrl(string value, string label, ICollection<string> errors)
        {
            if (IsPlaceholder(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                errors.Add(label + " URL must be a real HTTPS URL.");
        }

        public static bool IsPlaceholder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string normalized = value.Trim().ToUpperInvariant();
            return normalized.Contains("REPLACE_ME") || normalized.Contains("YOUR_") || normalized.Contains("SAMPLE") || normalized.Contains("TEST_ID");
        }
    }
}
