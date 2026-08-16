using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using MonsterLogic.Puzzle;

namespace MonsterLogic.Services
{
    [Serializable] public sealed class LevelResult { public string levelId; public float bestTime; public int bestMistakes = 99; }
    [Serializable] public sealed class SettingsData
    {
        public bool darkTheme; public bool music = true; public bool sfx = true; public bool haptics = true;
        public bool colourFriendly; public bool regionSymbols; public bool reducedMotion; public bool accessibilityCycle; public bool automaticNotesIdentical;
    }
    [Serializable] public sealed class SaveData
    {
        public int schemaVersion = 3; public int highestUnlocked = 1; public List<LevelResult> completed = new List<LevelResult>();
        public string currentLevelId = "campaign-001"; public bool tutorialComplete; public SettingsData settings = new SettingsData();
        public int[] inProgressMonsters = Array.Empty<int>(); public int[] inProgressPlayerNotes = Array.Empty<int>();
        public int inProgressHearts = 3; public int inProgressMistakes; public float inProgressSeconds;
        public List<string> acknowledgedVillainTiers = new List<string>();
    }

    public sealed class SaveService
    {
        private readonly string _path = Path.Combine(Application.persistentDataPath, "monster-logic-save.json");
        private string BackupPath => _path + ".bak";
        public SaveData Data { get; private set; }

        public SaveService() => Data = Load();

        public void RecordCompletion(PuzzleLevelData level, float seconds, int mistakes)
        {
            var result = Data.completed.FirstOrDefault(x => x.levelId == level.levelId);
            if (result == null) { result = new LevelResult { levelId = level.levelId }; Data.completed.Add(result); }
            if (result.bestTime <= 0 || seconds < result.bestTime) result.bestTime = seconds;
            result.bestMistakes = Math.Min(result.bestMistakes, mistakes);
            Data.highestUnlocked = Math.Max(Data.highestUnlocked, Math.Min(250, level.displayNumber + 1));
            Data.currentLevelId = $"campaign-{Data.highestUnlocked:000}"; ClearInProgress(false); Save();
        }

        public void StoreSession(PuzzleSession session)
        {
            if (session == null || session.IsComplete) return;
            Data.currentLevelId = session.Level.levelId;
            Data.inProgressMonsters = session.Monsters.Select((placed, cell) => (placed, cell)).Where(x => x.placed && !session.Level.IsLocked(x.cell)).Select(x => x.cell).ToArray();
            Data.inProgressPlayerNotes = session.PlayerNotes.Select((placed, cell) => (placed, cell)).Where(x => x.placed).Select(x => x.cell).ToArray();
            Data.inProgressHearts = session.Hearts; Data.inProgressMistakes = session.Mistakes; Data.inProgressSeconds = session.ElapsedSeconds; Save();
        }

        // A timer alone does not represent player progress. Treat it as a fresh board so
        // interrupted, untouched levels do not restore their authored clue visuals.
        public bool HasSessionFor(PuzzleLevelData level) => level != null && Data.currentLevelId == level.levelId &&
            ((Data.inProgressMonsters?.Length ?? 0) > 0 || (Data.inProgressPlayerNotes?.Length ?? 0) > 0 || Data.inProgressMistakes > 0);

        public void ClearInProgress(bool save = true)
        {
            Data.inProgressMonsters = Array.Empty<int>(); Data.inProgressPlayerNotes = Array.Empty<int>();
            Data.inProgressHearts = 3; Data.inProgressMistakes = 0; Data.inProgressSeconds = 0;
            if (save) Save();
        }

        public bool HasAcknowledgedVillainTier(string tierId) => !string.IsNullOrEmpty(tierId) && Data.acknowledgedVillainTiers.Contains(tierId);

        public void AcknowledgeVillainTier(string tierId)
        {
            if (string.IsNullOrEmpty(tierId) || HasAcknowledgedVillainTier(tierId)) return;
            Data.acknowledgedVillainTiers.Add(tierId); Save();
        }

        public void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(Data, true), temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(_path)) File.Copy(_path, BackupPath, true);
                File.Copy(temp, _path, true); File.Delete(temp);
            }
            catch (Exception ex) { Debug.LogWarning($"Save failed safely: {ex.Message}"); }
        }

        private SaveData Load()
        {
            SaveData Try(string path)
            {
                try { return File.Exists(path) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(path)) : null; }
                catch { return null; }
            }
            var data = Try(_path) ?? Try(BackupPath) ?? new SaveData();
            data.settings ??= new SettingsData(); data.completed ??= new List<LevelResult>(); data.highestUnlocked = Mathf.Clamp(data.highestUnlocked, 1, 250);
            data.inProgressMonsters ??= Array.Empty<int>(); data.inProgressPlayerNotes ??= Array.Empty<int>(); data.acknowledgedVillainTiers ??= new List<string>(); data.schemaVersion = 3;
            return data;
        }
    }

    public interface IAdService
    {
        bool IsRewardedReady { get; }
        void ShowRewardedHint(Action<bool> completed);
        void ShowRewardedRevive(Action<bool> completed);
        void ShowPostLevelInterstitialIfAllowed(int completedLevel);
    }

    public sealed class NoOpAdService : IAdService
    {
        public bool IsRewardedReady => false;
        public void ShowRewardedHint(Action<bool> completed) => completed?.Invoke(false);
        public void ShowRewardedRevive(Action<bool> completed) => completed?.Invoke(false);
        public void ShowPostLevelInterstitialIfAllowed(int completedLevel) { }
    }

    public sealed class HapticService
    {
        private readonly SettingsData _settings;
        public HapticService(SettingsData settings) => _settings = settings;
        public void Light() { }
        public void Warning() { if (_settings.haptics && Application.isMobilePlatform) Handheld.Vibrate(); }
        public void Success() { if (_settings.haptics && Application.isMobilePlatform) Handheld.Vibrate(); }
    }

    public sealed class AudioService
    {
        private readonly SettingsData _settings; private readonly AudioSource _source, _ambience; private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        public AudioService(SettingsData settings, GameObject host)
        {
            _settings = settings; _source = host.AddComponent<AudioSource>(); _source.playOnAwake = false;
            _ambience = host.AddComponent<AudioSource>(); _ambience.playOnAwake = false; _ambience.loop = true; _ambience.volume = .55f;
            _clips["tap"] = CreateTone("tap", 640, .08f, .08f); _clips["x"] = CreateTone("x", 410, .11f, .07f);
            _clips["monster"] = CreateTone("monster", 520, .20f, .10f); _clips["mistake"] = CreateTone("mistake", 180, .25f, .10f);
            _clips["hint"] = CreateTone("hint", 780, .22f, .07f); _clips["panel"] = CreateTone("panel", 330, .12f, .06f);
            _clips["victory"] = CreateChord("victory", new[] { 523f, 659f, 784f }, .75f, .08f);
            _clips["ambience-light"] = CreateAmbient("ambience-light", new[] { 196f, 246.94f, 293.66f });
            _clips["ambience-dark"] = CreateAmbient("ambience-dark", new[] { 130.81f, 196f, 233.08f });
        }
        public void Play(string key) { if (_settings.sfx && _clips.TryGetValue(key, out var clip)) _source.PlayOneShot(clip); }
        public void SetAmbience(bool dark)
        {
            if (!_settings.music) { _ambience.Stop(); return; }
            var clip = _clips[dark ? "ambience-dark" : "ambience-light"];
            if (_ambience.clip == clip && _ambience.isPlaying) return; _ambience.clip = clip; _ambience.Play();
        }
        private static AudioClip CreateTone(string name, float hz, float seconds, float volume)
        {
            int rate = 22050, count = Mathf.CeilToInt(rate * seconds); var data = new float[count];
            for (int i = 0; i < count; i++) { float t = i / (float)rate, envelope = Mathf.Sin(Mathf.PI * i / count); data[i] = Mathf.Sin(t * hz * Mathf.PI * 2) * envelope * volume; }
            var clip = AudioClip.Create(name, count, 1, rate, false); clip.SetData(data, 0); return clip;
        }
        private static AudioClip CreateChord(string name, float[] hz, float seconds, float volume)
        {
            int rate = 22050, count = Mathf.CeilToInt(rate * seconds); var data = new float[count];
            for (int i = 0; i < count; i++) { float t = i / (float)rate, envelope = Mathf.Sin(Mathf.PI * i / count); foreach (float f in hz) data[i] += Mathf.Sin(t * f * Mathf.PI * 2) / hz.Length * envelope * volume; }
            var clip = AudioClip.Create(name, count, 1, rate, false); clip.SetData(data, 0); return clip;
        }
        private static AudioClip CreateAmbient(string name, float[] hz)
        {
            int rate = 22050, count = rate * 8; var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate, phase = Mathf.Sin(Mathf.PI * 2f * i / count) * .12f;
                for (int j = 0; j < hz.Length; j++) data[i] += Mathf.Sin((t * hz[j] + phase * (j + 1)) * Mathf.PI * 2f) * .008f / hz.Length;
            }
            var clip = AudioClip.Create(name, count, 1, rate, false); clip.SetData(data, 0); return clip;
        }
    }

    public readonly struct ThemePalette
    {
        public readonly Color background, panel, panelAlt, ink, muted, accent, danger, success;
        public readonly Color[] regions;
        public ThemePalette(Color background, Color panel, Color panelAlt, Color ink, Color muted, Color accent, Color danger, Color success, Color[] regions)
        { this.background = background; this.panel = panel; this.panelAlt = panelAlt; this.ink = ink; this.muted = muted; this.accent = accent; this.danger = danger; this.success = success; this.regions = regions; }
    }

    public static class ThemeService
    {
        public static ThemePalette Get(bool dark, bool colourFriendly)
        {
            if (dark) return new ThemePalette(C("11152D"), C("202044"), C("2B2853"), C("F1ECFF"), C("AAA3C8"), C("9C7CFF"), C("FF6F91"), C("79D8B1"),
                colourFriendly ? new[] { C("344E78"), C("6C407B"), C("246B68"), C("795630"), C("4E633B"), C("703C4B"), C("3D5770"), C("635079") } : new[] { C("353B67"), C("5C3A69"), C("315F61"), C("714A3A"), C("4D5D3B"), C("653C53"), C("364F70"), C("5D4A75") });
            return new ThemePalette(C("F7F3FF"), C("FFFCFF"), C("EEE9F8"), C("35206B"), C("756E91"), C("7650C7"), C("D95874"), C("4B9B79"),
                colourFriendly ? new[] { C("DCE8FA"), C("E8D7F5"), C("CFE8E1"), C("F6D8C8"), C("DCE8C8"), C("F2D5DE"), C("D4E5EE"), C("E2D8F2") } : new[] { C("DDE6FA"), C("E9D9F5"), C("D5E8E5"), C("F7D9D0"), C("E2EBD3"), C("F2D8E3"), C("D8E8F0"), C("E5DCF3") });
        }
        private static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }
    }
}
