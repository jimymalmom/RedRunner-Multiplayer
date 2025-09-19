using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

namespace RedRunner.Analytics
{
    /// <summary>
    /// Comprehensive analytics system for tracking player behavior and game metrics
    /// Supports multiple analytics providers and custom event tracking
    /// </summary>
    public class AnalyticsManager : MonoBehaviour
    {
        private static AnalyticsManager instance;
        public static AnalyticsManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("AnalyticsManager");
                    instance = go.AddComponent<AnalyticsManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }
        
        [Header("Configuration")]
        [SerializeField] private bool enableAnalytics = true;
        [SerializeField] private bool enableDebugLogging = false;
        [SerializeField] private float batchUploadInterval = 30f;
        [SerializeField] private int maxEventBatchSize = 50;
        
        [Header("Session Settings")]
        [SerializeField] private float sessionTimeoutSeconds = 300f; // 5 minutes
        
        // Event batching
        private Queue<AnalyticsEvent> eventQueue;
        private Coroutine uploadCoroutine;
        
        // Session tracking
        private SessionData currentSession;
        private float lastActivityTime;
        
        // Player data
        private PlayerAnalyticsData playerData;
        
        // Cached data for offline support
        private List<AnalyticsEvent> cachedEvents;
        
        public bool IsAnalyticsEnabled => enableAnalytics;
        public SessionData CurrentSession => currentSession;
        public PlayerAnalyticsData PlayerData => playerData;
        
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
            eventQueue = new Queue<AnalyticsEvent>();
            cachedEvents = new List<AnalyticsEvent>();
            
            LoadPlayerData();
            StartNewSession();
            
            if (enableAnalytics)
            {
                uploadCoroutine = StartCoroutine(BatchUploadRoutine());
            }
        }
        
        private void LoadPlayerData()
        {
            if (PlayerPrefs.HasKey("AnalyticsPlayerData"))
            {
                try
                {
                    string json = PlayerPrefs.GetString("AnalyticsPlayerData");
                    playerData = JsonUtility.FromJson<PlayerAnalyticsData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load player analytics data: {e.Message}");
                    playerData = new PlayerAnalyticsData();
                }
            }
            else
            {
                playerData = new PlayerAnalyticsData();
                playerData.playerId = GeneratePlayerId();
                playerData.firstSeenDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            
            playerData.lastSeenDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            SavePlayerData();
        }
        
        private void SavePlayerData()
        {
            try
            {
                string json = JsonUtility.ToJson(playerData);
                PlayerPrefs.SetString("AnalyticsPlayerData", json);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save player analytics data: {e.Message}");
            }
        }
        
        private string GeneratePlayerId()
        {
            return System.Guid.NewGuid().ToString();
        }
        
        private void StartNewSession()
        {
            currentSession = new SessionData
            {
                sessionId = System.Guid.NewGuid().ToString(),
                startTime = DateTime.UtcNow,
                platform = Application.platform.ToString(),
                appVersion = Application.version,
                deviceModel = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem
            };
            
            lastActivityTime = Time.time;
            
            TrackEvent("session_start", new Dictionary<string, object>
            {
                { "session_id", currentSession.sessionId },
                { "platform", currentSession.platform },
                { "device_model", currentSession.deviceModel },
                { "os", currentSession.operatingSystem },
                { "app_version", currentSession.appVersion }
            });
        }
        
        private void EndCurrentSession()
        {
            if (currentSession != null)
            {
                currentSession.endTime = DateTime.UtcNow;
                currentSession.duration = (float)(currentSession.endTime - currentSession.startTime).TotalSeconds;
                
                TrackEvent("session_end", new Dictionary<string, object>
                {
                    { "session_id", currentSession.sessionId },
                    { "duration", currentSession.duration },
                    { "levels_played", currentSession.levelsPlayed },
                    { "coins_collected", currentSession.coinsCollected },
                    { "deaths", currentSession.deaths }
                });
            }
        }
        
        void Update()
        {
            // Check for session timeout
            if (currentSession != null && Time.time - lastActivityTime > sessionTimeoutSeconds)
            {
                EndCurrentSession();
                StartNewSession();
            }
        }
        
        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                EndCurrentSession();
                ForceUploadEvents();
            }
            else
            {
                StartNewSession();
            }
        }
        
        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                EndCurrentSession();
                ForceUploadEvents();
            }
            else
            {
                StartNewSession();
            }
        }
        
        void OnDestroy()
        {
            EndCurrentSession();
            ForceUploadEvents();
        }
        
        /// <summary>
        /// Track a custom event with parameters
        /// </summary>
        public void TrackEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            if (!enableAnalytics) return;
            
            lastActivityTime = Time.time;
            
            var analyticsEvent = new AnalyticsEvent
            {
                eventName = eventName,
                timestamp = DateTime.UtcNow,
                sessionId = currentSession?.sessionId,
                playerId = playerData?.playerId,
                parameters = parameters ?? new Dictionary<string, object>()
            };
            
            // Add standard parameters
            analyticsEvent.parameters["game_version"] = Application.version;
            analyticsEvent.parameters["platform"] = Application.platform.ToString();
            analyticsEvent.parameters["session_time"] = Time.time;
            
            eventQueue.Enqueue(analyticsEvent);
            
            if (enableDebugLogging)
            {
                Debug.Log($"Analytics Event: {eventName} - {JsonUtility.ToJson(analyticsEvent)}");
            }
        }
        
        /// <summary>
        /// Track game progression events
        /// </summary>
        public void TrackProgression(string progressionStatus, string progression01, int score = 0, Dictionary<string, object> customParams = null)
        {
            var parameters = new Dictionary<string, object>
            {
                { "progression_status", progressionStatus },
                { "progression_01", progression01 },
                { "score", score }
            };
            
            if (customParams != null)
            {
                foreach (var kvp in customParams)
                {
                    parameters[kvp.Key] = kvp.Value;
                }
            }
            
            TrackEvent("progression", parameters);
            
            // Update player data
            if (progressionStatus == "complete")
            {
                playerData.totalLevelsCompleted++;
                if (currentSession != null)
                    currentSession.levelsPlayed++;
            }
            
            SavePlayerData();
        }
        
        /// <summary>
        /// Track resource events (coins, gems, etc.)
        /// </summary>
        public void TrackResourceEvent(string flowType, string currency, int amount, string itemType = "", string itemId = "")
        {
            var parameters = new Dictionary<string, object>
            {
                { "flow_type", flowType }, // "source" or "sink"
                { "currency", currency },
                { "amount", amount },
                { "item_type", itemType },
                { "item_id", itemId }
            };
            
            TrackEvent("resource", parameters);
            
            // Update player data
            if (currency == "coins")
            {
                if (flowType == "source")
                {
                    playerData.totalCoinsEarned += amount;
                    if (currentSession != null)
                        currentSession.coinsCollected += amount;
                }
                else if (flowType == "sink")
                {
                    playerData.totalCoinsSpent += amount;
                }
            }
            
            SavePlayerData();
        }
        
        /// <summary>
        /// Track design events (game balance and design insights)
        /// </summary>
        public void TrackDesignEvent(string eventId, float value = 0f, Dictionary<string, object> customParams = null)
        {
            var parameters = new Dictionary<string, object>
            {
                { "event_id", eventId },
                { "value", value }
            };
            
            if (customParams != null)
            {
                foreach (var kvp in customParams)
                {
                    parameters[kvp.Key] = kvp.Value;
                }
            }
            
            TrackEvent("design", parameters);
        }
        
        /// <summary>
        /// Track error events for debugging and stability monitoring
        /// </summary>
        public void TrackErrorEvent(string severity, string message, string stackTrace = "")
        {
            var parameters = new Dictionary<string, object>
            {
                { "severity", severity },
                { "message", message },
                { "stack_trace", stackTrace },
                { "device_model", SystemInfo.deviceModel },
                { "graphics_device", SystemInfo.graphicsDeviceName },
                { "memory_size", SystemInfo.systemMemorySize }
            };
            
            TrackEvent("error", parameters);
        }
        
        /// <summary>
        /// Track business events (IAP, ads, etc.)
        /// </summary>
        public void TrackBusinessEvent(string currency, int amount, string itemType, string itemId, string receipt = "")
        {
            var parameters = new Dictionary<string, object>
            {
                { "currency", currency },
                { "amount", amount },
                { "item_type", itemType },
                { "item_id", itemId },
                { "receipt", receipt },
                { "transaction_num", playerData.totalPurchases + 1 }
            };
            
            TrackEvent("business", parameters);
            
            // Update player data
            playerData.totalPurchases++;
            playerData.totalRevenue += amount;
            SavePlayerData();
        }
        
        /// <summary>
        /// Track player death events
        /// </summary>
        public void TrackPlayerDeath(Vector3 deathPosition, string causeOfDeath, float survivalTime, int coinsCollected)
        {
            var parameters = new Dictionary<string, object>
            {
                { "death_position_x", deathPosition.x },
                { "death_position_y", deathPosition.y },
                { "cause_of_death", causeOfDeath },
                { "survival_time", survivalTime },
                { "coins_collected", coinsCollected },
                { "distance_traveled", deathPosition.x }
            };
            
            TrackEvent("player_death", parameters);
            
            // Update session data
            if (currentSession != null)
                currentSession.deaths++;
        }
        
        /// <summary>
        /// Track multiplayer events
        /// </summary>
        public void TrackMultiplayerEvent(string eventType, int playerCount, string matchId = "", Dictionary<string, object> customParams = null)
        {
            var parameters = new Dictionary<string, object>
            {
                { "event_type", eventType },
                { "player_count", playerCount },
                { "match_id", matchId }
            };
            
            if (customParams != null)
            {
                foreach (var kvp in customParams)
                {
                    parameters[kvp.Key] = kvp.Value;
                }
            }
            
            TrackEvent("multiplayer", parameters);
        }
        
        private IEnumerator BatchUploadRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(batchUploadInterval);
                
                if (eventQueue.Count > 0)
                {
                    UploadEventBatch();
                }
            }
        }
        
        private void UploadEventBatch()
        {
            var batch = new List<AnalyticsEvent>();
            int count = Mathf.Min(eventQueue.Count, maxEventBatchSize);
            
            for (int i = 0; i < count; i++)
            {
                if (eventQueue.Count > 0)
                {
                    batch.Add(eventQueue.Dequeue());
                }
            }
            
            if (batch.Count > 0)
            {
                StartCoroutine(SendEventBatch(batch));
            }
        }
        
        private IEnumerator SendEventBatch(List<AnalyticsEvent> events)
        {
            // TODO: Implement actual HTTP upload to analytics server
            // For now, we'll just log the events
            
            if (enableDebugLogging)
            {
                Debug.Log($"Uploading {events.Count} analytics events");
            }
            
            // Simulate network delay
            yield return new WaitForSeconds(1f);
            
            // In a real implementation, you would:
            // 1. Serialize events to JSON
            // 2. Send HTTP POST request to analytics endpoint
            // 3. Handle response and retry on failure
            // 4. Cache failed events for later retry
            
            foreach (var analyticsEvent in events)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"Event uploaded: {analyticsEvent.eventName}");
                }
            }
        }
        
        private void ForceUploadEvents()
        {
            while (eventQueue.Count > 0)
            {
                cachedEvents.Add(eventQueue.Dequeue());
            }
            
            // Save cached events to persistent storage
            SaveCachedEvents();
        }
        
        private void SaveCachedEvents()
        {
            try
            {
                string json = JsonUtility.ToJson(new SerializableEventList { events = cachedEvents });
                PlayerPrefs.SetString("CachedAnalyticsEvents", json);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save cached analytics events: {e.Message}");
            }
        }
        
        private void LoadCachedEvents()
        {
            if (PlayerPrefs.HasKey("CachedAnalyticsEvents"))
            {
                try
                {
                    string json = PlayerPrefs.GetString("CachedAnalyticsEvents");
                    var eventList = JsonUtility.FromJson<SerializableEventList>(json);
                    
                    foreach (var cachedEvent in eventList.events)
                    {
                        eventQueue.Enqueue(cachedEvent);
                    }
                    
                    // Clear cached events after loading
                    PlayerPrefs.DeleteKey("CachedAnalyticsEvents");
                    cachedEvents.Clear();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load cached analytics events: {e.Message}");
                }
            }
        }
    }
    
    [System.Serializable]
    public class AnalyticsEvent
    {
        public string eventName;
        public DateTime timestamp;
        public string sessionId;
        public string playerId;
        public Dictionary<string, object> parameters;
    }
    
    [System.Serializable]
    public class SessionData
    {
        public string sessionId;
        public DateTime startTime;
        public DateTime endTime;
        public float duration;
        public string platform;
        public string appVersion;
        public string deviceModel;
        public string operatingSystem;
        public int levelsPlayed;
        public int coinsCollected;
        public int deaths;
    }
    
    [System.Serializable]
    public class PlayerAnalyticsData
    {
        public string playerId;
        public string firstSeenDate;
        public string lastSeenDate;
        public int totalSessions;
        public float totalPlayTime;
        public int totalLevelsCompleted;
        public int totalCoinsEarned;
        public int totalCoinsSpent;
        public int totalPurchases;
        public float totalRevenue;
        public int totalDeaths;
    }
    
    [System.Serializable]
    public class SerializableEventList
    {
        public List<AnalyticsEvent> events;
    }
}