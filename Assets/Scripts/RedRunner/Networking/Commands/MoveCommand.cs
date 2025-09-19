using UnityEngine;
using System.IO;
using RedRunner.Characters;

namespace RedRunner.Networking.Commands
{
    /// <summary>
    /// Command for player movement input
    /// Handles horizontal movement with validation and prediction support
    /// </summary>
    [System.Serializable]
    public class MoveCommand : IGameCommand
    {
        public uint CommandId { get; set; }
        public uint Tick { get; set; }
        public uint PlayerId { get; set; }
        
        [SerializeField] private float horizontalInput;
        [SerializeField] private Vector2 inputPosition; // For touch input validation
        [SerializeField] private float deltaTime;
        
        public float HorizontalInput => horizontalInput;
        public Vector2 InputPosition => inputPosition;
        public float DeltaTime => deltaTime;
        
        public MoveCommand() { }
        
        public MoveCommand(uint playerId, float horizontal, Vector2 touchPos, float dt)
        {
            PlayerId = playerId;
            horizontalInput = Mathf.Clamp(horizontal, -1f, 1f);
            inputPosition = touchPos;
            deltaTime = dt;
        }
        
        public void Execute(IGameState gameState)
        {
            var playerState = gameState.GetPlayerState(PlayerId);
            if (playerState == null || playerState.IsDead) return;
            
            // Apply movement with proper speed calculations
            var character = playerState.Character;
            if (character != null)
            {
                float speed = 10f; // Default run speed
                var networkCharacter = character as NetworkRedCharacter;
                if (networkCharacter != null)
                    speed = networkCharacter.GetCurrentRunSpeed();
                var velocity = character.Rigidbody2D.linearVelocity;
                velocity.x = speed * horizontalInput;
                character.Rigidbody2D.linearVelocity = velocity;
                
                // Handle character facing direction
                if (Mathf.Abs(horizontalInput) > 0.1f)
                {
                    var scale = character.transform.localScale;
                    scale.x = Mathf.Sign(horizontalInput);
                    character.transform.localScale = scale;
                }
                
                // Update animation parameters
                character.Animator.SetFloat("Speed", Mathf.Abs(velocity.x));
                character.Animator.SetFloat("VelocityX", Mathf.Abs(velocity.x));
            }
        }
        
        public bool Validate(IGameState gameState)
        {
            var playerState = gameState.GetPlayerState(PlayerId);
            if (playerState == null) return false;
            
            // Validate input range
            if (Mathf.Abs(horizontalInput) > 1.1f) return false;
            
            // Validate player is not dead
            if (playerState.IsDead) return false;
            
            // Validate reasonable delta time (prevent speed hacking)
            if (deltaTime < 0.008f || deltaTime > 0.1f) return false;
            
            return true;
        }
        
        public void Undo(IGameState gameState)
        {
            // For movement, we typically don't undo but rather apply correction
            // This would be used in more complex scenarios like ability usage
        }
        
        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(CommandId);
                writer.Write(Tick);
                writer.Write(PlayerId);
                writer.Write(horizontalInput);
                writer.Write(inputPosition.x);
                writer.Write(inputPosition.y);
                writer.Write(deltaTime);
                return stream.ToArray();
            }
        }
        
        public void Deserialize(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                CommandId = reader.ReadUInt32();
                Tick = reader.ReadUInt32();
                PlayerId = reader.ReadUInt32();
                horizontalInput = reader.ReadSingle();
                inputPosition = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                deltaTime = reader.ReadSingle();
            }
        }
    }
}