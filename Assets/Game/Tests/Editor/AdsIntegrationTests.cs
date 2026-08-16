using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MonsterLogic.Ads;
using MonsterLogic.Puzzle;
using MonsterLogic.Services;
using MonsterLogic.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace MonsterLogic.Tests.Editor
{
    public sealed class AdsIntegrationTests
    {
        [Test]
        public void BannerUnlockRequiresLevelThreeCompletionOrMigratedProgress()
        {
            var policy = new AdPolicy();
            var save = new SaveData { highestUnlocked = 1 };
            Assert.That(policy.IsBannerEligible(save), Is.False);
            save.highestUnlocked = 3;
            Assert.That(policy.IsBannerEligible(save), Is.False, "Reaching level 3 is not the same as completing it.");
            save.completed.Add(new LevelResult { levelId = "campaign-003" });
            Assert.That(policy.IsBannerEligible(save), Is.True);

            var migrated = new SaveData { highestUnlocked = 4 };
            Assert.That(policy.IsBannerEligible(migrated), Is.True);
        }

        [Test]
        public void InterstitialStartsAtTenAndConsumesEachCompletionTokenOnce()
        {
            var policy = new AdPolicy();
            for (int level = 1; level <= 9; level++) Assert.That(policy.IsInterstitialEligible(level), Is.False, "Level " + level);
            for (int level = 10; level <= 20; level++) Assert.That(policy.IsInterstitialEligible(level), Is.True, "Level " + level);
            Assert.That(policy.TryConsumeInterstitial(101, 10), Is.True);
            Assert.That(policy.TryConsumeInterstitial(101, 10), Is.False);
            Assert.That(policy.TryConsumeInterstitial(102, 11), Is.True);
        }

        [Test]
        public void GrantHeartPersistsClampsRaisesChangedAndCannotBeUndone()
        {
            var level = Resources.Load<PuzzleLevelDatabase>("PuzzleLevelDatabase").GetByNumber(2);
            var session = new PuzzleSession(level);
            int correct = level.Cell(0, level.solutionColumnByRow[0]);
            int incorrect = Enumerable.Range(0, session.Monsters.Length).First(cell => !level.IsSolutionCell(cell));
            session.ToggleMonster(correct);
            session.ToggleMonster(incorrect);
            session.ToggleMonster(incorrect);
            session.ToggleMonster(incorrect);
            Assert.That(session.Hearts, Is.Zero);

            int changed = 0;
            session.Changed += () => changed++;
            Assert.That(session.GrantHeart(), Is.True);
            Assert.That(session.Hearts, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(session.Undo(), Is.True);
            Assert.That(session.Hearts, Is.EqualTo(1), "Board undo must not remove or duplicate an ad-earned life.");
            Assert.That(session.GrantHeart(99), Is.True);
            Assert.That(session.Hearts, Is.EqualTo(3));
            Assert.That(session.GrantHeart(), Is.False);

            WithTemporarySave(save =>
            {
                save.StoreSession(session);
                Assert.That(save.Data.inProgressHearts, Is.EqualTo(3));
            });
        }

        [Test]
        public void BoosterCountsStartAtOnePersistAtZeroAndCannotBeRestoredByUndo()
        {
            var level = Resources.Load<PuzzleLevelDatabase>("PuzzleLevelDatabase").GetByNumber(2);
            var session = new PuzzleSession(level);
            int correct = level.Cell(0, level.solutionColumnByRow[0]);
            session.ToggleMonster(correct);

            Assert.That(session.VillainBoosters, Is.EqualTo(1));
            Assert.That(session.HintBoosters, Is.EqualTo(1));
            Assert.That(session.TryConsumeVillainBooster(), Is.True);
            Assert.That(session.TryConsumeHintBooster(), Is.True);
            Assert.That(session.Undo(), Is.True);
            Assert.That(session.VillainBoosters, Is.Zero);
            Assert.That(session.HintBoosters, Is.Zero);

            WithTemporarySave(save =>
            {
                save.StoreSession(session);
                Assert.That(save.Data.inProgressVillainBoosters, Is.Zero);
                Assert.That(save.Data.inProgressHintBoosters, Is.Zero);
                Assert.That(save.HasSessionFor(level), Is.True, "Spent boosters must keep the puzzle resumable even before a board mark is saved.");
            });
        }

        [Test]
        public void RewardStateMachineCompletesExactlyOnceForAllCallbackOrders()
        {
            var earned = new RewardedAdStateMachine();
            int earnedCalls = 0;
            RewardedAdResult earnedResult = RewardedAdResult.NotReady;
            Assert.That(earned.TryBegin(result => { earnedCalls++; earnedResult = result; }), Is.True);
            earned.MarkRewardEarned();
            earned.MarkRewardEarned();
            earned.CompleteHidden();
            earned.CompleteHidden();
            earned.CompleteDisplayFailed();
            Assert.That(earnedCalls, Is.EqualTo(1));
            Assert.That(earnedResult, Is.EqualTo(RewardedAdResult.Earned));

            var dismissed = new RewardedAdStateMachine();
            RewardedAdResult dismissedResult = RewardedAdResult.Earned;
            dismissed.TryBegin(result => dismissedResult = result);
            dismissed.CompleteHidden();
            Assert.That(dismissedResult, Is.EqualTo(RewardedAdResult.DismissedWithoutReward));

            var failed = new RewardedAdStateMachine();
            int failureCalls = 0;
            RewardedAdResult failureResult = RewardedAdResult.Earned;
            failed.TryBegin(result => { failureCalls++; failureResult = result; });
            failed.CompleteDisplayFailed();
            failed.MarkRewardEarned();
            failed.CompleteHidden();
            Assert.That(failureCalls, Is.EqualTo(1));
            Assert.That(failureResult, Is.EqualTo(RewardedAdResult.DisplayFailed));
        }

        [Test]
        public void HintRequestsCorrectPlacementAndOnlyEarnedResultAdvancesIt()
        {
            WithAppHarness((app, session, fake) =>
            {
                SetHintStage(session, 1);
                fake.NextRewardedResult = RewardedAdResult.DismissedWithoutReward;
                InvokePrivate(app, "RequestRewardedHint");
                Assert.That(fake.LastRewardPlacement, Is.EqualTo(RewardPlacement.Hint));
                Assert.That(session.Monsters.Count(value => value), Is.Zero);
                Assert.That(GetHintStage(session), Is.EqualTo(1));

                fake.NextRewardedResult = RewardedAdResult.Earned;
                InvokePrivate(app, "RequestRewardedHint");
                Assert.That(session.Monsters.Count(value => value), Is.EqualTo(1));
            });
        }

        [Test]
        public void VillainRevealRequestsCorrectPlacementAndOnlyEarnedResultPlacesOneValidVillain()
        {
            WithAppHarness((app, session, fake) =>
            {
                fake.NextRewardedResult = RewardedAdResult.DismissedWithoutReward;
                InvokePrivate(app, "RequestRewardedVillainReveal");
                Assert.That(fake.LastRewardPlacement, Is.EqualTo(RewardPlacement.RevealVillain));
                Assert.That(session.Monsters.Count(value => value), Is.Zero);

                fake.NextRewardedResult = RewardedAdResult.Earned;
                InvokePrivate(app, "RequestRewardedVillainReveal");
                int[] placed = session.Monsters.Select((value, cell) => (value, cell)).Where(item => item.value).Select(item => item.cell).ToArray();
                Assert.That(placed, Has.Length.EqualTo(1));
                Assert.That(session.Level.IsSolutionCell(placed[0]), Is.True);
            });
        }

        [Test]
        public void ZeroCountBoosterButtonsOpenTheirMatchingRewardedAds()
        {
            WithAppHarness((app, session, fake) =>
            {
                Assert.That(session.TryConsumeHintBooster(), Is.True);
                fake.NextRewardedResult = RewardedAdResult.DismissedWithoutReward;
                InvokePrivate(app, "UseHintBoosterOrAd");
                Assert.That(fake.LastRewardPlacement, Is.EqualTo(RewardPlacement.Hint));

                Assert.That(session.TryConsumeVillainBooster(), Is.True);
                InvokePrivate(app, "UseVillainBoosterOrAd");
                Assert.That(fake.LastRewardPlacement, Is.EqualTo(RewardPlacement.RevealVillain));
            });
        }

        private static void WithAppHarness(Action<MonsterLogicApp, PuzzleSession, FakeAdService> test)
        {
            string id = Guid.NewGuid().ToString("N");
            string savePath = Path.Combine(Application.temporaryCachePath, "monster-logic-ads-test-" + id + ".json");
            string marker = "MonsterLogic.Tests.AdsMarker." + id;
            var host = new GameObject("AdsIntegrationTestHost");
            var hintObject = new GameObject("HintText", typeof(RectTransform), typeof(TextMeshProUGUI));
            try
            {
                var database = Resources.Load<PuzzleLevelDatabase>("PuzzleLevelDatabase");
                var session = new PuzzleSession(database.GetByNumber(2));
                var save = new SaveService(savePath, marker);
                var fake = new FakeAdService();
                var app = host.AddComponent<MonsterLogicApp>();
                SetField(app, "_session", session);
                SetField(app, "_save", save);
                SetField(app, "_ads", fake);
                SetField(app, "_adPolicy", new AdPolicy());
                SetField(app, "_hintText", hintObject.GetComponent<TextMeshProUGUI>());
                SetField(app, "_audio", new AudioService(save.Data.settings, host));
                SetField(app, "_haptics", new HapticService(save.Data.settings));
                test(app, session, fake);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hintObject);
                UnityEngine.Object.DestroyImmediate(host);
                PlayerPrefs.DeleteKey(marker);
                PlayerPrefs.Save();
                DeleteIfPresent(savePath);
                DeleteIfPresent(savePath + ".bak");
                DeleteIfPresent(savePath + ".tmp");
            }
        }

        private static void WithTemporarySave(Action<SaveService> test)
        {
            string id = Guid.NewGuid().ToString("N");
            string path = Path.Combine(Application.temporaryCachePath, "monster-logic-heart-test-" + id + ".json");
            string marker = "MonsterLogic.Tests.HeartMarker." + id;
            try { test(new SaveService(path, marker)); }
            finally
            {
                PlayerPrefs.DeleteKey(marker);
                PlayerPrefs.Save();
                DeleteIfPresent(path);
                DeleteIfPresent(path + ".bak");
                DeleteIfPresent(path + ".tmp");
            }
        }

        private static void SetField(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
        private static void InvokePrivate(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
        private static void SetHintStage(PuzzleSession session, int value) => typeof(PuzzleSession).GetField("_hintStage", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(session, value);
        private static int GetHintStage(PuzzleSession session) => (int)typeof(PuzzleSession).GetField("_hintStage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        private static void DeleteIfPresent(string path) { if (File.Exists(path)) File.Delete(path); }
    }
}
