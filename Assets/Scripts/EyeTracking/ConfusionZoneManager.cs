using UnityEngine;

namespace EyeTracking
{
    /// <summary>
    /// Manages confusion zones for eye tracking with accuracy and precision visualization.
    /// Handles smooth center updates based on gaze position and calibrated values.
    /// </summary>
    public class ConfusionZoneManager
    {
        #region Public Properties

        public Vector2 Center { get; private set; }
        public float CurrentAccuracyRadius { get; private set; }
        public float CurrentPrecisionRadius { get; private set; }

        #endregion

        #region Private Fields

        private bool _isInitialized;
        public bool _useCalibratedValue; // Should be private but keeping as-is
        private float _calibratedStdDev;
        private float _calibratedMeanDistance;
        private GameObject _visualizationAccuracy;
        private GameObject _visualizationPrecision;
        private Vector2 _targetCenter;
        private float _smoothSpeed = 20f; // Speed multiplier for center interpolation
        private RectTransform _crosshair;
        
        // Z-position scaling fields
        private float _calibratedMeanZPosition = 0f; // Mean Z position during calibration
        private bool _hasZCalibrationData = false;   // Whether Z calibration data is available

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes confusion zone manager with visualization prefabs.
        /// </summary>
        /// <param name="accuracyPrefab">Prefab for accuracy zone visualization</param>
        /// <param name="precisionPrefab">Prefab for precision zone visualization</param>
        /// <param name="parent">Parent transform for visualizations</param>
        /// <param name="crosshair">Crosshair UI element to position</param>
        public ConfusionZoneManager(
            GameObject accuracyPrefab,
            GameObject precisionPrefab,
            Transform parent, 
            RectTransform crosshair)
        {
            _crosshair = crosshair;
            _useCalibratedValue = false;
            InitializeVisualizations(accuracyPrefab, precisionPrefab, parent);
            Initialize();
        }

        #endregion

        #region Configuration Methods

        /// <summary>
        /// Sets calibrated values to override default radius calculations.
        /// </summary>
        /// <param name="stdDev">Standard deviation for precision radius</param>
        /// <param name="meanDistance">Mean distance for accuracy radius</param>
        /// <param name="calibratedMeanZPosition">Mean Z position during calibration (optional)</param>
        public void SetCalibratedValues(float stdDev, float meanDistance, float calibratedMeanZPosition = 0f)
        {
            _calibratedStdDev = stdDev;
            _calibratedMeanDistance = meanDistance;
            _useCalibratedValue = true;
            
            if (calibratedMeanZPosition > 0f)
            {
                _calibratedMeanZPosition = calibratedMeanZPosition;
                _hasZCalibrationData = true;
                Debug.Log($"ConfusionZone: Set calibrated Z position: {calibratedMeanZPosition:F2}mm");
            }
        }

        /// <summary>
        /// Initializes center position to screen center.
        /// </summary>
        private void Initialize()
        {
            Center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            _targetCenter = Center;
        }

        /// <summary>
        /// Creates visualization GameObjects from prefabs.
        /// </summary>
        /// <param name="accuracyPrefab">Accuracy zone prefab</param>
        /// <param name="precisionPrefab">Precision zone prefab</param>
        /// <param name="parent">Parent transform for visualizations</param>
        private void InitializeVisualizations(GameObject accuracyPrefab, GameObject precisionPrefab, Transform parent)
        {
            if (accuracyPrefab != null && parent != null)
            {
                _visualizationAccuracy = Object.Instantiate(accuracyPrefab, parent);
                _visualizationAccuracy.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            }

            if (precisionPrefab != null && parent != null)
            {
                _visualizationPrecision = Object.Instantiate(precisionPrefab, parent);
                _visualizationPrecision.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            }
        }

        #endregion

        #region Runtime Update Methods

        /// <summary>
        /// Updates confusion zone center based on gaze position and radius thresholds.
        /// Uses three-zone behavior: immediate snap, smooth interpolation, or no movement.
        /// </summary>
        /// <param name="filteredGazePosition">Pre-filtered gaze position</param>
        /// <param name="accuracyRadius">Outer radius threshold</param>
        /// <param name="precisionRadius">Inner radius threshold</param>
        /// <param name="currentZPosition">Current head tracker Z position (optional for scaling)</param>
        public void Update(Vector2 filteredGazePosition, float accuracyRadius, float precisionRadius, float currentZPosition = 0f)
        {
            // Calculate Z-position scaling factor
            float zScalingFactor = CalculateZScalingFactor(currentZPosition);
            
            // Use calibrated values if available, scaled by Z position
            CurrentAccuracyRadius = _useCalibratedValue ? _calibratedMeanDistance * zScalingFactor : accuracyRadius;
            CurrentPrecisionRadius = _useCalibratedValue ? _calibratedStdDev * zScalingFactor : precisionRadius;

            // Initialize center on first update
            if (!_isInitialized)
            {
                Center = filteredGazePosition;
                _targetCenter = Center;
                _isInitialized = true;
                return;
            }

            float distanceToCenter = Vector2.Distance(filteredGazePosition, Center);

            // Zone 1: Outside accuracy radius - snap center immediately
            if (distanceToCenter > CurrentAccuracyRadius)
            {
                Vector2 direction = (filteredGazePosition - Center).normalized;
                _targetCenter = filteredGazePosition - direction * CurrentAccuracyRadius;
                Center = _targetCenter;
            }
            // Zone 2: Between precision and accuracy radius - smooth interpolation
            else if (distanceToCenter > CurrentPrecisionRadius)
            {
                Vector2 direction = (filteredGazePosition - Center).normalized;
                _targetCenter = filteredGazePosition - direction * CurrentPrecisionRadius;

                // Calculate interpolation speed based on distance from center
                float normalizedDistance = distanceToCenter / CurrentAccuracyRadius;
                float t = Mathf.Clamp01(normalizedDistance * normalizedDistance);
                t *= Time.deltaTime * _smoothSpeed;
                Center = Vector2.Lerp(Center, _targetCenter, t);
            }
            // Zone 3: Inside precision radius - no movement (implicit)

            UpdateVisualizations(_crosshair);
        }

        #endregion

        #region Z-Position Scaling

        /// <summary>
        /// Calculates scaling factor based on Z position ratio from calibration
        /// </summary>
        /// <param name="currentZPosition">Current head tracker Z position</param>
        /// <returns>Scaling factor for confusion zone radii</returns>
        private float CalculateZScalingFactor(float currentZPosition)
        {
            // If we don't have Z calibration data or current Z position, return 1.0 (no scaling)
            if (!_hasZCalibrationData || currentZPosition <= 0f || _calibratedMeanZPosition <= 0f)
            {
                return 1.0f;
            }
            
            // Calculate ratio: if user is closer, zones should be smaller; if farther, zones should be larger
            float zRatio = currentZPosition / _calibratedMeanZPosition;
            
            // Clamp the scaling factor to reasonable bounds (0.5x to 2.0x)
            float scalingFactor = Mathf.Clamp(zRatio, 0.5f, 2.0f);
            
            return scalingFactor;
        }

        #endregion

        #region Visualization Methods

        /// <summary>
        /// Updates crosshair position and visualization sizes.
        /// </summary>
        /// <param name="crosshair">Crosshair RectTransform to update</param>
        public void UpdateVisualizations(RectTransform crosshair)
        {
            // Convert screen space center to UI anchor position
            crosshair.anchoredPosition = Center - new Vector2(Screen.width / 2f, Screen.height / 2f);
            UpdateVisualization(_visualizationAccuracy, CurrentAccuracyRadius, crosshair);
            UpdateVisualization(_visualizationPrecision, CurrentPrecisionRadius, crosshair);
        }

        /// <summary>
        /// Updates individual visualization position and size.
        /// </summary>
        /// <param name="visualization">Visualization GameObject</param>
        /// <param name="radius">Radius for the visualization</param>
        /// <param name="crosshair">Reference crosshair RectTransform</param>
        private void UpdateVisualization(GameObject visualization, float radius, RectTransform crosshair)
        {
            if (visualization == null) return;

            RectTransform rt = visualization.GetComponent<RectTransform>();
            if (crosshair != null)
            {
                rt.anchoredPosition = crosshair.anchoredPosition;
            }
            rt.sizeDelta = new Vector2(radius * 2, radius * 2);
        }

        #endregion
    }
}