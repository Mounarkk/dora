using UnityEngine;

namespace ChangeBlindness.Changes.OutFoV
{
    /// <summary>
    /// OutFoV experiment that moves an object to a new position when it's outside the field of view.
    /// The positional change occurs when the object is not visible, creating a surprising relocation
    /// that becomes apparent when the user looks back at the area where the object was originally located.
    /// </summary>
    public class MovementOutFoV : BaseExperiment
    {
        /// <summary>
        /// The new world position where the object will be moved during the experiment.
        /// This position should be configured to create a noticeable change when the user returns their attention.
        /// </summary>
        [SerializeField] private Vector3 changedPosition;
        
        /// <summary>
        /// The game object that will be moved during the experiment.
        /// This object serves as the trigger for field of view detection and determines when the movement should occur.
        /// </summary>
        [SerializeField] private GameObject targetObject;
        
        /// <summary>
        /// The original world position of the object before any movement is applied.
        /// Used for reverting the object back to its initial location.
        /// </summary>
        private Vector3 originalPosition;
        
        /// <summary>
        /// Unity Start callback. Stores the original position of the object for later restoration.
        /// Called after Awake to ensure all components are properly initialized.
        /// </summary>
        private void Start()
        {
            originalPosition = targetObject.transform.position;
        }
        
        /// <summary>
        /// Moves this component's transform to the specified changed position.
        /// Sets the IsChanged flag to true to track the experiment state.
        /// </summary>
        public override void ApplyChange()
        {
            targetObject.transform.position = changedPosition;
            IsChanged = true;
        }

        /// <summary>
        /// Reverts this component's transform back to the original position.
        /// Restores the position that was stored during the Start method.
        /// Sets the IsChanged flag to false to indicate the original state is restored.
        /// </summary>
        public override void RevertChange()
        {
            targetObject.transform.position = originalPosition;
            IsChanged = false;
        }
        
        /// <summary>
        /// Determines if the target object is currently visible within the camera's field of view.
        /// For OutFoV experiments, the change should only be applied when this returns false,
        /// meaning the object is outside the visible area and the movement won't be immediately noticed.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if the target object is visible in the camera's field of view, false otherwise.</returns>
        /// <remarks>
        /// The OutFoV experiment logic will trigger the movement when this returns false,
        /// ensuring the positional change happens while the object is not being observed.
        /// </remarks>
        public override bool IsInFieldOfView(Camera camera)
        {
            Vector3 screenPoint = camera.WorldToViewportPoint(targetObject.transform.position);
            bool isVisible = screenPoint.z > 0 && 
                             screenPoint.x > 0 && screenPoint.x < 1 && 
                             screenPoint.y > 0 && screenPoint.y < 1;
            
            return isVisible;
        }
    }
}