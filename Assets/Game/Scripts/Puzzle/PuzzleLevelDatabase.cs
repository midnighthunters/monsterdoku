using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterLogic.Puzzle
{
    [CreateAssetMenu(fileName = "PuzzleLevelDatabase", menuName = "Monster Logic/Puzzle Level Database")]
    public sealed class PuzzleLevelDatabase : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;
        public int schemaVersion = CurrentSchemaVersion;
        public string contentVersion = "2.0.0";
        public List<PuzzleLevelData> levels = new List<PuzzleLevelData>(250);

        private Dictionary<string, PuzzleLevelData> _byId;

        public PuzzleLevelData GetById(string stableId)
        {
            EnsureIndex();
            return stableId != null && _byId.TryGetValue(stableId, out var level) ? level : null;
        }

        public PuzzleLevelData GetByNumber(int displayNumber)
        {
            if (displayNumber < 1) return null;
            EnsureIndex();
            return levels.Find(level => level != null && level.displayNumber == displayNumber);
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
            if (schemaVersion < CurrentSchemaVersion) schemaVersion = CurrentSchemaVersion;
        }
    }
}
