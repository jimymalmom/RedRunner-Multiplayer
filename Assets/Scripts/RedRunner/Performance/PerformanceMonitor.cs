using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace RedRunner.Performance
{
    /// <summary>
    /// Comprehensive performance monitoring system for mobile optimization
    /// Tracks FPS, memory usage, battery, and provides adaptive quality settings
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour
    {
        private static PerformanceMonitor instance;
        public static PerformanceMonitor Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("PerformanceMonitor");
                    instance = go.AddComponent<PerformanceMonitor>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [Header("Monitoring Settings")]
        [SerializeField] private bool enableMonitoring = true;
        [SerializeField] private float monitoringInterval = 1f;
        [SerializeField] private bool enableAdaptiveQuality = true;
        [SerializeField] private bool showDebugOverlay = false;
        
        [Header("Performance Targets")]
        [SerializeField] private float targetFrameRate = 60f;
        [SerializeField] private float minAcceptableFrameRate = 30f;
        [SerializeField] private long maxMemoryUsageBytes = 512 * 1024 * 1024; // 512MB
        [SerializeField] private float maxCPUUsagePercent = 80f;
        
        [Header("Quality Settings")]
        [SerializeField] private QualityProfile[] qualityProfiles;
        [SerializeField] private int currentQualityLevel = 2; // Medium as default
        
        // Performance tracking
        private PerformanceData currentPerformance;
        private Queue<float> frameTimeHistory;
        private Queue<long> memoryHistory;
        private Queue<float> cpuHistory;
        
        // Adaptive quality
        private float lastQualityAdjustment = 0f;
        private float qualityAdjustmentCooldown = 5f;
        private int qualityAdjustmentDirection = 0; // -1 for decreasing, 1 for increasing
        
        // Device info
        private DeviceInfo deviceInfo;
        private BatteryInfo batteryInfo;
        
        // Coroutines
        private Coroutine monitoringCoroutine;
        private Coroutine adaptiveQualityCoroutine;
        
        // Events
        public static event Action<PerformanceData> OnPerformanceUpdated;
        public static event Action<int> OnQualityLevelChanged;
        public static event Action<PerformanceWarning> OnPerformanceWarning;
        
        public PerformanceData CurrentPerformance => currentPerformance;
        public DeviceInfo DeviceInfo => deviceInfo;
        public BatteryInfo BatteryInfo => batteryInfo;
        public int CurrentQualityLevel => currentQualityLevel;

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
            // Initialize performance tracking
            currentPerformance = new PerformanceData();
            frameTimeHistory = new Queue<float>();
            memoryHistory = new Queue<long>();
            cpuHistory = new Queue<float>();
            
            // Gather device information
            GatherDeviceInfo();
            
            // Initialize quality profiles if not set
            if (qualityProfiles == null || qualityProfiles.Length == 0)
            {
                CreateDefaultQualityProfiles();
            }
            
            // Set initial quality based on device capability
            SetInitialQuality();
            
            // Start monitoring
            if (enableMonitoring)
            {
                StartMonitoring();
            }
        }

        private void GatherDeviceInfo()
        {
            deviceInfo = new DeviceInfo
            {
                deviceModel = SystemInfo.deviceModel,
                deviceName = SystemInfo.deviceName,
                operatingSystem = SystemInfo.operatingSystem,
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                systemMemorySize = SystemInfo.systemMemorySize,
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsMemorySize = SystemInfo.graphicsMemorySize,
                maxTextureSize = SystemInfo.maxTextureSize,
                supportsInstancing = SystemInfo.supportsInstancing,
                supportsComputeShaders = SystemInfo.supportsComputeShaders,
                deviceType = SystemInfo.deviceType,
                batteryLevel = SystemInfo.batteryLevel,
                batteryStatus = SystemInfo.batteryStatus
            };
            
            batteryInfo = new BatteryInfo
            {
                level = SystemInfo.batteryLevel,
                status = SystemInfo.batteryStatus,
                lastUpdate = Time.time
            };
        }

        private void CreateDefaultQualityProfiles()
        {
            qualityProfiles = new QualityProfile[]
            {
                new QualityProfile // Low Quality
                {
                    name = "Low",
                    targetFrameRate = 30,
                    renderScale = 0.6f,
                    shadowQuality = ShadowQuality.Disable,
                    antiAliasing = 0,
                    particleMaxCount = 50,
                    enablePostProcessing = false,
                    maxLODLevel = 2,
                    textureQuality = 3 // Quarter resolution
                },
                new QualityProfile // Medium Quality
                {
                    name = "Medium",
                    targetFrameRate = 45,
                    renderScale = 0.8f,
                    shadowQuality = ShadowQuality.HardOnly,
                    antiAliasing = 2,
                    particleMaxCount = 100,
                    enablePostProcessing = true,
                    maxLODLevel = 1,
                    textureQuality = 1 // Half resolution
                },
                new QualityProfile // High Quality
                {
                    name = "High",
                    targetFrameRate = 60,
                    renderScale = 1.0f,
                    shadowQuality = ShadowQuality.All,
                    antiAliasing = 4,
                    particleMaxCount = 200,
                    enablePostProcessing = true,
                    maxLODLevel = 0,
                    textureQuality = 0 // Full resolution
                }
            };
        }

        private void SetInitialQuality()
        {
            // Determine initial quality based on device specs
            int recommendedQuality = CalculateRecommendedQuality();
            SetQualityLevel(recommendedQuality);
        }

        private int CalculateRecommendedQuality()
        {
            int score = 0;
            
            // RAM score
            if (deviceInfo.systemMemorySize >= 4096) score += 2; // 4GB+
            else if (deviceInfo.systemMemorySize >= 2048) score += 1; // 2GB+
            
            // CPU score
            if (deviceInfo.processorCount >= 8) score += 2; // 8+ cores
            else if (deviceInfo.processorCount >= 4) score += 1; // 4+ cores
            
            // GPU score (rough estimation based on graphics memory)
            if (deviceInfo.graphicsMemorySize >= 2048) score += 2; // 2GB+ VRAM
            else if (deviceInfo.graphicsMemorySize >= 1024) score += 1; // 1GB+ VRAM
            
            // Device type bonus
            if (deviceInfo.deviceType == DeviceType.Console) score += 3;
            else if (deviceInfo.deviceType == DeviceType.Desktop) score += 2;
            
            // Convert score to quality level
            if (score >= 6) return 2; // High
            else if (score >= 3) return 1; // Medium
            else return 0; // Low
        }

        public void StartMonitoring()
        {
            if (monitoringCoroutine != null)
                StopCoroutine(monitoringCoroutine);
                
            monitoringCoroutine = StartCoroutine(MonitoringRoutine());
            
            if (enableAdaptiveQuality)
            {
                if (adaptiveQualityCoroutine != null)
                    StopCoroutine(adaptiveQualityCoroutine);
                    
                adaptiveQualityCoroutine = StartCoroutine(AdaptiveQualityRoutine());
            }
        }

        public void StopMonitoring()
        {
            if (monitoringCoroutine != null)
            {
                StopCoroutine(monitoringCoroutine);
                monitoringCoroutine = null;
            }
            
            if (adaptiveQualityCoroutine != null)
            {
                StopCoroutine(adaptiveQualityCoroutine);
                adaptiveQualityCoroutine = null;
            }
        }

        private IEnumerator MonitoringRoutine()
        {
            while (enableMonitoring)
            {
                UpdatePerformanceMetrics();
                yield return new WaitForSeconds(monitoringInterval);
            }
        }

        private void UpdatePerformanceMetrics()
        {
            // Frame rate calculation
            float deltaTime = Time.unscaledDeltaTime;
            float fps = deltaTime > 0 ? 1f / deltaTime : 0f;
            
            frameTimeHistory.Enqueue(deltaTime);
            if (frameTimeHistory.Count > 60) // Keep 60 samples
                frameTimeHistory.Dequeue();
            
            // Memory usage
            long totalMemory = GC.GetTotalMemory(false);
            memoryHistory.Enqueue(totalMemory);
            if (memoryHistory.Count > 30) // Keep 30 samples
                memoryHistory.Dequeue();
            
            // CPU usage estimation (simplified)
            float cpuUsage = EstimateCPUUsage();
            cpuHistory.Enqueue(cpuUsage);
            if (cpuHistory.Count > 30)
                cpuHistory.Dequeue();
            
            // Battery info update
            UpdateBatteryInfo();
            
            // Calculate averages
            currentPerformance.averageFPS = CalculateAverageFPS();
            currentPerformance.averageFrameTime = CalculateAverageFrameTime();
            currentPerformance.memoryUsageBytes = totalMemory;
            currentPerformance.memoryUsageMB = totalMemory / (1024f * 1024f);
            currentPerformance.cpuUsagePercent = cpuUsage;
            currentPerformance.batteryLevel = batteryInfo.level;
            currentPerformance.temperature = GetDeviceTemperature();
            
            // Check for performance warnings
            CheckPerformanceWarnings();
            
            // Notify listeners
            OnPerformanceUpdated?.Invoke(currentPerformance);
        }

        private float CalculateAverageFPS()
        {
            if (frameTimeHistory.Count == 0) return 0f;
            
            float totalFrameTime = 0f;
            foreach (float frameTime in frameTimeHistory)
            {
                totalFrameTime += frameTime;
            }
            
            float averageFrameTime = totalFrameTime / frameTimeHistory.Count;
            return averageFrameTime > 0 ? 1f / averageFrameTime : 0f;
        }

        private float CalculateAverageFrameTime()
        {
            if (frameTimeHistory.Count == 0) return 0f;
            
            float total = 0f;
            foreach (float frameTime in frameTimeHistory)
            {
                total += frameTime;
            }
            
            return total / frameTimeHistory.Count;
        }

        private float EstimateCPUUsage()
        {
            // Simplified CPU usage estimation based on frame time
            float frameTime = Time.unscaledDeltaTime;
            float targetFrameTime = 1f / targetFrameRate;
            
            return Mathf.Clamp01(frameTime / targetFrameTime) * 100f;
        }

        private void UpdateBatteryInfo()
        {
            batteryInfo.level = SystemInfo.batteryLevel;
            batteryInfo.status = SystemInfo.batteryStatus;
            batteryInfo.lastUpdate = Time.time;
        }

        private float GetDeviceTemperature()
        {
            // Device temperature monitoring would require platform-specific implementation
            // For now, return a simulated value based on performance
            float baseTemp = 25f; // Room temperature
            float performanceTemp = currentPerformance.cpuUsagePercent * 0.3f; // Rough estimate
            
            return baseTemp + performanceTemp;
        }

        private void CheckPerformanceWarnings()
        {
            // FPS warning
            if (currentPerformance.averageFPS < minAcceptableFrameRate)
            {
                TriggerPerformanceWarning(PerformanceWarning.LowFrameRate, 
                    $"FPS dropped to {currentPerformance.averageFPS:F1}");
            }
            
            // Memory warning
            if (currentPerformance.memoryUsageBytes > maxMemoryUsageBytes)
            {
                TriggerPerformanceWarning(PerformanceWarning.HighMemoryUsage, 
                    $"Memory usage: {currentPerformance.memoryUsageMB:F1} MB");
            }
            
            // CPU warning
            if (currentPerformance.cpuUsagePercent > maxCPUUsagePercent)
            {
                TriggerPerformanceWarning(PerformanceWarning.HighCPUUsage, 
                    $"CPU usage: {currentPerformance.cpuUsagePercent:F1}%");
            }
            
            // Battery warning
            if (batteryInfo.level < 0.2f && batteryInfo.status == BatteryStatus.Discharging)
            {
                TriggerPerformanceWarning(PerformanceWarning.LowBattery, 
                    $"Battery level: {batteryInfo.level * 100:F0}%");
            }
        }

        private void TriggerPerformanceWarning(PerformanceWarning warning, string details)
        {
            Debug.LogWarning($"Performance Warning: {warning} - {details}");
            OnPerformanceWarning?.Invoke(warning);
        }

        private IEnumerator AdaptiveQualityRoutine()
        {
            while (enableAdaptiveQuality)
            {
                yield return new WaitForSeconds(qualityAdjustmentCooldown);
                
                if (Time.time - lastQualityAdjustment >= qualityAdjustmentCooldown)
                {
                    EvaluateQualityAdjustment();
                }
            }
        }

        private void EvaluateQualityAdjustment()
        {
            float avgFPS = currentPerformance.averageFPS;
            float targetFPS = qualityProfiles[currentQualityLevel].targetFrameRate;
            
            bool shouldDecrease = false;
            bool shouldIncrease = false;
            
            // Check if we should decrease quality
            if (avgFPS < targetFPS * 0.85f) // 15% below target
            {
                shouldDecrease = true;
            }
            
            // Check if we should increase quality
            if (avgFPS > targetFPS * 1.1f && currentPerformance.cpuUsagePercent < 60f) // 10% above target and low CPU
            {
                shouldIncrease = true;
            }
            
            // Apply adjustment
            if (shouldDecrease && currentQualityLevel > 0)
            {
                SetQualityLevel(currentQualityLevel - 1);
                qualityAdjustmentDirection = -1;
                lastQualityAdjustment = Time.time;
                Debug.Log($"Adaptive Quality: Decreased to {qualityProfiles[currentQualityLevel].name}");
            }
            else if (shouldIncrease && currentQualityLevel < qualityProfiles.Length - 1)
            {
                SetQualityLevel(currentQualityLevel + 1);
                qualityAdjustmentDirection = 1;
                lastQualityAdjustment = Time.time;
                Debug.Log($"Adaptive Quality: Increased to {qualityProfiles[currentQualityLevel].name}");
            }
        }

        public void SetQualityLevel(int level)
        {
            level = Mathf.Clamp(level, 0, qualityProfiles.Length - 1);
            
            if (level == currentQualityLevel) return;
            
            currentQualityLevel = level;
            ApplyQualityProfile(qualityProfiles[level]);
            
            OnQualityLevelChanged?.Invoke(level);
        }

        private void ApplyQualityProfile(QualityProfile profile)
        {
            // Apply Unity Quality Settings
            QualitySettings.SetQualityLevel(currentQualityLevel, true);
            
            // Apply custom settings
            Application.targetFrameRate = profile.targetFrameRate;
            QualitySettings.shadows = profile.shadowQuality;
            QualitySettings.antiAliasing = profile.antiAliasing;
            QualitySettings.globalTextureMipmapLimit = profile.textureQuality;
            
            // Apply render scale (would need to be implemented in your rendering pipeline)
            SetRenderScale(profile.renderScale);
            
            // Configure particle systems
            ConfigureParticleQuality(profile.particleMaxCount);
            
            // Configure post-processing
            ConfigurePostProcessing(profile.enablePostProcessing);
            
            Debug.Log($"Applied quality profile: {profile.name}");
        }

        private void SetRenderScale(float scale)
        {
            // This would depend on your rendering pipeline
            // For Universal Render Pipeline:
            // var renderPipelineAsset = GraphicsSettings.renderPipelineAsset as UniversalRenderPipelineAsset;
            // if (renderPipelineAsset != null)
            //     renderPipelineAsset.renderScale = scale;
            
            Debug.Log($"Render scale set to: {scale}");
        }

        private void ConfigureParticleQuality(int maxParticles)
        {
            // Find and configure all particle systems
            var particleSystems = FindObjectsOfType<ParticleSystem>();
            
            foreach (var ps in particleSystems)
            {
                var main = ps.main;
                main.maxParticles = Mathf.Min(main.maxParticles, maxParticles);
            }
        }

        private void ConfigurePostProcessing(bool enabled)
        {
            // Configure post-processing volumes
            // This would depend on your post-processing setup
            Debug.Log($"Post-processing enabled: {enabled}");
        }

        public void ForceQualityLevel(int level)
        {
            enableAdaptiveQuality = false;
            SetQualityLevel(level);
        }

        public void EnableAdaptiveQuality(bool enable)
        {
            enableAdaptiveQuality = enable;
            
            if (enable && adaptiveQualityCoroutine == null)
            {
                adaptiveQualityCoroutine = StartCoroutine(AdaptiveQualityRoutine());
            }
            else if (!enable && adaptiveQualityCoroutine != null)
            {
                StopCoroutine(adaptiveQualityCoroutine);
                adaptiveQualityCoroutine = null;
            }
        }

        public PerformanceReport GenerateReport()
        {
            return new PerformanceReport
            {
                deviceInfo = deviceInfo,
                currentPerformance = currentPerformance,
                currentQualityLevel = currentQualityLevel,
                qualityProfileName = qualityProfiles[currentQualityLevel].name,
                monitoringDuration = Time.time,
                adaptiveQualityEnabled = enableAdaptiveQuality
            };
        }

        void OnGUI()
        {
            if (!showDebugOverlay) return;
            
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label($"FPS: {currentPerformance.averageFPS:F1}");
            GUILayout.Label($"Frame Time: {currentPerformance.averageFrameTime * 1000:F1}ms");
            GUILayout.Label($"Memory: {currentPerformance.memoryUsageMB:F1} MB");
            GUILayout.Label($"CPU: {currentPerformance.cpuUsagePercent:F1}%");
            GUILayout.Label($"Quality: {qualityProfiles[currentQualityLevel].name}");
            GUILayout.Label($"Battery: {batteryInfo.level * 100:F0}%");
            GUILayout.EndArea();
        }
    }

    [System.Serializable]
    public class QualityProfile
    {
        public string name;
        public int targetFrameRate;
        public float renderScale;
        public ShadowQuality shadowQuality;
        public int antiAliasing;
        public int particleMaxCount;
        public bool enablePostProcessing;
        public int maxLODLevel;
        public int textureQuality;
    }

    [System.Serializable]
    public class PerformanceData
    {
        public float averageFPS;
        public float averageFrameTime;
        public long memoryUsageBytes;
        public float memoryUsageMB;
        public float cpuUsagePercent;
        public float batteryLevel;
        public float temperature;
    }

    [System.Serializable]
    public class DeviceInfo
    {
        public string deviceModel;
        public string deviceName;
        public string operatingSystem;
        public string processorType;
        public int processorCount;
        public int systemMemorySize;
        public string graphicsDeviceName;
        public int graphicsMemorySize;
        public int maxTextureSize;
        public bool supportsInstancing;
        public bool supportsComputeShaders;
        public DeviceType deviceType;
        public float batteryLevel;
        public BatteryStatus batteryStatus;
    }

    [System.Serializable]
    public class BatteryInfo
    {
        public float level;
        public BatteryStatus status;
        public float lastUpdate;
    }

    [System.Serializable]
    public class PerformanceReport
    {
        public DeviceInfo deviceInfo;
        public PerformanceData currentPerformance;
        public int currentQualityLevel;
        public string qualityProfileName;
        public float monitoringDuration;
        public bool adaptiveQualityEnabled;
    }

    public enum PerformanceWarning
    {
        LowFrameRate,
        HighMemoryUsage,
        HighCPUUsage,
        LowBattery,
        Overheating
    }
}