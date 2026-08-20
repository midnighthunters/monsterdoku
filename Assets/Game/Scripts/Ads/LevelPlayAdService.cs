using System;
using System.Collections;
using System.Collections.Generic;
using MonsterLogic.Services;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace MonsterLogic.Ads
{
    [DisallowMultipleComponent]
    public sealed class LevelPlayAdService : MonoBehaviour, IAdService
    {
        private const string RewardExtraHeartPlacement = "reward_extra_heart";
        private const string RewardHintPlacement = "reward_hint";
        private const string RewardRevealVillainPlacement = "reward_reveal_villain";
        private const string InterstitialPlacement = "interstitial_post_level";
        private const string BannerPlacement = "banner_progression";

        private static LevelPlayAdService _active;
        private readonly RewardedAdStateMachine _rewardedState = new RewardedAdStateMachine();
        private readonly object _revenueLock = new object();
        private readonly Queue<AdRevenueEvent> _revenueQueue = new Queue<AdRevenueEvent>();

        private AdsConfig _config;
        private AdPolicy _policy;
        private string _appKey;
        private string _rewardedAdUnitId;
        private string _interstitialAdUnitId;
        private string _bannerAdUnitId;
        private LevelPlayRewardedAd _rewardedAd;
        private LevelPlayInterstitialAd _interstitialAd;
        private LevelPlayBannerAd _bannerAd;
        private Action<InterstitialAdResult> _interstitialCompleted;
        private bool _sdkCallbacksSubscribed;
        private bool _initializing;
        private bool _shuttingDown;
        private bool _bannerLoaded;
        private bool _bannerDesired;
        private bool _bannerVisible;
        private int _rewardedRetryAttempt;
        private int _interstitialRetryAttempt;
        private Coroutine _rewardedRetry;
        private Coroutine _interstitialRetry;
        private bool _previousAudioPause;
        private float _previousAudioVolume;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        public event Action RewardedAvailabilityChanged;
        public event Action<bool> FullscreenAdStateChanged;
        public event Action<float> BannerHeightChanged;
        public event Action FullscreenAdWillPresent;
        public event Action<AdRevenueEvent> RevenuePaid;

        public bool IsInitialized { get; private set; }
        public bool IsRewardedReady
        {
            get { return IsInitialized && !_rewardedState.HasPendingRequest && HasLoadedRewardedAd(); }
        }
        public bool IsFullscreenAdShowing { get; private set; }
        public bool CanShowPrivacyOptions => false;

        public void Configure(AdsConfig config, AdPolicy policy)
        {
            if (_initializing || IsInitialized) throw new InvalidOperationException("LevelPlay cannot be reconfigured after initialization starts.");
            _config = config;
            _policy = policy ?? AdPolicy.FromConfig(config);
        }

        public void Initialize()
        {
            if (_initializing || IsInitialized || _shuttingDown) return;
            if (_active != null && _active != this)
            {
                Debug.LogWarning("A second LevelPlayAdService was rejected to prevent duplicate callback subscriptions.");
                return;
            }

            string reason = "AdsConfig is missing.";
            if (_config == null || !_config.IsRuntimeReady(out reason))
            {
                Debug.Log("LevelPlay ads remain disabled: " + reason);
                return;
            }

            _appKey = _config.LevelPlayAppKey;
            _rewardedAdUnitId = _config.RewardedAdUnitId;
            _interstitialAdUnitId = _config.InterstitialAdUnitId;
            _bannerAdUnitId = _config.BannerAdUnitId;
            _active = this;
            _initializing = true;
            SubscribeSdkCallbacks();

            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LevelPlay.SetAdaptersDebug(_config.developmentTestMode);
#else
                LevelPlay.SetAdaptersDebug(false);
#endif
                LevelPlay.Init(_appKey);
                StartCoroutine(InitializationTimeout());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LevelPlay initialization failed safely: " + exception.Message);
                DisableForSession();
            }
        }

        public void ShowRewarded(RewardPlacement placement, Action<RewardedAdResult> completed)
        {
            if (_rewardedState.HasPendingRequest || !IsRewardedReady)
            {
                InvokeSafely(completed, RewardedAdResult.NotReady);
                return;
            }
            if (!_rewardedState.TryBegin(completed))
            {
                InvokeSafely(completed, RewardedAdResult.NotReady);
                return;
            }

            try
            {
                NotifyWillPresent();
                SetFullscreenState(true);
                _rewardedAd.ShowAd(PlacementName(placement));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LevelPlay rewarded display failed safely: " + exception.Message);
                SetFullscreenState(false);
                CompleteRewardedDisplayFailed();
                LoadRewarded();
            }
        }

        public void ShowPostLevelInterstitialIfAllowed(int completedLevel, long completionToken, Action<InterstitialAdResult> completed)
        {
            if (_policy == null || !_policy.IsInterstitialEligible(completedLevel) || !_policy.TryConsumeInterstitial(completionToken, completedLevel))
            {
                InvokeSafely(completed, InterstitialAdResult.Ineligible);
                return;
            }
            if (!IsInitialized || IsFullscreenAdShowing || _interstitialCompleted != null || !HasLoadedInterstitialAd())
            {
                InvokeSafely(completed, InterstitialAdResult.NotReady);
                return;
            }

            _interstitialCompleted = completed;
            try
            {
                NotifyWillPresent();
                SetFullscreenState(true);
                _interstitialAd.ShowAd(InterstitialPlacement);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LevelPlay interstitial display failed safely: " + exception.Message);
                SetFullscreenState(false);
                CompleteInterstitial(InterstitialAdResult.DisplayFailed);
                LoadInterstitial();
            }
        }

        public void SetBannerDesired(bool desired)
        {
            _bannerDesired = desired;
            ApplyBannerVisibility();
        }

        public void ShowPrivacyOptions(Action<bool> completed)
        {
            // LevelPlay exposes privacy-consent flags, but a configured CMP owns
            // any in-app privacy-choices presentation.
            InvokeSafely(completed, false);
        }

        public void ShowTestSuite(Action<bool> completed)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            InvokeSafely(completed, false);
#else
            if (!IsInitialized || _config == null || !_config.developmentTestMode)
            {
                InvokeSafely(completed, false);
                return;
            }

            try
            {
                LevelPlay.LaunchTestSuite();
                InvokeSafely(completed, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LevelPlay test suite could not open: " + exception.Message);
                InvokeSafely(completed, false);
            }
#endif
        }

        public void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            StopAllCoroutines();
            _rewardedRetry = null;
            _interstitialRetry = null;
            _interstitialCompleted = null;
            _rewardedState.CancelWithoutCallback();
            UnsubscribeSdkCallbacks();
            DestroyAdUnits();
            lock (_revenueLock) _revenueQueue.Clear();
            _bannerLoaded = false;
            _bannerVisible = false;
            PublishBannerHeight(0f);
            SetFullscreenState(false);
            IsInitialized = false;
            _initializing = false;
            if (_active == this) _active = null;
        }

        private void Update()
        {
            DrainRevenueQueue();
            if (!_bannerVisible || Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight) return;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            PublishCurrentBannerHeight();
        }

        private void OnDestroy() => Shutdown();

        private IEnumerator InitializationTimeout()
        {
            yield return new WaitForSecondsRealtime(30f);
            if (!_initializing || IsInitialized) yield break;
            Debug.LogWarning("LevelPlay initialization timed out; ads are disabled for this session.");
            DisableForSession();
        }

        private void SubscribeSdkCallbacks()
        {
            if (_sdkCallbacksSubscribed) return;
            _sdkCallbacksSubscribed = true;
            LevelPlay.OnInitSuccess += OnLevelPlayInitialized;
            LevelPlay.OnInitFailed += OnLevelPlayInitializationFailed;
        }

        private void UnsubscribeSdkCallbacks()
        {
            if (!_sdkCallbacksSubscribed) return;
            _sdkCallbacksSubscribed = false;
            LevelPlay.OnInitSuccess -= OnLevelPlayInitialized;
            LevelPlay.OnInitFailed -= OnLevelPlayInitializationFailed;
        }

        private void OnLevelPlayInitialized(LevelPlayConfiguration configuration)
        {
            if (_shuttingDown || !_initializing) return;
            _initializing = false;
            IsInitialized = true;
            CreateAdUnits();
            LoadRewarded();
            LoadInterstitial();
            LoadBanner();
        }

        private void OnLevelPlayInitializationFailed(LevelPlayInitError error)
        {
            if (_shuttingDown || !_initializing) return;
            Debug.LogWarning("LevelPlay initialization failed safely: " + error);
            DisableForSession();
        }

        private void DisableForSession()
        {
            _initializing = false;
            IsInitialized = false;
            UnsubscribeSdkCallbacks();
            DestroyAdUnits();
            if (_active == this) _active = null;
        }

        private void CreateAdUnits()
        {
            CreateRewardedAd();
            CreateInterstitialAd();
            CreateBannerAd();
        }

        private void CreateRewardedAd()
        {
            if (_rewardedAd != null) return;
            try
            {
                _rewardedAd = new LevelPlayRewardedAd(_rewardedAdUnitId);
                _rewardedAd.OnAdLoaded += OnRewardedLoaded;
                _rewardedAd.OnAdLoadFailed += OnRewardedLoadFailed;
                _rewardedAd.OnAdDisplayFailed += OnRewardedDisplayFailed;
                _rewardedAd.OnAdRewarded += OnRewardedRewarded;
                _rewardedAd.OnAdClosed += OnRewardedClosed;
                _rewardedAd.OnAdImpressionDataReady += OnRewardedImpressionDataReady;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LevelPlay rewarded ad creation failed safely: " + exception.Message);
                _rewardedAd = null;
            }
        }

        private void CreateInterstitialAd()
        {
            if (_interstitialAd != null) return;
            try
            {
                _interstitialAd = new LevelPlayInterstitialAd(_interstitialAdUnitId);
                _interstitialAd.OnAdLoaded += OnInterstitialLoaded;
                _interstitialAd.OnAdLoadFailed += OnInterstitialLoadFailed;
                _interstitialAd.OnAdDisplayFailed += OnInterstitialDisplayFailed;
                _interstitialAd.OnAdClosed += OnInterstitialClosed;
                _interstitialAd.OnAdImpressionDataReady += OnInterstitialImpressionDataReady;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LevelPlay interstitial ad creation failed safely: " + exception.Message);
                _interstitialAd = null;
            }
        }

        private void CreateBannerAd()
        {
            if (_bannerAd != null) return;
            try
            {
                var configuration = new LevelPlayBannerAd.Config.Builder()
                    .SetSize(LevelPlayAdSize.CreateAdaptiveAdSize())
                    .SetPosition(LevelPlayBannerPosition.BottomCenter)
                    .SetPlacementName(BannerPlacement)
                    .SetDisplayOnLoad(false)
                    .SetRespectSafeArea(true)
                    .Build();
                _bannerAd = new LevelPlayBannerAd(_bannerAdUnitId, configuration);
                _bannerAd.OnAdLoaded += OnBannerLoaded;
                _bannerAd.OnAdLoadFailed += OnBannerLoadFailed;
                _bannerAd.OnAdImpressionDataReady += OnBannerImpressionDataReady;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LevelPlay banner ad creation failed safely: " + exception.Message);
                _bannerAd = null;
            }
        }

        private void DestroyAdUnits()
        {
            if (_rewardedAd != null)
            {
                _rewardedAd.OnAdLoaded -= OnRewardedLoaded;
                _rewardedAd.OnAdLoadFailed -= OnRewardedLoadFailed;
                _rewardedAd.OnAdDisplayFailed -= OnRewardedDisplayFailed;
                _rewardedAd.OnAdRewarded -= OnRewardedRewarded;
                _rewardedAd.OnAdClosed -= OnRewardedClosed;
                _rewardedAd.OnAdImpressionDataReady -= OnRewardedImpressionDataReady;
                try { _rewardedAd.DestroyAd(); } catch { }
                _rewardedAd = null;
            }
            if (_interstitialAd != null)
            {
                _interstitialAd.OnAdLoaded -= OnInterstitialLoaded;
                _interstitialAd.OnAdLoadFailed -= OnInterstitialLoadFailed;
                _interstitialAd.OnAdDisplayFailed -= OnInterstitialDisplayFailed;
                _interstitialAd.OnAdClosed -= OnInterstitialClosed;
                _interstitialAd.OnAdImpressionDataReady -= OnInterstitialImpressionDataReady;
                try { _interstitialAd.DestroyAd(); } catch { }
                _interstitialAd = null;
            }
            if (_bannerAd != null)
            {
                _bannerAd.OnAdLoaded -= OnBannerLoaded;
                _bannerAd.OnAdLoadFailed -= OnBannerLoadFailed;
                _bannerAd.OnAdImpressionDataReady -= OnBannerImpressionDataReady;
                try { _bannerAd.DestroyAd(); } catch { }
                _bannerAd = null;
            }
        }

        private void LoadRewarded()
        {
            if (!IsInitialized || _shuttingDown || _rewardedAd == null) return;
            try { _rewardedAd.LoadAd(); }
            catch (Exception exception) { Debug.LogWarning("LevelPlay rewarded load failed safely: " + exception.Message); }
        }

        private void LoadInterstitial()
        {
            if (!IsInitialized || _shuttingDown || _interstitialAd == null) return;
            try { _interstitialAd.LoadAd(); }
            catch (Exception exception) { Debug.LogWarning("LevelPlay interstitial load failed safely: " + exception.Message); }
        }

        private void LoadBanner()
        {
            if (!IsInitialized || _shuttingDown || _bannerAd == null) return;
            try { _bannerAd.LoadAd(); }
            catch (Exception exception) { Debug.LogWarning("LevelPlay banner load failed safely: " + exception.Message); }
        }

        private bool HasLoadedRewardedAd()
        {
            try { return _rewardedAd != null && _rewardedAd.IsAdReady(); }
            catch { return false; }
        }

        private bool HasLoadedInterstitialAd()
        {
            try { return _interstitialAd != null && _interstitialAd.IsAdReady(); }
            catch { return false; }
        }

        private void OnRewardedLoaded(LevelPlayAdInfo adInfo)
        {
            _rewardedRetryAttempt = 0;
            if (_rewardedRetry != null) { StopCoroutine(_rewardedRetry); _rewardedRetry = null; }
            InvokeSafely(RewardedAvailabilityChanged);
        }

        private void OnRewardedLoadFailed(LevelPlayAdError error)
        {
            if (_shuttingDown) return;
            InvokeSafely(RewardedAvailabilityChanged);
            if (_rewardedRetry != null) StopCoroutine(_rewardedRetry);
            _rewardedRetry = StartCoroutine(RetryRewardedAfterDelay());
        }

        private IEnumerator RetryRewardedAfterDelay()
        {
            _rewardedRetryAttempt++;
            yield return new WaitForSecondsRealtime(Mathf.Pow(2f, Mathf.Min(6, _rewardedRetryAttempt)));
            _rewardedRetry = null;
            LoadRewarded();
        }

        private void OnRewardedRewarded(LevelPlayAdInfo adInfo, LevelPlayReward reward)
        {
            if (_rewardedState.HasPendingRequest) _rewardedState.MarkRewardEarned();
        }

        private void OnRewardedClosed(LevelPlayAdInfo adInfo)
        {
            if (!_rewardedState.HasPendingRequest) return;
            SetFullscreenState(false);
            try { _rewardedState.CompleteHidden(); }
            catch (Exception exception) { Debug.LogException(exception); }
            InvokeSafely(RewardedAvailabilityChanged);
            LoadRewarded();
        }

        private void OnRewardedDisplayFailed(LevelPlayAdInfo adInfo, LevelPlayAdError error)
        {
            if (!_rewardedState.HasPendingRequest) return;
            SetFullscreenState(false);
            CompleteRewardedDisplayFailed();
            InvokeSafely(RewardedAvailabilityChanged);
            LoadRewarded();
        }

        private void CompleteRewardedDisplayFailed()
        {
            try { _rewardedState.CompleteDisplayFailed(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private void OnInterstitialLoaded(LevelPlayAdInfo adInfo)
        {
            _interstitialRetryAttempt = 0;
            if (_interstitialRetry != null) { StopCoroutine(_interstitialRetry); _interstitialRetry = null; }
        }

        private void OnInterstitialLoadFailed(LevelPlayAdError error)
        {
            if (_shuttingDown) return;
            if (_interstitialRetry != null) StopCoroutine(_interstitialRetry);
            _interstitialRetry = StartCoroutine(RetryInterstitialAfterDelay());
        }

        private IEnumerator RetryInterstitialAfterDelay()
        {
            _interstitialRetryAttempt++;
            yield return new WaitForSecondsRealtime(Mathf.Pow(2f, Mathf.Min(6, _interstitialRetryAttempt)));
            _interstitialRetry = null;
            LoadInterstitial();
        }

        private void OnInterstitialClosed(LevelPlayAdInfo adInfo)
        {
            if (_interstitialCompleted == null) return;
            SetFullscreenState(false);
            CompleteInterstitial(InterstitialAdResult.DisplayedAndClosed);
            LoadInterstitial();
        }

        private void OnInterstitialDisplayFailed(LevelPlayAdInfo adInfo, LevelPlayAdError error)
        {
            if (_interstitialCompleted == null) return;
            SetFullscreenState(false);
            CompleteInterstitial(InterstitialAdResult.DisplayFailed);
            LoadInterstitial();
        }

        private void CompleteInterstitial(InterstitialAdResult result)
        {
            var callback = _interstitialCompleted;
            _interstitialCompleted = null;
            InvokeSafely(callback, result);
        }

        private void OnBannerLoaded(LevelPlayAdInfo adInfo)
        {
            _bannerLoaded = true;
            ApplyBannerVisibility();
            if (_bannerVisible) PublishCurrentBannerHeight();
        }

        private void OnBannerLoadFailed(LevelPlayAdError error)
        {
            _bannerLoaded = false;
            _bannerVisible = false;
            PublishBannerHeight(0f);
        }

        private void ApplyBannerVisibility()
        {
            bool shouldShow = IsInitialized && _bannerAd != null && _bannerLoaded && _bannerDesired && !IsFullscreenAdShowing;
            if (shouldShow == _bannerVisible) return;
            _bannerVisible = shouldShow;
            try
            {
                if (shouldShow)
                {
                    _bannerAd.ShowAd();
                    PublishCurrentBannerHeight();
                }
                else
                {
                    if (_bannerAd != null) _bannerAd.HideAd();
                    PublishBannerHeight(0f);
                }
            }
            catch (Exception exception)
            {
                _bannerVisible = false;
                PublishBannerHeight(0f);
                Debug.LogWarning("LevelPlay banner visibility failed safely: " + exception.Message);
            }
        }

        private void PublishCurrentBannerHeight()
        {
            if (!_bannerVisible || _bannerAd == null) { PublishBannerHeight(0f); return; }
            try
            {
                var size = _bannerAd.GetAdSize();
                PublishBannerHeight(size == null ? 0f : Mathf.Max(0f, size.Height));
            }
            catch { PublishBannerHeight(0f); }
        }

        private void PublishBannerHeight(float pixels) => InvokeSafely(BannerHeightChanged, pixels);

        private void SetFullscreenState(bool active)
        {
            if (IsFullscreenAdShowing == active) return;
            IsFullscreenAdShowing = active;
            if (active)
            {
                _previousAudioPause = AudioListener.pause;
                _previousAudioVolume = AudioListener.volume;
                AudioListener.pause = true;
                AudioListener.volume = 0f;
            }
            else
            {
                AudioListener.pause = _previousAudioPause;
                AudioListener.volume = _previousAudioVolume;
            }
            ApplyBannerVisibility();
            InvokeSafely(FullscreenAdStateChanged, active);
        }

        private void NotifyWillPresent()
        {
            try { FullscreenAdWillPresent?.Invoke(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private void OnRewardedImpressionDataReady(LevelPlayImpressionData data) => QueueRevenue("rewarded", data);
        private void OnInterstitialImpressionDataReady(LevelPlayImpressionData data) => QueueRevenue("interstitial", data);
        private void OnBannerImpressionDataReady(LevelPlayImpressionData data) => QueueRevenue("banner", data);

        private void QueueRevenue(string format, LevelPlayImpressionData data)
        {
            if (data == null) return;
            var revenueEvent = new AdRevenueEvent(format, data.Placement ?? string.Empty, data.AdNetwork ?? string.Empty, data.Revenue ?? 0d, data.Precision ?? string.Empty);
            lock (_revenueLock) _revenueQueue.Enqueue(revenueEvent);
        }

private void DrainRevenueQueue()
        {
            while (true)
            {
                AdRevenueEvent revenueEvent;
                lock (_revenueLock)
                {
                    if (_revenueQueue.Count == 0) return;
                    revenueEvent = _revenueQueue.Dequeue();
                }
                InvokeSafely(RevenuePaid, revenueEvent);
            }
        }

        private static string PlacementName(RewardPlacement placement) => placement switch
        {
            RewardPlacement.ExtraHeart => RewardExtraHeartPlacement,
            RewardPlacement.Hint => RewardHintPlacement,
            RewardPlacement.RevealVillain => RewardRevealVillainPlacement,
            _ => RewardHintPlacement
        };

        private static void InvokeSafely(Action callback)
        {
            try { callback?.Invoke(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private static void InvokeSafely<T>(Action<T> callback, T value)
        {
            try { callback?.Invoke(value); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
