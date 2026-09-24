using UnityEngine;

namespace ChangeBlindness.Changes.InFoV
{
    /// <summary>
    /// InFoV experiment that creates continuous subtle movement of an object when it's visible in the field of view.
    /// The object moves horizontally based on its screen position relative to the center, creating a gradual positional change
    /// that becomes noticeable over time while the subject is looking at the scene.
    /// </summary>
    public class ContinuousMoveInFoV : BaseExperiment
    {
        /// <summary>
        /// Cached reference to the main camera for viewport calculations and field of view checks.
        /// </summary>
        private Camera mainCamera;
        
        /// <summary>
        /// The game object that will undergo continuous movement during the experiment.
        /// This serves as both the trigger and target for the InFoV change.
        /// </summary>
        [SerializeField] private GameObject targetObject;
        
        /// <summary>
        /// Movement speed in world units per second.
        /// Kept very small to create subtle movement that gradually becomes noticeable.
        /// </summary>
        [SerializeField] private float moveSpeed = 0.01f;
        
        /// <summary>
        /// Maximum distance the object can move from its starting position in world units.
        /// Prevents the object from moving too far from its original location.
        /// </summary>
        [SerializeField] private float maxMoveDistance = 1f;
        
        /// <summary>
        /// The original world position of the target object before any movement begins.
        /// Used for reverting the object back to its initial state.
        /// </summary>
        private Vector3 initialPosition;
        
        /// <summary>
        /// Flag indicating whether the object is currently moving.
        /// Controls the movement behavior in the Update loop.
        /// </summary>
        private bool isMoving;
        
        /// <summary>
        /// Accumulated total distance the object has moved from its initial position.
        /// Used to enforce the maximum movement distance constraint.
        /// </summary>
        private float totalMovement;

        /// <summary>
        /// Unity Start callback. Initializes the camera reference and stores the target object's initial position.
        /// Sets up the experiment's initial state with movement disabled.
        /// </summary>
        private void Start()
        {
            mainCamera = Camera.main;
            initialPosition = targetObject.transform.position;
            isMoving = false;
        }

        /// <summary>
        /// Starts the continuous movement of the target object.
        /// Enables the movement flag and marks the object as changed.
        /// The actual movement is handled in the Update method.
        /// </summary>
        public override void ApplyChange()
        {
            isMoving = true;
            IsChanged = true;
        }

        /// <summary>
        /// Stops the movement and resets the target object to its original position.
        /// Clears all movement tracking variables and restores the initial state.
        /// </summary>
        public override void RevertChange()
        {
            isMoving = false;
            IsChanged = false;
            targetObject.transform.position = initialPosition;
            totalMovement = 0f;
        }

        /// <summary>
        /// Unity Update callback. Handles the continuous movement logic when the object is active and visible.
        /// Moves the object horizontally based on its screen position relative to the center,
        /// respecting the maximum movement distance constraint.
        /// </summary>
        private void Update()
        {
            if (!isMoving || !IsInFieldOfView(mainCamera)) return;

            // Get the screen center X position
            float screenCenterX = Screen.width / 2f;
            
            // Get the current screen position of the object
            Vector3 screenPos = mainCamera.WorldToScreenPoint(targetObject.transform.position);
            
            // Determine the movement direction based on the object's position relative to screen center
            float direction = screenPos.x < screenCenterX ? 1f : -1f;
            
            // Calculate movement amount for this frame
            float moveAmount = moveSpeed * Time.deltaTime;
            totalMovement += Mathf.Abs(moveAmount);
            
            // Only move if we haven't exceeded the maximum movement distance
            if (totalMovement <= maxMoveDistance)
            {
                // Move the object along world X axis
                Vector3 newPosition = targetObject.transform.position;
                newPosition.x += direction * moveAmount;
                targetObject.transform.position = newPosition;
            }
        }
        
        /// <summary>
        /// Determines if the target object is currently visible within the camera's field of view.
        /// Uses viewport coordinates to check if the object is within the camera's viewing frustum.
        /// Does not include gaze detection since this experiment relies on general visibility rather than focused attention.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if the object is visible in the camera's field of view, false otherwise.</returns>
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