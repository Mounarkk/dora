using UnityEngine;

namespace ChangeBlindness.Changes.InFoV
{
    /// <summary>
    /// InFoV experiment that moves an object by a specified offset when it's visible and a blink is detected.
    /// The movement is triggered when the object is within the camera's field of view and the subject is blinking,
    /// utilizing the brief moment of natural vision occlusion to perform the positional change.
    /// </summary>
    public class MoveOnBlinkInFoV : BaseExperiment
    {
        /// <summary>
        /// The game object that will be moved during the experiment.
        /// This serves as both the trigger for field of view detection and the target of the movement.
        /// </summary>
        [SerializeField] private GameObject objectToMove;
        
        /// <summary>
        /// The world space offset vector that will be applied to the object's position.
        /// Default value moves the object 1 unit to the right along the X-axis.
        /// Can be configured to move in any direction and distance.
        /// </summary>
        [SerializeField] private Vector3 moveOffset = new Vector3(1f, 0f, 0f);

        /// <summary>
        /// The original world position of the object before any movement is applied.
        /// Used for reverting the object back to its initial state.
        /// </summary>
        private Vector3 originalPosition;

        /// <summary>
        /// Unity Start callback. Stores the initial position of the object for later restoration.
        /// Called after Awake to ensure all components are properly initialized.
        /// </summary>
        private void Start()
        {
            originalPosition = objectToMove.transform.position;
        }

        /// <summary>
        /// Applies the movement offset to the object's position.
        /// Moves the object from its original position by the specified offset vector.
        /// Sets the IsChanged flag to true to track the object's state.
        /// </summary>
        public override void ApplyChange()
        {
            objectToMove.transform.position = originalPosition + moveOffset;
            IsChanged = true;
        }

        /// <summary>
        /// Reverts the object back to its original position.
        /// Restores the world position to the value stored before any changes were applied.
        /// Sets the IsChanged flag to false to indicate the object is in its original state.
        /// </summary>
        public override void RevertChange()
        {
            objectToMove.transform.position = originalPosition;
            IsChanged = false;
        }

        /// <summary>
        /// Determines if this experiment's object is visible in the field of view AND the subject is currently blinking.
        /// Combines field-of-view visibility checks with blink detection to ensure the movement
        /// happens during the optimal timing window when vision is naturally occluded.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if the object is visible and a blink is detected, false otherwise.</returns>
        /// <remarks>
        /// Note: Currently uses transform.position instead of objectToMove.transform.position for FoV check.
        /// This may need to be corrected to use objectToMove.transform.position for consistency.
        /// Requires a valid BlinkDetector component to function properly.
        /// </remarks>
        public override bool IsInFieldOfView(Camera camera)
        {
            Vector3 screenPoint = camera.WorldToViewportPoint(transform.position);
            bool isInFoV = screenPoint.z > 0 && 
                   screenPoint.x > 0 && screenPoint.x < 1 && 
                   screenPoint.y > 0 && screenPoint.y < 1;
            
            // Check if the blink count has changed
            return isInFoV && BlinkDetector.isBlinking;
        }
    }
}