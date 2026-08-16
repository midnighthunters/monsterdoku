using MonsterLogic.Ads;
using MonsterLogic.Puzzle;
using MonsterLogic.Services;
using MonsterLogic.UI;
using TMPro;
using UnityEngine;

namespace MonsterLogic.Core
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Typography")]
        [SerializeField] private TMP_FontAsset displayFont;
        [SerializeField] private TMP_FontAsset bodyFont;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
#if UNITY_EDITOR
            Screen.SetResolution(900, 1600, false);
#endif
            var database = Resources.Load<PuzzleLevelDatabase>("PuzzleLevelDatabase");
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<PuzzleLevelDatabase>();
                database.levels = PuzzleGenerator.GenerateCampaign();
                Debug.LogWarning("Campaign database asset was missing; generated an in-memory fallback.");
            }
            database.MigrateIfNeeded();
            var save = new SaveService();
            var adsConfig = Resources.Load<AdsConfig>("AdsConfig");
            var adPolicy = AdPolicy.FromConfig(adsConfig);
            IAdService ads = new NoOpAdService();
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            string adsDisabledReason = "AdsConfig is missing.";
            bool adsReady = adsConfig != null && adsConfig.IsRuntimeReady(out adsDisabledReason);
            if (adsReady)
            {
                var maxAds = gameObject.AddComponent<MaxAdService>();
                maxAds.Configure(adsConfig, adPolicy);
                ads = maxAds;
            }
            else
            {
                Debug.Log("MAX ads remain disabled: " + adsDisabledReason);
            }
#endif
            var app = gameObject.AddComponent<MonsterLogicApp>();
            app.Initialize(database, save, ads, adPolicy, displayFont, bodyFont);
            ads.Initialize();
        }
    }
}
