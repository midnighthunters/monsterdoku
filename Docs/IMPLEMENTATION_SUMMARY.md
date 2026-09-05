# Monster Logic implementation summary

## Project

- Unity: 6000.0.42f1
- Render pipeline: URP 17.0.4, 2D template
- Main/build scene: `Assets/Game/Scenes/MonsterLogic.unity`
- Product ID: `com.zemolabs.monsterlogic` for Android and iOS
- Orientation: portrait; reference canvas 900×1600 with safe-area anchoring

## Campaign and logic

- `Assets/Game/Data/Resources/PuzzleLevelDatabase.asset` contains exactly 250 stable-ID levels in ten chapters.
- `PuzzleGenerator` deterministically selects a no-touching solution permutation, begins with connected row bands, performs connectivity-preserving boundary mutations, and accepts only a one-solution region map.
- `PuzzleSolver` is board-size agnostic, deterministic, respects locked cells, and exits after two solutions for uniqueness checks.
- `PuzzleValidator` checks dimensions, contiguous region IDs, orthogonal connectivity, canonical row/column/region/adjacency rules, locked-cell correctness, exact uniqueness, and human-style completion.
- `DifficultyAnalyser` records single-candidate and controlled-contradiction traces and saves a score/technique profile on each entry.
- Symmetry keys normalize region labels under all rotations/reflections; generation produced 250 distinct canonical keys.

## Gameplay

- Delayed single tap toggles a player X without racing a double tap.
- Double tap and long press place/remove monsters; optional accessibility mode cycles empty → X → monster.
- Automatic notes use source counts and stay separate from player notes.
- Locked starters, atomic undo, confirmed restart, three hearts, invalid-placement feedback, two-stage hints, win, retry, next-level, and campaign-complete flows are implemented.
- Progress, best time, best mistakes, settings, current level, tutorial state, partial board, player notes, hearts, mistakes, and timer use stable IDs and an atomic JSON save with backup recovery.

## UI, themes, and accessibility

- Loading, home, chapters, 25-level chapter grid, game, tutorial copy, settings, win, out-of-hearts, and campaign-complete panels are runtime-built with uGUI and TextMeshPro.
- Light and dark palettes are separately authored; colour-friendly regions, optional region symbols, reduced motion, single-tap cycling, equivalent automatic X marks, music/SFX/haptic toggles, and safe areas are exposed.
- The 8×8 board remains fully visible without scrolling at small phone, 9:16, tall-phone, and 4:3 tablet render targets.
- Buttons meet approximately 44-point targets; cells use colour plus explicit region boundaries and lock labels.

## Editor workflow

Open **Monster Logic → Level Workshop** to preview a level, show region IDs/solution/locked cells, validate one or all levels, regenerate by seed, toggle a starter, recalculate difficulty, detect symmetry duplicates, import/export JSON, or enter Play Mode on the selected level.

Menu commands:

- **Monster Logic → Generate Campaign Database**
- **Monster Logic → Validate All 250 Levels**
- **Monster Logic → Configure Product Settings**
- **Monster Logic → Import TMP Essentials**

## Verification performed

- Unity compilation and console checked after every major milestone.
- All-level validation result: 250 valid, connected, uniquely solvable, symmetry-distinct maps.
- Editor validation result: 250 valid, connected, uniquely solvable, symmetry-distinct maps; representative 5×5, 6×6, 7×7, and 8×8 levels were exercised in Play Mode.
- Runtime launch, home → game navigation, pointer-event double tap, heart loss, 5×5 level 1, and 8×8 level 250 exercised through Unity MCP.
- Light home and light 5×5 game screenshots inspected.
- Dark 8×8 layouts inspected at 750×1334, 900×1600, 900×1950, and 768×1024 render targets.
- No ad/analytics/network dependency is installed; campaign runs offline.
- iOS is configured for IL2CPP/ARM64, version `1.0.0`, package ID `com.zemolabs.monsterlogic`, and portrait orientation. The final signed iOS archive still requires Xcode on macOS (or a macOS CI runner); this Windows editor session verifies the Unity project and generated iOS settings.

## Known, specific limitations

- Cross-device/cloud save sync is not implemented; persistence is local with backup recovery.
- Haptic patterns use Unity's portable vibration fallback because no platform-native iOS/Android haptic plugin is installed.
- Character emotion variants are animation states of the neutral atlas rather than 40 separate raster portraits.
- The 5×5 permutation repetition exception is mathematically unavoidable and documented in `REFERENCE_RESEARCH.md`; all 250 region maps remain unique under symmetry.
