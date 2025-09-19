using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;
using RedRunner.Progression;

namespace RedRunner.LiveOps
{
    /// <summary>
    /// Live Operations system for dynamic content updates and remote configuration
    /// Enables real-time game balancing, events, and content delivery without app updates
    /// </summary>
    public class LiveOpsManager : MonoBehaviour
    {
        private static LiveOpsManager instance;
        public static LiveOpsManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<LiveOpsManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("LiveOpsManager");
                        instance = go.AddComponent<LiveOpsManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Remote Config")]
        [SerializeField] private string remoteConfigUrl = "https://api.yourgame.com/config";
        [SerializeField] private bool enableRemoteConfig = true;
        [SerializeField] private float configFetchInterval = 3600f; // 1 hour
        [SerializeField] private RemoteConfigEntry[] defaultConfigs;
        
        [Header("Live Events")]
        [SerializeField] private bool enableLiveEvents = true;
        [SerializeField] private float eventCheckInterval = 300f; // 5 minutes
        [SerializeField] private string liveEventsUrl = "https://api.yourgame.com/events";
        
        [Header("Content Delivery")]
        [SerializeField] private bool enableContentDelivery = true;
        [SerializeField] private string contentDeliveryUrl = "https://api.yourgame.com/content";
        [SerializeField] private int maxCachedAssets = 50;
        
        [Header("A/B Testing")]
        [SerializeField] private bool enableABTesting = true;
        [SerializeField] private string abTestingUrl = "https://api.yourgame.com/abtests";
        [SerializeField] private ABTestConfig[] abTests;
        
        [Header("Push Notifications")]
        [SerializeField] private bool enablePushNotifications = true;
        [SerializeField] private NotificationSchedule[] scheduledNotifications;
        
        [Header("Caching")]
        [SerializeField] private float cacheExpiryHours = 24f;
        [SerializeField] private bool enableOfflineMode = true;
        [SerializeField] private int maxRetryAttempts = 3;
        
        // Remote config state
        private RemoteConfig currentConfig;
        private Dictionary<string, RemoteConfigEntry> configLookup;
        private DateTime lastConfigFetch;
        private bool configInitialized = false;
        
        // Live events state
        private List<LiveEvent> activeLiveEvents;
        private Dictionary<string, LiveEvent> liveEventLookup;
        private DateTime lastEventCheck;
        
        // Content delivery
        private Dictionary<string, CachedAsset> assetCache;
        private Queue<ContentRequest> contentRequestQueue;
        
        // A/B Testing
        private Dictionary<string, ABTestResult> abTestResults;
        private string playerSegment = "";
        
        // Coroutines
        private Coroutine configUpdateCoroutine;
        private Coroutine eventUpdateCoroutine;
        private Coroutine contentDeliveryCoroutine;
        
        // Events
        public static event Action OnRemoteConfigUpdated;
        public static event Action<LiveEvent> OnLiveEventStarted;
        public static event Action<LiveEvent> OnLiveEventEnded;
        public static event Action<string> OnContentDelivered;
        public static event Action<ABTestResult> OnABTestAssigned;
        public static event Action<string> OnLiveOpsError;
        
        // Properties
        public bool IsConfigInitialized => configInitialized;
        public RemoteConfig CurrentConfig => currentConfig;
        public List<LiveEvent> ActiveLiveEvents => activeLiveEvents ?? new List<LiveEvent>();

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
            configLookup = new Dictionary<string, RemoteConfigEntry>();
            activeLiveEvents = new List<LiveEvent>();
            liveEventLookup = new Dictionary<string, LiveEvent>();
            assetCache = new Dictionary<string, CachedAsset>();
            contentRequestQueue = new Queue<ContentRequest>();
            abTestResults = new Dictionary<string, ABTestResult>();
            
            // Build default config lookup
            BuildDefaultConfigLookup();
            
            // Load cached data
            LoadCachedData();
            
            // Determine player segment for A/B testing
            DeterminePlayerSegment();
            
            // Start live operations
            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                StartCoroutine(InitializeLiveOps());
            }
            else if (enableOfflineMode)
            {
                InitializeOfflineMode();
            }
        }

        private void BuildDefaultConfigLookup()
        {
            if (defaultConfigs != null)
            {
                foreach (var config in defaultConfigs)
                {
                    configLookup[config.key] = config;
                }
            }
        }

        private void LoadCachedData()
        {
            // Load cached remote config
            if (SaveGame.Exists("RemoteConfig"))
            {
                try
                {
                    string json = SaveGame.Load<string>("RemoteConfig");
                    var cachedConfig = JsonUtility.FromJson<CachedRemoteConfig>(json);
                    
                    if ((DateTime.UtcNow - cachedConfig.timestamp).TotalHours < cacheExpiryHours)
                    {
                        currentConfig = cachedConfig.config;
                        ApplyRemoteConfig();
                        configInitialized = true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load cached config: {e.Message}");
                }
            }
            
            // Load cached live events
            if (SaveGame.Exists("LiveEvents"))
            {
                try
                {
                    string json = SaveGame.Load<string>("LiveEvents");
                    var cachedEvents = JsonUtility.FromJson<CachedLiveEvents>(json);
                    
                    foreach (var eventData in cachedEvents.events)
                    {
                        if (eventData.endTime > DateTime.UtcNow)
                        {
                            activeLiveEvents.Add(eventData);
                            liveEventLookup[eventData.id] = eventData;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load cached events: {e.Message}");
                }
            }
            
            // Load A/B test results
            if (SaveGame.Exists("ABTestResults"))
            {
                try
                {
                    string json = SaveGame.Load<string>("ABTestResults");
                    var cachedABTests = JsonUtility.FromJson<CachedABTests>(json);
                    abTestResults = cachedABTests.results ?? new Dictionary<string, ABTestResult>();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load cached A/B tests: {e.Message}");
                }
            }
        }

        private void DeterminePlayerSegment()
        {
            // Determine player segment based on various factors
            var progressionManager = Progression.ProgressionManager.Instance;
            if (progressionManager != null)
            {
                int level = progressionManager.CurrentLevel;
                int coins = progressionManager.Coins;
                
                if (level >= 50 && coins >= 10000)
                {
                    playerSegment = "veteran";
                }
                else if (level >= 20)
                {
                    playerSegment = "experienced";
                }
                else if (level >= 5)
                {
                    playerSegment = "intermediate";
                }
                else
                {
                    playerSegment = "new_player";
                }
            }
            else
            {
                playerSegment = "new_player";
            }
            
            Debug.Log($"Player assigned to segment: {playerSegment}");
        }

        private IEnumerator InitializeLiveOps()
        {
            Debug.Log("Initializing Live Operations...");
            
            // Fetch remote config
            if (enableRemoteConfig)
            {
                yield return StartCoroutine(FetchRemoteConfig());
                
                if (configFetchInterval > 0)
                {
                    configUpdateCoroutine = StartCoroutine(ConfigUpdateRoutine());
                }
            }
            
            // Fetch live events
            if (enableLiveEvents)
            {
                yield return StartCoroutine(FetchLiveEvents());
                
                if (eventCheckInterval > 0)
                {
                    eventUpdateCoroutine = StartCoroutine(EventUpdateRoutine());
                }
            }
            
            // Initialize A/B tests
            if (enableABTesting)
            {
                yield return StartCoroutine(InitializeABTests());
            }
            
            // Start content delivery
            if (enableContentDelivery)
            {
                contentDeliveryCoroutine = StartCoroutine(ContentDeliveryRoutine());
            }
            
            // Schedule push notifications
            if (enablePushNotifications)
            {
                SchedulePushNotifications();
            }
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("live_ops_initialized", new Dictionary<string, object>
            {
                { "player_segment", playerSegment },
                { "active_events_count", activeLiveEvents.Count },
                { "ab_tests_count", abTestResults.Count }
            });
        }

        private void InitializeOfflineMode()
        {
            Debug.Log("Initializing offline mode with cached data");
            
            if (currentConfig == null)
            {
                // Use default config
                currentConfig = CreateDefaultConfig();
                ApplyRemoteConfig();
            }
            
            configInitialized = true;
        }

        private IEnumerator FetchRemoteConfig()
        {
            Debug.Log("Fetching remote configuration...");
            
            string url = $"{remoteConfigUrl}?version={Application.version}&platform={Application.platform}&segment={playerSegment}";
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        var remoteConfig = JsonUtility.FromJson<RemoteConfig>(json);
                        
                        if (remoteConfig != null)
                        {
                            currentConfig = remoteConfig;
                            ApplyRemoteConfig();
                            CacheRemoteConfig();
                            
                            lastConfigFetch = DateTime.UtcNow;
                            configInitialized = true;
                            
                            OnRemoteConfigUpdated?.Invoke();
                            
                            Debug.Log("Remote config updated successfully");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse remote config: {e.Message}");
                        OnLiveOpsError?.Invoke($"Config parse error: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to fetch remote config: {request.error}");
                    OnLiveOpsError?.Invoke($"Config fetch error: {request.error}");
                    
                    if (!configInitialized)
                    {
                        InitializeOfflineMode();
                    }
                }
            }
        }

        private RemoteConfig CreateDefaultConfig()
        {
            var config = new RemoteConfig
            {
                version = 1,
                timestamp = DateTime.UtcNow,
                parameters = new Dictionary<string, object>()
            };
            
            // Add default parameters
            foreach (var defaultConfig in configLookup.Values)
            {
                config.parameters[defaultConfig.key] = defaultConfig.defaultValue;
            }
            
            return config;
        }

        private void ApplyRemoteConfig()
        {
            if (currentConfig?.parameters == null) return;
            
            // Apply configuration parameters to game systems
            foreach (var parameter in currentConfig.parameters)
            {
                ApplyConfigParameter(parameter.Key, parameter.Value);
            }
        }

        private void ApplyConfigParameter(string key, object value)
        {
            switch (key)
            {
                case "coin_multiplier":
                    if (float.TryParse(value.ToString(), out float multiplier))
                    {
                        // Apply coin multiplier to game
                        Debug.Log($"Applied coin multiplier: {multiplier}");
                    }
                    break;
                    
                case "difficulty_modifier":
                    if (float.TryParse(value.ToString(), out float difficulty))
                    {
                        // Apply difficulty modifier
                        Debug.Log($"Applied difficulty modifier: {difficulty}");
                    }
                    break;
                    
                case "daily_reward_multiplier":
                    if (int.TryParse(value.ToString(), out int rewardMult))
                    {
                        // Apply daily reward multiplier
                        Debug.Log($"Applied daily reward multiplier: {rewardMult}");
                    }
                    break;
                    
                case "max_energy":
                    if (int.TryParse(value.ToString(), out int maxEnergy))
                    {
                        // Apply max energy setting
                        Debug.Log($"Applied max energy: {maxEnergy}");
                    }
                    break;
                    
                case "store_featured_item":
                    // Update featured store item
                    Debug.Log($"Featured store item: {value}");
                    break;
                    
                default:
                    Debug.Log($"Unhandled config parameter: {key} = {value}");
                    break;
            }
        }

        private void CacheRemoteConfig()
        {
            try
            {
                var cachedConfig = new CachedRemoteConfig
                {
                    config = currentConfig,
                    timestamp = DateTime.UtcNow
                };
                
                string json = JsonUtility.ToJson(cachedConfig);
                SaveGame.Save("RemoteConfig", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to cache remote config: {e.Message}");
            }
        }

        private IEnumerator FetchLiveEvents()
        {
            Debug.Log("Fetching live events...");
            
            string url = $"{liveEventsUrl}?segment={playerSegment}&timezone={TimeZoneInfo.Local.Id}";
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        var eventsResponse = JsonUtility.FromJson<LiveEventsResponse>(json);
                        
                        if (eventsResponse?.events != null)
                        {
                            UpdateLiveEvents(eventsResponse.events);
                            CacheLiveEvents();
                            
                            lastEventCheck = DateTime.UtcNow;
                            
                            Debug.Log($"Updated live events: {activeLiveEvents.Count} active events");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse live events: {e.Message}");
                        OnLiveOpsError?.Invoke($"Events parse error: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to fetch live events: {request.error}");
                    OnLiveOpsError?.Invoke($"Events fetch error: {request.error}");
                }
            }
        }

        private void UpdateLiveEvents(LiveEvent[] newEvents)
        {
            var now = DateTime.UtcNow;
            var previousEventIds = new HashSet<string>(liveEventLookup.Keys);
            
            // Clear expired events
            activeLiveEvents.RemoveAll(e => e.endTime <= now);
            
            foreach (var eventData in newEvents)
            {
                if (eventData.startTime <= now && eventData.endTime > now)
                {
                    bool isNewEvent = !liveEventLookup.ContainsKey(eventData.id);
                    
                    if (isNewEvent)
                    {
                        activeLiveEvents.Add(eventData);
                        liveEventLookup[eventData.id] = eventData;
                        OnLiveEventStarted?.Invoke(eventData);
                        
                        Debug.Log($"New live event started: {eventData.title}");
                        
                        // Analytics
                        AnalyticsManager.Instance?.TrackEvent("live_event_started", new Dictionary<string, object>
                        {
                            { "event_id", eventData.id },
                            { "event_type", eventData.type.ToString() },
                            { "duration_hours", (eventData.endTime - eventData.startTime).TotalHours }
                        });
                    }
                    
                    previousEventIds.Remove(eventData.id);
                }
            }
            
            // Handle ended events
            foreach (string endedEventId in previousEventIds)
            {
                if (liveEventLookup.TryGetValue(endedEventId, out LiveEvent endedEvent))
                {
                    OnLiveEventEnded?.Invoke(endedEvent);
                    liveEventLookup.Remove(endedEventId);
                    
                    Debug.Log($"Live event ended: {endedEvent.title}");
                }
            }
        }

        private void CacheLiveEvents()
        {
            try
            {
                var cachedEvents = new CachedLiveEvents
                {
                    events = activeLiveEvents.ToArray(),
                    timestamp = DateTime.UtcNow
                };
                
                string json = JsonUtility.ToJson(cachedEvents);
                SaveGame.Save("LiveEvents", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to cache live events: {e.Message}");
            }
        }

        private IEnumerator InitializeABTests()
        {
            Debug.Log("Initializing A/B tests...");
            
            string url = $"{abTestingUrl}?segment={playerSegment}&player_id={GetPlayerId()}";
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        var abTestResponse = JsonUtility.FromJson<ABTestResponse>(json);
                        
                        if (abTestResponse?.tests != null)
                        {
                            foreach (var test in abTestResponse.tests)
                            {
                                abTestResults[test.testId] = test;
                                OnABTestAssigned?.Invoke(test);
                                
                                Debug.Log($"A/B Test assigned: {test.testId} = {test.variant}");
                            }
                            
                            CacheABTestResults();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse A/B tests: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to fetch A/B tests: {request.error}");
                }
            }
        }

        private void CacheABTestResults()
        {
            try
            {
                var cachedABTests = new CachedABTests
                {
                    results = abTestResults,
                    timestamp = DateTime.UtcNow
                };
                
                string json = JsonUtility.ToJson(cachedABTests);
                SaveGame.Save("ABTestResults", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to cache A/B test results: {e.Message}");
            }
        }

        private IEnumerator ConfigUpdateRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(configFetchInterval);
                
                if (Application.internetReachability != NetworkReachability.NotReachable)
                {
                    yield return StartCoroutine(FetchRemoteConfig());
                }
            }
        }

        private IEnumerator EventUpdateRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(eventCheckInterval);
                
                if (Application.internetReachability != NetworkReachability.NotReachable)
                {
                    yield return StartCoroutine(FetchLiveEvents());
                }
                
                // Check for expired events even when offline
                CheckExpiredEvents();
            }
        }

        private void CheckExpiredEvents()
        {
            var now = DateTime.UtcNow;
            var expiredEvents = activeLiveEvents.FindAll(e => e.endTime <= now);
            
            foreach (var expiredEvent in expiredEvents)
            {
                activeLiveEvents.Remove(expiredEvent);
                liveEventLookup.Remove(expiredEvent.id);
                OnLiveEventEnded?.Invoke(expiredEvent);
                
                Debug.Log($"Live event expired: {expiredEvent.title}");
            }
        }

        private IEnumerator ContentDeliveryRoutine()
        {
            while (true)
            {
                if (contentRequestQueue.Count > 0)
                {
                    var request = contentRequestQueue.Dequeue();
                    yield return StartCoroutine(ProcessContentRequest(request));
                }
                else
                {
                    yield return new WaitForSeconds(1f);
                }
            }
        }

        private IEnumerator ProcessContentRequest(ContentRequest request)
        {
            string cacheKey = $"{request.contentId}_{request.version}";
            
            // Check cache first
            if (assetCache.TryGetValue(cacheKey, out CachedAsset cachedAsset))
            {
                if ((DateTime.UtcNow - cachedAsset.timestamp).TotalHours < cacheExpiryHours)
                {
                    request.callback?.Invoke(true, cachedAsset.data);
                    OnContentDelivered?.Invoke(request.contentId);
                    yield break;
                }
            }
            
            // Fetch from server
            string url = $"{contentDeliveryUrl}/{request.contentId}?version={request.version}";
            
            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                webRequest.timeout = 30;
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    var asset = new CachedAsset
                    {
                        contentId = request.contentId,
                        version = request.version,
                        data = webRequest.downloadHandler.data,
                        timestamp = DateTime.UtcNow
                    };
                    
                    // Cache the asset
                    assetCache[cacheKey] = asset;
                    
                    // Cleanup old cache if needed
                    if (assetCache.Count > maxCachedAssets)
                    {
                        CleanupAssetCache();
                    }
                    
                    request.callback?.Invoke(true, asset.data);
                    OnContentDelivered?.Invoke(request.contentId);
                    
                    Debug.Log($"Content delivered: {request.contentId}");
                }
                else
                {
                    Debug.LogError($"Failed to fetch content {request.contentId}: {webRequest.error}");
                    request.callback?.Invoke(false, null);
                }
            }
        }

        private void CleanupAssetCache()
        {
            // Remove oldest cached assets
            var sortedAssets = new List<KeyValuePair<string, CachedAsset>>(assetCache);
            sortedAssets.Sort((a, b) => a.Value.timestamp.CompareTo(b.Value.timestamp));
            
            int assetsToRemove = assetCache.Count - maxCachedAssets + 10; // Remove 10 extra
            for (int i = 0; i < assetsToRemove && i < sortedAssets.Count; i++)
            {
                assetCache.Remove(sortedAssets[i].Key);
            }
        }

        private void SchedulePushNotifications()
        {
            if (scheduledNotifications == null) return;
            
            foreach (var notification in scheduledNotifications)
            {
                ScheduleNotification(notification);
            }
        }

        private void ScheduleNotification(NotificationSchedule notification)
        {
            // This would integrate with Unity Mobile Notifications or a custom push service
            Debug.Log($"Scheduled notification: {notification.title} at {notification.scheduledTime}");
        }

        // Public API
        public T GetConfigValue<T>(string key, T defaultValue = default(T))
        {
            if (currentConfig?.parameters != null && currentConfig.parameters.TryGetValue(key, out object value))
            {
                try
                {
                    if (value is T directValue)
                        return directValue;
                    
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to convert config value for key '{key}': {e.Message}");
                }
            }
            
            // Fall back to default config
            if (configLookup.TryGetValue(key, out RemoteConfigEntry entry))
            {
                try
                {
                    return (T)Convert.ChangeType(entry.defaultValue, typeof(T));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to convert default config value for key '{key}': {e.Message}");
                }
            }
            
            return defaultValue;
        }

        public bool IsLiveEventActive(string eventId)
        {
            return liveEventLookup.ContainsKey(eventId);
        }

        public LiveEvent GetLiveEvent(string eventId)
        {
            return liveEventLookup.TryGetValue(eventId, out LiveEvent liveEvent) ? liveEvent : null;
        }

        public List<LiveEvent> GetLiveEventsByType(LiveEventType eventType)
        {
            return activeLiveEvents.FindAll(e => e.type == eventType);
        }

        public string GetABTestVariant(string testId, string defaultVariant = "control")
        {
            return abTestResults.TryGetValue(testId, out ABTestResult result) ? result.variant : defaultVariant;
        }

        public void RequestContent(string contentId, int version, Action<bool, byte[]> callback)
        {
            var request = new ContentRequest
            {
                contentId = contentId,
                version = version,
                callback = callback,
                timestamp = DateTime.UtcNow
            };
            
            contentRequestQueue.Enqueue(request);
        }

        public void TrackABTestConversion(string testId, string eventName, float value = 0f)
        {
            if (abTestResults.TryGetValue(testId, out ABTestResult result))
            {
                AnalyticsManager.Instance?.TrackEvent("ab_test_conversion", new Dictionary<string, object>
                {
                    { "test_id", testId },
                    { "variant", result.variant },
                    { "event_name", eventName },
                    { "value", value }
                });
            }
        }

        public void ForceRefreshConfig()
        {
            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                StartCoroutine(FetchRemoteConfig());
            }
        }

        public void ForceRefreshEvents()
        {
            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                StartCoroutine(FetchLiveEvents());
            }
        }

        private string GetPlayerId()
        {
            // Get player ID from progression manager or generate one
            return ProgressionManager.Instance?.PlayerData?.playerId ?? SystemInfo.deviceUniqueIdentifier;
        }

        void OnDestroy()
        {
            if (configUpdateCoroutine != null)
                StopCoroutine(configUpdateCoroutine);
                
            if (eventUpdateCoroutine != null)
                StopCoroutine(eventUpdateCoroutine);
                
            if (contentDeliveryCoroutine != null)
                StopCoroutine(contentDeliveryCoroutine);
        }
    }

    // Data structures
    [System.Serializable]
    public class RemoteConfig
    {
        public int version;
        public DateTime timestamp;
        public Dictionary<string, object> parameters;
    }

    [System.Serializable]
    public class RemoteConfigEntry
    {
        public string key;
        public object defaultValue;
        public string description;
        public ConfigValueType valueType;
    }

    [System.Serializable]
    public class LiveEvent
    {
        public string id;
        public string title;
        public string description;
        public LiveEventType type;
        public DateTime startTime;
        public DateTime endTime;
        public Dictionary<string, object> parameters;
        public EventReward[] rewards;
        public string[] targetSegments;
    }

    [System.Serializable]
    public class EventReward
    {
        public string rewardType;
        public int amount;
        public string itemId;
        public float probability;
    }

    [System.Serializable]
    public class ABTestConfig
    {
        public string testId;
        public string description;
        public ABTestVariant[] variants;
        public string[] targetSegments;
        public DateTime startDate;
        public DateTime endDate;
    }

    [System.Serializable]
    public class ABTestVariant
    {
        public string name;
        public float trafficPercentage;
        public Dictionary<string, object> parameters;
    }

    [System.Serializable]
    public class ABTestResult
    {
        public string testId;
        public string variant;
        public DateTime assignedDate;
    }

    [System.Serializable]
    public class NotificationSchedule
    {
        public string id;
        public string title;
        public string message;
        public DateTime scheduledTime;
        public bool repeating;
        public TimeSpan repeatInterval;
    }

    [System.Serializable]
    public class ContentRequest
    {
        public string contentId;
        public int version;
        public Action<bool, byte[]> callback;
        public DateTime timestamp;
    }

    [System.Serializable]
    public class CachedAsset
    {
        public string contentId;
        public int version;
        public byte[] data;
        public DateTime timestamp;
    }

    // Response classes for JSON deserialization
    [System.Serializable]
    public class LiveEventsResponse
    {
        public LiveEvent[] events;
    }

    [System.Serializable]
    public class ABTestResponse
    {
        public ABTestResult[] tests;
    }

    // Caching classes
    [System.Serializable]
    public class CachedRemoteConfig
    {
        public RemoteConfig config;
        public DateTime timestamp;
    }

    [System.Serializable]
    public class CachedLiveEvents
    {
        public LiveEvent[] events;
        public DateTime timestamp;
    }

    [System.Serializable]
    public class CachedABTests
    {
        public Dictionary<string, ABTestResult> results;
        public DateTime timestamp;
    }

    // Enums
    public enum LiveEventType
    {
        DoubleCoins,
        ExperienceBoost,
        SpecialOffer,
        Tournament,
        SeasonalEvent,
        CommunityChallenge
    }

    public enum ConfigValueType
    {
        String,
        Integer,
        Float,
        Boolean,
        JSON
    }
}