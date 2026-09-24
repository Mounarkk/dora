using UnityEngine;
using UnityEngine.UI;

namespace HeadTracking
{
    /// <summary>
    /// Creates a position-based vignette effect that darkens the screen edges when the user's head 
    /// moves outside defined comfort zones. Uses a dual-axis threshold system with smooth transitions.
    /// </summary>
    public class HeadPositionVignette : MonoBehaviour
    {
        #region Inspector Fields
        /// <summary>
        /// Head tracking controller providing current head position data.
        /// </summary>
        [SerializeField] private HeadPositionController headTracker;
        
        /// <summary>
        /// UI Image component used as the vignette overlay. Should cover the entire screen.
        /// </summary>
        [SerializeField] private Image vignetteImage;

        /// <summary>
        /// Horizontal distance where vignette effect begins to appear.
        /// </summary>
        [SerializeField] private float xStartThreshold = 40f;
        
        /// <summary>
        /// Horizontal distance where vignette reaches maximum intensity.
        /// </summary>
        [SerializeField] private float xMaxThreshold = 50f;
        
        /// <summary>
        /// Vertical distance where vignette effect begins to appear.
        /// </summary>
        [SerializeField] private float yStartThreshold = 5f;
        
        /// <summary>
        /// Vertical distance where vignette reaches maximum intensity.
        /// </summary>
        [SerializeField] private float yMaxThreshold = 10f;
        
        /// <summary>
        /// Speed of vignette alpha transitions. Higher values = faster fade in/out.
        /// </summary>
        [SerializeField] private float transitionSpeed = 5f;
        
        /// <summary>
        /// Maximum vignette opacity (0 = transparent, 1 = fully opaque).
        /// </summary>
        [SerializeField] private float maxAlpha = 1f;
        #endregion
        
        #region Unity Lifecycle
        /// <summary>
        /// Initializes vignette image to full transparency.
        /// </summary>
        private void Start()
        {
            if (vignetteImage == null)
            {
                Debug.LogError("Vignette image not assigned!");
                return;
            }
            
            // Initialize the vignette image to be fully transparent
            Color initialColor = vignetteImage.color;
            initialColor.a = 0f;
            vignetteImage.color = initialColor;
        }

        /// <summary>
        /// Updates vignette opacity based on the current head position.
        /// Uses smooth interpolation for gradual transitions.
        /// </summary>
        private void Update()
        {
            if (headTracker == null || vignetteImage == null) return;

            Vector3 headPosition = headTracker.Position;
            float targetAlpha = CalculateVignetteAlpha(headPosition);
            
            // Smoothly interpolate the current alpha to the target alpha
            Color currentColor = vignetteImage.color;
            currentColor.a = Mathf.Lerp(currentColor.a, targetAlpha, Time.deltaTime * transitionSpeed);
            vignetteImage.color = currentColor;
        }
        #endregion

        #region Vignette Calculation
        /// <summary>
        /// Calculates vignette alpha based on head position using a dual-axis threshold system.
        /// Takes the maximum progress from X and Y axes to determine final intensity.
        /// Formula: alpha = max(xProgress, yProgress) * maxAlpha
        /// </summary>
        /// <param name="headPosition">Current head position in world coordinates</param>
        /// <returns>Target vignette alpha value (0-1)</returns>
        private float CalculateVignetteAlpha(Vector3 headPosition)
        {
            // Calculate the progress between start and max thresholds for each axis
            float xProgress = CalculateAxisProgress(
                Mathf.Abs(headPosition.x), 
                xStartThreshold, 
                xMaxThreshold
            );
            
            float yProgress = CalculateAxisProgress(
                Mathf.Abs(headPosition.y), 
                yStartThreshold, 
                yMaxThreshold
            );
            
            // Use the larger of the two progress values (dominant axis approach)
            float progress = Mathf.Max(xProgress, yProgress);
            
            // Calculate alpha based on progress (0 to maxAlpha)
            return Mathf.Clamp01(progress) * maxAlpha;
        }

        /// <summary>
        /// Calculates linear interpolation progress between start and max thresholds for a single axis.
        /// Returns 0 if within the start threshold, 1 if beyond the max threshold, or interpolated value between.
        /// Formula: progress = (value - start) / (max - start)
        /// </summary>
        /// <param name="value">Absolute position value for the axis</param>
        /// <param name="startThreshold">Distance where the effect begins</param>
        /// <param name="maxThreshold">Distance where effect reaches maximum</param>
        /// <returns>Progress value (0-1)</returns>
        private float CalculateAxisProgress(float value, float startThreshold, float maxThreshold)
        {
            if (value <= startThreshold) return 0f;
            if (value >= maxThreshold) return 1f;
            
            // Calculate progress between start and max thresholds
            return (value - startThreshold) / (maxThreshold - startThreshold);
        }
        #endregion
    }
}