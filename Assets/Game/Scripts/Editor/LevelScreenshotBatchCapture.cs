#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MonsterLogic.UI;
using UnityEditor;
using UnityEngine;

namespace MonsterLogic.EditorTools
{
    /// <summary>
    /// Editor-only, resumable level screenshot automation. It never writes progression
    /// data and stores captures outside Assets to avoid AssetDatabase/import file locks.
    /// </summary>
    public static class LevelScreenshotBatchCapture
    {
        private const int DefaultStartLevel = 1;
        private const int DefaultEndLevel = 250;
        private const float DefaultSettleDelaySeconds = 3f;
        private const float LoadTimeoutSeconds = 30f;
        private const float CaptureTimeoutSeconds = 10f;
        private const int MaxCaptureRetries = 3;
        private const string RootFolderName = "Screenshots";
        private const string StateFileName = ".level-screenshot-state.json";
        private const string PendingOutputKey = "MonsterLogic.ScreenshotBatch.PendingOutput";
        private const string PendingStartKey = "MonsterLogic.ScreenshotBatch.PendingStart";
        private const string PendingEndKey = "MonsterLogic.ScreenshotBatch.PendingEnd";
        private const string PendingSettleKey = "MonsterLogic.ScreenshotBatch.PendingSettle";
        private static Runner _runner;
        private static bool _pendingPollInstalled;

        [Serializable]
        private sealed class BatchState
        {
            public int startLevel;
            public int endLevel;
            public float settleDelaySeconds;
            public string outputFolder;
            public int nextLevel;
            public int lastAttemptedLevel;
            public bool completed;
            public string status;
        }

        [MenuItem("Monster Logic/Screenshot Batch/Start New (Levels 1-250)")]
        private static void StartNewMenu() => StartNew(DefaultStartLevel, DefaultEndLevel, DefaultSettleDelaySeconds);

        [MenuItem("Monster Logic/Screenshot Batch/Resume Last Batch")]
        private static void ResumeMenu()
        {
            var folder = FindLatestIncompleteBatch();
            if (string.IsNullOrEmpty(folder))
            {
                Debug.LogWarning("Monster Logic screenshot batch: no incomplete batch was found under the Screenshots folder.");
                return;
            }
            var state = ReadState(folder);
            if (state == null) { Debug.LogError("Monster Logic screenshot batch: the latest batch state could not be read."); return; }
            StartOrQueue(state.outputFolder, state.startLevel, state.endLevel, state.settleDelaySeconds, true);
        }

        [MenuItem("Monster Logic/Screenshot Batch/Stop and Restore Popup")]
        private static void StopMenu()
        {
            if (_runner == null) { Debug.Log("Monster Logic screenshot batch: no active runner."); return; }
            _runner.RequestStop("Stopped by user.");
        }

        [InitializeOnLoadMethod]
        private static void InstallPlayModeHooks()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (EditorApplication.isPlaying) PollForPendingRunner();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) PollForPendingRunner();
        }

        private static void StartNew(int startLevel, int endLevel, float settleDelay)
        {
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, RootFolderName);
            Directory.CreateDirectory(root);
            string output = Path.Combine(root, "Batch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            int suffix = 1;
            while (Directory.Exists(output)) output = Path.Combine(root, "Batch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + suffix++);
            Directory.CreateDirectory(output);
            WriteState(new BatchState { startLevel = startLevel, endLevel = endLevel, settleDelaySeconds = settleDelay, outputFolder = output, nextLevel = startLevel, status = "Created" }, output);
            StartOrQueue(output, startLevel, endLevel, settleDelay, false);
        }

        private static void StartOrQueue(string output, int startLevel, int endLevel, float settleDelay, bool resume)
        {
            if (EditorApplication.isPlaying)
            {
                BeginRunner(output, startLevel, endLevel, settleDelay, resume);
                return;
            }
            SessionState.SetString(PendingOutputKey, output);
            SessionState.SetInt(PendingStartKey, startLevel);
            SessionState.SetInt(PendingEndKey, endLevel);
            SessionState.SetFloat(PendingSettleKey, settleDelay);
            EditorApplication.EnterPlaymode();
        }

        private static void PollForPendingRunner()
        {
            if (_pendingPollInstalled || !EditorApplication.isPlaying) return;
            _pendingPollInstalled = true;
            EditorApplication.update += PollPendingUpdate;
        }

        private static void PollPendingUpdate()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= PollPendingUpdate;
                _pendingPollInstalled = false;
                return;
            }
            string output = SessionState.GetString(PendingOutputKey, string.Empty);
            if (string.IsNullOrEmpty(output))
            {
                EditorApplication.update -= PollPendingUpdate;
                _pendingPollInstalled = false;
                return;
            }
            var app = UnityEngine.Object.FindObjectOfType<MonsterLogicApp>();
            if (app == null) return;
            EditorApplication.update -= PollPendingUpdate;
            _pendingPollInstalled = false;
            int start = SessionState.GetInt(PendingStartKey, DefaultStartLevel);
            int end = SessionState.GetInt(PendingEndKey, DefaultEndLevel);
            float settle = SessionState.GetFloat(PendingSettleKey, DefaultSettleDelaySeconds);
            bool resume = File.Exists(Path.Combine(output, StateFileName));
            SessionState.EraseString(PendingOutputKey);
            SessionState.EraseInt(PendingStartKey);
            SessionState.EraseInt(PendingEndKey);
            SessionState.EraseFloat(PendingSettleKey);
            BeginRunner(output, start, end, settle, resume);
        }

        private static void BeginRunner(string output, int startLevel, int endLevel, float settleDelay, bool resume)
        {
            if (_runner != null) { Debug.LogWarning("Monster Logic screenshot batch: a batch is already running."); return; }
            var go = new GameObject("MonsterLogicLevelScreenshotBatch");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            _runner.Begin(output, startLevel, endLevel, settleDelay, resume);
        }

        private static string FindLatestIncompleteBatch()
        {
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, RootFolderName);
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root)
                .Where(folder => File.Exists(Path.Combine(folder, StateFileName)))
                .Select(folder => new { folder, state = ReadState(folder), modified = Directory.GetLastWriteTimeUtc(folder) })
                .Where(item => item.state != null && !item.state.completed)
                .OrderByDescending(item => item.modified)
                .Select(item => item.folder)
                .FirstOrDefault();
        }

        private static BatchState ReadState(string folder)
        {
            try
            {
                string path = Path.Combine(folder, StateFileName);
                return File.Exists(path) ? JsonUtility.FromJson<BatchState>(File.ReadAllText(path)) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Monster Logic screenshot batch: state read failed: " + ex.Message);
                return null;
            }
        }

        private static void WriteState(BatchState state, string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, StateFileName), JsonUtility.ToJson(state, true));
            }
            catch (Exception ex) { Debug.LogWarning("Monster Logic screenshot batch: state write failed: " + ex.Message); }
        }

        private sealed class Runner : MonoBehaviour
        {
            private string _outputFolder;
            private int _startLevel;
            private int _endLevel;
            private float _settleDelay;
            private bool _resume;
            private bool _stopRequested;
            private bool _cleanupComplete;
            private MonsterLogicApp _app;
            private bool _previousPopupSuppression;
            private bool _popupWasPresent;
            private bool _popupWasActive;
            private int _waitingForLevel;
            private int _callbackLevel = -1;
            private bool _officialCallbackReceived;
            private float _callbackTime;
            private int _retryCount;
            private string _saveSnapshot;
            private bool _saveRestored;
            private readonly List<int> _failedLevels = new List<int>();
            private readonly List<string> _errors = new List<string>();
            private BatchState _state;

            public void Begin(string output, int startLevel, int endLevel, float settleDelay, bool resume)
            {
                _outputFolder = output;
                _startLevel = startLevel;
                _endLevel = endLevel;
                _settleDelay = Mathf.Max(0f, settleDelay);
                _resume = resume;
                StartCoroutine(Run());
            }

            public void RequestStop(string reason)
            {
                if (_cleanupComplete) return;
                _stopRequested = true;
                _errors.Add(reason);
                Debug.LogWarning("Monster Logic screenshot batch: " + reason);
                StopAllCoroutines();
                Finish(false, reason);
            }

            private IEnumerator Run()
            {
                bool completed = false;
                string finalStatus = "Failed";
                try
                {
                    while ((_app = FindObjectOfType<MonsterLogicApp>()) == null || !_app.EditorIsInitialized || !_app.EditorBootSequenceComplete)
                    {
                        if (_stopRequested) yield break;
                        yield return null;
                    }

                    _saveSnapshot = _app.EditorCaptureSaveSnapshot();
                    MonsterLogicApp.EditorBatchActive = true;
                    _previousPopupSuppression = MonsterLogicApp.EditorSuppressUnlockPopup;
                    MonsterLogicApp.EditorSuppressUnlockPopup = true;
                    var popup = _app.EditorUnlockPopup;
                    _popupWasPresent = popup != null;
                    _popupWasActive = popup != null && popup.activeSelf;
                    _app.EditorHideUnlockPopup();
                    _app.EditorLevelAndBoardLoaded += OnLevelAndBoardLoaded;

                    int available = _app.EditorLevelCount;
                    if (_startLevel < 1 || _endLevel < _startLevel || _endLevel > available)
                    {
                        Fail($"Requested range {_startLevel}-{_endLevel} is outside the authoritative database count ({available}).");
                        yield break;
                    }
                    int firstMissing = FindFirstMissingLevel();
                    if (firstMissing > _endLevel)
                    {
                        completed = true;
                        finalStatus = "Already complete";
                        yield break;
                    }
                    _state = ReadState(_outputFolder) ?? new BatchState();
                    _state.startLevel = _startLevel; _state.endLevel = _endLevel; _state.settleDelaySeconds = _settleDelay;
                    _state.outputFolder = _outputFolder; _state.nextLevel = firstMissing; _state.status = _resume ? "Resuming" : "Running";
                    WriteState(_state, _outputFolder);

                    for (int level = firstMissing; level <= _endLevel; level++)
                    {
                        if (_stopRequested) { Fail("Stopped before the next level."); yield break; }
                        _state.lastAttemptedLevel = level; _state.nextLevel = level; WriteState(_state, _outputFolder);
                        bool captured = false;
                        yield return CaptureLevel(level, result => captured = result);
                        if (!captured) { if (!_failedLevels.Contains(level)) _failedLevels.Add(level); yield break; }
                    }
                    completed = true;
                    finalStatus = "Completed";
                }
                finally
                {
                    Finish(completed, finalStatus);
                }
            }

            private IEnumerator CaptureLevel(int level, Action<bool> result)
            {
                _waitingForLevel = level;
                _callbackLevel = -1;
                _officialCallbackReceived = false;
                _retryCount = 0;
                _app.EditorHideUnlockPopup();
                if (_app.EditorCurrentLevelNumber != level)
                    _app.StartLevel(level);

                float waitStarted = Time.realtimeSinceStartup;
                while (!_officialCallbackReceived && Time.realtimeSinceStartup - waitStarted < LoadTimeoutSeconds)
                {
                    if (_stopRequested) { result(false); yield break; }
                    _app.EditorHideUnlockPopup();
                    yield return null;
                }

                string readinessReason;
                bool ready = _app.EditorIsLevelReady(level, out readinessReason);
                bool fallback = false;
                if (!_officialCallbackReceived)
                {
                    if (!ready)
                    {
                        Fail($"Level {level}: official board-loaded callback timed out after {LoadTimeoutSeconds:0.#} seconds; fallback rejected: {readinessReason}");
                        result(false); yield break;
                    }
                    fallback = true;
                    Debug.LogWarning($"Monster Logic screenshot batch: level {level}, callback status=timeout-fallback; readiness checks passed.");
                }

                float settleStarted = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(_settleDelay);
                float settleElapsed = Time.realtimeSinceStartup - settleStarted;
                ready = _app.EditorIsLevelReady(level, out readinessReason);
                if (!ready)
                {
                    Fail($"Level {level}: not ready after settle delay: {readinessReason}");
                    result(false); yield break;
                }
                if (_app.EditorUnlockPopup != null && _app.EditorUnlockPopup.activeInHierarchy)
                {
                    Fail($"Level {level}: unlock popup is visible at capture time.");
                    result(false); yield break;
                }

                string path = Path.Combine(_outputFolder, $"Level{level:000}.png");
                bool success = false;
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    success = true;
                    Debug.Log($"Monster Logic screenshot batch: level {level}, callback status={(fallback ? "fallback" : "official")}, settle={settleElapsed:0.00}s, retries=0, output={path} (existing valid file)");
                }
                else
                {
                    if (File.Exists(path)) File.Delete(path);
                    for (_retryCount = 1; _retryCount <= MaxCaptureRetries && !success; _retryCount++)
                    {
                        if (_stopRequested) break;
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(path, 1);
                        float captureStarted = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - captureStarted < CaptureTimeoutSeconds)
                        {
                            if (File.Exists(path) && new FileInfo(path).Length > 0) { success = true; break; }
                            yield return null;
                        }
                        if (!success && File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
                        if (!success) yield return new WaitForSecondsRealtime(.25f);
                    }
                    int retriesUsed = success ? _retryCount - 1 : MaxCaptureRetries;
                    Debug.Log(success
                        ? $"Monster Logic screenshot batch: level {level}, callback status={(fallback ? "fallback" : "official")}, settle={settleElapsed:0.00}s, retries={retriesUsed}, output={path}"
                        : $"Monster Logic screenshot batch: level {level}, callback status={(fallback ? "fallback" : "official")}, settle={settleElapsed:0.00}s, retries={MaxCaptureRetries}, output={path}, ERROR=capture file was missing or zero bytes");
                }
                if (!success)
                {
                    Fail($"Level {level}: screenshot failed after {MaxCaptureRetries} attempts.");
                    result(false); yield break;
                }
                _state.nextLevel = level + 1; _state.status = "Running"; WriteState(_state, _outputFolder);
                result(true);
            }

            private int FindFirstMissingLevel()
            {
                for (int level = _startLevel; level <= _endLevel; level++)
                {
                    string path = Path.Combine(_outputFolder, $"Level{level:000}.png");
                    if (!File.Exists(path) || new FileInfo(path).Length == 0) return level;
                }
                return _endLevel + 1;
            }

            private void OnLevelAndBoardLoaded(int level)
            {
                if (level != _waitingForLevel) return;
                _officialCallbackReceived = true;
                _callbackLevel = level;
                _callbackTime = Time.realtimeSinceStartup;
                Debug.Log($"Monster Logic screenshot batch: level {level}, callback status=official, board/grid entrance complete.");
            }

            private void Fail(string message)
            {
                _errors.Add(message);
                Debug.LogError("Monster Logic screenshot batch: " + message);
            }

            private void Finish(bool completed, string status)
            {
                if (_cleanupComplete) return;
                _cleanupComplete = true;
                if (_app != null) _app.EditorLevelAndBoardLoaded -= OnLevelAndBoardLoaded;

                OutputValidation validation;
                try
                {
                    validation = ValidateOutput();
                }
                catch (Exception ex)
                {
                    _errors.Add("Final output validation failed: " + ex.Message);
                    validation = new OutputValidation();
                    validation.missing.AddRange(Enumerable.Range(_startLevel, _endLevel - _startLevel + 1));
                }
                if (_state == null) _state = ReadState(_outputFolder) ?? new BatchState();
                _state.startLevel = _startLevel; _state.endLevel = _endLevel; _state.settleDelaySeconds = _settleDelay;
                _state.outputFolder = _outputFolder; _state.completed = completed && validation.missing.Count == 0;
                _state.nextLevel = validation.missing.Count == 0 ? _endLevel + 1 : validation.missing.Min();
                _state.status = _state.completed ? "Completed" : status;
                WriteState(_state, _outputFolder);

                try { RestoreSaveState(); }
                catch (Exception ex) { _errors.Add("Save-state restoration failed: " + ex.Message); }
                try { RestorePopupState(); }
                catch (Exception ex) { _errors.Add("Unlock popup restoration failed: " + ex.Message); }
                string missing = validation.missing.Count == 0 ? "none" : string.Join(", ", validation.missing);
                string duplicates = validation.duplicates.Count == 0 ? "none" : string.Join(", ", validation.duplicates);
                string errors = _errors.Count == 0 ? "none" : string.Join(" | ", _errors);
                Debug.Log($"Monster Logic screenshot batch report: expected={_endLevel - _startLevel + 1}, successful={validation.successful}, missing/failed={missing}, duplicates={duplicates}, output={_outputFolder}, saveStateRestored={_saveRestored}, unlockPopupRestored={_popupRestored}, errors/warnings={errors}");
                LevelScreenshotBatchCapture._runner = null;
                Destroy(gameObject);
            }

            private bool _popupRestored;

            private void RestoreSaveState()
            {
                if (_app != null && !string.IsNullOrEmpty(_saveSnapshot))
                {
                    _app.EditorRestoreSaveSnapshot(_saveSnapshot);
                    _saveRestored = true;
                }
                MonsterLogicApp.EditorBatchActive = false;
            }

            private void RestorePopupState()
            {
                if (_app != null)
                {
                    if (_popupWasPresent) _app.EditorRestoreUnlockPopup(_popupWasActive);
                    MonsterLogicApp.EditorSuppressUnlockPopup = _previousPopupSuppression;
                }
                _popupRestored = _app != null;
            }

            private OutputValidation ValidateOutput()
            {
                var result = new OutputValidation();
                if (!Directory.Exists(_outputFolder))
                {
                    result.missing.AddRange(Enumerable.Range(_startLevel, _endLevel - _startLevel + 1));
                    return result;
                }
                var pngs = Directory.GetFiles(_outputFolder, "*.png", SearchOption.TopDirectoryOnly);
                var levelPattern = new Regex(@"^Level(\d{3})(?:.*)\.png$", RegexOptions.IgnoreCase);
                var grouped = pngs.Select(path => new { path, fileName = Path.GetFileName(path), match = levelPattern.Match(Path.GetFileName(path)) })
                    .Where(item => item.match.Success)
                    .GroupBy(item => int.Parse(item.match.Groups[1].Value));
                foreach (var group in grouped)
                {
                    if (group.Count() > 1) result.duplicates.Add(group.Key);
                    string expectedName = $"Level{group.Key:000}.png";
                    if (group.Key >= _startLevel && group.Key <= _endLevel)
                    {
                        var expected = group.FirstOrDefault(item => string.Equals(item.fileName, expectedName, StringComparison.OrdinalIgnoreCase));
                        if (expected != null && new FileInfo(expected.path).Length > 0) result.successful++;
                    }
                }
                for (int level = _startLevel; level <= _endLevel; level++)
                {
                    string expected = Path.Combine(_outputFolder, $"Level{level:000}.png");
                    if (!File.Exists(expected) || new FileInfo(expected).Length == 0) result.missing.Add(level);
                }
                return result;
            }

            private void OnDestroy()
            {
                if (!_cleanupComplete)
                {
                    RestoreSaveState();
                    RestorePopupState();
                }
                if (LevelScreenshotBatchCapture._runner == this) LevelScreenshotBatchCapture._runner = null;
            }
        }

        private sealed class OutputValidation
        {
            public readonly List<int> missing = new List<int>();
            public readonly List<int> duplicates = new List<int>();
            public int successful;
        }
    }
}
#endif
