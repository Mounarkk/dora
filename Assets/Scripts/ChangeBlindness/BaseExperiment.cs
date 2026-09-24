using EyeTracking;
using UnityEngine;

namespace ChangeBlindness
{
    /// <summary>
    /// Defines the types of changes that can occur in change blindness experiments.
    /// Changes can happen either within the subject's field of view (InFoV) or outside of it (OutFoV).
    /// </summary>
    public enum ChangeType
    {
        /// <summary>
        /// Change occurs within the subject's field of view.
        /// </summary>
        InFoV,
        
        /// <summary>
        /// Change occurs outside the subject's field of view.
        /// </summary>
        OutFoV
    }
    
    /// <summary>
    /// Abstract base class for objects that can undergo changes in change blindness experiments.
    /// Provides core functionality for managing object states, and camera visibility.
    /// Integrates with eye tracking systems to determine gaze-based interactions and field of view calculations.
    /// </summary>
    public abstract class BaseExperiment : MonoBehaviour, IExperiment
    {
        /// <summary>
        /// The type of change this object can perform.
        /// Determines whether the change occurs within or outside the field of view.
        /// </summary>
        [SerializeField] protected ChangeType changeType;
        
        /// <summary>
        /// Gets the type of change this object performs.
        /// </summary>
        /// <value>The ChangeType enum value defining the change behavior.</value>
        public ChangeType ChangeType => changeType;
        
        /// <summary>
        /// Gets a value indicating whether this change occurs within the field of view.
        /// Determined by checking if the change type is InFoV.
        /// </summary>
        /// <value>True if the change occurs within the field of view, false otherwise.</value>
        public bool IsInFoVChange => changeType == ChangeType.InFoV;
        
        /// <summary>
        /// Flag indicating whether the object has been changed from its original state.
        /// </summary>
        protected bool IsChanged;
        
        /// <summary>
        /// Reference to the gaze manager for eye tracking functionality.
        /// Used for determining gaze position and accuracy calculations.
        /// </summary>
        protected GazeManager gazeDetector;
        
        /// <summary>
        /// Reference to the blink detector for eye blink detection.
        /// Used for timing changes during blink events.
        /// </summary>
        protected BlinkDetector BlinkDetector;

        /// <summary>
        /// Unity Awake callback. Initializes the experiment object by finding required components.
        /// Automatically locates GazeManager and BlinkDetector instances in the scene.
        /// Logs warnings if required components are not found.
        /// </summary>
        protected virtual void Awake()
        {
            // Automatically find the gaze and blink detectors in the scene
            gazeDetector = FindFirstObjectByType<GazeManager>();
            BlinkDetector = FindFirstObjectByType<BlinkDetector>();
            
            if (gazeDetector == null)
            {
                Debug.LogWarning($"No GazeManager found in scene for {gameObject.name}. Gaze-based functionality will not work.");
            }
            
            if (BlinkDetector == null)
            {
                Debug.LogWarning($"No BlinkDetector found in scene for {gameObject.name}. Blink-based functionality will not work.");
            }
        }

        /// <summary>
        /// Applies the specific change defined by the implementing class.
        /// This method must be implemented by derived classes to define their unique change behavior.
        /// Called when the experiment needs to execute its change during the appropriate timing window.
        /// </summary>
        public abstract void ApplyChange();
        
        /// <summary>
        /// Reverts the object back to its original state before any changes were applied.
        /// This method must be implemented by derived classes to properly restore their initial state.
        /// Should completely undo any modifications made by ApplyChange().
        /// </summary>
        public abstract void RevertChange();

        /// <summary>
        /// Determines if this object is currently visible within the specified camera's field of view.
        /// Must be implemented by derived classes to specify which object's position should be checked.
        /// Used for determining appropriate timing for InFoV vs OutFoV changes.
        /// </summary>
        /// <param name="camera">The camera to test visibility against.</param>
        /// <returns>True if the object is visible in the camera's field of view, false otherwise.</returns>
        public abstract bool IsInFieldOfView(Camera camera);

        /// <summary>
        /// Checks if the specified game object is within the confusion circle based on current gaze data.
        /// The confusion circle represents the area of gaze uncertainty based on eye tracking accuracy.
        /// </summary>
        /// <param name="obj">The game object to check for confusion circle inclusion.</param>
        /// <param name="camera">The camera used for world-to-viewport coordinate conversion.</param>
        /// <returns>True if the object is within the confusion circle, false otherwise.</returns>
        /// <remarks>
        /// Requires a valid GazeDetector with current gaze position and accuracy radius data.
        /// Returns false if GazeDetector is null or gaze data is invalid.
        /// </remarks>
        protected bool IsObjectToObserveInConfusionCircle(GameObject obj, Camera camera)
        {
            if (gazeDetector == null) return false;
            
            // Get the object's screen position
            Vector3 objScreenPos = camera.WorldToViewportPoint(obj.transform.position);
        
            // Check if it's within the confusion circle
            float distance = Vector2.Distance(gazeDetector.FilteredGazePosition, objScreenPos);
            return distance <= gazeDetector.CurrentAccuracyRadius;
        }
    }
}