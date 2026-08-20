using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MonsterLogic.Puzzle
{
    public static class PuzzleRules
    {
        public static bool IsCanonicalSolutionValid(PuzzleLevelData level)
        {
            int n = level.gridSize;
            if (n < 1 || level.solutionColumnByRow == null || level.solutionColumnByRow.Length != n) return false;
            var columns = new bool[n];
            var regions = new bool[n];
            for (int row = 0; row < n; row++)
            {
                int column = level.solutionColumnByRow[row];
                if (column < 0 || column >= n || columns[column]) return false;
                int region = level.Region(row, column);
                if (region < 0 || region >= n || regions[region]) return false;
                if (row > 0 && Math.Abs(column - level.solutionColumnByRow[row - 1]) <= 1) return false;
                columns[column] = true;
                regions[region] = true;
            }
            return true;
        }

        public static string ContentHash(PuzzleLevelData level)
        {
            unchecked
            {
                uint hash = 2166136261;
                void Add(int value) { hash = (hash ^ (uint)value) * 16777619; }
                Add(level.gridSize); Add(level.contentVersion);
                foreach (int value in level.regionIdByCell ?? Array.Empty<int>()) Add(value);
                foreach (int value in level.solutionColumnByRow ?? Array.Empty<int>()) Add(value);
                foreach (int value in level.starterCatCells ?? Array.Empty<int>()) Add(value);
                return hash.ToString("X8");
            }
        }

        public static bool PlacementsSatisfyAll(PuzzleLevelData level, bool[] monsters)
        {
            int n = level.gridSize;
            if (monsters == null || monsters.Length != n * n) return false;
            var rows = new int[n]; var columns = new int[n]; var regions = new int[n];
            for (int cell = 0; cell < monsters.Length; cell++)
            {
                if (!monsters[cell]) continue;
                int r = cell / n, c = cell % n, g = level.regionIdByCell[cell];
                rows[r]++; columns[c]++; regions[g]++;
                for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int rr = r + dr, cc = c + dc;
                    if (rr >= 0 && rr < n && cc >= 0 && cc < n && monsters[rr * n + cc]) return false;
                }
            }
            return rows.All(x => x == 1) && columns.All(x => x == 1) && regions.All(x => x == 1);
        }

        public static bool IsRegionConnected(PuzzleLevelData level, int region)
        {
            int n = level.gridSize, first = Array.IndexOf(level.regionIdByCell, region);
            if (first < 0) return false;
            var seen = new bool[n * n]; var queue = new Queue<int>();
            queue.Enqueue(first); seen[first] = true; int count = 0;
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(); count++; int r = cell / n, c = cell % n;
                Add(r - 1, c); Add(r + 1, c); Add(r, c - 1); Add(r, c + 1);
            }
            return count == level.regionIdByCell.Count(x => x == region);
            void Add(int r, int c)
            {
                if (r < 0 || r >= n || c < 0 || c >= n) return;
                int idx = r * n + c;
                if (!seen[idx] && level.regionIdByCell[idx] == region) { seen[idx] = true; queue.Enqueue(idx); }
            }
        }
    }

    public static class PuzzleSolver
    {
        public static int CountSolutions(PuzzleLevelData level, int earlyExit = 2, int[] requiredCells = null, List<int[]> output = null)
        {
            int n = level.gridSize, count = 0;
            var usedColumns = new bool[n]; var usedRegions = new bool[n]; var permutation = new int[n];
            var requiredColumnByRow = new int[n]; for (int i = 0; i < n; i++) requiredColumnByRow[i] = -1;
            foreach (int cell in requiredCells ?? Array.Empty<int>()) requiredColumnByRow[cell / n] = cell % n;
            Search(0);
            return count;

            void Search(int row)
            {
                if (count >= earlyExit) return;
                if (row == n) { count++; output?.Add((int[])permutation.Clone()); return; }
                int start = requiredColumnByRow[row] >= 0 ? requiredColumnByRow[row] : 0;
                int end = requiredColumnByRow[row] >= 0 ? start + 1 : n;
                for (int column = start; column < end; column++)
                {
                    int region = level.Region(row, column);
                    if (usedColumns[column] || usedRegions[region]) continue;
                    if (row > 0 && Math.Abs(column - permutation[row - 1]) <= 1) continue;
                    permutation[row] = column; usedColumns[column] = true; usedRegions[region] = true;
                    Search(row + 1);
                    usedColumns[column] = false; usedRegions[region] = false;
                }
            }
        }

        public static bool TryGetUniqueSolution(PuzzleLevelData level, out int[] solution)
        {
            var list = new List<int[]>(2); int count = CountSolutions(level, 2, null, list);
            solution = count == 1 ? list[0] : null; return count == 1;
        }
    }

    public static class DifficultyAnalyser
    {
        public static void Analyse(PuzzleLevelData level, ValidationReport report)
        {
            int n = level.gridSize;
            var placed = new bool[n * n]; var excluded = new bool[n * n];
            foreach (int cell in level.starterCatCells ?? Array.Empty<int>()) if (cell >= 0 && cell < placed.Length) placed[cell] = true;
            int steps = 0, contradictionSteps = 0;
            report.initialForcedMoves = placed.Count(x => x);
            while (placed.Count(x => x) < n && steps < n * n * 2)
            {
                ApplyDirectEliminations();
                int forced = FindSingle();
                if (forced >= 0)
                {
                    placed[forced] = true; steps++; AddTechnique("Single candidate"); continue;
                }
                int eliminated = FindProvablyImpossibleCell();
                if (eliminated < 0) break;
                excluded[eliminated] = true; steps++; contradictionSteps++; AddTechnique("Controlled contradiction");
            }
            report.deductionSteps = steps;
            report.maxChainDepth = Math.Max(1, steps - contradictionSteps);
            report.difficultyScore = n * 18 + steps * 3 + contradictionSteps * 9 - report.initialForcedMoves * 4;
            if (placed.Count(x => x) != n) report.errors.Add("Human-style analyser did not complete the puzzle.");

            void ApplyDirectEliminations()
            {
                for (int cell = 0; cell < placed.Length; cell++) if (placed[cell])
                {
                    int r = cell / n, c = cell % n, g = level.regionIdByCell[cell];
                    for (int i = 0; i < n; i++) { if (r * n + i != cell) excluded[r * n + i] = true; if (i * n + c != cell) excluded[i * n + c] = true; }
                    for (int i = 0; i < n * n; i++) if (i != cell && level.regionIdByCell[i] == g) excluded[i] = true;
                    for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                    { int rr = r + dr, cc = c + dc; if (rr >= 0 && rr < n && cc >= 0 && cc < n && (dr != 0 || dc != 0)) excluded[rr * n + cc] = true; }
                }
            }
            int FindSingle()
            {
                for (int r = 0; r < n; r++) { int v = Single(Enumerable.Range(0, n).Select(c => r * n + c)); if (v >= 0) return v; }
                for (int c = 0; c < n; c++) { int v = Single(Enumerable.Range(0, n).Select(r => r * n + c)); if (v >= 0) return v; }
                for (int g = 0; g < n; g++) { int gg = g; int v = Single(Enumerable.Range(0, n * n).Where(i => level.regionIdByCell[i] == gg)); if (v >= 0) return v; }
                return -1;
            }
            int Single(IEnumerable<int> cells)
            {
                var candidates = cells.Where(i => !excluded[i] && !placed[i]).Take(2).ToArray();
                return candidates.Length == 1 ? candidates[0] : -1;
            }
            int FindProvablyImpossibleCell()
            {
                var required = placed.Select((v, i) => (v, i)).Where(x => x.v).Select(x => x.i).ToList();
                for (int cell = 0; cell < n * n; cell++)
                {
                    if (placed[cell] || excluded[cell]) continue;
                    required.Add(cell); int count = PuzzleSolver.CountSolutions(level, 1, required.ToArray()); required.RemoveAt(required.Count - 1);
                    if (count == 0) return cell;
                }
                return -1;
            }
            void AddTechnique(string value) { if (!report.techniques.Contains(value)) report.techniques.Add(value); }
        }
    }

    public static class PuzzleValidator
    {
        public static ValidationReport Validate(PuzzleLevelData level)
        {
            var report = new ValidationReport();
            if (level == null) { report.errors.Add("Level is null."); return report; }
            int n = level.gridSize;
            if (n < 6 || n > 11) report.errors.Add("Grid size must be 6 through 11.");
            if (level.regionIdByCell == null || level.regionIdByCell.Length != n * n) report.errors.Add("Region cell count is incorrect.");
            if (report.errors.Count > 0) return report;
            var ids = level.regionIdByCell.Distinct().OrderBy(x => x).ToArray();
            if (ids.Length != n || !ids.SequenceEqual(Enumerable.Range(0, n))) report.errors.Add("Region IDs must be contiguous and total N.");
            for (int g = 0; g < n; g++) if (!PuzzleRules.IsRegionConnected(level, g)) report.errors.Add($"Region {g} is disconnected.");
            if (!PuzzleRules.IsCanonicalSolutionValid(level)) report.errors.Add("Canonical solution violates a core rule.");
            var starters = level.starterCatCells ?? Array.Empty<int>();
            if (starters.Distinct().Count() != starters.Length || starters.Any(cell => cell < 0 || cell >= n * n || !level.IsSolutionCell(cell)))
                report.errors.Add("Starter cats must be unique canonical solution cells.");
            report.solutionCount = PuzzleSolver.CountSolutions(level, 2);
            if (report.solutionCount != 1) report.errors.Add($"Expected one solution, found {report.solutionCount}{(report.solutionCount == 2 ? "+" : "")}.");
            if (report.errors.Count == 0) DifficultyAnalyser.Analyse(level, report);
            report.valid = report.errors.Count == 0;
            return report;
        }
    }

    public static class PuzzleGenerator
    {
        private static readonly string[] Themes = { "Moonlit Courtyard", "Pumpkin Village", "Witch's Library", "Vampire Hall", "Ghost Garden", "Mummy Ruins", "Medusa Grotto", "Werewolf Woods", "Stitched Laboratory", "Midnight Castle" };
        private static readonly string[] Characters = { "Medusa", "Vampire", "Witch", "Ghost", "Mummy", "Werewolf", "Pumpkin", "Stitched" };

        public static List<PuzzleLevelData> GenerateCampaign(int masterSeed = 0x4D4C3235)
        {
            var levels = new List<PuzzleLevelData>(250); var symmetryKeys = new HashSet<string>(); var solutionKeys = new HashSet<string>();
            for (int number = 1; number <= 250; number++)
            {
                int chapter = (number - 1) / 25 + 1;
                int n = BoardSizeFor(number);
                PuzzleLevelData level = null;
                for (int variant = 0; variant < 80; variant++)
                {
                    int seed = unchecked(masterSeed + number * 7919 + variant * 104729);
                    level = Generate(number, chapter, n, seed);
                    string key = CanonicalSymmetryKey(level);
                    if (PuzzleValidator.Validate(level).valid && symmetryKeys.Add(key) && solutionKeys.Add(string.Join(",", level.solutionColumnByRow))) break;
                    level = null;
                }
                if (level == null) throw new InvalidOperationException($"Could not generate unique level {number}.");
                levels.Add(level);
            }
            return levels;
        }

        public static PuzzleLevelData Generate(int number, int chapter, int n, int seed)
        {
            var rng = new System.Random(seed);
            for (int attempt = 0; attempt < 240; attempt++)
            {
                int[] solution = GenerateSolution(n, rng);
                int[] regions = GrowUniqueRegions(n, solution, rng);
                var level = new PuzzleLevelData
                {
                    levelId = $"campaign-{number:000}", chapterId = chapter, displayNumber = number, gridSize = n,
                    regionIdByCell = regions, solutionColumnByRow = solution,
                    characterTheme = Characters[(chapter - 1) % Characters.Length], backgroundTheme = Themes[chapter - 1],
                    difficultyTier = DifficultyFor(number), difficultyBand = BandFor(number), archetype = ArchetypeFor(number),
                    starterCatCells = StarterCatsFor(number, solution), generationSeed = seed, contentVersion = 2,
                    parTimeSeconds = n <= 6 ? 90 : n == 7 ? 150 : n == 8 ? 210 : n == 9 ? 300 : 420
                };
                var report = PuzzleValidator.Validate(level);
                if (!report.valid) continue;
                int recovery = number % 7 == 0 ? -12 : 0;
                level.difficultyScore = Math.Max(1, number * 4 + report.difficultyScore + recovery);
                level.expectedTechniques = report.techniques.Count > 0 ? report.techniques.ToArray() : new[] { "Direct elimination" };
                level.logicalSteps = report.deductionSteps; level.maxChainDepth = report.maxChainDepth; level.initialForcedMoves = report.initialForcedMoves;
                level.contentHash = PuzzleRules.ContentHash(level);
                return level;
            }
            throw new InvalidOperationException($"Region generator exhausted level {number} after 240 original candidates.");
        }

        private static int BoardSizeFor(int level) => level <= 45 ? 6 : level <= 100 ? 7 : level <= 160 ? 8 : level <= 215 ? 9 : level <= 235 ? 10 : 11;
        private static string BandFor(int level) => level <= 10 ? "Tutorial" : level <= 20 ? "Very Easy" : level <= 45 ? "Easy" : level <= 70 ? "Easy+" : level <= 100 ? "Medium" : level <= 130 ? "Medium" : level <= 160 ? "Medium-Hard" : level <= 190 ? "Hard" : level <= 215 ? "Hard+" : level <= 235 ? "Expert" : "Expert+";
        private static DifficultyTier DifficultyFor(int level) => level <= 20 ? DifficultyTier.Tutorial : level <= 70 ? DifficultyTier.Easy : level <= 130 ? DifficultyTier.Medium : level <= 215 ? DifficultyTier.Hard : DifficultyTier.Expert;
        private static string ArchetypeFor(int level)
        {
            string[] archetypes = { "SmallRegionOpening", "EdgePressure", "CenterLock", "DiagonalChain", "RowCascade", "ColumnCascade", "Staircase", "LongCorridor", "LargeAmbiguousRegion", "TwoSidedSolve", "SpiralReasoning", "CenterOut", "LateUnlock" };
            return archetypes[(level * 7 + level / 9) % archetypes.Length];
        }
        private static int[] StarterCatsFor(int level, int[] solution)
        {
            bool useStarter = level <= 10 || (level <= 20 && level % 4 == 0) || (level >= 46 && level <= 55 && level % 3 == 1) || (level >= 101 && level <= 108 && level % 2 == 1) || (level >= 161 && level <= 167 && level % 3 == 2);
            int row = level % solution.Length;
            return useStarter ? new[] { row * solution.Length + solution[row] } : Array.Empty<int>();
        }

        private static int[] GenerateSolution(int n, System.Random rng)
        {
            var result = new int[n]; var used = new bool[n];
            bool Search(int row)
            {
                if (row == n) return true;
                var candidates = Enumerable.Range(0, n).Where(column => !used[column] && (row == 0 || Math.Abs(result[row - 1] - column) > 1)).OrderBy(_ => rng.Next()).ToArray();
                foreach (int column in candidates)
                {
                    result[row] = column; used[column] = true;
                    if (Search(row + 1)) return true;
                    used[column] = false;
                }
                return false;
            }
            if (!Search(0)) throw new InvalidOperationException("Could not construct a non-touching solution.");
            return result;
        }

        private static int[] GrowUniqueRegions(int n, int[] solution, System.Random rng)
        {
            for (int restart = 0; restart < (n <= 8 ? 480 : 0); restart++)
            {
                var regions = new int[n * n];
                for (int cell = 0; cell < regions.Length; cell++) regions[cell] = cell / n;
                for (int iteration = 0; iteration < n * n * 20; iteration++)
                {
                    int cell = rng.Next(regions.Length), source = regions[cell], row = cell / n, column = cell % n;
                    if (cell == source * n + solution[source]) continue;
                    var owners = new List<int>(4);
                    Add(row - 1, column); Add(row + 1, column); Add(row, column - 1); Add(row, column + 1);
                    if (owners.Count == 0) continue;
                    int target = owners[rng.Next(owners.Count)]; regions[cell] = target;
                    if (!RegionIsConnected(regions, n, source)) regions[cell] = source;
                    void Add(int r, int c)
                    {
                        if (r < 0 || r >= n || c < 0 || c >= n) return;
                        int owner = regions[r * n + c];
                        if (owner != source && !owners.Contains(owner)) owners.Add(owner);
                    }
                }
                var candidate = new PuzzleLevelData { gridSize = n, regionIdByCell = regions, solutionColumnByRow = solution, starterCatCells = Array.Empty<int>(), contentVersion = 2 };
                if (PuzzleSolver.CountSolutions(candidate, 2) == 1) return regions;
            }
            for (int attempt = 0; attempt < 2000; attempt++)
            {
                var compact = BuildConstrainedRegionMap(n, solution, rng);
                if (compact == null) continue;
                var candidate = new PuzzleLevelData { gridSize = n, regionIdByCell = compact, solutionColumnByRow = solution, starterCatCells = Array.Empty<int>(), contentVersion = 2 };
                if (PuzzleSolver.CountSolutions(candidate, 2) == 1) return compact;
            }
            throw new InvalidOperationException("Could not create a unique connected region map.");
        }

        private static int[] BuildConstrainedRegionMap(int n, int[] solution, System.Random rng)
        {
            var regions = Enumerable.Repeat(n - 1, n * n).ToArray();
            var solutionCells = new HashSet<int>(Enumerable.Range(0, n).Select(row => row * n + solution[row]));
            for (int region = 0; region < n - 1; region++)
            {
                int seed = region * n + solution[region]; regions[seed] = region;
                int row = seed / n, column = seed % n;
                var choices = new List<int>(); Add(row - 1, column); Add(row + 1, column); Add(row, column - 1); Add(row, column + 1);
                if (choices.Count == 0) return null;
                regions[choices[rng.Next(choices.Count)]] = region;
                void Add(int r, int c)
                {
                    if (r < 0 || r >= n || c < 0 || c >= n) return;
                    int cell = r * n + c;
                    if (!solutionCells.Contains(cell) && regions[cell] == n - 1) choices.Add(cell);
                }
            }
            for (int region = 0; region < n; region++) if (!RegionIsConnected(regions, n, region)) return null;
            return regions;
        }

        private static bool RegionIsConnected(int[] regions, int n, int region)
        {
            int first = Array.IndexOf(regions, region); if (first < 0) return false;
            var seen = new bool[regions.Length]; var queue = new Queue<int>(); queue.Enqueue(first); seen[first] = true; int count = 0;
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(); count++; int row = cell / n, column = cell % n;
                Add(row - 1, column); Add(row + 1, column); Add(row, column - 1); Add(row, column + 1);
            }
            return count == regions.Count(cell => cell == region);
            void Add(int row, int column)
            {
                if (row < 0 || row >= n || column < 0 || column >= n) return;
                int cell = row * n + column;
                if (!seen[cell] && regions[cell] == region) { seen[cell] = true; queue.Enqueue(cell); }
            }
        }

        public static string CanonicalSymmetryKey(PuzzleLevelData level)
        {
            int n = level.gridSize; var keys = new List<string>(8);
            for (int variant = 0; variant < 8; variant++)
            {
                var normalized = new Dictionary<int, int>(); int next = 0; var sb = new StringBuilder(n * n * 2);
                for (int r = 0; r < n; r++) for (int c = 0; c < n; c++)
                {
                    Transform(r, c, variant, n, out int rr, out int cc); int id = level.regionIdByCell[rr * n + cc];
                    if (!normalized.TryGetValue(id, out int value)) normalized[id] = value = next++;
                    sb.Append((char)('A' + value));
                }
                keys.Add(sb.ToString());
            }
            keys.Sort(StringComparer.Ordinal); return keys[0];
        }

        private static void Transform(int r, int c, int variant, int n, out int rr, out int cc)
        {
            bool mirror = variant >= 4; int rotation = variant % 4; int x = c, y = r;
            if (mirror) x = n - 1 - x;
            for (int i = 0; i < rotation; i++) { int oldX = x; x = n - 1 - y; y = oldX; }
            rr = y; cc = x;
        }
    }
}
