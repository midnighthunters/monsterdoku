using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterLogic.Puzzle
{
    public enum DifficultyTier { Tutorial, Easy, Medium, Hard, Expert }

    [Serializable]
    public sealed class PuzzleLevelData
    {
        public string levelId;
        public int chapterId;
        public int displayNumber;
        public int gridSize;
        public int[] regionIdByCell;
        public int[] solutionColumnByRow;
        public string characterTheme;
        public string backgroundTheme;
        public DifficultyTier difficultyTier;
        public int difficultyScore;
        public string[] expectedTechniques;
        public int generationSeed;
        public int parTimeSeconds;
        public int contentVersion = 1;
        public int[] starterCatCells = Array.Empty<int>();
        public string contentHash;
        public string difficultyBand;
        public string archetype;
        public int logicalSteps;
        public int maxChainDepth;
        public int initialForcedMoves;

        public int Cell(int row, int column) => row * gridSize + column;
        public int Region(int row, int column) => regionIdByCell[Cell(row, column)];
        public bool IsSolutionCell(int cell) => solutionColumnByRow[cell / gridSize] == cell % gridSize;
        public bool IsStarterCat(int cell) => starterCatCells != null && Array.IndexOf(starterCatCells, cell) >= 0;
    }

    [Serializable]
    public sealed class ValidationReport
    {
        public bool valid;
        public int solutionCount;
        public int deductionSteps;
        public int difficultyScore;
        public int initialForcedMoves;
        public int maxChainDepth;
        public int averageRegionCandidates;
        public int maximumRegionCandidates;
        public int solverNodes;
        public float similarityToPrevious;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> techniques = new List<string>();
        public override string ToString() => valid ? $"VALID | unique | score {difficultyScore} | {deductionSteps} steps" : string.Join("\n", errors);
    }
}
