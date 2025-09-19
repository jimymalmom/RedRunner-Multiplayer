using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

using BayatGames.SaveGameFree;
using BayatGames.SaveGameFree.Serializers;

using RedRunner.Characters;
using RedRunner.Collectables;
using RedRunner.TerrainGeneration;
using RedRunner.Networking;
using RedRunner.Networking.Commands;
using RedRunner.Core;
using RedRunner.Competition;

namespace RedRunner
{
    /// <summary>
    /// Network-ready Game Manager with tick-based updates and multiplayer support
    /// Handles both single-player and multiplayer game states
    /// </summary>
    public sealed class NetworkGameManager : MonoBehaviour
    {
        public delegate void AudioEnabledHandler(bool active);
        public delegate void ScoreHandler(float newScore, float highScore, float lastScore);
        public delegate void ResetHandler();
        public delegate void PlayerJoinedHandler(uint playerId, string playerName);
        public delegate void PlayerLeftHandler(uint playerId);
        public delegate void GameStateUpdatedHandler(IGameState gameState);

        public static event ResetHandler OnReset;
        public static event ScoreHandler OnScoreChanged;
        public static event AudioEnabledHandler OnAudioEnabled;
        public static event PlayerJoinedHandler OnPlayerJoined;
        public static event PlayerLeftHandler OnPlayerLeft;
        public static event GameStateUpdatedHandler OnGameStateUpdated;

        private static NetworkGameManager instance;
        public static NetworkGameManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<NetworkGameManager>();
                }
                return instance;
            }
        }

        [Header("Game Configuration")]
        [SerializeField] private Character mainCharacterPrefab;
        [SerializeField] [TextArea(3, 30)] private string shareText;
        [SerializeField] private string shareUrl;
        
        [Header("Network Settings")]
        [SerializeField] private bool enableNetworking = false;
        [SerializeField] private int tickRate = 60;
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private float networkSendRate = 20f;
        
        [Header("Game State")]
        [SerializeField] private bool enableGameStateLogging = false;
        
        // Network state
        private bool isMultiplayerEnabled = false;
        private bool isServer = false;
        private bool isClient = false;
        private uint localPlayerId = 0;
        private uint currentTick = 0;
        private float tickTimer = 0f;
        private Queue<IGameCommand> commandQueue = new Queue<IGameCommand>();
        
        // Game state management
        private IGameState gameState;
        private Dictionary<uint, Character> playerCharacters = new Dictionary<uint, Character>();
        
        // Game Variables
        private float startScoreX = 0f;
        private float highScore = 0f;
        private float lastScore = 0f;
        private bool gameStarted = false;
        private bool gameRunning = false;
        private bool audioEnabled = true;
        
        // Callbacks
        public Property<int> Coins { get; private set; }

        #region Properties
        public bool GameStarted => gameStarted;
        public bool GameRunning => gameRunning;
        public bool AudioEnabled => audioEnabled;
        public bool IsMultiplayer => isMultiplayerEnabled;
        public bool IsServer => isServer;
        public bool IsClient => isClient;
        public uint LocalPlayerId => localPlayerId;
        public IGameState GameState => gameState;
        public int TickRate => tickRate;
        public uint CurrentTick => currentTick;
        #endregion

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            InitializeGameState();
        }

        void Start()
        {
            Initialize();
        }

        void Update()
        {
            if (gameRunning)
            {
                UpdateGameTick();
                UpdateScores();
            }
        }

        #region Initialization
        
        private void InitializeGameState()
        {
            gameState = new NetworkGameState();
            Coins = new Property<int>(0);
            
            // Load saved data
            LoadGameData();
        }

        private void Initialize()
        {
            AudioManager.Singleton.PlayMusic();
            
            if (enableNetworking && isMultiplayerEnabled)
            {
                InitializeNetworking();
            }
            else
            {
                InitializeSinglePlayer();
            }
        }

        private void InitializeNetworking()
        {
            Debug.Log("Initializing networking systems...");
            // Network initialization would go here
        }

        private void InitializeSinglePlayer()
        {
            // Create main player
            var mainPlayer = SpawnCharacter(0, "Player");
            if (mainPlayer != null)
            {
                mainPlayer.IsDead.AddEventAndFire(UpdateDeathEvent, this);
                startScoreX = mainPlayer.transform.position.x;
            }
        }

        #endregion

        #region Character Management

        public Character SpawnCharacter(uint playerId, string playerName)
        {
            if (playerCharacters.ContainsKey(playerId))
            {
                Debug.LogWarning($"Player {playerId} already has a character!");
                return playerCharacters[playerId];
            }

            var character = Instantiate(mainCharacterPrefab);
            playerCharacters[playerId] = character;
            
            // Create player state
            var playerState = new PlayerState
            {
                PlayerId = playerId,
                PlayerName = playerName,
                Character = character,
                Score = 0f,
                IsDead = false
            };
            
            gameState.SetPlayerState(playerId, playerState);
            
            OnPlayerJoined?.Invoke(playerId, playerName);
            
            return character;
        }

        public void RemoveCharacter(uint playerId)
        {
            if (playerCharacters.TryGetValue(playerId, out var character))
            {
                Destroy(character.gameObject);
                playerCharacters.Remove(playerId);
                gameState.RemovePlayer(playerId);
                
                OnPlayerLeft?.Invoke(playerId);
            }
        }

        #endregion

        #region Game Tick System

        private void UpdateGameTick()
        {
            tickTimer += Time.deltaTime;
            float tickInterval = 1f / tickRate;
            
            while (tickTimer >= tickInterval)
            {
                ProcessGameTick();
                tickTimer -= tickInterval;
                currentTick++;
            }
        }

        private void ProcessGameTick()
        {
            // Process queued commands
            while (commandQueue.Count > 0)
            {
                var command = commandQueue.Dequeue();
                if (command.Validate(gameState))
                {
                    command.Execute(gameState);
                }
            }
            
            // Update game state
            // Game state is updated through individual method callsick);
            
            // Broadcast state updates if needed
            OnGameStateUpdated?.Invoke(gameState);
        }

        #endregion

        #region Score Management

        private void UpdateScores()
        {
            if (isMultiplayerEnabled)
            {
                UpdateAllPlayerScores();
            }
            else
            {
                UpdateSinglePlayerScore();
            }
        }

        private void UpdateAllPlayerScores()
        {
            foreach (var kvp in gameState.GetAllPlayers())
            {
                var playerState = kvp.Value;
                if (playerState.Character != null && !playerState.IsDead)
                {
                    float newScore = playerState.Character.transform.position.x;
                    if (newScore > playerState.Score)
                    {
                        playerState.Score = newScore;
                    }
                }
            }
        }

        private void UpdateSinglePlayerScore()
        {
            var mainPlayer = gameState.GetPlayerState(0);
            if (mainPlayer?.Character != null)
            {
                var character = mainPlayer.Character;
                if (character.transform.position.x > startScoreX && character.transform.position.x > lastScore)
                {
                    lastScore = character.transform.position.x;
                    OnScoreChanged?.Invoke(lastScore, highScore, lastScore);
                }
            }
        }

        #endregion

        #region Game Flow

        void UpdateDeathEvent(bool isDead)
        {
            if (isDead)
            {
                StartCoroutine(DeathCoroutine());
            }
        }

        IEnumerator DeathCoroutine()
        {
            if (!isMultiplayerEnabled)
            {
                // Calculate final score based on character position
                var mainPlayer = gameState.GetPlayerState(0);
                if (mainPlayer?.Character != null)
                {
                    lastScore = mainPlayer.Character.transform.position.x;
                }
                
                if (lastScore > highScore)
                    highScore = lastScore;
                    
                OnScoreChanged?.Invoke(lastScore, highScore, lastScore);
            }
            else
            {
                // For multiplayer, update all player scores
                UpdateAllPlayerScores();
            }

            yield return new WaitForSecondsRealtime(1.5f);

            EndGame();
            var endScreen = UIManager.Singleton.UISCREENS.Find(el => el.ScreenInfo == UIScreenInfo.END_SCREEN);
            UIManager.Singleton.OpenScreen(endScreen);
        }

        public void Init()
        {
            EndGame();
            UIManager.Singleton.Init();
            StartCoroutine(LoadCoroutine());
        }

        IEnumerator LoadCoroutine()
        {
            var startScreen = UIManager.Singleton.UISCREENS.Find(el => el.ScreenInfo == UIScreenInfo.START_SCREEN);
            yield return new WaitForSecondsRealtime(3f);
            UIManager.Singleton.OpenScreen(startScreen);
        }

        #endregion

        #region Command System

        public void QueueCommand(IGameCommand command)
        {
            command.CommandId = currentTick + (uint)commandQueue.Count;
            command.Tick = currentTick;
            commandQueue.Enqueue(command);
        }

        #endregion

        #region Multiplayer

        public void EnableMultiplayer(bool asServer, uint playerId = 0)
        {
            isMultiplayerEnabled = true;
            isServer = asServer;
            isClient = !asServer || playerId > 0; // Server can also be a client
            localPlayerId = playerId;
            
            Debug.Log($"Multiplayer enabled: Server={isServer}, Client={isClient}, PlayerId={localPlayerId}");
        }

        public void DisableMultiplayer()
        {
            isMultiplayerEnabled = false;
            isServer = false;
            isClient = false;
            localPlayerId = 0;
            
            // Clear multiplayer state
            foreach (var kvp in playerCharacters)
            {
                if (kvp.Key != 0) // Keep main player
                {
                    Destroy(kvp.Value.gameObject);
                }
            }
            
            var playersToRemove = new List<uint>();
            foreach (var kvp in gameState.GetAllPlayers())
            {
                if (kvp.Key != 0)
                {
                    playersToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var playerId in playersToRemove)
            {
                gameState.RemovePlayer(playerId);
            }
        }

        #endregion

        #region Game Control

        public void StartGame()
        {
            gameStarted = true;
            gameRunning = true;
            Time.timeScale = 1f;
            
            // Reset scores for new game
            lastScore = 0f;
            currentTick = 0;
            tickTimer = 0f;
            
            // Reset character positions
            if (!isMultiplayerEnabled)
            {
                var mainPlayer = gameState.GetPlayerState(0);
                if (mainPlayer?.Character != null)
                {
                    startScoreX = mainPlayer.Character.transform.position.x;
                }
            }
        }

        public void PauseGame()
        {
            gameRunning = false;
            Time.timeScale = 0f;
        }

        public void ResumeGame()
        {
            gameRunning = true;
            Time.timeScale = 1f;
        }

        public void StopGame()
        {
            gameRunning = false;
            Time.timeScale = 1f;
        }

        public void EndGame()
        {
            gameStarted = false;
            
            // Submit score to leaderboard before stopping the game
            SubmitScoreToLeaderboard();
            
            StopGame();
        }

        #endregion

        #region Leaderboard Integration
        
        private void SubmitScoreToLeaderboard()
        {
            if (LeaderboardManager.Instance == null)
            {
                Debug.LogWarning("LeaderboardManager not available for score submission");
                return;
            }
            
            float finalScore = 0f;
            
            if (!isMultiplayerEnabled)
            {
                // Single player - use lastScore
                finalScore = lastScore;
            }
            else
            {
                // Multiplayer - get local player's score
                var localPlayer = gameState.GetPlayerState(localPlayerId);
                if (localPlayer != null)
                {
                    finalScore = localPlayer.Score;
                }
            }
            
            // Only submit if we have a valid score
            if (finalScore > 0f)
            {
                LeaderboardManager.Instance.SubmitScore("global_highscore", finalScore);
                Debug.Log($"Submitted score to leaderboard: {finalScore}");
            }
            else
            {
                Debug.LogWarning("No valid score to submit to leaderboard");
            }
        }

        #endregion

        #region Character Respawn

        public void RespawnMainCharacter()
        {
            if (isMultiplayerEnabled)
            {
                RespawnCharacter(localPlayerId);
            }
            else
            {
                RespawnCharacter(0);
            }
        }

        public void RespawnCharacter(uint playerId)
        {
            if (playerCharacters.TryGetValue(playerId, out var character))
            {
                var block = TerrainGenerator.Singleton?.GetCharacterBlock();
                if (block != null)
                {
                    Vector3 position = block.transform.position;
                    position.y += 2.56f;
                    position.x += 1.28f;
                    character.transform.position = position;
                    character.IsDead.Value = false;
                    
                    // Update game state
                    var playerState = gameState.GetPlayerState(playerId);
                    if (playerState != null)
                    {
                        playerState.IsDead = false;
                    }
                }
            }
        }

        #endregion

        #region Audio Control

        public void ToggleAudio()
        {
            audioEnabled = !audioEnabled;
            OnAudioEnabled?.Invoke(audioEnabled);
            
            if (audioEnabled)
            {
                AudioManager.Singleton.PlayMusic();
            }
            else
            {
                // Note: AudioManager doesn't have StopMusic - could add volume control instead();
            }
        }

        #endregion

        #region Data Persistence

        private void LoadGameData()
        {
            if (SaveGame.Exists("GameData"))
            {
                try
                {
                    var gameData = SaveGame.Load<GameData>("GameData");
                    highScore = gameData.highScore;
                    audioEnabled = gameData.audioEnabled;
                    Coins.Value = gameData.coins;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load game data: {e.Message}");
                    CreateDefaultGameData();
                }
            }
            else
            {
                CreateDefaultGameData();
            }
        }

        private void SaveGameData()
        {
            try
            {
                var gameData = new GameData
                {
                    highScore = highScore,
                    lastScore = lastScore,
                    audioEnabled = audioEnabled,
                    coins = Coins.Value
                };
                
                SaveGame.Save("GameData", gameData);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save game data: {e.Message}");
            }
        }

        private void CreateDefaultGameData()
        {
            highScore = 0f;
            lastScore = 0f;
            audioEnabled = true;
            Coins.Value = 0;
            
            SaveGameData();
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && gameStarted)
            {
                SaveGameData();
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && gameStarted)
            {
                SaveGameData();
            }
        }

        #endregion

        #region Reset

        public void ResetGame()
        {
            lastScore = 0f;
            gameStarted = false;
            gameRunning = false;
            currentTick = 0;
            tickTimer = 0f;
            
            // Clear command queue
            commandQueue.Clear();
            
            // Reset player states
            foreach (var kvp in gameState.GetAllPlayers())
            {
                var playerState = kvp.Value;
                playerState.Score = 0f;
                playerState.IsDead = false;
            }
            
            OnReset?.Invoke();
        }

        #endregion
    }

    /// <summary>
    /// Game data structure for persistence
    /// </summary>
    [System.Serializable]
    public class GameData
    {
        public float highScore;
        public float lastScore;
        public bool audioEnabled;
        public int coins;
    }

    /// <summary>
    /// Player state for game management
    /// </summary>
    [System.Serializable]
    public class PlayerState : RedRunner.Networking.IPlayerState
    {
        public uint PlayerId { get; set; }
        public string PlayerName { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Score { get; set; }
        public int Coins { get; set; }
        public bool IsDead { get; set; }
        public bool IsGrounded { get; set; }
        public uint LastJumpTick { get; set; }
        public int JumpCount { get; set; }
        public Character Character { get; set; }
        
        public byte[] Serialize()
        {
            // Basic serialization - could be enhanced with proper binary serialization
            return System.Text.Encoding.UTF8.GetBytes($"{PlayerId},{PlayerName},{Score},{IsDead}");
        }
        
        public void Deserialize(byte[] data)
        {
            // Basic deserialization - could be enhanced with proper binary deserialization
            string text = System.Text.Encoding.UTF8.GetString(data);
            string[] parts = text.Split(',');
            if (parts.Length >= 4)
            {
                uint.TryParse(parts[0], out uint id);
                PlayerId = id;
                PlayerName = parts[1];
                float.TryParse(parts[2], out float score);
                Score = score;
                bool.TryParse(parts[3], out bool dead);
                IsDead = dead;
            }
        }
    }
}