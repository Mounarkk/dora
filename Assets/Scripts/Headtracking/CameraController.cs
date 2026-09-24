
using UnityEngine;

namespace HeadTracking
{
    /// <summary>
    /// Controls the main camera to simulate a "window" into the 3D scene based on head-tracking data.
    /// Dynamically adjusts the camera's position, orientation, and field of view so that the viewport
    /// matches the user's physical screen perspective, creating the illusion that the screen is a transparent
    /// window looking into a virtual world.
    /// 
    /// The "window" effect is achieved through mathematical viewport computations that:
    /// 1. Define a fixed virtual viewport in world space at a specific distance from the camera
    /// 2. Calculate the optimal viewing angle and direction to encompass this viewport from the current head position
    /// 3. Adjust the camera's field of view to match the angular size of the viewport as seen from the head position
    /// 
    /// Two computational approaches are provided:
    /// - StaticViewport: Uses pre-computed world points for consistent viewport dimensions
    /// - ScreenViewport: Dynamically calculates viewport based on current screen dimensions
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        #region Private Fields
        /// <summary>
        /// Cached reference to the main camera component for efficient access.
        /// </summary>
        private Camera cam;
        
        /// <summary>
        /// Cached reference to this component's transform for position and rotation updates.
        /// </summary>
        private Transform camTransform;

        /// <summary>
        /// Reference to the head-tracking system that provides smoothed head position data.
        /// The head position determines the camera's viewpoint in the virtual window.
        /// </summary>
        [SerializeField] private HeadPositionController headPosition;
        
        
        /// <summary>
        /// Right edge point of the virtual viewport in world coordinates.
        /// Computed during initialization using ViewportToWorldPoint at viewport coordinate (1.0, 0.5, depth).
        /// Represents the rightmost visible boundary of the virtual window at mid-height.
        /// </summary>
        private Vector3 wPlus;
        
        /// <summary>
        /// Left edge point of the virtual viewport in world coordinates.
        /// Computed during initialization using ViewportToWorldPoint at viewport coordinate (0.0, 0.5, depth).
        /// Represents the leftmost visible boundary of the virtual window at mid-height.
        /// </summary>
        private Vector3 wMinus;
        
        /// <summary>
        /// Flag indicating whether the first valid Z position has been acquired.
        /// This is used to ensure the camera's depth is correctly set after the first head-tracking update.
        /// </summary>
        private bool firstValidZAcquired = false;

        #endregion

        #region Unity Lifecycle
        /// <summary>
        /// Unity Start callback. Initializes camera references and computes the static viewport boundary points.
        /// The viewport points (wPlus, wMinus) define the left and right edges of the virtual window
        /// at a fixed depth, which serve as reference points for the window effect calculations.
        /// </summary>
        private void Start()
        {
            cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("Camera not found!");
            }
            else
            {
                camTransform = transform;

                // Points defining the viewport size at a certain distance in front of the camera
                // Used to create a window effect by establishing fixed world-space viewport boundaries
                if (cam != null)
                {
                    // Calculate world positions of viewport edges at 40 units depth
                    // These points define the virtual "window frame" that remains constant in world space
                    wPlus = cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, 40f));  // Right edge at mid-height
                    wMinus = cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, 40f)); // Left edge at mid-height
                }
            }
        }

        /// <summary>
        /// Unity Update callback. Applies head-tracked position to the camera and recalculates
        /// the viewing parameters to maintain the window effect illusion.
        /// </summary>
        private void Update()
        {
            if (headPosition == null) return;

            // Position update: Use the smoothed head position directly as camera position
            Vector3 scaled = headPosition.Position;
            if (!firstValidZAcquired && headPosition.IsTrackingActive)
            {
                wPlus.z = Mathf.Abs(scaled.z); // Ensure the camera stays at the same depth
                wMinus.z = Mathf.Abs(scaled.z); // Ensure the camera stays at the same depth
                firstValidZAcquired = true;
            }
            camTransform.position = scaled;

            // Recalculate viewing parameters based on the new camera position
            // Choose one of the computation methods below:
            var (viewDirection, vFov) = ComputeStaticViewportParams(camTransform.position);
            // var (viewDirection, vFov) = ComputeScreenViewportParams(camTransform.position);

            // Apply computed view direction and field of view to maintain the window effect
            camTransform.rotation = Quaternion.LookRotation(viewDirection, Vector3.up);
            cam.fieldOfView = vFov;
        }
        #endregion

        #region Window Effect Computation Methods
        /// <summary>
        /// Computes viewing direction and field of view for a static virtual viewport approach.
        /// This method maintains a fixed-size virtual window in world space, calculating the optimal
        /// camera parameters to view this window from the current head position.
        /// 
        /// Mathematical approach:
        /// 1. Calculate normalized direction vectors from camera to viewport edges (wPlus, wMinus)
        /// 2. Compute the bisector of these directions as the optimal viewing direction
        /// 3. Calculate the angular separation between view direction and edge directions
        /// 4. Convert angular field of view from horizontal to vertical for Unity camera
        /// 
        /// The window effect emerges because as the head moves, the camera adjusts its viewing angle
        /// to keep the same world-space viewport visible, simulating looking through a fixed window frame.
        /// </summary>
        /// <param name="cameraPos">Current world position of the camera (head position).</param>
        /// <returns>Tuple containing the normalized view direction vector and horizontal field of view in degrees.</returns>
        private (Vector3 viewDir, float verticalFov) ComputeStaticViewportParams(Vector3 cameraPos)
        {
            // Calculate normalized direction vectors from camera position to viewport edges
            Vector3 toRight = (wPlus - cameraPos).normalized;   // Direction to the right viewport edge
            Vector3 toLeft  = (wMinus - cameraPos).normalized;  // Direction to the left viewport edge

            // Compute the optimal viewing direction as the bisector of edge directions
            // This ensures the viewport appears centered in the camera's view
            Vector3 viewDirNorm = (toLeft + toRight).normalized;
            
            // Calculate the half-horizontal field of view using dot product
            // The angle between view direction and edge direction gives us half the FOV
            float halfHFovRad = Mathf.Acos(Vector3.Dot(viewDirNorm, toRight)); 
            
            // Convert to full horizontal FOV in degrees
            // Note: Using 1f multiplier instead of 2f - this may need correction for proper FOV calculation
            float horizontalFovDeg = halfHFovRad * 2f * Mathf.Rad2Deg; 
            
            // Convert horizontal FOV to vertical FOV using the camera's aspect ratio
            // Unity cameras use vertical FOV, so this conversion is necessary
            float verticalFov = Camera.HorizontalToVerticalFieldOfView(horizontalFovDeg, cam.aspect);

            return (viewDirNorm, verticalFov);
        }
        

        /// <summary>
        /// Computes viewing direction and field of view using dynamic screen viewport approach.
        /// This method calculates viewport boundaries based on current screen dimensions,
        /// making it adaptable to different screen sizes and resolutions.
        /// 
        /// Mathematical approach:
        /// 1. Convert screen pixel coordinates to world coordinates using ScreenToWorldPoint
        /// 2. Use screen edges at mid-height as viewport boundaries
        /// 3. Apply the same angular calculation as StaticViewportParams
        /// 4. Calculate proper full horizontal FOV (using 2f multiplier for  the complete angle)
        /// 
        /// This approach is more flexible for different display configurations but requires
        /// the camera to have valid screen-to-world conversion at the time of calculation.
        /// </summary>
        /// <param name="cameraPos">Current world position of the camera (head position).</param>
        /// <returns>Tuple containing the normalized view direction vector and vertical field of view in degrees.</returns>
        private (Vector3 viewDir, float verticalFov) ComputeScreenViewportParams(Vector3 cameraPos)
        {
            // Calculate world-space points at mid-height on left and right screen edges
            float midY = (Screen.height - 1) / 2f;  // Middle of screen vertically
            
            // Convert screen pixel coordinates to world positions at fixed depth
            Vector3 worldRight = cam.ScreenToWorldPoint(new Vector3(Screen.width - 1, midY, 10f));  // Right screen edge
            Vector3 worldLeft  = cam.ScreenToWorldPoint(new Vector3(0, midY, 10f));                 // Left screen edge

            // Calculate normalized direction vectors from camera to screen edges
            Vector3 toRight = (worldRight - cameraPos).normalized;
            Vector3 toLeft  = (worldLeft  - cameraPos).normalized;

            // Compute the bisector direction for optimal viewport centering
            Vector3 viewDirNorm = (toLeft + toRight).normalized;
            
            // Calculate the half horizontal FOV and convert to full FOV
            float halfHFovRad = Mathf.Acos(Vector3.Dot(viewDirNorm, toRight));
            float horizontalFovDeg = halfHFovRad * 2f * Mathf.Rad2Deg;  // Proper full FOV calculation
            
            // Convert to vertical FOV for Unity camera
            float verticalFov = Camera.HorizontalToVerticalFieldOfView(horizontalFovDeg, Camera.main.aspect);
            
            return (viewDirNorm, verticalFov);
        }
        #endregion
    }
}