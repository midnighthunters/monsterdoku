using System.Collections.Generic;
using System.Linq;
using MonsterLogic.Services;

namespace MonsterLogic.Ads
{
    public sealed class AdPolicy
    {
        public const int DefaultBannerUnlockCompletedLevel = 3;
        public const int DefaultInterstitialStartCompletedLevel = 10;
        public const int DefaultInterstitialEveryNLevelCompletions = 1;

        private readonly HashSet<long> _consumedCompletionTokens = new HashSet<long>();

        public int BannerUnlockCompletedLevel { get; }
        public int InterstitialStartCompletedLevel { get; }
        public int InterstitialEveryNLevelCompletions { get; }

        public AdPolicy(int bannerUnlockCompletedLevel = DefaultBannerUnlockCompletedLevel,
            int interstitialStartCompletedLevel = DefaultInterstitialStartCompletedLevel,
            int interstitialEveryNLevelCompletions = DefaultInterstitialEveryNLevelCompletions)
        {
            BannerUnlockCompletedLevel = System.Math.Max(1, bannerUnlockCompletedLevel);
            InterstitialStartCompletedLevel = System.Math.Max(1, interstitialStartCompletedLevel);
            InterstitialEveryNLevelCompletions = System.Math.Max(1, interstitialEveryNLevelCompletions);
        }

        public static AdPolicy FromConfig(AdsConfig config) => config == null
            ? new AdPolicy()
            : new AdPolicy(config.bannerUnlockCompletedLevel, config.interstitialStartCompletedLevel, config.interstitialEveryNLevelCompletions);

        public bool IsBannerEligible(SaveData save)
        {
            if (save == null) return false;
            string unlockId = $"campaign-{BannerUnlockCompletedLevel:000}";
            return (save.completed?.Any(result => result != null && result.levelId == unlockId) ?? false) ||
                   save.highestUnlocked >= BannerUnlockCompletedLevel + 1;
        }

        public bool IsInterstitialEligible(int completedLevel)
        {
            if (completedLevel < InterstitialStartCompletedLevel) return false;
            return (completedLevel - InterstitialStartCompletedLevel) % InterstitialEveryNLevelCompletions == 0;
        }

        public bool TryConsumeInterstitial(long completionToken, int completedLevel)
        {
            if (completionToken <= 0 || !IsInterstitialEligible(completedLevel)) return false;
            return _consumedCompletionTokens.Add(completionToken);
        }
    }
}
