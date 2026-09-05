# Monster Logic reference research

Research date: 2026-08-16. The attached screenshot and public pages were used only to verify behaviour and broad mobile composition. No store artwork, level map, audio, APK/IPA, code, or branding was copied into the project.

| Observed behaviour | Source | Confidence | Our implementation |
|---|---|---:|---|
| Exactly one character belongs in every coloured region, row, and column; characters cannot touch, including diagonally. | [Google Play listing](https://play.google.com/store/apps/details?id=com.oakever.meowdoku), [App Store listing](https://apps.apple.com/us/app/meowdoku/id6761760135) | High | The four constraints are explicit in `PuzzleRules`, solver search, generator rejection, and completion validation. |
| A double tap places a character and an incorrect placement consumes one of three hearts. | [Google Play listing](https://play.google.com/store/apps/details?id=com.oakever.meowdoku) | High | Delayed single tap toggles an X; double tap or long press confirms a monster. Invalid confirmation costs one heart and restores the prior cell state. |
| Single tap marks an excluded cell with X; undo and restart are useful board tools. | [Public gameplay guide](https://meowdoku.app/wiki/how-to-play-meowdoku) | Medium | Single tap toggles a player-owned X. Undo restores the complete atomic state; restart asks for confirmation. |
| Placed characters eliminate their row, column, region, and neighbouring cells. | [Official store rules](https://play.google.com/store/apps/details?id=com.oakever.meowdoku), [public rules article](https://dlegames.org/blog/meowdoku-rules) | High | Automatic X source counts are recalculated from all placed/locked monsters and never delete a deliberate player X. |
| Contradiction can be surfaced as a hint on advanced boards. | [Public player discussion](https://www.reddit.com/r/puzzles/comments/1vcpwt7/what_does_this_meowdoku_hint_even_mean/) | Medium | First-stage hints point to a useful line/region and explain the deduction; a second use may reveal the forced cell. Controlled contradiction is named in analysis metadata. |
| Puzzles are intended for offline, short-session play. | [Google Play listing](https://play.google.com/store/apps/details?id=com.oakever.meowdoku) | High | All 250 campaign levels, saves, audio, art, hints, and validation are local; no network is required. |
| Current players value the puzzle loop but report disruptive ads and loss/removal of undo/draft affordances. | [Google Play reviews](https://play.google.com/store/apps/details?id=com.oakever.meowdoku), [App Store reviews](https://apps.apple.com/us/app/meowdoku/id6761760135) | Medium | No ad SDK is installed; `IAdService` defaults to no-op. Undo/restart and player X notes remain first-class controls. |
| The reference composition uses a safe-area top bar, progress/hearts, compact rules, a dominant square board, and bottom tools. | User-supplied screenshot | High | The responsive uGUI screen follows this information hierarchy with original typography, palettes, shapes, characters, and spacing. |

## Decisions and inferences

- Public sources disagree about some transient production features (for example undo availability and ad cadence). The attached specification takes priority, so undo is always available and ads are optional abstractions only.
- Long press was not consistently verified. It is implemented as an optional placement shortcut because it improves accessibility and does not change puzzle logic.
- Public fan sites show varying board sizes and may describe randomized level numbering. Their boards were not recorded or reproduced. Monster Logic follows the specified fixed 5×5–8×8 ten-chapter campaign.
- A strict mathematical limit exists: only 14 permutations of a 5×5 board satisfy unique columns and the neighbouring-row diagonal-separation rule. Therefore 25 non-repeating solution permutations within each 5×5 chapter is impossible without weakening a core rule. Monster Logic keeps the rule and provides 25 symmetry-distinct connected region maps per chapter; solution permutations may repeat only where this finite limit requires it.
