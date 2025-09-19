using UnityEngine;
using System.IO;

namespace RedRunner.Networking.Commands
{
    /// <summary>
    /// Command for player jump input
    /// Includes anti-cheat validation for ground state and cooldowns
    /// </summary>
    [System.Serializable]
    public class JumpCommand : IGameCommand
    {
        public uint CommandId { get; set; }
        public uint Tick { get; set; }
        public uint PlayerId { get; set; }
        
        [SerializeField] private Vector2 inputPosition;
        [SerializeField] private float jumpStrength;
        [SerializeField] private bool isGrounded; // Client's reported ground state
        
        public Vector2 InputPosition => inputPosition;
        public float JumpStrength => jumpStrength;
        public bool IsGrounded => isGrounded;
        
        public JumpCommand() { }
        
        public JumpCommand(uint playerId, Vector2 touchPos, float strength, bool grounded)
        {
            PlayerId = playerId;
            inputPosition = touchPos;
            jumpStrength = strength;
            isGrounded = grounded;
        }
        
        public void Execute(IGameState gameState)
        {
            var playerState = gameState.GetPlayerState(PlayerId);
            if (playerState == null || playerState.IsDead) return;
            
            var character = playerState.Character;
            if (character == null) return;
            
            // Server-side ground check (more authoritative than client report)
            bool serverGroundState = character.GroundCheck.IsGrounded;
            
            if (serverGroundState)
            {
                var velocity = character.Rigidbody2D.linearVelocity;
                velocity.y = jumpStrength;
                character.Rigidbody2D.linearVelocity = velocity;
                
                // Update animation
                character.Animator.ResetTrigger("Jump");
                character.Animator.SetTrigger("Jump");
                
                // Play effects
                if (character.JumpParticleSystem != null)
                    character.JumpParticleSystem.Play();
                
                // Play audio
                AudioManager.Singleton?.PlayJumpSound(character.Audio);
                
                // Update player state
                playerState.LastJumpTick = Tick;
                playerState.JumpCount++;
            }
        }
        
        public bool Validate(IGameState gameState)
        {
            var playerState = gameState.GetPlayerState(PlayerId);
            if (playerState == null) return false;
            
            // Validate player is not dead
            if (playerState.IsDead) return false;
            
            // Validate jump strength is within reasonable bounds
            if (jumpStrength < 5f || jumpStrength > 20f) return false;
            
            // Anti-cheat: Check jump cooldown (prevent spam jumping)
            const uint MIN_JUMP_COOLDOWN_TICKS = 10; // ~0.16 seconds at 60 TPS
            if (Tick - playerState.LastJumpTick < MIN_JUMP_COOLDOWN_TICKS) return false;
            
            var character = playerState.Character;
            if (character == null) return false;
            
            // Server-authoritative ground check
            bool serverGroundState = character.GroundCheck.IsGrounded;
            
            // If client claims to be grounded but server disagrees, reject
            if (isGrounded && !serverGroundState) return false;
            
            // Only allow jumping when actually grounded
            if (!serverGroundState) return false;
            
            return true;
        }
        
        public void Undo(IGameState gameState)
        {
            var playerState = gameState.GetPlayerState(PlayerId);
            if (playerState == null) return;
            
            // Revert jump count and last jump tick
            if (playerState.JumpCount > 0)
                playerState.JumpCount--;
        }
        
        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(CommandId);
                writer.Write(Tick);
                writer.Write(PlayerId);
                writer.Write(inputPosition.x);
                writer.Write(inputPosition.y);
                writer.Write(jumpStrength);
                writer.Write(isGrounded);
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
                inputPosition = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                jumpStrength = reader.ReadSingle();
                isGrounded = reader.ReadBoolean();
            }
        }
    }
}