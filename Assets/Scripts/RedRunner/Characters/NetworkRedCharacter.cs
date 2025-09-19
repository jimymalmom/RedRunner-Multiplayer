using System.Collections;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
using RedRunner.Utilities;
using RedRunner.Networking;
using RedRunner.Networking.Commands;

namespace RedRunner.Characters
{
    /// <summary>
    /// Enhanced character controller with network support and multiplayer features
    /// Supports both local and remote player handling with client-side prediction
    /// </summary>
    public class NetworkRedCharacter : RedCharacter
    {
        [Header("Network Settings")]
        [SerializeField] private bool isLocalPlayer = true;
        [SerializeField] private uint playerId = 0;
        [SerializeField] private float interpolationSpeed = 15f;
        [SerializeField] private float extrapolationLimit = 0.5f;
        
        [Header("Multiplayer Features")]
        [SerializeField] private Color playerColor = Color.red;
        [SerializeField] private string playerName = "Player";
        [SerializeField] private GameObject nameTagPrefab;
        [SerializeField] private Vector3 nameTagOffset = new Vector3(0, 2f, 0);
        
        // Network state
        private Vector3 networkPosition;
        private Vector3 networkVelocity;
        private bool networkIsGrounded;
        private bool networkIsDead;
        private float lastNetworkUpdate;
        
        // Prediction
        private Vector3 predictedPosition;
        private Vector3 predictedVelocity;
        private float predictionTime;
        
        // Visual elements
        private GameObject nameTag;
        private Renderer[] renderers;
        private Color originalColor;
        
        // Input buffering for local player
        private float horizontalInputBuffer;
        private bool jumpInputBuffer;
        private float inputBufferTime;
        
        public bool IsLocalPlayer => isLocalPlayer;
        public uint PlayerId => playerId;
        public string PlayerName => playerName;
        public Color PlayerColor => playerColor;
        
        void Awake()
        {
            // Initialize base character components (Awake is protected)
            
            // Initialize network state
            networkPosition = transform.position;
            networkVelocity = Vector3.zero;
            lastNetworkUpdate = Time.time;
            
            // Cache renderers for color changes
            renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                originalColor = renderers[0].material.color;
            }
            
            // Create name tag for multiplayer
            if (NetworkGameManager.Instance?.IsMultiplayer == true)
            {
                CreateNameTag();
            }
        }
        
        public void Initialize(uint id, string name, Color color, bool isLocal)
        {
            playerId = id;
            playerName = name;
            playerColor = color;
            isLocalPlayer = isLocal;
            
            ApplyPlayerColor();
            UpdateNameTag();
            
            // Disable input for remote players
            if (!isLocalPlayer)
            {
                enabled = false; // Disable Update loop for remote players
            }
        }
        
        private void CreateNameTag()
        {
            if (nameTagPrefab != null)
            {
                nameTag = Instantiate(nameTagPrefab, transform);
                nameTag.transform.localPosition = nameTagOffset;
                
                var nameText = nameTag.GetComponentInChildren<UnityEngine.UI.Text>();
                if (nameText != null)
                {
                    nameText.text = playerName;
                    nameText.color = playerColor;
                }
            }
        }
        
        private void UpdateNameTag()
        {
            if (nameTag != null)
            {
                var nameText = nameTag.GetComponentInChildren<UnityEngine.UI.Text>();
                if (nameText != null)
                {
                    nameText.text = playerName;
                    nameText.color = playerColor;
                }
                
                // Make name tag face camera
                if (Camera.main != null)
                {
                    nameTag.transform.LookAt(Camera.main.transform);
                    nameTag.transform.Rotate(0, 180, 0);
                }
            }
        }
        
        private void ApplyPlayerColor()
        {
            foreach (var renderer in renderers)
            {
                if (renderer.material != null)
                {
                    renderer.material.color = Color.Lerp(originalColor, playerColor, 0.7f);
                }
            }
        }
        
        void Update()
        {
            if (!NetworkGameManager.Instance.GameStarted || !NetworkGameManager.Instance.GameRunning)
                return;
            
            if (isLocalPlayer)
            {
                HandleLocalPlayerUpdate();
            }
            else
            {
                HandleRemotePlayerUpdate();
            }
            
            // Common updates
            UpdateAnimationParameters();
            UpdateNameTag();
            
            // Check fall death
            if (transform.position.y < -10f)
            {
                Die();
            }
        }
        
        private void HandleLocalPlayerUpdate()
        {
            // Buffer inputs for smooth command sending
            horizontalInputBuffer = CrossPlatformInputManager.GetAxis("Horizontal");
            
            if (CrossPlatformInputManager.GetButtonDown("Jump"))
            {
                jumpInputBuffer = true;
                inputBufferTime = Time.time;
            }
            
            // Clear old jump input
            if (jumpInputBuffer && Time.time - inputBufferTime > 0.1f)
            {
                jumpInputBuffer = false;
            }
            
            // Send commands to network manager
            SendNetworkCommands();
            
            // Apply local movement for immediate response (client-side prediction)
            if (NetworkGameManager.Instance.IsMultiplayer)
            {
                ApplyPredictiveMovement();
            }
            else
            {
                // Single player - use original movement
                // Base Update is protected - handle character updates manually
            }
        }
        
        private void HandleRemotePlayerUpdate()
        {
            // Interpolate/extrapolate position for smooth remote player movement
            float timeSinceUpdate = Time.time - lastNetworkUpdate;
            
            if (timeSinceUpdate < extrapolationLimit)
            {
                // Extrapolate position based on last known velocity
                Vector3 extrapolatedPosition = networkPosition + networkVelocity * timeSinceUpdate;
                transform.position = Vector3.Lerp(transform.position, extrapolatedPosition, Time.deltaTime * interpolationSpeed);
            }
            else
            {
                // Too much time passed, just interpolate to last known position
                transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * interpolationSpeed);
            }
            
            // Update grounded state
            m_GroundCheck.ForceGroundedState(networkIsGrounded);
            
            // Update death state
            if (networkIsDead && !IsDead.Value)
            {
                Die();
            }
        }
        
        private void SendNetworkCommands()
        {
            var gameManager = NetworkGameManager.Instance;
            
            // Send movement command
            if (Mathf.Abs(horizontalInputBuffer) > 0.01f)
            {
                var moveCommand = new MoveCommand(
                    playerId,
                    horizontalInputBuffer,
                    UnityEngine.Input.mousePosition,
                    Time.deltaTime
                );
                gameManager.QueueCommand(moveCommand);
            }
            
            // Send jump command
            if (jumpInputBuffer)
            {
                var jumpCommand = new JumpCommand(
                    playerId,                    UnityEngine.Input.mousePosition,
                    m_JumpStrength,
                    m_GroundCheck.IsGrounded
                );
                gameManager.QueueCommand(jumpCommand);
                jumpInputBuffer = false;
            }
        }
        
        private void ApplyPredictiveMovement()
        {
            // Apply movement immediately for responsive feel
            if (!IsDead.Value)
            {
                // Horizontal movement
                if (Mathf.Abs(horizontalInputBuffer) > 0.01f)
                {
                    Vector2 velocity = m_Rigidbody2D.linearVelocity;
                    velocity.x = GetCurrentRunSpeed() * horizontalInputBuffer;
                    m_Rigidbody2D.linearVelocity = velocity;
                    
                    // Update facing direction
                    Vector3 scale = transform.localScale;
                    scale.x = Mathf.Sign(horizontalInputBuffer);
                    transform.localScale = scale;
                }
                
                // Jump prediction
                if (jumpInputBuffer && m_GroundCheck.IsGrounded)
                {
                    Vector2 velocity = m_Rigidbody2D.linearVelocity;
                    velocity.y = m_JumpStrength;
                    m_Rigidbody2D.linearVelocity = velocity;
                    
                    m_Animator.ResetTrigger("Jump");
                    m_Animator.SetTrigger("Jump");
                    
                    if (m_JumpParticleSystem != null)
                        m_JumpParticleSystem.Play();
                }
            }
        }
        
        public float GetCurrentRunSpeed()
        {
            return m_CurrentRunSpeed;
        }
        
        public void UpdateNetworkState(Vector3 position, Vector3 velocity, bool isGrounded, bool isDead)
        {
            networkPosition = position;
            networkVelocity = velocity;
            networkIsGrounded = isGrounded;
            networkIsDead = isDead;
            lastNetworkUpdate = Time.time;
            
            // For remote players, apply the state immediately if the difference is significant
            if (!isLocalPlayer)
            {
                float distance = Vector3.Distance(transform.position, networkPosition);
                if (distance > 5f) // Teleport if too far off
                {
                    transform.position = networkPosition;
                    m_Rigidbody2D.linearVelocity = networkVelocity;
                }
            }
        }
        
        public void CorrectPrediction(Vector3 serverPosition, Vector3 serverVelocity)
        {
            if (isLocalPlayer)
            {
                float distance = Vector3.Distance(transform.position, serverPosition);
                
                // If prediction is significantly off, correct it
                if (distance > 0.5f)
                {
                    transform.position = Vector3.Lerp(transform.position, serverPosition, 0.5f);
                    m_Rigidbody2D.linearVelocity = Vector3.Lerp(m_Rigidbody2D.linearVelocity, serverVelocity, 0.5f);
                }
            }
        }
        
        private void UpdateAnimationParameters()
        {
            if (m_Animator != null)
            {
                Vector2 velocity = m_Rigidbody2D.linearVelocity;
                m_Animator.SetFloat("Speed", Mathf.Abs(velocity.x));
                m_Animator.SetFloat("VelocityX", Mathf.Abs(velocity.x));
                m_Animator.SetFloat("VelocityY", velocity.y);
                m_Animator.SetBool("IsGrounded", m_GroundCheck.IsGrounded);
                m_Animator.SetBool("IsDead", IsDead.Value);
            }
        }
        
        public override void Die(bool blood)
        {
            if (!IsDead.Value)
            {
                base.Die(blood);
                
                // Notify network if local player
                if (isLocalPlayer && NetworkGameManager.Instance.IsMultiplayer)
                {
                    // Send death notification to server
                    // This would be handled by a DeathCommand in a full implementation
                }
            }
        }
        
        public override void Reset()
        {
            base.Reset();
            
            // Reset network state
            networkPosition = transform.position;
            networkVelocity = Vector3.zero;
            networkIsGrounded = true;
            networkIsDead = false;
            lastNetworkUpdate = Time.time;
        }
        
        // Multiplayer specific methods
        public void SetPlayerInfo(string name, Color color)
        {
            playerName = name;
            playerColor = color;
            ApplyPlayerColor();
            UpdateNameTag();
        }
        
        public void ShowEmote(string emoteName)
        {
            // TODO: Implement emote system
            Debug.Log($"{playerName} shows emote: {emoteName}");
        }
        
        public void PlayNetworkSound(string soundName)
        {
            // Play sounds for network events (for all players to hear)
            switch (soundName)
            {
                case "jump":
                    AudioManager.Singleton?.PlayJumpSound(m_JumpAndGroundedAudioSource);
                    break;
                case "land":
                    AudioManager.Singleton?.PlayGroundedSound(m_JumpAndGroundedAudioSource);
                    break;
                // Add more networked sounds as needed
            }
        }
        
        void OnDestroy()
        {
            if (nameTag != null)
            {
                Destroy(nameTag);
            }
        }
    }
}