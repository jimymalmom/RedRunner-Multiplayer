using System.Collections.Generic;
using UnityEngine;
using System.IO;
using RedRunner.Characters;

namespace RedRunner.Networking
{
    /// <summary>
    /// Concrete implementation of network-synchronized game state
    /// Handles client-side prediction and server reconciliation
    /// </summary>
    public class NetworkGameState : IGameState
    {
        private Dictionary<uint, IPlayerState> players;
        private ITerrainState terrainState;
        private List<ICollectibleState> collectibles;
        
        public uint CurrentTick { get; set; }
        
        public NetworkGameState()
        {
            players = new Dictionary<uint, IPlayerState>();
            collectibles = new List<ICollectibleState>();
            terrainState = new TerrainState();
            CurrentTick = 0;
        }
        
        public IPlayerState GetPlayerState(uint playerId)
        {
            return players.TryGetValue(playerId, out var state) ? state : null;
        }
        
        public void SetPlayerState(uint playerId, IPlayerState playerState)
        {
            players[playerId] = playerState;
        }
        
        public void RemovePlayer(uint playerId)
        {
            players.Remove(playerId);
        }
        
        public Dictionary<uint, IPlayerState> GetAllPlayers()
        {
            return new Dictionary<uint, IPlayerState>(players);
        }
        
        public ITerrainState GetTerrainState()
        {
            return terrainState;
        }
        
        public void SetTerrainState(ITerrainState newTerrainState)
        {
            terrainState = newTerrainState;
        }
        
        public List<ICollectibleState> GetCollectibles()
        {
            return new List<ICollectibleState>(collectibles);
        }
        
        public void AddCollectible(ICollectibleState collectible)
        {
            collectibles.Add(collectible);
        }
        
        public void RemoveCollectible(uint collectibleId)
        {
            collectibles.RemoveAll(c => c.CollectibleId == collectibleId);
        }
        
        public GameStateSnapshot CreateSnapshot()
        {
            var snapshot = new GameStateSnapshot
            {
                tick = CurrentTick,
                terrainState = terrainState?.Serialize()
            };
            
            // Serialize all player states
            foreach (var kvp in players)
            {
                snapshot.playerStates[kvp.Key] = kvp.Value.Serialize();
            }
            
            // Serialize all collectible states
            foreach (var collectible in collectibles)
            {
                snapshot.collectibleStates.Add(collectible.Serialize());
            }
            
            return snapshot;
        }
        
        public void RestoreFromSnapshot(GameStateSnapshot snapshot)
        {
            CurrentTick = snapshot.tick;
            
            // Restore terrain state
            if (snapshot.terrainState != null)
            {
                terrainState.Deserialize(snapshot.terrainState);
            }
            
            // Restore player states
            players.Clear();
            foreach (var kvp in snapshot.playerStates)
            {
                var playerState = new PlayerState();
                playerState.Deserialize(kvp.Value);
                players[kvp.Key] = playerState;
            }
            
            // Restore collectible states
            collectibles.Clear();
            foreach (var collectibleData in snapshot.collectibleStates)
            {
                var collectible = new CollectibleState();
                collectible.Deserialize(collectibleData);
                collectibles.Add(collectible);
            }
        }
    }
    
    /// <summary>
    /// Concrete player state implementation
    /// </summary>
    [System.Serializable]
    public class PlayerState : IPlayerState
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
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(PlayerId);
                writer.Write(PlayerName ?? "");
                writer.Write(Position.x);
                writer.Write(Position.y);
                writer.Write(Position.z);
                writer.Write(Velocity.x);
                writer.Write(Velocity.y);
                writer.Write(Velocity.z);
                writer.Write(Score);
                writer.Write(Coins);
                writer.Write(IsDead);
                writer.Write(IsGrounded);
                writer.Write(LastJumpTick);
                writer.Write(JumpCount);
                return stream.ToArray();
            }
        }
        
        public void Deserialize(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                PlayerId = reader.ReadUInt32();
                PlayerName = reader.ReadString();
                Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Velocity = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Score = reader.ReadSingle();
                Coins = reader.ReadInt32();
                IsDead = reader.ReadBoolean();
                IsGrounded = reader.ReadBoolean();
                LastJumpTick = reader.ReadUInt32();
                JumpCount = reader.ReadInt32();
            }
        }
    }
    
    /// <summary>
    /// Concrete terrain state implementation
    /// </summary>
    [System.Serializable]
    public class TerrainState : ITerrainState
    {
        public float CurrentX { get; set; }
        public float FarthestX { get; set; }
        public List<BlockData> ActiveBlocks { get; set; }
        public int Seed { get; set; }
        
        public TerrainState()
        {
            ActiveBlocks = new List<BlockData>();
        }
        
        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(CurrentX);
                writer.Write(FarthestX);
                writer.Write(Seed);
                writer.Write(ActiveBlocks.Count);
                
                foreach (var block in ActiveBlocks)
                {
                    writer.Write(block.position.x);
                    writer.Write(block.position.y);
                    writer.Write(block.position.z);
                    writer.Write(block.blockType ?? "");
                    writer.Write(block.width);
                    writer.Write(block.spawnTick);
                }
                
                return stream.ToArray();
            }
        }
        
        public void Deserialize(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                CurrentX = reader.ReadSingle();
                FarthestX = reader.ReadSingle();
                Seed = reader.ReadInt32();
                
                int blockCount = reader.ReadInt32();
                ActiveBlocks.Clear();
                
                for (int i = 0; i < blockCount; i++)
                {
                    var block = new BlockData
                    {
                        position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        blockType = reader.ReadString(),
                        width = reader.ReadSingle(),
                        spawnTick = reader.ReadUInt32()
                    };
                    ActiveBlocks.Add(block);
                }
            }
        }
    }
    
    /// <summary>
    /// Concrete collectible state implementation
    /// </summary>
    [System.Serializable]
    public class CollectibleState : ICollectibleState
    {
        public uint CollectibleId { get; set; }
        public Vector3 Position { get; set; }
        public string Type { get; set; }
        public int Value { get; set; }
        public bool IsCollected { get; set; }
        public uint CollectedByPlayer { get; set; }
        public uint CollectedAtTick { get; set; }
        
        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(CollectibleId);
                writer.Write(Position.x);
                writer.Write(Position.y);
                writer.Write(Position.z);
                writer.Write(Type ?? "");
                writer.Write(Value);
                writer.Write(IsCollected);
                writer.Write(CollectedByPlayer);
                writer.Write(CollectedAtTick);
                return stream.ToArray();
            }
        }
        
        public void Deserialize(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                CollectibleId = reader.ReadUInt32();
                Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Type = reader.ReadString();
                Value = reader.ReadInt32();
                IsCollected = reader.ReadBoolean();
                CollectedByPlayer = reader.ReadUInt32();
                CollectedAtTick = reader.ReadUInt32();
            }
        }
    }
}