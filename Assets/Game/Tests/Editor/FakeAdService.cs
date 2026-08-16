using System;
using MonsterLogic.Services;

namespace MonsterLogic.Tests.Editor
{
    internal sealed class FakeAdService : IAdService
    {
        public event Action RewardedAvailabilityChanged;
        public event Action<bool> FullscreenAdStateChanged;
        public event Action<float> BannerHeightChanged;
        public event Action FullscreenAdWillPresent;
        public event Action<AdRevenueEvent> RevenuePaid;

        public bool IsInitialized { get; set; } = true;
        public bool IsRewardedReady { get; set; } = true;
        public bool IsFullscreenAdShowing { get; private set; }
        public bool CanShowPrivacyOptions { get; set; } = true;
        public RewardedAdResult NextRewardedResult { get; set; } = RewardedAdResult.Earned;
        public InterstitialAdResult NextInterstitialResult { get; set; } = InterstitialAdResult.NotReady;
        public RewardPlacement? LastRewardPlacement { get; private set; }
        public int RewardedRequestCount { get; private set; }
        public int InterstitialRequestCount { get; private set; }
        public long LastCompletionToken { get; private set; }
        public bool BannerDesired { get; private set; }

        public void Initialize() => IsInitialized = true;

        public void ShowRewarded(RewardPlacement placement, Action<RewardedAdResult> completed)
        {
            RewardedRequestCount++;
            LastRewardPlacement = placement;
            if (!IsInitialized || !IsRewardedReady) { completed?.Invoke(RewardedAdResult.NotReady); return; }
            FullscreenAdWillPresent?.Invoke();
            IsFullscreenAdShowing = true;
            FullscreenAdStateChanged?.Invoke(true);
            IsFullscreenAdShowing = false;
            FullscreenAdStateChanged?.Invoke(false);
            completed?.Invoke(NextRewardedResult);
        }

        public void ShowPostLevelInterstitialIfAllowed(int completedLevel, long completionToken, Action<InterstitialAdResult> completed)
        {
            InterstitialRequestCount++;
            LastCompletionToken = completionToken;
            completed?.Invoke(NextInterstitialResult);
        }

        public void SetBannerDesired(bool desired) => BannerDesired = desired;
        public void ShowPrivacyOptions(Action<bool> completed) => completed?.Invoke(CanShowPrivacyOptions);
        public void Shutdown() { }

        public void PublishRewardedAvailability() => RewardedAvailabilityChanged?.Invoke();
        public void PublishBannerHeight(float pixels) => BannerHeightChanged?.Invoke(pixels);
        public void PublishRevenue(AdRevenueEvent revenueEvent) => RevenuePaid?.Invoke(revenueEvent);
    }
}
