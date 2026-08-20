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
        [SerializeField] private TMP_FontAsset displayFont;
        [SerializeField] private TMP_FontAsset bodyFont;
        [SerializeField] private AudioClip everytimeMusic;
        [SerializeField] private AudioClip matchSound;

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
                var levelPlayAds = gameObject.AddComponent<LevelPlayAdService>();
                levelPlayAds.Configure(adsConfig, adPolicy);
                ads = levelPlayAds;
            }
            else
            {
                Debug.Log("LevelPlay ads remain disabled: " + adsDisabledReason);
            }
#endif
            var app = gameObject.AddComponent<MonsterLogicApp>();
            app.Initialize(database, save, ads, adPolicy, displayFont, bodyFont, everytimeMusic, matchSound);
            ads.Initialize();
        }
    }
}
