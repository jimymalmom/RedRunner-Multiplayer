using System.Collections.Generic;
using UnityEngine;
using RedRunner.Characters;

namespace RedRunner.Networking
{
    /// <summary>
    /// Interface for game state management
    /// Provides abstraction for both client and server state handling
    /// </summary>
    public interface IGameState
    {
        /// <summary>
        /// Current network tick
        /// </summary>
        uint CurrentTick { get; set; }
        
        /// <summary>
        /// Get player state by ID
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <returns>Player state or null if not found</returns>
        IPlayerState GetPlayerState(uint playerId);
        
        /// <summary>
        /// Add or update player state
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <param name="playerState">Player state to set</param>
        void SetPlayerState(uint playerId, IPlayerState playerState);
        
        /// <summary>
        /// Remove player from game state
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        void RemovePlayer(uint playerId);
        
        /// <summary>
        /// Get all active players
        /// </summary>
        /// <returns>Dictionary of all player states</returns>
        Dictionary<uint, IPlayerState> GetAllPlayers();
        
        /// <summary>
        /// Get current terrain state
        /// </summary>
        /// <returns>Terrain state information</returns>
        ITerrainState GetTerrainState();
        
        /// <summary>
        /// Update terrain state
        /// </summary>
        /// <param name="terrainState">New terrain state</param>
        void SetTerrainState(ITerrainState terrainState);
        
        /// <summary>
        /// Get all active collectibles
        /// </summary>
        /// <returns>List of collectible states</returns>
        List<ICollectibleState> GetCollectibles();
        
        /// <summary>
        /// Add collectible to game state
        /// </summary>
        /// <param name="collectible">Collectible state to add</param>
        void AddCollectible(ICollectibleState collectible);
        
        /// <summary>
        /// Remove collectible from game state
        /// </summary>
        /// <param name="collectibleId">Collectible identifier</param>
        void RemoveCollectible(uint collectibleId);
        
        /// <summary>
        /// Create a snapshot of current state for rollback
        /// </summary>
        /// <returns>Serializable state snapshot</returns>
        GameStateSnapshot CreateSnapshot();
        
        /// <summary>
        /// Restore state from snapshot
        /// </summary>
        /// <param name="snapshot">State snapshot to restore</param>
        void RestoreFromSnapshot(GameStateSnapshot snapshot);
    }
    
    /// <summary>
    /// Player state interface for network synchronization
    /// </summary>
    public interface IPlayerState
    {
        uint PlayerId { get; set; }
        string PlayerName { get; set; }
        Vector3 Position { get; set; }
        Vector3 Velocity { get; set; }
        float Score { get; set; }
        int Coins { get; set; }
        bool IsDead { get; set; }
        bool IsGrounded { get; set; }
        uint LastJumpTick { get; set; }
        int JumpCount { get; set; }
        Character Character { get; set; }
        
        /// <summary>
        /// Serialize player state for network transmission
        /// </summary>
        byte[] Serialize();
        
        /// <summary>
        /// Deserialize player state from network data
        /// </summary>
        void Deserialize(byte[] data);
    }
    
    /// <summary>
    /// Terrain state interface for synchronized world generation
    /// </summary>
    public interface ITerrainState
    {
        float CurrentX { get; set; }
        float FarthestX { get; set; }
        List<BlockData> ActiveBlocks { get; set; }
        int Seed { get; set; }
        
        byte[] Serialize();
        void Deserialize(byte[] data);
    }
    
    /// <summary>
    /// Collectible state interface for synchronized pickups
    /// </summary>
    public interface ICollectibleState
    {
        uint CollectibleId { get; set; }
        Vector3 Position { get; set; }
        string Type { get; set; }
        int Value { get; set; }
        bool IsCollected { get; set; }
        uint CollectedByPlayer { get; set; }
        uint CollectedAtTick { get; set; }
        
        byte[] Serialize();
        void Deserialize(byte[] data);
    }
    
    /// <summary>
    /// Block data for terrain synchronization
    /// </summary>
    [System.Serializable]
    public struct BlockData
    {
        public Vector3 position;
        public string blockType;
        public float width;
        public uint spawnTick;
    }
    
    /// <summary>
    /// Complete game state snapshot for rollback
    /// </summary>
    [System.Serializable]
    public class GameStateSnapshot
    {
        public uint tick;
        public Dictionary<uint, byte[]> playerStates;
        public byte[] terrainState;
        public List<byte[]> collectibleStates;
        
        public GameStateSnapshot()
        {
            playerStates = new Dictionary<uint, byte[]>();
            collectibleStates = new List<byte[]>();
        }
    }
}