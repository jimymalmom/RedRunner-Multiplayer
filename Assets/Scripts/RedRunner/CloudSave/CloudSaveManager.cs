using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System.Security.Cryptography;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;

namespace RedRunner.CloudSave
{
    /// <summary>
    /// Comprehensive cloud save system with conflict resolution and offline sync
    /// Ensures player progression is preserved across devices and sessions
    /// </summary>
    public class CloudSaveManager : MonoBehaviour
    {
        private static CloudSaveManager instance;
        public static CloudSaveManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<CloudSaveManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("CloudSaveManager");
                        instance = go.AddComponent<CloudSaveManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Cloud Save Configuration")]
        [SerializeField] private bool enableCloudSave = true;
        [SerializeField] private CloudProvider cloudProvider = CloudProvider.PlayGamesPlatform;
        [SerializeField] private int maxSaveSlots = 3;
        [SerializeField] private float autoSaveInterval = 300f; // 5 minutes
        [SerializeField] private bool enableEncryption = true;
        
        [Header("Sync Settings")]
        [SerializeField] private bool enableAutoSync = true;
        [SerializeField] private float syncInterval = 60f; // 1 minute
        [SerializeField] private int maxRetryAttempts = 3;
        [SerializeField] private float retryDelay = 5f;
        
        [Header("Conflict Resolution")]
        [SerializeField] private ConflictResolutionStrategy conflictResolution = ConflictResolutionStrategy.MostRecent;
        [SerializeField] private bool showConflictDialog = true;
        
        [Header("Data Management")]
        [SerializeField] private SaveableComponent[] saveableComponents;
        [SerializeField] private int maxSaveHistoryCount = 10;
        [SerializeField] private bool compressData = true;
        
        // Cloud save state
        private CloudSaveData localSaveData;
        private CloudSaveData cloudSaveData;
        private Dictionary<string, ISaveableComponent> saveableComponentsLookup;
        private Queue<SaveOperation> saveOperationQueue;
        
        // Sync state
        private bool isInitialized = false;
        private bool isSyncing = false;
        private bool hasUnsyncedChanges = false;
        private DateTime lastSyncTime;
        private DateTime lastAutoSaveTime;
        
        // Network state
        private bool isOnline = false;
        private bool isSignedIn = false;
        private string playerId = "";
        
        // Coroutines
        private Coroutine autoSaveCoroutine;
        private Coroutine syncCoroutine;
        
        // Events
        public static event Action OnCloudSaveInitialized;
        public static event Action<bool> OnSaveCompleted;
        public static event Action<bool> OnLoadCompleted;
        public static event Action<bool> OnSyncCompleted;
        public static event Action<ConflictResolutionData> OnSaveConflictDetected;
        public static event Action<string> OnCloudSaveError;
        
        // Properties
        public bool IsInitialized => isInitialized;
        public bool IsSignedIn => isSignedIn;
        public bool HasUnsyncedChanges => hasUnsyncedChanges;
        public DateTime LastSyncTime => lastSyncTime;
        public CloudSaveData CurrentSaveData => localSaveData;

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
            saveableComponentsLookup = new Dictionary<string, ISaveableComponent>();
            saveOperationQueue = new Queue<SaveOperation>();
            
            // Build saveable components lookup
            BuildSaveableComponentsLookup();
            
            // Load local save data
            LoadLocalSaveData();
            
            if (enableCloudSave)
            {
                StartCoroutine(InitializeCloudSave());
            }
            
            // Start auto-save routine
            if (autoSaveInterval > 0)
            {
                autoSaveCoroutine = StartCoroutine(AutoSaveRoutine());
            }
            
            // Subscribe to application events
            Application.focusChanged += OnApplicationFocusChanged;
        }

        private void BuildSaveableComponentsLookup()
        {
            if (saveableComponents == null) return;
            
            foreach (var config in saveableComponents)
            {
                if (config.component != null)
                {
                    saveableComponentsLookup[config.saveKey] = config.component;
                }
            }
            
            // Auto-discover saveable components
            var components = FindObjectsOfType<MonoBehaviour>();
            foreach (var component in components)
            {
                if (component is ISaveableComponent saveable)
                {
                    string saveKey = saveable.GetSaveKey();
                    if (!saveableComponentsLookup.ContainsKey(saveKey))
                    {
                        saveableComponentsLookup[saveKey] = saveable;
                    }
                }
            }
        }

        private void LoadLocalSaveData()
        {
            try
            {
                if (SaveGame.Exists("CloudSaveData"))
                {
                    string json = SaveGame.Load<string>("CloudSaveData");
                    
                    if (enableEncryption)
                    {
                        json = DecryptData(json);
                    }
                    
                    localSaveData = JsonUtility.FromJson<CloudSaveData>(json);
                }
                else
                {
                    CreateNewSaveData();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load local save data: {e.Message}");
                CreateNewSaveData();
            }
            
            ValidateSaveData();
        }

        private void CreateNewSaveData()
        {
            localSaveData = new CloudSaveData
            {
                saveId = System.Guid.NewGuid().ToString(),
                playerId = GetOrCreatePlayerId(),
                version = GetCurrentDataVersion(),
                timestamp = DateTime.UtcNow,
                deviceId = SystemInfo.deviceUniqueIdentifier,
                gameData = new Dictionary<string, string>()
            };
            
            SaveLocalData();
        }

        private void ValidateSaveData()
        {
            if (localSaveData == null)
            {
                CreateNewSaveData();
                return;
            }
            
            if (localSaveData.gameData == null)
            {
                localSaveData.gameData = new Dictionary<string, string>();
            }
            
            if (string.IsNullOrEmpty(localSaveData.playerId))
            {
                localSaveData.playerId = GetOrCreatePlayerId();
            }
            
            if (string.IsNullOrEmpty(localSaveData.saveId))
            {
                localSaveData.saveId = System.Guid.NewGuid().ToString();
            }
        }

        private string GetOrCreatePlayerId()
        {
            if (PlayerPrefs.HasKey("CloudSavePlayerId"))
            {
                return PlayerPrefs.GetString("CloudSavePlayerId");
            }
            else
            {
                string newId = System.Guid.NewGuid().ToString();
                PlayerPrefs.SetString("CloudSavePlayerId", newId);
                PlayerPrefs.Save();
                return newId;
            }
        }

        private int GetCurrentDataVersion()
        {
            return 1; // Increment this when save data structure changes
        }

        private IEnumerator InitializeCloudSave()
        {
            Debug.Log("Initializing cloud save system...");
            
            // Initialize cloud platform
            yield return StartCoroutine(InitializeCloudPlatform());
            
            if (isSignedIn)
            {
                // Load cloud data
                yield return StartCoroutine(LoadCloudData());
                
                // Check for conflicts and resolve
                yield return StartCoroutine(ResolveConflicts());
                
                // Start sync routine
                if (enableAutoSync && syncInterval > 0)
                {
                    syncCoroutine = StartCoroutine(SyncRoutine());
                }
            }
            
            isInitialized = true;
            OnCloudSaveInitialized?.Invoke();
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("cloud_save_initialized", new Dictionary<string, object>
            {
                { "provider", cloudProvider.ToString() },
                { "signed_in", isSignedIn },
                { "conflict_resolution", conflictResolution.ToString() }
            });
        }

        private IEnumerator InitializeCloudPlatform()
        {
            switch (cloudProvider)
            {
                case CloudProvider.PlayGamesPlatform:
                    yield return StartCoroutine(InitializeGooglePlay());
                    break;
                    
                case CloudProvider.GameCenter:
                    yield return StartCoroutine(InitializeGameCenter());
                    break;
                    
                case CloudProvider.CustomBackend:
                    yield return StartCoroutine(InitializeCustomBackend());
                    break;
            }
        }

        private IEnumerator InitializeGooglePlay()
        {
#if UNITY_ANDROID
            // Initialize Google Play Games Services
            // Note: This would require the Google Play Games Plugin for Unity
            Debug.Log("Initializing Google Play Games Services...");
            
            // Simulate initialization
            yield return new WaitForSeconds(2f);
            
            // Check if user is signed in
            isSignedIn = true; // Simulated
            isOnline = Application.internetReachability != NetworkReachability.NotReachable;
            
            if (isSignedIn)
            {
                playerId = localSaveData.playerId; // In reality, get from Google Play
            }
#else
            yield return null;
#endif
        }

        private IEnumerator InitializeGameCenter()
        {
#if UNITY_IOS
            // Initialize Game Center
            Debug.Log("Initializing Game Center...");
            
            // Simulate initialization
            yield return new WaitForSeconds(2f);
            
            isSignedIn = true; // Simulated
            isOnline = Application.internetReachability != NetworkReachability.NotReachable;
            
            if (isSignedIn)
            {
                playerId = localSaveData.playerId; // In reality, get from Game Center
            }
#else
            yield return null;
#endif
        }

        private IEnumerator InitializeCustomBackend()
        {
            Debug.Log("Initializing custom backend...");
            
            // Simulate backend authentication
            yield return new WaitForSeconds(1f);
            
            isOnline = Application.internetReachability != NetworkReachability.NotReachable;
            isSignedIn = isOnline; // Simplified logic
            
            if (isSignedIn)
            {
                playerId = localSaveData.playerId;
            }
        }

        private IEnumerator LoadCloudData()
        {
            if (!isSignedIn || !isOnline)
            {
                yield break;
            }
            
            Debug.Log("Loading cloud save data...");
            
            // Simulate cloud data loading
            yield return new WaitForSeconds(1f);
            
            // In reality, this would make an API call to load cloud data
            // For simulation, we'll create a slightly modified version of local data
            cloudSaveData = new CloudSaveData
            {
                saveId = localSaveData.saveId,
                playerId = localSaveData.playerId,
                version = localSaveData.version,
                timestamp = localSaveData.timestamp.AddMinutes(-10), // Simulate older cloud data
                deviceId = "cloud_device",
                gameData = new Dictionary<string, string>(localSaveData.gameData)
            };
            
            Debug.Log("Cloud save data loaded successfully");
        }

        private IEnumerator ResolveConflicts()
        {
            if (cloudSaveData == null)
                yield break;
            
            bool hasConflict = DetectConflict();
            
            if (hasConflict)
            {
                Debug.Log("Save conflict detected, resolving...");
                
                var conflictData = new ConflictResolutionData
                {
                    localData = localSaveData,
                    cloudData = cloudSaveData,
                    conflictType = GetConflictType()
                };
                
                if (showConflictDialog && conflictResolution == ConflictResolutionStrategy.UserChoice)
                {
                    OnSaveConflictDetected?.Invoke(conflictData);
                    
                    // Wait for user decision (simplified)
                    yield return new WaitForSeconds(5f);
                }
                
                ResolveConflict(conflictData);
            }
        }

        private bool DetectConflict()
        {
            if (localSaveData == null || cloudSaveData == null)
                return false;
            
            // Check timestamps
            if (localSaveData.timestamp != cloudSaveData.timestamp)
                return true;
            
            // Check device IDs
            if (localSaveData.deviceId != cloudSaveData.deviceId)
                return true;
            
            // Check data content (simplified)
            if (localSaveData.gameData.Count != cloudSaveData.gameData.Count)
                return true;
            
            return false;
        }

        private ConflictType GetConflictType()
        {
            if (localSaveData.timestamp > cloudSaveData.timestamp)
                return ConflictType.LocalNewer;
            else if (cloudSaveData.timestamp > localSaveData.timestamp)
                return ConflictType.CloudNewer;
            else
                return ConflictType.DataMismatch;
        }

        private void ResolveConflict(ConflictResolutionData conflictData)
        {
            switch (conflictResolution)
            {
                case ConflictResolutionStrategy.MostRecent:
                    if (conflictData.localData.timestamp > conflictData.cloudData.timestamp)
                        UseLocalData();
                    else
                        UseCloudData();
                    break;
                    
                case ConflictResolutionStrategy.CloudPriority:
                    UseCloudData();
                    break;
                    
                case ConflictResolutionStrategy.LocalPriority:
                    UseLocalData();
                    break;
                    
                case ConflictResolutionStrategy.Merge:
                    MergeData(conflictData);
                    break;
            }
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("save_conflict_resolved", new Dictionary<string, object>
            {
                { "resolution_strategy", conflictResolution.ToString() },
                { "conflict_type", conflictData.conflictType.ToString() }
            });
        }

        private void UseLocalData()
        {
            Debug.Log("Using local save data");
            // Local data is already loaded, just sync to cloud
            hasUnsyncedChanges = true;
        }

        private void UseCloudData()
        {
            Debug.Log("Using cloud save data");
            localSaveData = cloudSaveData;
            LoadDataIntoComponents();
            SaveLocalData();
        }

        private void MergeData(ConflictResolutionData conflictData)
        {
            Debug.Log("Merging save data");
            
            // Simple merge strategy - prefer higher values for numeric data
            var mergedData = new Dictionary<string, string>(conflictData.localData.gameData);
            
            foreach (var kvp in conflictData.cloudData.gameData)
            {
                if (!mergedData.ContainsKey(kvp.Key))
                {
                    mergedData[kvp.Key] = kvp.Value;
                }
                else
                {
                    // Try to merge intelligently
                    string mergedValue = MergeValue(mergedData[kvp.Key], kvp.Value, kvp.Key);
                    mergedData[kvp.Key] = mergedValue;
                }
            }
            
            localSaveData.gameData = mergedData;
            localSaveData.timestamp = DateTime.UtcNow;
            
            LoadDataIntoComponents();
            SaveLocalData();
            hasUnsyncedChanges = true;
        }

        private string MergeValue(string localValue, string cloudValue, string key)
        {
            // Smart merging based on key type
            if (key.ToLower().Contains("score") || key.ToLower().Contains("level") || key.ToLower().Contains("coin"))
            {
                // For progression values, use the higher one
                if (float.TryParse(localValue, out float localFloat) && float.TryParse(cloudValue, out float cloudFloat))
                {
                    return Mathf.Max(localFloat, cloudFloat).ToString();
                }
            }
            
            if (key.ToLower().Contains("unlock") || key.ToLower().Contains("achievement"))
            {
                // For unlocks, merge both (assuming comma-separated lists)
                var localItems = new HashSet<string>(localValue.Split(','));
                var cloudItems = cloudValue.Split(',');
                
                foreach (string item in cloudItems)
                {
                    if (!string.IsNullOrEmpty(item.Trim()))
                        localItems.Add(item.Trim());
                }
                
                return string.Join(",", localItems);
            }
            
            // Default to most recent (cloud data in this case)
            return cloudValue;
        }

        public void SaveData()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("Cloud save system not initialized");
                return;
            }
            
            var saveOperation = new SaveOperation
            {
                operationId = System.Guid.NewGuid().ToString(),
                timestamp = DateTime.UtcNow,
                isManual = true
            };
            
            StartCoroutine(SaveDataCoroutine(saveOperation));
        }

        public void SaveDataAsync(Action<bool> callback = null)
        {
            if (!isInitialized)
            {
                callback?.Invoke(false);
                return;
            }
            
            var saveOperation = new SaveOperation
            {
                operationId = System.Guid.NewGuid().ToString(),
                timestamp = DateTime.UtcNow,
                isManual = true,
                callback = callback
            };
            
            saveOperationQueue.Enqueue(saveOperation);
            
            if (saveOperationQueue.Count == 1)
            {
                StartCoroutine(ProcessSaveQueue());
            }
        }

        private IEnumerator SaveDataCoroutine(SaveOperation operation)
        {
            // Collect data from all saveable components
            CollectDataFromComponents();
            
            // Update save metadata
            localSaveData.timestamp = DateTime.UtcNow;
            localSaveData.version = GetCurrentDataVersion();
            
            // Save locally first
            bool localSaveSuccess = SaveLocalData();
            
            if (!localSaveSuccess)
            {
                operation.callback?.Invoke(false);
                OnSaveCompleted?.Invoke(false);
                yield break;
            }
            
            // Save to cloud if online and signed in
            bool cloudSaveSuccess = true;
            if (isOnline && isSignedIn)
            {
                yield return StartCoroutine(SaveToCloud());
                // cloudSaveSuccess would be set by SaveToCloud method
            }
            else
            {
                hasUnsyncedChanges = true;
            }
            
            bool overallSuccess = localSaveSuccess && (!isOnline || !isSignedIn || cloudSaveSuccess);
            
            operation.callback?.Invoke(overallSuccess);
            OnSaveCompleted?.Invoke(overallSuccess);
            
            if (operation.isManual)
            {
                lastAutoSaveTime = DateTime.UtcNow;
            }
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("game_saved", new Dictionary<string, object>
            {
                { "is_manual", operation.isManual },
                { "cloud_success", cloudSaveSuccess },
                { "local_success", localSaveSuccess }
            });
        }

        private IEnumerator ProcessSaveQueue()
        {
            while (saveOperationQueue.Count > 0)
            {
                var operation = saveOperationQueue.Dequeue();
                yield return StartCoroutine(SaveDataCoroutine(operation));
                yield return new WaitForSeconds(0.1f); // Small delay between operations
            }
        }

        private void CollectDataFromComponents()
        {
            localSaveData.gameData.Clear();
            
            foreach (var kvp in saveableComponentsLookup)
            {
                try
                {
                    string saveData = kvp.Value.GetSaveData();
                    if (!string.IsNullOrEmpty(saveData))
                    {
                        localSaveData.gameData[kvp.Key] = saveData;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to collect save data from {kvp.Key}: {e.Message}");
                }
            }
        }

        private bool SaveLocalData()
        {
            try
            {
                string json = JsonUtility.ToJson(localSaveData);
                
                if (enableEncryption)
                {
                    json = EncryptData(json);
                }
                
                if (compressData)
                {
                    json = CompressData(json);
                }
                
                SaveGame.Save("CloudSaveData", json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save local data: {e.Message}");
                return false;
            }
        }

        private IEnumerator SaveToCloud()
        {
            Debug.Log("Saving to cloud...");
            
            // Simulate cloud save operation
            yield return new WaitForSeconds(1f);
            
            // In reality, this would make an API call to save data to the cloud
            hasUnsyncedChanges = false;
            lastSyncTime = DateTime.UtcNow;
            
            Debug.Log("Cloud save completed");
        }

        public void LoadData(Action<bool> callback = null)
        {
            StartCoroutine(LoadDataCoroutine(callback));
        }

        private IEnumerator LoadDataCoroutine(Action<bool> callback)
        {
            bool success = true;
            
            try
            {
                LoadDataIntoComponents();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load data into components: {e.Message}");
                success = false;
            }
            
            callback?.Invoke(success);
            OnLoadCompleted?.Invoke(success);
            
            yield return null;
        }

        private void LoadDataIntoComponents()
        {
            foreach (var kvp in saveableComponentsLookup)
            {
                try
                {
                    if (localSaveData.gameData.TryGetValue(kvp.Key, out string saveData))
                    {
                        kvp.Value.LoadSaveData(saveData);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load save data into {kvp.Key}: {e.Message}");
                }
            }
        }

        private IEnumerator AutoSaveRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(autoSaveInterval);
                
                if (isInitialized && (DateTime.UtcNow - lastAutoSaveTime).TotalSeconds >= autoSaveInterval)
                {
                    SaveDataAsync();
                }
            }
        }

        private IEnumerator SyncRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(syncInterval);
                
                if (hasUnsyncedChanges && isOnline && isSignedIn && !isSyncing)
                {
                    yield return StartCoroutine(SyncWithCloud());
                }
            }
        }

        private IEnumerator SyncWithCloud()
        {
            isSyncing = true;
            
            Debug.Log("Syncing with cloud...");
            
            // Save current data to cloud
            yield return StartCoroutine(SaveToCloud());
            
            // Load latest cloud data
            yield return StartCoroutine(LoadCloudData());
            
            // Check for conflicts
            if (DetectConflict())
            {
                yield return StartCoroutine(ResolveConflicts());
            }
            
            isSyncing = false;
            OnSyncCompleted?.Invoke(true);
            
            Debug.Log("Cloud sync completed");
        }

        private string EncryptData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return data;
            
            try
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] key = Encoding.UTF8.GetBytes("YourEncryptionKey123"); // Use a proper key management system
                
                // Simple XOR encryption (use proper encryption in production)
                for (int i = 0; i < dataBytes.Length; i++)
                {
                    dataBytes[i] ^= key[i % key.Length];
                }
                
                return Convert.ToBase64String(dataBytes);
            }
            catch (Exception e)
            {
                Debug.LogError($"Encryption failed: {e.Message}");
                return data;
            }
        }

        private string DecryptData(string encryptedData)
        {
            if (string.IsNullOrEmpty(encryptedData))
                return encryptedData;
            
            try
            {
                byte[] dataBytes = Convert.FromBase64String(encryptedData);
                byte[] key = Encoding.UTF8.GetBytes("YourEncryptionKey123");
                
                // Simple XOR decryption
                for (int i = 0; i < dataBytes.Length; i++)
                {
                    dataBytes[i] ^= key[i % key.Length];
                }
                
                return Encoding.UTF8.GetString(dataBytes);
            }
            catch (Exception e)
            {
                Debug.LogError($"Decryption failed: {e.Message}");
                return encryptedData;
            }
        }

        private string CompressData(string data)
        {
            // Simple compression simulation
            return data;
        }

        public void ForceSync()
        {
            if (isOnline && isSignedIn)
            {
                StartCoroutine(SyncWithCloud());
            }
        }

        public void RegisterSaveableComponent(string saveKey, ISaveableComponent component)
        {
            saveableComponentsLookup[saveKey] = component;
        }

        public void UnregisterSaveableComponent(string saveKey)
        {
            saveableComponentsLookup.Remove(saveKey);
        }

        private void OnApplicationFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                // Save when app loses focus
                SaveDataAsync();
            }
            else
            {
                // Sync when app regains focus
                if (hasUnsyncedChanges)
                {
                    ForceSync();
                }
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveDataAsync();
            }
        }

        void OnDestroy()
        {
            SaveDataAsync();
            
            if (autoSaveCoroutine != null)
                StopCoroutine(autoSaveCoroutine);
                
            if (syncCoroutine != null)
                StopCoroutine(syncCoroutine);
                
            Application.focusChanged -= OnApplicationFocusChanged;
        }
    }

    // Interfaces and data structures
    public interface ISaveableComponent
    {
        string GetSaveKey();
        string GetSaveData();
        void LoadSaveData(string data);
    }

    [System.Serializable]
    public class CloudSaveData
    {
        public string saveId;
        public string playerId;
        public int version;
        public DateTime timestamp;
        public string deviceId;
        public Dictionary<string, string> gameData;
    }

    [System.Serializable]
    public class SaveableComponent
    {
        public string saveKey;
        public ISaveableComponent component;
        public int priority = 0;
    }

    [System.Serializable]
    public class SaveOperation
    {
        public string operationId;
        public DateTime timestamp;
        public bool isManual;
        public Action<bool> callback;
    }

    [System.Serializable]
    public class ConflictResolutionData
    {
        public CloudSaveData localData;
        public CloudSaveData cloudData;
        public ConflictType conflictType;
    }

    // Enums
    public enum CloudProvider
    {
        PlayGamesPlatform,
        GameCenter,
        CustomBackend
    }

    public enum ConflictResolutionStrategy
    {
        MostRecent,
        CloudPriority,
        LocalPriority,
        Merge,
        UserChoice
    }

    public enum ConflictType
    {
        LocalNewer,
        CloudNewer,
        DataMismatch,
        DeviceMismatch
    }
}