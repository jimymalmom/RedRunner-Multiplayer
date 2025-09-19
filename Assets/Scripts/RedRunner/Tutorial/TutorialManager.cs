using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;
using RedRunner.UI;
using RedRunner.Progression;
using RedRunner.Networking;

namespace RedRunner.Tutorial
{
    /// <summary>
    /// Comprehensive tutorial and onboarding system for user retention
    /// Provides contextual help, progressive disclosure, and adaptive guidance
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        private static TutorialManager instance;
        public static TutorialManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<TutorialManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("TutorialManager");
                        instance = go.AddComponent<TutorialManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Tutorial Configuration")]
        [SerializeField] private bool enableTutorials = true;
        [SerializeField] private TutorialSequence[] tutorialSequences;
        [SerializeField] private float defaultStepDuration = 5f;
        [SerializeField] private bool allowSkipping = true;

        [Header("UI References")]
        [SerializeField] private Canvas tutorialCanvas;
        [SerializeField] private GameObject tutorialOverlay;
        [SerializeField] private RectTransform highlightFrame;
        [SerializeField] private GameObject tutorialBubble;
        [SerializeField] private TextMeshProUGUI tutorialText;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button skipButton;

        [Header("Visual Effects")]
        [SerializeField] private GameObject tapIndicator;
        [SerializeField] private GameObject swipeIndicator;
        [SerializeField] private ParticleSystem celebrationParticles;
        [SerializeField] private AudioClip tutorialCompleteSound;

        [Header("Onboarding")]
        [SerializeField] private OnboardingFlow onboardingFlow;
        [SerializeField] private bool showOnboardingOnFirstLaunch = true;
        [SerializeField] private float welcomeAnimationDuration = 2f;

        [Header("Adaptive Help")]
        [SerializeField] private bool enableAdaptiveHelp = true;
        [SerializeField] private float helpTriggerDelay = 10f;
        [SerializeField] private int maxHelpShows = 3;

        [Header("Progress Tracking")]
        [SerializeField] private bool trackTutorialProgress = true;
        [SerializeField] private TutorialTrigger[] contextualTriggers;

        // Tutorial state
        private TutorialData tutorialData;
        private Dictionary<string, TutorialSequence> tutorialLookup;
        private TutorialSequence currentSequence;
        private int currentStepIndex = 0;
        private bool isTutorialActive = false;
        private bool isOnboardingActive = false;

        // UI state
        private Camera tutorialCamera;
        private GraphicRaycaster tutorialRaycaster;
        private CanvasGroup tutorialCanvasGroup;

        // Adaptive help
        private Dictionary<string, AdaptiveHelpData> adaptiveHelpData;
        private Dictionary<string, float> featureUsageTimes;
        private Coroutine adaptiveHelpCoroutine;

        // Events
        public static event Action<string> OnTutorialStarted;
        public static event Action<string> OnTutorialCompleted;
        public static event Action<string, int> OnTutorialStepCompleted;
        public static event Action OnOnboardingCompleted;
        public static event Action<string> OnAdaptiveHelpTriggered;

        // Properties
        public bool IsTutorialActive => isTutorialActive;
        public bool IsOnboardingActive => isOnboardingActive;
        public bool IsFirstLaunch => tutorialData?.isFirstLaunch ?? true;
        public TutorialData CurrentTutorialData => tutorialData;

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
            LoadTutorialData();
            BuildTutorialLookup();
            InitializeUI();
            
            adaptiveHelpData = new Dictionary<string, AdaptiveHelpData>();
            featureUsageTimes = new Dictionary<string, float>();

            // Subscribe to game events
            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.OnGameStateUpdated += OnGameStateUpdated;
            }

            if (ProgressionManager.Instance != null)
            {
                ProgressionManager.OnLevelUp += OnPlayerLevelUp;
            }
        }

        private void LoadTutorialData()
        {
            if (SaveGame.Exists("TutorialData"))
            {
                try
                {
                    string json = SaveGame.Load<string>("TutorialData");
                    tutorialData = JsonUtility.FromJson<TutorialData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load tutorial data: {e.Message}");
                    CreateNewTutorialData();
                }
            }
            else
            {
                CreateNewTutorialData();
            }

            ValidateTutorialData();
        }

        private void CreateNewTutorialData()
        {
            tutorialData = new TutorialData
            {
                isFirstLaunch = true,
                completedTutorials = new List<string>(),
                completedOnboarding = false,
                tutorialProgress = new Dictionary<string, int>(),
                adaptiveHelpShown = new Dictionary<string, int>(),
                lastPlayDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                tutorialSettings = new TutorialSettings
                {
                    showHints = true,
                    autoProgress = false,
                    animationSpeed = 1f
                }
            };

            SaveTutorialData();
        }

        private void ValidateTutorialData()
        {
            if (tutorialData.completedTutorials == null)
                tutorialData.completedTutorials = new List<string>();

            if (tutorialData.tutorialProgress == null)
                tutorialData.tutorialProgress = new Dictionary<string, int>();

            if (tutorialData.adaptiveHelpShown == null)
                tutorialData.adaptiveHelpShown = new Dictionary<string, int>();

            if (tutorialData.tutorialSettings == null)
                tutorialData.tutorialSettings = new TutorialSettings();
        }

        private void BuildTutorialLookup()
        {
            tutorialLookup = new Dictionary<string, TutorialSequence>();

            if (tutorialSequences != null)
            {
                foreach (var sequence in tutorialSequences)
                {
                    tutorialLookup[sequence.id] = sequence;
                }
            }
        }

        private void InitializeUI()
        {
            if (tutorialCanvas == null)
            {
                CreateTutorialCanvas();
            }

            tutorialCanvasGroup = tutorialCanvas.GetComponent<CanvasGroup>();
            if (tutorialCanvasGroup == null)
            {
                tutorialCanvasGroup = tutorialCanvas.gameObject.AddComponent<CanvasGroup>();
            }

            tutorialRaycaster = tutorialCanvas.GetComponent<GraphicRaycaster>();
            tutorialCamera = tutorialCanvas.worldCamera ?? Camera.main;

            // Setup UI event handlers
            if (nextButton != null)
            {
                nextButton.onClick.AddListener(NextStep);
            }

            if (skipButton != null)
            {
                skipButton.onClick.AddListener(SkipTutorial);
            }

            // Hide tutorial UI initially
            SetTutorialUIVisibility(false);
        }

        private void CreateTutorialCanvas()
        {
            var canvasGO = new GameObject("TutorialCanvas");
            tutorialCanvas = canvasGO.AddComponent<Canvas>();
            tutorialCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            tutorialCanvas.sortingOrder = 1000; // Ensure it's on top

            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            DontDestroyOnLoad(canvasGO);
        }

        void Start()
        {
            // Check if we should show onboarding
            if (showOnboardingOnFirstLaunch && tutorialData.isFirstLaunch && !tutorialData.completedOnboarding)
            {
                StartCoroutine(ShowOnboardingAfterDelay());
            }
            else if (enableAdaptiveHelp)
            {
                StartAdaptiveHelpMonitoring();
            }
        }

        private IEnumerator ShowOnboardingAfterDelay()
        {
            // Wait for game systems to initialize
            yield return new WaitForSeconds(2f);

            if (onboardingFlow != null)
            {
                StartOnboarding();
            }
        }

        public void StartOnboarding()
        {
            if (isOnboardingActive || !enableTutorials) return;

            isOnboardingActive = true;
            StartCoroutine(OnboardingSequence());

            // Analytics
            AnalyticsManager.Instance?.TrackEvent("onboarding_started", new Dictionary<string, object>
            {
                { "is_first_launch", tutorialData.isFirstLaunch },
                { "onboarding_version", onboardingFlow?.version ?? "1.0" }
            });
        }

        private IEnumerator OnboardingSequence()
        {
            if (onboardingFlow == null) yield break;

            // Welcome animation
            yield return StartCoroutine(ShowWelcomeAnimation());

            // Run through onboarding steps
            foreach (var step in onboardingFlow.steps)
            {
                yield return StartCoroutine(ExecuteOnboardingStep(step));
            }

            // Complete onboarding
            CompleteOnboarding();
        }

        private IEnumerator ShowWelcomeAnimation()
        {
            SetTutorialUIVisibility(true);

            if (tutorialText != null)
            {
                tutorialText.text = onboardingFlow.welcomeMessage;
                tutorialText.transform.localScale = Vector3.zero;
                tutorialText.transform.DOScale(1f, welcomeAnimationDuration).SetEase(Ease.OutBack);
            }

            if (celebrationParticles != null)
            {
                celebrationParticles.Play();
            }

            yield return new WaitForSeconds(welcomeAnimationDuration + 1f);
        }

        private IEnumerator ExecuteOnboardingStep(OnboardingStep step)
        {
            switch (step.type)
            {
                case OnboardingStepType.ShowMessage:
                    yield return StartCoroutine(ShowMessage(step.message, step.duration));
                    break;

                case OnboardingStepType.HighlightUI:
                    yield return StartCoroutine(HighlightUIElement(step.targetElement, step.message, step.duration));
                    break;

                case OnboardingStepType.WaitForAction:
                    yield return StartCoroutine(WaitForUserAction(step.requiredAction));
                    break;

                case OnboardingStepType.PlayTutorial:
                    yield return StartCoroutine(PlayEmbeddedTutorial(step.tutorialId));
                    break;

                case OnboardingStepType.ShowGesture:
                    yield return StartCoroutine(ShowGestureDemo(step.gestureType, step.targetElement));
                    break;
            }
        }

        private IEnumerator ShowMessage(string message, float duration)
        {
            if (tutorialText != null)
            {
                tutorialText.text = message;
                
                // Fade in text
                tutorialText.alpha = 0f;
                tutorialText.DOFade(1f, 0.5f);
            }

            yield return new WaitForSeconds(duration);

            // Fade out text
            if (tutorialText != null)
            {
                tutorialText.DOFade(0f, 0.5f);
            }
        }

        private IEnumerator HighlightUIElement(GameObject targetElement, string message, float duration)
        {
            if (targetElement == null || highlightFrame == null) yield break;

            // Position highlight frame over target
            var targetRect = targetElement.GetComponent<RectTransform>();
            if (targetRect != null)
            {
                highlightFrame.position = targetRect.position;
                highlightFrame.sizeDelta = targetRect.sizeDelta * 1.1f; // Slightly larger

                // Animate highlight
                highlightFrame.gameObject.SetActive(true);
                highlightFrame.localScale = Vector3.zero;
                highlightFrame.DOScale(1f, 0.5f).SetEase(Ease.OutBack);

                // Pulsing effect
                var pulseSequence = DOTween.Sequence();
                pulseSequence.Append(highlightFrame.DOScale(1.1f, 0.5f))
                           .Append(highlightFrame.DOScale(1f, 0.5f))
                           .SetLoops(-1, LoopType.Yoyo);
            }

            // Show message
            if (!string.IsNullOrEmpty(message))
            {
                yield return StartCoroutine(ShowMessage(message, duration));
            }
            else
            {
                yield return new WaitForSeconds(duration);
            }

            // Hide highlight
            highlightFrame.DOKill();
            highlightFrame.DOScale(0f, 0.3f).OnComplete(() => highlightFrame.gameObject.SetActive(false));
        }

        private IEnumerator WaitForUserAction(UserAction requiredAction)
        {
            bool actionCompleted = false;
            float timeout = 30f; // 30 second timeout
            float elapsed = 0f;

            // Set up action listeners based on required action
            Action actionHandler = () => actionCompleted = true;

            switch (requiredAction)
            {
                case UserAction.Tap:
                    // Listen for any tap
                    break;
                case UserAction.Jump:
                    // Listen for jump input
                    break;
                case UserAction.CollectCoin:
                    // Listen for coin collection
                    break;
                case UserAction.OpenMenu:
                    // Listen for menu opening
                    break;
            }

            // Wait for action or timeout
            while (!actionCompleted && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (actionCompleted)
            {
                // Show positive feedback
                if (celebrationParticles != null)
                {
                    celebrationParticles.Play();
                }

                if (tutorialCompleteSound != null)
                {
                    AudioSource.PlayClipAtPoint(tutorialCompleteSound, Camera.main.transform.position);
                }
            }
        }

        private IEnumerator PlayEmbeddedTutorial(string tutorialId)
        {
            yield return StartCoroutine(StartTutorialCoroutine(tutorialId));
        }

        private IEnumerator ShowGestureDemo(GestureType gestureType, GameObject targetElement)
        {
            GameObject gestureIndicator = null;

            switch (gestureType)
            {
                case GestureType.Tap:
                    gestureIndicator = tapIndicator;
                    break;
                case GestureType.Swipe:
                    gestureIndicator = swipeIndicator;
                    break;
            }

            if (gestureIndicator != null && targetElement != null)
            {
                // Position gesture indicator
                gestureIndicator.transform.position = targetElement.transform.position;
                gestureIndicator.SetActive(true);

                // Animate gesture
                yield return StartCoroutine(AnimateGesture(gestureIndicator, gestureType));

                gestureIndicator.SetActive(false);
            }
        }

        private IEnumerator AnimateGesture(GameObject indicator, GestureType gestureType)
        {
            switch (gestureType)
            {
                case GestureType.Tap:
                    // Pulse animation
                    for (int i = 0; i < 3; i++)
                    {
                        indicator.transform.DOScale(1.2f, 0.3f);
                        yield return new WaitForSeconds(0.3f);
                        indicator.transform.DOScale(1f, 0.3f);
                        yield return new WaitForSeconds(0.3f);
                    }
                    break;

                case GestureType.Swipe:
                    // Swipe animation
                    var startPos = indicator.transform.position;
                    var endPos = startPos + Vector3.right * 100f;
                    
                    for (int i = 0; i < 3; i++)
                    {
                        indicator.transform.position = startPos;
                        indicator.transform.DOMove(endPos, 1f);
                        yield return new WaitForSeconds(1.2f);
                    }
                    break;
            }
        }

        private void CompleteOnboarding()
        {
            isOnboardingActive = false;
            tutorialData.completedOnboarding = true;
            tutorialData.isFirstLaunch = false;

            SetTutorialUIVisibility(false);
            SaveTutorialData();

            OnOnboardingCompleted?.Invoke();

            // Analytics
            AnalyticsManager.Instance?.TrackEvent("onboarding_completed", new Dictionary<string, object>
            {
                { "completion_time", Time.time },
                { "steps_completed", onboardingFlow?.steps?.Length ?? 0 }
            });

            // Start adaptive help monitoring
            if (enableAdaptiveHelp)
            {
                StartAdaptiveHelpMonitoring();
            }
        }

        public void StartTutorial(string tutorialId)
        {
            if (!enableTutorials || isTutorialActive) return;

            if (!tutorialLookup.TryGetValue(tutorialId, out var sequence))
            {
                Debug.LogWarning($"Tutorial not found: {tutorialId}");
                return;
            }

            StartCoroutine(StartTutorialCoroutine(tutorialId));
        }

        private IEnumerator StartTutorialCoroutine(string tutorialId)
        {
            var sequence = tutorialLookup[tutorialId];
            currentSequence = sequence;
            currentStepIndex = 0;
            isTutorialActive = true;

            SetTutorialUIVisibility(true);
            OnTutorialStarted?.Invoke(tutorialId);

            // Execute tutorial steps
            for (int i = 0; i < sequence.steps.Length; i++)
            {
                currentStepIndex = i;
                yield return StartCoroutine(ExecuteTutorialStep(sequence.steps[i], i));
                
                OnTutorialStepCompleted?.Invoke(tutorialId, i);
            }

            // Complete tutorial
            CompleteTutorial(tutorialId);
        }

        private IEnumerator ExecuteTutorialStep(TutorialStep step, int stepIndex)
        {
            // Show step UI
            yield return StartCoroutine(ShowTutorialStep(step));

            // Wait for completion condition
            yield return StartCoroutine(WaitForStepCompletion(step));
        }

        private IEnumerator ShowTutorialStep(TutorialStep step)
        {
            // Update tutorial text
            if (tutorialText != null)
            {
                tutorialText.text = step.instruction;
            }

            // Show/hide UI elements
            if (nextButton != null)
            {
                nextButton.gameObject.SetActive(step.showNextButton);
            }

            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(allowSkipping);
            }

            // Position tutorial bubble
            if (tutorialBubble != null && step.targetUI != null)
            {
                PositionTutorialBubble(step.targetUI, step.bubblePosition);
            }

            // Highlight target element
            if (step.targetUI != null && step.highlightTarget)
            {
                yield return StartCoroutine(HighlightUIElement(step.targetUI, "", 0f));
            }

            yield return null;
        }

        private void PositionTutorialBubble(GameObject target, BubblePosition position)
        {
            if (tutorialBubble == null || target == null) return;

            var targetRect = target.GetComponent<RectTransform>();
            var bubbleRect = tutorialBubble.GetComponent<RectTransform>();

            if (targetRect != null && bubbleRect != null)
            {
                Vector3 targetPos = targetRect.position;
                Vector3 offset = Vector3.zero;

                switch (position)
                {
                    case BubblePosition.Above:
                        offset = Vector3.up * (targetRect.rect.height / 2 + bubbleRect.rect.height / 2 + 20f);
                        break;
                    case BubblePosition.Below:
                        offset = Vector3.down * (targetRect.rect.height / 2 + bubbleRect.rect.height / 2 + 20f);
                        break;
                    case BubblePosition.Left:
                        offset = Vector3.left * (targetRect.rect.width / 2 + bubbleRect.rect.width / 2 + 20f);
                        break;
                    case BubblePosition.Right:
                        offset = Vector3.right * (targetRect.rect.width / 2 + bubbleRect.rect.width / 2 + 20f);
                        break;
                }

                bubbleRect.position = targetPos + offset;
            }
        }

        private IEnumerator WaitForStepCompletion(TutorialStep step)
        {
            bool stepCompleted = false;
            float timeout = step.timeLimit > 0 ? step.timeLimit : defaultStepDuration;
            float elapsed = 0f;

            switch (step.completionType)
            {
                case TutorialCompletionType.Timer:
                    yield return new WaitForSeconds(step.duration);
                    stepCompleted = true;
                    break;

                case TutorialCompletionType.UserAction:
                    // Wait for specific user action
                    while (!stepCompleted && elapsed < timeout)
                    {
                        if (CheckUserAction(step.requiredAction))
                        {
                            stepCompleted = true;
                        }
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                    break;

                case TutorialCompletionType.ButtonPress:
                    // Wait for next button or auto-advance
                    if (step.autoAdvance)
                    {
                        yield return new WaitForSeconds(step.duration);
                        stepCompleted = true;
                    }
                    else
                    {
                        // Wait for user to click next
                        while (!stepCompleted)
                        {
                            yield return null;
                        }
                    }
                    break;
            }
        }

        private bool CheckUserAction(UserAction requiredAction)
        {
            // This would be expanded based on your game's specific actions
            switch (requiredAction)
            {
                case UserAction.Tap:
                    return UnityEngine.Input.GetMouseButtonDown(0) || (UnityEngine.Input.touchCount > 0 && UnityEngine.Input.GetTouch(0).phase == TouchPhase.Began);
                case UserAction.Jump:
                    return UnityEngine.Input.GetKeyDown(KeyCode.Space) || UnityEngine.Input.GetButtonDown("Jump");
                // Add more actions as needed
            }
            return false;
        }

        public void NextStep()
        {
            // This method is called by the Next button
            // The actual step completion is handled in WaitForStepCompletion
        }

        public void SkipTutorial()
        {
            if (currentSequence != null)
            {
                StopAllCoroutines();
                CompleteTutorial(currentSequence.id);

                // Analytics
                AnalyticsManager.Instance?.TrackEvent("tutorial_skipped", new Dictionary<string, object>
                {
                    { "tutorial_id", currentSequence.id },
                    { "step_index", currentStepIndex },
                    { "completion_percentage", (float)currentStepIndex / currentSequence.steps.Length }
                });
            }
        }

        private void CompleteTutorial(string tutorialId)
        {
            isTutorialActive = false;
            
            if (!tutorialData.completedTutorials.Contains(tutorialId))
            {
                tutorialData.completedTutorials.Add(tutorialId);
            }

            tutorialData.tutorialProgress[tutorialId] = currentSequence?.steps.Length ?? 0;

            SetTutorialUIVisibility(false);
            SaveTutorialData();

            OnTutorialCompleted?.Invoke(tutorialId);

            // Analytics
            AnalyticsManager.Instance?.TrackEvent("tutorial_completed", new Dictionary<string, object>
            {
                { "tutorial_id", tutorialId },
                { "steps_completed", currentSequence?.steps.Length ?? 0 }
            });

            currentSequence = null;
            currentStepIndex = 0;
        }

        private void StartAdaptiveHelpMonitoring()
        {
            if (adaptiveHelpCoroutine != null)
            {
                StopCoroutine(adaptiveHelpCoroutine);
            }

            adaptiveHelpCoroutine = StartCoroutine(AdaptiveHelpRoutine());
        }

        private IEnumerator AdaptiveHelpRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);

                // Check for features that might need help
                CheckForAdaptiveHelpTriggers();
            }
        }

        private void CheckForAdaptiveHelpTriggers()
        {
            if (contextualTriggers == null) return;

            foreach (var trigger in contextualTriggers)
            {
                if (ShouldTriggerAdaptiveHelp(trigger))
                {
                    TriggerAdaptiveHelp(trigger);
                }
            }
        }

        private bool ShouldTriggerAdaptiveHelp(TutorialTrigger trigger)
        {
            // Check if already shown too many times
            int shownCount = tutorialData.adaptiveHelpShown.GetValueOrDefault(trigger.id, 0);
            if (shownCount >= maxHelpShows) return false;

            // Check if conditions are met
            return trigger.condition.Evaluate();
        }

        private void TriggerAdaptiveHelp(TutorialTrigger trigger)
        {
            // Show contextual help
            StartCoroutine(ShowAdaptiveHelpMessage(trigger));

            // Update show count
            tutorialData.adaptiveHelpShown[trigger.id] = tutorialData.adaptiveHelpShown.GetValueOrDefault(trigger.id, 0) + 1;
            SaveTutorialData();

            OnAdaptiveHelpTriggered?.Invoke(trigger.id);

            // Analytics
            AnalyticsManager.Instance?.TrackEvent("adaptive_help_triggered", new Dictionary<string, object>
            {
                { "trigger_id", trigger.id },
                { "show_count", tutorialData.adaptiveHelpShown[trigger.id] }
            });
        }

        private IEnumerator ShowAdaptiveHelpMessage(TutorialTrigger trigger)
        {
            SetTutorialUIVisibility(true);

            if (tutorialText != null)
            {
                tutorialText.text = trigger.helpMessage;
            }

            yield return new WaitForSeconds(trigger.displayDuration);

            SetTutorialUIVisibility(false);
        }

        private void SetTutorialUIVisibility(bool visible)
        {
            if (tutorialCanvasGroup != null)
            {
                tutorialCanvasGroup.alpha = visible ? 1f : 0f;
                tutorialCanvasGroup.interactable = visible;
                tutorialCanvasGroup.blocksRaycasts = visible;
            }

            if (tutorialOverlay != null)
            {
                tutorialOverlay.SetActive(visible);
            }
        }

        // Event handlers
        private void OnGameStateUpdated(IGameState gameState)
        {
            // Track feature usage for adaptive help
            TrackFeatureUsage("multiplayer_game");
        }

        private void OnPlayerLevelUp(int newLevel)
        {
            // Trigger level-based tutorials
            CheckLevelBasedTutorials(newLevel);
        }

        private void CheckLevelBasedTutorials(int level)
        {
            foreach (var sequence in tutorialSequences)
            {
                if (sequence.triggerLevel == level && !tutorialData.completedTutorials.Contains(sequence.id))
                {
                    StartTutorial(sequence.id);
                    break; // Only show one tutorial at a time
                }
            }
        }

        private void TrackFeatureUsage(string featureName)
        {
            featureUsageTimes[featureName] = Time.time;
        }

        // Public API
        public bool IsTutorialCompleted(string tutorialId)
        {
            return tutorialData.completedTutorials.Contains(tutorialId);
        }

        public int GetTutorialProgress(string tutorialId)
        {
            return tutorialData.tutorialProgress.GetValueOrDefault(tutorialId, 0);
        }

        public void ResetTutorialProgress(string tutorialId)
        {
            tutorialData.completedTutorials.Remove(tutorialId);
            tutorialData.tutorialProgress.Remove(tutorialId);
            SaveTutorialData();
        }

        public void ResetAllTutorials()
        {
            tutorialData.completedTutorials.Clear();
            tutorialData.tutorialProgress.Clear();
            tutorialData.adaptiveHelpShown.Clear();
            tutorialData.completedOnboarding = false;
            SaveTutorialData();
        }

        public void SetTutorialSettings(TutorialSettings settings)
        {
            tutorialData.tutorialSettings = settings;
            SaveTutorialData();
        }

        private void SaveTutorialData()
        {
            try
            {
                string json = JsonUtility.ToJson(tutorialData);
                SaveGame.Save("TutorialData", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save tutorial data: {e.Message}");
            }
        }

        void OnDestroy()
        {
            SaveTutorialData();

            if (nextButton != null)
            {
                nextButton.onClick.RemoveAllListeners();
            }

            if (skipButton != null)
            {
                skipButton.onClick.RemoveAllListeners();
            }
        }
    }

    // Data structures
    [System.Serializable]
    public class TutorialData
    {
        public bool isFirstLaunch;
        public List<string> completedTutorials;
        public bool completedOnboarding;
        public Dictionary<string, int> tutorialProgress;
        public Dictionary<string, int> adaptiveHelpShown;
        public string lastPlayDate;
        public TutorialSettings tutorialSettings;
    }

    [System.Serializable]
    public class TutorialSettings
    {
        public bool showHints = true;
        public bool autoProgress = false;
        public float animationSpeed = 1f;
        public bool enableVoiceOver = false;
    }

    [System.Serializable]
    public class TutorialSequence
    {
        public string id;
        public string displayName;
        public string description;
        public int triggerLevel = 0;
        public TutorialStep[] steps;
        public bool isOptional = false;
    }

    [System.Serializable]
    public class TutorialStep
    {
        public string instruction;
        public TutorialCompletionType completionType;
        public float duration = 5f;
        public float timeLimit = 30f;
        public bool autoAdvance = false;
        public bool showNextButton = true;
        public bool highlightTarget = true;
        public GameObject targetUI;
        public BubblePosition bubblePosition = BubblePosition.Above;
        public UserAction requiredAction;
        public string audioClipKey;
    }

    [System.Serializable]
    public class OnboardingFlow
    {
        public string version = "1.0";
        public string welcomeMessage;
        public OnboardingStep[] steps;
    }

    [System.Serializable]
    public class OnboardingStep
    {
        public OnboardingStepType type;
        public string message;
        public float duration = 3f;
        public GameObject targetElement;
        public UserAction requiredAction;
        public string tutorialId;
        public GestureType gestureType;
    }

    [System.Serializable]
    public class TutorialTrigger
    {
        public string id;
        public string helpMessage;
        public float displayDuration = 5f;
        public TutorialCondition condition;
    }

    [System.Serializable]
    public class TutorialCondition
    {
        public ConditionType type;
        public float value;
        public string stringValue;

        public bool Evaluate()
        {
            switch (type)
            {
                case ConditionType.TimeInFeature:
                    // Check if user has been in a feature for too long without action
                    return Time.time % 30f < 1f; // Simplified condition
                case ConditionType.FailureCount:
                    // Check if user has failed repeatedly
                    return false; // Would implement based on game state
                case ConditionType.LevelReached:
                    var progressionManager = ProgressionManager.Instance;
                    return progressionManager != null && progressionManager.CurrentLevel >= value;
                default:
                    return false;
            }
        }
    }

    [System.Serializable]
    public class AdaptiveHelpData
    {
        public string featureId;
        public float timeSpent;
        public int usageCount;
        public DateTime lastUsed;
        public bool needsHelp;
    }

    // Enums
    public enum TutorialCompletionType
    {
        Timer,
        UserAction,
        ButtonPress
    }

    public enum OnboardingStepType
    {
        ShowMessage,
        HighlightUI,
        WaitForAction,
        PlayTutorial,
        ShowGesture
    }

    public enum UserAction
    {
        Tap,
        Jump,
        Swipe,
        CollectCoin,
        OpenMenu,
        PurchaseItem,
        JoinMultiplayer
    }

    public enum GestureType
    {
        Tap,
        Swipe,
        Pinch,
        Hold
    }

    public enum BubblePosition
    {
        Above,
        Below,
        Left,
        Right,
        Center
    }

    public enum ConditionType
    {
        TimeInFeature,
        FailureCount,
        LevelReached,
        FeatureNotUsed
    }
}