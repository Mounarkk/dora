
using UnityEngine;

namespace ChangeBlindness.Changes.OutFoV
{
    /// <summary>
    /// OutFoV experiment that removes a visible object from the scene when it's outside the field of view.
    /// The object starts active and becomes invisible only when the change is applied while the object
    /// is not visible in the camera's viewing frustum, creating a surprising disappearance when the user looks back.
    /// </summary>
    public class SubstractionOutFoV : BaseExperiment
    {
        /// <summary>
        /// The game object that will be removed (deactivated) during the experiment.
        /// This object serves as both the trigger for field of view detection and the target of the subtraction change.
        /// Starts active and becomes invisible when the change is applied.
        /// </summary>
        [SerializeField] private GameObject targetObject;

        /// <summary>
        /// Unity Start callback. Ensures the target object starts in an active state.
        /// This establishes the baseline condition where the object is visible in the scene.
        /// </summary>
        private void Start()
        {
            targetObject.SetActive(true);
        }

        /// <summary>
        /// Deactivates the target object, making it invisible in the scene.
        /// This represents the "subtraction" change where an existing object disappears.
        /// Sets the IsChanged flag to true to track the experiment state.
        /// </summary>
        public override void ApplyChange()
        {
            targetObject.SetActive(false);
            IsChanged = true;
        }

        /// <summary>
        /// Reactivates the target object, making it visible in the scene again.
        /// Reverts back to the original state where the object was present and active.
        /// Sets the IsChanged flag to false to indicate the original state is restored.
        /// </summary>
        public override void RevertChange()
        {
            targetObject.SetActive(true);
            IsChanged = false;
        }
        
        /// <summary>
        /// Determines if the target object is currently visible within the camera's field of view.
        /// For OutFoV experiments, the change should only be applied when this returns false,
        /// meaning the object is outside the visible area and its disappearance won't be immediately noticed.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if the object is visible in the camera's field of view, false otherwise.</returns>
        /// <remarks>
        /// The OutFoV experiment logic will trigger the subtraction when this returns false,
        /// ensuring the object removal happens while the object is not being observed.
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