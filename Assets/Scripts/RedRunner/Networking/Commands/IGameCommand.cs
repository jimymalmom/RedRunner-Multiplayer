using UnityEngine;

namespace RedRunner.Networking.Commands
{
    /// <summary>
    /// Interface for all game commands that can be executed, validated, and undone
    /// Essential for client-side prediction and server reconciliation
    /// </summary>
    public interface IGameCommand
    {
        /// <summary>
        /// Unique identifier for this command
        /// </summary>
        uint CommandId { get; set; }
        
        /// <summary>
        /// Network tick when this command was created
        /// </summary>
        uint Tick { get; set; }
        
        /// <summary>
        /// Player who issued this command
        /// </summary>
        uint PlayerId { get; set; }
        
        /// <summary>
        /// Execute the command on the game state
        /// </summary>
        /// <param name="gameState">Current game state to modify</param>
        void Execute(IGameState gameState);
        
        /// <summary>
        /// Validate if this command can be executed
        /// </summary>
        /// <param name="gameState">Current game state to check against</param>
        /// <returns>True if command is valid</returns>
        bool Validate(IGameState gameState);
        
        /// <summary>
        /// Undo the effects of this command (for client-side prediction rollback)
        /// </summary>
        /// <param name="gameState">Game state to rollback</param>
        void Undo(IGameState gameState);
        
        /// <summary>
        /// Serialize command for network transmission
        /// </summary>
        /// <returns>Serialized command data</returns>
        byte[] Serialize();
        
        /// <summary>
        /// Deserialize command from network data
        /// </summary>
        /// <param name="data">Serialized command data</param>
        void Deserialize(byte[] data);
    }
}