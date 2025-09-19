using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;

namespace RedRunner.Localization
{
    /// <summary>
    /// Comprehensive localization system supporting multiple languages and regions
    /// Handles text, audio, images, and cultural adaptations for global markets
    /// </summary>
    public class LocalizationManager : MonoBehaviour
    {
        private static LocalizationManager instance;
        public static LocalizationManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<LocalizationManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("LocalizationManager");
                        instance = go.AddComponent<LocalizationManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Localization Configuration")]
        [SerializeField] private SystemLanguage defaultLanguage = SystemLanguage.English;
        [SerializeField] private bool autoDetectLanguage = true;
        [SerializeField] private LanguageConfig[] supportedLanguages;
        [SerializeField] private string localizationDataPath = "Localization/";

        [Header("Text Settings")]
        [SerializeField] private bool enableRTLSupport = true;
        [SerializeField] private bool enableFontSubstitution = true;
        [SerializeField] private FontMapping[] fontMappings;

        [Header("Audio Localization")]
        [SerializeField] private bool enableAudioLocalization = true;
        [SerializeField] private AudioLocalizationMapping[] audioMappings;

        [Header("Image Localization")]
        [SerializeField] private bool enableImageLocalization = true;
        [SerializeField] private ImageLocalizationMapping[] imageMappings;

        [Header("Cultural Adaptations")]
        [SerializeField] private bool enableCulturalAdaptations = true;
        [SerializeField] private CulturalAdaptation[] culturalAdaptations;

        [Header("Testing")]
        [SerializeField] private bool enablePseudoLocalization = false;
        [SerializeField] private bool showLocalizationKeys = false;

        // Current localization state
        private SystemLanguage currentLanguage;
        private Dictionary<string, string> localizedStrings;
        private Dictionary<SystemLanguage, LanguageConfig> languageConfigs;
        private List<ILocalizable> localizableComponents;

        // Font management
        private Dictionary<SystemLanguage, Font> languageFonts;
        private Dictionary<SystemLanguage, TMP_FontAsset> tmpLanguageFonts;

        // Audio localization
        private Dictionary<string, AudioClip> localizedAudioClips;

        // Image localization
        private Dictionary<string, Sprite> localizedSprites;

        // Cultural settings
        private CultureInfo currentCultureInfo;

        // Events
        public static event Action<SystemLanguage> OnLanguageChanged;
        public static event Action OnLocalizationLoaded;
        public static event Action<string> OnLocalizationError;

        // Properties
        public SystemLanguage CurrentLanguage => currentLanguage;
        public bool IsInitialized { get; private set; }
        public CultureInfo CurrentCultureInfo => currentCultureInfo;

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
            localizedStrings = new Dictionary<string, string>();
            languageConfigs = new Dictionary<SystemLanguage, LanguageConfig>();
            localizableComponents = new List<ILocalizable>();
            languageFonts = new Dictionary<SystemLanguage, Font>();
            tmpLanguageFonts = new Dictionary<SystemLanguage, TMP_FontAsset>();
            localizedAudioClips = new Dictionary<string, AudioClip>();
            localizedSprites = new Dictionary<string, Sprite>();

            BuildLanguageConfigs();
            LoadSavedLanguage();
            StartCoroutine(InitializeLocalization());
        }

        private void BuildLanguageConfigs()
        {
            if (supportedLanguages != null)
            {
                foreach (var config in supportedLanguages)
                {
                    languageConfigs[config.language] = config;
                    
                    // Cache fonts
                    if (config.font != null)
                        languageFonts[config.language] = config.font;
                    
                    if (config.tmpFont != null)
                        tmpLanguageFonts[config.language] = config.tmpFont;
                }
            }
        }

        private void LoadSavedLanguage()
        {
            if (SaveGame.Exists("SelectedLanguage"))
            {
                try
                {
                    int languageIndex = SaveGame.Load<int>("SelectedLanguage");
                    currentLanguage = (SystemLanguage)languageIndex;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load saved language: {e.Message}");
                    currentLanguage = GetDefaultLanguage();
                }
            }
            else
            {
                currentLanguage = GetDefaultLanguage();
            }
        }

        private SystemLanguage GetDefaultLanguage()
        {
            if (autoDetectLanguage)
            {
                var systemLanguage = Application.systemLanguage;
                
                // Check if system language is supported
                if (languageConfigs.ContainsKey(systemLanguage))
                {
                    return systemLanguage;
                }
                
                // Fall back to closest match
                var closestLanguage = FindClosestSupportedLanguage(systemLanguage);
                if (closestLanguage != SystemLanguage.Unknown)
                {
                    return closestLanguage;
                }
            }
            
            return defaultLanguage;
        }

        private SystemLanguage FindClosestSupportedLanguage(SystemLanguage targetLanguage)
        {
            // Language family mappings for fallbacks
            var languageFamilies = new Dictionary<SystemLanguage, SystemLanguage[]>
            {
                { SystemLanguage.Chinese, new[] { SystemLanguage.ChineseSimplified, SystemLanguage.ChineseTraditional } },
                { SystemLanguage.ChineseSimplified, new[] { SystemLanguage.Chinese, SystemLanguage.ChineseTraditional } },
                { SystemLanguage.ChineseTraditional, new[] { SystemLanguage.Chinese, SystemLanguage.ChineseSimplified } },
                { SystemLanguage.Spanish, new[] { SystemLanguage.Spanish } },
                { SystemLanguage.Portuguese, new[] { SystemLanguage.Portuguese } },
                { SystemLanguage.Arabic, new[] { SystemLanguage.Arabic } }
            };

            if (languageFamilies.TryGetValue(targetLanguage, out var family))
            {
                foreach (var lang in family)
                {
                    if (languageConfigs.ContainsKey(lang))
                        return lang;
                }
            }

            return SystemLanguage.Unknown;
        }

        private IEnumerator InitializeLocalization()
        {
            Debug.Log($"Initializing localization for language: {currentLanguage}");

            // Load localization data
            yield return StartCoroutine(LoadLocalizationData(currentLanguage));

            // Initialize cultural adaptations
            if (enableCulturalAdaptations)
            {
                InitializeCulturalSettings();
            }

            // Find and register localizable components
            RegisterLocalizableComponents();

            // Apply localization
            ApplyLocalization();

            IsInitialized = true;
            OnLocalizationLoaded?.Invoke();

            // Analytics
            AnalyticsManager.Instance?.TrackEvent("localization_initialized", new Dictionary<string, object>
            {
                { "language", currentLanguage.ToString() },
                { "auto_detected", autoDetectLanguage },
                { "components_count", localizableComponents.Count }
            });
        }

        private IEnumerator LoadLocalizationData(SystemLanguage language)
        {
            string languageCode = GetLanguageCode(language);
            string dataPath = $"{localizationDataPath}{languageCode}";

            // Load text localization
            yield return StartCoroutine(LoadTextLocalization(dataPath));

            // Load audio localization
            if (enableAudioLocalization)
            {
                yield return StartCoroutine(LoadAudioLocalization(dataPath));
            }

            // Load image localization
            if (enableImageLocalization)
            {
                yield return StartCoroutine(LoadImageLocalization(dataPath));
            }
        }

        private IEnumerator LoadTextLocalization(string basePath)
        {
            string textPath = $"{basePath}/strings";
            
            // Try to load from Resources
            var textAsset = Resources.Load<TextAsset>(textPath);
            
            if (textAsset != null)
            {
                ParseLocalizationData(textAsset.text);
            }
            else
            {
                Debug.LogWarning($"Could not load text localization for path: {textPath}");
                
                // Fall back to default language if current language fails
                if (currentLanguage != defaultLanguage)
                {
                    string defaultPath = $"{localizationDataPath}{GetLanguageCode(defaultLanguage)}/strings";
                    var defaultTextAsset = Resources.Load<TextAsset>(defaultPath);
                    
                    if (defaultTextAsset != null)
                    {
                        ParseLocalizationData(defaultTextAsset.text);
                        Debug.Log($"Loaded fallback localization: {defaultLanguage}");
                    }
                }
            }
            
            yield return null;
        }

        private void ParseLocalizationData(string csvData)
        {
            localizedStrings.Clear();
            
            if (string.IsNullOrEmpty(csvData))
                return;

            string[] lines = csvData.Split('\n');
            
            for (int i = 1; i < lines.Length; i++) // Skip header row
            {
                if (string.IsNullOrEmpty(lines[i].Trim()))
                    continue;
                
                string[] columns = ParseCSVLine(lines[i]);
                
                if (columns.Length >= 2)
                {
                    string key = columns[0].Trim();
                    string value = columns[1].Trim();
                    
                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    
                    // Handle escape sequences
                    value = value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"");
                    
                    // Apply pseudo-localization if enabled
                    if (enablePseudoLocalization && currentLanguage != defaultLanguage)
                    {
                        value = ApplyPseudoLocalization(value);
                    }
                    
                    localizedStrings[key] = value;
                }
            }
            
            Debug.Log($"Loaded {localizedStrings.Count} localized strings");
        }

        private string[] ParseCSVLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            string currentValue = "";
            
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentValue);
                    currentValue = "";
                }
                else
                {
                    currentValue += c;
                }
            }
            
            result.Add(currentValue);
            return result.ToArray();
        }

        private string ApplyPseudoLocalization(string text)
        {
            // Pseudo-localization for testing
            // Adds brackets and extends text to simulate longer translations
            return $"[{text} àáâãäå]";
        }

        private IEnumerator LoadAudioLocalization(string basePath)
        {
            if (audioMappings == null) yield break;
            
            localizedAudioClips.Clear();
            
            foreach (var mapping in audioMappings)
            {
                string audioPath = $"{basePath}/audio/{mapping.key}";
                var audioClip = Resources.Load<AudioClip>(audioPath);
                
                if (audioClip != null)
                {
                    localizedAudioClips[mapping.key] = audioClip;
                }
                else if (mapping.fallbackClip != null)
                {
                    localizedAudioClips[mapping.key] = mapping.fallbackClip;
                }
                
                yield return null;
            }
            
            Debug.Log($"Loaded {localizedAudioClips.Count} localized audio clips");
        }

        private IEnumerator LoadImageLocalization(string basePath)
        {
            if (imageMappings == null) yield break;
            
            localizedSprites.Clear();
            
            foreach (var mapping in imageMappings)
            {
                string imagePath = $"{basePath}/images/{mapping.key}";
                var sprite = Resources.Load<Sprite>(imagePath);
                
                if (sprite != null)
                {
                    localizedSprites[mapping.key] = sprite;
                }
                else if (mapping.fallbackSprite != null)
                {
                    localizedSprites[mapping.key] = mapping.fallbackSprite;
                }
                
                yield return null;
            }
            
            Debug.Log($"Loaded {localizedSprites.Count} localized images");
        }

        private void InitializeCulturalSettings()
        {
            var config = languageConfigs.TryGetValue(currentLanguage, out var langConfig) ? langConfig : null;
            
            currentCultureInfo = new CultureInfo
            {
                language = currentLanguage,
                isRTL = config?.isRTL ?? false,
                dateFormat = config?.dateFormat ?? "MM/dd/yyyy",
                timeFormat = config?.timeFormat ?? "HH:mm",
                currencySymbol = config?.currencySymbol ?? "$",
                numberFormat = config?.numberFormat ?? "en-US"
            };
            
            // Apply cultural adaptations
            if (culturalAdaptations != null)
            {
                foreach (var adaptation in culturalAdaptations)
                {
                    if (Array.Exists(adaptation.targetLanguages, lang => lang == currentLanguage))
                    {
                        ApplyCulturalAdaptation(adaptation);
                    }
                }
            }
        }

        private void ApplyCulturalAdaptation(CulturalAdaptation adaptation)
        {
            switch (adaptation.adaptationType)
            {
                case CulturalAdaptationType.ColorScheme:
                    // Apply culture-specific color preferences
                    break;
                    
                case CulturalAdaptationType.ContentFilter:
                    // Apply content filtering for specific cultures
                    break;
                    
                case CulturalAdaptationType.CurrencyDisplay:
                    // Adapt currency display format
                    break;
                    
                case CulturalAdaptationType.DateTimeFormat:
                    // Already handled in CultureInfo
                    break;
            }
        }

        private void RegisterLocalizableComponents()
        {
            localizableComponents.Clear();
            
            // Find all ILocalizable components in the scene
            var localizableObjects = FindObjectsOfType<MonoBehaviour>();
            foreach (var obj in localizableObjects)
            {
                if (obj is ILocalizable localizable)
                {
                    localizableComponents.Add(localizable);
                }
            }
            
            // Auto-register UI text components
            var textComponents = FindObjectsOfType<Text>();
            foreach (var text in textComponents)
            {
                var localizableText = text.GetComponent<LocalizableText>();
                if (localizableText == null)
                {
                    localizableText = text.gameObject.AddComponent<LocalizableText>();
                    localizableText.AutoDetectKey();
                }
                localizableComponents.Add(localizableText);
            }
            
            // Auto-register TextMeshPro components
            var tmpTexts = FindObjectsOfType<TextMeshProUGUI>();
            foreach (var tmpText in tmpTexts)
            {
                var localizableText = tmpText.GetComponent<LocalizableTMPText>();
                if (localizableText == null)
                {
                    localizableText = tmpText.gameObject.AddComponent<LocalizableTMPText>();
                    localizableText.AutoDetectKey();
                }
                localizableComponents.Add(localizableText);
            }
            
            Debug.Log($"Registered {localizableComponents.Count} localizable components");
        }

        private void ApplyLocalization()
        {
            foreach (var component in localizableComponents)
            {
                try
                {
                    component.UpdateLocalization();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to update localization for component: {e.Message}");
                }
            }
        }

        public void ChangeLanguage(SystemLanguage newLanguage)
        {
            if (newLanguage == currentLanguage)
                return;
                
            if (!languageConfigs.ContainsKey(newLanguage))
            {
                Debug.LogWarning($"Language not supported: {newLanguage}");
                return;
            }
            
            var oldLanguage = currentLanguage;
            currentLanguage = newLanguage;
            
            // Save language preference
            SaveGame.Save("SelectedLanguage", (int)currentLanguage);
            
            // Reload localization
            StartCoroutine(ReloadLocalization());
            
            OnLanguageChanged?.Invoke(newLanguage);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("language_changed", new Dictionary<string, object>
            {
                { "old_language", oldLanguage.ToString() },
                { "new_language", newLanguage.ToString() }
            });
        }

        private IEnumerator ReloadLocalization()
        {
            yield return StartCoroutine(LoadLocalizationData(currentLanguage));
            InitializeCulturalSettings();
            RegisterLocalizableComponents();
            ApplyLocalization();
        }

        public string GetLocalizedString(string key, params object[] args)
        {
            if (localizedStrings.TryGetValue(key, out string localizedText))
            {
                if (args != null && args.Length > 0)
                {
                    try
                    {
                        return string.Format(localizedText, args);
                    }
                    catch (FormatException e)
                    {
                        Debug.LogError($"Format error for localization key '{key}': {e.Message}");
                        return localizedText;
                    }
                }
                return localizedText;
            }
            
            // Return key with indicator if not found
            string fallback = showLocalizationKeys ? $"[{key}]" : key;
            
            Debug.LogWarning($"Localization key not found: {key}");
            OnLocalizationError?.Invoke($"Missing key: {key}");
            
            return fallback;
        }

        public AudioClip GetLocalizedAudioClip(string key)
        {
            return localizedAudioClips.TryGetValue(key, out AudioClip clip) ? clip : null;
        }

        public Sprite GetLocalizedSprite(string key)
        {
            return localizedSprites.TryGetValue(key, out Sprite sprite) ? sprite : null;
        }

        public Font GetCurrentLanguageFont()
        {
            return languageFonts.TryGetValue(currentLanguage, out Font font) ? font : null;
        }

        public TMP_FontAsset GetCurrentLanguageTMPFont()
        {
            return tmpLanguageFonts.TryGetValue(currentLanguage, out TMP_FontAsset font) ? font : null;
        }

        public string FormatCurrency(float amount)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(currentCultureInfo.numberFormat);
            return amount.ToString("C", culture);
        }

        public string FormatDate(DateTime date)
        {
            return date.ToString(currentCultureInfo.dateFormat);
        }

        public string FormatTime(DateTime time)
        {
            return time.ToString(currentCultureInfo.timeFormat);
        }

        public bool IsRTLLanguage()
        {
            return currentCultureInfo.isRTL;
        }

        public void RegisterLocalizable(ILocalizable localizable)
        {
            if (!localizableComponents.Contains(localizable))
            {
                localizableComponents.Add(localizable);
                
                if (IsInitialized)
                {
                    localizable.UpdateLocalization();
                }
            }
        }

        public void UnregisterLocalizable(ILocalizable localizable)
        {
            localizableComponents.Remove(localizable);
        }

        public List<SystemLanguage> GetSupportedLanguages()
        {
            return new List<SystemLanguage>(languageConfigs.Keys);
        }

        public string GetLanguageDisplayName(SystemLanguage language)
        {
            if (languageConfigs.TryGetValue(language, out var config))
            {
                return config.displayName;
            }
            return language.ToString();
        }

        private string GetLanguageCode(SystemLanguage language)
        {
            var languageCodes = new Dictionary<SystemLanguage, string>
            {
                { SystemLanguage.English, "en" },
                { SystemLanguage.Spanish, "es" },
                { SystemLanguage.French, "fr" },
                { SystemLanguage.German, "de" },
                { SystemLanguage.Italian, "it" },
                { SystemLanguage.Portuguese, "pt" },
                { SystemLanguage.Russian, "ru" },
                { SystemLanguage.Japanese, "ja" },
                { SystemLanguage.Korean, "ko" },
                { SystemLanguage.Chinese, "zh" },
                { SystemLanguage.ChineseSimplified, "zh-CN" },
                { SystemLanguage.ChineseTraditional, "zh-TW" },
                { SystemLanguage.Arabic, "ar" },
                { SystemLanguage.Dutch, "nl" },
                { SystemLanguage.Polish, "pl" },
                { SystemLanguage.Turkish, "tr" },
                { SystemLanguage.Swedish, "sv" },
                { SystemLanguage.Danish, "da" },
                { SystemLanguage.Norwegian, "no" },
                { SystemLanguage.Finnish, "fi" },
                { SystemLanguage.Czech, "cs" },
                { SystemLanguage.Hungarian, "hu" },
                { SystemLanguage.Greek, "el" },
                { SystemLanguage.Hebrew, "he" },
                { SystemLanguage.Thai, "th" },
                { SystemLanguage.Vietnamese, "vi" },
                { SystemLanguage.Indonesian, "id" }
            };
            
            return languageCodes.TryGetValue(language, out string code) ? code : "en";
        }

        void OnValidate()
        {
            // Validate configuration in editor
            if (supportedLanguages != null)
            {
                foreach (var config in supportedLanguages)
                {
                    if (string.IsNullOrEmpty(config.displayName))
                    {
                        config.displayName = config.language.ToString();
                    }
                }
            }
        }
    }

    // Interfaces and components
    public interface ILocalizable
    {
        void UpdateLocalization();
    }

    // Data structures
    [System.Serializable]
    public class LanguageConfig
    {
        public SystemLanguage language;
        public string displayName;
        public string languageCode;
        public bool isRTL = false;
        public Font font;
        public TMP_FontAsset tmpFont;
        public string dateFormat = "MM/dd/yyyy";
        public string timeFormat = "HH:mm";
        public string currencySymbol = "$";
        public string numberFormat = "en-US";
    }

    [System.Serializable]
    public class FontMapping
    {
        public SystemLanguage language;
        public Font font;
        public TMP_FontAsset tmpFont;
    }

    [System.Serializable]
    public class AudioLocalizationMapping
    {
        public string key;
        public AudioClip fallbackClip;
    }

    [System.Serializable]
    public class ImageLocalizationMapping
    {
        public string key;
        public Sprite fallbackSprite;
    }

    [System.Serializable]
    public class CulturalAdaptation
    {
        public string name;
        public SystemLanguage[] targetLanguages;
        public CulturalAdaptationType adaptationType;
        public object adaptationData;
    }

    [System.Serializable]
    public class CultureInfo
    {
        public SystemLanguage language;
        public bool isRTL;
        public string dateFormat;
        public string timeFormat;
        public string currencySymbol;
        public string numberFormat;
    }

    // Localizable UI components
    public class LocalizableText : MonoBehaviour, ILocalizable
    {
        [SerializeField] private string localizationKey;
        [SerializeField] private bool autoDetectKey = true;
        
        private Text textComponent;
        
        void Awake()
        {
            textComponent = GetComponent<Text>();
            
            if (autoDetectKey && string.IsNullOrEmpty(localizationKey))
            {
                AutoDetectKey();
            }
        }
        
        void Start()
        {
            LocalizationManager.Instance.RegisterLocalizable(this);
        }
        
        void OnDestroy()
        {
            if (LocalizationManager.Instance != null)
            {
                LocalizationManager.Instance.UnregisterLocalizable(this);
            }
        }
        
        public void AutoDetectKey()
        {
            if (textComponent != null && !string.IsNullOrEmpty(textComponent.text))
            {
                // Create key from text content
                localizationKey = textComponent.text.Replace(" ", "_").ToLower();
            }
        }
        
        public void UpdateLocalization()
        {
            if (textComponent != null && !string.IsNullOrEmpty(localizationKey))
            {
                textComponent.text = LocalizationManager.Instance.GetLocalizedString(localizationKey);
                
                // Apply font if needed
                var font = LocalizationManager.Instance.GetCurrentLanguageFont();
                if (font != null)
                {
                    textComponent.font = font;
                }
            }
        }
        
        public void SetLocalizationKey(string key)
        {
            localizationKey = key;
            UpdateLocalization();
        }
    }

    public class LocalizableTMPText : MonoBehaviour, ILocalizable
    {
        [SerializeField] private string localizationKey;
        [SerializeField] private bool autoDetectKey = true;
        
        private TextMeshProUGUI textComponent;
        
        void Awake()
        {
            textComponent = GetComponent<TextMeshProUGUI>();
            
            if (autoDetectKey && string.IsNullOrEmpty(localizationKey))
            {
                AutoDetectKey();
            }
        }
        
        void Start()
        {
            LocalizationManager.Instance.RegisterLocalizable(this);
        }
        
        void OnDestroy()
        {
            if (LocalizationManager.Instance != null)
            {
                LocalizationManager.Instance.UnregisterLocalizable(this);
            }
        }
        
        public void AutoDetectKey()
        {
            if (textComponent != null && !string.IsNullOrEmpty(textComponent.text))
            {
                localizationKey = textComponent.text.Replace(" ", "_").ToLower();
            }
        }
        
        public void UpdateLocalization()
        {
            if (textComponent != null && !string.IsNullOrEmpty(localizationKey))
            {
                textComponent.text = LocalizationManager.Instance.GetLocalizedString(localizationKey);
                
                // Apply TMP font if needed
                var font = LocalizationManager.Instance.GetCurrentLanguageTMPFont();
                if (font != null)
                {
                    textComponent.font = font;
                }
                
                // Handle RTL text if needed
                if (LocalizationManager.Instance.IsRTLLanguage())
                {
                    textComponent.alignment = TextAlignmentOptions.TopRight;
                    textComponent.isRightToLeftText = true;
                }
                else
                {
                    textComponent.alignment = TextAlignmentOptions.TopLeft;
                    textComponent.isRightToLeftText = false;
                }
            }
        }
        
        public void SetLocalizationKey(string key)
        {
            localizationKey = key;
            UpdateLocalization();
        }
    }

    public class LocalizableImage : MonoBehaviour, ILocalizable
    {
        [SerializeField] private string localizationKey;
        
        private Image imageComponent;
        
        void Awake()
        {
            imageComponent = GetComponent<Image>();
        }
        
        void Start()
        {
            LocalizationManager.Instance.RegisterLocalizable(this);
        }
        
        void OnDestroy()
        {
            if (LocalizationManager.Instance != null)
            {
                LocalizationManager.Instance.UnregisterLocalizable(this);
            }
        }
        
        public void UpdateLocalization()
        {
            if (imageComponent != null && !string.IsNullOrEmpty(localizationKey))
            {
                var sprite = LocalizationManager.Instance.GetLocalizedSprite(localizationKey);
                if (sprite != null)
                {
                    imageComponent.sprite = sprite;
                }
            }
        }
        
        public void SetLocalizationKey(string key)
        {
            localizationKey = key;
            UpdateLocalization();
        }
    }

    public class LocalizableAudioSource : MonoBehaviour, ILocalizable
    {
        [SerializeField] private string localizationKey;
        
        private AudioSource audioSource;
        
        void Awake()
        {
            audioSource = GetComponent<AudioSource>();
        }
        
        void Start()
        {
            LocalizationManager.Instance.RegisterLocalizable(this);
        }
        
        void OnDestroy()
        {
            if (LocalizationManager.Instance != null)
            {
                LocalizationManager.Instance.UnregisterLocalizable(this);
            }
        }
        
        public void UpdateLocalization()
        {
            if (audioSource != null && !string.IsNullOrEmpty(localizationKey))
            {
                var audioClip = LocalizationManager.Instance.GetLocalizedAudioClip(localizationKey);
                if (audioClip != null)
                {
                    audioSource.clip = audioClip;
                }
            }
        }
        
        public void SetLocalizationKey(string key)
        {
            localizationKey = key;
            UpdateLocalization();
        }
    }

    // Enums
    public enum CulturalAdaptationType
    {
        ColorScheme,
        ContentFilter,
        CurrencyDisplay,
        DateTimeFormat,
        NumberFormat,
        TextDirection
    }
}