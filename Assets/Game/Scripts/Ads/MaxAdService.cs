using System;
using System.Collections;
using MonsterLogic.Services;
using UnityEngine;

namespace MonsterLogic.Ads
{
    [DisallowMultipleComponent]
    public sealed class MaxAdService : MonoBehaviour, IAdService
    {
        private const string RewardExtraHeartPlacement = "reward_extra_heart";
        private const string RewardHintPlacement = "reward_hint";
        private const string RewardRevealVillainPlacement = "reward_reveal_villain";
        private const string InterstitialPlacement = "interstitial_post_level";
        private const string BannerPlacement = "banner_progression";

        private static MaxAdService _active;

        private readonly RewardedAdStateMachine _rewardedState = new RewardedAdStateMachine();
        private AdsConfig _config;
        private AdPolicy _policy;
        private string _rewardedAdUnitId;
        private string _interstitialAdUnitId;
        private string _bannerAdUnitId;
        private Action<InterstitialAdResult> _interstitialCompleted;
        private bool _subscribed;
        private bool _initializing;
        private bool _shuttingDown;
        private bool _bannerCreated;
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
        public bool IsRewardedReady => IsInitialized && !_rewardedState.HasPendingRequest && MaxSdk.IsRewardedAdReady(_rewardedAdUnitId);
        public bool IsFullscreenAdShowing { get; private set; }
        public bool CanShowPrivacyOptions
        {
            get
            {
                if (!IsInitialized) return false;
                try { return MaxSdk.CmpService.HasSupportedCmp; }
                catch { return false; }
            }
        }

        public void Configure(AdsConfig config, AdPolicy policy)
        {
            if (_initializing || IsInitialized) throw new InvalidOperationException("MAX cannot be reconfigured after initialization starts.");
            _config = config;
            _policy = policy ?? AdPolicy.FromConfig(config);
        }

        public void Initialize()
        {
            if (_initializing || IsInitialized || _shuttingDown) return;
            if (_active != null && _active != this)
            {
                Debug.LogWarning("A second MaxAdService was rejected to prevent duplicate MAX callback subscriptions.");
                return;
            }
            string reason = "AdsConfig is missing.";
            bool runtimeReady = _config != null && _config.IsRuntimeReady(out reason);
            if (!runtimeReady)
            {
                Debug.Log("MAX ads remain disabled: " + reason);
                return;
            }

            _rewardedAdUnitId = _config.RewardedAdUnitId;
            _interstitialAdUnitId = _config.InterstitialAdUnitId;
            _bannerAdUnitId = _config.BannerAdUnitId;
            _active = this;
            _initializing = true;
            SubscribeCallbacks();

            try
            {
                MaxSdk.SetVerboseLogging(_config.developmentTestMode);
                MaxSdk.InitializeSdk(new[] { _rewardedAdUnitId, _interstitialAdUnitId, _bannerAdUnitId });
                StartCoroutine(InitializationTimeout());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MAX initialization failed safely: " + exception.Message);
                _initializing = false;
                UnsubscribeCallbacks();
                if (_active == this) _active = null;
            }
        }

        public void ShowRewarded(RewardPlacement placement, Action<RewardedAdResult> completed)
        {
            if (_rewardedState.HasPendingRequest)
            {
                InvokeSafely(completed, RewardedAdResult.NotReady);
                return;
            }
            if (!IsInitialized || !MaxSdk.IsRewardedAdReady(_rewardedAdUnitId))
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
                MaxSdk.ShowRewardedAd(_rewardedAdUnitId, PlacementName(placement));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MAX rewarded display failed safely: " + exception.Message);
                SetFullscreenState(false);
                CompleteRewardedDisplayFailed();
                LoadRewarded();
            }
        }

        public void ShowPostLevelInterstitialIfAllowed(int completedLevel, long completionToken, Action<InterstitialAdResult> completed)
        {
            if (_policy == null || !_policy.IsInterstitialEligible(completedLevel))
            {
                InvokeSafely(completed, InterstitialAdResult.Ineligible);
                return;
            }
            if (!_policy.TryConsumeInterstitial(completionToken, completedLevel))
            {
                InvokeSafely(completed, InterstitialAdResult.Ineligible);
                return;
            }
            if (!IsInitialized || IsFullscreenAdShowing || _interstitialCompleted != null || !MaxSdk.IsInterstitialReady(_interstitialAdUnitId))
            {
                InvokeSafely(completed, InterstitialAdResult.NotReady);
                return;
            }

            _interstitialCompleted = completed;
            try
            {
                NotifyWillPresent();
                SetFullscreenState(true);
                MaxSdk.ShowInterstitial(_interstitialAdUnitId, InterstitialPlacement);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MAX interstitial display failed safely: " + exception.Message);
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
            if (!CanShowPrivacyOptions)
            {
                InvokeSafely(completed, false);
                return;
            }

            bool callbackInvoked = false;
            try
            {
                MaxSdk.CmpService.ShowCmpForExistingUser(error =>
                {
                    if (callbackInvoked) return;
                    callbackInvoked = true;
                    InvokeSafely(completed, error == null);
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MAX privacy choices could not open: " + exception.Message);
                if (!callbackInvoked) InvokeSafely(completed, false);
            }
        }

        public void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            StopAllCoroutines();
            _rewardedRetry = null;
            _interstitialRetry = null;
            UnsubscribeCallbacks();
            _rewardedState.CancelWithoutCallback();
            _interstitialCompleted = null;

            if (_bannerCreated)
            {
                try { MaxSdk.DestroyBanner(_bannerAdUnitId); }
                catch { }
            }
            _bannerCreated = false;
            _bannerVisible = false;
            PublishBannerHeight(0f);
            SetFullscreenState(false);
            IsInitialized = false;
            _initializing = false;
            if (_active == this) _active = null;
        }

        private void Update()
        {
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
            _initializing = false;
            Debug.LogWarning("MAX initialization timed out; ads are disabled for this session.");
            UnsubscribeCallbacks();
            if (_active == this) _active = null;
        }

        private void SubscribeCallbacks()
        {
            if (_subscribed) return;
            _subscribed = true;
            MaxSdkCallbacks.OnSdkInitializedEvent += OnSdkInitialized;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedReceivedReward;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedRevenuePaid;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHidden;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnInterstitialRevenuePaid;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent += OnBannerLoaded;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += OnBannerLoadFailed;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnBannerRevenuePaid;
        }

        private void UnsubscribeCallbacks()
        {
            if (!_subscribed) return;
            _subscribed = false;
            MaxSdkCallbacks.OnSdkInitializedEvent -= OnSdkInitialized;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent -= OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent -= OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent -= OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent -= OnRewardedReceivedReward;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent -= OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent -= OnRewardedRevenuePaid;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent -= OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent -= OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent -= OnInterstitialDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent -= OnInterstitialHidden;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent -= OnInterstitialRevenuePaid;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent -= OnBannerLoaded;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent -= OnBannerLoadFailed;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent -= OnBannerRevenuePaid;
        }

        private void OnSdkInitialized(MaxSdk.SdkConfiguration configuration)
        {
            if (_shuttingDown || !_initializing) return;
            _initializing = false;
            IsInitialized = true;
            LoadRewarded();
            LoadInterstitial();
            CreateBannerOnce();
        }

        private void LoadRewarded()
        {
            if (!IsInitialized || _shuttingDown) return;
            try { MaxSdk.LoadRewardedAd(_rewardedAdUnitId); }
            catch (Exception exception) { Debug.LogWarning("MAX rewarded load failed safely: " + exception.Message); }
        }

        private void LoadInterstitial()
        {
            if (!IsInitialized || _shuttingDown) return;
            try { MaxSdk.LoadInterstitial(_interstitialAdUnitId); }
            catch (Exception exception) { Debug.LogWarning("MAX interstitial load failed safely: " + exception.Message); }
        }

        private void CreateBannerOnce()
        {
            if (_bannerCreated || !IsInitialized) return;
            try
            {
                var configuration = new MaxSdk.AdViewConfiguration(MaxSdk.AdViewPosition.BottomCenter) { IsAdaptive = true };
                MaxSdk.CreateBanner(_bannerAdUnitId, configuration);
                MaxSdk.SetBannerPlacement(_bannerAdUnitId, BannerPlacement);
                MaxSdk.SetBannerBackgroundColor(_bannerAdUnitId, new Color(0.035f, 0.025f, 0.12f, 1f));
                MaxSdk.HideBanner(_bannerAdUnitId);
                _bannerCreated = true;
                ApplyBannerVisibility();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MAX banner creation failed safely: " + exception.Message);
                PublishBannerHeight(0f);
            }
        }

        private void OnRewardedLoaded(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId != _rewardedAdUnitId) return;
            _rewardedRetryAttempt = 0;
            if (_rewardedRetry != null) { StopCoroutine(_rewardedRetry); _rewardedRetry = null; }
            InvokeSafely(RewardedAvailabilityChanged);
        }

        private void OnRewardedLoadFailed(string adUnitId, MaxSdk.ErrorInfo errorInfo)
        {
            if (adUnitId != _rewardedAdUnitId || _shuttingDown) return;
            InvokeSafely(RewardedAvailabilityChanged);
            if (_rewardedRetry != null) StopCoroutine(_rewardedRetry);
            _rewardedRetry = StartCoroutine(RetryRewardedAfterDelay());
        }

        private IEnumerator RetryRewardedAfterDelay()
        {
            _rewardedRetryAttempt++;
            float delay = Mathf.Pow(2f, Mathf.Min(6, _rewardedRetryAttempt));
            yield return new WaitForSecondsRealtime(delay);
            _rewardedRetry = null;
            LoadRewarded();
        }

        private void OnRewardedReceivedReward(string adUnitId, MaxSdk.Reward reward, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId == _rewardedAdUnitId) _rewardedState.MarkRewardEarned();
        }

        private void OnRewardedHidden(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId != _rewardedAdUnitId || !_rewardedState.HasPendingRequest) return;
            SetFullscreenState(false);
            try { _rewardedState.CompleteHidden(); }
            catch (Exception exception) { Debug.LogException(exception); }
            InvokeSafely(RewardedAvailabilityChanged);
            LoadRewarded();
        }

        private void OnRewardedDisplayFailed(string adUnitId, MaxSdk.ErrorInfo errorInfo, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId != _rewardedAdUnitId || !_rewardedState.HasPendingRequest) return;
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

        private void OnInterstitialLoaded(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId != _interstitialAdUnitId) return;
            _interstitialRetryAttempt = 0;
            if (_interstitialRetry != null) { StopCoroutine(_interstitialRetry); _interstitialRetry = null; }
        }

        private void OnInterstitialLoadFailed(string adUnitId, MaxSdk.ErrorInfo errorInfo)
        {
            if (adUnitId != _interstitialAdUnitId || _shuttingDown) return;
            if (_interstitialRetry != null) StopCoroutine(_interstitialRetry);
            _interstitialRetry = StartCoroutine(RetryInterstitialAfterDelay());
        }

        private IEnumerator RetryInterstitialAfterDelay()
        {
            _interstitialRetryAttempt++;
            float delay = Mathf.Pow(2f, Mathf.Min(6, _interstitialRetryAttempt));
            yield return new WaitForSecondsRealtime(delay);
            _interstitialRetry = null;
            LoadInterstitial();
        }

        private void OnInterstitialHidden(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId != _interstitialAdUnitId || _interstitialCompleted == null) return;
            SetFullscreenState(false);
            CompleteInterstitial(InterstitialAdResult.DisplayedAndClosed);
            LoadInterstitial();
        }

        private void OnInterstitialDisplayFailed(string adUnitId, MaxSdk.ErrorInfo errorInfo, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId != _interstitialAdUnitId || _interstitialCompleted == null) return;
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

        private void OnBannerLoaded(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            if (adUnitId != _bannerAdUnitId) return;
            ApplyBannerVisibility();
            if (_bannerVisible) PublishCurrentBannerHeight();
        }

        private void OnBannerLoadFailed(string adUnitId, MaxSdk.ErrorInfo errorInfo)
        {
            if (adUnitId != _bannerAdUnitId) return;
            _bannerVisible = false;
            PublishBannerHeight(0f);
        }

        private void ApplyBannerVisibility()
        {
            bool shouldShow = IsInitialized && _bannerCreated && _bannerDesired && !IsFullscreenAdShowing;
            if (shouldShow == _bannerVisible) return;
            _bannerVisible = shouldShow;
            try
            {
                if (shouldShow)
                {
                    MaxSdk.ShowBanner(_bannerAdUnitId);
                    PublishCurrentBannerHeight();
                }
                else
                {
                    if (_bannerCreated) MaxSdk.HideBanner(_bannerAdUnitId);
                    PublishBannerHeight(0f);
                }
            }
            catch (Exception exception)
            {
                _bannerVisible = false;
                PublishBannerHeight(0f);
                Debug.LogWarning("MAX banner visibility failed safely: " + exception.Message);
            }
        }

        private void PublishCurrentBannerHeight()
        {
            if (!_bannerVisible) { PublishBannerHeight(0f); return; }
            try { PublishBannerHeight(Mathf.Max(0f, MaxSdk.GetBannerLayout(_bannerAdUnitId).height)); }
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

        private void OnRewardedRevenuePaid(string adUnitId, MaxSdk.AdInfo adInfo) => PublishRevenue("rewarded", adInfo);
        private void OnInterstitialRevenuePaid(string adUnitId, MaxSdk.AdInfo adInfo) => PublishRevenue("interstitial", adInfo);
        private void OnBannerRevenuePaid(string adUnitId, MaxSdk.AdInfo adInfo) => PublishRevenue("banner", adInfo);

        private void PublishRevenue(string format, MaxSdk.AdInfo adInfo)
        {
            if (adInfo == null) return;
            var revenueEvent = new AdRevenueEvent(format, adInfo.Placement, adInfo.NetworkName, adInfo.Revenue, adInfo.RevenuePrecision);
            Debug.Log($"MAX revenue event: format={format}, placement={adInfo.Placement}, network={adInfo.NetworkName}, revenue={adInfo.Revenue}, precision={adInfo.RevenuePrecision}");
            InvokeSafely(RevenuePaid, revenueEvent);
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
