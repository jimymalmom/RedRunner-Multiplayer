using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;
using RedRunner.Progression;
using RedRunner.Social;
using System.Linq;

namespace RedRunner.Competition
{
    /// <summary>
    /// Comprehensive leaderboards and tournament system for competitive gameplay
    /// Handles global/friends leaderboards, seasonal tournaments, and ranking systems
    /// </summary>
    public class LeaderboardManager : MonoBehaviour
    {
        private static LeaderboardManager instance;
        public static LeaderboardManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<LeaderboardManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("LeaderboardManager");
                        instance = go.AddComponent<LeaderboardManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Leaderboard Configuration")]
        [SerializeField] private string leaderboardApiUrl = "https://api.yourgame.com/leaderboards";
        [SerializeField] private bool enableLeaderboards = true;
        [SerializeField] private LeaderboardConfig[] leaderboards;
        [SerializeField] private int maxLeaderboardEntries = 100;
        [SerializeField] private float leaderboardRefreshInterval = 300f; // 5 minutes

        [Header("Tournament System")]
        [SerializeField] private bool enableTournaments = true;
        [SerializeField] private string tournamentApiUrl = "https://api.yourgame.com/tournaments";
        [SerializeField] private TournamentTemplate[] tournamentTemplates;
        [SerializeField] private float tournamentCheckInterval = 600f; // 10 minutes

        [Header("Ranking System")]
        [SerializeField] private bool enableRanking = true;
        [SerializeField] private RankTier[] rankTiers;
        [SerializeField] private int rankDecayDays = 7;
        [SerializeField] private float rankUpdateInterval = 3600f; // 1 hour

        [Header("Rewards")]
        [SerializeField] private LeaderboardReward[] weeklyRewards;
        [SerializeField] private TournamentReward[] tournamentRewards;
        [SerializeField] private SeasonalReward[] seasonalRewards;

        [Header("Caching")]
        [SerializeField] private float cacheExpiryMinutes = 30f;
        [SerializeField] private bool enableOfflineMode = true;

        // Leaderboard state
        private Dictionary<string, Leaderboard> leaderboards_cache;
        private Dictionary<string, DateTime> lastLeaderboardUpdate;
        private PlayerRanking currentPlayerRanking;

        // Tournament state
        private List<Tournament> activeTournaments;
        private Dictionary<string, Tournament> tournamentLookup;
        private List<Tournament> playerTournaments;
        private DateTime lastTournamentCheck;

        // Ranking system
        private Dictionary<string, PlayerRank> playerRanks;
        private SeasonInfo currentSeason;
        private DateTime lastRankUpdate;

        // Local data
        private CompetitionData competitionData;
        
        // Coroutines
        private Coroutine leaderboardUpdateCoroutine;
        private Coroutine tournamentUpdateCoroutine;
        private Coroutine rankUpdateCoroutine;

        // Events
        public static event Action<string, Leaderboard> OnLeaderboardUpdated;
        public static event Action<Tournament> OnTournamentStarted;
        public static event Action<Tournament> OnTournamentEnded;
        public static event Action<string, int> OnPlayerRankChanged;
        public static event Action<LeaderboardReward> OnLeaderboardRewardEarned;
        public static event Action<TournamentReward> OnTournamentRewardEarned;
        public static event Action<string> OnCompetitionError;

        // Properties
        public bool IsInitialized { get; private set; }
        public List<Tournament> ActiveTournaments => activeTournaments ?? new List<Tournament>();
        public PlayerRanking CurrentPlayerRanking => currentPlayerRanking;

        public string PlayerId => competitionData?.playerId ?? GetPlayerId();
        public SeasonInfo CurrentSeason => currentSeason;

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
            leaderboards_cache = new Dictionary<string, Leaderboard>();
            lastLeaderboardUpdate = new Dictionary<string, DateTime>();
            activeTournaments = new List<Tournament>();
            tournamentLookup = new Dictionary<string, Tournament>();
            playerTournaments = new List<Tournament>();
            playerRanks = new Dictionary<string, PlayerRank>();

            LoadCompetitionData();
            InitializeRankSystem();

            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                StartCoroutine(InitializeCompetitionSystems());
            }
            else if (enableOfflineMode)
            {
                InitializeOfflineMode();
            }
        }

        private void LoadCompetitionData()
        {
            if (SaveGame.Exists("CompetitionData"))
            {
                try
                {
                    string json = SaveGame.Load<string>("CompetitionData");
                    competitionData = JsonUtility.FromJson<CompetitionData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load competition data: {e.Message}");
                    CreateNewCompetitionData();
                }
            }
            else
            {
                CreateNewCompetitionData();
            }

            ValidateCompetitionData();
        }

        private void CreateNewCompetitionData()
        {
            competitionData = new CompetitionData
            {
                playerId = GetPlayerId(),
                playerName = GetPlayerName(),
                personalBests = new Dictionary<string, float>(),
                tournamentHistory = new List<TournamentParticipation>(),
                weeklyStats = new WeeklyStats(),
                seasonalStats = new SeasonalStats(),
                lastSeasonParticipation = "",
                totalTrophies = 0
            };

            SaveCompetitionData();
        }

        private void ValidateCompetitionData()
        {
            if (competitionData.personalBests == null)
                competitionData.personalBests = new Dictionary<string, float>();

            if (competitionData.tournamentHistory == null)
                competitionData.tournamentHistory = new List<TournamentParticipation>();

            if (competitionData.weeklyStats == null)
                competitionData.weeklyStats = new WeeklyStats();

            if (competitionData.seasonalStats == null)
                competitionData.seasonalStats = new SeasonalStats();

            if (string.IsNullOrEmpty(competitionData.playerId))
                competitionData.playerId = GetPlayerId();

            if (string.IsNullOrEmpty(competitionData.playerName))
                competitionData.playerName = GetPlayerName();
        }

        private void InitializeRankSystem()
        {
            // Initialize player ranking
            currentPlayerRanking = new PlayerRanking
            {
                playerId = competitionData.playerId,
                playerName = competitionData.playerName,
                currentRank = GetPlayerCurrentRank(),
                trophies = competitionData.totalTrophies,
                seasonWins = competitionData.seasonalStats.wins,
                globalPosition = 0, // Will be updated from server
                friendsPosition = 0
            };
        }

        private string GetPlayerCurrentRank()
        {
            int trophies = competitionData.totalTrophies;

            foreach (var tier in rankTiers)
            {
                if (trophies >= tier.minTrophies && trophies <= tier.maxTrophies)
                {
                    return tier.rankName;
                }
            }

            return rankTiers[0].rankName; // Default to lowest rank
        }

        private IEnumerator InitializeCompetitionSystems()
        {
            Debug.Log("Initializing competition systems...");

            // Initialize leaderboards
            if (enableLeaderboards)
            {
                yield return StartCoroutine(InitializeLeaderboards());

                if (leaderboardRefreshInterval > 0)
                {
                    leaderboardUpdateCoroutine = StartCoroutine(LeaderboardUpdateRoutine());
                }
            }

            // Initialize tournaments
            if (enableTournaments)
            {
                yield return StartCoroutine(InitializeTournaments());

                if (tournamentCheckInterval > 0)
                {
                    tournamentUpdateCoroutine = StartCoroutine(TournamentUpdateRoutine());
                }
            }

            // Initialize ranking system
            if (enableRanking)
            {
                yield return StartCoroutine(InitializeRanking());

                if (rankUpdateInterval > 0)
                {
                    rankUpdateCoroutine = StartCoroutine(RankUpdateRoutine());
                }
            }

            IsInitialized = true;

            // Analytics
            AnalyticsManager.Instance?.TrackEvent("competition_systems_initialized", new Dictionary<string, object>
            {
                { "player_trophies", competitionData.totalTrophies },
                { "active_tournaments", activeTournaments.Count },
                { "current_rank", currentPlayerRanking.currentRank }
            });
        }

        private void InitializeOfflineMode()
        {
            Debug.Log("Initializing competition systems in offline mode");
            IsInitialized = true;
        }

        private IEnumerator InitializeLeaderboards()
        {
            Debug.Log("Initializing leaderboards...");

            foreach (var config in leaderboards)
            {
                yield return StartCoroutine(FetchLeaderboard(config.leaderboardId));
            }
        }

        private IEnumerator FetchLeaderboard(string leaderboardId)
        {
            string url = $"{leaderboardApiUrl}/{leaderboardId}?player_id={competitionData.playerId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        var leaderboard = JsonUtility.FromJson<Leaderboard>(json);

                        if (leaderboard != null)
                        {
                            leaderboards_cache[leaderboardId] = leaderboard;
                            lastLeaderboardUpdate[leaderboardId] = DateTime.UtcNow;

                            OnLeaderboardUpdated?.Invoke(leaderboardId, leaderboard);

                            Debug.Log($"Leaderboard updated: {leaderboardId} ({leaderboard.entries.Length} entries)");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse leaderboard {leaderboardId}: {e.Message}");
                        OnCompetitionError?.Invoke($"Leaderboard parse error: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to fetch leaderboard {leaderboardId}: {request.error}");
                    OnCompetitionError?.Invoke($"Leaderboard fetch error: {request.error}");
                }
            }
        }

        private IEnumerator InitializeTournaments()
        {
            Debug.Log("Initializing tournaments...");

            string url = $"{tournamentApiUrl}/active?player_id={competitionData.playerId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        var tournamentsResponse = JsonUtility.FromJson<TournamentsResponse>(json);

                        if (tournamentsResponse?.tournaments != null)
                        {
                            UpdateTournaments(tournamentsResponse.tournaments);
                            lastTournamentCheck = DateTime.UtcNow;

                            Debug.Log($"Tournaments updated: {activeTournaments.Count} active tournaments");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse tournaments: {e.Message}");
                        OnCompetitionError?.Invoke($"Tournaments parse error: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to fetch tournaments: {request.error}");
                    OnCompetitionError?.Invoke($"Tournaments fetch error: {request.error}");
                }
            }
        }

        private void UpdateTournaments(Tournament[] newTournaments)
        {
            var now = DateTime.UtcNow;
            var previousTournamentIds = new HashSet<string>(tournamentLookup.Keys);

            // Clear expired tournaments
            activeTournaments.RemoveAll(t => t.endTime <= now);

            foreach (var tournament in newTournaments)
            {
                if (tournament.startTime <= now && tournament.endTime > now)
                {
                    bool isNewTournament = !tournamentLookup.ContainsKey(tournament.id);

                    if (isNewTournament)
                    {
                        activeTournaments.Add(tournament);
                        tournamentLookup[tournament.id] = tournament;
                        OnTournamentStarted?.Invoke(tournament);

                        Debug.Log($"New tournament started: {tournament.name}");

                        // Auto-join if player qualifies
                        if (CanPlayerJoinTournament(tournament))
                        {
                            StartCoroutine(JoinTournament(tournament.id));
                        }

                        // Analytics
                        AnalyticsManager.Instance?.TrackEvent("tournament_started", new Dictionary<string, object>
                        {
                            { "tournament_id", tournament.id },
                            { "tournament_type", tournament.type.ToString() },
                            { "duration_hours", (tournament.endTime - tournament.startTime).TotalHours }
                        });
                    }

                    previousTournamentIds.Remove(tournament.id);
                }
            }

            // Handle ended tournaments
            foreach (string endedTournamentId in previousTournamentIds)
            {
                if (tournamentLookup.TryGetValue(endedTournamentId, out Tournament endedTournament))
                {
                    OnTournamentEnded?.Invoke(endedTournament);
                    tournamentLookup.Remove(endedTournamentId);

                    // Process tournament results
                    ProcessTournamentEnd(endedTournament);

                    Debug.Log($"Tournament ended: {endedTournament.name}");
                }
            }
        }

        private bool CanPlayerJoinTournament(Tournament tournament)
        {
            // Check entry requirements
            if (tournament.entryRequirement.minLevel > ProgressionManager.Instance?.CurrentLevel)
                return false;

            if (tournament.entryRequirement.minTrophies > competitionData.totalTrophies)
                return false;

            if (tournament.entryRequirement.entryCost > ProgressionManager.Instance?.Coins)
                return false;

            return true;
        }

        private IEnumerator JoinTournament(string tournamentId)
        {
            Debug.Log($"Joining tournament: {tournamentId}");

            string url = $"{tournamentApiUrl}/{tournamentId}/join";
            var joinData = new TournamentJoinRequest
            {
                playerId = competitionData.playerId,
                playerName = competitionData.playerName,
                currentTrophies = competitionData.totalTrophies
            };

            string jsonData = JsonUtility.ToJson(joinData);
            byte[] postData = System.Text.Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(postData);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 30;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"Successfully joined tournament: {tournamentId}");

                    // Add to player tournaments
                    if (tournamentLookup.TryGetValue(tournamentId, out Tournament tournament))
                    {
                        playerTournaments.Add(tournament);
                    }

                    // Analytics
                    AnalyticsManager.Instance?.TrackEvent("tournament_joined", new Dictionary<string, object>
                    {
                        { "tournament_id", tournamentId },
                        { "player_trophies", competitionData.totalTrophies }
                    });
                }
                else
                {
                    Debug.LogError($"Failed to join tournament {tournamentId}: {request.error}");
                    OnCompetitionError?.Invoke($"Tournament join error: {request.error}");
                }
            }
        }

        private void ProcessTournamentEnd(Tournament tournament)
        {
            // Check if player participated
            var participation = playerTournaments.Find(t => t.id == tournament.id);
            if (participation != null)
            {
                // Create tournament history entry
                var historyEntry = new TournamentParticipation
                {
                    tournamentId = tournament.id,
                    tournamentName = tournament.name,
                    participationDate = tournament.startTime,
                    finalPosition = GetPlayerTournamentPosition(tournament.id),
                    pointsEarned = GetPlayerTournamentPoints(tournament.id),
                    rewardsEarned = new List<string>()
                };

                competitionData.tournamentHistory.Add(historyEntry);

                // Process rewards
                ProcessTournamentRewards(tournament, historyEntry.finalPosition);

                // Remove from active player tournaments
                playerTournaments.Remove(participation);

                SaveCompetitionData();
            }
        }

        private int GetPlayerTournamentPosition(string tournamentId)
        {
            // This would typically come from server response
            // For simulation, return a random position
            return UnityEngine.Random.Range(1, 101);
        }

        private int GetPlayerTournamentPoints(string tournamentId)
        {
            // This would typically come from server response
            // For simulation, return player's current score or a calculated value
            return UnityEngine.Random.Range(1000, 10000);
        }

        private void ProcessTournamentRewards(Tournament tournament, int position)
        {
            foreach (var reward in tournamentRewards)
            {
                if (position <= reward.maxPosition)
                {
                    GrantTournamentReward(reward);
                    OnTournamentRewardEarned?.Invoke(reward);
                    break; // Only grant the first applicable reward
                }
            }
        }

        private void GrantTournamentReward(TournamentReward reward)
        {
            switch (reward.rewardType)
            {
                case "coins":
                    ProgressionManager.Instance?.AddCurrency(CurrencyType.Coins, reward.amount);
                    break;

                case "gems":
                    ProgressionManager.Instance?.AddCurrency(CurrencyType.Gems, reward.amount);
                    break;

                case "trophies":
                    competitionData.totalTrophies += reward.amount;
                    UpdatePlayerRank();
                    break;

                case "experience":
                    ProgressionManager.Instance?.AddExperience(reward.amount, "tournament_reward");
                    break;
            }
        }

        private IEnumerator InitializeRanking()
        {
            Debug.Log("Initializing ranking system...");

            string url = $"{leaderboardApiUrl}/ranking?player_id={competitionData.playerId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        var rankingResponse = JsonUtility.FromJson<RankingResponse>(json);

                        if (rankingResponse != null)
                        {
                            currentPlayerRanking = rankingResponse.playerRanking;
                            currentSeason = rankingResponse.currentSeason;
                            lastRankUpdate = DateTime.UtcNow;

                            Debug.Log($"Player ranking initialized: {currentPlayerRanking.currentRank} ({currentPlayerRanking.trophies} trophies)");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse ranking: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to fetch ranking: {request.error}");
                }
            }
        }

        private IEnumerator LeaderboardUpdateRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(leaderboardRefreshInterval);

                if (Application.internetReachability != NetworkReachability.NotReachable)
                {
                    foreach (var config in leaderboards)
                    {
                        yield return StartCoroutine(FetchLeaderboard(config.leaderboardId));
                    }
                }
            }
        }

        private IEnumerator TournamentUpdateRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(tournamentCheckInterval);

                if (Application.internetReachability != NetworkReachability.NotReachable)
                {
                    yield return StartCoroutine(InitializeTournaments());
                }

                CheckExpiredTournaments();
            }
        }

        private void CheckExpiredTournaments()
        {
            var now = DateTime.UtcNow;
            var expiredTournaments = activeTournaments.FindAll(t => t.endTime <= now);

            foreach (var expiredTournament in expiredTournaments)
            {
                activeTournaments.Remove(expiredTournament);
                tournamentLookup.Remove(expiredTournament.id);
                OnTournamentEnded?.Invoke(expiredTournament);

                ProcessTournamentEnd(expiredTournament);
            }
        }

        private IEnumerator RankUpdateRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(rankUpdateInterval);

                if (Application.internetReachability != NetworkReachability.NotReachable)
                {
                    yield return StartCoroutine(UpdatePlayerRanking());
                }

                CheckRankDecay();
            }
        }

        private IEnumerator UpdatePlayerRanking()
        {
            yield return StartCoroutine(InitializeRanking());
        }

        private void CheckRankDecay()
        {
            // Implement rank decay for inactive players
            if ((DateTime.UtcNow - lastRankUpdate).TotalDays >= rankDecayDays)
            {
                ApplyRankDecay();
            }
        }

        private void ApplyRankDecay()
        {
            // Reduce trophies for inactivity
            int decayAmount = Mathf.RoundToInt(competitionData.totalTrophies * 0.05f); // 5% decay
            competitionData.totalTrophies = Mathf.Max(0, competitionData.totalTrophies - decayAmount);

            UpdatePlayerRank();
            SaveCompetitionData();

            Debug.Log($"Rank decay applied: -{decayAmount} trophies");
        }

        // Public API
        public void SubmitScore(string leaderboardId, float score)
        {
            // Update personal best
            if (!competitionData.personalBests.TryGetValue(leaderboardId, out float currentBest) || score > currentBest)
            {
                competitionData.personalBests[leaderboardId] = score;
                SaveCompetitionData();

                Debug.Log($"New personal best: {leaderboardId} = {score}");
            }

            // Submit to server
            StartCoroutine(SubmitScoreToServer(leaderboardId, score));

            // Analytics
            AnalyticsManager.Instance?.TrackEvent("score_submitted", new Dictionary<string, object>
            {
                { "leaderboard_id", leaderboardId },
                { "score", score },
                { "is_personal_best", score > currentBest }
            });
        }

        private IEnumerator SubmitScoreToServer(string leaderboardId, float score)
        {
            string url = $"{leaderboardApiUrl}/{leaderboardId}/submit";
            var scoreSubmission = new ScoreSubmission
            {
                playerId = competitionData.playerId,
                playerName = competitionData.playerName,
                score = score,
                timestamp = DateTime.UtcNow
            };

            string jsonData = JsonUtility.ToJson(scoreSubmission);
            byte[] postData = System.Text.Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(postData);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 30;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"Score submitted successfully: {leaderboardId} = {score}");
                    
                    // Refresh leaderboard after submission
                    yield return StartCoroutine(FetchLeaderboard(leaderboardId));
                }
                else
                {
                    Debug.LogError($"Failed to submit score: {request.error}");
                }
            }
        }

        public Leaderboard GetLeaderboard(string leaderboardId)
        {
            return leaderboards_cache.TryGetValue(leaderboardId, out Leaderboard leaderboard) ? leaderboard : null;
        }

        public List<LeaderboardEntry> GetFriendsLeaderboard(string leaderboardId)
        {
            var leaderboard = GetLeaderboard(leaderboardId);
            if (leaderboard == null) return new List<LeaderboardEntry>();

            // Filter for friends only
            var friendsList = SocialManager.Instance?.Friends ?? new List<Social.Friend>();
            var friendIds = new HashSet<string>(friendsList.ConvertAll(f => f.playerId));

            var friendsEntries = new List<LeaderboardEntry>();
            foreach (var entry in leaderboard.entries)
            {
                if (friendIds.Contains(entry.playerId) || entry.playerId == competitionData.playerId)
                {
                    friendsEntries.Add(entry);
                }
            }

            return friendsEntries;
        }

        public int GetPlayerLeaderboardPosition(string leaderboardId)
        {
            var leaderboard = GetLeaderboard(leaderboardId);
            if (leaderboard == null) return -1;

            for (int i = 0; i < leaderboard.entries.Length; i++)
            {
                if (leaderboard.entries[i].playerId == competitionData.playerId)
                {
                    return i + 1; // Position is 1-based
                }
            }

            return -1; // Not found
        }

        public float GetPersonalBest(string leaderboardId)
        {
            return competitionData.personalBests.TryGetValue(leaderboardId, out float best) ? best : 0f;
        }

        public Tournament GetTournament(string tournamentId)
        {
            return tournamentLookup.TryGetValue(tournamentId, out Tournament tournament) ? tournament : null;
        }

        public bool IsPlayerInTournament(string tournamentId)
        {
            return playerTournaments.Any(t => t.id == tournamentId);
        }

        public void RefreshLeaderboard(string leaderboardId)
        {
            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                StartCoroutine(FetchLeaderboard(leaderboardId));
            }
        }

        public void RefreshTournaments()
        {
            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                StartCoroutine(InitializeTournaments());
            }
        }

        private void UpdatePlayerRank()
        {
            string oldRank = currentPlayerRanking?.currentRank ?? "";
            string newRank = GetPlayerCurrentRank();

            if (newRank != oldRank)
            {
                if (currentPlayerRanking != null)
                {
                    currentPlayerRanking.currentRank = newRank;
                    currentPlayerRanking.trophies = competitionData.totalTrophies;
                }

                OnPlayerRankChanged?.Invoke(newRank, competitionData.totalTrophies);

                // Analytics
                AnalyticsManager.Instance?.TrackEvent("player_rank_changed", new Dictionary<string, object>
                {
                    { "old_rank", oldRank },
                    { "new_rank", newRank },
                    { "trophies", competitionData.totalTrophies }
                });
            }
        }

        private void SaveCompetitionData()
        {
            try
            {
                string json = JsonUtility.ToJson(competitionData);
                SaveGame.Save("CompetitionData", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save competition data: {e.Message}");
            }
        }

        private string GetPlayerId()
        {
            return ProgressionManager.Instance?.PlayerData?.playerId ?? SystemInfo.deviceUniqueIdentifier;
        }

        private string GetPlayerName()
        {
            return SocialManager.Instance?.SocialData?.playerName ?? $"Player_{UnityEngine.Random.Range(1000, 9999)}";
        }

        void OnDestroy()
        {
            if (leaderboardUpdateCoroutine != null)
                StopCoroutine(leaderboardUpdateCoroutine);

            if (tournamentUpdateCoroutine != null)
                StopCoroutine(tournamentUpdateCoroutine);

            if (rankUpdateCoroutine != null)
                StopCoroutine(rankUpdateCoroutine);

            SaveCompetitionData();
        }
    }

    // Data structures
    [System.Serializable]
    public class LeaderboardConfig
    {
        public string leaderboardId;
        public string displayName;
        public LeaderboardType type;
        public bool showFriendsOnly;
        public int refreshIntervalSeconds;
    }

    [System.Serializable]
    public class Leaderboard
    {
        public string id;
        public string name;
        public LeaderboardType type;
        public LeaderboardEntry[] entries;
        public DateTime lastUpdated;
        public int totalParticipants;
    }

    [System.Serializable]
    public class LeaderboardEntry
    {
        public string playerId;
        public string playerName;
        public float score;
        public int position;
        public string avatarUrl;
        public DateTime submitTime;
    }

    [System.Serializable]
    public class Tournament
    {
        public string id;
        public string name;
        public string description;
        public TournamentType type;
        public DateTime startTime;
        public DateTime endTime;
        public TournamentEntryRequirement entryRequirement;
        public TournamentSettings settings;
        public int maxParticipants;
        public int currentParticipants;
        public TournamentReward[] rewards;
    }

    [System.Serializable]
    public class TournamentTemplate
    {
        public string templateId;
        public string name;
        public TournamentType type;
        public float durationHours;
        public TournamentEntryRequirement entryRequirement;
        public TournamentReward[] rewards;
        public bool isRecurring;
        public float recurringIntervalHours;
    }

    [System.Serializable]
    public class TournamentEntryRequirement
    {
        public int minLevel;
        public int minTrophies;
        public int entryCost;
        public string requiredItem;
    }

    [System.Serializable]
    public class TournamentSettings
    {
        public int maxAttempts;
        public float timeLimit;
        public bool allowPowerUps;
        public string specialRules;
    }

    [System.Serializable]
    public class RankTier
    {
        public string rankName;
        public int minTrophies;
        public int maxTrophies;
        public string iconPath;
        public Color rankColor;
    }

    [System.Serializable]
    public class PlayerRanking
    {
        public string playerId;
        public string playerName;
        public string currentRank;
        public int trophies;
        public int seasonWins;
        public int globalPosition;
        public int friendsPosition;
    }

    [System.Serializable]
    public class SeasonInfo
    {
        public string seasonId;
        public string seasonName;
        public DateTime startDate;
        public DateTime endDate;
        public SeasonalReward[] rewards;
    }

    [System.Serializable]
    public class CompetitionData
    {
        public string playerId;
        public string playerName;
        public Dictionary<string, float> personalBests;
        public List<TournamentParticipation> tournamentHistory;
        public WeeklyStats weeklyStats;
        public SeasonalStats seasonalStats;
        public string lastSeasonParticipation;
        public int totalTrophies;
    }

    [System.Serializable]
    public class TournamentParticipation
    {
        public string tournamentId;
        public string tournamentName;
        public DateTime participationDate;
        public int finalPosition;
        public int pointsEarned;
        public List<string> rewardsEarned;
    }

    [System.Serializable]
    public class WeeklyStats
    {
        public int gamesPlayed = 0;
        public int wins = 0;
        public float totalScore = 0;
        public DateTime weekStartDate;
    }

    [System.Serializable]
    public class SeasonalStats
    {
        public int wins = 0;
        public int losses = 0;
        public int trophiesGained = 0;
        public int trophiesLost = 0;
        public int tournamentsWon = 0;
    }

    [System.Serializable]
    public class LeaderboardReward
    {
        public int minPosition;
        public int maxPosition;
        public string rewardType;
        public int amount;
        public string itemId;
    }

    [System.Serializable]
    public class TournamentReward
    {
        public int minPosition;
        public int maxPosition;
        public string rewardType;
        public int amount;
        public string itemId;
    }

    [System.Serializable]
    public class SeasonalReward
    {
        public string rankRequirement;
        public int minTrophies;
        public string rewardType;
        public int amount;
        public string itemId;
    }

    // Request/Response classes
    [System.Serializable]
    public class ScoreSubmission
    {
        public string playerId;
        public string playerName;
        public float score;
        public DateTime timestamp;
    }

    [System.Serializable]
    public class TournamentJoinRequest
    {
        public string playerId;
        public string playerName;
        public int currentTrophies;
    }

    [System.Serializable]
    public class TournamentsResponse
    {
        public Tournament[] tournaments;
    }

    [System.Serializable]
    public class RankingResponse
    {
        public PlayerRanking playerRanking;
        public SeasonInfo currentSeason;
    }

    // Enums
    public enum LeaderboardType
    {
        HighScore,
        Distance,
        Coins,
        Time,
        Weekly,
        AllTime
    }

    public enum TournamentType
    {
        HighScore,
        Survival,
        TimeAttack,
        Collection,
        Elimination
    }
}