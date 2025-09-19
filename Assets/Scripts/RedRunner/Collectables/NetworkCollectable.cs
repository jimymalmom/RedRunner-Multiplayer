using UnityEngine;
using System.Collections.Generic;
using RedRunner.Core;
using RedRunner.Networking;
using RedRunner.Analytics;

namespace RedRunner.Collectables
{
    /// <summary>
    /// Enhanced collectible system with networking support and object pooling
    /// Handles coins, gems, power-ups, and special items with server validation
    /// </summary>
    public class NetworkCollectable : Collectable, Core.IPoolable
    {
        [Header("Network Settings")]
        [SerializeField] private uint collectibleId;
        [SerializeField] private bool requireServerValidation = true;
        [SerializeField] private float networkSyncInterval = 0.1f;
        
        [Header("Collectible Properties")]
        [SerializeField] private CollectibleType collectibleType = CollectibleType.Coin;
        [SerializeField] private int baseValue = 1;
        [SerializeField] private int multiplierValue = 1;
        [SerializeField] private float magnetRange = 2f;
        [SerializeField] private float magnetSpeed = 8f;
        
        [Header("Visual Effects")]
        [SerializeField] private ParticleSystem collectEffect;
        [SerializeField] private AudioClip collectSound;
        [SerializeField] private GameObject floatingTextPrefab;
        [SerializeField] private AnimationCurve collectAnimation = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        [Header("Special Effects")]
        [SerializeField] private bool enableMagnetEffect = true;
        [SerializeField] private float glowIntensity = 1f;
        [SerializeField] private Color glowColor = Color.yellow;
        
        // Network state
        private bool isCollected = false;
        private uint collectedByPlayer = 0;
        private float lastNetworkSync = 0f;
        
        // Magnet effect
        private Transform targetPlayer;
        private bool isBeingMagneted = false;
        private Vector3 originalPosition;
        
        // Visual components
        private Renderer[] renderers;
        private Material[] originalMaterials;
        private Material glowMaterial;
        
        // Animation
        private float animationTime = 0f;
        private Vector3 startScale;
        
        public uint CollectibleId => collectibleId;
        public CollectibleType Type => collectibleType;
        public int Value => baseValue * multiplierValue;
        
        
        // Abstract implementations required by Collectable base class
        private SpriteRenderer spriteRenderer;
        private Collider2D cachedCollider2D;
        private Animator animator;
        private bool useOnTriggerEnter2D = true;
        
        public override SpriteRenderer SpriteRenderer => spriteRenderer ?? (spriteRenderer = GetComponent<SpriteRenderer>());
        public override Collider2D Collider2D => cachedCollider2D ?? (cachedCollider2D = GetComponent<Collider2D>());
        public override Animator Animator => animator ?? (animator = GetComponent<Animator>());
        public override bool UseOnTriggerEnter2D { get => useOnTriggerEnter2D; set => useOnTriggerEnter2D = value; }
        
        public override void OnTriggerEnter2D(Collider2D other)
        {
            var character = other.GetComponent<Characters.Character>();
            if (character != null)
            {
                TryCollect(character);
            }
        }
        
        public override void OnCollisionEnter2D(Collision2D collision2D)
        {
            var character = collision2D.gameObject.GetComponent<Characters.Character>();
            if (character != null)
            {
                TryCollect(character);
            }
        }
        
        public override void Collect()
        {
            if (!isCollected)
            {
                isCollected = true;
                PlayCollectionEffects(true);
                ReturnToPool();
            }
        }
public bool IsCollected => isCollected;

        void Awake()
        {
            // Initialize base collectable components
            
            // Generate unique ID if not set
            if (collectibleId == 0)
            {
                collectibleId = (uint)Random.Range(1, int.MaxValue);
            }
            
            // Cache components
            renderers = GetComponentsInChildren<Renderer>();
            originalMaterials = new Material[renderers.Length];
            
            for (int i = 0; i < renderers.Length; i++)
            {
                originalMaterials[i] = renderers[i].material;
            }
            
            startScale = transform.localScale;
            originalPosition = transform.position;
            
            // Create glow material
            CreateGlowMaterial();
        }

        void Start()
        {
            // Register with network game state if multiplayer
            if (NetworkGameManager.Instance?.IsMultiplayer == true)
            {
                RegisterWithNetworkState();
            }
            
            // Start idle animation
            StartIdleAnimation();
        }

        void Update()
        {
            if (isCollected) return;
            
            // Update idle animation
            UpdateIdleAnimation();
            
            // Handle magnet effect
            if (enableMagnetEffect && !isBeingMagneted)
            {
                CheckForMagnetEffect();
            }
            
            if (isBeingMagneted && targetPlayer != null)
            {
                UpdateMagnetMovement();
            }
            
            // Network synchronization
            if (NetworkGameManager.Instance?.IsMultiplayer == true && Time.time - lastNetworkSync > networkSyncInterval)
            {
                SynchronizeNetworkState();
                lastNetworkSync = Time.time;
            }
        }

        private void CreateGlowMaterial()
        {
            if (renderers.Length > 0 && originalMaterials[0] != null)
            {
                glowMaterial = new Material(originalMaterials[0]);
                glowMaterial.EnableKeyword("_EMISSION");
                glowMaterial.SetColor("_EmissionColor", glowColor * glowIntensity);
            }
        }

        private void StartIdleAnimation()
        {
            // Gentle floating animation
            animationTime = Random.Range(0f, Mathf.PI * 2f); // Random start phase
        }

        private void UpdateIdleAnimation()
        {
            animationTime += Time.deltaTime * 2f; // Animation speed
            
            // Floating motion
            float yOffset = Mathf.Sin(animationTime) * 0.2f;
            transform.position = originalPosition + Vector3.up * yOffset;
            
            // Gentle rotation
            transform.Rotate(Vector3.up, 30f * Time.deltaTime);
            
            // Breathing scale effect
            float scaleMultiplier = 1f + Mathf.Sin(animationTime * 1.5f) * 0.05f;
            transform.localScale = startScale * scaleMultiplier;
        }

        private void CheckForMagnetEffect()
        {
            if (NetworkGameManager.Instance == null) return;
            
            // Find closest player within magnet range
            var allPlayers = NetworkGameManager.Instance.GameState.GetAllPlayers();
            float closestDistance = magnetRange;
            Transform closestPlayer = null;
            
            foreach (var playerState in allPlayers.Values)
            {
                if (playerState.Character != null && !playerState.IsDead)
                {
                    float distance = Vector3.Distance(transform.position, playerState.Character.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestPlayer = playerState.Character.transform;
                    }
                }
            }
            
            if (closestPlayer != null)
            {
                StartMagnetEffect(closestPlayer);
            }
        }

        private void StartMagnetEffect(Transform player)
        {
            targetPlayer = player;
            isBeingMagneted = true;
            
            // Visual feedback for magnet effect
            if (glowMaterial != null)
            {
                foreach (var renderer in renderers)
                {
                    renderer.material = glowMaterial;
                }
            }
            
            // Audio feedback
            if (collectSound != null)
            {
                AudioSource.PlayClipAtPoint(collectSound, transform.position, 0.3f);
            }
        }

        private void UpdateMagnetMovement()
        {
            if (targetPlayer == null)
            {
                isBeingMagneted = false;
                return;
            }
            
            // Move towards player
            Vector3 direction = (targetPlayer.position - transform.position).normalized;
            transform.position += direction * magnetSpeed * Time.deltaTime;
            
            // Check if close enough to collect
            if (Vector3.Distance(transform.position, targetPlayer.position) < 0.5f)
            {
                var character = targetPlayer.GetComponent<Characters.Character>();
                if (character != null)
                {
                    TryCollect(character);
                }
            }
        }

        private void RegisterWithNetworkState()
        {
            var collectibleState = new CollectibleState
            {
                CollectibleId = collectibleId,
                Position = transform.position,
                Type = collectibleType.ToString(),
                Value = Value,
                IsCollected = false,
                CollectedByPlayer = 0,
                CollectedAtTick = 0
            };
            
            NetworkGameManager.Instance.GameState.AddCollectible(collectibleState);
        }

        private void SynchronizeNetworkState()
        {
            var gameState = NetworkGameManager.Instance.GameState;
            var collectibles = gameState.GetCollectibles();
            
            foreach (var collectible in collectibles)
            {
                if (collectible.CollectibleId == collectibleId)
                {
                    if (collectible.IsCollected && !isCollected)
                    {
                        // This collectible was collected by another player
                        HandleNetworkCollection(collectible.CollectedByPlayer);
                    }
                    break;
                }
            }
        }

        private void HandleNetworkCollection(uint playerId)
        {
            isCollected = true;
            collectedByPlayer = playerId;
            
            // Play effects without giving value to local player
            PlayCollectionEffects(false);
            
            // Remove from scene
            ReturnToPool();
        }

        public void TryCollect(Characters.Character character)
        {
            if (isCollected) return;
            
            uint playerId = 0;
            
            // Get player ID for multiplayer
            if (NetworkGameManager.Instance?.IsMultiplayer == true)
            {
                var networkCharacter = character.GetComponent<Characters.NetworkRedCharacter>();
                if (networkCharacter != null)
                {
                    playerId = networkCharacter.PlayerId;
                }
                
                // Validate collection on server if required
                if (requireServerValidation && !ValidateCollection(playerId))
                {
                    return;
                }
            }
            
            // Mark as collected
            isCollected = true;
            collectedByPlayer = playerId;
            
            // Apply collection effects and rewards
            ApplyCollectionReward(character, playerId);
            
            // Update network state
            if (NetworkGameManager.Instance?.IsMultiplayer == true)
            {
                UpdateNetworkCollectionState(playerId);
            }
            
            // Play effects
            PlayCollectionEffects(true);
            
            // Analytics
            TrackCollectionAnalytics(playerId);
            
            // Remove from scene
            ReturnToPool();
        }

        private bool ValidateCollection(uint playerId)
        {
            // Server-side validation logic
            // Check if player is in valid state, not dead, etc.
            var playerState = NetworkGameManager.Instance.GameState.GetPlayerState(playerId);
            
            if (playerState == null || playerState.IsDead)
                return false;
            
            // Check distance to prevent cheating
            if (playerState.Character != null)
            {
                float distance = Vector3.Distance(transform.position, playerState.Character.transform.position);
                if (distance > 3f) // Max collection distance
                    return false;
            }
            
            return true;
        }

        private void ApplyCollectionReward(Characters.Character character, uint playerId)
        {
            switch (collectibleType)
            {
                case CollectibleType.Coin:
                    ApplyCoinReward(playerId);
                    break;
                case CollectibleType.Gem:
                    ApplyGemReward(playerId);
                    break;
                case CollectibleType.PowerUp:
                    ApplyPowerUpReward(character, playerId);
                    break;
                case CollectibleType.SpecialItem:
                    ApplySpecialItemReward(character, playerId);
                    break;
            }
        }

        private void ApplyCoinReward(uint playerId)
        {
            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.Instance.Coins.Value += Value;
            }
            
            // Track resource gain
            AnalyticsManager.Instance?.TrackResourceEvent("source", "coins", Value, "pickup", $"coin_{collectibleId}");
        }

        private void ApplyGemReward(uint playerId)
        {
            // Gems are premium currency
            // This would integrate with your premium currency system
            Debug.Log($"Player {playerId} collected gem worth {Value}");
            
            AnalyticsManager.Instance?.TrackResourceEvent("source", "gems", Value, "pickup", $"gem_{collectibleId}");
        }

        private void ApplyPowerUpReward(Characters.Character character, uint playerId)
        {
            // Apply temporary power-up effects
            var powerUpComponent = character.GetComponent<PowerUpController>();
            if (powerUpComponent != null)
            {
                powerUpComponent.ApplyPowerUp(GetPowerUpType(), 10f); // 10 second duration
            }
            
            AnalyticsManager.Instance?.TrackDesignEvent($"powerup_collected_{GetPowerUpType()}", 1f);
        }

        private void ApplySpecialItemReward(Characters.Character character, uint playerId)
        {
            // Handle special quest items, keys, etc.
            Debug.Log($"Player {playerId} collected special item: {name}");
            
            AnalyticsManager.Instance?.TrackDesignEvent($"special_item_collected_{name}", 1f);
        }

        private PowerUpType GetPowerUpType()
        {
            // Determine power-up type based on collectible properties
            return PowerUpType.SpeedBoost; // Default, could be configured per collectible
        }

        private void UpdateNetworkCollectionState(uint playerId)
        {
            var gameState = NetworkGameManager.Instance.GameState;
            var collectibles = gameState.GetCollectibles();
            
            foreach (var collectible in collectibles)
            {
                if (collectible.CollectibleId == collectibleId)
                {
                    collectible.IsCollected = true;
                    collectible.CollectedByPlayer = playerId;
                    collectible.CollectedAtTick = NetworkGameManager.Instance.CurrentTick;
                    break;
                }
            }
        }

        private void PlayCollectionEffects(bool isLocalCollection)
        {
            // Particle effects
            if (collectEffect != null)
            {
                var effect = Instantiate(collectEffect, transform.position, Quaternion.identity);
                effect.Play();
                Destroy(effect.gameObject, effect.main.duration);
            }
            
            // Audio effects
            if (collectSound != null && isLocalCollection)
            {
                AudioManager.Singleton?.PlayCoinSound(transform.position);
            }
            
            // Floating text
            if (floatingTextPrefab != null && isLocalCollection)
            {
                ShowFloatingText($"+{Value}");
            }
            
            // Screen effects for valuable items
            if (collectibleType == CollectibleType.Gem || Value > 10)
            {
                TriggerScreenEffect();
            }
        }

        private void ShowFloatingText(string text)
        {
            if (floatingTextPrefab != null)
            {
                var floatingText = Instantiate(floatingTextPrefab, transform.position + Vector3.up, Quaternion.identity);
                var textComponent = floatingText.GetComponent<UnityEngine.UI.Text>();
                
                if (textComponent != null)
                {
                    textComponent.text = text;
                    textComponent.color = GetValueColor();
                }
                
                // Animate floating text
                StartCoroutine(AnimateFloatingText(floatingText));
            }
        }

        private System.Collections.IEnumerator AnimateFloatingText(GameObject floatingText)
        {
            Vector3 startPos = floatingText.transform.position;
            Vector3 endPos = startPos + Vector3.up * 2f;
            float duration = 1f;
            float elapsed = 0f;
            
            var canvasGroup = floatingText.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = floatingText.AddComponent<CanvasGroup>();
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                
                floatingText.transform.position = Vector3.Lerp(startPos, endPos, collectAnimation.Evaluate(progress));
                canvasGroup.alpha = 1f - progress;
                
                yield return null;
            }
            
            Destroy(floatingText);
        }

        private Color GetValueColor()
        {
            switch (collectibleType)
            {
                case CollectibleType.Coin: return Color.yellow;
                case CollectibleType.Gem: return Color.cyan;
                case CollectibleType.PowerUp: return Color.green;
                case CollectibleType.SpecialItem: return Color.magenta;
                default: return Color.white;
            }
        }

        private void TriggerScreenEffect()
        {
            // Screen flash or other dramatic effect for valuable collectibles
            // This would integrate with your screen effects system
            Debug.Log("Valuable item collected - trigger screen effect!");
        }

        private void TrackCollectionAnalytics(uint playerId)
        {
            var customParams = new Dictionary<string, object>
            {
                { "collectible_type", collectibleType.ToString() },
                { "value", Value },
                { "position_x", transform.position.x },
                { "position_y", transform.position.y },
                { "player_id", playerId },
                { "was_magneted", isBeingMagneted }
            };
            
            AnalyticsManager.Instance?.TrackEvent("collectible_collected", customParams);
        }

        // IPoolable implementation
        public void OnSpawned()
        {
            // Reset state when spawned from pool
            isCollected = false;
            collectedByPlayer = 0;
            isBeingMagneted = false;
            targetPlayer = null;
            lastNetworkSync = 0f;
            
            // Reset visual state
            transform.localScale = startScale;
            
            if (originalMaterials != null)
            {
                for (int i = 0; i < renderers.Length && i < originalMaterials.Length; i++)
                {
                    if (renderers[i] != null && originalMaterials[i] != null)
                        renderers[i].material = originalMaterials[i];
                }
            }
            
            // Register with network state if multiplayer
            if (NetworkGameManager.Instance?.IsMultiplayer == true)
            {
                RegisterWithNetworkState();
            }
            
            // Restart animations
            StartIdleAnimation();
        }

        public void OnDespawned()
        {
            // Cleanup when returned to pool
            if (NetworkGameManager.Instance?.IsMultiplayer == true)
            {
                NetworkGameManager.Instance.GameState.RemoveCollectible(collectibleId);
            }
            
            // Stop any ongoing coroutines
            StopAllCoroutines();
        }

        private void ReturnToPool()
        {
            var pooledObject = GetComponent<PooledObject>();
            if (pooledObject != null)
            {
                pooledObject.ReturnToPool();
            }
            else
            {
                // Fallback to destruction if not pooled
                Destroy(gameObject);
            }
        }

        public void SetMultiplierValue(int multiplier)
        {
            multiplierValue = Mathf.Max(1, multiplier);
        }

        public void SetCollectibleType(CollectibleType type)
        {
            collectibleType = type;
        }
    }

    public enum CollectibleType
    {
        Coin,
        Gem,
        PowerUp,
        SpecialItem
    }

    public enum PowerUpType
    {
        SpeedBoost,
        JumpBoost,
        Shield,
        Magnet,
        DoubleCoins,
        SlowMotion
    }

    /// <summary>
    /// Power-up controller for handling temporary character enhancements
    /// </summary>
    public class PowerUpController : MonoBehaviour
    {
        private Dictionary<PowerUpType, float> activePowerUps = new Dictionary<PowerUpType, float>();
        private Characters.Character character;

        void Awake()
        {
            character = GetComponent<Characters.Character>();
        }

        void Update()
        {
            // Update power-up timers
            var keysToRemove = new List<PowerUpType>();
            
            foreach (var kvp in activePowerUps)
            {
                activePowerUps[kvp.Key] -= Time.deltaTime;
                
                if (activePowerUps[kvp.Key] <= 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            // Remove expired power-ups
            foreach (var key in keysToRemove)
            {
                RemovePowerUp(key);
            }
        }

        public void ApplyPowerUp(PowerUpType type, float duration)
        {
            if (activePowerUps.ContainsKey(type))
            {
                // Extend duration if already active
                activePowerUps[type] = Mathf.Max(activePowerUps[type], duration);
            }
            else
            {
                activePowerUps[type] = duration;
                ActivatePowerUp(type);
            }
        }

        private void ActivatePowerUp(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.SpeedBoost:
                    // Increase character speed
                    break;
                case PowerUpType.JumpBoost:
                    // Increase jump strength
                    break;
                case PowerUpType.Shield:
                    // Make character invulnerable
                    break;
                case PowerUpType.Magnet:
                    // Increase coin magnet range
                    break;
                case PowerUpType.DoubleCoins:
                    // Double coin values
                    break;
                case PowerUpType.SlowMotion:
                    // Slow down time
                    break;
            }
            
            Debug.Log($"Power-up activated: {type}");
        }

        private void RemovePowerUp(PowerUpType type)
        {
            activePowerUps.Remove(type);
            DeactivatePowerUp(type);
        }

        private void DeactivatePowerUp(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.SpeedBoost:
                    // Reset character speed
                    break;
                case PowerUpType.JumpBoost:
                    // Reset jump strength
                    break;
                case PowerUpType.Shield:
                    // Remove invulnerability
                    break;
                case PowerUpType.Magnet:
                    // Reset coin magnet range
                    break;
                case PowerUpType.DoubleCoins:
                    // Reset coin values
                    break;
                case PowerUpType.SlowMotion:
                    // Reset time scale
                    break;
            }
            
            Debug.Log($"Power-up deactivated: {type}");
        }

        public bool HasPowerUp(PowerUpType type)
        {
            return activePowerUps.ContainsKey(type);
        }

        public float GetPowerUpTimeRemaining(PowerUpType type)
        {
            return activePowerUps.TryGetValue(type, out float time) ? time : 0f;
        }
    }
}