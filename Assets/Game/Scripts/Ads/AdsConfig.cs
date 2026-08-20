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
        [Tooltip("Enables verbose MAX diagnostics and the in-app Mediation Debugger only in a Unity Development build. Test devices/test mode must still be configured in MAX and AdMob.")]
        public bool developmentTestMode;

        [Header("Android MAX ad units")]
        public string androidRewardedAdUnitId = "REPLACE_ME_ANDROID_MAX_REWARDED";
        public string androidInterstitialAdUnitId = "REPLACE_ME_ANDROID_MAX_INTERSTITIAL";
        public string androidBannerAdUnitId = "REPLACE_ME_ANDROID_MAX_BANNER";

        [Header("iOS MAX ad units")]
        public string iosRewardedAdUnitId = "REPLACE_ME_IOS_MAX_REWARDED";
        public string iosInterstitialAdUnitId = "REPLACE_ME_IOS_MAX_INTERSTITIAL";
        public string iosBannerAdUnitId = "REPLACE_ME_IOS_MAX_BANNER";

        [Header("Progression")]
        [Min(1)] public int bannerUnlockCompletedLevel = 3;
        [Min(1)] public int interstitialStartCompletedLevel = 10;
        [Min(1)] public int interstitialEveryNLevelCompletions = 1;

        [Header("Consent flow validation")]
        public string privacyPolicyUrl = "REPLACE_ME_HTTPS_PRIVACY_POLICY";
        public string termsOfServiceUrl = "REPLACE_ME_HTTPS_TERMS";

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
            reason = "MAX runtime ads are disabled on this platform.";
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
            ValidateId(android ? androidRewardedAdUnitId : iosRewardedAdUnitId, android ? "Android rewarded" : "iOS rewarded", errors);
            ValidateId(android ? androidInterstitialAdUnitId : iosInterstitialAdUnitId, android ? "Android interstitial" : "iOS interstitial", errors);
            ValidateId(android ? androidBannerAdUnitId : iosBannerAdUnitId, android ? "Android banner" : "iOS banner", errors);
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

        private static void ValidateId(string value, string label, ICollection<string> errors)
        {
            if (IsPlaceholder(value)) errors.Add(label + " MAX ad unit ID is missing or a placeholder.");
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
