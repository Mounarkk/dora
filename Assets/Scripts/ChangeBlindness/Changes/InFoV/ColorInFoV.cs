using UnityEngine;

namespace ChangeBlindness.Changes.InFoV
{
    /// <summary>
    /// InFoV experiment that changes the color of an object when the subject's gaze is within the confusion circle.
    /// The change is triggered when the object to observe is both visible in the camera's field of view 
    /// and within the gaze detection accuracy radius.
    /// </summary>
    public class ColorInFoV : BaseExperiment
    {
        /// <summary>
        /// The object that needs to be observed (gazed at) to trigger the color change.
        /// Used for gaze detection and field of view calculations.
        /// </summary>
        [SerializeField] private GameObject objectToObserve;
        
        /// <summary>
        /// Cached renderer component of the object to observe for efficient color manipulation.
        /// </summary>
        private Renderer targetObjectToChangeRenderer;
        
        /// <summary>
        /// The target object whose color will be changed during the experiment.
        /// Currently unused - the objectToObserve serves as both target and trigger.
        /// </summary>
        [SerializeField] private GameObject targetObjectToChange;
        
        /// <summary>
        /// The original color of the object before any changes are applied.
        /// Used for reverting the object back to its initial state.
        /// </summary>
        private Color originalColor;

        /// <summary>
        /// Unity Start callback. Initializes the original color and caches the renderer component.
        /// Should be called after Awake to ensure all components are properly initialized.
        /// </summary>
        private void Start()
        {
            targetObjectToChangeRenderer = objectToObserve.GetComponent<Renderer>();
            originalColor = targetObjectToChangeRenderer.material.color;
        }

        /// <summary>
        /// Applies a random color change to the observed object.
        /// Generates a new random RGB color and applies it to the object's material.
        /// Sets the IsChanged flag to true to track the object's state.
        /// </summary>
        public override void ApplyChange()
        {
            // Create a random color and applies it to the object when the user's gaze is on the object to observe
            Color newColor = new Color(Random.value, Random.value, Random.value);
            targetObjectToChangeRenderer.material.color = newColor;
            IsChanged = true;

        }

        /// <summary>
        /// Reverts the object back to its original color.
        /// Restores the material color to the value stored before any changes were applied.
        /// Sets the IsChanged flag to false, to indicate the object is in its original state.
        /// </summary>
        public override void RevertChange()
        {
            targetObjectToChangeRenderer.material.color = originalColor;
            IsChanged = false;
        }

        /// <summary>
        /// Determines if the object to observe is in the field of view and within the gaze confusion circle.
        /// Combines camera visibility checks with gaze-based detection to determine if conditions are met
        /// for triggering an InFoV change.
        /// </summary>
        /// <param name="camera">The camera used for field of view and viewport calculations.</param>
        /// <returns>True if the object is visible and being gazed at, false otherwise.</returns>
        public override bool IsInFieldOfView(Camera camera)
        {
            Vector3 screenPoint = camera.WorldToViewportPoint(objectToObserve.transform.position);
            bool isGazeOnTarget = IsObjectToObserveInConfusionCircle(objectToObserve, camera);
            bool isVisible = screenPoint.z > 0 && 
                             screenPoint.x > 0 && screenPoint.x < 1 && 
                             screenPoint.y > 0 && screenPoint.y < 1;
            
            return isGazeOnTarget && isVisible;
        }
        
        /// <summary>
        /// Checks if a game object is within the confusion circle
        /// </summary>
        public new bool IsObjectToObserveInConfusionCircle(GameObject obj, Camera camera)
        {
            // Get the object's screen position
            Vector3 objScreenPos = camera.WorldToScreenPoint(obj.transform.position);
            // Check if it's within the confusion circle
            float distance = Vector2.Distance(gazeDetector.FilteredGazePosition, objScreenPos);
            return distance <= gazeDetector.CurrentAccuracyRadius;
        }
    }
}