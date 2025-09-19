using System.Collections.Generic;
using UnityEngine;
using RedRunner.Networking;
using RedRunner.Competition;

namespace RedRunner.UI
{
    /// <summary>
    /// UI components for multiplayer and enhanced features
    /// These are basic implementations that can be expanded upon
    /// </summary>
    
    public class MultiplayerHUD : MonoBehaviour
    {
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private UnityEngine.UI.Text playerCountText;
        [SerializeField] private Transform playerListParent;
        
        private bool isVisible = false;
        
        public void SetVisible(bool visible)
        {
            isVisible = visible;
            if (hudRoot != null)
                hudRoot.SetActive(visible);
        }
        
        public void UpdateGameState(IGameState gameState)
        {
            if (!isVisible || gameState == null) return;
            
            var players = gameState.GetAllPlayers();
            if (playerCountText != null)
            {
                playerCountText.text = $"Players: {players.Count}";
            }
        }
    }
    
    public class PlayerListPanel : MonoBehaviour
    {
        [SerializeField] private Transform listParent;
        [SerializeField] private GameObject playerEntryPrefab;
        
        private List<GameObject> playerEntries = new List<GameObject>();
        
        public void UpdatePlayerList(List<PlayerInfo> players)
        {
            // Clear existing entries
            foreach (var entry in playerEntries)
            {
                if (entry != null)
                    Destroy(entry);
            }
            playerEntries.Clear();
            
            // Create new entries
            if (players != null && listParent != null && playerEntryPrefab != null)
            {
                foreach (var player in players)
                {
                    var entry = Instantiate(playerEntryPrefab, listParent);
                    // Configure entry with player data
                    var text = entry.GetComponent<UnityEngine.UI.Text>();
                    if (text != null)
                    {
                        text.text = $"{player.playerName} - Score: {player.score}";
                    }
                    playerEntries.Add(entry);
                }
            }
        }
    }
    
    public class ChatPanel : MonoBehaviour
    {
        [SerializeField] private Transform messageParent;
        [SerializeField] private GameObject messagePrefab;
        [SerializeField] private UnityEngine.UI.InputField messageInput;
        [SerializeField] private UnityEngine.UI.Button sendButton;
        
        private List<GameObject> messages = new List<GameObject>();
        
        void Start()
        {
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(SendMessage);
            }
        }
        
        public void AddMessage(string playerId, string message)
        {
            if (messageParent != null && messagePrefab != null)
            {
                var messageObj = Instantiate(messagePrefab, messageParent);
                var text = messageObj.GetComponent<UnityEngine.UI.Text>();
                if (text != null)
                {
                    text.text = $"{playerId}: {message}";
                }
                messages.Add(messageObj);
                
                // Limit message history
                if (messages.Count > 50)
                {
                    if (messages[0] != null)
                        Destroy(messages[0]);
                    messages.RemoveAt(0);
                }
            }
        }
        
        private void SendMessage()
        {
            if (messageInput != null && !string.IsNullOrEmpty(messageInput.text))
            {
                // Send message through social manager
                RedRunner.Social.SocialManager.Instance?.SendChatMessage(messageInput.text);
                messageInput.text = "";
            }
        }
    }
    
    public class MatchmakingPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private UnityEngine.UI.Button findMatchButton;
        [SerializeField] private UnityEngine.UI.Button cancelButton;
        [SerializeField] private UnityEngine.UI.Text statusText;
        
        private bool isSearching = false;
        
        void Start()
        {
            if (findMatchButton != null)
                findMatchButton.onClick.AddListener(FindMatch);
                
            if (cancelButton != null)
                cancelButton.onClick.AddListener(CancelSearch);
        }
        
        public void SetVisible(bool visible)
        {
            if (panelRoot != null)
                panelRoot.SetActive(visible);
        }
        
        private void FindMatch()
        {
            isSearching = true;
            if (statusText != null)
                statusText.text = "Searching for match...";
                
            if (findMatchButton != null)
                findMatchButton.interactable = false;
                
            // TODO: Implement actual matchmaking logic
        }
        
        private void CancelSearch()
        {
            isSearching = false;
            if (statusText != null)
                statusText.text = "Ready to find match";
                
            if (findMatchButton != null)
                findMatchButton.interactable = true;
        }
    }
    
    public class NotificationPanel : MonoBehaviour
    {
        [SerializeField] private Transform notificationParent;
        
        // This is a basic stub - notifications are handled in EnhancedUIManager
        public void ShowNotification(string title, string message)
        {
            Debug.Log($"Notification: {title} - {message}");
        }
    }
    
    public class LoadingOverlay : MonoBehaviour
    {
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private UnityEngine.UI.Text loadingText;
        [SerializeField] private UnityEngine.UI.Slider progressSlider;
        [SerializeField] private UnityEngine.UI.Image loadingSpinner;
        
        public void Show(string message = "Loading...", bool showProgress = false)
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(true);
                
            if (loadingText != null)
                loadingText.text = message;
                
            if (progressSlider != null)
                progressSlider.gameObject.SetActive(showProgress);
        }
        
        public void Hide()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }
        
        public void UpdateProgress(float progress)
        {
            if (progressSlider != null)
            {
                progressSlider.value = Mathf.Clamp01(progress);
            }
        }
    }
    
    public class ScreenTransition : MonoBehaviour
    {
        [SerializeField] private CanvasGroup transitionCanvas;
        [SerializeField] private float transitionDuration = 0.5f;
        
        public System.Collections.IEnumerator TransitionOut()
        {
            if (transitionCanvas != null)
            {
                transitionCanvas.gameObject.SetActive(true);
                float elapsed = 0f;
                
                while (elapsed < transitionDuration)
                {
                    elapsed += Time.deltaTime;
                    transitionCanvas.alpha = Mathf.Lerp(0f, 1f, elapsed / transitionDuration);
                    yield return null;
                }
                
                transitionCanvas.alpha = 1f;
            }
        }
        
        public System.Collections.IEnumerator TransitionIn()
        {
            if (transitionCanvas != null)
            {
                float elapsed = 0f;
                
                while (elapsed < transitionDuration)
                {
                    elapsed += Time.deltaTime;
                    transitionCanvas.alpha = Mathf.Lerp(1f, 0f, elapsed / transitionDuration);
                    yield return null;
                }
                
                transitionCanvas.alpha = 0f;
                transitionCanvas.gameObject.SetActive(false);
            }
        }
    }
    

}