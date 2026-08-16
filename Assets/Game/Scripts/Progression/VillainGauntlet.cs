using System;
using UnityEngine;

namespace MonsterLogic.Progression
{
    /// <summary>
    /// Pure campaign mapping for the villain gauntlet. A tier is ten levels long and
    /// the six-villain order repeats every sixty levels, so it remains valid for
    /// campaign extensions beyond the authored 250 puzzles.
    /// </summary>
    public static class VillainGauntlet
    {
        public const int LevelsPerTier = 10;
        public const int VillainsPerCycle = 6;

        private static readonly VillainDefinition[] Roster =
        {
            // Sheet and sprite names deliberately identify the imported slices rather
            // than relying on Resources.LoadAll ordering. Medusa is sheet 1, top-right.
            new VillainDefinition("medusa", "MEDUSA", "villains/ChatGPT Image Aug 16, 2026, 12_59_21 PM (1)", "ChatGPT Image Aug 16, 2026, 12_59_21 PM (1)_1", "34E6A1"),
            new VillainDefinition("werewolf", "MOON WEREWOLF", "villains/ChatGPT Image Aug 16, 2026, 12_59_21 PM (2)", "ChatGPT Image Aug 16, 2026, 12_59_21 PM (2)_0", "66D9FF"),
            new VillainDefinition("knight", "VOID KNIGHT", "villains/ChatGPT Image Aug 16, 2026, 12_59_21 PM (3)", "ChatGPT Image Aug 16, 2026, 12_59_21 PM (3)_0", "A96CFF"),
            new VillainDefinition("mummy", "MOON MUMMY", "villains/ChatGPT Image Aug 16, 2026, 12_59_21 PM (4)", "ChatGPT Image Aug 16, 2026, 12_59_21 PM (4)_2", "62F4DF"),
            new VillainDefinition("spider", "SPIDER QUEEN", "villains/ChatGPT Image Aug 16, 2026, 12_59_21 PM (5)", "ChatGPT Image Aug 16, 2026, 12_59_21 PM (5)_4", "BE79FF"),
            new VillainDefinition("pumpkin", "PUMPKIN LORD", "villains/ChatGPT Image Aug 16, 2026, 12_59_21 PM (6)", "ChatGPT Image Aug 16, 2026, 12_59_21 PM (6)_1", "FF9A3D")
        };

        public static int RosterCount => Roster.Length;

        public static VillainTier Resolve(int levelNumber)
        {
            int safeLevel = Mathf.Max(1, levelNumber);
            int absoluteTier = (safeLevel - 1) / LevelsPerTier;
            int rosterIndex = absoluteTier % Roster.Length;
            int firstLevel = absoluteTier * LevelsPerTier + 1;
            return new VillainTier(Roster[rosterIndex], absoluteTier, safeLevel, firstLevel, firstLevel + LevelsPerTier - 1);
        }
    }

    public readonly struct VillainDefinition
    {
        public readonly string id;
        public readonly string displayName;
        public readonly string resourcePath;
        public readonly string spriteName;
        public readonly string accentHex;

        public VillainDefinition(string id, string displayName, string resourcePath, string spriteName, string accentHex)
        {
            this.id = id;
            this.displayName = displayName;
            this.resourcePath = resourcePath;
            this.spriteName = spriteName;
            this.accentHex = accentHex;
        }
    }

    public readonly struct VillainTier
    {
        public readonly VillainDefinition villain;
        public readonly int tierIndex;
        public readonly int levelNumber;
        public readonly int firstLevel;
        public readonly int lastLevel;

        public VillainTier(VillainDefinition villain, int tierIndex, int levelNumber, int firstLevel, int lastLevel)
        {
            this.villain = villain;
            this.tierIndex = tierIndex;
            this.levelNumber = levelNumber;
            this.firstLevel = firstLevel;
            this.lastLevel = lastLevel;
        }

        public int LevelProgress => Mathf.Clamp(levelNumber - firstLevel + 1, 1, VillainGauntlet.LevelsPerTier);
        public bool IsFirstLevel => levelNumber == firstLevel;
        public string AcknowledgementId => $"{villain.id}-tier-{firstLevel:000}";
        public string LevelRangeLabel => $"LEVELS {firstLevel}–{lastLevel}";
    }
}
