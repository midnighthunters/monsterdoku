using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MonsterLogic.Progression;
using MonsterLogic.Puzzle;
using MonsterLogic.Services;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MonsterLogic.UI
{
    public sealed class MonsterLogicApp : MonoBehaviour
    {
        private PuzzleLevelDatabase _database; private SaveService _save; private IAdService _ads;
        private AudioService _audio; private HapticService _haptics; private ThemePalette _theme; private TMP_FontAsset _font, _displayFont;
        private Canvas _canvas; private RectTransform _safeRoot; private GameObject _screen; private PuzzleSession _session;
        private readonly List<CellView> _cells = new List<CellView>(); private TMP_Text _progress, _hearts, _timer, _hintText, _placeActionLabel;
        private Image _placeActionArt, _placeActionRing;
        private Texture2D _appIcon; private Sprite _activeVillainSprite; private bool _placeMode, _initialized, _hideInitialClueVisuals;

        public void Initialize(PuzzleLevelDatabase database, SaveService save, IAdService ads, TMP_FontAsset displayFont = null, TMP_FontAsset bodyFont = null)
        {
            if (_initialized) return; _initialized = true; _database = database; _save = save; _ads = ads;
            _theme = ThemeService.Get(_save.Data.settings.darkTheme, _save.Data.settings.colourFriendly);
            _audio = new AudioService(_save.Data.settings, gameObject); _haptics = new HapticService(_save.Data.settings);
            _audio.SetAmbience(_save.Data.settings.darkTheme);
            _font = bodyFont != null ? bodyFont : TMP_Settings.defaultFontAsset; _displayFont = displayFont != null ? displayFont : _font;
            _appIcon = Resources.Load<Texture2D>("AppIcon");
            BuildFoundation(); StartCoroutine(BootSequence());
        }

        private IEnumerator BootSequence()
        {
            ShowLoading(); yield return new WaitForSecondsRealtime(.35f);
            int requested = PlayerPrefs.GetInt("MonsterLogic.PlayLevel", 0); PlayerPrefs.DeleteKey("MonsterLogic.PlayLevel");
            if (requested > 0) StartLevel(requested); else ShowHome();
        }

        private void Update()
        {
            if (_session != null && !_session.IsComplete && _session.Hearts > 0)
            { _session.ElapsedSeconds += Time.unscaledDeltaTime; if (_timer != null) _timer.text = FormatTime(_session.ElapsedSeconds); }
        }

        private void OnApplicationPause(bool paused) { if (paused) PersistSession(); }
        private void OnApplicationQuit() => PersistSession();

        private void BuildFoundation()
        {
            var canvasGo = new GameObject("MonsterLogicCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false); _canvas = canvasGo.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceCamera; _canvas.worldCamera = Camera.main; _canvas.planeDistance = 1f;
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(900, 1600); scaler.matchWidthOrHeight = .5f;
            var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter)); safe.transform.SetParent(canvasGo.transform, false); _safeRoot = safe.GetComponent<RectTransform>(); Stretch(_safeRoot);
            if (FindFirstObjectByType<EventSystem>() == null)
            { var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); es.transform.SetParent(transform, false); }
        }

        private void ShowLoading()
        {
            BeginScreen("Loading"); AddBackdrop(_screen.transform, 0);
            var emblem = Raw(_screen.transform, "Emblem", _appIcon); Anchor(emblem.rectTransform, .5f, .62f, 270, 270);
            var title = Text(_screen.transform, "Title", "MONSTER LOGIC", 62, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(title.rectTransform, .5f, .44f, 760, 100);
            var sub = Text(_screen.transform, "Subtitle", "A cosy puzzle after moonrise", 28, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(sub.rectTransform, .5f, .385f, 700, 60);
        }

        public void ShowHome()
        {
            PersistSession(); _session = null; BeginScreen("Home"); AddBackdrop(_screen.transform, Mathf.Max(0, (_save.Data.highestUnlocked - 1) / 25));
            var settings = IconButton(_screen.transform, "Settings", RuntimeIcon.Gear, ShowSettings); Anchor(settings, .90f, .94f, 92, 92);
            var theme = Button(_screen.transform, "Theme", _save.Data.settings.darkTheme ? "DAY" : "NITE", ToggleTheme, true); Anchor(theme, .78f, .94f, 92, 92);
            var emblem = Raw(_screen.transform, "Emblem", _appIcon); Anchor(emblem.rectTransform, .5f, .79f, 230, 230);
            var title = Text(_screen.transform, "Title", "MONSTER\nLOGIC", 78, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(title.rectTransform, .5f, .65f, 760, 210);
            var tag = Text(_screen.transform, "Tag", "ONE MONSTER. EVERY LINE. NO TOUCHING.", 23, _theme.muted, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(tag.rectTransform, .5f, .54f, 780, 65);
            bool canContinue = _save.Data.highestUnlocked > 1 && _save.Data.currentLevelId == $"campaign-{_save.Data.highestUnlocked:000}";
            var play = Button(_screen.transform, "Play", canContinue ? $"CONTINUE  ·  LEVEL {_save.Data.highestUnlocked}" : "PLAY", () => StartLevel(_save.Data.highestUnlocked)); Anchor(play, .5f, .40f, 650, 118);
            var levels = Button(_screen.transform, "Levels", "CHAPTERS  ·  250 PUZZLES", ShowLevelSelect, false); Anchor(levels, .5f, .30f, 650, 96);
            var progress = Text(_screen.transform, "Progress", $"{_save.Data.completed.Count} / 250 complete", 27, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(progress.rectTransform, .5f, .21f, 600, 60);
            var footer = Text(_screen.transform, "Footer", "Offline campaign  •  No forced ads  •  Progress backed up", 20, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(footer.rectTransform, .5f, .07f, 820, 54);
        }

        private void ShowLevelSelect()
        {
            BeginScreen("LevelSelect"); AddBackdrop(_screen.transform, Mathf.Max(0, (_save.Data.highestUnlocked - 1) / 25));
            var back = Button(_screen.transform, "Back", "<", ShowHome, true); Anchor(back, .09f, .94f, 92, 92);
            var title = Text(_screen.transform, "Title", "CHAPTERS", 54, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(title.rectTransform, .5f, .94f, 520, 90);
            var grid = Panel(_screen.transform, "ChapterGrid", new Color(0,0,0,0)); Anchor(grid, .5f, .51f, 800, 1240);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>(); layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 2; layout.cellSize = new Vector2(380, 220); layout.spacing = new Vector2(24, 24); layout.padding = new RectOffset(8, 8, 8, 8);
            string[] names = { "Moonlit Courtyard", "Pumpkin Village", "Witch's Library", "Vampire Hall", "Ghost Garden", "Mummy Ruins", "Medusa Grotto", "Werewolf Woods", "Stitched Lab", "Midnight Castle" };
            for (int chapter = 1; chapter <= 10; chapter++)
            {
                int ch = chapter, first = (chapter - 1) * 25 + 1; bool unlocked = _save.Data.highestUnlocked >= first; int done = _save.Data.completed.Count(x => ParseNumber(x.levelId) >= first && ParseNumber(x.levelId) < first + 25);
                var card = Button(grid, $"Chapter{chapter}", unlocked ? $"{chapter:00}  {names[chapter - 1]}\n{done}/25  ·  {(_database.GetByNumber(first)?.gridSize ?? 5)}×{(_database.GetByNumber(first)?.gridSize ?? 5)}" : $"{chapter:00}  LOCKED\nComplete chapter {chapter - 1}", () => { if (unlocked) ShowChapter(ch); }, false);
                card.GetComponent<Image>().color = unlocked ? _theme.panel : Color.Lerp(_theme.panelAlt, _theme.background, .45f);
            }
        }

        private void ShowChapter(int chapter)
        {
            BeginScreen("Chapter"); AddBackdrop(_screen.transform, chapter - 1);
            var back = Button(_screen.transform, "Back", "<", ShowLevelSelect, true); Anchor(back, .09f, .94f, 92, 92);
            var title = Text(_screen.transform, "Title", $"CHAPTER {chapter}", 52, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(title.rectTransform, .5f, .94f, 540, 90);
            var grid = Panel(_screen.transform, "LevelGrid", new Color(0,0,0,0)); Anchor(grid, .5f, .52f, 800, 1220);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>(); layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 5; layout.cellSize = new Vector2(140, 140); layout.spacing = new Vector2(18, 18); layout.padding = new RectOffset(10, 10, 20, 20);
            int first = (chapter - 1) * 25 + 1;
            for (int number = first; number < first + 25; number++)
            {
                int n = number; bool unlocked = n <= _save.Data.highestUnlocked, done = _save.Data.completed.Any(x => x.levelId == $"campaign-{n:000}");
                var b = Button(grid, $"Level{n}", unlocked ? (done ? $"OK\n{n}" : n.ToString()) : "-", () => { if (unlocked) StartLevel(n); }, true);
                b.GetComponent<Image>().color = done ? Color.Lerp(_theme.success, _theme.panel, .65f) : unlocked ? _theme.panel : _theme.panelAlt;
            }
        }

        public void StartLevel(int number)
        {
            var level = _database.GetByNumber(Mathf.Clamp(number, 1, 250)); if (level == null) return;
            _placeMode = false;
            _activeVillainSprite = VillainSprite(VillainGauntlet.Resolve(level.displayNumber));
            bool restoringSession = _save.HasSessionFor(level);
            _hideInitialClueVisuals = !restoringSession;
            _session = new PuzzleSession(level);
            if (restoringSession) _session.Restore(_save.Data.inProgressMonsters, _save.Data.inProgressPlayerNotes, _save.Data.inProgressHearts, _save.Data.inProgressMistakes, _save.Data.inProgressSeconds);
            else _save.ClearInProgress(false);
            _session.Changed += OnSessionChanged; _session.MistakeMade += OnMistake; _session.Completed += OnCompleted;
            BeginScreen("Game"); AddBackdrop(_screen.transform, level.chapterId - 1);
            var back = Button(_screen.transform, "Back", "<", ShowHome, true); Anchor(back, .08f, .95f, 82, 82);
            var settings = IconButton(_screen.transform, "Settings", RuntimeIcon.Gear, ShowSettings); Anchor(settings, .92f, .95f, 82, 82);
            var title = DisplayText(_screen.transform, "LevelTitle", level.displayNumber % 25 == 0 ? $"MASTER PUZZLE  ·  {level.displayNumber}" : $"LEVEL {level.displayNumber}", 48, _theme.ink, TextAlignmentOptions.Center); Anchor(title.rectTransform, .5f, .95f, 620, 80);
            var stats = Panel(_screen.transform, "Stats", _theme.panel); Anchor(stats, .5f, .875f, 780, 98);
            _progress = Text(stats, "Progress", $"0/{level.gridSize} MONSTERS", 29, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Left); Anchor(_progress.rectTransform, .24f, .5f, 310, 76);
            _timer = Text(stats, "Timer", "0:00", 28, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(_timer.rectTransform, .51f, .5f, 180, 70);
            _hearts = Text(stats, "Hearts", "HEARTS  3", 30, new Color(.91f,.25f,.38f), FontStyles.Bold, TextAlignmentOptions.Right); Anchor(_hearts.rectTransform, .77f, .5f, 300, 76);
            var rules = Panel(_screen.transform, "Rules", new Color(0,0,0,0)); Anchor(rules, .5f, .785f, 800, 76);
            var ruleLayout = rules.gameObject.AddComponent<HorizontalLayoutGroup>(); ruleLayout.spacing = 12; ruleLayout.childForceExpandWidth = true; ruleLayout.childForceExpandHeight = true;
            Rule(rules, "1 per region"); Rule(rules, "1 per row + column"); Rule(rules, "No touching");
            BuildBoard(level);
            BuildGameplayActions(level);
            _hintText = Text(_screen.transform, "HintText", TutorialCopy(level.displayNumber), 22, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(_hintText.rectTransform, .5f, .185f, 790, 64);
            RefreshBoard();
            StartCoroutine(ShowVillainUnlockIfNeeded(level.displayNumber));
        }

        private void BuildBoard(PuzzleLevelData level)
        {
            _cells.Clear(); int n = level.gridSize; float boardSize = n == 8 ? 770 : 740;
            var frame = Panel(_screen.transform, "BoardFrame", Color.Lerp(_theme.panel, _theme.accent, .08f)); Anchor(frame, .5f, .47f, boardSize + 32, boardSize + 32);
            var board = Panel(frame, "Board", new Color(0,0,0,0)); Stretch(board);
            var grid = board.gameObject.AddComponent<GridLayoutGroup>(); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = n; grid.cellSize = new Vector2(boardSize / n - 5, boardSize / n - 5); grid.spacing = new Vector2(5, 5); grid.padding = new RectOffset(16, 16, 16, 16);
            for (int cell = 0; cell < n * n; cell++)
            {
                int index = cell, region = level.regionIdByCell[cell]; var go = new GameObject($"Cell_{cell / n}_{cell % n}", typeof(RectTransform), typeof(Image), typeof(CellView)); go.transform.SetParent(board, false);
                var bg = go.GetComponent<Image>(); bg.color = _theme.regions[region % _theme.regions.Length]; bg.sprite = RuntimeArt.RoundedSprite(24); bg.type = UnityEngine.UI.Image.Type.Sliced;
                var mark = Text(go.transform, "Mark", "", Mathf.RoundToInt(170f / n), _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Stretch(mark.rectTransform); mark.raycastTarget = false;
                var monsterGo = new GameObject("Monster", typeof(RectTransform), typeof(Image)); monsterGo.transform.SetParent(go.transform, false); var monster = monsterGo.GetComponent<Image>(); monster.sprite = _activeVillainSprite; monster.preserveAspect = true; monster.color = _activeVillainSprite == null ? Color.clear : Color.white; monster.raycastTarget = false; Anchor(monster.rectTransform, .5f, .52f, grid.cellSize.x * .94f, grid.cellSize.y * .94f);
                var lockBadge = Text(go.transform, "Lock", "L", Mathf.RoundToInt(65f / n + 13), _theme.accent, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(lockBadge.rectTransform, .84f, .82f, 30, 30); lockBadge.raycastTarget = false;
                var regionSymbol = Text(go.transform, "Region", ((char)('A' + region)).ToString(), Mathf.RoundToInt(50f / n + 9), Color.Lerp(_theme.ink, bg.color, .35f), FontStyles.Bold, TextAlignmentOptions.Center); Anchor(regionSymbol.rectTransform, .17f, .82f, 26, 26); regionSymbol.raycastTarget = false;
                AddRegionEdges(go.transform, level, cell, grid.cellSize);
                var view = go.GetComponent<CellView>(); view.Configure(index, bg, mark, monster, lockBadge, regionSymbol); view.Activated += OnCellActivated; _cells.Add(view);
            }
        }

        // The selected villain is a deliberate, single-tap mode. It gives the board
        // the same directness as a physical puzzle piece while preserving tap-to-X.
private void BuildGameplayActions(PuzzleLevelData level)
        {
            var actions = Panel(_screen.transform, "GameplayActions", new Color(0f, 0f, 0f, 0f)); Anchor(actions, .5f, .085f, 650, 165);
            Color villainAccent = VillainAccent(VillainGauntlet.Resolve(level.displayNumber));
            var reveal = BuildActionOrb(actions, "RevealVillain", villainAccent, "REVEAL\nVILLAIN", RevealRandomVillain);
            Anchor(reveal, .25f, .5f, 154, 154);
            var villainArt = SpriteImage(reveal, "VillainArt", _activeVillainSprite); Anchor(villainArt.rectTransform, .5f, .58f, 92, 92);
            if (_activeVillainSprite == null)
            {
                var fallback = DisplayText(reveal, "VillainFallback", "◆", 56, Color.white, TextAlignmentOptions.Center); Anchor(fallback.rectTransform, .5f, .58f, 92, 92);
            }
            var revealLabel = Text(reveal, "ActionLabel", "REVEAL\nVILLAIN", 17, Color.white, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(revealLabel.rectTransform, .5f, .15f, 136, 42);

            var hint = BuildActionOrb(actions, "Hint", Color.Lerp(_theme.success, _theme.accent, .35f), "HINT", ShowHint);
            Anchor(hint, .75f, .5f, 154, 154);
            var hintGlyph = Icon(hint, "HintGlyph", RuntimeIcon.Hint, Color.white); Anchor(hintGlyph.rectTransform, .5f, .60f, 82, 82);
            var hintLabel = Text(hint, "ActionLabel", "SHOW\nHINT", 17, Color.white, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(hintLabel.rectTransform, .5f, .15f, 136, 42);
        }

        private RectTransform BuildActionOrb(Transform parent, string name, Color color, string label, Action action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.color = color; image.sprite = RuntimeArt.DiscSprite(); image.type = UnityEngine.UI.Image.Type.Simple;
            var button = go.GetComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.96f, .96f, 1f); colors.pressedColor = new Color(.76f, .76f, .84f); button.colors = colors;
            button.onClick.AddListener(() => { _audio?.Play("tap"); action?.Invoke(); });
            var rim = Image(go.transform, "Rim", new Color(1f, 1f, 1f, .22f)); rim.sprite = RuntimeArt.RoundedSprite(30); rim.type = UnityEngine.UI.Image.Type.Sliced; Anchor(rim.rectTransform, .5f, .5f, 142, 142); rim.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        private void TogglePlaceMode() => SetPlaceMode(!_placeMode);

        private void SetPlaceMode(bool enabled)
        {
            _placeMode = enabled;
            if (_placeActionLabel != null) _placeActionLabel.text = enabled ? "TAP A CELL\nTO PLACE" : "PLACE\nVILLAIN";
            if (_placeActionRing != null && _session != null)
            {
                Color accent = VillainAccent(VillainGauntlet.Resolve(_session.Level.displayNumber));
                _placeActionRing.color = enabled ? Color.Lerp(accent, Color.white, .20f) : accent;
            }
            if (_hintText != null && _session != null) _hintText.text = enabled ? "Villain selected — tap an open cell to place it." : TutorialCopy(_session.Level.displayNumber);
        }

        private IEnumerator ShowVillainUnlockIfNeeded(int levelNumber)
        {
            // Wait for the board to render before dimming it, making the reveal feel
            // intentionally layered above the active puzzle rather than a scene swap.
            yield return null;
            if (_session == null || _session.Level.displayNumber != levelNumber) yield break;
            var tier = VillainGauntlet.Resolve(levelNumber);
            if (!tier.IsFirstLevel || _save.HasAcknowledgedVillainTier(tier.AcknowledgementId)) yield break;
            ShowVillainUnlock(tier);
        }

        private void ShowVillainUnlock(VillainTier tier)
        {
            Color accent = VillainAccent(tier);
            var overlay = Panel(_canvas.transform, "VillainUnlockOverlay", new Color(.025f, .02f, .08f, .88f)); Stretch(overlay); overlay.SetAsLastSibling();
            overlay.GetComponent<Image>().raycastTarget = true; // Full-screen input shield; no board input can pass through.
            var halo = Image(overlay, "SummoningHalo", new Color(accent.r, accent.g, accent.b, .12f)); halo.sprite = RuntimeArt.DiscSprite(); Anchor(halo.rectTransform, .5f, .52f, 820, 820);
            var frame = Panel(overlay, "UnlockFrame", Color.Lerp(accent, new Color(.03f, .025f, .10f), .80f)); Anchor(frame, .5f, .50f, 774, 1044);
            var card = Panel(frame, "UnlockCard", new Color(.055f, .052f, .15f, 1f)); Anchor(card, .5f, .5f, 744, 1014);
            var topRule = Image(card, "RuneRule", accent); Anchor(topRule.rectTransform, .5f, .888f, 460, 5);
            var rune = DisplayText(card, "RuneHeader", "ARCANE GAUNTLET", 24, accent, TextAlignmentOptions.Center); Anchor(rune.rectTransform, .5f, .925f, 560, 42);
            var heading = DisplayText(card, "UnlockTitle", "VILLAIN\nUNLOCKED", 54, _theme.ink, TextAlignmentOptions.Center); Anchor(heading.rectTransform, .5f, .785f, 650, 140);

            var auraOuter = Image(card, "VillainAuraOuter", new Color(accent.r, accent.g, accent.b, .16f)); auraOuter.sprite = RuntimeArt.DiscSprite(); Anchor(auraOuter.rectTransform, .5f, .515f, 500, 500);
            var auraInner = Image(card, "VillainAuraInner", new Color(accent.r, accent.g, accent.b, .25f)); auraInner.sprite = RuntimeArt.DiscSprite(); Anchor(auraInner.rectTransform, .5f, .515f, 382, 382);
            AddSummoningSparks(card, accent);
            var villain = SpriteImage(card, "UnlockedVillain", VillainSprite(tier)); Anchor(villain.rectTransform, .5f, .515f, 430, 430);
            if (villain.sprite == null)
            {
                var fallback = DisplayText(card, "MissingVillain", "ANCIENT\nSIGIL", 44, accent, TextAlignmentOptions.Center); Anchor(fallback.rectTransform, .5f, .515f, 330, 150);
            }

            var name = DisplayText(card, "VillainName", tier.villain.displayName, 45, accent, TextAlignmentOptions.Center); Anchor(name.rectTransform, .5f, .275f, 640, 72);
            var band = Text(card, "VillainBand", tier.LevelRangeLabel, 25, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(band.rectTransform, .5f, .215f, 500, 44);
            var copy = Text(card, "HuntCopy", "A new adversary has stepped from the dark.\nStudy the pattern. Break the curse.", 22, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(copy.rectTransform, .5f, .145f, 620, 72);
            var begin = Button(card, "BeginHunt", "BEGIN THE HUNT", () => { _save.AcknowledgeVillainTier(tier.AcknowledgementId); Destroy(overlay.gameObject); });
            begin.GetComponent<Image>().color = accent; Anchor(begin, .5f, .065f, 590, 84);
            _audio.Play("panel");
        }

        private void AddSummoningSparks(Transform parent, Color accent)
        {
            // Deterministic positions make the reward card feel magical without
            // allocating a particle system every time a tier opens.
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f + .18f;
                float radius = i % 2 == 0 ? 245f : 208f;
                var spark = Image(parent, "RuneSpark", new Color(accent.r, accent.g, accent.b, i % 3 == 0 ? .95f : .58f)); spark.sprite = RuntimeArt.DiscSprite();
                Anchor(spark.rectTransform, .5f + Mathf.Cos(angle) * radius / 744f, .515f + Mathf.Sin(angle) * radius / 1014f, i % 3 == 0 ? 13 : 8, i % 3 == 0 ? 13 : 8);
            }
        }

        private Sprite VillainSprite(VillainTier tier)
        {
            return Resources.LoadAll<Sprite>(tier.villain.resourcePath).FirstOrDefault(sprite => sprite != null && sprite.name == tier.villain.spriteName);
        }

        private static Color VillainAccent(VillainTier tier)
        {
            return ColorUtility.TryParseHtmlString("#" + tier.villain.accentHex, out Color accent) ? accent : Color.white;
        }

        private void AddRegionEdges(Transform parent, PuzzleLevelData level, int cell, Vector2 cellSize)
        {
            int n = level.gridSize, r = cell / n, c = cell % n, g = level.regionIdByCell[cell]; Color edge = Color.Lerp(_theme.ink, _theme.accent, .25f); float w = 3f;
            if (r == 0 || level.regionIdByCell[(r - 1) * n + c] != g) Edge("Top", .5f, 1f, cellSize.x, w);
            if (r == n - 1 || level.regionIdByCell[(r + 1) * n + c] != g) Edge("Bottom", .5f, 0f, cellSize.x, w);
            if (c == 0 || level.regionIdByCell[r * n + c - 1] != g) Edge("Left", 0f, .5f, w, cellSize.y);
            if (c == n - 1 || level.regionIdByCell[r * n + c + 1] != g) Edge("Right", 1f, .5f, w, cellSize.y);
            void Edge(string name, float ax, float ay, float sx, float sy) { var img = Image(parent, name, edge); img.raycastTarget = false; Anchor(img.rectTransform, ax, ay, sx, sy); }
        }

private void OnCellActivated(int cell, bool monsterAction)
        {
            if (_session == null) return;

            if (_save.Data.settings.accessibilityCycle && !monsterAction) _session.Cycle(cell);
            else if (monsterAction) _session.ToggleMonster(cell);
            else _session.ToggleNote(cell);

            _audio.Play(monsterAction ? "monster" : "x");
            if (_session.Hearts <= 0) ShowOutOfHearts();
        }

private void RevealRandomVillain()
        {
            if (_session == null || _session.IsComplete || _session.Hearts <= 0) return;
            int[] candidates = Enumerable.Range(0, _session.Monsters.Length)
                .Where(cell => !_session.Monsters[cell] && _session.Level.IsSolutionCell(cell))
                .ToArray();
            if (candidates.Length == 0)
            {
                if (_hintText != null) _hintText.text = "Every villain location is already revealed.";
                return;
            }

            int cell = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            _session.ToggleMonster(cell);
            if (_hintText != null) _hintText.text = "A random villain location has been revealed.";
            if (cell >= 0 && cell < _cells.Count) StartCoroutine(Highlight(_cells[cell].Background));
            _haptics.Light();
        }


private void RefreshBoard()
        {
            if (_session == null || _cells.Count == 0) return; int n = _session.Level.gridSize;
            for (int i = 0; i < _cells.Count; i++)
            {
                var view = _cells[i]; var mark = _session.GetMark(i); view.Mark.text = ""; view.Mark.fontSize = view.BaseFontSize; view.Monster.enabled = false; view.LockBadge.enabled = false; view.RegionSymbol.enabled = _save.Data.settings.regionSymbols;
                if (_hideInitialClueVisuals && mark == CellMark.LockedMonster) continue;
                if (mark == CellMark.PlayerX) { view.Mark.text = "X"; view.Mark.color = _theme.ink; }
                else if (mark == CellMark.AutomaticX) { view.Mark.text = "X"; view.Mark.color = _save.Data.settings.automaticNotesIdentical ? _theme.ink : Color.Lerp(_theme.muted, view.Background.color, .25f); view.Mark.fontSize *= .88f; }
                else if (mark == CellMark.Monster || mark == CellMark.LockedMonster)
                {
                    view.Monster.sprite = _activeVillainSprite;
                    view.Monster.enabled = _activeVillainSprite != null;
                    if (_activeVillainSprite == null) { view.Mark.text = "◆"; view.Mark.color = VillainAccent(VillainGauntlet.Resolve(_session.Level.displayNumber)); }
                    view.LockBadge.enabled = mark == CellMark.LockedMonster;
                }
            }
            int hiddenLocks = _hideInitialClueVisuals ? (_session.Level.lockedMonsterCells?.Length ?? 0) : 0;
            int visibleMonsterCount = _session.Monsters.Where((monster, cell) => monster && (!_hideInitialClueVisuals || !_session.Level.IsLocked(cell))).Count();
            _progress.text = $"{visibleMonsterCount}/{n - hiddenLocks}  MONSTERS"; _hearts.text = $"HEARTS  {_session.Hearts}";
        }

        private void OnSessionChanged() { RefreshBoard(); PersistSession(); }
        private void PersistSession() { if (_session != null && !_session.IsComplete) _save.StoreSession(_session); }
        private void RestartCurrentLevel() { if (_session == null) return; int number = _session.Level.displayNumber; _save.ClearInProgress(); StartLevel(number); }

        private void OnMistake(int cell)
        {
            _audio.Play("mistake"); _haptics.Warning(); var view = _cells[cell]; StartCoroutine(Shake(view.transform as RectTransform));
        }

        private IEnumerator Shake(RectTransform rect)
        {
            var start = rect.anchoredPosition; for (int i = 0; i < 6; i++) { rect.anchoredPosition = start + Vector2.right * (i % 2 == 0 ? 9 : -9); yield return new WaitForSecondsRealtime(.035f); } rect.anchoredPosition = start;
        }

        private void OnCompleted()
        {
            _save.RecordCompletion(_session.Level, _session.ElapsedSeconds, _session.Mistakes); _audio.Play("victory"); _haptics.Success(); StartCoroutine(WinSequence());
        }

        private IEnumerator WinSequence()
        {
            if (!_save.Data.settings.reducedMotion) for (int i = 0; i < _cells.Count; i++) if (_session.Monsters[i]) { StartCoroutine(Pop(_cells[i].transform)); yield return new WaitForSecondsRealtime(.08f); }
            yield return new WaitForSecondsRealtime(.35f); ShowWin();
        }

        private IEnumerator Pop(Transform t) { Vector3 s = t.localScale; t.localScale = s * .8f; float d = 0; while (d < .2f) { d += Time.unscaledDeltaTime; t.localScale = Vector3.Lerp(s * .8f, s * 1.08f, d / .2f); yield return null; } t.localScale = s; }

        private void ShowWin()
        {
            int number = _session.Level.displayNumber; string title = number == 250 ? "CAMPAIGN COMPLETE" : number % 25 == 0 ? "MASTERED!" : "PUZZLE CLEARED";
            ShowModal(title, $"Level {number}\n{FormatTime(_session.ElapsedSeconds)}  ·  {_session.Mistakes} mistakes", number == 250 ? "CHAPTERS" : "NEXT LEVEL", () => { if (number == 250) ShowLevelSelect(); else StartLevel(number + 1); }, "REPLAY", () => StartLevel(number));
        }

        private void ShowOutOfHearts() => ShowModal("THE MOON WENT QUIET", "No hearts left. Your progress is safe—retry when ready.", "RETRY", () => StartLevel(_session.Level.displayNumber), "HOME", ShowHome);

        private void ShowHint()
        {
            string hint = _session.GetHint(out int cell, out bool revealed); _hintText.text = hint; _audio.Play("hint");
            if (cell >= 0 && cell < _cells.Count) StartCoroutine(Highlight(_cells[cell].Background));
            if (revealed) _haptics.Light(); else StartCoroutine(EmphasizeHintCrosses(cell));
        }
        private IEnumerator Highlight(Image image) { Color start = image.color; image.color = Color.Lerp(start, _theme.accent, .45f); yield return new WaitForSecondsRealtime(.65f); image.color = start; }

        // A hint that narrows the board should be visible on the board, not just in
        // prose. Existing automatic exclusions are temporarily promoted to clear Xs.
        private IEnumerator EmphasizeHintCrosses(int focusCell)
        {
            if (_session == null) yield break;
            int[] crosses = _session.GetHelpfulHintCrosses(focusCell);
            foreach (int cell in crosses)
            {
                if (cell < 0 || cell >= _cells.Count) continue;
                var view = _cells[cell]; view.Mark.text = "×"; view.Mark.color = _theme.accent; view.Mark.fontSize = view.BaseFontSize * 1.18f;
                StartCoroutine(Highlight(view.Background));
            }
            if (crosses.Length > 0) _hintText.text += "\nMarked safe exclusions with ×.";
            yield return new WaitForSecondsRealtime(.9f);
            RefreshBoard();
        }

        private void ConfirmRestart() => ShowModal("RESTART PUZZLE?", "Your notes and placed villains will be cleared.", "RESTART", RestartCurrentLevel, "KEEP PLAYING", () => { });

        private void ShowSettings()
        {
            if (GameObject.Find("SettingsOverlay") != null) return;
            var overlayImage = Image(_canvas.transform, "SettingsOverlay", new Color(.05f, .035f, .09f, .76f));
            var overlay = overlayImage.rectTransform; Stretch(overlay); overlay.SetAsLastSibling();
            overlayImage.raycastTarget = true;
            var sheet = Panel(overlay, "SettingsSheet", new Color(.985f, .955f, .90f, 1f)); Anchor(sheet, .5f, .50f, 760, 1220);
            var header = Image(sheet, "Header", new Color(.94f, .86f, .78f)); header.sprite = RuntimeArt.RoundedSprite(28); header.type = UnityEngine.UI.Image.Type.Sliced; Anchor(header.rectTransform, .5f, .94f, 760, 132);
            var title = DisplayText(sheet, "Title", "SETTINGS", 52, new Color(.35f, .18f, .25f), TextAlignmentOptions.Center); title.outlineWidth = 0; Anchor(title.rectTransform, .5f, .94f, 490, 78);
            var close = IconButton(sheet, "Close", RuntimeIcon.Close, () => Destroy(overlay.gameObject)); Anchor(close, .90f, .94f, 70, 70);

            SettingsFeatureCard(sheet, "Sound", RuntimeIcon.Sound, "Sound", 2, .74f);
            SettingsFeatureCard(sheet, "Music", RuntimeIcon.Music, "Music", 1, .50f);
            SettingsFeatureCard(sheet, "Haptics", RuntimeIcon.Haptic, "Haptics", 3, .26f);

            var access = Text(sheet, "AccessibilityTitle", "PLAY YOUR WAY", 21, new Color(.48f, .30f, .29f), FontStyles.Bold, TextAlignmentOptions.Left); Anchor(access.rectTransform, .07f, .585f, 500, 38);
            SettingsRow(sheet, "Theme", "Dark theme", 0, .52f);
            SettingsRow(sheet, "Palette", "Colour-friendly palette", 4, .45f);
            SettingsRow(sheet, "Symbols", "Show region symbols", 5, .38f);
            SettingsRow(sheet, "Motion", "Reduced motion", 6, .31f);
            SettingsRow(sheet, "Input", "Single-tap cycle input", 7, .24f);
            SettingsRow(sheet, "Notes", "Equal automatic X marks", 8, .17f);

            if (_session != null)
            {
                var restart = Button(sheet, "Restart", "RESTART LEVEL", () => { Destroy(overlay.gameObject); ConfirmRestart(); }, false);
                restart.GetComponent<Image>().color = new Color(.99f, .94f, .88f); restart.Find("Label").GetComponent<TMP_Text>().color = new Color(.66f, .25f, .20f);
                var restartOutline = restart.gameObject.AddComponent<Outline>(); restartOutline.effectColor = new Color(.70f, .30f, .22f); restartOutline.effectDistance = new Vector2(3f, -3f); Anchor(restart, .5f, .065f, 620, 78);
            }
        }

        private void SettingsFeatureCard(Transform parent, string name, RuntimeIcon glyph, string label, int settingIndex, float x)
        {
            var card = Panel(parent, name, Color.white); Anchor(card, x, .70f, 194, 178);
            var glyphImage = Icon(card, "Glyph", glyph, new Color(.48f, .30f, .29f)); Anchor(glyphImage.rectTransform, .5f, .66f, 68, 68);
            var labelText = Text(card, "Label", label, 19, new Color(.48f, .30f, .29f), FontStyles.Bold, TextAlignmentOptions.Center); Anchor(labelText.rectTransform, .5f, .42f, 150, 34);
            var button = card.gameObject.AddComponent<Button>(); button.targetGraphic = card.GetComponent<Image>(); button.onClick.AddListener(() => FlipSetting(settingIndex));
            AddToggle(card, SettingValue(settingIndex), .5f, .17f, 126, 46);
        }

        private void SettingsRow(Transform parent, string name, string label, int settingIndex, float y)
        {
            var row = Panel(parent, name, Color.white); Anchor(row, .5f, y, 650, 64);
            var rowLabel = Text(row, "Label", label, 21, new Color(.42f, .27f, .28f), FontStyles.Bold, TextAlignmentOptions.Left); Anchor(rowLabel.rectTransform, .06f, .5f, 415, 42);
            var button = row.gameObject.AddComponent<Button>(); button.targetGraphic = row.GetComponent<Image>(); button.onClick.AddListener(() => FlipSetting(settingIndex));
            AddToggle(row, SettingValue(settingIndex), .88f, .5f, 96, 34);
        }

        private void AddToggle(Transform parent, bool enabled, float x, float y, float width, float height)
        {
            var track = Image(parent, "Toggle", enabled ? new Color(.27f, .70f, .36f) : new Color(.70f, .62f, .57f)); track.sprite = RuntimeArt.RoundedSprite(26); track.type = UnityEngine.UI.Image.Type.Sliced; track.raycastTarget = false; Anchor(track.rectTransform, x, y, width, height);
            var thumb = Image(track.transform, "Thumb", new Color(.99f, .97f, .92f)); thumb.sprite = RuntimeArt.DiscSprite(); thumb.raycastTarget = false; Anchor(thumb.rectTransform, enabled ? .78f : .22f, .5f, height - 8, height - 8);
            var state = Text(track.transform, "State", enabled ? "ON" : "OFF", 14, Color.white, FontStyles.Bold, TextAlignmentOptions.Left); state.raycastTarget = false; Anchor(state.rectTransform, .27f, .5f, 42, height - 4);
        }

        private bool SettingValue(int index) => index switch
        {
            0 => _save.Data.settings.darkTheme, 1 => _save.Data.settings.music, 2 => _save.Data.settings.sfx, 3 => _save.Data.settings.haptics,
            4 => _save.Data.settings.colourFriendly, 5 => _save.Data.settings.regionSymbols, 6 => _save.Data.settings.reducedMotion,
            7 => _save.Data.settings.accessibilityCycle, 8 => _save.Data.settings.automaticNotesIdentical, _ => false
        };

        private void FlipSetting(int index)
        {
            switch (index)
            {
                case 0: _save.Data.settings.darkTheme = !_save.Data.settings.darkTheme; break;
                case 1: _save.Data.settings.music = !_save.Data.settings.music; break;
                case 2: _save.Data.settings.sfx = !_save.Data.settings.sfx; break;
                case 3: _save.Data.settings.haptics = !_save.Data.settings.haptics; break;
                case 4: _save.Data.settings.colourFriendly = !_save.Data.settings.colourFriendly; break;
                case 5: _save.Data.settings.regionSymbols = !_save.Data.settings.regionSymbols; break;
                case 6: _save.Data.settings.reducedMotion = !_save.Data.settings.reducedMotion; break;
                case 7: _save.Data.settings.accessibilityCycle = !_save.Data.settings.accessibilityCycle; break;
                case 8: _save.Data.settings.automaticNotesIdentical = !_save.Data.settings.automaticNotesIdentical; break;
            }
            _save.Save(); _audio.SetAmbience(_save.Data.settings.darkTheme);
            var previous = GameObject.Find("SettingsOverlay"); if (previous != null) Destroy(previous);
            ShowSettings();
        }

        private void ToggleTheme() { _save.Data.settings.darkTheme = !_save.Data.settings.darkTheme; _save.Save(); _theme = ThemeService.Get(_save.Data.settings.darkTheme, _save.Data.settings.colourFriendly); _audio.SetAmbience(_save.Data.settings.darkTheme); ShowHome(); }

        private void ShowModal(string title, string body, string primary, Action primaryAction, string secondary, Action secondaryAction)
        {
            var overlay = Panel(_canvas.transform, "ModalOverlay", new Color(0.05f, .03f, .12f, .72f)); Stretch(overlay); overlay.SetAsLastSibling();
            var card = Panel(overlay, "ModalCard", _theme.panel); Anchor(card, .5f, .5f, 720, 560);
            var icon = Text(card, "Icon", "*", 84, _theme.accent, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(icon.rectTransform, .5f, .78f, 150, 120);
            var head = Text(card, "Title", title, 43, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(head.rectTransform, .5f, .62f, 640, 100);
            var copy = Text(card, "Body", body, 26, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(copy.rectTransform, .5f, .45f, 620, 120);
            var p = Button(card, "Primary", primary, () => { Destroy(overlay.gameObject); primaryAction?.Invoke(); }); Anchor(p, .5f, .24f, 580, 92);
            var s = Button(card, "Secondary", secondary, () => { Destroy(overlay.gameObject); secondaryAction?.Invoke(); }, false); Anchor(s, .5f, .08f, 580, 76);
            _audio.Play("panel");
        }

        private void BeginScreen(string name)
        {
            if (_screen != null) Destroy(_screen); _screen = new GameObject(name, typeof(RectTransform)); _screen.transform.SetParent(_safeRoot, false); Stretch((RectTransform)_screen.transform);
        }

        private void AddBackdrop(Transform parent, int chapter)
        {
            Image(parent, "Background", _theme.background).transform.SetAsFirstSibling(); Stretch((RectTransform)parent.Find("Background"));
            var moon = Text(parent, "Moon", "O", 180, new Color(_theme.accent.r, _theme.accent.g, _theme.accent.b, .08f), FontStyles.Normal, TextAlignmentOptions.Center); Anchor(moon.rectTransform, .82f, .84f, 250, 250); moon.raycastTarget = false;
            for (int i = 0; i < 9; i++) { var star = Text(parent, "Star", "*", 20 + i % 3 * 8, new Color(_theme.accent.r, _theme.accent.g, _theme.accent.b, .10f), FontStyles.Normal, TextAlignmentOptions.Center); Anchor(star.rectTransform, .08f + (i * .113f) % .84f, .12f + (i * .197f) % .78f, 40, 40); star.raycastTarget = false; }
        }

        private void Rule(Transform parent, string value) { var p = Panel(parent, "Rule", _theme.panel); var t = Text(p, "Text", value, 20, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Stretch(t.rectTransform); }
        private RectTransform Panel(Transform parent, string name, Color color) { var image = Image(parent, name, color); image.sprite = RuntimeArt.RoundedSprite(28); image.type = UnityEngine.UI.Image.Type.Sliced; Elevate(image, 4f); return image.rectTransform; }
        private Image Image(Transform parent, string name, Color color) { var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); var image = go.GetComponent<Image>(); image.color = color; return image; }
        private Image Icon(Transform parent, string name, RuntimeIcon icon, Color color) { var image = Image(parent, name, color); image.sprite = RuntimeArt.IconSprite(icon); image.preserveAspect = true; image.raycastTarget = false; return image; }
        private RectTransform IconButton(Transform parent, string name, RuntimeIcon icon, Action action)
        {
            var button = Button(parent, name, string.Empty, action, true); var image = Icon(button, "Icon", icon, _theme.ink); Anchor(image.rectTransform, .5f, .5f, 46, 46); return button;
        }
        private static void Elevate(Graphic graphic, float distance)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(.06f, .025f, .11f, .20f); shadow.effectDistance = new Vector2(0f, -distance);
        }
        private RawImage Raw(Transform parent, string name, Texture texture) { var go = new GameObject(name, typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(parent, false); var image = go.GetComponent<RawImage>(); image.texture = texture; image.color = texture == null ? new Color(0,0,0,0) : Color.white; image.raycastTarget = false; return image; }
        private Image SpriteImage(Transform parent, string name, Sprite sprite)
        {
            var image = Image(parent, name, Color.white); image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
            if (sprite == null) image.color = new Color(0f, 0f, 0f, 0f); return image;
        }
        private TMP_Text Text(Transform parent, string name, string value, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        { var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); var text = go.GetComponent<TextMeshProUGUI>(); text.font = _font; text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style; text.alignment = alignment; text.enableWordWrapping = true; return text; }
        private TMP_Text DisplayText(Transform parent, string name, string value, float size, Color color, TextAlignmentOptions alignment)
        {
            var text = Text(parent, name, value, size, color, FontStyles.Bold, alignment);
            text.font = _displayFont;
            text.outlineColor = new Color(.055f, .025f, .12f, .95f); text.outlineWidth = .18f; text.characterSpacing = 1.1f;
            return text;
        }
        private RectTransform Button(Transform parent, string name, string label, Action action, bool icon = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false); var image = go.GetComponent<Image>(); image.color = icon ? _theme.panel : _theme.accent; image.sprite = RuntimeArt.RoundedSprite(icon ? 30 : 34); image.type = UnityEngine.UI.Image.Type.Sliced; Elevate(image, icon ? 4f : 5f);
            var button = go.GetComponent<Button>(); button.targetGraphic = image; var colors = button.colors; colors.normalColor = Color.white; colors.pressedColor = new Color(.84f,.84f,.9f); colors.highlightedColor = new Color(.96f,.96f,1f); button.colors = colors; button.onClick.AddListener(() => { _audio?.Play("tap"); action?.Invoke(); });
            var text = Text(go.transform, "Label", label, icon ? (label.Length > 1 ? 18 : 44) : 27, icon ? _theme.ink : Color.white, FontStyles.Bold, TextAlignmentOptions.Center); Stretch(text.rectTransform); text.raycastTarget = false; return (RectTransform)go.transform;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Anchor(RectTransform rect, float x, float y, float width, float height) { rect.anchorMin = rect.anchorMax = new Vector2(x, y); rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = Vector2.zero; }
        private static string FormatTime(float seconds) => $"{Mathf.FloorToInt(seconds / 60)}:{Mathf.FloorToInt(seconds % 60):00}";
        private static int ParseNumber(string id) => int.TryParse(id?.Split('-').LastOrDefault(), out int n) ? n : 0;
        private static string TutorialCopy(int level) => level switch { 1 => "Tap once to mark an impossible cell with X.", 2 => "Double-tap or long-press to place a monster.", 3 => "Every coloured region needs exactly one monster.", 4 => "Every row and column needs exactly one monster.", 5 => "Regions can bend—follow the thicker boundary.", 6 => "Monsters cannot touch, even at the corners.", <= 10 => "Combine all four rules. Hints explain before they reveal.", _ => "Single tap: X  ·  Double tap: monster" };
    }

    internal enum RuntimeIcon { Gear, Sound, Music, Haptic, Hint, Close }

    internal static class RuntimeArt
    {
        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>(); private static readonly Dictionary<RuntimeIcon, Sprite> IconCache = new Dictionary<RuntimeIcon, Sprite>(); private static Sprite _disc;

        public static Sprite IconSprite(RuntimeIcon icon)
        {
            if (IconCache.TryGetValue(icon, out var cached)) return cached;
            const int size = 96; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "UI_" + icon, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size]; Color32 white = new Color32(255, 255, 255, 255);
            void Dot(int x, int y) { if (x >= 0 && x < size && y >= 0 && y < size) pixels[y * size + x] = white; }
            void Rect(int minX, int minY, int maxX, int maxY) { for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++) Dot(x, y); }
            void Circle(int cx, int cy, int radius) { for (int y = cy - radius; y <= cy + radius; y++) for (int x = cx - radius; x <= cx + radius; x++) if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius) Dot(x, y); }
            void Line(int x0, int y0, int x1, int y1, int width = 5) { int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)); for (int i = 0; i <= steps; i++) Circle(Mathf.RoundToInt(Mathf.Lerp(x0, x1, i / (float)steps)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, i / (float)steps)), width); }
            switch (icon)
            {
                case RuntimeIcon.Gear:
                    Circle(48, 48, 27); Circle(48, 48, 12); for (int i = 0; i < 8; i++) { float angle = i * Mathf.PI * .25f; Circle(48 + Mathf.RoundToInt(Mathf.Cos(angle) * 31), 48 + Mathf.RoundToInt(Mathf.Sin(angle) * 31), 9); } Circle(48, 48, 11); break;
                case RuntimeIcon.Sound:
                    Rect(15, 39, 36, 57); for (int x = 36; x <= 58; x++) for (int y = 25; y <= 71; y++) if (Mathf.Abs(y - 48) <= x - 30) Dot(x, y); Line(67, 36, 76, 48, 4); Line(76, 48, 67, 60, 4); break;
                case RuntimeIcon.Music:
                    Line(60, 20, 60, 65, 5); Line(38, 22, 62, 22, 5); Circle(46, 69, 13); break;
                case RuntimeIcon.Haptic:
                    Rect(34, 18, 62, 76); Circle(48, 67, 4); Line(24, 32, 18, 48, 4); Line(18, 48, 24, 64, 4); Line(72, 32, 78, 48, 4); Line(78, 48, 72, 64, 4); break;
                case RuntimeIcon.Hint:
                    Circle(48, 39, 22); Rect(40, 57, 56, 68); Rect(42, 72, 54, 78); break;
                case RuntimeIcon.Close:
                    Line(24, 24, 72, 72, 6); Line(72, 24, 24, 72, 6); break;
            }
            texture.SetPixels32(pixels); texture.Apply(); cached = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100); IconCache[icon] = cached; return cached;
        }

        public static Sprite DiscSprite()
        {
            if (_disc != null) return _disc;
            const int size = 96; var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "RoundAura", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size]; float center = (size - 1) * .5f, radius = center - 1f;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)); byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius + 1f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            tex.SetPixels32(pixels); tex.Apply(); _disc = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100); return _disc;
        }

        public static Sprite RoundedSprite(int radius)
        {
            if (Cache.TryGetValue(radius, out var sprite)) return sprite;
            const int size = 64; var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = $"RoundedPanel_{radius}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size]; float r = Mathf.Clamp(radius, 2, 31);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(r - x, 0, x - (size - 1 - r)), dy = Mathf.Max(r - y, 0, y - (size - 1 - r)); float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(r + .5f - distance) * 255); pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels); tex.Apply(); sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius)); Cache[radius] = sprite; return sprite;
        }
    }
}
