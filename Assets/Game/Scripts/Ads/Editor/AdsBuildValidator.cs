using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                Debug.LogWarning(message + "\nDevelopment build is allowed, but LevelPlay remains fail-closed until configuration is valid.");
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
            RequirePackage(manifest, "com.unity.services.levelplay", "Unity LevelPlay Ads Mediation", errors);
        }

        private static void RequirePackage(string manifest, string package, string label, ICollection<string> errors)
        {
            if (!manifest.Contains('"' + package + '"', StringComparison.Ordinal)) errors.Add(label + " package is not installed.");
        }
    }
}
