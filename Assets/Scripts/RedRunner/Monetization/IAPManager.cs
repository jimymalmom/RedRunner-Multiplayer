using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;
using RedRunner.Progression;

namespace RedRunner.Monetization
{
    /// <summary>
    /// Comprehensive In-App Purchase system with security, analytics, and progression integration
    /// Handles all IAP functionality including consumables, non-consumables, and subscriptions
    /// </summary>
    public class IAPManager : MonoBehaviour, IStoreListener
    {
        private static IAPManager instance;
        public static IAPManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<IAPManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("IAPManager");
                        instance = go.AddComponent<IAPManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("IAP Configuration")]
        [SerializeField] private IAPProduct[] products;
        [SerializeField] private bool enableIAP = true;
        [SerializeField] private bool enableReceiptValidation = true;
        [SerializeField] private string serverValidationUrl = "";
        
        [Header("Special Offers")]
        [SerializeField] private SpecialOffer[] specialOffers;
        [SerializeField] private float offerCheckInterval = 300f; // 5 minutes
        
        [Header("Security")]
        [SerializeField] private bool enableAntiPiracy = true;
        [SerializeField] private int maxPurchaseRetries = 3;
        [SerializeField] private float purchaseTimeout = 30f;
        
        // Unity IAP
        private static IStoreController storeController;
        private static IExtensionProvider storeExtensionProvider;
        
        // State management
        private Dictionary<string, IAPProduct> productLookup;
        private Dictionary<string, PurchaseState> purchaseStates;
        private List<string> ownedNonConsumables;
        private PurchaseData purchaseData;
        
        // Coroutines
        private Coroutine offerUpdateCoroutine;
        
        // Events
        public static event Action<string> OnPurchaseSucceeded;
        public static event Action<string, PurchaseFailureReason> OnPurchaseFailedEvent;
        public static event Action<IAPProduct> OnProductPurchased;
        public static event Action<SpecialOffer> OnSpecialOfferAvailable;
        public static event Action<string> OnSubscriptionExpired;
        public static event Action OnStoreInitialized;
        
        public bool IsInitialized => storeController != null && storeExtensionProvider != null;
        public List<string> OwnedNonConsumables => ownedNonConsumables ?? new List<string>();

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
            LoadPurchaseData();
            BuildProductLookup();
            
            purchaseStates = new Dictionary<string, PurchaseState>();
            ownedNonConsumables = new List<string>();
            
            if (enableIAP)
            {
                InitializeStore();
            }
            
            // Start special offers update
            if (specialOffers != null && specialOffers.Length > 0)
            {
                offerUpdateCoroutine = StartCoroutine(UpdateSpecialOffers());
            }
        }

        private void LoadPurchaseData()
        {
            if (SaveGame.Exists("PurchaseData"))
            {
                try
                {
                    string json = SaveGame.Load<string>("PurchaseData");
                    purchaseData = JsonUtility.FromJson<PurchaseData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load purchase data: {e.Message}");
                    CreateNewPurchaseData();
                }
            }
            else
            {
                CreateNewPurchaseData();
            }
        }

        private void CreateNewPurchaseData()
        {
            purchaseData = new PurchaseData
            {
                purchaseHistory = new List<PurchaseRecord>(),
                ownedProducts = new List<string>(),
                subscriptions = new List<SubscriptionInfo>(),
                totalSpent = 0f,
                firstPurchaseDate = "",
                lastPurchaseDate = ""
            };
            
            SavePurchaseData();
        }

        private void SavePurchaseData()
        {
            try
            {
                string json = JsonUtility.ToJson(purchaseData);
                SaveGame.Save("PurchaseData", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save purchase data: {e.Message}");
            }
        }

        private void BuildProductLookup()
        {
            productLookup = new Dictionary<string, IAPProduct>();
            
            if (products != null)
            {
                foreach (var product in products)
                {
                    productLookup[product.id] = product;
                }
            }
        }

        private void InitializeStore()
        {
            if (IsInitialized)
                return;
            
            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            
            // Add products to the builder
            foreach (var product in products)
            {
                builder.AddProduct(product.id, product.type);
            }
            
            // Initialize purchasing
            UnityPurchasing.Initialize(this, builder);
        }

        // IStoreListener implementation
        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            Debug.Log("Unity Purchasing initialized successfully");
            
            storeController = controller;
            storeExtensionProvider = extensions;
            
            // Restore previous purchases
            RestorePurchases();
            
            OnStoreInitialized?.Invoke();
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("iap_store_initialized", new Dictionary<string, object>
            {
                { "product_count", products.Length },
                { "owned_products", ownedNonConsumables.Count }
            });
        }

public void OnInitializeFailed(InitializationFailureReason error)
        {
            Debug.LogError($"Unity Purchasing initialization failed: {error}");
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("iap_store_init_failed", new Dictionary<string, object>
            {
                { "error", error.ToString() }
            });
        }

public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            Debug.LogError($"Unity Purchasing initialization failed: {error} - {message}");
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("iap_store_init_failed", new Dictionary<string, object>
            {
                { "error", error.ToString() },
                { "message", message }
            });
        }


public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            var product = args.purchasedProduct;
            
            Debug.Log($"Processing purchase: {product.definition.id}");
            
            // Validate receipt if enabled
            if (enableReceiptValidation && !ValidateReceipt(product))
            {
                Debug.LogError("Receipt validation failed");
                OnPurchaseFailedEvent?.Invoke(product.definition.id, PurchaseFailureReason.SignatureInvalid);
                return PurchaseProcessingResult.Complete;
            }
            
            // Process the purchase
            ProcessValidPurchase(product);
            
            return PurchaseProcessingResult.Complete;
        }

public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            Debug.LogError($"Purchase failed: {product.definition.id} - {failureReason}");
            
            // Update purchase state
            if (purchaseStates.ContainsKey(product.definition.id))
            {
                purchaseStates[product.definition.id].status = PurchaseStatus.Failed;
                purchaseStates[product.definition.id].failureReason = failureReason;
            }
            
            OnPurchaseFailedEvent?.Invoke(product.definition.id, failureReason);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("iap_purchase_failed", new Dictionary<string, object>
            {
                { "product_id", product.definition.id },
                { "failure_reason", failureReason.ToString() },
                { "price", GetProductPrice(product.definition.id) }
            });
        }




        public void PurchaseProduct(string productId)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("Store not initialized");
                return;
            }
            
            if (!productLookup.ContainsKey(productId))
            {
                Debug.LogError($"Product not found: {productId}");
                return;
            }
            
            var product = storeController.products.WithID(productId);
            if (product == null || !product.availableToPurchase)
            {
                Debug.LogError($"Product not available for purchase: {productId}");
                return;
            }
            
            // Check if already owned (for non-consumables)
            var productConfig = productLookup[productId];
            if (productConfig.type == ProductType.NonConsumable && IsProductOwned(productId))
            {
                Debug.Log($"Product already owned: {productId}");
                return;
            }
            
            // Track purchase attempt
            var purchaseState = new PurchaseState
            {
                productId = productId,
                status = PurchaseStatus.Pending,
                timestamp = DateTime.UtcNow
            };
            
            purchaseStates[productId] = purchaseState;
            
            // Start purchase
            storeController.InitiatePurchase(product);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("iap_purchase_initiated", new Dictionary<string, object>
            {
                { "product_id", productId },
                { "price", product.metadata.localizedPriceString },
                { "currency", product.metadata.isoCurrencyCode }
            });
        }

        private void ProcessValidPurchase(Product product)
        {
            var productId = product.definition.id;
            var productConfig = productLookup[productId];
            
            // Create purchase record
            var purchaseRecord = new PurchaseRecord
            {
                productId = productId,
                transactionId = product.transactionID,
                receipt = product.receipt,
                price = decimal.Parse(product.metadata.localizedPrice.ToString()),
                currency = product.metadata.isoCurrencyCode,
                timestamp = DateTime.UtcNow
            };
            
            purchaseData.purchaseHistory.Add(purchaseRecord);
            purchaseData.totalSpent += (float)purchaseRecord.price;
            
            if (string.IsNullOrEmpty(purchaseData.firstPurchaseDate))
            {
                purchaseData.firstPurchaseDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            
            purchaseData.lastPurchaseDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            
            // Handle different product types
            switch (productConfig.type)
            {
                case ProductType.Consumable:
                    ProcessConsumablePurchase(productConfig);
                    break;
                    
                case ProductType.NonConsumable:
                    ProcessNonConsumablePurchase(productConfig);
                    break;
                    
                case ProductType.Subscription:
                    ProcessSubscriptionPurchase(productConfig, product);
                    break;
            }
            
            // Update purchase state
            if (purchaseStates.ContainsKey(productId))
            {
                purchaseStates[productId].status = PurchaseStatus.Completed;
            }
            
            OnPurchaseSucceeded?.Invoke(productId);
            OnProductPurchased?.Invoke(productConfig);
            
            // Analytics
            AnalyticsManager.Instance?.TrackBusinessEvent(
                purchaseRecord.currency,
                (int)(purchaseRecord.price * 100), // Convert to cents
                productConfig.category,
                productId,
                purchaseRecord.receipt
            );
            
            SavePurchaseData();
        }

        private void ProcessConsumablePurchase(IAPProduct product)
        {
            // Grant consumable rewards
            foreach (var reward in product.rewards)
            {
                switch (reward.type)
                {
                    case RewardType.Coins:
                        ProgressionManager.Instance?.AddCurrency(CurrencyType.Coins, reward.amount);
                        break;
                        
                    case RewardType.Gems:
                        ProgressionManager.Instance?.AddCurrency(CurrencyType.Gems, reward.amount);
                        break;
                        
                    case RewardType.Experience:
                        ProgressionManager.Instance?.AddExperience(reward.amount, "iap_purchase");
                        break;
                }
            }
            
            Debug.Log($"Consumable purchase processed: {product.displayName}");
        }

        private void ProcessNonConsumablePurchase(IAPProduct product)
        {
            // Add to owned products
            if (!ownedNonConsumables.Contains(product.id))
            {
                ownedNonConsumables.Add(product.id);
                purchaseData.ownedProducts.Add(product.id);
            }
            
            // Grant one-time rewards
            foreach (var reward in product.rewards)
            {
                switch (reward.type)
                {
                    case RewardType.Character:
                        ProgressionManager.Instance?.UnlockContent($"character_{reward.itemId}");
                        break;
                        
                    case RewardType.RemoveAds:
                        SetAdRemovalStatus(true);
                        break;
                        
                    case RewardType.PremiumCurrency:
                        ProgressionManager.Instance?.AddCurrency(CurrencyType.Gems, reward.amount);
                        break;
                }
            }
            
            Debug.Log($"Non-consumable purchase processed: {product.displayName}");
        }

        private void ProcessSubscriptionPurchase(IAPProduct product, Product unityProduct)
        {
            var subscriptionManager = new SubscriptionManager(unityProduct, null);
            var subscriptionInfo = subscriptionManager.getSubscriptionInfo();
            
            var subscription = new SubscriptionInfo
            {
                productId = product.id,
                subscriptionId = unityProduct.transactionID,
                startDate = DateTime.UtcNow,
                endDate = subscriptionInfo.getExpireDate(),
                isActive = true,
                autoRenewing = subscriptionInfo.isAutoRenewing() == Result.True
            };
            
            purchaseData.subscriptions.Add(subscription);
            
            // Grant subscription benefits
            ApplySubscriptionBenefits(product, true);
            
            Debug.Log($"Subscription purchase processed: {product.displayName}");
        }

        private bool ValidateReceipt(Product product)
        {
            if (!enableReceiptValidation)
                return true;
            
            // Basic client-side validation
            if (string.IsNullOrEmpty(product.receipt))
                return false;
            
            // TODO: Implement server-side receipt validation
            // This should send the receipt to your server for validation
            // against Apple/Google servers
            
            return true;
        }

        private void RestorePurchases()
        {
            if (!IsInitialized)
                return;
            
            // Restore non-consumables and subscriptions
            foreach (var product in storeController.products.all)
            {
                if (product.hasReceipt)
                {
                    var productConfig = productLookup.ContainsKey(product.definition.id) ? 
                        productLookup[product.definition.id] : null;
                    
                    if (productConfig != null)
                    {
                        switch (productConfig.type)
                        {
                            case ProductType.NonConsumable:
                                if (!ownedNonConsumables.Contains(product.definition.id))
                                {
                                    ownedNonConsumables.Add(product.definition.id);
                                }
                                break;
                                
                            case ProductType.Subscription:
                                ValidateSubscription(product);
                                break;
                        }
                    }
                }
            }
        }

        private void ValidateSubscription(Product product)
        {
            var subscriptionManager = new SubscriptionManager(product, null);
            var subscriptionInfo = subscriptionManager.getSubscriptionInfo();
            
            bool isActive = subscriptionInfo.isSubscribed() == Result.True;
            bool isExpired = subscriptionInfo.isExpired() == Result.True;
            
            var existingSubscription = purchaseData.subscriptions
                .Find(s => s.productId == product.definition.id);
            
            if (existingSubscription != null)
            {
                existingSubscription.isActive = isActive;
                existingSubscription.endDate = subscriptionInfo.getExpireDate();
                
                if (isExpired && existingSubscription.isActive)
                {
                    existingSubscription.isActive = false;
                    OnSubscriptionExpired?.Invoke(product.definition.id);
                    
                    // Remove subscription benefits
                    var productConfig = productLookup[product.definition.id];
                    ApplySubscriptionBenefits(productConfig, false);
                }
            }
        }

        private void ApplySubscriptionBenefits(IAPProduct product, bool enable)
        {
            foreach (var reward in product.rewards)
            {
                switch (reward.type)
                {
                    case RewardType.RemoveAds:
                        SetAdRemovalStatus(enable);
                        break;
                        
                    case RewardType.PremiumFeatures:
                        SetPremiumStatus(enable);
                        break;
                        
                    case RewardType.CoinMultiplier:
                        SetCoinMultiplier(enable ? reward.amount : 1);
                        break;
                }
            }
        }

        private IEnumerator UpdateSpecialOffers()
        {
            while (true)
            {
                yield return new WaitForSeconds(offerCheckInterval);
                
                foreach (var offer in specialOffers)
                {
                    if (IsOfferActive(offer) && !HasSeenOffer(offer.id))
                    {
                        OnSpecialOfferAvailable?.Invoke(offer);
                        MarkOfferAsSeen(offer.id);
                    }
                }
            }
        }

        private bool IsOfferActive(SpecialOffer offer)
        {
            var now = DateTime.UtcNow;
            return now >= offer.startDate && now <= offer.endDate;
        }

        private bool HasSeenOffer(string offerId)
        {
            return PlayerPrefs.GetInt($"SeenOffer_{offerId}", 0) == 1;
        }

        private void MarkOfferAsSeen(string offerId)
        {
            PlayerPrefs.SetInt($"SeenOffer_{offerId}", 1);
        }

        // Public API methods
        public bool IsProductOwned(string productId)
        {
            return ownedNonConsumables.Contains(productId);
        }

        public bool HasActiveSubscription(string productId)
        {
            var subscription = purchaseData.subscriptions.Find(s => s.productId == productId);
            return subscription != null && subscription.isActive && DateTime.UtcNow < subscription.endDate;
        }

        public string GetProductPrice(string productId)
        {
            if (!IsInitialized)
                return "N/A";
            
            var product = storeController.products.WithID(productId);
            return product?.metadata.localizedPriceString ?? "N/A";
        }

        public IAPProduct GetProductConfig(string productId)
        {
            return productLookup.TryGetValue(productId, out var product) ? product : null;
        }

        public PurchaseData GetPurchaseData()
        {
            return purchaseData;
        }

        public List<SpecialOffer> GetActiveOffers()
        {
            var activeOffers = new List<SpecialOffer>();
            
            foreach (var offer in specialOffers)
            {
                if (IsOfferActive(offer))
                {
                    activeOffers.Add(offer);
                }
            }
            
            return activeOffers;
        }

        private void SetAdRemovalStatus(bool removed)
        {
            PlayerPrefs.SetInt("AdsRemoved", removed ? 1 : 0);
        }

        private void SetPremiumStatus(bool premium)
        {
            PlayerPrefs.SetInt("PremiumUser", premium ? 1 : 0);
        }

        private void SetCoinMultiplier(int multiplier)
        {
            PlayerPrefs.SetInt("CoinMultiplier", multiplier);
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SavePurchaseData();
            }
            else
            {
                // Check subscriptions when returning to app
                ValidateSubscriptions();
            }
        }

        private void ValidateSubscriptions()
        {
            foreach (var subscription in purchaseData.subscriptions)
            {
                if (subscription.isActive && IsInitialized)
                {
                    var product = storeController.products.WithID(subscription.productId);
                    if (product != null)
                    {
                        ValidateSubscription(product);
                    }
                }
            }
        }

        void OnDestroy()
        {
            SavePurchaseData();
            
            if (offerUpdateCoroutine != null)
            {
                StopCoroutine(offerUpdateCoroutine);
            }
        }
    }

    // Data structures
    [System.Serializable]
    public class IAPProduct
    {
        public string id;
        public string displayName;
        public string description;
        public ProductType type;
        public string category;
        public Sprite icon;
        public ProductReward[] rewards;
        public float discountPercentage;
        public bool isFeatured;
    }

    [System.Serializable]
    public class ProductReward
    {
        public RewardType type;
        public int amount;
        public string itemId;
    }

    [System.Serializable]
    public class PurchaseData
    {
        public List<PurchaseRecord> purchaseHistory;
        public List<string> ownedProducts;
        public List<SubscriptionInfo> subscriptions;
        public float totalSpent;
        public string firstPurchaseDate;
        public string lastPurchaseDate;
    }

    [System.Serializable]
    public class PurchaseRecord
    {
        public string productId;
        public string transactionId;
        public string receipt;
        public decimal price;
        public string currency;
        public DateTime timestamp;
    }

    [System.Serializable]
    public class SubscriptionInfo
    {
        public string productId;
        public string subscriptionId;
        public DateTime startDate;
        public DateTime endDate;
        public bool isActive;
        public bool autoRenewing;
    }

    [System.Serializable]
    public class PurchaseState
    {
        public string productId;
        public PurchaseStatus status;
        public DateTime timestamp;
        public PurchaseFailureReason failureReason;
    }

    [System.Serializable]
    public class SpecialOffer
    {
        public string id;
        public string title;
        public string description;
        public string productId;
        public float discountPercentage;
        public DateTime startDate;
        public DateTime endDate;
        public Sprite bannerImage;
        public bool isLimitedTime;
    }

    // Enums
    public enum RewardType
    {
        Coins,
        Gems,
        Experience,
        Character,
        RemoveAds,
        PremiumCurrency,
        PremiumFeatures,
        CoinMultiplier,
        SpecialItem
    }

    public enum PurchaseStatus
    {
        None,
        Pending,
        Completed,
        Failed,
        Refunded
    }
}