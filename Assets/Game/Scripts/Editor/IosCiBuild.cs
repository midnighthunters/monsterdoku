using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MonsterLogic.Editor
{
    /// <summary>
    /// Command-line iOS exporter used by the GitHub Actions workflows.
    /// Run with -executeMethod MonsterLogic.Editor.IosCiBuild.Build.
    /// </summary>
    public static class IosCiBuild
    {
        private const string DefaultBuildPath = "Builds/iOS";

        public static void Build()
        {
            string buildPath = Path.GetFullPath(GetArgument("-buildPath") ?? DefaultBuildPath);
            string buildNumber = RequirePositiveInteger("-buildNumber");
            string version = GetArgument("-marketingVersion");
            bool developmentBuild = GetBooleanArgument("-developmentBuild", false);
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && File.Exists(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new BuildFailedException("No enabled, existing scenes are configured in ProjectSettings/EditorBuildSettings.asset.");

            if (!string.IsNullOrWhiteSpace(version) && !IsValidVersion(version))
                throw new BuildFailedException("-marketingVersion must contain only digits and dots, for example 1.0.1.");

            string originalBuildNumber = PlayerSettings.iOS.buildNumber;
            string originalVersion = PlayerSettings.bundleVersion;

            try
            {
                Directory.CreateDirectory(buildPath);
                PlayerSettings.iOS.buildNumber = buildNumber;
                if (!string.IsNullOrWhiteSpace(version)) PlayerSettings.bundleVersion = version;

                var options = BuildOptions.StrictMode;
                if (developmentBuild) options |= BuildOptions.Development;

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = buildPath,
                    target = BuildTarget.iOS,
                    options = options
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"iOS Xcode export failed with result {report.summary.result}. See the Unity log for details.");

                Debug.Log($"iOS Xcode project exported to {buildPath}. Version={PlayerSettings.bundleVersion}, build={PlayerSettings.iOS.buildNumber}, development={developmentBuild}.");
            }
            finally
            {
                PlayerSettings.iOS.buildNumber = originalBuildNumber;
                PlayerSettings.bundleVersion = originalVersion;
            }
        }

        private static string RequirePositiveInteger(string name)
        {
            string value = GetArgument(name);
            if (!int.TryParse(value, out int number) || number < 1)
                throw new BuildFailedException($"{name} must be a positive integer.");
            return number.ToString();
        }

        private static bool GetBooleanArgument(string name, bool defaultValue)
        {
            string value = GetArgument(name);
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            if (bool.TryParse(value, out bool result)) return result;
            throw new BuildFailedException($"{name} must be true or false.");
        }

        private static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index < arguments.Length - 1 ? arguments[index + 1] : null;
        }

        private static bool IsValidVersion(string value)
        {
            return value.Split('.').All(segment => !string.IsNullOrEmpty(segment) && segment.All(char.IsDigit));
        }
    }
}
