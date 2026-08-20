using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppLovinMax.Scripts.IntegrationManager.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MonsterLogic.Ads.Editor
{
    public sealed class AdsBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android && report.summary.platform != BuildTarget.iOS) return;
            var errors = CollectErrors(report.summary.platform);
            if (errors.Count == 0) return;

            string message = "Monsterdoku ads configuration is incomplete:\n - " + string.Join("\n - ", errors);
            if ((report.summary.options & BuildOptions.Development) != 0)
            {
                Debug.LogWarning(message + "\nDevelopment build is allowed, but MAX remains fail-closed until configuration is valid.");
                return;
            }
            throw new BuildFailedException(message);
        }

        [MenuItem("Tools/Monsterdoku/Validate Ads Configuration")]
        public static void ValidateFromMenu()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? BuildTarget.Android : BuildTarget.iOS;
            var errors = CollectErrors(target);
            if (errors.Count == 0) Debug.Log("Monsterdoku ads configuration is valid for " + target + ".");
            else Debug.LogError("Monsterdoku ads configuration is incomplete for " + target + ":\n - " + string.Join("\n - ", errors));
        }

        public static List<string> CollectErrors(BuildTarget target)
        {
            var errors = new List<string>();
            var config = Resources.Load<AdsConfig>("AdsConfig");
            if (config == null)
            {
                errors.Add("Assets/Game/Ads/Resources/AdsConfig.asset is missing.");
                return errors;
            }
            if (!config.adsEnabled) return errors;

            errors.AddRange(config.GetValidationErrors(target == BuildTarget.Android));
            ValidatePackageManifest(errors);
            ValidateIntegrationManager(errors, target);

            if (target == BuildTarget.Android)
            {
                int targetSdk = (int)PlayerSettings.Android.targetSdkVersion;
                if (targetSdk > 0 && targetSdk < 34)
                    errors.Add("Android target SDK is below API 34; use Automatic (highest installed) or API 34+ and satisfy the current Google Play requirement.");
            }
            return errors.Distinct().ToList();
        }

        private static void ValidatePackageManifest(ICollection<string> errors)
        {
            string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
            string manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : string.Empty;
            RequirePackage(manifest, "com.applovin.mediation.ads", "AppLovin MAX Unity plugin", errors);
            RequirePackage(manifest, "com.applovin.mediation.adapters.google.android", "MAX Google AdMob Android adapter", errors);
            RequirePackage(manifest, "com.applovin.mediation.adapters.google.ios", "MAX Google AdMob iOS adapter", errors);
            if (manifest.Contains("com.google.ads.mobile", StringComparison.OrdinalIgnoreCase))
                errors.Add("A standalone Google Mobile Ads Unity runtime appears in Packages; AdMob must only be installed as a MAX-mediated adapter.");
        }

        private static void ValidateIntegrationManager(ICollection<string> errors, BuildTarget target)
        {
            try
            {
                var config = Resources.Load<AdsConfig>("AdsConfig");
                var settings = AppLovinSettings.Instance;
                if (AdsConfig.IsPlaceholder(settings.SdkKey)) errors.Add("AppLovin SDK key is missing in AppLovin > Integration Manager.");

                if (target == BuildTarget.Android && AdsConfig.IsPlaceholder(settings.AdMobAndroidAppId))
                    errors.Add("Android AdMob App ID is missing in AppLovin > Integration Manager.");
                if (target == BuildTarget.iOS && AdsConfig.IsPlaceholder(settings.AdMobIosAppId))
                    errors.Add("iOS AdMob App ID is missing in AppLovin > Integration Manager.");

                var privacy = AppLovinInternalSettings.Instance;
                if (!privacy.ConsentFlowEnabled) errors.Add("MAX terms and privacy policy flow is not enabled in Integration Manager.");
                if (!IsHttps(privacy.ConsentFlowPrivacyPolicyUrl)) errors.Add("MAX consent-flow privacy policy URL is missing or not HTTPS.");
                if (!IsHttps(privacy.ConsentFlowTermsOfServiceUrl)) errors.Add("MAX consent-flow terms URL is missing or not HTTPS.");

                if (config != null && IsHttps(config.privacyPolicyUrl) && IsHttps(privacy.ConsentFlowPrivacyPolicyUrl) && !UrlsMatch(config.privacyPolicyUrl, privacy.ConsentFlowPrivacyPolicyUrl))
                    errors.Add("AdsConfig privacy policy URL must match the MAX consent-flow privacy policy URL.");
                if (config != null && IsHttps(config.termsOfServiceUrl) && IsHttps(privacy.ConsentFlowTermsOfServiceUrl) && !UrlsMatch(config.termsOfServiceUrl, privacy.ConsentFlowTermsOfServiceUrl))
                    errors.Add("AdsConfig terms URL must match the MAX consent-flow terms URL.");
            }
            catch (Exception exception)
            {
                errors.Add("AppLovin Integration Manager settings could not be validated: " + exception.Message);
            }
        }

        private static bool UrlsMatch(string left, string right)
        {
            if (!Uri.TryCreate(left, UriKind.Absolute, out var leftUri)) return false;
            if (!Uri.TryCreate(right, UriKind.Absolute, out var rightUri)) return false;

            return string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(leftUri.Host, rightUri.Host, StringComparison.OrdinalIgnoreCase)
                && leftUri.Port == rightUri.Port
                && string.Equals(leftUri.UserInfo, rightUri.UserInfo, StringComparison.Ordinal)
                && string.Equals(leftUri.PathAndQuery, rightUri.PathAndQuery, StringComparison.Ordinal)
                && string.Equals(leftUri.Fragment, rightUri.Fragment, StringComparison.Ordinal);
        }

        private static void RequirePackage(string manifest, string package, string label, ICollection<string> errors)
        {
            if (!manifest.Contains('"' + package + '"', StringComparison.Ordinal)) errors.Add(label + " package is not installed.");
        }

        private static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !AdsConfig.IsPlaceholder(value);
    }
}
