using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RedRunner.Input
{
    /// <summary>
    /// Advanced mobile input manager with gesture recognition and haptic feedback
    /// Optimized for infinite runner games with customizable touch zones
    /// </summary>
    public class MobileInputManager : MonoBehaviour
    {
        private static MobileInputManager instance;
        public static MobileInputManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<MobileInputManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("MobileInputManager");
                        instance = go.AddComponent<MobileInputManager>();
                    }
                }
                return instance;
            }
        }

        [Header("Touch Zones")]
        [SerializeField] private RectTransform leftTouchZone;
        [SerializeField] private RectTransform rightTouchZone;
        [SerializeField] private RectTransform jumpTouchZone;
        [SerializeField] private bool autoCreateTouchZones = true;

        [Header("Input Settings")]
        [SerializeField] private float swipeThreshold = 50f;
        [SerializeField] private float tapTimeThreshold = 0.3f;
        [SerializeField] private float doubleTapTimeWindow = 0.5f;
        [SerializeField] private float holdTimeThreshold = 0.5f;

        [Header("Sensitivity")]
        [SerializeField] private float horizontalSensitivity = 1f;
        [SerializeField] private AnimationCurve sensitivityCurve = AnimationCurve.Linear(0, 0, 1, 1);

        [Header("Haptic Feedback")]
        [SerializeField] private bool enableHaptics = true;
        [SerializeField] private HapticFeedbackType jumpHaptic = HapticFeedbackType.LightImpact;
        [SerializeField] private HapticFeedbackType deathHaptic = HapticFeedbackType.HeavyImpact;

        [Header("Visual Feedback")]
        [SerializeField] private GameObject touchEffectPrefab;
        [SerializeField] private float touchEffectDuration = 0.5f;

        // Touch tracking
        private Dictionary<int, TouchData> activeTouches = new Dictionary<int, TouchData>();
        private Canvas uiCanvas;
        private Camera uiCamera;

        // Input state
        private float horizontalInput = 0f;
        private bool jumpPressed = false;
        private bool jumpHeld = false;
        private Vector2 lastTouchPosition;

        // Gesture detection
        private List<Vector2> gesturePoints = new List<Vector2>();
        private float gestureStartTime;

        // Events
        public static event Action<Vector2> OnTap;
        public static event Action<Vector2> OnDoubleTap;
        public static event Action<Vector2, Vector2> OnSwipe;
        public static event Action<Vector2> OnHold;
        public static event Action<float> OnHorizontalInput;
        public static event Action OnJumpPressed;
        public static event Action OnJumpReleased;

        public float HorizontalInput => horizontalInput;
        public bool JumpPressed => jumpPressed;
        public bool JumpHeld => jumpHeld;

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
            // Find UI Canvas
            uiCanvas = FindObjectOfType<Canvas>();
            if (uiCanvas != null)
            {
                uiCamera = uiCanvas.worldCamera ?? Camera.main;
            }

            // Create touch zones if they don't exist
            if (autoCreateTouchZones)
            {
                CreateDefaultTouchZones();
            }

            // Enable haptics based on platform
#if !UNITY_ANDROID && !UNITY_IOS
            enableHaptics = false;
#endif
        }

        private void CreateDefaultTouchZones()
        {
            if (uiCanvas == null) return;

            var canvasRect = uiCanvas.GetComponent<RectTransform>();

            // Create left touch zone (for moving left)
            if (leftTouchZone == null)
            {
                var leftZone = new GameObject("LeftTouchZone");
                leftZone.transform.SetParent(canvasRect, false);
                leftTouchZone = leftZone.AddComponent<RectTransform>();
                leftTouchZone.anchorMin = new Vector2(0, 0);
                leftTouchZone.anchorMax = new Vector2(0.3f, 1);
                leftTouchZone.offsetMin = Vector2.zero;
                leftTouchZone.offsetMax = Vector2.zero;
            }

            // Create right touch zone (for moving right)
            if (rightTouchZone == null)
            {
                var rightZone = new GameObject("RightTouchZone");
                rightZone.transform.SetParent(canvasRect, false);
                rightTouchZone = rightZone.AddComponent<RectTransform>();
                rightTouchZone.anchorMin = new Vector2(0.7f, 0);
                rightTouchZone.anchorMax = new Vector2(1, 1);
                rightTouchZone.offsetMin = Vector2.zero;
                rightTouchZone.offsetMax = Vector2.zero;
            }

            // Create jump touch zone (center area)
            if (jumpTouchZone == null)
            {
                var jumpZone = new GameObject("JumpTouchZone");
                jumpZone.transform.SetParent(canvasRect, false);
                jumpTouchZone = jumpZone.AddComponent<RectTransform>();
                jumpTouchZone.anchorMin = new Vector2(0.2f, 0.3f);
                jumpTouchZone.anchorMax = new Vector2(0.8f, 1);
                jumpTouchZone.offsetMin = Vector2.zero;
                jumpTouchZone.offsetMax = Vector2.zero;
            }
        }

        void Update()
        {
            // Clear frame-based inputs
            jumpPressed = false;

            // Handle input based on platform
#if UNITY_EDITOR || UNITY_STANDALONE
            HandleMouseInput();
#elif UNITY_ANDROID || UNITY_IOS
            HandleTouchInput();
#endif

            // Process gestures
            ProcessGestures();

            // Update horizontal input
            UpdateHorizontalInput();
        }

        private void HandleMouseInput()
        {
            // Mouse input for testing in editor
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                Vector2 mousePos = UnityEngine.Input.mousePosition;
                HandleTouchStart(0, mousePos);
            }
            else if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                Vector2 mousePos = UnityEngine.Input.mousePosition;
                HandleTouchEnd(0, mousePos);
            }
            else if (UnityEngine.Input.GetMouseButton(0))
            {
                Vector2 mousePos = UnityEngine.Input.mousePosition;
                HandleTouchMove(0, mousePos);
            }

            // Keyboard fallback
            float keyboardInput = 0f;
            if (UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow))
                keyboardInput -= 1f;
            if (UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow))
                keyboardInput += 1f;

            if (Mathf.Abs(keyboardInput) > 0.1f)
                horizontalInput = keyboardInput;

            if (UnityEngine.Input.GetKeyDown(KeyCode.Space) || UnityEngine.Input.GetKeyDown(KeyCode.W) || UnityEngine.Input.GetKeyDown(KeyCode.UpArrow))
            {
                jumpPressed = true;
                jumpHeld = true;
                OnJumpPressed?.Invoke();
            }

            if (UnityEngine.Input.GetKeyUp(KeyCode.Space) || UnityEngine.Input.GetKeyUp(KeyCode.W) || UnityEngine.Input.GetKeyUp(KeyCode.UpArrow))
            {
                jumpHeld = false;
                OnJumpReleased?.Invoke();
            }
        }

        private void HandleTouchInput()
        {
            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                Touch touch = UnityEngine.Input.GetTouch(i);

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        HandleTouchStart(touch.fingerId, touch.position);
                        break;
                    case TouchPhase.Moved:
                        HandleTouchMove(touch.fingerId, touch.position);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        HandleTouchEnd(touch.fingerId, touch.position);
                        break;
                }
            }

            // Remove ended touches
            var keysToRemove = new List<int>();
            foreach (var kvp in activeTouches)
            {
                if (Time.time - kvp.Value.lastUpdateTime > 0.1f)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (int key in keysToRemove)
            {
                activeTouches.Remove(key);
            }
        }

        private void HandleTouchStart(int touchId, Vector2 screenPosition)
        {
            if (IsOverUI(screenPosition)) return;

            var touchData = new TouchData
            {
                startPosition = screenPosition,
                currentPosition = screenPosition,
                startTime = Time.time,
                lastUpdateTime = Time.time,
                zone = GetTouchZone(screenPosition)
            };

            activeTouches[touchId] = touchData;

            // Start gesture tracking
            gesturePoints.Clear();
            gesturePoints.Add(screenPosition);
            gestureStartTime = Time.time;

            // Visual feedback
            ShowTouchEffect(screenPosition);

            lastTouchPosition = screenPosition;
        }

        private void HandleTouchMove(int touchId, Vector2 screenPosition)
        {
            if (!activeTouches.ContainsKey(touchId)) return;

            var touchData = activeTouches[touchId];
            touchData.currentPosition = screenPosition;
            touchData.lastUpdateTime = Time.time;

            // Add to gesture points
            gesturePoints.Add(screenPosition);

            // Update horizontal input based on touch zone
            UpdateTouchZoneInput(touchData);
        }

        private void HandleTouchEnd(int touchId, Vector2 screenPosition)
        {
            if (!activeTouches.ContainsKey(touchId)) return;

            var touchData = activeTouches[touchId];
            float touchDuration = Time.time - touchData.startTime;
            Vector2 deltaPosition = screenPosition - touchData.startPosition;
            float distance = deltaPosition.magnitude;

            // Determine gesture type
            if (touchDuration < tapTimeThreshold && distance < swipeThreshold)
            {
                // Tap or double tap
                if (touchData.zone == TouchZone.Jump)
                {
                    HandleJumpInput();
                }
                else
                {
                    OnTap?.Invoke(screenPosition);
                }
            }
            else if (distance > swipeThreshold)
            {
                // Swipe
                Vector2 swipeDirection = deltaPosition.normalized;
                OnSwipe?.Invoke(touchData.startPosition, swipeDirection);
                HandleSwipeGesture(swipeDirection);
            }
            else if (touchDuration > holdTimeThreshold)
            {
                // Hold
                OnHold?.Invoke(screenPosition);
            }

            activeTouches.Remove(touchId);

            // Reset horizontal input if this was the controlling touch
            if (activeTouches.Count == 0)
            {
                horizontalInput = 0f;
                jumpHeld = false;
                OnJumpReleased?.Invoke();
            }
        }

        private void UpdateTouchZoneInput(TouchData touchData)
        {
            switch (touchData.zone)
            {
                case TouchZone.Left:
                    horizontalInput = -1f * horizontalSensitivity;
                    break;
                case TouchZone.Right:
                    horizontalInput = 1f * horizontalSensitivity;
                    break;
                case TouchZone.Jump:
                    if (!jumpHeld)
                    {
                        jumpHeld = true;
                        HandleJumpInput();
                    }
                    break;
            }
        }

        private void HandleJumpInput()
        {
            jumpPressed = true;
            
            if (enableHaptics)
            {
                TriggerHapticFeedback(jumpHaptic);
            }

            OnJumpPressed?.Invoke();
        }

        private void HandleSwipeGesture(Vector2 direction)
        {
            // Handle swipe-based inputs
            if (Mathf.Abs(direction.y) > Mathf.Abs(direction.x))
            {
                if (direction.y > 0)
                {
                    // Swipe up - Jump
                    HandleJumpInput();
                }
                else
                {
                    // Swipe down - Slide/Duck (future feature)
                }
            }
            else
            {
                // Horizontal swipe for quick direction changes
                horizontalInput = Mathf.Sign(direction.x) * horizontalSensitivity;
            }
        }

        private TouchZone GetTouchZone(Vector2 screenPosition)
        {
            if (leftTouchZone != null && RectTransformUtility.RectangleContainsScreenPoint(leftTouchZone, screenPosition, uiCamera))
                return TouchZone.Left;
            
            if (rightTouchZone != null && RectTransformUtility.RectangleContainsScreenPoint(rightTouchZone, screenPosition, uiCamera))
                return TouchZone.Right;
            
            if (jumpTouchZone != null && RectTransformUtility.RectangleContainsScreenPoint(jumpTouchZone, screenPosition, uiCamera))
                return TouchZone.Jump;

            return TouchZone.None;
        }

        private bool IsOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;

            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            
            // Ignore our touch zones
            foreach (var result in results)
            {
                if (result.gameObject.name.Contains("TouchZone"))
                    continue;
                    
                return true;
            }
            
            return false;
        }

        private void ProcessGestures()
        {
            // Advanced gesture recognition could be implemented here
            // For now, we handle basic gestures in HandleTouchEnd
        }

        private void UpdateHorizontalInput()
        {
            // Apply sensitivity curve
            if (Mathf.Abs(horizontalInput) > 0.1f)
            {
                float normalizedInput = Mathf.Abs(horizontalInput);
                float curveValue = sensitivityCurve.Evaluate(normalizedInput);
                horizontalInput = Mathf.Sign(horizontalInput) * curveValue;
                
                OnHorizontalInput?.Invoke(horizontalInput);
            }
        }

        private void ShowTouchEffect(Vector2 screenPosition)
        {
            if (touchEffectPrefab != null)
            {
                Vector3 worldPosition = Camera.main.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 10f));
                var effect = Instantiate(touchEffectPrefab, worldPosition, Quaternion.identity);
                
                // Auto-destroy after duration
                Destroy(effect, touchEffectDuration);
            }
        }

        private void TriggerHapticFeedback(HapticFeedbackType type)
        {
#if UNITY_ANDROID
            if (enableHaptics)
            {
                // Use Unity's Handheld.Vibrate() or implement custom Android haptics
                Handheld.Vibrate();
            }
#elif UNITY_IOS
            if (enableHaptics)
            {
                // Implement iOS haptic feedback using native plugins
                switch (type)
                {
                    case HapticFeedbackType.LightImpact:
                        // iOS light impact
                        break;
                    case HapticFeedbackType.MediumImpact:
                        // iOS medium impact
                        break;
                    case HapticFeedbackType.HeavyImpact:
                        // iOS heavy impact
                        break;
                }
            }
#endif
        }

        public void TriggerDeathHaptic()
        {
            if (enableHaptics)
            {
                TriggerHapticFeedback(deathHaptic);
            }
        }

        public void SetTouchZoneVisibility(bool visible)
        {
            SetTouchZoneAlpha(visible ? 0.3f : 0f);
        }

        private void SetTouchZoneAlpha(float alpha)
        {
            var zones = new[] { leftTouchZone, rightTouchZone, jumpTouchZone };
            
            foreach (var zone in zones)
            {
                if (zone != null)
                {
                    var image = zone.GetComponent<UnityEngine.UI.Image>();
                    if (image != null)
                    {
                        var color = image.color;
                        color.a = alpha;
                        image.color = color;
                    }
                }
            }
        }

        public void CalibrateInputSensitivity(float sensitivity)
        {
            horizontalSensitivity = Mathf.Clamp(sensitivity, 0.1f, 3f);
        }

        public InputCalibrationData GetCalibrationData()
        {
            return new InputCalibrationData
            {
                sensitivity = horizontalSensitivity,
                swipeThreshold = swipeThreshold,
                hapticEnabled = enableHaptics
            };
        }

        public void ApplyCalibrationData(InputCalibrationData data)
        {
            horizontalSensitivity = data.sensitivity;
            swipeThreshold = data.swipeThreshold;
            enableHaptics = data.hapticEnabled;
        }
    }

    [System.Serializable]
    public class TouchData
    {
        public Vector2 startPosition;
        public Vector2 currentPosition;
        public float startTime;
        public float lastUpdateTime;
        public TouchZone zone;
    }

    public enum TouchZone
    {
        None,
        Left,
        Right,
        Jump
    }

    public enum HapticFeedbackType
    {
        LightImpact,
        MediumImpact,
        HeavyImpact,
        Selection,
        Success,
        Warning,
        Error
    }

    [System.Serializable]
    public class InputCalibrationData
    {
        public float sensitivity = 1f;
        public float swipeThreshold = 50f;
        public bool hapticEnabled = true;
    }
}