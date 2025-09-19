using UnityEngine;

namespace RedRunner.Utilities
{
    /// <summary>
    /// Extension of GroundCheck to support network synchronization
    /// Allows forcing grounded state for remote players
    /// </summary>
    public static class GroundCheckExtensions
    {
        /// <summary>
        /// Force the grounded state for network synchronization
        /// Used for remote players to match server state
        /// </summary>
        /// <param name="groundCheck">The GroundCheck component to modify</param>
        /// <param name="isGrounded">The forced grounded state</param>
        public static void ForceGroundedState(this GroundCheck groundCheck, bool isGrounded)
        {
            if (groundCheck != null)
            {
                // Use reflection to access private field if necessary
                var field = typeof(GroundCheck).GetField("m_IsGrounded", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                
                if (field != null)
                {
                    field.SetValue(groundCheck, isGrounded);
                }
                else
                {
                    // Fallback: If we can't access private field, 
                    // we might need to modify the original GroundCheck class
                    Debug.LogWarning("Could not force grounded state. Consider modifying GroundCheck class.");
                }
            }
        }
    }
    
    /// <summary>
    /// Network-aware version of GroundCheck with additional features
    /// </summary>
    public class NetworkGroundCheck : GroundCheck
    {
        [Header("Network Settings")]
        [SerializeField] private bool allowForceState = true;
        
        private bool forcedGroundedState;
        private bool usesForcedState;
        
        /// <summary>
        /// Override the grounded state for network synchronization
        /// </summary>
        /// <param name="isGrounded">Forced grounded state</param>
        public void SetForcedGroundedState(bool isGrounded)
        {
            if (allowForceState)
            {
                forcedGroundedState = isGrounded;
                usesForcedState = true;
            }
        }
        
        /// <summary>
        /// Clear forced state and return to physics-based detection
        /// </summary>
        public void ClearForcedState()
        {
            usesForcedState = false;
        }
        
        /// <summary>
        /// Override IsGrounded to use forced state when needed
        /// </summary>
        public new bool IsGrounded
        {
            get
            {
                if (usesForcedState && allowForceState)
                {
                    return forcedGroundedState;
                }
                return base.IsGrounded;
            }
        }
        
        /// <summary>
        /// Get the physics-based grounded state (ignoring forced state)
        /// </summary>
        public bool PhysicsGroundedState => base.IsGrounded;
        
        /// <summary>
        /// Check if currently using forced state
        /// </summary>
        public bool IsUsingForcedState => usesForcedState && allowForceState;
    }
}