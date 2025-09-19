using System;
using System.Collections.Generic;
using UnityEngine;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;

namespace RedRunner.Progression
{
    /// <summary>
    /// Comprehensive progression system with unlocks, achievements, and monetization
    /// Handles player advancement, rewards, and content gating for F2P mechanics
    /// </summary>
    public class ProgressionManager : MonoBehaviour
    {
        private static ProgressionManager instance;
        public static ProgressionManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<ProgressionManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("ProgressionManager");
                        instance = go.AddComponent<ProgressionManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Progression Configuration")]
        [SerializeField] private PlayerProgressionConfig progressionConfig;
        [SerializeField] private UnlockableContent[] unlockableContent;
        [SerializeField] private Achievement[] achievements;
        [SerializeField] private DailyChallenge[] dailyChallenges;
        
        [Header("Currency Settings")]
        [SerializeField] private int startingCoins = 100;
        [SerializeField] private int startingGems = 10;
        [SerializeField] private float coinMultiplierBase = 1.0f;
        
        // Player data
        private PlayerProgressionData playerData;
        private Dictionary<string, UnlockableContent> contentLookup;
        private Dictionary<string, Achievement> achievementLookup;
        private List<DailyChallenge> activeChallenges;
        
        // Events
        public static event Action<int> OnLevelUp;
        public static event Action<string> OnContentUnlocked;
        public static event Action<Achievement> OnAchievementUnlocked;
        public static event Action<int, CurrencyType> OnCurrencyChanged;
        public static event Action<DailyChallenge> OnChallengeCompleted;
        public static event Action<int> OnExperienceGained;
        
        // Properties
        public PlayerProgressionData PlayerData => playerData;
        public int CurrentLevel => playerData?.level ?? 1;
        public int CurrentExperience => playerData?.experience ?? 0;
        public int Coins => playerData?.coins ?? 0;
        public int Gems => playerData?.gems ?? 0;
        public float CoinMultiplier => CalculateCoinMultiplier();

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
            LoadPlayerData();
            BuildLookupTables();
            InitializeDailyChallenges();
            
            // Subscribe to game events
            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.Instance.Coins.AddEventAndFire(OnCoinsChangedExternal, this);
            }
        }

        private void LoadPlayerData()
        {
            if (SaveGame.Exists("PlayerProgressionData"))
            {
                try
                {
                    string json = SaveGame.Load<string>("PlayerProgressionData");
                    playerData = JsonUtility.FromJson<PlayerProgressionData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load progression data: {e.Message}");
                    CreateNewPlayerData();
                }
            }
            else
            {
                CreateNewPlayerData();
            }
            
            // Validate and fix any corrupted data
            ValidatePlayerData();
        }

        private void CreateNewPlayerData()
        {
            playerData = new PlayerProgressionData
            {
                playerId = System.Guid.NewGuid().ToString(),
                level = 1,
                experience = 0,
                coins = startingCoins,
                gems = startingGems,
                unlockedContent = new List<string>(),
                completedAchievements = new List<string>(),
                statistics = new PlayerStatistics(),
                lastLoginDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                creationDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };
            
            // Unlock starting content
            UnlockStartingContent();
            SavePlayerData();
        }

        private void ValidatePlayerData()
        {
            if (playerData.unlockedContent == null)
                playerData.unlockedContent = new List<string>();
                
            if (playerData.completedAchievements == null)
                playerData.completedAchievements = new List<string>();
                
            if (playerData.statistics == null)
                playerData.statistics = new PlayerStatistics();
                
            // Ensure minimum values
            playerData.level = Mathf.Max(1, playerData.level);
            playerData.coins = Mathf.Max(0, playerData.coins);
            playerData.gems = Mathf.Max(0, playerData.gems);
        }

        private void UnlockStartingContent()
        {
            // Unlock default character and basic content
            UnlockContent("character_red_basic", false);
            UnlockContent("trail_basic", false);
        }

        private void BuildLookupTables()
        {
            contentLookup = new Dictionary<string, UnlockableContent>();
            achievementLookup = new Dictionary<string, Achievement>();
            
            if (unlockableContent != null)
            {
                foreach (var content in unlockableContent)
                {
                    contentLookup[content.id] = content;
                }
            }
            
            if (achievements != null)
            {
                foreach (var achievement in achievements)
                {
                    achievementLookup[achievement.id] = achievement;
                }
            }
        }

        private void InitializeDailyChallenges()
        {
            activeChallenges = new List<DailyChallenge>();
            
            // Check if we need to generate new daily challenges
            string lastChallengeDate = SaveGame.Exists("LastChallengeDate") ? 
                SaveGame.Load<string>("LastChallengeDate") : "";
                
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            
            if (lastChallengeDate != today)
            {
                GenerateDailyChallenges();
                SaveGame.Save("LastChallengeDate", today);
            }
            else
            {
                LoadDailyChallenges();
            }
        }

        private void GenerateDailyChallenges()
        {
            activeChallenges.Clear();
            
            if (dailyChallenges == null || dailyChallenges.Length == 0) return;
            
            // Select 3 random challenges for today
            var availableChallenges = new List<DailyChallenge>(dailyChallenges);
            
            for (int i = 0; i < Mathf.Min(3, availableChallenges.Count); i++)
            {
                int randomIndex = UnityEngine.Random.Range(0, availableChallenges.Count);
                var challenge = availableChallenges[randomIndex];
                
                // Create a copy with fresh progress
                var dailyChallenge = new DailyChallenge
                {
                    id = challenge.id + "_" + DateTime.UtcNow.ToString("yyyyMMdd"),
                    title = challenge.title,
                    description = challenge.description,
                    type = challenge.type,
                    targetValue = challenge.targetValue,
                    currentProgress = 0,
                    reward = challenge.reward,
                    isCompleted = false
                };
                
                activeChallenges.Add(dailyChallenge);
                availableChallenges.RemoveAt(randomIndex);
            }
            
            SaveDailyChallenges();
        }

        private void LoadDailyChallenges()
        {
            if (SaveGame.Exists("ActiveDailyChallenges"))
            {
                try
                {
                    string json = SaveGame.Load<string>("ActiveDailyChallenges");
                    var challengeList = JsonUtility.FromJson<DailyChallengeList>(json);
                    activeChallenges = challengeList.challenges ?? new List<DailyChallenge>();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load daily challenges: {e.Message}");
                    activeChallenges = new List<DailyChallenge>();
                }
            }
        }

        private void SaveDailyChallenges()
        {
            try
            {
                var challengeList = new DailyChallengeList { challenges = activeChallenges };
                string json = JsonUtility.ToJson(challengeList);
                SaveGame.Save("ActiveDailyChallenges", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save daily challenges: {e.Message}");
            }
        }

        public void SavePlayerData()
        {
            try
            {
                string json = JsonUtility.ToJson(playerData);
                SaveGame.Save("PlayerProgressionData", json);
                PlayerPrefs.Save(); // Force save to disk
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save progression data: {e.Message}");
            }
        }

        public void AddExperience(int amount, string source = "")
        {
            if (amount <= 0) return;
            
            int oldLevel = playerData.level;
            playerData.experience += amount;
            
            // Check for level up
            int newLevel = CalculateLevelFromExperience(playerData.experience);
            if (newLevel > oldLevel)
            {
                LevelUp(newLevel);
            }
            
            OnExperienceGained?.Invoke(amount);
            
            // Analytics
            AnalyticsManager.Instance?.TrackDesignEvent($"experience_gained_{source}", amount);
            
            SavePlayerData();
        }

        private int CalculateLevelFromExperience(int experience)
        {
            if (progressionConfig == null) return 1;
            
            int level = 1;
            int requiredXP = 0;
            
            while (requiredXP <= experience && level < progressionConfig.maxLevel)
            {
                requiredXP += GetExperienceRequiredForLevel(level + 1);
                if (requiredXP <= experience)
                    level++;
            }
            
            return level;
        }

        private int GetExperienceRequiredForLevel(int level)
        {
            if (progressionConfig == null) return 100;
            
            // Exponential curve: baseXP * (level^curve)
            return Mathf.RoundToInt(progressionConfig.baseExperienceRequired * 
                Mathf.Pow(level, progressionConfig.experienceCurve));
        }

        private void LevelUp(int newLevel)
        {
            int oldLevel = playerData.level;
            playerData.level = newLevel;
            
            // Grant level up rewards
            for (int level = oldLevel + 1; level <= newLevel; level++)
            {
                GrantLevelUpReward(level);
            }
            
            OnLevelUp?.Invoke(newLevel);
            
            // Analytics
            AnalyticsManager.Instance?.TrackProgression("level_up", "level", newLevel);
            
            Debug.Log($"Level Up! Player reached level {newLevel}");
        }

        private void GrantLevelUpReward(int level)
        {
            if (progressionConfig == null) return;
            
            // Base rewards
            int coinReward = progressionConfig.baseCoinReward * level;
            AddCurrency(CurrencyType.Coins, coinReward);
            
            // Gem rewards every 5 levels
            if (level % 5 == 0)
            {
                int gemReward = progressionConfig.baseGemReward;
                AddCurrency(CurrencyType.Gems, gemReward);
            }
            
            // Check for content unlocks
            CheckLevelBasedUnlocks(level);
        }

        private void CheckLevelBasedUnlocks(int level)
        {
            if (unlockableContent == null) return;
            
            foreach (var content in unlockableContent)
            {
                if (content.unlockCondition.type == UnlockConditionType.Level &&
                    content.unlockCondition.requiredValue <= level &&
                    !IsContentUnlocked(content.id))
                {
                    UnlockContent(content.id);
                }
            }
        }

        public void AddCurrency(CurrencyType type, int amount)
        {
            if (amount == 0) return;
            
            switch (type)
            {
                case CurrencyType.Coins:
                    playerData.coins = Mathf.Max(0, playerData.coins + amount);
                    OnCurrencyChanged?.Invoke(playerData.coins, CurrencyType.Coins);
                    
                    // Update external coin system
                    if (NetworkGameManager.Instance != null)
                    {
                        NetworkGameManager.Instance.Coins.Value = playerData.coins;
                    }
                    break;
                    
                case CurrencyType.Gems:
                    playerData.gems = Mathf.Max(0, playerData.gems + amount);
                    OnCurrencyChanged?.Invoke(playerData.gems, CurrencyType.Gems);
                    break;
            }
            
            // Analytics
            string flowType = amount > 0 ? "source" : "sink";
            AnalyticsManager.Instance?.TrackResourceEvent(flowType, type.ToString().ToLower(), 
                Mathf.Abs(amount), "progression", "currency_change");
            
            SavePlayerData();
        }

        public bool SpendCurrency(CurrencyType type, int amount)
        {
            if (amount <= 0) return false;
            
            bool canAfford = false;
            
            switch (type)
            {
                case CurrencyType.Coins:
                    canAfford = playerData.coins >= amount;
                    break;
                case CurrencyType.Gems:
                    canAfford = playerData.gems >= amount;
                    break;
            }
            
            if (canAfford)
            {
                AddCurrency(type, -amount);
                return true;
            }
            
            return false;
        }

        public void UnlockContent(string contentId, bool showNotification = true)
        {
            if (IsContentUnlocked(contentId)) return;
            
            playerData.unlockedContent.Add(contentId);
            
            if (showNotification)
            {
                OnContentUnlocked?.Invoke(contentId);
                
                // Show unlock notification
                if (contentLookup.TryGetValue(contentId, out var content))
                {
                    Debug.Log($"Content Unlocked: {content.displayName}");
                }
            }
            
            // Analytics
            AnalyticsManager.Instance?.TrackDesignEvent($"content_unlocked_{contentId}", 1f);
            
            SavePlayerData();
        }

        public bool IsContentUnlocked(string contentId)
        {
            return playerData.unlockedContent.Contains(contentId);
        }

        public bool CanUnlockContent(string contentId)
        {
            if (!contentLookup.TryGetValue(contentId, out var content))
                return false;
                
            if (IsContentUnlocked(contentId))
                return false;
                
            return CheckUnlockCondition(content.unlockCondition);
        }

        private bool CheckUnlockCondition(UnlockCondition condition)
        {
            switch (condition.type)
            {
                case UnlockConditionType.Level:
                    return playerData.level >= condition.requiredValue;
                    
                case UnlockConditionType.Coins:
                    return playerData.coins >= condition.requiredValue;
                    
                case UnlockConditionType.Achievement:
                    return playerData.completedAchievements.Contains(condition.requiredString);
                    
                case UnlockConditionType.Distance:
                    return playerData.statistics.totalDistanceTraveled >= condition.requiredValue;
                    
                case UnlockConditionType.Playtime:
                    return playerData.statistics.totalPlayTime >= condition.requiredValue;
                    
                default:
                    return false;
            }
        }

        public void UpdateStatistic(StatisticType type, float value)
        {
            switch (type)
            {
                case StatisticType.DistanceTraveled:
                    playerData.statistics.totalDistanceTraveled += value;
                    break;
                case StatisticType.CoinsCollected:
                    playerData.statistics.totalCoinsCollected += (int)value;
                    break;
                case StatisticType.JumpsPerformed:
                    playerData.statistics.totalJumps += (int)value;
                    break;
                case StatisticType.PlayTime:
                    playerData.statistics.totalPlayTime += value;
                    break;
                case StatisticType.GamesPlayed:
                    playerData.statistics.gamesPlayed += (int)value;
                    break;
            }
            
            // Check achievement progress
            CheckAchievementProgress();
            
            // Check daily challenge progress
            UpdateDailyChallengeProgress(type, value);
            
            SavePlayerData();
        }

        private void CheckAchievementProgress()
        {
            if (achievements == null) return;
            
            foreach (var achievement in achievements)
            {
                if (playerData.completedAchievements.Contains(achievement.id))
                    continue;
                    
                if (CheckAchievementCondition(achievement))
                {
                    UnlockAchievement(achievement);
                }
            }
        }

        private bool CheckAchievementCondition(Achievement achievement)
        {
            switch (achievement.condition.type)
            {
                case AchievementConditionType.Level:
                    return playerData.level >= achievement.condition.requiredValue;
                    
                case AchievementConditionType.TotalDistance:
                    return playerData.statistics.totalDistanceTraveled >= achievement.condition.requiredValue;
                    
                case AchievementConditionType.TotalCoins:
                    return playerData.statistics.totalCoinsCollected >= achievement.condition.requiredValue;
                    
                case AchievementConditionType.TotalJumps:
                    return playerData.statistics.totalJumps >= achievement.condition.requiredValue;
                    
                case AchievementConditionType.PlayTime:
                    return playerData.statistics.totalPlayTime >= achievement.condition.requiredValue;
                    
                case AchievementConditionType.GamesPlayed:
                    return playerData.statistics.gamesPlayed >= achievement.condition.requiredValue;
                    
                default:
                    return false;
            }
        }

        private void UnlockAchievement(Achievement achievement)
        {
            playerData.completedAchievements.Add(achievement.id);
            
            // Grant rewards
            if (achievement.coinReward > 0)
                AddCurrency(CurrencyType.Coins, achievement.coinReward);
                
            if (achievement.gemReward > 0)
                AddCurrency(CurrencyType.Gems, achievement.gemReward);
                
            if (achievement.experienceReward > 0)
                AddExperience(achievement.experienceReward, "achievement");
            
            OnAchievementUnlocked?.Invoke(achievement);
            
            // Analytics
            AnalyticsManager.Instance?.TrackDesignEvent($"achievement_unlocked_{achievement.id}", 1f);
            
            Debug.Log($"Achievement Unlocked: {achievement.title}");
        }

        private void UpdateDailyChallengeProgress(StatisticType type, float value)
        {
            foreach (var challenge in activeChallenges)
            {
                if (challenge.isCompleted) continue;
                
                bool updated = false;
                
                switch (challenge.type)
                {
                    case ChallengeType.CollectCoins when type == StatisticType.CoinsCollected:
                        challenge.currentProgress += (int)value;
                        updated = true;
                        break;
                        
                    case ChallengeType.TravelDistance when type == StatisticType.DistanceTraveled:
                        challenge.currentProgress += (int)value;
                        updated = true;
                        break;
                        
                    case ChallengeType.PerformJumps when type == StatisticType.JumpsPerformed:
                        challenge.currentProgress += (int)value;
                        updated = true;
                        break;
                        
                    case ChallengeType.PlayGames when type == StatisticType.GamesPlayed:
                        challenge.currentProgress += (int)value;
                        updated = true;
                        break;
                }
                
                if (updated && challenge.currentProgress >= challenge.targetValue)
                {
                    CompleteDailyChallenge(challenge);
                }
            }
            
            if (activeChallenges.Count > 0)
                SaveDailyChallenges();
        }

        private void CompleteDailyChallenge(DailyChallenge challenge)
        {
            challenge.isCompleted = true;
            
            // Grant rewards
            if (challenge.reward.coins > 0)
                AddCurrency(CurrencyType.Coins, challenge.reward.coins);
                
            if (challenge.reward.gems > 0)
                AddCurrency(CurrencyType.Gems, challenge.reward.gems);
                
            if (challenge.reward.experience > 0)
                AddExperience(challenge.reward.experience, "daily_challenge");
            
            OnChallengeCompleted?.Invoke(challenge);
            
            // Analytics
            AnalyticsManager.Instance?.TrackDesignEvent($"daily_challenge_completed_{challenge.type}", 1f);
            
            Debug.Log($"Daily Challenge Completed: {challenge.title}");
        }

        private float CalculateCoinMultiplier()
        {
            float multiplier = coinMultiplierBase;
            
            // Level bonus
            multiplier += (playerData.level - 1) * 0.05f; // 5% per level
            
            // Achievement bonuses would be added here
            
            return multiplier;
        }

        private void OnCoinsChangedExternal(int newValue)
        {
            // Sync with external coin system
            if (playerData.coins != newValue)
            {
                playerData.coins = newValue;
                OnCurrencyChanged?.Invoke(playerData.coins, CurrencyType.Coins);
                SavePlayerData();
            }
        }

        public List<UnlockableContent> GetUnlockableContent(ContentType type)
        {
            var result = new List<UnlockableContent>();
            
            if (unlockableContent == null) return result;
            
            foreach (var content in unlockableContent)
            {
                if (content.contentType == type)
                {
                    result.Add(content);
                }
            }
            
            return result;
        }

        public List<Achievement> GetAchievements(bool completedOnly = false)
        {
            var result = new List<Achievement>();
            
            if (achievements == null) return result;
            
            foreach (var achievement in achievements)
            {
                bool isCompleted = playerData.completedAchievements.Contains(achievement.id);
                
                if (!completedOnly || isCompleted)
                {
                    result.Add(achievement);
                }
            }
            
            return result;
        }

        public List<DailyChallenge> GetActiveDailyChallenges()
        {
            return new List<DailyChallenge>(activeChallenges);
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SavePlayerData();
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                SavePlayerData();
            }
        }
    }

    // Data structures
    [System.Serializable]
    public class PlayerProgressionData
    {
        public string playerId;
        public int level;
        public int experience;
        public int coins;
        public int gems;
        public List<string> unlockedContent;
        public List<string> completedAchievements;
        public PlayerStatistics statistics;
        public string lastLoginDate;
        public string creationDate;
    }

    [System.Serializable]
    public class PlayerStatistics
    {
        public float totalDistanceTraveled;
        public int totalCoinsCollected;
        public int totalJumps;
        public float totalPlayTime;
        public int gamesPlayed;
        public int highScore;
        public int longestRun;
    }

    [System.Serializable]
    public class UnlockableContent
    {
        public string id;
        public string displayName;
        public string description;
        public ContentType contentType;
        public UnlockCondition unlockCondition;
        public Sprite icon;
        public GameObject prefab;
        public int coinCost;
        public int gemCost;
    }

    [System.Serializable]
    public class UnlockCondition
    {
        public UnlockConditionType type;
        public int requiredValue;
        public string requiredString;
    }

    [System.Serializable]
    public class Achievement
    {
        public string id;
        public string title;
        public string description;
        public AchievementCondition condition;
        public int coinReward;
        public int gemReward;
        public int experienceReward;
        public Sprite icon;
    }

    [System.Serializable]
    public class AchievementCondition
    {
        public AchievementConditionType type;
        public int requiredValue;
    }

    [System.Serializable]
    public class DailyChallenge
    {
        public string id;
        public string title;
        public string description;
        public ChallengeType type;
        public int targetValue;
        public int currentProgress;
        public ChallengeReward reward;
        public bool isCompleted;
    }

    [System.Serializable]
    public class ChallengeReward
    {
        public int coins;
        public int gems;
        public int experience;
    }

    [System.Serializable]
    public class DailyChallengeList
    {
        public List<DailyChallenge> challenges;
    }

    [System.Serializable]
    public class PlayerProgressionConfig
    {
        public int maxLevel = 100;
        public int baseExperienceRequired = 100;
        public float experienceCurve = 1.2f;
        public int baseCoinReward = 50;
        public int baseGemReward = 5;
    }

    // Enums
    public enum CurrencyType
    {
        Coins,
        Gems
    }

    public enum ContentType
    {
        Character,
        Trail,
        Environment,
        PowerUp,
        Special
    }

    public enum UnlockConditionType
    {
        Level,
        Coins,
        Achievement,
        Distance,
        Playtime
    }

    public enum AchievementConditionType
    {
        Level,
        TotalDistance,
        TotalCoins,
        TotalJumps,
        PlayTime,
        GamesPlayed
    }

    public enum ChallengeType
    {
        CollectCoins,
        TravelDistance,
        PerformJumps,
        PlayGames,
        SurviveTime
    }

    public enum StatisticType
    {
        DistanceTraveled,
        CoinsCollected,
        JumpsPerformed,
        PlayTime,
        GamesPlayed
    }
}