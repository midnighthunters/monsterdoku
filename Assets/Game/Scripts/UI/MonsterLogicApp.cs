using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MonsterLogic.Ads;
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
        private PuzzleLevelDatabase _database; private SaveService _save; private IAdService _ads; private AdPolicy _adPolicy;
        private AudioService _audio; private HapticService _haptics; private ThemePalette _theme; private TMP_FontAsset _font, _displayFont;
        private Canvas _canvas; private RectTransform _safeRoot, _contentRoot; private BannerAwareContentLayout _bannerLayout; private GameObject _screen, _toast; private PuzzleSession _session;
        private readonly List<CellView> _cells = new List<CellView>(); private TMP_Text _progress, _hearts, _timer, _hintText;
        private TMP_Text _villainBoosterCount, _hintBoosterCount;
        private Image _villainAdBadge, _hintAdBadge;
        private Texture2D _appIcon, _zemoLogo; private GameSceneSpriteSet _gameSprites;
        private Sprite _lockSprite, _activeVillainSprite, _activeCrossSprite, _playAdSprite;
        private static readonly Color GameScreenLavender = new Color32(204, 188, 235, 255);
        private Material _crossImprintMaterial;
        private bool _initialized, _adActionInProgress, _adBreakActive, _screenWantsBanner; private int _overlayDepth; private long _completionToken;

        private const string FeedbackUrl = "mailto:support@zemolabs.com?subject=Monster%20Logic%20Feedback";
        private const string TermsUrl = "https://zemolabs.com/terms";
        private const string PrivacyUrl = "https://zemolabs.com/privacy";

        public void Initialize(PuzzleLevelDatabase database, SaveService save, IAdService ads, AdPolicy adPolicy, TMP_FontAsset displayFont = null, TMP_FontAsset bodyFont = null)
        {
            if (_initialized) return;
            _initialized = true;
            _database = database;
            _save = save;
            _ads = ads ?? new NoOpAdService();
            _adPolicy = adPolicy ?? new AdPolicy();
            _ads.FullscreenAdStateChanged += OnFullscreenAdStateChanged;
            _ads.BannerHeightChanged += OnBannerHeightChanged;
            _ads.FullscreenAdWillPresent += PersistSession;
            _theme = ThemeService.Get(_save.Data.settings.darkTheme, _save.Data.settings.colourFriendly);
            _audio = new AudioService(_save.Data.settings, gameObject);
            _haptics = new HapticService(_save.Data.settings);
            _audio.SetAmbience(_save.Data.settings.darkTheme);
            _font = bodyFont != null ? bodyFont : TMP_Settings.defaultFontAsset;
            _displayFont = displayFont != null ? displayFont : _font;
            _appIcon = Resources.Load<Texture2D>("AppIcon");
            _zemoLogo = Resources.Load<Texture2D>("logo");
            _gameSprites = Resources.Load<GameSceneSpriteSet>("GameSceneSpriteSet");
            if (_gameSprites == null) Debug.LogError("Monster Logic gameplay sprite set is missing from Resources/GameSceneSpriteSet.");
            _playAdSprite = _gameSprites != null ? _gameSprites.PlayAdIcon : null;
            _activeCrossSprite = _gameSprites != null ? _gameSprites.CellBatWatermark : null;
            _crossImprintMaterial = Resources.Load<Material>("CrossImprint");
            _lockSprite = Resources.LoadAll<Sprite>("lock").FirstOrDefault(sprite => sprite != null && sprite.name == "lock_0");
            BuildFoundation();
            StartCoroutine(BootSequence());
        }

        private IEnumerator BootSequence()
        {
            ShowLoading(); yield return StartCoroutine(AnimateSplash());
            int requested = PlayerPrefs.GetInt("MonsterLogic.PlayLevel", 0); PlayerPrefs.DeleteKey("MonsterLogic.PlayLevel");
            if (requested > 0) StartLevel(requested); else ShowHome();
        }

        private void Update()
        {
            if (_session != null && !_session.IsComplete && _session.Hearts > 0 && !_adBreakActive)
            { _session.ElapsedSeconds += Time.unscaledDeltaTime; if (_timer != null) _timer.text = FormatTime(_session.ElapsedSeconds); }
        }

        private void OnApplicationPause(bool paused) { if (paused) PersistSession(); }
        private void OnApplicationQuit() => PersistSession();
        private void OnDestroy()
        {
            if (_ads == null) return;
            _ads.FullscreenAdStateChanged -= OnFullscreenAdStateChanged;
            _ads.BannerHeightChanged -= OnBannerHeightChanged;
            _ads.FullscreenAdWillPresent -= PersistSession;
            _ads.Shutdown();
        }

        private void BuildFoundation()
        {
            if (TryUseAuthoredFoundation())
            {
                EnsureEventSystem();
                return;
            }

            var canvasGo = new GameObject("MonsterLogicCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            ConfigureCanvas(_canvas);

            var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            safe.transform.SetParent(canvasGo.transform, false);
            _safeRoot = safe.GetComponent<RectTransform>();
            Stretch(_safeRoot);

            var content = new GameObject("BannerAwareContent", typeof(RectTransform), typeof(BannerAwareContentLayout));
            content.transform.SetParent(_safeRoot, false);
            _contentRoot = content.GetComponent<RectTransform>();
            Stretch(_contentRoot);
            _bannerLayout = content.GetComponent<BannerAwareContentLayout>();
            _bannerLayout.Configure(_canvas);
            EnsureEventSystem();
        }

        private bool TryUseAuthoredFoundation()
        {
            var canvasRoot = transform.Find("MonsterLogicCanvas");
            if (canvasRoot == null) return false;

            var canvas = canvasRoot.GetComponent<Canvas>();
            var safeRoot = canvasRoot.Find("SafeArea") as RectTransform;
            var contentRoot = safeRoot != null ? safeRoot.Find("BannerAwareContent") as RectTransform : null;
            var bannerLayout = contentRoot != null ? contentRoot.GetComponent<BannerAwareContentLayout>() : null;
            if (canvas == null || safeRoot == null || contentRoot == null || bannerLayout == null)
            {
                Debug.LogWarning("Monster Logic found an authored MonsterLogicCanvas, but it is missing Canvas, SafeArea, or BannerAwareContent. Creating a runtime fallback instead.");
                return false;
            }

            _canvas = canvas;
            _safeRoot = safeRoot;
            _contentRoot = contentRoot;
            _bannerLayout = bannerLayout;
            ConfigureCanvas(_canvas);
            Stretch(_safeRoot);
            Stretch(_contentRoot);
            _bannerLayout.Configure(_canvas);
            return true;
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;

            var scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = .5f;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(transform, false);
        }

        private void ShowLoading()
        {
            BeginScreen("ZemoLabsSplash");
            var background = Image(_screen.transform, "Background", new Color(.035f, .025f, .12f, 1f)); Stretch(background.rectTransform);
            var emblem = Raw(_screen.transform, "ZemoLabsLogo", _zemoLogo); Anchor(emblem.rectTransform, .5f, .5f, 500, 500);
        }

        private IEnumerator AnimateSplash()
        {
            if (_screen == null) yield break;
            var splash = _screen;
            var group = splash.AddComponent<CanvasGroup>();
            var logo = splash.transform.Find("ZemoLabsLogo") as RectTransform;
            group.alpha = 0f;
            if (logo != null) logo.localScale = Vector3.one * .78f;
            float elapsed = 0f;
            while (elapsed < .48f && splash != null)
            {
                elapsed += Time.unscaledDeltaTime; float t = Mathf.Clamp01(elapsed / .48f); float eased = 1f - Mathf.Pow(1f - t, 3f);
                group.alpha = eased;
                if (logo != null) logo.localScale = Vector3.one * Mathf.Lerp(.78f, 1f, eased);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.52f);
            elapsed = 0f;
            while (elapsed < .24f && splash != null)
            {
                elapsed += Time.unscaledDeltaTime; group.alpha = 1f - Mathf.Clamp01(elapsed / .24f); yield return null;
            }
        }

        public void ShowHome()
        {
            PersistSession(); _session = null; BeginScreen("Home"); AddBackdrop(_screen.transform, Mathf.Max(0, (_save.Data.highestUnlocked - 1) / 25));
            var emblem = RawButton(_screen.transform, "ChapterLogo", _appIcon, ShowLevelSelect); Anchor(emblem, .5f, .72f, 340, 340);
            var title = Text(_screen.transform, "Title", "monsterdoku", 82, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(title.rectTransform, .5f, .49f, 760, 210);
            bool canContinue = _save.Data.highestUnlocked > 1 && _save.Data.currentLevelId == $"campaign-{_save.Data.highestUnlocked:000}";
            var play = Button(_screen.transform, "Play", canContinue ? $"CONTINUE  ·  LEVEL {_save.Data.highestUnlocked}" : "PLAY", () => StartLevel(_save.Data.highestUnlocked)); Anchor(play, .5f, .30f, 650, 118);
            var footer = Text(_screen.transform, "Footer", "250 puzzles  •  Optional rewards  •  Progress saved", 20, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(footer.rectTransform, .5f, .08f, 760, 46);
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
                BuildChapterCard(grid, ch, names[chapter - 1], first, done, unlocked);
            }
        }

        private void BuildChapterCard(Transform parent, int chapter, string chapterName, int firstLevel, int completed, bool unlocked)
        {
            var card = Button(parent, $"Chapter{chapter}", string.Empty, () => { if (unlocked) ShowChapter(chapter); }, false);
            var cardImage = card.GetComponent<Image>(); cardImage.color = unlocked ? _theme.panel : Color.Lerp(_theme.panelAlt, _theme.background, .35f);
            card.GetComponent<Button>().interactable = unlocked;

            var portrait = SpriteImage(card, "VillainPortrait", VillainSprite(VillainGauntlet.Resolve(firstLevel)));
            portrait.color = portrait.sprite == null ? Color.clear : unlocked ? Color.white : new Color(.58f, .58f, .64f, .62f);
            Anchor(portrait.rectTransform, .5f, .64f, 186, 152);

            var caption = Image(card, "Caption", new Color(_theme.background.r, _theme.background.g, _theme.background.b, .90f));
            caption.sprite = RuntimeArt.RoundedSprite(18); caption.type = UnityEngine.UI.Image.Type.Sliced; caption.raycastTarget = false; Anchor(caption.rectTransform, .5f, .15f, 356, 58);
            var label = Text(caption.transform, "Label", $"{chapter:00}  {chapterName}\n{completed}/25  ·  {(_database.GetByNumber(firstLevel)?.gridSize ?? 5)}×{(_database.GetByNumber(firstLevel)?.gridSize ?? 5)}", 18, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(label.rectTransform); label.raycastTarget = false;

            if (!unlocked)
            {
                var shade = Image(card, "LockedShade", new Color(.03f, .025f, .10f, .28f)); Stretch(shade.rectTransform); shade.raycastTarget = false;
                var lockIcon = SpriteImage(card, "LockIcon", _lockSprite); lockIcon.color = Color.white; Anchor(lockIcon.rectTransform, .78f, .66f, 78, 98);
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
            var level = _database.GetByNumber(Mathf.Clamp(number, 1, Mathf.Max(1, _database.levels.Count)));
            if (level == null) return;

            _activeVillainSprite = VillainSprite(VillainGauntlet.Resolve(level.displayNumber));
            _activeCrossSprite = _gameSprites != null ? _gameSprites.CellBatWatermark : null;
            bool restoringSession = _save.HasSessionFor(level);
            _session = new PuzzleSession(level);
            if (restoringSession)
                _session.Restore(_save.Data.inProgressMonsters, _save.Data.inProgressPlayerNotes, _save.Data.inProgressHearts, _save.Data.inProgressMistakes, _save.Data.inProgressSeconds, _save.Data.inProgressVillainBoosters, _save.Data.inProgressHintBoosters);
            else
                _save.ClearInProgress(false);
            _session.Changed += OnSessionChanged;
            _session.MistakeMade += OnMistake;
            _session.Completed += OnCompleted;

            BeginScreen("Game");
            AddGameBackdrop(_screen.transform);

            var plaque = SpritePanel(_screen.transform, "LevelPlaque", _gameSprites != null ? _gameSprites.HeaderPlaque : null, new Color(.36f, .24f, .62f, 1f));
            Anchor(plaque, .5f, .947f, 630, 224);
            if (plaque.GetComponent<Image>().sprite != null) Elevate(plaque.GetComponent<Image>(), 7f);
            var title = DisplayText(_screen.transform, "LevelTitle", level.displayNumber % 25 == 0 ? $"MASTER PUZZLE  ·  {level.displayNumber}" : $"LEVEL {level.displayNumber}", 51, Color.white, TextAlignmentOptions.Center);
            Anchor(title.rectTransform, .5f, .947f, 580, 82);

            var back = Button(_screen.transform, "Back", string.Empty, ShowHome, true);
            StyleGameSpriteButton(back, _gameSprites != null ? _gameSprites.BackButton : null);
            Anchor(back, .09f, .958f, 118, 118);
            var settings = Button(_screen.transform, "Settings", string.Empty, ShowSettings, true);
            StyleGameSpriteButton(settings, _gameSprites != null ? _gameSprites.SettingsButton : null);
            Anchor(settings, .91f, .958f, 118, 118);

            BuildStats(level);

            var rules = Container(_screen.transform, "Rules");
            Anchor(rules, .5f, .748f, 1030, 142);
            var ruleLayout = rules.gameObject.AddComponent<HorizontalLayoutGroup>();
            ruleLayout.spacing = 12;
            ruleLayout.childForceExpandWidth = true;
            ruleLayout.childForceExpandHeight = true;
            ruleLayout.childControlWidth = true;
            ruleLayout.childControlHeight = true;
            Rule(rules, "RuleRegion", _gameSprites != null ? _gameSprites.RuleRegion : null, "1 per region");
            Rule(rules, "RuleRowColumn", _gameSprites != null ? _gameSprites.RuleRowColumn : null, "1 per row\n+ column");
            Rule(rules, "RuleNoTouch", _gameSprites != null ? _gameSprites.RuleNoTouch : null, "No touching");

            float boardSize = BoardSizeFor(level.gridSize);
            var boardFrame = Image(_screen.transform, "BoardFrame", new Color(1f, 1f, 1f, .82f));
            boardFrame.sprite = RuntimeArt.RoundedSprite(34);
            boardFrame.type = UnityEngine.UI.Image.Type.Sliced;
            boardFrame.raycastTarget = false;
            var frameOutline = boardFrame.gameObject.AddComponent<Outline>();
            frameOutline.effectColor = new Color(.34f, .19f, .64f, .46f);
            frameOutline.effectDistance = new Vector2(2f, -2f);
            Elevate(boardFrame, 9f);
            Anchor(boardFrame.rectTransform, .5f, .4375f, boardSize + 42, boardSize + 42);
            BuildBoard(level);
            BuildInstructionPanel(level);
            BuildGameplayActions(level);
            RefreshBoard();
            StartCoroutine(PlayLevelEntrance(level.displayNumber));
        }

        private static float BoardSizeFor(int gridSize) => Mathf.Clamp(892f + Mathf.Max(0, gridSize - 6) * 35f, 892f, 962f);

        private void BuildBoard(PuzzleLevelData level)
        {
            _cells.Clear();
            int n = level.gridSize;
            float boardSize = BoardSizeFor(n);
            var board = Container(_screen.transform, "Board");
            Anchor(board, .5f, .4375f, boardSize, boardSize);

            const float cellGap = 6f;
            float cellSize = (boardSize - cellGap * (n - 1)) / n;
            var grid = board.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = n;
            grid.cellSize = new Vector2(cellSize, cellSize);
            grid.spacing = new Vector2(cellGap, cellGap);
            grid.padding = new RectOffset(0, 0, 0, 0);

            for (int cell = 0; cell < n * n; cell++)
            {
                int index = cell;
                int region = level.regionIdByCell[cell];
                var go = new GameObject($"Cell_{cell / n}_{cell % n}", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(CellView));
                go.transform.SetParent(board, false);

                var bg = go.GetComponent<Image>();
                bg.sprite = RuntimeArt.RoundedSprite(14);
                bg.type = UnityEngine.UI.Image.Type.Sliced;
                bg.color = _theme.regions[region % _theme.regions.Length];
                bg.raycastTarget = true;
                var outline = go.AddComponent<Outline>();
                outline.effectColor = new Color(.43f, .28f, .69f, .27f);
                outline.effectDistance = new Vector2(1f, -1f);

                var emptyIcon = SpriteImage(go.transform, "EmptyIcon", _activeCrossSprite);
                if (_crossImprintMaterial != null) emptyIcon.material = _crossImprintMaterial;
                emptyIcon.color = _activeCrossSprite == null ? Color.clear : new Color(.35f, .31f, .53f, .11f);
                Anchor(emptyIcon.rectTransform, .5f, .5f, grid.cellSize.x * .64f, grid.cellSize.y * .64f);

                var mark = Text(go.transform, "Mark", string.Empty, Mathf.RoundToInt(170f / n), _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(mark.rectTransform);
                mark.raycastTarget = false;

                var monsterGo = new GameObject("Monster", typeof(RectTransform), typeof(Image));
                monsterGo.transform.SetParent(go.transform, false);
                var monster = monsterGo.GetComponent<Image>();
                monster.sprite = _activeVillainSprite;
                monster.preserveAspect = true;
                monster.color = _activeVillainSprite == null ? Color.clear : Color.white;
                monster.raycastTarget = false;
                Anchor(monster.rectTransform, .5f, .52f, grid.cellSize.x * .90f, grid.cellSize.y * .90f);

                var regionSymbol = Text(go.transform, "Region", ((char)('A' + region)).ToString(), Mathf.RoundToInt(50f / n + 9), Color.Lerp(_theme.ink, bg.color, .35f), FontStyles.Bold, TextAlignmentOptions.Center);
                Anchor(regionSymbol.rectTransform, .17f, .82f, 26, 26);
                regionSymbol.raycastTarget = false;

                var view = go.GetComponent<CellView>();
                view.Configure(index, bg, emptyIcon, mark, monster, regionSymbol);
                view.Activated += OnCellActivated;
                _cells.Add(view);
            }
        }

        private void BuildGameplayActions(PuzzleLevelData level)
        {
            var actions = Container(_screen.transform, "GameplayActions");
            Anchor(actions, .5f, .072f, 560, 220);

            var villain = BuildBoosterIcon(actions, "VillainBooster", _gameSprites != null ? _gameSprites.BoosterCircle : null, _gameSprites != null ? _gameSprites.MummyBoosterAvatar : _activeVillainSprite, UseVillainBoosterOrAd, out _villainBoosterCount, out _villainAdBadge);
            Anchor(villain, .28f, .57f, 204, 204);

            var hint = BuildBoosterIcon(actions, "HintBooster", _gameSprites != null ? _gameSprites.HintButton : null, null, UseHintBoosterOrAd, out _hintBoosterCount, out _hintAdBadge);
            Anchor(hint, .72f, .57f, 204, 204);
            RefreshBoosterBar();
        }

        private RectTransform BuildBoosterIcon(Transform parent, string name, Sprite frameSprite, Sprite iconSprite, Action action, out TMP_Text countText, out Image playBadge)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var hitArea = go.GetComponent<Image>();
            hitArea.color = Color.clear;

            var frame = SpritePanel(go.transform, "Frame", frameSprite, Color.white);
            Stretch(frame);
            if (iconSprite != null)
            {
                var icon = SpriteImage(go.transform, "Icon", iconSprite);
                icon.color = Color.white;
                Anchor(icon.rectTransform, .5f, .58f, 132, 132);
            }

            var badge = SpritePanel(go.transform, "CountBadge", _gameSprites != null ? _gameSprites.BoosterCountPill : null, Color.white);
            Anchor(badge, .5f, .045f, 92, 48);
            countText = Text(badge, "Count", string.Empty, 25, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(countText.rectTransform);
            countText.raycastTarget = false;
            playBadge = SpriteImage(badge, "PlayAd", _playAdSprite);
            Anchor(playBadge.rectTransform, .5f, .5f, 36, 36);

            var button = go.GetComponent<Button>();
            button.targetGraphic = hitArea;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.96f, .96f, 1f);
            colors.pressedColor = new Color(.76f, .76f, .84f);
            button.colors = colors;
            button.onClick.AddListener(() => { if (_adBreakActive) return; _audio?.Play("tap"); action?.Invoke(); });
            return (RectTransform)go.transform;
        }

        private void RefreshBoosterBar()
        {
            if (_session == null) return;
            RefreshBoosterBadge(_villainBoosterCount, _villainAdBadge, _session.VillainBoosters);
            RefreshBoosterBadge(_hintBoosterCount, _hintAdBadge, _session.HintBoosters);
        }

        private static void RefreshBoosterBadge(TMP_Text countText, Image playBadge, int count)
        {
            bool hasCharge = count > 0;
            if (countText != null) { countText.enabled = hasCharge; countText.text = count.ToString(); }
            if (playBadge != null) playBadge.enabled = !hasCharge;
        }

private IEnumerator ShowVillainUnlockIfNeeded(int levelNumber)
        {
            yield return null;
            if (_session == null || _session.Level.displayNumber != levelNumber) yield break;
#if UNITY_EDITOR
            if (EditorSuppressUnlockPopup) yield break;
#endif
            var tier = VillainGauntlet.Resolve(levelNumber);
            if (!tier.IsFirstLevel || _save.HasAcknowledgedVillainTier(tier.AcknowledgementId)) yield break;
            ShowVillainUnlock(tier);
        }

private IEnumerator PlayLevelEntrance(int levelNumber)
        {
            yield return StartCoroutine(AnimateBoardEntrance());
#if UNITY_EDITOR
            EditorLevelAndBoardLoaded?.Invoke(levelNumber);
#endif
            yield return StartCoroutine(ShowVillainUnlockIfNeeded(levelNumber));
        }

        private IEnumerator AnimateBoardEntrance()
        {
            if (_save.Data.settings.reducedMotion || _cells.Count == 0) yield break;
            Canvas.ForceUpdateCanvases();
            int size = _session?.Level.gridSize ?? 1;
            var groups = new CanvasGroup[_cells.Count];
            var startAngles = new float[_cells.Count];
            var delays = new float[_cells.Count];
            float lastDelay = 0f;
            for (int i = 0; i < _cells.Count; i++)
            {
                groups[i] = _cells[i].GetComponent<CanvasGroup>();
                groups[i].alpha = 0f;
                _cells[i].transform.localScale = Vector3.one * .68f;
                startAngles[i] = ((i / size + i % size) % 2 == 0 ? -1f : 1f) * 4.5f;
                _cells[i].transform.localRotation = Quaternion.Euler(0f, 0f, startAngles[i]);
                int row = i / size, column = i % size;
                delays[i] = (row + column) * .035f + Mathf.Abs(row - column) * .006f;
                lastDelay = Mathf.Max(lastDelay, delays[i]);
            }

            const float duration = .30f;
            float elapsed = 0f;
            while (elapsed < lastDelay + duration)
            {
                elapsed += Time.unscaledDeltaTime;
                for (int i = 0; i < _cells.Count; i++)
                {
                    if (_cells[i] == null) continue;
                    float t = Mathf.Clamp01((elapsed - delays[i]) / duration);
                    float p = t - 1f;
                    float backEase = 1f + 2.70158f * p * p * p + 1.70158f * p * p;
                    groups[i].alpha = t;
                    _cells[i].transform.localScale = Vector3.one * Mathf.LerpUnclamped(.68f, 1f, backEase);
                    _cells[i].transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startAngles[i], 0f, t));
                }
                yield return null;
            }

            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i] == null) continue;
                groups[i].alpha = 1f; _cells[i].transform.localScale = Vector3.one; _cells[i].transform.localRotation = Quaternion.identity;
            }
        }

        private void ShowVillainUnlock(VillainTier tier)
        {
            Color accent = VillainAccent(tier);
            var overlay = BeginOverlay("VillainUnlockOverlay", new Color(.025f, .02f, .08f, .88f));
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
            var begin = Button(card, "BeginHunt", "BEGIN THE HUNT", () => { _save.AcknowledgeVillainTier(tier.AcknowledgementId); CloseOverlay(overlay.gameObject); });
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

        private void OnCellActivated(int cell, bool monsterAction)
        {
            if (_session == null || _adBreakActive) return;

            bool placeVillain = monsterAction;
            if (_save.Data.settings.accessibilityCycle && !placeVillain) _session.Cycle(cell);
            else if (placeVillain) _session.ToggleMonster(cell);
            else _session.ToggleNote(cell);

            _audio.Play(placeVillain ? "monster" : "x");
            if (_session.Hearts <= 0) ShowOutOfHearts();
        }

        private void RefreshBoard()
        {
            if (_session == null || _cells.Count == 0) return; int n = _session.Level.gridSize;
            for (int i = 0; i < _cells.Count; i++)
            {
                var view = _cells[i]; var mark = _session.GetMark(i); view.Mark.text = ""; view.Mark.fontSize = view.BaseFontSize; view.EmptyIcon.enabled = mark == CellMark.Empty && _activeCrossSprite != null; view.Monster.enabled = false; view.RegionSymbol.enabled = _save.Data.settings.regionSymbols;
                if (mark == CellMark.PlayerX) { view.Mark.text = "X"; view.Mark.color = _theme.ink; }
                else if (mark == CellMark.AutomaticX) { view.Mark.text = "X"; view.Mark.color = _save.Data.settings.automaticNotesIdentical ? _theme.ink : Color.Lerp(_theme.muted, view.Background.color, .25f); view.Mark.fontSize *= .88f; }
                else if (mark == CellMark.Monster)
                {
                    view.Monster.sprite = _activeVillainSprite;
                    view.Monster.enabled = _activeVillainSprite != null;
                    if (_activeVillainSprite == null) { view.Mark.text = "◆"; view.Mark.color = VillainAccent(VillainGauntlet.Resolve(_session.Level.displayNumber)); }
                }
            }
            int visibleMonsterCount = _session.Monsters.Count(monster => monster);
            _progress.text = $"{visibleMonsterCount}/{n}"; _hearts.text = $"{_session.Hearts}";
        }

        private void OnSessionChanged() { RefreshBoard(); RefreshBoosterBar(); PersistSession(); }
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
            if (_session == null) return;
            var completedSession = _session;
            long token = ++_completionToken;
            _adBreakActive = true;
            _screenWantsBanner = false;
            UpdateBannerDesiredState();
            _save.RecordCompletion(completedSession.Level, completedSession.ElapsedSeconds, completedSession.Mistakes);
            _audio.Play("victory"); _haptics.Success(); StartCoroutine(WinSequence(completedSession, token));
        }

        private IEnumerator WinSequence(PuzzleSession completedSession, long token)
        {
            if (!_save.Data.settings.reducedMotion) for (int i = 0; i < _cells.Count; i++) if (completedSession.Monsters[i]) { StartCoroutine(Pop(_cells[i].transform)); yield return new WaitForSecondsRealtime(.08f); }
            yield return new WaitForSecondsRealtime(.35f);
            if (_session == completedSession) ShowWin(completedSession, token);
        }

        private IEnumerator Pop(Transform t) { Vector3 s = t.localScale; t.localScale = s * .8f; float d = 0; while (d < .2f) { d += Time.unscaledDeltaTime; t.localScale = Vector3.Lerp(s * .8f, s * 1.08f, d / .2f); yield return null; } t.localScale = s; }

        private void ShowWin(PuzzleSession completedSession, long token)
        {
            int number = completedSession.Level.displayNumber;
            string title = number == 250 ? "CAMPAIGN COMPLETE" : number % 25 == 0 ? "MASTERED!" : "PUZZLE CLEARED";
            var overlay = BeginOverlay("ResultOverlay", new Color(.05f, .03f, .12f, .78f));
            var card = Panel(overlay, "ResultCard", _theme.panel); Anchor(card, .5f, .5f, 720, 700);
            var icon = Text(card, "Icon", "★", 84, _theme.success, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(icon.rectTransform, .5f, .84f, 150, 110);
            var head = Text(card, "Title", title, 43, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(head.rectTransform, .5f, .70f, 640, 100);
            var copy = Text(card, "Body", $"Level {number}\n{FormatTime(completedSession.ElapsedSeconds)}  ·  {completedSession.Mistakes} mistakes", 27, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(copy.rectTransform, .5f, .53f, 620, 120);
            var breakCopy = Text(card, "BreakStatus", "RESULTS SAVED", 20, _theme.success, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(breakCopy.rectTransform, .5f, .40f, 600, 50);
            Canvas.ForceUpdateCanvases();
            StartCoroutine(RequestPostLevelInterstitial(completedSession, token, overlay, card, breakCopy));
            _audio.Play("panel");
        }

        private IEnumerator RequestPostLevelInterstitial(PuzzleSession completedSession, long token, RectTransform overlay, RectTransform card, TMP_Text status)
        {
            yield return null;
            if (_session != completedSession || overlay == null) yield break;
            int number = completedSession.Level.displayNumber;
            _ads.ShowPostLevelInterstitialIfAllowed(number, token, result =>
            {
                if (_session != completedSession || overlay == null || card == null) return;
                _adBreakActive = false;
                status.text = string.Empty;
                RevealResultNavigation(overlay, card, number);
                UpdateBannerDesiredState();
            });
        }

        private void RevealResultNavigation(RectTransform overlay, RectTransform card, int number)
        {
            if (card.Find("Primary") != null) return;
            string primaryLabel = number == 250 ? "CHAPTERS" : "NEXT LEVEL";
            var primary = Button(card, "Primary", primaryLabel, () =>
            {
                CloseOverlay(overlay.gameObject);
                if (number == 250) ShowLevelSelect(); else StartLevel(number + 1);
            });
            Anchor(primary, .5f, .29f, 580, 88);
            var replay = Button(card, "Replay", "REPLAY", () => { CloseOverlay(overlay.gameObject); _save.ClearInProgress(); StartLevel(number); }, false); Anchor(replay, .5f, .16f, 580, 70);
            if (number < 250)
            {
                var chapters = Button(card, "Chapters", "CHAPTERS", () => { CloseOverlay(overlay.gameObject); ShowLevelSelect(); }, false); Anchor(chapters, .5f, .055f, 580, 62);
            }
        }

        private void UseHintBoosterOrAd()
        {
            var session = _session;
            if (session == null || session.IsComplete || session.Hearts <= 0 || _adBreakActive) return;
            if (session.HintBoosters <= 0) { RequestRewardedHint(); return; }
            if (session.TryConsumeHintBooster()) ApplyHintReward(session);
        }

        private void UseVillainBoosterOrAd()
        {
            var session = _session;
            if (session == null || session.IsComplete || session.Hearts <= 0 || _adBreakActive) return;
            if (GetRevealCandidates(session).Length == 0)
            {
                ShowToast("Every villain position is already revealed.");
                return;
            }
            if (session.VillainBoosters <= 0) { RequestRewardedVillainReveal(); return; }
            if (session.TryConsumeVillainBooster()) ApplyVillainRevealReward(session);
        }

        private void RequestRewardedHint()
        {
            var session = _session;
            if (!TryBeginRewardedRequest(session)) return;
            string levelId = session.Level.levelId;
            PersistSession();
            _ads.ShowRewarded(RewardPlacement.Hint, result =>
            {
                FinishRewardedRequest();
                if (!IsSameLiveSession(session, levelId)) return;
                if (result == RewardedAdResult.Earned) ApplyHintReward(session);
                else ShowRewardFailure(result, "hint");
            });
        }

        private void ApplyHintReward(PuzzleSession session)
        {
            if (session != _session || session.IsComplete) return;
            string hint = session.GetHint(out int cell, out bool revealed); _hintText.text = hint; _audio.Play("hint");
            if (cell >= 0 && cell < _cells.Count) StartCoroutine(Highlight(_cells[cell].Background));
            if (revealed) _haptics.Light(); else StartCoroutine(EmphasizeHintCrosses(cell));
        }

        private void RequestRewardedVillainReveal()
        {
            var session = _session;
            if (session == null || session.IsComplete || session.Hearts <= 0 || _adBreakActive) return;
            if (GetRevealCandidates(session).Length == 0)
            {
                ShowToast("Every villain position is already revealed.");
                return;
            }
            if (!TryBeginRewardedRequest(session)) return;
            string levelId = session.Level.levelId;
            PersistSession();
            _ads.ShowRewarded(RewardPlacement.RevealVillain, result =>
            {
                FinishRewardedRequest();
                if (!IsSameLiveSession(session, levelId)) return;
                if (result == RewardedAdResult.Earned) ApplyVillainRevealReward(session);
                else ShowRewardFailure(result, "villain");
            });
        }

        private void ApplyVillainRevealReward(PuzzleSession session)
        {
            if (session != _session || session.IsComplete) return;
            int[] candidates = GetRevealCandidates(session);
            if (candidates.Length == 0) { ShowToast("Every villain position is already revealed."); return; }
            int cell = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            session.ToggleMonster(cell);
            if (_hintText != null) _hintText.text = "A villain was placed directly in a valid cell.";
            if (cell >= 0 && cell < _cells.Count) StartCoroutine(Highlight(_cells[cell].Background));
            _haptics.Light();
        }

        private static int[] GetRevealCandidates(PuzzleSession session) => Enumerable.Range(0, session.Monsters.Length)
            .Where(cell => !session.Monsters[cell] && session.Level.IsSolutionCell(cell)).ToArray();

        private bool TryBeginRewardedRequest(PuzzleSession session, bool allowZeroHearts = false)
        {
            if (session == null || session.IsComplete || !allowZeroHearts && session.Hearts <= 0 || _adActionInProgress || _adBreakActive) return false;
            _adActionInProgress = true;
            _adBreakActive = true;
            UpdateBannerDesiredState();
            return true;
        }

        private void FinishRewardedRequest()
        {
            _adActionInProgress = false;
            _adBreakActive = _ads != null && _ads.IsFullscreenAdShowing;
            UpdateBannerDesiredState();
        }

        private bool IsSameLiveSession(PuzzleSession session, string levelId) => session != null && session == _session && !session.IsComplete && session.Level.levelId == levelId;

        private void ShowRewardFailure(RewardedAdResult result, string rewardName)
        {
            ShowToast(result == RewardedAdResult.DismissedWithoutReward
                ? $"Finish the ad to receive the {rewardName}."
                : "Ad unavailable—try again shortly.");
        }

        private void ShowOutOfHearts()
        {
            if (_session == null || _session.IsComplete || _session.Hearts > 0 || GameObject.Find("OutOfHeartsOverlay") != null) return;
            var session = _session;
            string levelId = session.Level.levelId;
            int levelNumber = session.Level.displayNumber;
            var overlay = BeginOverlay("OutOfHeartsOverlay", new Color(.05f, .03f, .12f, .80f));
            var card = Panel(overlay, "OutOfHeartsCard", _theme.panel); Anchor(card, .5f, .5f, 720, 700);
            var icon = Text(card, "Icon", "♥", 82, _theme.danger, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(icon.rectTransform, .5f, .84f, 150, 110);
            var head = Text(card, "Title", "THE MOON WENT QUIET", 40, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(head.rectTransform, .5f, .70f, 640, 86);
            var copy = Text(card, "Body", "No hearts left. Your board is safe. Watch an optional ad for one life, retry, or return home.", 24, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(copy.rectTransform, .5f, .55f, 620, 130);
            var status = Text(card, "Status", string.Empty, 20, _theme.danger, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(status.rectTransform, .5f, .43f, 620, 52);

            var watch = Button(card, "RewardedLife", "WATCH AD · +1 LIFE", () =>
            {
                if (!IsSameSession(session, levelId) || !TryBeginRewardedRequest(session, true)) return;
                status.text = string.Empty;
                PersistSession();
                _ads.ShowRewarded(RewardPlacement.ExtraHeart, result =>
                {
                    FinishRewardedRequest();
                    if (!IsSameSession(session, levelId)) return;
                    if (result == RewardedAdResult.Earned && session.GrantHeart())
                    {
                        CloseOverlay(overlay.gameObject);
                        RefreshBoard();
                        PersistSession();
                    }
                    else
                    {
                        status.text = result == RewardedAdResult.DismissedWithoutReward
                            ? "Finish the ad to receive a life."
                            : "Ad unavailable—retry or try again shortly.";
                    }
                });
            });
            Anchor(watch, .5f, .32f, 580, 92);
            var retry = Button(card, "Retry", "RETRY", () => { CloseOverlay(overlay.gameObject); _save.ClearInProgress(); StartLevel(levelNumber); }, false); Anchor(retry, .5f, .17f, 580, 76);
            var home = Button(card, "Home", "HOME", () => { CloseOverlay(overlay.gameObject); ShowHome(); }, false); Anchor(home, .5f, .055f, 580, 66);
            _audio.Play("panel");
        }

        private bool IsSameSession(PuzzleSession session, string levelId) => session != null && session == _session && session.Level.levelId == levelId;
        private IEnumerator Highlight(Image image)
        {
            if (image == null) yield break;
            Color start = image.color;
            image.color = Color.Lerp(start, _theme.accent, .45f);
            yield return new WaitForSecondsRealtime(.65f);
            if (image != null) image.color = start;
        }

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
            if (_adBreakActive || GameObject.Find("SettingsOverlay") != null) return;
            var overlay = BeginOverlay("SettingsOverlay", new Color(.05f, .035f, .09f, .76f));
            overlay.GetComponent<Image>().raycastTarget = true;
            var sheet = Panel(overlay, "SettingsSheet", new Color(.985f, .955f, .90f, 1f)); Anchor(sheet, .5f, .50f, 760, 820);
            var header = Image(sheet, "Header", new Color(.94f, .86f, .78f)); header.sprite = RuntimeArt.RoundedSprite(28); header.type = UnityEngine.UI.Image.Type.Sliced; Anchor(header.rectTransform, .5f, .94f, 760, 132);
            var title = DisplayText(sheet, "Title", "SETTINGS", 52, new Color(.35f, .18f, .25f), TextAlignmentOptions.Center); title.outlineWidth = 0; Anchor(title.rectTransform, .5f, .94f, 490, 78);
            var close = IconButton(sheet, "Close", RuntimeIcon.Close, () => CloseOverlay(overlay.gameObject)); Anchor(close, .90f, .94f, 70, 70);

            var feedback = Button(sheet, "Feedback", "SEND FEEDBACK", () => Application.OpenURL(FeedbackUrl)); Anchor(feedback, .5f, .68f, 620, 96);
            var terms = Button(sheet, "TermsOfService", "TERMS OF SERVICE", () => Application.OpenURL(TermsUrl), false); Anchor(terms, .5f, .49f, 620, 76);
            terms.GetComponent<Image>().color = Color.white; terms.Find("Label").GetComponent<TMP_Text>().color = new Color(.42f, .27f, .28f);
            var privacy = Button(sheet, "PrivacyPolicy", "PRIVACY POLICY", () => Application.OpenURL(PrivacyUrl), false); Anchor(privacy, .5f, .35f, 620, 76);
            privacy.GetComponent<Image>().color = Color.white; privacy.Find("Label").GetComponent<TMP_Text>().color = new Color(.42f, .27f, .28f);
            var privacyStatus = Text(sheet, "PrivacyStatus", string.Empty, 18, new Color(.66f, .25f, .20f), FontStyles.Bold, TextAlignmentOptions.Center); Anchor(privacyStatus.rectTransform, .5f, .09f, 620, 46);
            var choices = Button(sheet, "PrivacyChoices", "PRIVACY CHOICES", () =>
            {
                _ads.ShowPrivacyOptions(opened => privacyStatus.text = opened ? string.Empty : "Privacy choices are unavailable while ads are disabled.");
            }, false);
            choices.GetComponent<Image>().color = Color.white; choices.Find("Label").GetComponent<TMP_Text>().color = new Color(.42f, .27f, .28f); Anchor(choices, .5f, .21f, 620, 76);
        }

        private void ShowModal(string title, string body, string primary, Action primaryAction, string secondary, Action secondaryAction)
        {
            var overlay = BeginOverlay("ModalOverlay", new Color(0.05f, .03f, .12f, .72f));
            var card = Panel(overlay, "ModalCard", _theme.panel); Anchor(card, .5f, .5f, 720, 560);
            var icon = Text(card, "Icon", "*", 84, _theme.accent, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(icon.rectTransform, .5f, .78f, 150, 120);
            var head = Text(card, "Title", title, 43, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Anchor(head.rectTransform, .5f, .62f, 640, 100);
            var copy = Text(card, "Body", body, 26, _theme.muted, FontStyles.Normal, TextAlignmentOptions.Center); Anchor(copy.rectTransform, .5f, .45f, 620, 120);
            var p = Button(card, "Primary", primary, () => { CloseOverlay(overlay.gameObject); primaryAction?.Invoke(); }); Anchor(p, .5f, .24f, 580, 92);
            var s = Button(card, "Secondary", secondary, () => { CloseOverlay(overlay.gameObject); secondaryAction?.Invoke(); }, false); Anchor(s, .5f, .08f, 580, 76);
            _audio.Play("panel");
        }

        private void BeginScreen(string name)
        {
            if (_screen != null) Destroy(_screen); _screen = new GameObject(name, typeof(RectTransform)); _screen.transform.SetParent(_contentRoot, false); Stretch((RectTransform)_screen.transform);
            _screenWantsBanner = name == "Home" || name == "LevelSelect" || name == "Chapter" || name == "Game";
            UpdateBannerDesiredState();
        }

        private RectTransform BeginOverlay(string name, Color color)
        {
            var overlay = Panel(_contentRoot, name, color); Stretch(overlay); overlay.SetAsLastSibling();
            overlay.GetComponent<Image>().raycastTarget = true;
            _overlayDepth++;
            UpdateBannerDesiredState();
            return overlay;
        }

        private void CloseOverlay(GameObject overlay)
        {
            if (overlay == null) return;
            Destroy(overlay);
            _overlayDepth = Mathf.Max(0, _overlayDepth - 1);
            UpdateBannerDesiredState();
        }

        private void OnFullscreenAdStateChanged(bool active)
        {
            _adBreakActive = active || _adActionInProgress;
            UpdateBannerDesiredState();
        }

        private void OnBannerHeightChanged(float pixels)
        {
            if (_bannerLayout != null) _bannerLayout.SetBannerHeightPixels(pixels);
        }

        private void UpdateBannerDesiredState()
        {
            if (_ads == null || _adPolicy == null || _save == null) return;
            bool desired = _screenWantsBanner && _overlayDepth == 0 && !_adBreakActive && _adPolicy.IsBannerEligible(_save.Data);
            _ads.SetBannerDesired(desired);
            if (!desired && _bannerLayout != null) _bannerLayout.SetBannerHeightPixels(0f);
        }

        private void ShowToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || _contentRoot == null) return;
            if (_toast != null) Destroy(_toast);
            _toast = new GameObject("AdStatusToast", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _toast.transform.SetParent(_contentRoot, false);
            var image = _toast.GetComponent<Image>(); image.color = Color.Lerp(_theme.panel, _theme.background, .25f); image.sprite = RuntimeArt.RoundedSprite(24); image.type = UnityEngine.UI.Image.Type.Sliced; Elevate(image, 5f);
            var text = Text(_toast.transform, "Message", message, 20, _theme.ink, FontStyles.Bold, TextAlignmentOptions.Center); Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(18f, 8f); text.rectTransform.offsetMax = new Vector2(-18f, -8f);
            Anchor((RectTransform)_toast.transform, .5f, .22f, 700, 78);
            _toast.transform.SetAsLastSibling();
            StartCoroutine(FadeToast(_toast));
        }

        private IEnumerator FadeToast(GameObject toast)
        {
            yield return new WaitForSecondsRealtime(1.8f);
            if (toast == null) yield break;
            var group = toast.GetComponent<CanvasGroup>();
            float elapsed = 0f;
            while (elapsed < .25f && toast != null)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / .25f);
                yield return null;
            }
            if (toast != null) Destroy(toast);
            if (_toast == toast) _toast = null;
        }

        private void AddBackdrop(Transform parent, int chapter)
        {
            Image(parent, "Background", _theme.background).transform.SetAsFirstSibling(); Stretch((RectTransform)parent.Find("Background"));
            var moon = Text(parent, "Moon", "O", 180, new Color(_theme.accent.r, _theme.accent.g, _theme.accent.b, .08f), FontStyles.Normal, TextAlignmentOptions.Center); Anchor(moon.rectTransform, .82f, .84f, 250, 250); moon.raycastTarget = false;
            for (int i = 0; i < 9; i++) { var star = Text(parent, "Star", "*", 20 + i % 3 * 8, new Color(_theme.accent.r, _theme.accent.g, _theme.accent.b, .10f), FontStyles.Normal, TextAlignmentOptions.Center); Anchor(star.rectTransform, .08f + (i * .113f) % .84f, .12f + (i * .197f) % .78f, 40, 40); star.raycastTarget = false; }
        }

        private void Rule(Transform parent, string name, Sprite sprite, string value)
        {
            var panel = SpritePanel(parent, name, sprite, Color.white);
            var layoutElement = panel.gameObject.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            var text = Text(panel, "Text", value, 21, new Color32(48, 37, 89, 255), FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(text.rectTransform, .67f, .5f, 166, 68);
            text.raycastTarget = false;
        }

        private void AddGameBackdrop(Transform parent)
        {
            var background = Image(parent, "Background", GameScreenLavender);
            Stretch(background.rectTransform);
            background.transform.SetAsFirstSibling();
            background.raycastTarget = false;

            var ground = Image(parent, "BottomEnvironment", new Color32(79, 73, 128, 204));
            ground.sprite = RuntimeArt.RoundedSprite(88);
            ground.type = UnityEngine.UI.Image.Type.Sliced;
            ground.raycastTarget = false;
            Anchor(ground.rectTransform, .5f, .035f, 1080, 260);

            var haze = Image(parent, "TopHaze", new Color(1f, 1f, 1f, .20f));
            haze.sprite = RuntimeArt.RoundedSprite(80);
            haze.type = UnityEngine.UI.Image.Type.Sliced;
            haze.raycastTarget = false;
            Anchor(haze.rectTransform, .5f, .81f, 1080, 300);

            for (int i = 0; i < 12; i++)
            {
                float x = .06f + (i * .137f) % .87f;
                float y = .17f + (i * .213f) % .72f;
                if (i % 3 == 0 && _gameSprites != null && _gameSprites.YellowStar != null)
                {
                    var star = SpritePanel(parent, $"Star_{i}", _gameSprites.YellowStar, new Color(1f, .87f, 1f, .68f));
                    Anchor(star, x, y, 28, 28);
                }
                else
                {
                    var dot = Image(parent, $"StarDot_{i}", new Color(1f, 1f, 1f, .58f));
                    dot.sprite = RuntimeArt.DiscSprite();
                    dot.raycastTarget = false;
                    Anchor(dot.rectTransform, x, y, 6, 6);
                }
            }
        }

        private void BuildStats(PuzzleLevelData level)
        {
            var stats = SpritePanel(_screen.transform, "Stats", _gameSprites != null ? _gameSprites.StatsPanel : null, Color.white);
            Anchor(stats, .5f, .842f, 1032, 174);
            if (stats.GetComponent<Image>().sprite != null) Elevate(stats.GetComponent<Image>(), 6f);

            AddStatSeparator(stats, .405f);
            AddStatSeparator(stats, .635f);
            var ink = new Color32(48, 37, 89, 255);

            var monster = SpriteImage(stats, "ProgressMonster", _gameSprites != null ? _gameSprites.MonsterIcon : null);
            Anchor(monster.rectTransform, .105f, .60f, 92, 82);
            _progress = Text(stats, "Progress", $"0/{level.gridSize}", 38, ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(_progress.rectTransform, .245f, .61f, 156, 54);
            var monsterLabel = Text(stats, "MonsterLabel", "MONSTERS", 19, ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(monsterLabel.rectTransform, .245f, .34f, 176, 34);

            var hourglass = SpriteImage(stats, "Hourglass", _gameSprites != null ? _gameSprites.HourglassIcon : null);
            Anchor(hourglass.rectTransform, .52f, .64f, 48, 67);
            _timer = Text(stats, "Timer", "0:00", 32, ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(_timer.rectTransform, .52f, .32f, 128, 44);

            var heart = SpriteImage(stats, "Heart", _gameSprites != null ? _gameSprites.HeartIcon : null);
            Anchor(heart.rectTransform, .735f, .60f, 92, 78);
            var heartsLabel = Text(stats, "HeartsLabel", "HEARTS", 22, new Color32(218, 48, 111, 255), FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(heartsLabel.rectTransform, .855f, .60f, 120, 44);
            _hearts = Text(stats, "Hearts", "3", 30, new Color32(218, 48, 111, 255), FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(_hearts.rectTransform, .945f, .60f, 50, 44);
        }

        private void AddStatSeparator(Transform parent, float x)
        {
            var separator = Image(parent, "StatSeparator", new Color(.58f, .42f, .78f, .46f));
            separator.sprite = RuntimeArt.RoundedSprite(3);
            separator.type = UnityEngine.UI.Image.Type.Sliced;
            separator.raycastTarget = false;
            Anchor(separator.rectTransform, x, .5f, 3, 104);
        }

        private void BuildInstructionPanel(PuzzleLevelData level)
        {
            var hintPanel = SpritePanel(_screen.transform, "HintPanel", _gameSprites != null ? _gameSprites.InstructionStrip : null, Color.white);
            Anchor(hintPanel, .5f, .145f, 780, 120);
            _hintText = Text(hintPanel, "HintText", TutorialCopy(level.displayNumber), 21, new Color32(48, 37, 89, 255), FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(_hintText.rectTransform);
            _hintText.rectTransform.offsetMin = new Vector2(70, 6);
            _hintText.rectTransform.offsetMax = new Vector2(-70, -6);
            _hintText.raycastTarget = false;
        }

        private RectTransform SpritePanel(Transform parent, string name, Sprite sprite, Color tint)
        {
            var image = Image(parent, name, sprite == null ? Color.clear : tint);
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private void StyleGameSpriteButton(RectTransform button, Sprite sprite)
        {
            var image = button.GetComponent<Image>();
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
            image.color = sprite == null ? Color.clear : Color.white;
            var label = button.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null) label.enabled = false;
            var icon = button.Find("Icon")?.GetComponent<Image>();
            if (icon != null) icon.enabled = false;
        }

private RectTransform Container(Transform parent, string name) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return (RectTransform)go.transform; }
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
        private RectTransform RawButton(Transform parent, string name, Texture texture, Action action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage), typeof(Button)); go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>(); image.texture = texture; image.color = texture == null ? Color.clear : Color.white;
            var button = go.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => { if (_adBreakActive) return; _audio?.Play("tap"); action?.Invoke(); });
            return (RectTransform)go.transform;
        }
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
            var button = go.GetComponent<Button>(); button.targetGraphic = image; var colors = button.colors; colors.normalColor = Color.white; colors.pressedColor = new Color(.84f,.84f,.9f); colors.highlightedColor = new Color(.96f,.96f,1f); button.colors = colors; button.onClick.AddListener(() => { if (_adBreakActive) return; _audio?.Play("tap"); action?.Invoke(); });
            var text = Text(go.transform, "Label", label, icon ? (label.Length > 1 ? 18 : 44) : 27, icon ? _theme.ink : Color.white, FontStyles.Bold, TextAlignmentOptions.Center); Stretch(text.rectTransform); text.raycastTarget = false; return (RectTransform)go.transform;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Anchor(RectTransform rect, float x, float y, float width, float height) { rect.anchorMin = rect.anchorMax = new Vector2(x, y); rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = Vector2.zero; }
        private static string FormatTime(float seconds) => $"{Mathf.FloorToInt(seconds / 60)}:{Mathf.FloorToInt(seconds % 60):00}";
        private static int ParseNumber(string id) => int.TryParse(id?.Split('-').LastOrDefault(), out int n) ? n : 0;
        #if UNITY_EDITOR
        public static bool EditorBatchActive { get; set; }
        public static bool EditorSuppressUnlockPopup { get; set; }
        public bool EditorIsInitialized => _initialized;
        public bool EditorBootSequenceComplete => _initialized && _screen != null && _screen.name != "ZemoLabsSplash";
        public int EditorCurrentLevelNumber => _session?.Level.displayNumber ?? 0;
        public int EditorLevelCount => _database?.levels?.Count ?? 0;
        public GameObject EditorUnlockPopup => GameObject.Find("VillainUnlockOverlay");
        public event Action<int> EditorLevelAndBoardLoaded;
        public string EditorCaptureSaveSnapshot() => _save?.EditorCaptureSnapshot() ?? string.Empty;
        public void EditorRestoreSaveSnapshot(string snapshot) => _save?.EditorRestoreSnapshot(snapshot);
        public void EditorHideUnlockPopup()
        {
            var popup = EditorUnlockPopup;
            if (popup != null) popup.SetActive(false);
        }
        public void EditorRestoreUnlockPopup(bool active)
        {
            var popup = EditorUnlockPopup;
            if (popup != null) popup.SetActive(active);
        }
        public bool EditorIsLevelReady(int level, out string reason)
        {
            if (_session == null || _session.Level.displayNumber != level) { reason = "Requested session is not active."; return false; }
            if (_screen == null || _screen.name != "Game") { reason = "Gameplay screen is not active."; return false; }
            int expectedCells = _session.Level.gridSize * _session.Level.gridSize;
            if (_cells.Count != expectedCells || _screen.transform.Find("Board") == null) { reason = "Board has not finished building."; return false; }
            reason = string.Empty;
            return true;
        }
#endif

        
private static string TutorialCopy(int level) => level switch { 1 => "Tap once for X. Double-tap or hold to place a monster.", 2 => "The two bottom icons are optional monster and hint boosters.", 3 => "Every coloured region needs exactly one monster.", 4 => "Every row and column needs exactly one monster.", 5 => "Regions can bend—follow each colour shape.", 6 => "Monsters cannot touch, even at the corners.", <= 10 => "Combine all four rules. Hints explain before they reveal.", _ => "Tap for X  ·  Double-tap or hold to place a monster" };
    }

    internal enum RuntimeIcon { Gear, Sound, Music, Haptic, Hint, Close, Hourglass, Region, Grid, Ghost }

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
                case RuntimeIcon.Hourglass:
                    Rect(26, 74, 70, 82); Rect(26, 14, 70, 22);
                    for (int y = 48; y <= 72; y++) { int hw = Mathf.RoundToInt((y - 48) / 24f * 20f); Rect(48 - hw, y, 48 + hw, y); }
                    for (int y = 24; y <= 48; y++) { int hw = Mathf.RoundToInt((48 - y) / 24f * 20f); Rect(48 - hw, y, 48 + hw, y); }
                    break;
                case RuntimeIcon.Region:
                    Circle(48, 48, 15); Circle(29, 48, 12); Circle(67, 48, 12); Circle(48, 29, 12); Circle(48, 67, 12); break;
                case RuntimeIcon.Grid:
                    Rect(23, 51, 45, 73); Rect(51, 51, 73, 73); Rect(23, 23, 45, 45); Rect(51, 23, 73, 45); break;
                case RuntimeIcon.Ghost:
                    Circle(48, 52, 23); Rect(25, 22, 71, 54); Circle(33, 22, 9); Circle(48, 22, 9); Circle(63, 22, 9); break;
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
            tex.SetPixels32(pixels); tex.Apply(); sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r)); Cache[radius] = sprite; return sprite;
        }
    }
}
