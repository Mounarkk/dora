using UnityEngine;

namespace ChangeBlindness.Changes.InFoV
{
    /// <summary>
    /// InFoV experiment that exchanges the positions of two objects when both are visible and a blink is detected.
    /// The exchange is triggered only when both objects are within the camera's field of view and the subject is blinking,
    /// taking advantage of the brief moment when vision is occluded to perform the positional swap.
    /// </summary>
    public class ExchangeInFoVOnBlink : BaseExperiment
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
        /// Used for reverting the exchange back to the original state.
        /// </summary>
        private Vector3 originalObjectPosition;
        
        /// <summary>
        /// The initial world position of the replacement object before any changes are applied.
        /// Used for reverting the exchange back to the original state.
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
        /// Sets the IsChanged flag to true to track the object's state.
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
        /// Sets the IsChanged flag to false to indicate the objects are in their original state.
        /// </summary>
        public override void RevertChange()
        {
            originalObject.transform.position = originalObjectPosition;
            replacementObject.transform.position = replacementObjectPosition;
            
            IsChanged = false;
        }

        /// <summary>
        /// Determines if both objects are visible in the field of view AND the subject is currently blinking.
        /// Combines field-of-view visibility checks for both objects with blink detection to ensure
        /// the exchange happens during the optimal timing window when vision is naturally occluded.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if both objects are visible and a blink is detected, false otherwise.</returns>
        /// <remarks>
        /// Requires a valid BlinkDetector component to function properly.
        /// Both objects must be simultaneously visible for the exchange to be triggered.
        /// </remarks>
        public override bool IsInFieldOfView(Camera camera)
        {
            // Check if both objects are in field of view
            Vector3 screenPointOriginal = camera.WorldToViewportPoint(originalObject.transform.position);
            bool isOriginalInFoV = screenPointOriginal.z > 0 && 
                   screenPointOriginal.x > 0 && screenPointOriginal.x < 1 && 
                   screenPointOriginal.y > 0 && screenPointOriginal.y < 1;
                   
            Vector3 screenPointReplacement = camera.WorldToViewportPoint(replacementObject.transform.position);
            bool isReplacementInFoV = screenPointReplacement.z > 0 && 
                                   screenPointReplacement.x > 0 && screenPointReplacement.x < 1 && 
                                   screenPointReplacement.y > 0 && screenPointReplacement.y < 1;
            
            // Only return true if both objects are in FoV AND user is blinking
            return (isOriginalInFoV && isReplacementInFoV) && BlinkDetector.isBlinking;
        }
    }
}