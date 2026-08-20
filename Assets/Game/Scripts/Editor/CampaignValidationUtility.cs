#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonsterLogic.Puzzle;
using UnityEditor;
using UnityEngine;

namespace MonsterLogic.Editor
{
    public static class CampaignValidationUtility
    {
        private const string DatabasePath = "Assets/Game/Data/Resources/PuzzleLevelDatabase.asset";
        private const string ReportPath = "Assets/Game/Docs/CampaignDifficultyReport.csv";

        [MenuItem("Monster Logic/Level Design/Validate All Levels")]
        public static void ValidateAllLevels()
        {
            var database = AssetDatabase.LoadAssetAtPath<PuzzleLevelDatabase>(DatabasePath);
            if (database == null) throw new InvalidOperationException("PuzzleLevelDatabase asset is missing.");
            string result = Validate(database, true);
            if (!result.StartsWith("PASS", StringComparison.Ordinal)) throw new InvalidOperationException(result);
            Debug.Log(result);
        }

        [MenuItem("Monster Logic/Level Design/Export Difficulty Report")]
        public static void ExportDifficultyReport()
        {
            var database = AssetDatabase.LoadAssetAtPath<PuzzleLevelDatabase>(DatabasePath);
            if (database == null) throw new InvalidOperationException("PuzzleLevelDatabase asset is missing.");
            string result = Validate(database, false);
            if (!result.StartsWith("PASS", StringComparison.Ordinal)) throw new InvalidOperationException(result);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            var rows = new List<string> { "Level,BoardSize,DifficultyBand,DifficultyScore,StarterCats,LogicalSteps,InitialForcedMoves,LongestChain,Archetype,UniqueSolution,LogicOnlySolvable,ValidationStatus" };
            foreach (var level in database.levels)
            {
                var report = PuzzleValidator.Validate(level);
                rows.Add(string.Join(",", level.displayNumber, level.gridSize, level.difficultyBand, level.difficultyScore, level.starterCatCells?.Length ?? 0, report.deductionSteps, report.initialForcedMoves, report.maxChainDepth, level.archetype, report.solutionCount == 1, report.valid, report.valid ? "PASS" : "FAIL"));
            }
            File.WriteAllLines(ReportPath, rows);
            AssetDatabase.Refresh();
            Debug.Log($"{result} Report: {ReportPath}");
        }

        public static string Validate(PuzzleLevelDatabase database, bool logEachFailure)
        {
            if (database == null || database.levels == null || database.levels.Count != 250) return "FAIL: Campaign must contain exactly 250 levels.";
            var ids = new HashSet<string>(); var hashes = new HashSet<string>(); var shapes = new HashSet<string>();
            var expectedSizes = new[] { 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 10, 10, 11, 11, 11, 11 };
            var errors = new List<string>();
            for (int index = 0; index < database.levels.Count; index++)
            {
                var level = database.levels[index];
                var report = PuzzleValidator.Validate(level);
                bool shapeUnique = level != null && shapes.Add(PuzzleGenerator.CanonicalSymmetryKey(level));
                bool valid = report.valid && level != null && level.displayNumber == index + 1 && level.levelId == $"campaign-{index + 1:000}" && ids.Add(level.levelId) && hashes.Add(level.contentHash) && shapeUnique && level.gridSize == ExpectedSize(index + 1);
                if (!valid) errors.Add($"Level {index + 1}: {report}");
            }
            if (errors.Count == 0) return "PASS: 250 stable-ID levels are unique, logical, connected, and size-progression compliant.";
            if (logEachFailure) foreach (var error in errors) Debug.LogError(error);
            return $"FAIL: {errors.Count} campaign validation errors.";
        }

        private static int ExpectedSize(int level) => level <= 45 ? 6 : level <= 100 ? 7 : level <= 160 ? 8 : level <= 215 ? 9 : level <= 235 ? 10 : 11;
    }
}
#endif