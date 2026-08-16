#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
using System.IO;
using TMPro;

namespace MonsterLogic.EditorTools
{
    public sealed class MonsterLogicAssetPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Game/Art/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = assetPath.EndsWith("MonsterHeads.png") ? 2048 : 1024;
        }

        [MenuItem("Monster Logic/Configure Product Settings")]
        public static void ConfigureProduct()
        {
            PlayerSettings.productName = "Monster Logic"; PlayerSettings.companyName = "Zemo Labs";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.zemolabs.monsterlogic");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.zemolabs.monsterlogic");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/UI/AppIcon.png");
            if (icon != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Android, new[] { icon }, IconKind.Application);
                PlayerSettings.SetIcons(NamedBuildTarget.iOS, new[] { icon }, IconKind.Application);
            }
            AssetDatabase.SaveAssets(); Debug.Log("Monster Logic product name, identifiers, portrait orientation, and app icon configured.");
        }

        [MenuItem("Monster Logic/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            string source = Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(source, false);
            Debug.Log("TextMeshPro essential resources imported for Monster Logic.");
        }
    }
}
#endif
