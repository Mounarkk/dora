using UnityEngine;

namespace ChangeBlindness.Changes.OutFoV
{
    /// <summary>
    /// OutFoV experiment that adds a previously invisible object to the scene when it's outside the field of view.
    /// The object starts deactivated and becomes visible only when the change is applied while the object's position
    /// is not visible in the camera's viewing frustum, creating a surprise appearance when the user looks back.
    /// </summary>
    public class AdditionOutFoV : BaseExperiment
    {
        /// <summary>
        /// The game object that will be added (activated) during the experiment.
        /// This object serves as both the trigger for field of view detection and the target of the addition change.
        /// Starts inactive and becomes visible when the change is applied.
        /// </summary>
        [SerializeField] private GameObject targetObject;

        /// <summary>
        /// Unity Start callback. Ensures the target object starts in an inactive state.
        /// This establishes the baseline condition where the object is not visible in the scene.
        /// </summary>
        private void Start()
        {
            ChangeBlindnessManager manager = FindAnyObjectByType<ChangeBlindnessManager>();
            if (manager == null)
            {
                Debug.LogError("ChangeBlindnessManager not found in the scene. Please ensure it is present.");
                return;
            }
            for (int i = 0; i < manager.experiments.Count; i++)
            {
                if (typeof(AdditionOutFoV) == manager.experiments[i].GetType())
                {
                    targetObject.SetActive(false);
                    break;
                }
            }
        }
        
        /// <summary>
        /// Activates the target object, making it visible in the scene.
        /// This represents the "addition" change where a new object appears.
        /// Sets the IsChanged flag to true to track the experiment state.
        /// </summary>
        public override void ApplyChange()
        {
            targetObject.SetActive(true);
            IsChanged = true;
        }

        /// <summary>
        /// Deactivates the target object, removing it from the visible scene.
        /// Reverts back to the original state where the object was not present.
        /// Sets the IsChanged flag to false to indicate the original state is restored.
        /// </summary>
        public override void RevertChange()
        {
            targetObject.SetActive(false);
            IsChanged = false;
        }
        
        /// <summary>
        /// Determines if the target object's position is currently visible within the camera's field of view.
        /// For OutFoV experiments, the change should only be applied when this returns false,
        /// meaning the object's location is outside the visible area.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if the object's position is visible in the camera's field of view, false otherwise.</returns>
        /// <remarks>
        /// Note: This method checks visibility based on the object's transform position even when the object is inactive.
        /// The OutFoV experiment logic will trigger changes when this returns false.
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