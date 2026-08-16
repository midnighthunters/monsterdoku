using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterLogic.Puzzle
{
    [CreateAssetMenu(fileName = "PuzzleLevelDatabase", menuName = "Monster Logic/Puzzle Level Database")]
    public sealed class PuzzleLevelDatabase : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public string contentVersion = "1.0.0";
        public List<PuzzleLevelData> levels = new List<PuzzleLevelData>(250);

        private Dictionary<string, PuzzleLevelData> _byId;

        public PuzzleLevelData GetById(string stableId)
        {
            EnsureIndex();
            return stableId != null && _byId.TryGetValue(stableId, out var level) ? level : null;
        }

        public PuzzleLevelData GetByNumber(int displayNumber)
        {
            return displayNumber >= 1 && displayNumber <= levels.Count ? levels[displayNumber - 1] : null;
        }

        public void EnsureIndex()
        {
            if (_byId != null && _byId.Count == levels.Count) return;
            _byId = new Dictionary<string, PuzzleLevelData>(StringComparer.Ordinal);
            foreach (var level in levels)
                if (level != null && !string.IsNullOrWhiteSpace(level.levelId)) _byId[level.levelId] = level;
        }

        public void MigrateIfNeeded()
        {
            if (schemaVersion < 1) schemaVersion = 1;
        }
    }
}
