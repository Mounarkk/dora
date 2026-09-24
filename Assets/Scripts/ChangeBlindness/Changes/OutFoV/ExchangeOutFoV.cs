using UnityEngine;

namespace ChangeBlindness.Changes.OutFoV
{
    /// <summary>
    /// OutFoV experiment that exchanges the positions of two objects when both are outside the field of view.
    /// The positional swap occurs when neither object is visible, creating a surprising rearrangement
    /// that becomes apparent when the user looks back at the area where the objects were located.
    /// </summary>
    public class ExchangeOutFoV : BaseExperiment
    {
        /// <summary>
        /// The first object involved in the position exchange.
        /// Will be moved to the replacement object's position when the change is applied.
        /// </summary>
        [SerializeField] private GameObject originalObject;
        
        /// <summary>
        /// The second object involved in the position exchange.
        /// Will be moved to the original object's position when the change is applied.
        /// </summary>
        [SerializeField] private GameObject replacementObject;
        
        /// <summary>
        /// The initial world position of the original object before any changes are applied.
        /// Used for reverting the exchange back to the original arrangement.
        /// </summary>
        private Vector3 originalObjectPosition;
        
        /// <summary>
        /// The initial world position of the replacement object before any changes are applied.
        /// Used for reverting the exchange back to the original arrangement.
        /// </summary>
        private Vector3 replacementObjectPosition;

        /// <summary>
        /// Unity Start callback. Stores the initial positions of both objects for later restoration.
        /// Called after Awake to ensure all components are properly initialized.
        /// </summary>
        private void Start()
        {
            originalObjectPosition = originalObject.transform.position;
            replacementObjectPosition = replacementObject.transform.position;
        }

        /// <summary>
        /// Performs the position exchange between the two objects.
        /// Swaps the world positions of the original and replacement objects instantly.
        /// Sets the IsChanged flag to true to track the experiment state.
        /// </summary>
        public override void ApplyChange()
        {
            originalObject.transform.position = replacementObjectPosition;
            replacementObject.transform.position = originalObjectPosition;
            
            IsChanged = true;
        }

        /// <summary>
        /// Reverts both objects back to their original positions.
        /// Restores the initial world positions stored during Start().
        /// Sets the IsChanged flag to false to indicate the objects are in their original arrangement.
        /// </summary>
        public override void RevertChange()
        {
            originalObject.transform.position = originalObjectPosition;
            replacementObject.transform.position = replacementObjectPosition;
            
            IsChanged = false;
        }

        /// <summary>
        /// Determines if both objects are currently visible within the camera's field of view.
        /// For OutFoV experiments, the change should only be applied when this returns false,
        /// meaning both objects are outside the visible area and the exchange won't be immediately noticed.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if both objects are visible in the camera's field of view, false otherwise.</returns>
        /// <remarks>
        /// Note: Current implementation returns true when both objects are visible, but OutFoV logic
        /// typically triggers when objects are NOT in field of view. The experiment manager should
        /// apply changes when this method returns false, ensuring the exchange happens while unobserved.
        /// </remarks>
        public override bool IsInFieldOfView(Camera camera)
        {
            // Check visibility of both objects
            Vector3 screenPointOriginal = camera.WorldToViewportPoint(originalObject.transform.position);
            bool isOriginalInFoV = screenPointOriginal.z > 0 && 
                   screenPointOriginal.x > 0 && screenPointOriginal.x < 1 && 
                   screenPointOriginal.y > 0 && screenPointOriginal.y < 1;
            
            Vector3 screenPointReplacement = camera.WorldToViewportPoint(replacementObject.transform.position);
            bool isReplacementInFoV = screenPointReplacement.z > 0 && 
                                   screenPointReplacement.x > 0 && screenPointReplacement.x < 1 && 
                                   screenPointReplacement.y > 0 && screenPointReplacement.y < 1;
            
            return isOriginalInFoV && isReplacementInFoV;
        }
    }
}