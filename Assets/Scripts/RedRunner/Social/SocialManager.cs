using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BayatGames.SaveGameFree;
using RedRunner.Analytics;
using RedRunner.Progression;
using System.Linq;
using RedRunner.Networking;

namespace RedRunner.Social
{
    /// <summary>
    /// Comprehensive social system with friends, chat, and community features
    /// Handles player relationships, messaging, and social engagement mechanics
    /// </summary>
    public class SocialManager : MonoBehaviour
    {
        private static SocialManager instance;
        public static SocialManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<SocialManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("SocialManager");
                        instance = go.AddComponent<SocialManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Social Configuration")]
        [SerializeField] private int maxFriends = 100;
        [SerializeField] private int maxChatHistory = 50;
        [SerializeField] private float friendRequestTimeout = 72f; // Hours
        [SerializeField] private bool enableGlobalChat = true;
        [SerializeField] private bool enableEmotes = true;
        
        [Header("Content Filtering")]
        [SerializeField] private bool enableProfanityFilter = true;
        [SerializeField] private string[] bannedWords;
        [SerializeField] private int maxMessageLength = 140;
        [SerializeField] private float chatCooldown = 1f;
        
        [Header("Clan System")]
        [SerializeField] private bool enableClans = true;
        [SerializeField] private int maxClanMembers = 50;
        [SerializeField] private int clanCreationCost = 1000; // Coins
        
        // Social data
        private SocialData socialData;
        private Dictionary<string, PlayerProfile> playerProfiles;
        private List<ChatMessage> chatHistory;
        private Dictionary<string, ClanInfo> clanCache;
        
        // State tracking
        private float lastChatTime = 0f;
        private bool isOnline = false;
        private PlayerStatus currentStatus = PlayerStatus.Online;
        
        // Events
        public static event Action<FriendRequest> OnFriendRequestReceived;
        public static event Action<string> OnFriendAdded;
        public static event Action<string> OnFriendRemoved;
        public static event Action<ChatMessage> OnChatMessageReceived;
        public static event Action<string, PlayerStatus> OnFriendStatusChanged;
        public static event Action<ClanInvite> OnClanInviteReceived;
        public static event Action<string> OnClanJoined;
        public static event Action OnClanLeft;
        
        // Properties
        public SocialData SocialData => socialData;
        public bool IsOnline => isOnline;
        public PlayerStatus CurrentStatus => currentStatus;
        public List<Friend> Friends => socialData?.friends ?? new List<Friend>();
        public string CurrentClanId => socialData?.currentClanId;

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
            LoadSocialData();
            playerProfiles = new Dictionary<string, PlayerProfile>();
            chatHistory = new List<ChatMessage>();
            clanCache = new Dictionary<string, ClanInfo>();
            
            // Subscribe to network events
            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.OnPlayerJoined += OnPlayerJoinedGame;
                NetworkGameManager.OnPlayerLeft += OnPlayerLeftGame;
            }
            
            // Start social services
            StartCoroutine(SocialUpdateLoop());
        }

        private void LoadSocialData()
        {
            if (SaveGame.Exists("SocialData"))
            {
                try
                {
                    string json = SaveGame.Load<string>("SocialData");
                    socialData = JsonUtility.FromJson<SocialData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load social data: {e.Message}");
                    CreateNewSocialData();
                }
            }
            else
            {
                CreateNewSocialData();
            }
            
            ValidateSocialData();
        }

        private void CreateNewSocialData()
        {
            socialData = new SocialData
            {
                playerId = System.Guid.NewGuid().ToString(),
                playerName = "Player_" + UnityEngine.Random.Range(1000, 9999),
                friends = new List<Friend>(),
                blockedPlayers = new List<string>(),
                friendRequests = new List<FriendRequest>(),
                clanInvites = new List<ClanInvite>(),
                settings = new SocialSettings
                {
                    allowFriendRequests = true,
                    allowClanInvites = true,
                    showOnlineStatus = true,
                    allowDirectMessages = true
                }
            };
            
            SaveSocialData();
        }

        private void ValidateSocialData()
        {
            if (socialData.friends == null)
                socialData.friends = new List<Friend>();
                
            if (socialData.blockedPlayers == null)
                socialData.blockedPlayers = new List<string>();
                
            if (socialData.friendRequests == null)
                socialData.friendRequests = new List<FriendRequest>();
                
            if (socialData.clanInvites == null)
                socialData.clanInvites = new List<ClanInvite>();
                
            if (socialData.settings == null)
                socialData.settings = new SocialSettings();
        }

        private void SaveSocialData()
        {
            try
            {
                string json = JsonUtility.ToJson(socialData);
                SaveGame.Save("SocialData", json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save social data: {e.Message}");
            }
        }

        private IEnumerator SocialUpdateLoop()
        {
            while (true)
            {
                if (isOnline)
                {
                    UpdateFriendStatuses();
                    ProcessPendingRequests();
                    SyncWithServer();
                }
                
                yield return new WaitForSeconds(30f); // Update every 30 seconds
            }
        }

        public void SetOnlineStatus(bool online)
        {
            isOnline = online;
            
            if (online)
            {
                SetPlayerStatus(PlayerStatus.Online);
            }
            else
            {
                SetPlayerStatus(PlayerStatus.Offline);
            }
        }

        public void SetPlayerStatus(PlayerStatus status)
        {
            currentStatus = status;
            
            // Notify server and friends
            NotifyStatusChange(status);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("player_status_changed", new Dictionary<string, object>
            {
                { "status", status.ToString() },
                { "friends_count", socialData.friends.Count }
            });
        }

        public void SendFriendRequest(string targetPlayerId, string message = "")
        {
            if (!CanSendFriendRequest(targetPlayerId))
                return;
            
            var request = new FriendRequest
            {
                requestId = System.Guid.NewGuid().ToString(),
                fromPlayerId = socialData.playerId,
                fromPlayerName = socialData.playerName,
                toPlayerId = targetPlayerId,
                message = FilterMessage(message),
                timestamp = DateTime.UtcNow,
                status = RequestStatus.Pending
            };
            
            // Send to server (simulated)
            SendFriendRequestToServer(request);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("friend_request_sent", new Dictionary<string, object>
            {
                { "target_player_id", targetPlayerId },
                { "has_message", !string.IsNullOrEmpty(message) }
            });
        }

        private bool CanSendFriendRequest(string targetPlayerId)
        {
            if (string.IsNullOrEmpty(targetPlayerId))
                return false;
                
            if (targetPlayerId == socialData.playerId)
                return false;
                
            if (IsBlocked(targetPlayerId))
                return false;
                
            if (IsFriend(targetPlayerId))
                return false;
                
            if (HasPendingRequest(targetPlayerId))
                return false;
                
            if (socialData.friends.Count >= maxFriends)
                return false;
                
            return true;
        }

        public void AcceptFriendRequest(string requestId)
        {
            var request = socialData.friendRequests.Find(r => r.requestId == requestId);
            if (request == null || request.status != RequestStatus.Pending)
                return;
            
            request.status = RequestStatus.Accepted;
            
            // Add friend
            var friend = new Friend
            {
                playerId = request.fromPlayerId,
                playerName = request.fromPlayerName,
                friendshipDate = DateTime.UtcNow,
                status = PlayerStatus.Unknown,
                lastSeen = DateTime.UtcNow
            };
            
            socialData.friends.Add(friend);
            socialData.friendRequests.Remove(request);
            
            // Notify server
            NotifyFriendRequestResponse(requestId, true);
            
            OnFriendAdded?.Invoke(friend.playerId);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("friend_request_accepted", new Dictionary<string, object>
            {
                { "from_player_id", request.fromPlayerId },
                { "friends_count", socialData.friends.Count }
            });
            
            SaveSocialData();
        }

        public void DeclineFriendRequest(string requestId)
        {
            var request = socialData.friendRequests.Find(r => r.requestId == requestId);
            if (request == null || request.status != RequestStatus.Pending)
                return;
            
            request.status = RequestStatus.Declined;
            socialData.friendRequests.Remove(request);
            
            // Notify server
            NotifyFriendRequestResponse(requestId, false);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("friend_request_declined", new Dictionary<string, object>
            {
                { "from_player_id", request.fromPlayerId }
            });
            
            SaveSocialData();
        }

        public void RemoveFriend(string playerId)
        {
            var friend = socialData.friends.Find(f => f.playerId == playerId);
            if (friend == null)
                return;
            
            socialData.friends.Remove(friend);
            
            // Notify server
            NotifyFriendRemoval(playerId);
            
            OnFriendRemoved?.Invoke(playerId);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("friend_removed", new Dictionary<string, object>
            {
                { "friend_player_id", playerId },
                { "friends_count", socialData.friends.Count }
            });
            
            SaveSocialData();
        }

        public void SendChatMessage(string message, string targetPlayerId = "", ChatChannel channel = ChatChannel.Global)
        {
            if (Time.time - lastChatTime < chatCooldown)
                return;
            
            message = FilterMessage(message);
            if (string.IsNullOrEmpty(message))
                return;
            
            var chatMessage = new ChatMessage
            {
                messageId = System.Guid.NewGuid().ToString(),
                fromPlayerId = socialData.playerId,
                fromPlayerName = socialData.playerName,
                toPlayerId = targetPlayerId,
                message = message,
                channel = channel,
                timestamp = DateTime.UtcNow
            };
            
            // Add to local history
            chatHistory.Add(chatMessage);
            if (chatHistory.Count > maxChatHistory)
            {
                chatHistory.RemoveAt(0);
            }
            
            // Send to server
            SendChatMessageToServer(chatMessage);
            
            lastChatTime = Time.time;
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("chat_message_sent", new Dictionary<string, object>
            {
                { "channel", channel.ToString() },
                { "message_length", message.Length },
                { "is_direct_message", !string.IsNullOrEmpty(targetPlayerId) }
            });
        }

        private string FilterMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return "";
            
            // Trim and limit length
            message = message.Trim();
            if (message.Length > maxMessageLength)
            {
                message = message.Substring(0, maxMessageLength);
            }
            
            // Apply profanity filter
            if (enableProfanityFilter && bannedWords != null)
            {
                foreach (var bannedWord in bannedWords)
                {
                    if (message.Contains(bannedWord))
                    {
                        message = message.Replace(bannedWord, new string('*', bannedWord.Length));
                    }
                }
            }
            
            return message;
        }

        public void BlockPlayer(string playerId)
        {
            if (IsBlocked(playerId))
                return;
            
            socialData.blockedPlayers.Add(playerId);
            
            // Remove from friends if they are one
            var friend = socialData.friends.Find(f => f.playerId == playerId);
            if (friend != null)
            {
                RemoveFriend(playerId);
            }
            
            // Remove any pending friend requests
            socialData.friendRequests.RemoveAll(r => r.fromPlayerId == playerId || r.toPlayerId == playerId);
            
            // Notify server
            NotifyPlayerBlocked(playerId);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("player_blocked", new Dictionary<string, object>
            {
                { "blocked_player_id", playerId }
            });
            
            SaveSocialData();
        }

        public void UnblockPlayer(string playerId)
        {
            socialData.blockedPlayers.Remove(playerId);
            
            // Notify server
            NotifyPlayerUnblocked(playerId);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("player_unblocked", new Dictionary<string, object>
            {
                { "unblocked_player_id", playerId }
            });
            
            SaveSocialData();
        }

        public void CreateClan(string clanName, string description = "", string tag = "")
        {
            if (!enableClans)
                return;
                
            if (!string.IsNullOrEmpty(socialData.currentClanId))
                return; // Already in a clan
                
            // Check if player has enough coins
            var progressionManager = ProgressionManager.Instance;
            if (progressionManager != null && progressionManager.Coins < clanCreationCost)
                return;
            
            var clanRequest = new ClanCreationRequest
            {
                clanName = clanName,
                description = description,
                tag = tag,
                creatorId = socialData.playerId,
                creatorName = socialData.playerName
            };
            
            // Send to server (simulated)
            SendClanCreationRequest(clanRequest);
            
            // Deduct coins
            progressionManager?.AddCurrency(Progression.CurrencyType.Coins, -clanCreationCost);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("clan_creation_requested", new Dictionary<string, object>
            {
                { "clan_name", clanName },
                { "cost", clanCreationCost }
            });
        }

        public void SendClanInvite(string clanId, string targetPlayerId)
        {
            if (!enableClans || socialData.currentClanId != clanId)
                return;
            
            var invite = new ClanInvite
            {
                inviteId = System.Guid.NewGuid().ToString(),
                clanId = clanId,
                fromPlayerId = socialData.playerId,
                fromPlayerName = socialData.playerName,
                toPlayerId = targetPlayerId,
                timestamp = DateTime.UtcNow,
                status = RequestStatus.Pending
            };
            
            // Send to server
            SendClanInviteToServer(invite);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("clan_invite_sent", new Dictionary<string, object>
            {
                { "clan_id", clanId },
                { "target_player_id", targetPlayerId }
            });
        }

        public void AcceptClanInvite(string inviteId)
        {
            var invite = socialData.clanInvites.Find(i => i.inviteId == inviteId);
            if (invite == null || invite.status != RequestStatus.Pending)
                return;
            
            invite.status = RequestStatus.Accepted;
            socialData.currentClanId = invite.clanId;
            socialData.clanInvites.Remove(invite);
            
            // Notify server
            NotifyClanInviteResponse(inviteId, true);
            
            OnClanJoined?.Invoke(invite.clanId);
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("clan_invite_accepted", new Dictionary<string, object>
            {
                { "clan_id", invite.clanId }
            });
            
            SaveSocialData();
        }

        public void LeaveClan()
        {
            if (string.IsNullOrEmpty(socialData.currentClanId))
                return;
            
            string clanId = socialData.currentClanId;
            socialData.currentClanId = "";
            
            // Notify server
            NotifyClanLeave(clanId);
            
            OnClanLeft?.Invoke();
            
            // Analytics
            AnalyticsManager.Instance?.TrackEvent("clan_left", new Dictionary<string, object>
            {
                { "clan_id", clanId }
            });
            
            SaveSocialData();
        }

        // Query methods
        public bool IsFriend(string playerId) => socialData.friends.Any(f => f.playerId == playerId);
        public bool IsBlocked(string playerId) => socialData.blockedPlayers.Contains(playerId);
        public bool HasPendingRequest(string playerId) => socialData.friendRequests.Any(r => r.toPlayerId == playerId && r.status == RequestStatus.Pending);

        public List<Friend> GetOnlineFriends()
        {
            return socialData.friends.Where(f => f.status == PlayerStatus.Online || f.status == PlayerStatus.InGame).ToList();
        }

        public List<ChatMessage> GetChatHistory(ChatChannel channel = ChatChannel.All)
        {
            if (channel == ChatChannel.All)
                return new List<ChatMessage>(chatHistory);
            
            return chatHistory.Where(m => m.channel == channel).ToList();
        }

        public PlayerProfile GetPlayerProfile(string playerId)
        {
            if (playerProfiles.TryGetValue(playerId, out var profile))
                return profile;
            
            // Request from server if not cached
            RequestPlayerProfile(playerId);
            return null;
        }

        // Server communication methods (simulated for this example)
        private void SendFriendRequestToServer(FriendRequest request)
        {
            // In a real implementation, this would send to your backend
            Debug.Log($"Sending friend request from {request.fromPlayerName} to {request.toPlayerId}");
            
            // Simulate server response after delay
            StartCoroutine(SimulateFriendRequestDelivery(request));
        }

        private IEnumerator SimulateFriendRequestDelivery(FriendRequest request)
        {
            yield return new WaitForSeconds(1f);
            
            // Simulate the target player receiving the request
            // In reality, this would be handled by the server and delivered to the target client
            OnFriendRequestReceived?.Invoke(request);
        }

        private void NotifyStatusChange(PlayerStatus status)
        {
            Debug.Log($"Player status changed to: {status}");
            // Send to server to notify friends
        }

        private void NotifyFriendRequestResponse(string requestId, bool accepted)
        {
            Debug.Log($"Friend request {requestId} {(accepted ? "accepted" : "declined")}");
        }

        private void NotifyFriendRemoval(string playerId)
        {
            Debug.Log($"Removed friend: {playerId}");
        }

        private void SendChatMessageToServer(ChatMessage message)
        {
            Debug.Log($"Chat message sent: {message.message}");
            
            // Simulate message delivery
            StartCoroutine(SimulateChatMessageDelivery(message));
        }

        private IEnumerator SimulateChatMessageDelivery(ChatMessage message)
        {
            yield return new WaitForSeconds(0.5f);
            
            OnChatMessageReceived?.Invoke(message);
        }

        private void NotifyPlayerBlocked(string playerId)
        {
            Debug.Log($"Blocked player: {playerId}");
        }

        private void NotifyPlayerUnblocked(string playerId)
        {
            Debug.Log($"Unblocked player: {playerId}");
        }

        private void SendClanCreationRequest(ClanCreationRequest request)
        {
            Debug.Log($"Clan creation requested: {request.clanName}");
        }

        private void SendClanInviteToServer(ClanInvite invite)
        {
            Debug.Log($"Clan invite sent to: {invite.toPlayerId}");
        }

        private void NotifyClanInviteResponse(string inviteId, bool accepted)
        {
            Debug.Log($"Clan invite {inviteId} {(accepted ? "accepted" : "declined")}");
        }

        private void NotifyClanLeave(string clanId)
        {
            Debug.Log($"Left clan: {clanId}");
        }

        private void RequestPlayerProfile(string playerId)
        {
            Debug.Log($"Requesting player profile: {playerId}");
        }

        private void UpdateFriendStatuses()
        {
            // Update friend online statuses from server
        }

        private void ProcessPendingRequests()
        {
            // Clean up expired requests
            var expiredRequests = socialData.friendRequests
                .Where(r => (DateTime.UtcNow - r.timestamp).TotalHours > friendRequestTimeout)
                .ToList();
            
            foreach (var request in expiredRequests)
            {
                socialData.friendRequests.Remove(request);
            }
            
            if (expiredRequests.Count > 0)
            {
                SaveSocialData();
            }
        }

        private void SyncWithServer()
        {
            // Sync social data with server
        }

        // Event handlers
        private void OnPlayerJoinedGame(uint playerId, string playerName)
        {
            var friend = socialData.friends.Find(f => f.playerId == playerId.ToString());
            if (friend != null)
            {
                friend.status = PlayerStatus.InGame;
                friend.lastSeen = DateTime.UtcNow;
                OnFriendStatusChanged?.Invoke(friend.playerId, friend.status);
            }
        }

        private void OnPlayerLeftGame(uint playerId)
        {
            var friend = socialData.friends.Find(f => f.playerId == playerId.ToString());
            if (friend != null)
            {
                friend.status = PlayerStatus.Online;
                OnFriendStatusChanged?.Invoke(friend.playerId, friend.status);
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveSocialData();
                SetPlayerStatus(PlayerStatus.Away);
            }
            else
            {
                SetPlayerStatus(PlayerStatus.Online);
            }
        }

        void OnDestroy()
        {
            SaveSocialData();
            SetPlayerStatus(PlayerStatus.Offline);
        }
    }

    // Data structures
    [System.Serializable]
    public class SocialData
    {
        public string playerId;
        public string playerName;
        public List<Friend> friends;
        public List<string> blockedPlayers;
        public List<FriendRequest> friendRequests;
        public List<ClanInvite> clanInvites;
        public string currentClanId;
        public SocialSettings settings;
    }

    [System.Serializable]
    public class Friend
    {
        public string playerId;
        public string playerName;
        public DateTime friendshipDate;
        public PlayerStatus status;
        public DateTime lastSeen;
        public int level;
        public string avatarUrl;
    }

    [System.Serializable]
    public class FriendRequest
    {
        public string requestId;
        public string fromPlayerId;
        public string fromPlayerName;
        public string toPlayerId;
        public string message;
        public DateTime timestamp;
        public RequestStatus status;
    }

    [System.Serializable]
    public class ChatMessage
    {
        public string messageId;
        public string fromPlayerId;
        public string fromPlayerName;
        public string toPlayerId; // Empty for global chat
        public string message;
        public ChatChannel channel;
        public DateTime timestamp;
    }

    [System.Serializable]
    public class ClanInvite
    {
        public string inviteId;
        public string clanId;
        public string clanName;
        public string fromPlayerId;
        public string fromPlayerName;
        public string toPlayerId;
        public DateTime timestamp;
        public RequestStatus status;
    }

    [System.Serializable]
    public class ClanInfo
    {
        public string clanId;
        public string clanName;
        public string clanTag;
        public string description;
        public string leaderId;
        public List<string> memberIds;
        public DateTime creationDate;
        public int level;
        public int trophies;
    }

    [System.Serializable]
    public class PlayerProfile
    {
        public string playerId;
        public string playerName;
        public int level;
        public int trophies;
        public string avatarUrl;
        public PlayerStatus status;
        public DateTime lastSeen;
        public PlayerStats stats;
    }

    [System.Serializable]
    public class PlayerStats
    {
        public int gamesPlayed;
        public float totalDistance;
        public int totalCoins;
        public int highScore;
        public int achievements;
    }

    [System.Serializable]
    public class SocialSettings
    {
        public bool allowFriendRequests = true;
        public bool allowClanInvites = true;
        public bool showOnlineStatus = true;
        public bool allowDirectMessages = true;
        public bool enableNotifications = true;
    }

    [System.Serializable]
    public class ClanCreationRequest
    {
        public string clanName;
        public string description;
        public string tag;
        public string creatorId;
        public string creatorName;
    }

    // Enums
    public enum PlayerStatus
    {
        Unknown,
        Offline,
        Online,
        Away,
        InGame,
        Busy
    }

    public enum RequestStatus
    {
        Pending,
        Accepted,
        Declined,
        Expired
    }

    public enum ChatChannel
    {
        All,
        Global,
        Friends,
        Clan,
        Direct
    }
}