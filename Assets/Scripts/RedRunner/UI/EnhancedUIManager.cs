using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;
using RedRunner.Analytics;
using RedRunner.Progression;
using RedRunner.Networking;

namespace RedRunner.UI
{
    /// <summary>
    /// Enhanced UI system with multiplayer features, animations, and mobile optimization
    /// Handles all UI interactions with proper scaling and performance
    /// </summary>
    public class EnhancedUIManager : MonoBehaviour
    {
        private static EnhancedUIManager instance;
        public static EnhancedUIManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<EnhancedUIManager>();
                }
                return instance;
            }
        }

        [Header("UI Configuration")]
        [SerializeField] private Canvas mainCanvas;
        [SerializeField] private CanvasScaler canvasScaler;
        [SerializeField] private GraphicRaycaster graphicRaycaster;
        [SerializeField] private EventSystem eventSystem;
        
        [Header("Screen Management")]
        [SerializeField] private List<EnhancedUIScreen> screens = new List<EnhancedUIScreen>();
        [SerializeField] private EnhancedUIScreen startingScreen;
        [SerializeField] private float screenTransitionDuration = 0.5f;
        [SerializeField] private Ease screenTransitionEase = Ease.OutCubic;
        
        [Header("Multiplayer UI")]
        [SerializeField] private MultiplayerHUD multiplayerHUD;
        [SerializeField] private PlayerListPanel playerListPanel;
        [SerializeField] private ChatPanel chatPanel;
        [SerializeField] private MatchmakingPanel matchmakingPanel;
        
        [Header("Notification System")]
        [SerializeField] private NotificationPanel notificationPanel;
        [SerializeField] private Transform notificationParent;
        [SerializeField] private GameObject notificationPrefab;
        [SerializeField] private int maxVisibleNotifications = 5;
        
        [Header("Loading & Transitions")]
        [SerializeField] private LoadingOverlay loadingOverlay;
        [SerializeField] private ScreenTransition screenTransition;
        
        [Header("Mobile Optimization")]
        [SerializeField] private bool enableHapticFeedback = true;
        [SerializeField] private bool enableSafeAreaHandling = true;
        [SerializeField] private float minTouchSize = 44f; // iOS HIG minimum
        
        // Screen management
        private EnhancedUIScreen currentScreen;
        private EnhancedUIScreen previousScreen;
        private Queue<ScreenTransitionRequest> screenTransitionQueue = new Queue<ScreenTransitionRequest>();
        private bool isTransitioning = false;
        
        // Notification management
        private Queue<NotificationData> notificationQueue = new Queue<NotificationData>();
        private List<GameObject> activeNotifications = new List<GameObject>();
        private Coroutine notificationCoroutine;
        
        // UI state
        private Dictionary<string, object> uiState = new Dictionary<string, object>();
        private SafeArea safeArea;
        
        // Events
        public static event Action<EnhancedUIScreen, EnhancedUIScreen> OnScreenChanged;
        public static event Action<NotificationData> OnNotificationShown;
        public static event Action<string> OnButtonClicked;
        
        public EnhancedUIScreen CurrentScreen => currentScreen;
        public bool IsTransitioning => isTransitioning;

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
            SetupCanvasScaling();
            SetupSafeArea();
            InitializeScreens();
            SetupEventSystem();
            
            // Start notification processing
            if (notificationCoroutine == null)
            {
                notificationCoroutine = StartCoroutine(ProcessNotificationQueue());
            }
            
            // Subscribe to game events
            SubscribeToEvents();
        }

        private void SetupCanvasScaling()
        {
            if (canvasScaler == null)
                canvasScaler = mainCanvas.GetComponent<CanvasScaler>();
            
            // Configure for mobile-first scaling
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1080, 1920); // Portrait mobile
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f; // Balanced scaling
            canvasScaler.referencePixelsPerUnit = 100f;
        }

        private void SetupSafeArea()
        {
            if (!enableSafeAreaHandling) return;
            
            safeArea = FindObjectOfType<SafeArea>();
            if (safeArea == null)
            {
                var safeAreaGO = new GameObject("SafeArea");
                safeAreaGO.transform.SetParent(mainCanvas.transform, false);
                safeArea = safeAreaGO.AddComponent<SafeArea>();
            }
            
            safeArea.Initialize(mainCanvas);
        }

        private void InitializeScreens()
        {
            var screenLookup = new Dictionary<string, EnhancedUIScreen>();
            
            foreach (var screen in screens)
            {
                if (screen != null)
                {
                    screen.Initialize(this);
                    screen.SetVisible(false, false);
                    screenLookup[screen.ScreenId] = screen;
                }
            }
            
            // Show starting screen
            if (startingScreen != null)
            {
                ShowScreen(startingScreen, false);
            }
        }

        private void SetupEventSystem()
        {
            if (eventSystem == null)
                eventSystem = FindObjectOfType<EventSystem>();
            
            if (eventSystem == null)
            {
                var eventSystemGO = new GameObject("EventSystem");
                eventSystem = eventSystemGO.AddComponent<EventSystem>();
                eventSystemGO.AddComponent<StandaloneInputModule>();
                DontDestroyOnLoad(eventSystemGO);
            }
        }

        private void SubscribeToEvents()
        {
            // Subscribe to progression events
            if (ProgressionManager.Instance != null)
            {
                ProgressionManager.OnLevelUp += OnPlayerLevelUp;
                ProgressionManager.OnAchievementUnlocked += OnAchievementUnlocked;
                ProgressionManager.OnContentUnlocked += OnContentUnlocked;
                ProgressionManager.OnCurrencyChanged += OnCurrencyChanged;
            }
            
            // Subscribe to network events
            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.OnPlayerJoined += OnPlayerJoined;
                NetworkGameManager.OnPlayerLeft += OnPlayerLeft;
                NetworkGameManager.OnGameStateUpdated += OnGameStateUpdated;
            }
        }

        public void ShowScreen(string screenId, bool animated = true)
        {
            var screen = screens.Find(s => s.ScreenId == screenId);
            if (screen != null)
            {
                ShowScreen(screen, animated);
            }
            else
            {
                Debug.LogWarning($"Screen with ID '{screenId}' not found!");
            }
        }

        public void ShowScreen(EnhancedUIScreen screen, bool animated = true)
        {
            if (screen == null || screen == currentScreen) return;
            
            var request = new ScreenTransitionRequest
            {
                targetScreen = screen,
                animated = animated,
                timestamp = Time.time
            };
            
            if (isTransitioning)
            {
                screenTransitionQueue.Enqueue(request);
                return;
            }
            
            StartCoroutine(TransitionToScreen(request));
        }

        private IEnumerator TransitionToScreen(ScreenTransitionRequest request)
        {
            isTransitioning = true;
            previousScreen = currentScreen;
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("screen_transition", new Dictionary<string, object>
            {
                { "from_screen", previousScreen?.ScreenId ?? "none" },
                { "to_screen", request.targetScreen.ScreenId },
                { "animated", request.animated }
            });
            
            if (request.animated && screenTransition != null)
            {
                yield return StartCoroutine(screenTransition.TransitionOut());
            }
            
            // Hide previous screen
            if (previousScreen != null)
            {
                yield return StartCoroutine(previousScreen.Hide(request.animated));
            }
            
            // Show new screen
            currentScreen = request.targetScreen;
            yield return StartCoroutine(currentScreen.Show(request.animated));
            
            if (request.animated && screenTransition != null)
            {
                yield return StartCoroutine(screenTransition.TransitionIn());
            }
            
            OnScreenChanged?.Invoke(previousScreen, currentScreen);
            
            isTransitioning = false;
            
            // Process queued transitions
            if (screenTransitionQueue.Count > 0)
            {
                var nextRequest = screenTransitionQueue.Dequeue();
                StartCoroutine(TransitionToScreen(nextRequest));
            }
        }

        public void ShowPreviousScreen(bool animated = true)
        {
            if (previousScreen != null)
            {
                ShowScreen(previousScreen, animated);
            }
        }

        public void ShowNotification(NotificationData notification)
        {
            notificationQueue.Enqueue(notification);
        }

        public void ShowNotification(string title, string message, NotificationType type = NotificationType.Info, float duration = 3f)
        {
            var notification = new NotificationData
            {
                title = title,
                message = message,
                type = type,
                duration = duration,
                timestamp = DateTime.Now
            };
            
            ShowNotification(notification);
        }

        private IEnumerator ProcessNotificationQueue()
        {
            while (true)
            {
                if (notificationQueue.Count > 0 && activeNotifications.Count < maxVisibleNotifications)
                {
                    var notification = notificationQueue.Dequeue();
                    yield return StartCoroutine(DisplayNotification(notification));
                }
                
                yield return new WaitForSeconds(0.1f);
            }
        }

        private IEnumerator DisplayNotification(NotificationData notification)
        {
            if (notificationPrefab == null) yield break;
            
            var notificationGO = Instantiate(notificationPrefab, notificationParent);
            var notificationUI = notificationGO.GetComponent<UnityEngine.UI.Text>(); // Simplified notification display
            
            if (notificationUI != null)
                {
                    notificationUI.text = notification.title + ": " + notification.message;
                activeNotifications.Add(notificationGO);
                
                // Animate in
                notificationGO.transform.localScale = Vector3.zero;
                notificationGO.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
                
                OnNotificationShown?.Invoke(notification);
                
                // Wait for duration
                yield return new WaitForSeconds(notification.duration);
                
                // Animate out
                yield return notificationGO.transform.DOScale(0f, 0.2f).SetEase(Ease.InBack).WaitForCompletion();
                
                activeNotifications.Remove(notificationGO);
                Destroy(notificationGO);
            }
        }

        public void ShowLoadingOverlay(string message = "Loading...", bool showProgress = false)
        {
            if (loadingOverlay != null)
            {
                loadingOverlay.Show(message, showProgress);
            }
        }

        public void HideLoadingOverlay()
        {
            if (loadingOverlay != null)
            {
                loadingOverlay.Hide();
            }
        }

        public void UpdateLoadingProgress(float progress)
        {
            if (loadingOverlay != null)
            {
                loadingOverlay.UpdateProgress(progress);
            }
        }

        public void RegisterButtonClick(string buttonId)
        {
            OnButtonClicked?.Invoke(buttonId);
            
            // Haptic feedback
            if (enableHapticFeedback)
            {
                TriggerHapticFeedback();
            }
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("button_clicked", new Dictionary<string, object>
            {
                { "button_id", buttonId },
                { "screen", currentScreen?.ScreenId ?? "unknown" }
            });
        }

        private void TriggerHapticFeedback()
        {
#if UNITY_IOS
            // iOS haptic feedback
            Handheld.Vibrate();
#elif UNITY_ANDROID
            // Android haptic feedback
            Handheld.Vibrate();
#endif
        }

        public void SetUIState(string key, object value)
        {
            uiState[key] = value;
        }

        public T GetUIState<T>(string key, T defaultValue = default(T))
        {
            if (uiState.TryGetValue(key, out var value) && value is T)
            {
                return (T)value;
            }
            return defaultValue;
        }

        public void UpdateMultiplayerHUD(bool visible)
        {
            if (multiplayerHUD != null)
            {
                multiplayerHUD.SetVisible(visible);
            }
        }

        public void UpdatePlayerList(List<PlayerInfo> players)
        {
            if (playerListPanel != null)
            {
                playerListPanel.UpdatePlayerList(players);
            }
        }

        public void ShowChatMessage(string playerId, string message)
        {
            if (chatPanel != null)
            {
                chatPanel.AddMessage(playerId, message);
            }
        }

        public void ShowMatchmakingPanel(bool show)
        {
            if (matchmakingPanel != null)
            {
                matchmakingPanel.SetVisible(show);
            }
        }

        // Event handlers
        private void OnPlayerLevelUp(int newLevel)
        {
            ShowNotification("Level Up!", $"You reached level {newLevel}!", NotificationType.Success, 4f);
        }

        private void OnAchievementUnlocked(Achievement achievement)
        {
            ShowNotification("Achievement Unlocked!", achievement.title, NotificationType.Achievement, 5f);
        }

        private void OnContentUnlocked(string contentId)
        {
            ShowNotification("New Content!", "Check out your new unlock!", NotificationType.Unlock, 4f);
        }

        private void OnCurrencyChanged(int amount, CurrencyType type)
        {
            // Update currency displays
            UpdateCurrencyUI(type, amount);
        }

        private void OnPlayerJoined(uint playerId, string playerName)
        {
            ShowNotification("Player Joined", $"{playerName} joined the game", NotificationType.Info, 3f);
        }

        private void OnPlayerLeft(uint playerId)
        {
            ShowNotification("Player Left", "A player left the game", NotificationType.Warning, 3f);
        }

        private void OnGameStateUpdated(IGameState gameState)
        {
            if (multiplayerHUD != null)
            {
                multiplayerHUD.UpdateGameState(gameState);
            }
        }

        private void UpdateCurrencyUI(CurrencyType type, int amount)
        {
            // Find and update all currency displays
            var currencyDisplays = FindObjectsOfType<CurrencyDisplay>();
            foreach (var display in currencyDisplays)
            {
                if (display.CurrencyType == type)
                {
                    display.UpdateValue(amount);
                }
            }
        }

        public void ValidateTouchTargets()
        {
            // Ensure all interactive elements meet minimum touch size requirements
            var buttons = FindObjectsOfType<Button>();
            foreach (var button in buttons)
            {
                var rectTransform = button.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    var size = rectTransform.rect.size;
                    if (size.x < minTouchSize || size.y < minTouchSize)
                    {
                        Debug.LogWarning($"Button '{button.name}' is smaller than minimum touch size ({minTouchSize}px)");
                    }
                }
            }
        }

        void Update()
        {
            // Handle back button on Android
#if UNITY_ANDROID
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                HandleBackButton();
            }
#endif
        }

        private void HandleBackButton()
        {
            if (currentScreen != null && currentScreen.CanGoBack)
            {
                currentScreen.OnBackButtonPressed();
            }
            else
            {
                ShowPreviousScreen();
            }
        }

        void OnDestroy()
        {
            // Unsubscribe from events
            if (ProgressionManager.Instance != null)
            {
                ProgressionManager.OnLevelUp -= OnPlayerLevelUp;
                ProgressionManager.OnAchievementUnlocked -= OnAchievementUnlocked;
                ProgressionManager.OnContentUnlocked -= OnContentUnlocked;
                ProgressionManager.OnCurrencyChanged -= OnCurrencyChanged;
            }
        }
    }

    /// <summary>
    /// Base class for enhanced UI screens with animation support
    /// </summary>
    public abstract class EnhancedUIScreen : MonoBehaviour
    {
        [Header("Screen Settings")]
        [SerializeField] private string screenId;
        [SerializeField] private bool canGoBack = true;
        [SerializeField] private float animationDuration = 0.5f;
        [SerializeField] private Ease animationEase = Ease.OutCubic;
        
        [Header("Animation Settings")]
        [SerializeField] private ScreenAnimationType animationType = ScreenAnimationType.Scale;
        [SerializeField] private Vector3 hiddenScale = Vector3.zero;
        [SerializeField] private Vector3 hiddenPosition = Vector3.zero;
        [SerializeField] private float hiddenAlpha = 0f;
        
        protected EnhancedUIManager uiManager;
        protected CanvasGroup canvasGroup;
        protected RectTransform rectTransform;
        
        public string ScreenId => screenId;
        public bool CanGoBack => canGoBack;
        public bool IsVisible { get; private set; }

        public virtual void Initialize(EnhancedUIManager manager)
        {
            uiManager = manager;
            canvasGroup = GetComponent<CanvasGroup>();
            rectTransform = GetComponent<RectTransform>();
            
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
                
            OnInitialize();
        }

        protected virtual void OnInitialize() { }

        public virtual IEnumerator Show(bool animated = true)
        {
            IsVisible = true;
            gameObject.SetActive(true);
            
            if (animated)
            {
                yield return StartCoroutine(AnimateIn());
            }
            else
            {
                SetVisibleState();
            }
            
            OnShow();
        }

        public virtual IEnumerator Hide(bool animated = true)
        {
            if (animated)
            {
                yield return StartCoroutine(AnimateOut());
            }
            else
            {
                SetHiddenState();
            }
            
            IsVisible = false;
            gameObject.SetActive(false);
            OnHide();
        }

        public virtual void SetVisible(bool visible, bool animated = true)
        {
            if (visible)
            {
                StartCoroutine(Show(animated));
            }
            else
            {
                StartCoroutine(Hide(animated));
            }
        }

        private IEnumerator AnimateIn()
        {
            var tween = CreateShowTween();
            if (tween != null)
            {
                yield return tween.WaitForCompletion();
            }
        }

        private IEnumerator AnimateOut()
        {
            var tween = CreateHideTween();
            if (tween != null)
            {
                yield return tween.WaitForCompletion();
            }
        }

        private Tween CreateShowTween()
        {
            switch (animationType)
            {
                case ScreenAnimationType.Scale:
                    transform.localScale = hiddenScale;
                    return transform.DOScale(Vector3.one, animationDuration).SetEase(animationEase);
                    
                case ScreenAnimationType.Slide:
                    rectTransform.anchoredPosition = hiddenPosition;
                    return rectTransform.DOAnchorPos(Vector2.zero, animationDuration).SetEase(animationEase);
                    
                case ScreenAnimationType.Fade:
                    canvasGroup.alpha = hiddenAlpha;
                    return canvasGroup.DOFade(1f, animationDuration).SetEase(animationEase);
                    
                case ScreenAnimationType.Combined:
                    transform.localScale = hiddenScale;
                    canvasGroup.alpha = hiddenAlpha;
                    var sequence = DOTween.Sequence();
                    sequence.Append(transform.DOScale(Vector3.one, animationDuration).SetEase(animationEase));
                    sequence.Join(canvasGroup.DOFade(1f, animationDuration).SetEase(animationEase));
                    return sequence;
                    
                default:
                    return null;
            }
        }

        private Tween CreateHideTween()
        {
            switch (animationType)
            {
                case ScreenAnimationType.Scale:
                    return transform.DOScale(hiddenScale, animationDuration).SetEase(animationEase);
                    
                case ScreenAnimationType.Slide:
                    return rectTransform.DOAnchorPos(hiddenPosition, animationDuration).SetEase(animationEase);
                    
                case ScreenAnimationType.Fade:
                    return canvasGroup.DOFade(hiddenAlpha, animationDuration).SetEase(animationEase);
                    
                case ScreenAnimationType.Combined:
                    var sequence = DOTween.Sequence();
                    sequence.Append(transform.DOScale(hiddenScale, animationDuration).SetEase(animationEase));
                    sequence.Join(canvasGroup.DOFade(hiddenAlpha, animationDuration).SetEase(animationEase));
                    return sequence;
                    
                default:
                    return null;
            }
        }

        private void SetVisibleState()
        {
            transform.localScale = Vector3.one;
            rectTransform.anchoredPosition = Vector2.zero;
            canvasGroup.alpha = 1f;
        }

        private void SetHiddenState()
        {
            switch (animationType)
            {
                case ScreenAnimationType.Scale:
                case ScreenAnimationType.Combined:
                    transform.localScale = hiddenScale;
                    break;
                    
                case ScreenAnimationType.Slide:
                    rectTransform.anchoredPosition = hiddenPosition;
                    break;
            }
            
            if (animationType == ScreenAnimationType.Fade || animationType == ScreenAnimationType.Combined)
            {
                canvasGroup.alpha = hiddenAlpha;
            }
        }

        public virtual void OnBackButtonPressed()
        {
            if (canGoBack)
            {
                uiManager.ShowPreviousScreen();
            }
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }

        protected void RegisterButtonClick(string buttonId)
        {
            uiManager.RegisterButtonClick($"{screenId}_{buttonId}");
        }
    }

    // Supporting classes and data structures
    [System.Serializable]
    public class NotificationData
    {
        public string title;
        public string message;
        public NotificationType type;
        public float duration;
        public DateTime timestamp;
        public Sprite icon;
    }

    [System.Serializable]
    public class ScreenTransitionRequest
    {
        public EnhancedUIScreen targetScreen;
        public bool animated;
        public float timestamp;
    }

    [System.Serializable]
    public class PlayerInfo
    {
        public uint playerId;
        public string playerName;
        public int score;
        public bool isAlive;
        public Color playerColor;
    }

    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error,
        Achievement,
        Unlock
    }

    public enum ScreenAnimationType
    {
        None,
        Scale,
        Slide,
        Fade,
        Combined
    }

    /// <summary>
    /// Component for handling safe area on mobile devices
    /// </summary>
    public class SafeArea : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect lastSafeArea = new Rect(0, 0, 0, 0);
        
        public void Initialize(Canvas canvas)
        {
            rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
                rectTransform = gameObject.AddComponent<RectTransform>();
                
            ApplySafeArea();
        }
        
        void Update()
        {
            if (Screen.safeArea != lastSafeArea)
            {
                ApplySafeArea();
            }
        }
        
        private void ApplySafeArea()
        {
            var safeArea = Screen.safeArea;
            
            var anchorMin = safeArea.position;
            var anchorMax = safeArea.position + safeArea.size;
            
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;
            
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            
            lastSafeArea = safeArea;
        }
    }

    /// <summary>
    /// UI component for displaying currency with animations
    /// </summary>
    public class CurrencyDisplay : MonoBehaviour
    {
        [SerializeField] private CurrencyType currencyType;
        [SerializeField] private TextMeshProUGUI valueText;
        [SerializeField] private Image iconImage;
        [SerializeField] private float animationDuration = 0.5f;
        
        private int currentValue;
        
        public CurrencyType CurrencyType => currencyType;
        
        public void UpdateValue(int newValue, bool animated = true)
        {
            if (animated)
            {
                AnimateValueChange(currentValue, newValue);
            }
            else
            {
                currentValue = newValue;
                UpdateDisplay();
            }
        }
        
        private void AnimateValueChange(int fromValue, int toValue)
        {
            DOTween.To(() => currentValue, x => currentValue = x, toValue, animationDuration)
                .OnUpdate(UpdateDisplay)
                .SetEase(Ease.OutCubic);
        }
        
        private void UpdateDisplay()
        {
            if (valueText != null)
            {
                valueText.text = FormatCurrency(currentValue);
            }
        }
        
        private string FormatCurrency(int value)
        {
            if (value >= 1000000)
                return $"{value / 1000000f:F1}M";
            else if (value >= 1000)
                return $"{value / 1000f:F1}K";
            else
                return value.ToString();
        }
    }
}