using System;
using System.Collections.Generic;
using System.Linq;

namespace MonsterLogic.Puzzle
{
    public enum CellMark { Empty, PlayerX, AutomaticX, Monster, LockedMonster }

    public sealed class PuzzleSession
    {
        private sealed class Snapshot
        {
            public bool[] monsters;
            public bool[] playerNotes;
            public int hearts;
            public int mistakes;
        }

        public PuzzleLevelData Level { get; private set; }
        public bool[] Monsters { get; private set; }
        public bool[] PlayerNotes { get; private set; }
        public int[] AutomaticNoteSources { get; private set; }
        public int Hearts { get; private set; }
        public int Mistakes { get; private set; }
        public bool IsComplete { get; private set; }
        public float ElapsedSeconds { get; set; }
        public event Action Changed;
        public event Action<int> MistakeMade;
        public event Action Completed;

        private readonly Stack<Snapshot> _history = new Stack<Snapshot>();
        private int _hintStage;

        public PuzzleSession(PuzzleLevelData level) => Start(level);

        public void Start(PuzzleLevelData level)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));
            int count = level.gridSize * level.gridSize;
            Monsters = new bool[count]; PlayerNotes = new bool[count]; AutomaticNoteSources = new int[count];
            foreach (int cell in level.lockedMonsterCells ?? Array.Empty<int>()) Monsters[cell] = true;
            Hearts = 3; Mistakes = 0; IsComplete = false; ElapsedSeconds = 0; _hintStage = 0; _history.Clear();
            RebuildAutomaticNotes(); Changed?.Invoke();
        }

        public bool Restore(IEnumerable<int> monsterCells, IEnumerable<int> playerNoteCells, int hearts, int mistakes, float elapsedSeconds)
        {
            if (Level == null) return false;
            int count = Level.gridSize * Level.gridSize;
            var restoredMonsters = new bool[count];
            foreach (int cell in Level.lockedMonsterCells ?? Array.Empty<int>()) restoredMonsters[cell] = true;
            foreach (int cell in monsterCells ?? Enumerable.Empty<int>())
                if (cell >= 0 && cell < count && Level.IsSolutionCell(cell)) restoredMonsters[cell] = true;

            var restoredNotes = new bool[count];
            foreach (int cell in playerNoteCells ?? Enumerable.Empty<int>())
                if (cell >= 0 && cell < count && !restoredMonsters[cell]) restoredNotes[cell] = true;

            Monsters = restoredMonsters; PlayerNotes = restoredNotes;
            Hearts = Math.Max(0, Math.Min(3, hearts)); Mistakes = Math.Max(0, mistakes); ElapsedSeconds = Math.Max(0, elapsedSeconds);
            IsComplete = PuzzleRules.PlacementsSatisfyAll(Level, Monsters); _history.Clear(); _hintStage = 0;
            RebuildAutomaticNotes(); Changed?.Invoke(); return true;
        }

        public CellMark GetMark(int cell)
        {
            if (Monsters[cell]) return Level.IsLocked(cell) ? CellMark.LockedMonster : CellMark.Monster;
            if (PlayerNotes[cell]) return CellMark.PlayerX;
            return AutomaticNoteSources[cell] > 0 ? CellMark.AutomaticX : CellMark.Empty;
        }

        public void ToggleNote(int cell)
        {
            if (!CanEdit(cell) || Monsters[cell]) return;
            PushSnapshot(); PlayerNotes[cell] = !PlayerNotes[cell]; _hintStage = 0; Changed?.Invoke();
        }

        public void ToggleMonster(int cell)
        {
            if (!CanEdit(cell)) return;
            if (Monsters[cell])
            {
                PushSnapshot(); Monsters[cell] = false; RebuildAutomaticNotes(); IsComplete = false; _hintStage = 0; Changed?.Invoke(); return;
            }
            if (!Level.IsSolutionCell(cell))
            {
                Hearts = Math.Max(0, Hearts - 1); Mistakes++; _hintStage = 0; MistakeMade?.Invoke(cell); Changed?.Invoke(); return;
            }
            PushSnapshot(); Monsters[cell] = true; PlayerNotes[cell] = false; RebuildAutomaticNotes(); _hintStage = 0;
            IsComplete = PuzzleRules.PlacementsSatisfyAll(Level, Monsters);
            Changed?.Invoke(); if (IsComplete) Completed?.Invoke();
        }

        public void Cycle(int cell)
        {
            switch (GetMark(cell))
            {
                case CellMark.Empty: case CellMark.AutomaticX: ToggleNote(cell); break;
                case CellMark.PlayerX: ToggleMonster(cell); break;
                case CellMark.Monster: ToggleMonster(cell); break;
            }
        }

        public bool Undo()
        {
            if (_history.Count == 0) return false;
            var s = _history.Pop(); Monsters = s.monsters; PlayerNotes = s.playerNotes; Hearts = s.hearts; Mistakes = s.mistakes;
            IsComplete = false; RebuildAutomaticNotes(); Changed?.Invoke(); return true;
        }

        public void Restart() => Start(Level);

        public string GetHint(out int focusCell, out bool revealed)
        {
            focusCell = -1; revealed = false;
            int n = Level.gridSize;
            for (int row = 0; row < n; row++)
            {
                if (Monsters.Skip(row * n).Take(n).Any(x => x)) continue;
                var candidates = Enumerable.Range(0, n).Select(c => row * n + c).Where(CanBeMonster).ToArray();
                if (candidates.Length == 1)
                {
                    focusCell = candidates[0];
                    if (_hintStage++ == 0) return $"Row {row + 1} has only one cell left. Check its column, region, and neighbours.";
                    revealed = true; ToggleMonster(focusCell); return $"The forced monster is in row {row + 1}, column {focusCell % n + 1}.";
                }
            }
            focusCell = Enumerable.Range(0, n * n).FirstOrDefault(i => !Monsters[i] && Level.IsSolutionCell(i));
            if (_hintStage++ == 0) return $"Inspect the violet region around row {focusCell / n + 1}. One option creates a contradiction.";
            revealed = true; ToggleMonster(focusCell); return $"Place a monster at row {focusCell / n + 1}, column {focusCell % n + 1}.";
        }

        /// <summary>
        /// Returns real exclusions for the current board.  The UI uses these for the
        /// first hint so an explanatory hint always has a visible puzzle consequence.
        /// </summary>
        public int[] GetHelpfulHintCrosses(int focusCell, int maximum = 3)
        {
            if (Level == null || maximum <= 0) return Array.Empty<int>();
            int n = Level.gridSize;
            var crosses = new List<int>();
            void Collect(IEnumerable<int> cells)
            {
                foreach (int cell in cells)
                {
                    if (crosses.Count >= maximum) return;
                    if (cell == focusCell || Monsters[cell] || PlayerNotes[cell] || crosses.Contains(cell)) continue;
                    if (!CanBeMonster(cell)) crosses.Add(cell);
                }
            }

            if (focusCell >= 0 && focusCell < Monsters.Length)
            {
                int row = focusCell / n;
                Collect(Enumerable.Range(row * n, n));
            }
            Collect(Enumerable.Range(0, Monsters.Length));

            // Authored levels have one answer per cell set; this final pass remains
            // correct even before the solver has derived an automatic exclusion.
            if (crosses.Count == 0)
                Collect(Enumerable.Range(0, Monsters.Length).Where(cell => !Level.IsSolutionCell(cell)));
            return crosses.ToArray();
        }

        private bool CanBeMonster(int cell)
        {
            if (PlayerNotes[cell] || AutomaticNoteSources[cell] > 0 || Monsters[cell]) return false;
            var required = Monsters.Select((v, i) => (v, i)).Where(x => x.v).Select(x => x.i).Append(cell).ToArray();
            return PuzzleSolver.CountSolutions(Level, 1, required) > 0;
        }

        private bool CanEdit(int cell) => cell >= 0 && cell < Monsters.Length && !IsComplete && Hearts > 0 && !Level.IsLocked(cell);

        private void PushSnapshot() => _history.Push(new Snapshot { monsters = (bool[])Monsters.Clone(), playerNotes = (bool[])PlayerNotes.Clone(), hearts = Hearts, mistakes = Mistakes });

private void RebuildAutomaticNotes()
        {
            // Board input must affect only its selected tile; derived cross marks are disabled.
            Array.Clear(AutomaticNoteSources, 0, AutomaticNoteSources.Length);
        }
    }
}
