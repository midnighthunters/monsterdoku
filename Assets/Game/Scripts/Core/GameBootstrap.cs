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
            var app = gameObject.AddComponent<MonsterLogicApp>();
            app.Initialize(database, save, new NoOpAdService(), displayFont, bodyFont);
        }
    }
}
