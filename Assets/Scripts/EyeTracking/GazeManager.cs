using System.Collections.Generic;
using PupilLabs;
using UnityEngine;
using System.IO;
using System;
using System.Linq;
using HeadTracking;

namespace EyeTracking
{
    /// <summary>
    /// Central controller for gaze tracking system that coordinates:
    /// - Real-time eye tracking data acquisition and processing
    /// - Head position synchronization
    /// - 9-point calibration procedures
    /// - Data visualization and logging
    /// - Surface stability validation
    /// - Gaze filtering and confidence zone management
    /// 
    /// Acts as the primary integration point between Pupil Labs hardware,
    /// Unity visualization systems, and custom gaze analysis components.
    /// </summary>
    public class GazeManager : MonoBehaviour
    {
        #region Inspector Configuration
        [Header("Controllers & Dependencies")]
        public SubscriptionsController subscriptionsController;  // Pupil Labs subscription handler
        private GazeDataLogger dataLogger;                      // CSV data recorder
        public ConfusionZoneManager confusionZone;              // Accuracy/precision zone handler
        private CalibrationManager calibration;                 // 9-point calibration system
        #endregion

        #region Prefabs and GameObjects
        [Header("UI, Prefabs and GameObjects")]
        [SerializeField] private Shader gazeReplacementShader;  // Compiled shader for gaze visualization
        public RectTransform crosshair;                         // Primary gaze cursor (filtered)
        private GazeVisualizer visualizer;                      // Visualization controller
        private ProcessingHelper helper;                         // Data processing utilities
        private RequestController requestCtrl;                   // Pupil Labs request handler
        public RectTransform rawGazeCrosshair;                  // Unfiltered gaze indicator
        public Canvas analysisCanvas;                           // Parent canvas for visualizations
        [SerializeField] private UDPReceiver receiver;          // Head tracker data receiver
        public GameObject CalibrationCover;                     // UI overlay during calibration
        public GameObject confusionZonePrefabPrecision;         // Precision zone visualization
        public GameObject confusionZonePrefabAccuracy;          // Accuracy zone visualization
        public GameObject meanGazePointPrefab;                  // Calibration result visualization
        public GameObject gazeIndicator;                        // 3D gaze pointer
        [SerializeField] private GameObject offsetVectorPrefab; // Calibration offset visualizer
        #endregion

        #region Configuration Settings
        [Header("Blink Detection")]
        [SerializeField] 
        [Tooltip("Minimum confidence threshold for valid eye data (0-1)")]
        private float blinkConfidenceThreshold = 0.9f;

        [Header("Calibration")]
        [SerializeField] 
        [Tooltip("Maximum recording duration per calibration point (seconds)")]
        public float maxRecordingDuration = 2f;
        
        [SerializeField] 
        [Tooltip("9-point calibration targets in screen space")]
        private Transform[] calibrationTargetPoints = new Transform[9];
        
        [SerializeField] 
        [Tooltip("Time before gaze prediction decays during signal loss")]
        private float signalLossTimeout = 2f;

        [Header("Movement Filtering")]
        [SerializeField] 
        [Tooltip("Weight for velocity-based prediction (0=no prediction, 1=full prediction)")]
        private float predictionFactor = 0.5f;
        
        [SerializeField] 
        [Tooltip("Exponential smoothing coefficient (higher = smoother)")]
        private float smoothingFactor = 20f;
        
        [HideInInspector] 
        public float calibratedStdDev;           // Post-calibration standard deviation (pixels)
        
        [HideInInspector] 
        public float calibratedMeanDistance;     // Post-calibration mean error (pixels)
        #endregion

        #region Current Gaze and Surface Data
        [Header("Current Gaze and Surface State")]
        public bool IsSurfaceStable { get; private set; } = true;  // Surface tracking reliability flag
        
        private float gazeAccuracy;              // Current angular accuracy estimate (degrees)
        private float gazePrecision;             // Current angular precision estimate (degrees)

        // Simplified gaze state tracking
        [HideInInspector] 
        public Vector2 rawGazeData = Vector2.zero;      // Current raw gaze (screen pixels)
        
        private Vector2 filteredGazePosition;     // Processed stable gaze position
        private Vector2 gazeVelocity = Vector2.zero;    // Current gaze velocity (pixels/sec)
        private float timeSinceLastValidData = 0f;      // Duration since last valid sample
        private bool hasHadValidData = false;           // Initial data acquisition flag
        #endregion

        #region Session State
        private bool sessionStarted;              // Calibration session active flag
        private bool isRecording;                 // Calibration recording in progress
        private bool HeadTrackerChecked = false;   // Head tracker warning suppression
        #endregion

        #region Blink State
        private bool inBlinkRecovery;             // Post-blink stabilization period
        private Vector2 lastValidGazeBeforeBlink = Vector2.zero;  // Pre-blink reference
        private bool wasBlinking;                 // Previous frame blink state
        private int recoveryFrameCount;           // Frames since blink ended
        #endregion

        #region Surface Data
        private int numberOfTags;                 // Detected surface markers
        private int numberOfNeededTags;           // Required markers for tracking
        private float eye0Confidence;             // Left eye confidence (0-1)
        private float eye1Confidence;             // Right eye confidence (0-1)
        private Dictionary<int, float> markerConfidences = new Dictionary<int, float>();  // Marker ID → Confidence
        private List<float> markerAngles = new List<float>();  // Marker quadrilateral interior angles
        #endregion

        #region Public Properties for External Access
        /// <summary>
        /// Current filtered gaze position in screen pixels (always valid)
        /// </summary>
        public Vector2 FilteredGazePosition => filteredGazePosition;

        /// <summary>
        /// Current accuracy zone radius in pixels
        /// </summary>
        public float CurrentAccuracyRadius => confusionZone?.CurrentAccuracyRadius ?? 0f;

        /// <summary>
        /// Current precision zone radius in pixels
        /// </summary>
        public float CurrentPrecisionRadius => confusionZone?.CurrentPrecisionRadius ?? 0f;
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes visualization components and processing utilities
        /// </summary>
        private void InitializeComponents()
        {
            requestCtrl = subscriptionsController.requestCtrl;
            helper = new ProcessingHelper();

            // Configure visualization system
            visualizer = new GazeVisualizer(
                crosshair,
                rawGazeCrosshair,
                gazeIndicator,
                analysisCanvas,
                confusionZonePrefabAccuracy,
                confusionZonePrefabPrecision,
                meanGazePointPrefab,
                offsetVectorPrefab
            );

            // Find analysis canvas if not assigned
            if (analysisCanvas == null)
                analysisCanvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();

            // Configure gaze visualization shader
            if (gazeReplacementShader != null)
            {
                Camera.main.SetReplacementShader(gazeReplacementShader, "RenderType");
            }
            else
            {
                Debug.LogError("GazeReplacementShader is not assigned!");
            }
            
            CalibrationCover.SetActive(false);
        }

        /// <summary>
        /// Initializes core system managers and default states
        /// </summary>
        private void InitializeManagers()
        {
            dataLogger = new GazeDataLogger();
            confusionZone = new ConfusionZoneManager(
                confusionZonePrefabAccuracy,
                confusionZonePrefabPrecision,
                analysisCanvas.transform,
                crosshair
            );

            calibration = new CalibrationManager(
                calibrationTargetPoints,
                dataLogger,
                maxRecordingDuration
            );

            // Initialize filtered position to screen center
            filteredGazePosition = new Vector2(Screen.width / 2f, Screen.height / 2f);
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            InitializeComponents();
            InitializeManagers();
            HideAllTargets();
        }

        private void OnEnable()
        {
            // Subscribe to Pupil Labs connection events
            requestCtrl.OnConnected += StartGazeSubscription;
            if (requestCtrl.IsConnected) StartGazeSubscription();
        }

        private void OnDisable()
        {
            // Unsubscribe from connection events
            requestCtrl.OnConnected -= StartGazeSubscription;
            if (requestCtrl.IsConnected) StopGazeSubscription();

            // Reset shader replacement
            if (Camera.main != null)
            {
                Camera.main.ResetReplacementShader();
            }
        }

        /// <summary>
        /// Main update loop handling:
        /// 1. Crosshair visualization
        /// 2. Surface stability checks
        /// 3. Calibration session management
        /// 4. Debug input handling
        /// </summary>
        private void Update()
        {
            visualizer.UpdateCrosshairs(rawGazeData);
            CheckEyeTrackingStability();
            HandleCalibrationSession();
            HandleInput();
        }
        #endregion

        #region Surface Stability
        /// <summary>
        /// Evaluates surface tracking quality based on:
        /// - Marker detection count
        /// - Confidence thresholds
        /// - Geometric validity
        /// - Eye tracking reliability
        /// </summary>
        private void CheckEyeTrackingStability()
        {
            IsSurfaceStable = helper.CheckEyeTrackingStability(
                numberOfTags,
                numberOfNeededTags,
                markerConfidences,
                markerAngles,
                eye0Confidence,
                eye1Confidence
            );
        }
        #endregion

        #region Gaze Subscription Management
        /// <summary>
        /// Subscribes to Pupil Labs data streams:
        /// - "surfaces": Marker and gaze-on-surface data
        /// - "pupil": Eye confidence metrics
        /// </summary>
        private void StartGazeSubscription()
        {
            subscriptionsController.SubscribeTo("surfaces", CustomReceiveData);
            subscriptionsController.SubscribeTo("pupil", CustomReceivePupilData);
        }

        /// <summary>
        /// Unsubscribes from all data streams
        /// </summary>
        private void StopGazeSubscription()
        {
            subscriptionsController.UnsubscribeFrom("surfaces", CustomReceiveData);
            subscriptionsController.UnsubscribeFrom("pupil", CustomReceivePupilData);
        }
        #endregion

        #region Data Processing
        /// <summary>
        /// Processes pupil confidence data for:
        /// - Per-eye confidence tracking
        /// - Blink detection
        /// </summary>
        private void CustomReceivePupilData(string topic, Dictionary<string, object> dictionary, byte[] thirdFrame = null)
        {
            helper.ProcessPupilConfidence(dictionary, ref eye0Confidence, ref eye1Confidence);
        }

        /// <summary>
        /// Main data processing pipeline for surface tracking:
        /// 1. Extracts surface metrics
        /// 2. Processes marker data
        /// 3. Manages blink states
        /// 4. Processes gaze data
        /// </summary>
        private void CustomReceiveData(string topic, Dictionary<string, object> dictionary, byte[] thirdFrame = null)
        {
            // Extract surface tracking metrics
            helper.ExtractSurfaceData(dictionary, out numberOfTags, out numberOfNeededTags,
                out float newAccuracy, out float newPrecision);
            
            // Update metrics if valid
            if (newAccuracy != 0f) gazeAccuracy = newAccuracy;
            if (newPrecision != 0f) gazePrecision = newPrecision;

            // Process marker confidence data
            helper.ProcessMarkerData(dictionary, ref markerConfidences);
            ProcessMarkerAngles(dictionary);

            // Core state processing
            ProcessBlinkDetection();
            ProcessGazeData(dictionary);
        }

        /// <summary>
        /// Manages blink state transitions:
        /// 1. Detects blink start/end
        /// 2. Manages recovery period
        /// 3. Tracks pre-blink reference position
        /// </summary>
        private void ProcessBlinkDetection()
        {
            bool currentlyBlinking = helper.IsBlinking(eye0Confidence, eye1Confidence, blinkConfidenceThreshold);

            helper.ProcessBlinkState(currentlyBlinking, wasBlinking, rawGazeData,
                ref inBlinkRecovery, ref recoveryFrameCount, ref lastValidGazeBeforeBlink);

            wasBlinking = currentlyBlinking;
        }

        /// <summary>
        /// Processes marker geometry:
        /// 1. Extracts centroids
        /// 2. Verifies required markers
        /// 3. Calculates interior angles
        /// </summary>
        /// <param name="dictionary">Marker data dictionary</param>
        private void ProcessMarkerAngles(Dictionary<string, object> dictionary)
        {
            Dictionary<int, Vector2> centroids = helper.ProcessMarkerCentroids(dictionary);

            // Only calculate angles if all required markers are present
            if (helper.HasRequiredMarkers(centroids, new int[] { 0, 1, 2, 3 }))
            {
                helper.CalculateMarkerAngles(centroids, ref markerAngles);
            }
        }

        /// <summary>
        /// Core gaze processing pipeline:
        /// 1. Configures processing parameters
        /// 2. Applies calibration offsets
        /// 3. Filters gaze data
        /// 4. Updates subsystems
        /// </summary>
        /// <param name="dictionary">Gaze data dictionary</param>
        private void ProcessGazeData(Dictionary<string, object> dictionary)
        {
            // Configure processing parameters
            var config = new ProcessingHelper.GazeProcessingConfig
            {
                eye0Confidence = eye0Confidence,
                eye1Confidence = eye1Confidence,
                blinkConfidenceThreshold = blinkConfidenceThreshold,
                inBlinkRecovery = inBlinkRecovery,
                isSurfaceStable = IsSurfaceStable,
                deltaTime = Time.deltaTime,
                gazeAccuracy = gazeAccuracy,
                gazePrecision = gazePrecision,
                predictionFactor = predictionFactor,
                smoothingFactor = smoothingFactor,
                signalLossTimeout = signalLossTimeout,
                distanceToScreenMm = -(receiver.Position.z * 10), // Convert cm to mm
                calibratedStdDev = calibration.CalibratedStdDev,
                calibratedMeanDistance = calibration.CalibratedMeanDistance,
                useCalibratedValue = confusionZone._useCalibratedValue
            };

            // Warn about head tracker once
            if (!receiver.IsReceiving && !HeadTrackerChecked)
            {
                Debug.LogWarning("Head Tracker is not connected");
                HeadTrackerChecked = true;
            }

            // Get calibration offset if available
            System.Func<Vector2, Vector2> getCalibrationOffset = null;
            if (calibration.HasCalibrationData)
            {
                getCalibrationOffset = (rawScreenPos) => 
                    calibration.GetInterpolatedOffset(rawScreenPos);
            }

            // Process gaze data
            var results = helper.ProcessGazeData(
                dictionary, 
                filteredGazePosition, 
                gazeVelocity,
                timeSinceLastValidData, 
                config, 
                getCalibrationOffset
            );

            // Update state with processed results
            rawGazeData = results.rawGazeData;
            filteredGazePosition = results.filteredGazePosition;

            // Update timing and validity tracking
            if (results.hasValidDataThisFrame)
            {
                timeSinceLastValidData = 0f;
                hasHadValidData = true;

                // Update velocity estimate
                if (hasHadValidData && Time.deltaTime > 0)
                {
                    Vector2 instantVelocity = (rawGazeData - filteredGazePosition) / Time.deltaTime;
                    gazeVelocity = Vector2.Lerp(gazeVelocity, instantVelocity, 0.1f);
                }
            }
            else
            {
                timeSinceLastValidData += Time.deltaTime;
            }

            // Update subsystems with current head tracker Z position
            float currentZPositionMm = receiver != null ? -(receiver.Position.z * 10) : 0f; // Convert cm to mm
            confusionZone.Update(
                filteredGazePosition, 
                results.accuracyRadius, 
                results.precisionRadius,
                currentZPositionMm
            );
            dataLogger.RecordFrame(rawGazeData);
            
            // Record Z position sample during calibration
            if (calibration.CanCalibrate && sessionStarted && isRecording && currentZPositionMm > 0f)
            {
                calibration.RecordZPositionSample(currentZPositionMm);
            }
        }
        #endregion

        #region Input Handling
        /// <summary>
        /// Handles debug input commands:
        /// S - Start calibration session
        /// R - Toggle recording
        /// N - Analyze calibration data
        /// V - Toggle visualization
        /// </summary>
        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.S) && calibration.CanCalibrate)
            {
                confusionZone.SetCalibratedValues(0, 0, 0);
                StartCalibrationSession();
            }

            if (Input.GetKeyDown(KeyCode.R) && sessionStarted)
            {
                if (!isRecording)
                    StartRecording();
                else
                    StopRecording();
            }

            if (Input.GetKeyDown(KeyCode.N))
            {
                AnalyzeCalibrationData();
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                if (visualizer.isVisOn)
                    visualizer.ClearVisualizations();
                else
                    VisualizeAnalysisResults();
            }
        }
        #endregion

        #region Calibration Session Management
        /// <summary>
        /// Initiates or restarts calibration procedure:
        /// 1. Clears previous visualizations
        /// 2. Initializes calibration session
        /// 3. Activates first target
        /// </summary>
        private void StartCalibrationSession()
        {
            // Handle interrupted session
            if (sessionStarted && isRecording && calibration.CanCalibrate)
            {
                StopRecording();
            }

            sessionStarted = true;
            visualizer.ClearVisualizations();
            calibration.StartSession();

            Debug.Log("Calibration session started/restarted. Press 'R' to record at each target.");
        }

        /// <summary>
        /// Manages time-based recording limits:
        /// Automatically stops recording when max duration reached
        /// </summary>
        private void HandleCalibrationSession()
        {
            if (isRecording && Time.time - calibration.RecordingStartTime >= maxRecordingDuration)
            {
                StopRecording();
            }
        }

        /// <summary>
        /// Toggles calibration mode:
        /// - Activates/deactivates calibration UI
        /// - Enables/disables calibration controls
        /// </summary>
        public void SwitchCalibrationMode()
        {
            calibration.CanCalibrate = !calibration.CanCalibrate;
            CalibrationCover.SetActive(calibration.CanCalibrate);
        }

        /// <summary>
        /// Initiates recording for current calibration target:
        /// 1. Starts data collection timer
        /// 2. Activates target visualization
        /// </summary>
        private void StartRecording()
        {
            isRecording = true;
            calibration.StartRecording();
        }

        /// <summary>
        /// Stops recording and advances calibration:
        /// 1. Finalizes data collection
        /// 2. Moves to next target
        /// 3. Deactivates current target
        /// </summary>
        private void StopRecording()
        {
            isRecording = false;
            calibration.StopRecording();
        }

        /// <summary>
        /// Deactivates all calibration targets
        /// </summary>
        private void HideAllTargets()
        {
            foreach (var target in calibrationTargetPoints)
                if (target != null) target.gameObject.SetActive(false);
        }
        #endregion

        #region Data Analysis
        /// <summary>
        /// Analyzes recorded calibration data:
        /// 1. Processes recorded samples
        /// 2. Calculates precision/accuracy metrics
        /// 3. Updates system parameters
        /// 4. Enables calibrated mode
        /// </summary>
        private void AnalyzeCalibrationData()
        {
            calibration.AnalyzeDataFile();
            calibratedStdDev = calibration.CalibratedStdDev;
            calibratedMeanDistance = calibration.CalibratedMeanDistance;
            confusionZone.SetCalibratedValues(calibratedStdDev, calibratedMeanDistance, calibration.CalibratedMeanZPosition);
            VisualizeAnalysisResults();
        }

        /// <summary>
        /// Visualizes calibration results:
        /// 1. Mean gaze points per target
        /// 2. Offset vectors
        /// 3. Precision/accuracy metrics
        /// </summary>
        private void VisualizeAnalysisResults()
        {
            CalibrationManager.AnalysisResults results = calibration.GetAnalysisResults();
            visualizer.VisualizeAnalysisResults(
                calibratedStdDev,
                calibratedMeanDistance,
                results.targetPositions,
                results.meanGazePositions
            );
        }
        #endregion
    }
}