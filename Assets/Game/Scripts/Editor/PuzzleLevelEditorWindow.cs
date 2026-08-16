#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonsterLogic.Puzzle;
using UnityEditor;
using UnityEngine;

namespace MonsterLogic.EditorTools
{
    public sealed class PuzzleLevelEditorWindow : EditorWindow
    {
        [Serializable] private sealed class JsonWrapper { public int schemaVersion = 1; public List<PuzzleLevelData> levels = new List<PuzzleLevelData>(); }
        private const string DatabasePath = "Assets/Game/Data/Resources/PuzzleLevelDatabase.asset";
        private PuzzleLevelDatabase _database; private int _selected; private Vector2 _scroll; private string _status = "Ready.";

        [MenuItem("Monster Logic/Level Workshop")]
        public static void Open() => GetWindow<PuzzleLevelEditorWindow>("Level Workshop");

        [MenuItem("Monster Logic/Generate Campaign Database")]
        public static void GenerateCampaignMenu()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Monster Logic", "Generating 250 deterministic puzzles…", .15f);
                var levels = PuzzleGenerator.GenerateCampaign();
                EditorUtility.DisplayProgressBar("Monster Logic", "Creating database asset…", .85f);
                var database = AssetDatabase.LoadAssetAtPath<PuzzleLevelDatabase>(DatabasePath);
                if (database == null) { database = CreateInstance<PuzzleLevelDatabase>(); AssetDatabase.CreateAsset(database, DatabasePath); }
                database.schemaVersion = PuzzleLevelDatabase.CurrentSchemaVersion; database.contentVersion = "1.0.0"; database.levels = levels;
                EditorUtility.SetDirty(database); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                var summary = ValidateDatabase(database, false);
                Debug.Log($"Monster Logic campaign generated: {summary}");
                Selection.activeObject = database;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Monster Logic/Validate All 250 Levels")]
        public static void ValidateAllMenu()
        {
            var database = AssetDatabase.LoadAssetAtPath<PuzzleLevelDatabase>(DatabasePath);
            if (database == null) { Debug.LogError("Generate the campaign database first."); return; }
            Debug.Log("Monster Logic all-level validation: " + ValidateDatabase(database, true));
        }

        private void OnEnable() => _database = AssetDatabase.LoadAssetAtPath<PuzzleLevelDatabase>(DatabasePath);

        private void OnGUI()
        {
            EditorGUILayout.Space(8); EditorGUILayout.LabelField("Monster Logic · Campaign Workshop", EditorStyles.boldLabel);
            _database = (PuzzleLevelDatabase)EditorGUILayout.ObjectField("Database", _database, typeof(PuzzleLevelDatabase), false);
            if (_database == null) { if (GUILayout.Button("Generate 250 Levels")) GenerateCampaignMenu(); return; }
            EditorGUILayout.LabelField($"Schema {_database.schemaVersion} · {_database.levels.Count} campaign entries");
            _selected = EditorGUILayout.IntSlider("Preview level", _selected + 1, 1, Math.Max(1, _database.levels.Count)) - 1;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate selected")) _status = PuzzleValidator.Validate(Current).ToString();
                if (GUILayout.Button("Validate all")) _status = ValidateDatabase(_database, true);
                if (GUILayout.Button("Regenerate from seed")) RegenerateSelected();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Toggle starter at solution row 0")) ToggleStarter();
                if (GUILayout.Button("Recalculate difficulty")) RecalculateDifficulty();
                if (GUILayout.Button("Play selected")) PlaySelected();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export JSON")) ExportJson();
                if (GUILayout.Button("Import JSON")) ImportJson();
                if (GUILayout.Button("Detect symmetry duplicates")) _status = DuplicateSummary(_database);
            }
            EditorGUILayout.HelpBox(_status, _status.StartsWith("VALID") || _status.Contains("250 valid") ? MessageType.Info : MessageType.None);
            DrawPreview(Current);
        }

        private PuzzleLevelData Current => _database != null && _database.levels.Count > 0 ? _database.levels[Mathf.Clamp(_selected, 0, _database.levels.Count - 1)] : null;

        private void DrawPreview(PuzzleLevelData level)
        {
            if (level == null) return; _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField($"{level.levelId} · Chapter {level.chapterId} · {level.gridSize}×{level.gridSize} · seed {level.generationSeed}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{level.backgroundTheme} · {level.characterTheme} · {level.difficultyTier} {level.difficultyScore}");
            int n = level.gridSize; const float size = 38f; Rect board = GUILayoutUtility.GetRect(n * size, n * size);
            Color[] colors = { new Color(.76f,.82f,.95f), new Color(.87f,.76f,.93f), new Color(.72f,.87f,.84f), new Color(.95f,.75f,.69f), new Color(.82f,.89f,.70f), new Color(.91f,.78f,.85f), new Color(.73f,.84f,.88f), new Color(.82f,.76f,.91f) };
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++)
            {
                int cell = r * n + c, region = level.regionIdByCell[cell]; Rect rect = new Rect(board.x + c * size, board.y + r * size, size - 2, size - 2);
                EditorGUI.DrawRect(rect, colors[region % colors.Length]); GUI.Label(rect, level.solutionColumnByRow[r] == c ? (level.IsLocked(cell) ? "◆" : "●") : region.ToString(), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontStyle = level.solutionColumnByRow[r] == c ? FontStyle.Bold : FontStyle.Normal });
            }
            EditorGUILayout.LabelField("● canonical monster   ◆ locked starter   number = region ID"); EditorGUILayout.EndScrollView();
        }

        private void RegenerateSelected()
        {
            var old = Current; _database.levels[_selected] = PuzzleGenerator.Generate(old.displayNumber, old.chapterId, old.gridSize, old.generationSeed); SaveDatabase(); _status = "Regenerated deterministically from stored seed.";
        }
        private void ToggleStarter()
        {
            var level = Current; int cell = level.solutionColumnByRow[0]; var list = (level.lockedMonsterCells ?? Array.Empty<int>()).ToList(); if (list.Contains(cell)) list.Remove(cell); else list.Add(cell); level.lockedMonsterCells = list.ToArray(); SaveDatabase(); _status = PuzzleValidator.Validate(level).ToString();
        }
        private void RecalculateDifficulty()
        {
            var report = PuzzleValidator.Validate(Current); Current.difficultyScore = report.difficultyScore; Current.expectedTechniques = report.techniques.ToArray(); SaveDatabase(); _status = report.ToString();
        }
        private void PlaySelected()
        {
            PlayerPrefs.SetInt("MonsterLogic.PlayLevel", Current.displayNumber); EditorApplication.EnterPlaymode();
        }
        private void ExportJson()
        {
            string path = EditorUtility.SaveFilePanel("Export Monster Logic levels", Application.dataPath, "monster-logic-levels", "json"); if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonUtility.ToJson(new JsonWrapper { schemaVersion = _database.schemaVersion, levels = _database.levels }, true)); _status = "Exported human-readable JSON.";
        }
        private void ImportJson()
        {
            string path = EditorUtility.OpenFilePanel("Import Monster Logic levels", Application.dataPath, "json"); if (string.IsNullOrEmpty(path)) return;
            var wrapper = JsonUtility.FromJson<JsonWrapper>(File.ReadAllText(path)); if (wrapper?.levels == null || wrapper.levels.Count != 250) { _status = "Import rejected: expected exactly 250 levels."; return; }
            var temp = CreateInstance<PuzzleLevelDatabase>(); temp.levels = wrapper.levels; string result = ValidateDatabase(temp, false); DestroyImmediate(temp); if (!result.StartsWith("250 valid")) { _status = "Import rejected: " + result; return; }
            _database.levels = wrapper.levels; _database.schemaVersion = wrapper.schemaVersion; SaveDatabase(); _status = "Imported and validated 250 levels.";
        }
        private void SaveDatabase() { EditorUtility.SetDirty(_database); AssetDatabase.SaveAssets(); }

        public static string ValidateDatabase(PuzzleLevelDatabase database, bool progress)
        {
            if (database.levels.Count != 250) return $"Expected 250 levels, found {database.levels.Count}.";
            var ids = new HashSet<string>(); var symmetry = new HashSet<string>(); int invalid = 0;
            try
            {
                for (int i = 0; i < database.levels.Count; i++)
                {
                    if (progress) EditorUtility.DisplayProgressBar("Monster Logic validation", $"Level {i + 1} / 250", i / 250f);
                    var level = database.levels[i]; var report = PuzzleValidator.Validate(level);
                    if (!report.valid || level.displayNumber != i + 1 || !ids.Add(level.levelId) || !symmetry.Add(PuzzleGenerator.CanonicalSymmetryKey(level))) { invalid++; Debug.LogError($"Level {i + 1}: {report}"); }
                }
            }
            finally { if (progress) EditorUtility.ClearProgressBar(); }
            return invalid == 0 ? $"250 valid, unique, connected, uniquely solvable levels. {symmetry.Count} symmetry-distinct maps." : $"{invalid} invalid levels; inspect Console.";
        }

        private static string DuplicateSummary(PuzzleLevelDatabase database)
        {
            int duplicates = database.levels.GroupBy(PuzzleGenerator.CanonicalSymmetryKey).Count(g => g.Count() > 1); return duplicates == 0 ? "No rotation/reflection/recolour duplicates." : $"Found {duplicates} duplicate symmetry groups.";
        }
    }
}
#endif
