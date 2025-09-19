using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;
using RedRunner.Progression;
using RedRunner.Monetization;

namespace RedRunner.Advertising
{
    /// <summary>
    /// Comprehensive advertising system with multiple networks and smart placement
    /// Handles rewarded videos, interstitials, and banner ads with optimization
    /// </summary>
    public class AdManager : MonoBehaviour
    {
        private static AdManager instance;
        public static AdManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<AdManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("AdManager");
                        instance = go.AddComponent<AdManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Ad Configuration")]
        [SerializeField] private bool enableAds = true;
        [SerializeField] private AdNetworkProvider[] adProviders;
        [SerializeField] private AdPlacement[] adPlacements;
        
        [Header("Reward Settings")]
        [SerializeField] private RewardedAdReward[] rewardedAdRewards;
        [SerializeField] private float rewardMultiplier = 1.0f;
        [SerializeField] private int maxDailyRewards = 10;
        
        [Header("Frequency Control")]
        [SerializeField] private float interstitialCooldown = 300f; // 5 minutes
        [SerializeField] private int gamesUntilInterstitial = 3;
        [SerializeField] private float bannerRefreshInterval = 60f;
        
        [Header("User Experience")]
        [SerializeField] private bool respectDoNotTrack = true;
        [SerializeField] private bool enableConsentUI = true;
        [SerializeField] private float adTimeout = 30f;
        
        [Header("Mediation")]
        [SerializeField] private bool enableMediation = true;
        [SerializeField] private AdMediationType mediationType = AdMediationType.Waterfall;
        [SerializeField] private float networkSwitchDelay = 2f;
        
        // Ad state management
        private Dictionary<AdType, List<IAdProvider>> adProvidersByType;
        private Dictionary<string, AdPlacement> placementLookup;
        private AdData adData;
        private ConsentStatus consentStatus = ConsentStatus.Unknown;
        
        // Timing and frequency
        private float lastInterstitialTime = 0f;
        private int gamesSinceLastInterstitial = 0;
        private Dictionary<string, DateTime> lastRewardTimes;
        private Dictionary<string, int> dailyRewardCounts;
        
        // Current ad state
        private AdRequest currentRequest;
        private Coroutine adTimeoutCoroutine;
        private bool isShowingAd = false;
        
        // Events
        public static event Action<AdType, string> OnAdLoaded;
        public static event Action<AdType, string> OnAdFailedToLoad;
        public static event Action<AdType, string> OnAdShown;
        public static event Action<AdType, string> OnAdClosed;
        public static event Action<AdType, string> OnAdClicked;
        public static event Action<AdType, string, AdReward> OnAdRewardEarned;
        public static event Action<ConsentStatus> OnConsentStatusChanged;
        
        public bool IsInitialized { get; private set; }
        public bool AreAdsRemoved => IAPManager.Instance?.IsProductOwned("remove_ads") ?? false;
        public ConsentStatus ConsentStatus => consentStatus;
        public bool IsShowingAd => isShowingAd;

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                Initialize();
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Initialize()
        {
            LoadAdData();
            InitializeProviders();
            BuildPlacementLookup();
            
            lastRewardTimes = new Dictionary<string, DateTime>();
            dailyRewardCounts = new Dictionary<string, int>();
            
            // Check consent status
            if (enableConsentUI)
            {
                CheckConsentStatus();
            }
            else
            {
                consentStatus = ConsentStatus.Granted;
            }
            
            // Initialize ad networks
            if (enableAds && !AreAdsRemoved)
            {
                StartCoroutine(InitializeAdNetworks());
            }
            
            // Subscribe to IAP events
            if (IAPManager.Instance != null)
            {
                IAPManager.OnProductPurchased += OnProductPurchased;
            }
        }

        private void LoadAdData()
        {
            if (SaveGame.Exists("AdData"))
            {
                try
                {
                    string json = SaveGame.Load<string>("AdData");
                    adData = JsonUtility.FromJson<AdData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load ad data: {e.Message}");
                    CreateNewAdData();
                }
            }
            else
            {
                CreateNewAdData();
            }
            
            // Reset daily counters if needed
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (adData.lastResetDate != today)
            {
                adData.dailyStats = new AdDailyStats();
                adData.lastResetDate = today;
                SaveAdData();
            }
        }

        private void CreateNewAdData()
        {
            adData = new AdData
            {
                totalAdsWatched = 0,
                totalRewardsEarned = 0,
                lastResetDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                dailyStats = new AdDailyStats(),
                consentTimestamp = DateTime.MinValue,
                settings = new AdSettings
                {
                    enablePersonalizedAds = true,
                    enableRewardedAds = true,
                    enableInterstitialAds = true,
                    enableBannerAds = true
                }
            };
            
            SaveAdData();
        }

        private void SaveAdData()
        {
            try
            {
                string json = JsonUtility.ToJson(adData);
                SaveGame.Save("AdData", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save ad data: {e.Message}");
            }
        }

        private void InitializeProviders()
        {
            adProvidersByType = new Dictionary<AdType, List<IAdProvider>>();
            
            foreach (AdType adType in System.Enum.GetValues(typeof(AdType)))
            {
                adProvidersByType[adType] = new List<IAdProvider>();
            }
            
            // Initialize ad network providers
            foreach (var providerConfig in adProviders)
            {
                IAdProvider provider = CreateAdProvider(providerConfig);
                if (provider != null)
                {
                    foreach (var supportedType in provider.SupportedAdTypes)
                    {
                        adProvidersByType[supportedType].Add(provider);
                    }
                }
            }
        }

        private IAdProvider CreateAdProvider(AdNetworkProvider config)
        {
            // Factory pattern for creating ad providers
            switch (config.networkType)
            {
                case AdNetworkType.UnityAds:
                    return new UnityAdsProvider(config);
                case AdNetworkType.AdMob:
                    return new AdMobProvider(config);
                case AdNetworkType.AppLovin:
                    return new AppLovinProvider(config);
                case AdNetworkType.IronSource:
                    return new IronSourceProvider(config);
                default:
                    Debug.LogWarning($"Unsupported ad network: {config.networkType}");
                    return null;
            }
        }

        private void BuildPlacementLookup()
        {
            placementLookup = new Dictionary<string, AdPlacement>();
            
            if (adPlacements != null)
            {
                foreach (var placement in adPlacements)
                {
                    placementLookup[placement.id] = placement;
                }
            }
        }

        private IEnumerator InitializeAdNetworks()
        {
            foreach (var adType in adProvidersByType.Keys)
            {
                foreach (var provider in adProvidersByType[adType])
                {
                    provider.Initialize();
                    yield return new WaitForSeconds(0.5f); // Stagger initialization
                }
            }
            
            IsInitialized = true;
            
            // Start preloading ads
            StartCoroutine(PreloadAds());
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("ad_system_initialized", new Dictionary<string, object>
            {
                { "provider_count", adProviders.Length },
                { "consent_status", consentStatus.ToString() },
                { "ads_removed", AreAdsRemoved }
            });
        }

        private IEnumerator PreloadAds()
        {
            while (true)
            {
                if (IsInitialized && !AreAdsRemoved)
                {
                    // Preload interstitials
                    if (adData.settings.enableInterstitialAds)
                    {
                        PreloadAd(AdType.Interstitial);
                    }
                    
                    // Preload rewarded videos
                    if (adData.settings.enableRewardedAds)
                    {
                        PreloadAd(AdType.RewardedVideo);
                    }
                }
                
                yield return new WaitForSeconds(30f); // Check every 30 seconds
            }
        }

        private void PreloadAd(AdType adType)
        {
            var providers = adProvidersByType[adType];
            
            foreach (var provider in providers)
            {
                if (provider.IsAdReady(adType))
                    continue; // Already loaded
                
                provider.LoadAd(adType, GetDefaultPlacementId(adType));
                break; // Only load from one provider at a time
            }
        }

        private string GetDefaultPlacementId(AdType adType)
        {
            foreach (var placement in adPlacements)
            {
                if (placement.adType == adType)
                    return placement.id;
            }
            
            return adType.ToString().ToLower();
        }

        public void ShowAd(AdType adType, string placementId = "", Action<bool> callback = null)
        {
            if (!enableAds || AreAdsRemoved || isShowingAd)
            {
                callback?.Invoke(false);
                return;
            }
            
            if (!IsInitialized)
            {
                Debug.LogWarning("Ad system not initialized");
                callback?.Invoke(false);
                return;
            }
            
            // Check placement restrictions
            if (!string.IsNullOrEmpty(placementId) && !CanShowAdAtPlacement(placementId))
            {
                callback?.Invoke(false);
                return;
            }
            
            // Find available provider
            var provider = GetAvailableProvider(adType);
            if (provider == null)
            {
                Debug.LogWarning($"No provider available for ad type: {adType}");
                callback?.Invoke(false);
                return;
            }
            
            var request = new AdRequest
            {
                adType = adType,
                placementId = placementId,
                provider = provider,
                callback = callback,
                timestamp = DateTime.UtcNow
            };
            
            currentRequest = request;
            isShowingAd = true;
            
            // Start timeout
            if (adTimeoutCoroutine != null)
                StopCoroutine(adTimeoutCoroutine);
            
            adTimeoutCoroutine = StartCoroutine(AdTimeoutCoroutine());
            
            // Show the ad
            provider.ShowAd(adType, placementId, OnAdResult);
            
            // Update frequency tracking
            UpdateAdFrequency(adType);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("ad_show_attempt", new Dictionary<string, object>
            {
                { "ad_type", adType.ToString() },
                { "placement_id", placementId },
                { "provider", provider.GetType().Name }
            });
        }

        private IAdProvider GetAvailableProvider(AdType adType)
        {
            var providers = adProvidersByType[adType];
            
            if (enableMediation && mediationType == AdMediationType.Waterfall)
            {
                // Use waterfall logic - try providers in order
                foreach (var provider in providers)
                {
                    if (provider.IsAdReady(adType))
                        return provider;
                }
            }
            else
            {
                // Use first available provider
                foreach (var provider in providers)
                {
                    if (provider.IsAdReady(adType))
                        return provider;
                }
            }
            
            return null;
        }

        private bool CanShowAdAtPlacement(string placementId)
        {
            if (!placementLookup.TryGetValue(placementId, out var placement))
                return false;
            
            // Check frequency limits
            if (placement.adType == AdType.Interstitial)
            {
                if (Time.time - lastInterstitialTime < interstitialCooldown)
                    return false;
                
                if (gamesSinceLastInterstitial < gamesUntilInterstitial)
                    return false;
            }
            
            // Check daily limits
            if (placement.maxDailyShows > 0)
            {
                string key = $"{placementId}_{DateTime.UtcNow:yyyy-MM-dd}";
                if (dailyRewardCounts.TryGetValue(key, out int count) && count >= placement.maxDailyShows)
                    return false;
            }
            
            return true;
        }

        private void UpdateAdFrequency(AdType adType)
        {
            if (adType == AdType.Interstitial)
            {
                lastInterstitialTime = Time.time;
                gamesSinceLastInterstitial = 0;
            }
        }

        private IEnumerator AdTimeoutCoroutine()
        {
            yield return new WaitForSeconds(adTimeout);
            
            if (isShowingAd)
            {
                Debug.LogWarning("Ad timed out");
                OnAdResult(false, "timeout", null);
            }
        }

        private void OnAdResult(bool success, string result, AdReward reward)
        {
            if (adTimeoutCoroutine != null)
            {
                StopCoroutine(adTimeoutCoroutine);
                adTimeoutCoroutine = null;
            }
            
            isShowingAd = false;
            
            if (currentRequest == null)
                return;
            
            var request = currentRequest;
            currentRequest = null;
            
            if (success)
            {
                // Update statistics
                adData.totalAdsWatched++;
                adData.dailyStats.adsWatched++;
                
                if (request.adType == AdType.RewardedVideo && reward != null)
                {
                    ProcessAdReward(reward, request.placementId);
                }
                
                // Analytics
                AnalyticsManager.Instance?.TrackEvent("ad_completed", new Dictionary<string, object>
                {
                    { "ad_type", request.adType.ToString() },
                    { "placement_id", request.placementId },
                    { "duration", (DateTime.UtcNow - request.timestamp).TotalSeconds }
                });
                
                OnAdShown?.Invoke(request.adType, request.placementId);
            }
            else
            {
                // Analytics
                AnalyticsManager.Instance?.TrackEvent("ad_failed", new Dictionary<string, object>
                {
                    { "ad_type", request.adType.ToString() },
                    { "placement_id", request.placementId },
                    { "reason", result }
                });
            }
            
            request.callback?.Invoke(success);
            SaveAdData();
        }

        private void ProcessAdReward(AdReward reward, string placementId)
        {
            // Check if reward is valid
            if (!IsRewardValid(placementId))
                return;
            
            // Apply reward multiplier
            var finalAmount = Mathf.RoundToInt(reward.amount * rewardMultiplier);
            
            // Grant the reward
            switch (reward.type)
            {
                case RewardType.Coins:
                    ProgressionManager.Instance?.AddCurrency(CurrencyType.Coins, finalAmount);
                    break;
                    
                case RewardType.Gems:
                    ProgressionManager.Instance?.AddCurrency(CurrencyType.Gems, finalAmount);
                    break;
                    
                case RewardType.Experience:
                    ProgressionManager.Instance?.AddExperience(finalAmount, "rewarded_ad");
                    break;
                    
                case RewardType.ExtraLife:
                    // Grant extra life (implement based on your game logic)
                    break;
                    
                case RewardType.DoubleCoins:
                    // Apply coin multiplier for next run
                    ApplyTemporaryBonus("coin_multiplier", 2.0f, 300f); // 5 minutes
                    break;
            }
            
            // Update reward tracking
            string dailyKey = $"{placementId}_{DateTime.UtcNow:yyyy-MM-dd}";
            dailyRewardCounts[dailyKey] = dailyRewardCounts.GetValueOrDefault(dailyKey, 0) + 1;
            lastRewardTimes[placementId] = DateTime.UtcNow;
            
            adData.totalRewardsEarned++;
            adData.dailyStats.rewardsEarned++;
            
            OnAdRewardEarned?.Invoke(AdType.RewardedVideo, placementId, reward);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("ad_reward_earned", new Dictionary<string, object>
            {
                { "placement_id", placementId },
                { "reward_type", reward.type.ToString() },
                { "reward_amount", finalAmount }
            });
        }

        private bool IsRewardValid(string placementId)
        {
            // Check daily reward limit
            string dailyKey = $"{placementId}_{DateTime.UtcNow:yyyy-MM-dd}";
            int dailyCount = dailyRewardCounts.GetValueOrDefault(dailyKey, 0);
            
            if (dailyCount >= maxDailyRewards)
                return false;
            
            // Check time-based restrictions
            if (lastRewardTimes.TryGetValue(placementId, out DateTime lastTime))
            {
                if ((DateTime.UtcNow - lastTime).TotalMinutes < 5) // 5 minute cooldown
                    return false;
            }
            
            return true;
        }

        private void ApplyTemporaryBonus(string bonusType, float value, float duration)
        {
            // Implement temporary bonus system
            StartCoroutine(TemporaryBonusCoroutine(bonusType, value, duration));
        }

        private IEnumerator TemporaryBonusCoroutine(string bonusType, float value, float duration)
        {
            // Apply bonus
            switch (bonusType)
            {
                case "coin_multiplier":
                    // Set coin multiplier in game
                    break;
            }
            
            yield return new WaitForSeconds(duration);
            
            // Remove bonus
            switch (bonusType)
            {
                case "coin_multiplier":
                    // Reset coin multiplier
                    break;
            }
        }

        public void ShowBanner(string placementId = "")
        {
            if (!enableAds || AreAdsRemoved || !adData.settings.enableBannerAds)
                return;
            
            var provider = GetAvailableProvider(AdType.Banner);
            provider?.ShowAd(AdType.Banner, placementId, null);
        }

        public void HideBanner()
        {
            foreach (var provider in adProvidersByType[AdType.Banner])
            {
                provider.HideBanner();
            }
        }

        public void ShowInterstitial(string placementId = "", Action<bool> callback = null)
        {
            ShowAd(AdType.Interstitial, placementId, callback);
        }

        public void ShowRewardedVideo(string placementId = "", Action<bool> callback = null)
        {
            ShowAd(AdType.RewardedVideo, placementId, callback);
        }

        public bool IsRewardedVideoReady(string placementId = "")
        {
            var provider = GetAvailableProvider(AdType.RewardedVideo);
            return provider?.IsAdReady(AdType.RewardedVideo) ?? false;
        }

        public void OnGameStart()
        {
            gamesSinceLastInterstitial++;
        }

        public void OnGameEnd()
        {
            // Consider showing interstitial
            if (ShouldShowInterstitialAfterGame())
            {
                ShowInterstitial("game_end");
            }
        }

        private bool ShouldShowInterstitialAfterGame()
        {
            if (!adData.settings.enableInterstitialAds)
                return false;
            
            if (Time.time - lastInterstitialTime < interstitialCooldown)
                return false;
            
            if (gamesSinceLastInterstitial < gamesUntilInterstitial)
                return false;
            
            return true;
        }

        private void CheckConsentStatus()
        {
            // Check saved consent status
            if (PlayerPrefs.HasKey("AdConsentStatus"))
            {
                int statusInt = PlayerPrefs.GetInt("AdConsentStatus");
                consentStatus = (ConsentStatus)statusInt;
            }
            else
            {
                // Show consent dialog for GDPR compliance
                ShowConsentDialog();
            }
        }

        private void ShowConsentDialog()
        {
            // Implement consent UI
            // For now, we'll assume consent is granted
            SetConsentStatus(ConsentStatus.Granted);
        }

        public void SetConsentStatus(ConsentStatus status)
        {
            consentStatus = status;
            PlayerPrefs.SetInt("AdConsentStatus", (int)status);
            
            // Update ad providers with consent status
            foreach (var providerList in adProvidersByType.Values)
            {
                foreach (var provider in providerList)
                {
                    provider.SetConsentStatus(status);
                }
            }
            
            OnConsentStatusChanged?.Invoke(status);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("ad_consent_changed", new Dictionary<string, object>
            {
                { "status", status.ToString() }
            });
        }

        private void OnProductPurchased(IAPProduct product)
        {
            if (product.id == "remove_ads")
            {
                HideBanner();
                
                // Analytics
                AnalyticsManager.Instance?.TrackEvent("ads_removed_via_purchase", new Dictionary<string, object>
                {
                    { "product_id", product.id }
                });
            }
        }

        public AdDailyStats GetDailyStats()
        {
            return adData.dailyStats;
        }

        public AdSettings GetAdSettings()
        {
            return adData.settings;
        }

        public void UpdateAdSettings(AdSettings newSettings)
        {
            adData.settings = newSettings;
            SaveAdData();
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveAdData();
            }
        }

        void OnDestroy()
        {
            SaveAdData();
            
            // Cleanup ad providers
            foreach (var providerList in adProvidersByType.Values)
            {
                foreach (var provider in providerList)
                {
                    provider?.Dispose();
                }
            }
        }
    }

    // Interfaces and base classes
    public interface IAdProvider
    {
        void Initialize();
        void LoadAd(AdType adType, string placementId);
        void ShowAd(AdType adType, string placementId, Action<bool, string, AdReward> callback);
        bool IsAdReady(AdType adType);
        void HideBanner();
        void SetConsentStatus(ConsentStatus status);
        void Dispose();
        AdType[] SupportedAdTypes { get; }
    }

    // Mock ad provider implementations
    public class UnityAdsProvider : IAdProvider
    {
        private AdNetworkProvider config;
        
        public AdType[] SupportedAdTypes => new[] { AdType.RewardedVideo, AdType.Interstitial, AdType.Banner };
        
        public UnityAdsProvider(AdNetworkProvider config)
        {
            this.config = config;
        }
        
        public void Initialize() { Debug.Log("Unity Ads initialized"); }
        public void LoadAd(AdType adType, string placementId) { }
        public void ShowAd(AdType adType, string placementId, Action<bool, string, AdReward> callback) 
        { 
            callback?.Invoke(true, "success", new AdReward { type = RewardType.Coins, amount = 100 });
        }
        public bool IsAdReady(AdType adType) => true;
        public void HideBanner() { }
        public void SetConsentStatus(ConsentStatus status) { }
        public void Dispose() { }
    }

    public class AdMobProvider : IAdProvider
    {
        private AdNetworkProvider config;
        
        public AdType[] SupportedAdTypes => new[] { AdType.RewardedVideo, AdType.Interstitial, AdType.Banner };
        
        public AdMobProvider(AdNetworkProvider config)
        {
            this.config = config;
        }
        
        public void Initialize() { Debug.Log("AdMob initialized"); }
        public void LoadAd(AdType adType, string placementId) { }
        public void ShowAd(AdType adType, string placementId, Action<bool, string, AdReward> callback) 
        { 
            callback?.Invoke(true, "success", new AdReward { type = RewardType.Coins, amount = 100 });
        }
        public bool IsAdReady(AdType adType) => true;
        public void HideBanner() { }
        public void SetConsentStatus(ConsentStatus status) { }
        public void Dispose() { }
    }

    public class AppLovinProvider : IAdProvider
    {
        private AdNetworkProvider config;
        
        public AdType[] SupportedAdTypes => new[] { AdType.RewardedVideo, AdType.Interstitial, AdType.Banner };
        
        public AppLovinProvider(AdNetworkProvider config)
        {
            this.config = config;
        }
        
        public void Initialize() { Debug.Log("AppLovin initialized"); }
        public void LoadAd(AdType adType, string placementId) { }
        public void ShowAd(AdType adType, string placementId, Action<bool, string, AdReward> callback) 
        { 
            callback?.Invoke(true, "success", new AdReward { type = RewardType.Coins, amount = 100 });
        }
        public bool IsAdReady(AdType adType) => true;
        public void HideBanner() { }
        public void SetConsentStatus(ConsentStatus status) { }
        public void Dispose() { }
    }

    public class IronSourceProvider : IAdProvider
    {
        private AdNetworkProvider config;
        
        public AdType[] SupportedAdTypes => new[] { AdType.RewardedVideo, AdType.Interstitial };
        
        public IronSourceProvider(AdNetworkProvider config)
        {
            this.config = config;
        }
        
        public void Initialize() { Debug.Log("IronSource initialized"); }
        public void LoadAd(AdType adType, string placementId) { }
        public void ShowAd(AdType adType, string placementId, Action<bool, string, AdReward> callback) 
        { 
            callback?.Invoke(true, "success", new AdReward { type = RewardType.Coins, amount = 100 });
        }
        public bool IsAdReady(AdType adType) => true;
        public void HideBanner() { }
        public void SetConsentStatus(ConsentStatus status) { }
        public void Dispose() { }
    }

    // Data structures
    [System.Serializable]
    public class AdNetworkProvider
    {
        public AdNetworkType networkType;
        public string appId;
        public string androidAppId;
        public string iosAppId;
        public bool testMode;
        public int priority;
        public AdUnitConfig[] adUnits;
    }

    [System.Serializable]
    public class AdUnitConfig
    {
        public AdType adType;
        public string placementId;
        public string androidId;
        public string iosId;
    }

    [System.Serializable]
    public class AdPlacement
    {
        public string id;
        public string displayName;
        public AdType adType;
        public int maxDailyShows;
        public float cooldownMinutes;
        public bool requiresConsent;
    }

    [System.Serializable]
    public class RewardedAdReward
    {
        public string placementId;
        public RewardType rewardType;
        public int baseAmount;
        public float multiplier;
    }

    [System.Serializable]
    public class AdData
    {
        public int totalAdsWatched;
        public int totalRewardsEarned;
        public string lastResetDate;
        public AdDailyStats dailyStats;
        public DateTime consentTimestamp;
        public AdSettings settings;
    }

    [System.Serializable]
    public class AdDailyStats
    {
        public int adsWatched = 0;
        public int rewardsEarned = 0;
        public int interstitialsShown = 0;
        public int bannersShown = 0;
    }

    [System.Serializable]
    public class AdSettings
    {
        public bool enablePersonalizedAds = true;
        public bool enableRewardedAds = true;
        public bool enableInterstitialAds = true;
        public bool enableBannerAds = true;
    }

    [System.Serializable]
    public class AdRequest
    {
        public AdType adType;
        public string placementId;
        public IAdProvider provider;
        public Action<bool> callback;
        public DateTime timestamp;
    }

    [System.Serializable]
    public class AdReward
    {
        public RewardType type;
        public int amount;
        public string itemId;
    }

    // Enums
    public enum AdType
    {
        Banner,
        Interstitial,
        RewardedVideo,
        Native
    }

    public enum AdNetworkType
    {
        UnityAds,
        AdMob,
        AppLovin,
        IronSource,
        Vungle,
        ChartboostMedley
    }

    public enum ConsentStatus
    {
        Unknown,
        Granted,
        Denied,
        NotRequired
    }

    public enum AdMediationType
    {
        Waterfall,
        Bidding,
        Hybrid
    }

    public enum RewardType
    {
        Coins,
        Gems,
        Experience,
        ExtraLife,
        DoubleCoins,
        PowerUp,
        Character,
        Skin
    }
}