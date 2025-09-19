using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using RedRunner.Competition;
using RedRunner.Social;

namespace RedRunner.UI
{
    /// <summary>
    /// Leaderboard screen for displaying global and friends leaderboards
    /// Integrates with LeaderboardManager and SocialManager for competitive features
    /// </summary>
    public class LeaderboardScreen : UIScreen
    {
        [Header("Leaderboard Configuration")]
        [SerializeField] private string defaultLeaderboardId = "global_highscore";
        [SerializeField] private int maxDisplayEntries = 20;
        [SerializeField] private float refreshInterval = 30f;
        
        [Header("UI References")]
        [SerializeField] private Button globalLeaderboardButton;
        [SerializeField] private Button friendsLeaderboardButton;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button backButton;
        
        [Header("Leaderboard Display")]
        [SerializeField] private Transform leaderboardContentParent;
        [SerializeField] private GameObject leaderboardEntryPrefab;
        [SerializeField] private ScrollRect leaderboardScrollRect;
        
        [Header("Player Info")]
        [SerializeField] private GameObject playerRankPanel;
        [SerializeField] private TextMeshProUGUI playerRankText;
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private TextMeshProUGUI playerScoreText;
        [SerializeField] private TextMeshProUGUI playerPositionText;
        
        [Header("UI States")]
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private GameObject errorPanel;
        [SerializeField] private TextMeshProUGUI errorMessageText;
        [SerializeField] private Button retryButton;
        
        [Header("Tab System")]
        [SerializeField] private Image globalTabIndicator;
        [SerializeField] private Image friendsTabIndicator;
        [SerializeField] private Color activeTabColor = Color.white;
        [SerializeField] private Color inactiveTabColor = Color.gray;
        
        [Header("Animation")]
        [SerializeField] private float entryAnimationDuration = 0.3f;
        [SerializeField] private float entryAnimationDelay = 0.05f;
        [SerializeField] private Ease entryAnimationEase = Ease.OutQuart;
        
        // State management
        private LeaderboardDisplayMode currentMode = LeaderboardDisplayMode.Global;
        private List<GameObject> currentEntryObjects = new List<GameObject>();
        private Coroutine refreshCoroutine;
        private bool isInitialized = false;
        
        // Data
        private Leaderboard currentLeaderboard;
        private List<LeaderboardEntry> currentEntries;
        
        // Events
        public static event Action<LeaderboardDisplayMode> OnLeaderboardModeChanged;
        public static event Action<string, int> OnPlayerRankViewed;
        
        public enum LeaderboardDisplayMode
        {
            Global,
            Friends
        }
        
        #region Unity Lifecycle
        
        void Awake()
        {
            InitializeUI();
        }
        
        void OnEnable()
        {
            if (!isInitialized)
            {
                Initialize();
            }
            
            RefreshCurrentLeaderboard();
            StartAutoRefresh();
        }
        
        void OnDisable()
        {
            StopAutoRefresh();
        }
        
        void OnDestroy()
        {
            UnsubscribeFromEvents();
            StopAutoRefresh();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeUI()
        {
            // Button listeners
            if (globalLeaderboardButton != null)
                globalLeaderboardButton.onClick.AddListener(() => SwitchMode(LeaderboardDisplayMode.Global));
                
            if (friendsLeaderboardButton != null)
                friendsLeaderboardButton.onClick.AddListener(() => SwitchMode(LeaderboardDisplayMode.Friends));
                
            if (refreshButton != null)
                refreshButton.onClick.AddListener(RefreshCurrentLeaderboard);
                
            if (backButton != null)
                backButton.onClick.AddListener(CloseScreen);
                
            if (retryButton != null)
                retryButton.onClick.AddListener(RetryLoadLeaderboard);
            
            // Set initial UI state
            SetLoadingState(false);
            SetErrorState(false);
            UpdateTabIndicators();
        }
        
        private void Initialize()
        {
            if (LeaderboardManager.Instance == null)
            {
                Debug.LogError("LeaderboardManager instance not found!");
                ShowError("Leaderboard system unavailable");
                return;
            }
            
            SubscribeToEvents();
            isInitialized = true;
            
            Debug.Log("LeaderboardScreen initialized successfully");
        }
        
        private void SubscribeToEvents()
        {
            if (LeaderboardManager.Instance != null)
            {
                LeaderboardManager.OnLeaderboardUpdated += OnLeaderboardUpdated;
                LeaderboardManager.OnCompetitionError += OnLeaderboardError;
            }
        }
        
        private void UnsubscribeFromEvents()
        {
            if (LeaderboardManager.Instance != null)
            {
                LeaderboardManager.OnLeaderboardUpdated -= OnLeaderboardUpdated;
                LeaderboardManager.OnCompetitionError -= OnLeaderboardError;
            }
        }
        
        #endregion
        
        #region Public API
        
        public override void UpdateScreenStatus(bool open)
        {
            base.UpdateScreenStatus(open);
            
            if (open)
            {
                RefreshCurrentLeaderboard();
                UpdatePlayerInfo();
            }
        }
        
        public void SwitchMode(LeaderboardDisplayMode mode)
        {
            if (currentMode == mode) return;
            
            currentMode = mode;
            UpdateTabIndicators();
            RefreshCurrentLeaderboard();
            
            OnLeaderboardModeChanged?.Invoke(mode);
            
            Debug.Log($"Switched to {mode} leaderboard mode");
        }
        
        public void RefreshCurrentLeaderboard()
        {
            if (!isInitialized || LeaderboardManager.Instance == null) return;
            
            SetLoadingState(true);
            SetErrorState(false);
            
            switch (currentMode)
            {
                case LeaderboardDisplayMode.Global:
                    LoadGlobalLeaderboard();
                    break;
                case LeaderboardDisplayMode.Friends:
                    LoadFriendsLeaderboard();
                    break;
            }
        }
        
        #endregion
        
        #region Leaderboard Loading
        
        private void LoadGlobalLeaderboard()
        {
            // Try to get cached leaderboard first
            currentLeaderboard = LeaderboardManager.Instance.GetLeaderboard(defaultLeaderboardId);
            
            if (currentLeaderboard != null)
            {
                currentEntries = new List<LeaderboardEntry>(currentLeaderboard.entries);
                DisplayLeaderboard();
            }
            
            // Always refresh from server
            LeaderboardManager.Instance.RefreshLeaderboard(defaultLeaderboardId);
        }
        
        private void LoadFriendsLeaderboard()
        {
            currentEntries = LeaderboardManager.Instance.GetFriendsLeaderboard(defaultLeaderboardId);
            DisplayLeaderboard();
            
            // Refresh global data if needed for friends filtering
            if (currentLeaderboard == null)
            {
                LeaderboardManager.Instance.RefreshLeaderboard(defaultLeaderboardId);
            }
        }
        
        private void RetryLoadLeaderboard()
        {
            SetErrorState(false);
            RefreshCurrentLeaderboard();
        }
        
        #endregion
        
        #region UI Display
        
        private void DisplayLeaderboard()
        {
            ClearCurrentEntries();
            
            if (currentEntries == null || currentEntries.Count == 0)
            {
                ShowError(currentMode == LeaderboardDisplayMode.Friends ? 
                    "No friends on leaderboard yet" : "No leaderboard data available");
                return;
            }
            
            SetLoadingState(false);
            StartCoroutine(AnimateLeaderboardEntries());
        }
        
        private IEnumerator AnimateLeaderboardEntries()
        {
            int displayCount = Mathf.Min(currentEntries.Count, maxDisplayEntries);
            
            for (int i = 0; i < displayCount; i++)
            {
                var entry = currentEntries[i];
                var entryObj = CreateLeaderboardEntry(entry, i + 1);
                
                if (entryObj != null)
                {
                    currentEntryObjects.Add(entryObj);
                    
                    // Animate entry appearance
                    var canvasGroup = entryObj.GetComponent<CanvasGroup>();
                    if (canvasGroup == null)
                        canvasGroup = entryObj.AddComponent<CanvasGroup>();
                    
                    canvasGroup.alpha = 0f;
                    canvasGroup.transform.localScale = Vector3.zero;
                    
                    canvasGroup.DOFade(1f, entryAnimationDuration)
                        .SetEase(entryAnimationEase);
                    canvasGroup.transform.DOScale(1f, entryAnimationDuration)
                        .SetEase(entryAnimationEase);
                    
                    yield return new WaitForSeconds(entryAnimationDelay);
                }
            }
            
            UpdatePlayerInfo();
        }
        
        private GameObject CreateLeaderboardEntry(LeaderboardEntry entry, int displayPosition)
        {
            if (leaderboardEntryPrefab == null || leaderboardContentParent == null)
            {
                Debug.LogError("Leaderboard entry prefab or content parent not assigned!");
                return null;
            }
            
            var entryObj = Instantiate(leaderboardEntryPrefab, leaderboardContentParent);
            var entryComponent = entryObj.GetComponent<LeaderboardEntryUI>();
            
            if (entryComponent != null)
            {
                entryComponent.Setup(entry, displayPosition);
            }
            else
            {
                // Fallback if LeaderboardEntryUI component doesn't exist
                SetupEntryFallback(entryObj, entry, displayPosition);
            }
            
            return entryObj;
        }
        
        private void SetupEntryFallback(GameObject entryObj, LeaderboardEntry entry, int position)
        {
            // Basic text setup for when LeaderboardEntryUI component is not available
            var texts = entryObj.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length >= 3)
            {
                texts[0].text = position.ToString();
                texts[1].text = entry.playerName;
                texts[2].text = FormatScore(entry.score);
            }
        }
        
        private void UpdatePlayerInfo()
        {
            if (playerRankPanel == null || LeaderboardManager.Instance == null) return;
            
            var playerPosition = LeaderboardManager.Instance.GetPlayerLeaderboardPosition(defaultLeaderboardId);
            var personalBest = LeaderboardManager.Instance.GetPersonalBest(defaultLeaderboardId);
            
            if (playerPosition > 0)
            {
                playerRankPanel.SetActive(true);
                
                if (playerPositionText != null)
                    playerPositionText.text = $"#{playerPosition}";
                    
                if (playerNameText != null)
                    playerNameText.text = "You"; // Could get from save data
                    
                if (playerScoreText != null)
                    playerScoreText.text = FormatScore(personalBest);
                    
                OnPlayerRankViewed?.Invoke(defaultLeaderboardId, playerPosition);
            }
            else
            {
                playerRankPanel.SetActive(false);
            }
        }
        
        private void ClearCurrentEntries()
        {
            foreach (var entryObj in currentEntryObjects)
            {
                if (entryObj != null)
                    Destroy(entryObj);
            }
            currentEntryObjects.Clear();
        }
        
        private void UpdateTabIndicators()
        {
            if (globalTabIndicator != null)
                globalTabIndicator.color = currentMode == LeaderboardDisplayMode.Global ? activeTabColor : inactiveTabColor;
                
            if (friendsTabIndicator != null)
                friendsTabIndicator.color = currentMode == LeaderboardDisplayMode.Friends ? activeTabColor : inactiveTabColor;
        }
        
        #endregion
        
        #region UI States
        
        private void SetLoadingState(bool isLoading)
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(isLoading);
                
            if (refreshButton != null)
                refreshButton.interactable = !isLoading;
        }
        
        private void SetErrorState(bool hasError)
        {
            if (errorPanel != null)
                errorPanel.SetActive(hasError);
        }
        
        private void ShowError(string message)
        {
            SetLoadingState(false);
            SetErrorState(true);
            
            if (errorMessageText != null)
                errorMessageText.text = message;
                
            Debug.LogWarning($"Leaderboard error: {message}");
        }
        
        #endregion
        
        #region Auto Refresh
        
        private void StartAutoRefresh()
        {
            StopAutoRefresh();
            if (refreshInterval > 0)
            {
                refreshCoroutine = StartCoroutine(AutoRefreshCoroutine());
            }
        }
        
        private void StopAutoRefresh()
        {
            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
            }
        }
        
        private IEnumerator AutoRefreshCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(refreshInterval);
                
                if (IsOpen && Application.internetReachability != NetworkReachability.NotReachable)
                {
                    RefreshCurrentLeaderboard();
                }
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnLeaderboardUpdated(string leaderboardId, Leaderboard leaderboard)
        {
            if (leaderboardId == defaultLeaderboardId)
            {
                currentLeaderboard = leaderboard;
                
                // Update current entries based on display mode
                if (currentMode == LeaderboardDisplayMode.Global)
                {
                    currentEntries = new List<LeaderboardEntry>(leaderboard.entries);
                }
                else if (currentMode == LeaderboardDisplayMode.Friends)
                {
                    currentEntries = LeaderboardManager.Instance.GetFriendsLeaderboard(defaultLeaderboardId);
                }
                
                DisplayLeaderboard();
            }
        }
        
        private void OnLeaderboardError(string errorMessage)
        {
            ShowError(errorMessage);
        }
        
        #endregion
        
        #region Navigation
        
        private void CloseScreen()
        {
            if (UIManager.Singleton != null)
            {
                var startScreen = UIManager.Singleton.UISCREENS.Find(el => el.ScreenInfo == UIScreenInfo.START_SCREEN);
                if (startScreen != null)
                {
                    UIManager.Singleton.OpenScreen(startScreen);
                }
            }
        }
        
        #endregion
        
        #region Utilities
        
        private string FormatScore(float score)
        {
            if (score >= 1000000)
                return $"{score / 1000000:F1}M";
            else if (score >= 1000)
                return $"{score / 1000:F1}K";
            else
                return $"{score:F0}";
        }
        
        #endregion
    }
    
    /// <summary>
    /// Optional component for enhanced leaderboard entry display
    /// Can be attached to leaderboard entry prefabs for better control
    /// </summary>
    public class LeaderboardEntryUI : MonoBehaviour
    {
        [Header("Entry UI Elements")]
        [SerializeField] private TextMeshProUGUI positionText;
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image avatarImage;
        
        [Header("Highlighting")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color highlightColor = Color.yellow;
        [SerializeField] private Color playerEntryColor = Color.green;
        
        private LeaderboardEntry entryData;
        private bool isPlayerEntry;
        
        public void Setup(LeaderboardEntry entry, int displayPosition)
        {
            entryData = entry;
            
            // Check if this is the current player's entry
            isPlayerEntry = LeaderboardManager.Instance != null && 
                           entry.playerId == LeaderboardManager.Instance.PlayerId;
            
            // Set text values
            if (positionText != null)
                positionText.text = displayPosition.ToString();
                
            if (playerNameText != null)
                playerNameText.text = entry.playerName;
                
            if (scoreText != null)
                scoreText.text = FormatScore(entry.score);
            
            // Apply styling
            ApplyStyling();
            
            // Load avatar if available (placeholder for future implementation)
            LoadAvatar(entry.avatarUrl);
        }
        
        private void ApplyStyling()
        {
            if (backgroundImage != null)
            {
                backgroundImage.color = isPlayerEntry ? playerEntryColor : normalColor;
            }
            
            if (isPlayerEntry && playerNameText != null)
            {
                playerNameText.color = highlightColor;
            }
        }
        
        private void LoadAvatar(string avatarUrl)
        {
            // Placeholder for avatar loading implementation
            // Could integrate with web requests or default avatar system
            if (avatarImage != null && string.IsNullOrEmpty(avatarUrl))
            {
                // Set default avatar or hide image
                avatarImage.gameObject.SetActive(false);
            }
        }
        
        private string FormatScore(float score)
        {
            if (score >= 1000000)
                return $"{score / 1000000:F1}M";
            else if (score >= 1000)
                return $"{score / 1000:F1}K";
            else
                return $"{score:F0}";
        }
        
        public void OnEntryClicked()
        {
            // Optional: Handle entry click events (view player profile, etc.)
            Debug.Log($"Clicked on leaderboard entry: {entryData.playerName}");
        }
    }
}