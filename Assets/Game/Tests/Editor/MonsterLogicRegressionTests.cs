using System;
using System.IO;
using System.Linq;
using MonsterLogic.Progression;
using MonsterLogic.Puzzle;
using MonsterLogic.Services;
using NUnit.Framework;
using UnityEngine;

namespace MonsterLogic.Tests.Editor
{
    public sealed class MonsterLogicRegressionTests
    {
        [Test]
        public void EveryCorrectVillainPlacementRegistersAndCompletesTheLevel()
        {
            var database = Resources.Load<PuzzleLevelDatabase>("PuzzleLevelDatabase");
            Assert.That(database, Is.Not.Null);
            var level = database.GetByNumber(2);
            var session = new PuzzleSession(level);
            Assert.That(session.Monsters, Has.All.False, "A new level must not pre-place villains.");

            for (int row = 0; row < level.gridSize; row++)
            {
                int cell = level.Cell(row, level.solutionColumnByRow[row]);
                session.ToggleMonster(cell);
            }

            Assert.That(session.Monsters.Count(placed => placed), Is.EqualTo(level.gridSize));
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.Hearts, Is.EqualTo(3));
        }

        [Test]
        public void EveryCampaignLevelIsUniqueWithoutPresetVillains()
        {
            var database = Resources.Load<PuzzleLevelDatabase>("PuzzleLevelDatabase");
            Assert.That(database, Is.Not.Null);

            foreach (var level in database.levels)
                Assert.That(PuzzleSolver.CountSolutions(level, 2), Is.EqualTo(1), level.levelId);
        }

        [Test]
        public void SuppliedLogoLockAndVillainSpritesAreLoadable()
        {
            Assert.That(Resources.Load<Texture2D>("logo"), Is.Not.Null);
            Assert.That(Resources.LoadAll<Sprite>("lock").Any(sprite => sprite.name == "lock_0"), Is.True);
            Assert.That(Resources.LoadAll<Sprite>("cross").Length, Is.EqualTo(16));

            for (int rosterIndex = 0; rosterIndex < VillainGauntlet.RosterCount; rosterIndex++)
            {
                var tier = VillainGauntlet.Resolve(rosterIndex * VillainGauntlet.LevelsPerTier + 1);
                Assert.That(Resources.LoadAll<Sprite>(tier.villain.resourcePath).Any(sprite => sprite.name == tier.villain.spriteName), Is.True, tier.villain.displayName);
            }
        }

        [Test]
        public void RemovingPlayerPrefsMarkerResetsJsonProgressImmediately()
        {
            string id = Guid.NewGuid().ToString("N");
            string savePath = Path.Combine(Application.temporaryCachePath, $"monster-logic-save-test-{id}.json");
            string markerKey = $"MonsterLogic.Tests.SaveMarker.{id}";
            try
            {
                PlayerPrefs.DeleteKey(markerKey);
                var save = new SaveService(savePath, markerKey);
                save.Data.highestUnlocked = 84;
                save.Data.completed.Add(new LevelResult { levelId = "campaign-083", bestTime = 42f, bestMistakes = 0 });
                save.Save();

                PlayerPrefs.DeleteKey(markerKey);
                PlayerPrefs.Save();

                Assert.That(save.Data.highestUnlocked, Is.EqualTo(1));
                Assert.That(save.Data.completed, Is.Empty);
                Assert.That(PlayerPrefs.HasKey(markerKey), Is.True);
                Assert.That(new SaveService(savePath, markerKey).Data.highestUnlocked, Is.EqualTo(1));
            }
            finally
            {
                PlayerPrefs.DeleteKey(markerKey);
                PlayerPrefs.Save();
                if (File.Exists(savePath)) File.Delete(savePath);
                if (File.Exists(savePath + ".bak")) File.Delete(savePath + ".bak");
                if (File.Exists(savePath + ".tmp")) File.Delete(savePath + ".tmp");
            }
        }
    }
}
