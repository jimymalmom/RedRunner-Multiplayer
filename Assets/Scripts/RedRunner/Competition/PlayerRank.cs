using System;
using UnityEngine;

namespace RedRunner.Competition
{
    /// <summary>
    /// Player ranking and competition data structures
    /// </summary>
    [System.Serializable]
    public class PlayerRank
    {
        public string playerId;
        public string playerName;
        public string currentRank;
        public int trophies;
        public int seasonWins;
        public int seasonLosses;
        public DateTime lastRankUpdate;
        public string previousRank;
        public bool isPromoted;
        public bool isDemoted;
        public float winRate;
        
        public PlayerRank()
        {
            playerId = "";
            playerName = "";
            currentRank = "Bronze";
            trophies = 0;
            seasonWins = 0;
            seasonLosses = 0;
            lastRankUpdate = DateTime.UtcNow;
            previousRank = "";
            isPromoted = false;
            isDemoted = false;
            winRate = 0f;
        }
        
        public void UpdateWinRate()
        {
            int totalGames = seasonWins + seasonLosses;
            winRate = totalGames > 0 ? (float)seasonWins / totalGames : 0f;
        }
    }
}