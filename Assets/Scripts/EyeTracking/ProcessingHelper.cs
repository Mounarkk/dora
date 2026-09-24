using System.Collections.Generic;
using PupilLabs;
using UnityEngine;
using System;
using HeadTracking;
using System.IO;
using System.Globalization;
using System.Linq;

/// <summary>
/// Core data processing pipeline for gaze tracking system that handles:
/// - Raw gaze data acquisition, validation, and filtering
/// - Surface marker detection and geometric verification
/// - Blink state detection and recovery management
/// - Confidence zone calculations for visual uncertainty
/// - Coordinate space transformations between normalized, screen, and world spaces
/// 
/// This class serves as the central processing unit for gaze data, ensuring
/// robust and stable gaze estimation in real-time applications.
/// </summary>
public class ProcessingHelper
{
    #region Marker Processing

    /// <summary>
    /// Processes marker confidence data from Pupil Labs surface tracking,
    /// extracting confidence values for each detected marker
    /// </summary>
    /// <param name="dictionary">Raw marker data from Pupil Labs</param>
    /// <param name="_markerConfidences">Output dictionary mapping marker IDs to confidence values (0-1)</param>
    public void ProcessMarkerData(Dictionary<string, object> dictionary, ref Dictionary<int, float> _markerConfidences)
    {
        object[] listOfMarks = dictionary["list_of_markers"] as object[];
        if (listOfMarks != null)
        {
            _markerConfidences.Clear();
            foreach (object markerObj in listOfMarks)
            {
                if (markerObj is Dictionary<object, object> markerDict)
                {
                    // Extract marker ID from "type:id" format
                    string idString = markerDict["id"].ToString();
                    string[] parts = idString.Split(':');
                    if (parts.Length > 0 && int.TryParse(parts[^1], out int id))
                    {
                        _markerConfidences[id] = Convert.ToSingle(markerDict["confidence"]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Calculates interior angles for quadrilateral surface markers
    /// in consistent winding order (clockwise from bottom-left)
    /// </summary>
    /// <param name="centroids">Dictionary of marker positions (screen space)</param>
    /// <param name="_markerAngles">Output list of interior angles in degrees</param>
    public void CalculateMarkerAngles(Dictionary<int, Vector2> centroids, ref List<float> _markerAngles)
    {
        // Establish consistent winding order for quadrilateral
        Vector2[] orderedPoints = new Vector2[4];
        orderedPoints[0] = centroids[0]; // bottom-left (id0)
        orderedPoints[1] = centroids[1]; // top-left (id1)
        orderedPoints[2] = centroids[2]; // top-right (id2)
        orderedPoints[3] = centroids[3]; // bottom-right (id3)

        _markerAngles.Clear();

        for (int i = 0; i < 4; i++)
        {
            // Get adjacent points for angle calculation
            Vector2 A = orderedPoints[(i - 1 + 4) % 4];
            Vector2 B = orderedPoints[i];
            Vector2 C = orderedPoints[(i + 1) % 4];

            // Calculate vectors between points
            Vector2 BA = (A - B).normalized;
            Vector2 BC = (C - B).normalized;

            // Calculate angle using dot product
            float dot = Vector2.Dot(BA, BC);
            dot = Mathf.Clamp(dot, -1f, 1f);
            float angleRad = Mathf.Acos(dot);
            float angleDeg = angleRad * Mathf.Rad2Deg;

            _markerAngles.Add(angleDeg);
        }
    }

    /// <summary>
    /// Extracts screen-space centroid positions from surface tracking data
    /// with marker ID mapping
    /// </summary>
    /// <param name="dictionary">Raw surface tracking data</param>
    /// <returns>Dictionary mapping marker IDs to screen positions</returns>
    public Dictionary<int, Vector2> ProcessMarkerCentroids(Dictionary<string, object> dictionary)
    {
        Dictionary<int, Vector2> centroids = new Dictionary<int, Vector2>();
        object[] listOfMarks = dictionary["list_of_markers"] as object[];

        if (listOfMarks == null) return centroids;

        foreach (object markerObj in listOfMarks)
        {
            if (markerObj is Dictionary<object, object> markerDict)
            {
                int id = Convert.ToInt32(markerDict["id"].ToString());
                object[] centerArray = markerDict["centroid"] as object[];
                if (centerArray != null && centerArray.Length == 2)
                {
                    float centerX = Convert.ToSingle(centerArray[0]);
                    float centerY = Convert.ToSingle(centerArray[1]);
                    centroids[id] = new Vector2(centerX, centerY);
                }
            }
        }

        return centroids;
    }
    #endregion

    #region Gaze Processing

    /// <summary>
    /// Extracts normalized gaze coordinates from surface tracking data
    /// and converts to screen-space pixels
    /// </summary>
    /// <param name="dictionary">Raw gaze data dictionary</param>
    /// <returns>Gaze position in screen coordinates (pixels)</returns>
    public Vector2 ProcessGazeOnSurface(Dictionary<string, object> dictionary)
    {
        if (!dictionary.ContainsKey("name"))
            return Vector2.zero;

        var gazeOnSurface = dictionary["gaze_on_surfaces"] as object[];
        if (gazeOnSurface == null || gazeOnSurface.Length == 0)
            return Vector2.zero;

        var firstGaze = gazeOnSurface[0] as Dictionary<object, object>;
        if (firstGaze == null || !firstGaze.ContainsKey("norm_pos"))
            return Vector2.zero;

        var normPos = firstGaze["norm_pos"] as object[];
        if (normPos == null || normPos.Length < 2)
            return Vector2.zero;

        // Convert normalized coordinates to screen space
        float rawX = (float)(double)normPos[0];
        float rawY = (float)(double)normPos[1];

        Vector2 gaze = new Vector2(rawX, rawY);
        return NormalizeToScreen(gaze);
    }

    /// <summary>
    /// Configuration parameters for gaze processing pipeline
    /// </summary>
    public struct GazeProcessingConfig
    {
        public float eye0Confidence;                 // Confidence score for left eye (0-1)
        public float eye1Confidence;                 // Confidence score for right eye (0-1)
        public float blinkConfidenceThreshold;       // Minimum confidence to be considered non-blinking (0-1)
        public bool inBlinkRecovery;                 // Whether system is in post-blink stabilization
        public bool isSurfaceStable;                 // Marker tracking reliability flag
        public float gazeAccuracy;                   // Reported angular accuracy (degrees)
        public float deltaTime;                      // Time since last frame (seconds)
        public float gazePrecision;                  // Reported angular precision (degrees)
        public float predictionFactor;               // Velocity extrapolation weight (0-1)
        public float smoothingFactor;                // Exponential smoothing coefficient (0-1)
        public float signalLossTimeout;              // Max duration for gaze prediction (seconds)
        public float distanceToScreenMm;             // User-to-display distance (millimeters)
        public float calibratedStdDev;               // Post-calibration standard deviation (pixels)
        public float calibratedMeanDistance;         // Post-calibration mean error (pixels)
        public bool useCalibratedValue;              // Whether to use calibration metrics
    }

    /// <summary>
    /// Container for gaze processing results
    /// </summary>
    public struct GazeProcessingResults
    {
        public Vector2 rawGazeData;                  // Current unfiltered gaze position (screen space)
        public Vector2 filteredGazePosition;         // Processed stable gaze position (screen space)
        public float accuracyRadius;                 // Radius of gaze accuracy zone (pixels)
        public float precisionRadius;                // Radius of gaze precision zone (pixels)
        public bool hasValidDataThisFrame;           // Frame validity flag
    }

    /// <summary>
    /// Main gaze processing pipeline executing in order:
    /// 1. Validity check based on eye tracking state
    /// 2. Raw data extraction and calibration
    /// 3. Velocity-based filtering with signal loss handling
    /// 4. Confidence zone calculation
    /// </summary>
    /// <param name="dictionary">Raw gaze data dictionary</param>
    /// <param name="previousFilteredPosition">Last frame's filtered position</param>
    /// <param name="gazeVelocity">Current gaze velocity vector</param>
    /// <param name="timeSinceLastValidData">Duration since last valid data point</param>
    /// <param name="config">Processing configuration parameters</param>
    /// <param name="getCalibrationOffset">Optional calibration offset function</param>
    /// <returns>Processed gaze results including filtered position and confidence metrics</returns>
    public GazeProcessingResults ProcessGazeData(
        Dictionary<string, object> dictionary,
        Vector2 previousFilteredPosition,
        Vector2 gazeVelocity,
        float timeSinceLastValidData,
        GazeProcessingConfig config,
        System.Func<Vector2, Vector2> getCalibrationOffset = null)
    {
        var results = new GazeProcessingResults();

        // Check if we should process new gaze data
        bool shouldProcessGaze = ShouldProcessGaze(
            config.eye0Confidence, config.eye1Confidence,
            config.blinkConfidenceThreshold, config.inBlinkRecovery);

        Vector2 currentRawGaze = Vector2.zero;
        bool hasValidDataThisFrame = false;

        // Process raw gaze data if conditions are met
        if (dictionary.ContainsKey("name") && config.isSurfaceStable && shouldProcessGaze)
        {
            Vector2 rawScreenPos = ProcessGazeOnSurface(dictionary);
            
            if (rawScreenPos != Vector2.zero)
            {
                currentRawGaze = rawScreenPos;

                // Apply calibration offset if available
                if (getCalibrationOffset != null)
                {
                    Vector2 offset = getCalibrationOffset(rawScreenPos);
                    currentRawGaze -= offset;
                }

                hasValidDataThisFrame = true;
            }
        }

        // Set results
        results.rawGazeData = currentRawGaze;
        results.hasValidDataThisFrame = hasValidDataThisFrame;

        // Apply filtering to get smooth position
        results.filteredGazePosition = ApplyAccelerationFilter(
            currentRawGaze, 
            previousFilteredPosition, 
            gazeVelocity, 
            config.deltaTime, 
            hasValidDataThisFrame, 
            timeSinceLastValidData, 
            config.predictionFactor,
            config.smoothingFactor, 
            config.signalLossTimeout
        );

        // Calculate confusion zone radii
        CalculateConfusionZoneRadii(config.gazeAccuracy, config.gazePrecision, 
            config.calibratedStdDev, config.calibratedMeanDistance,
            config.useCalibratedValue, config.distanceToScreenMm,
            out results.accuracyRadius, out results.precisionRadius);

        return results;
    }

    /// <summary>
    /// Applies adaptive gaze filtering with:
    /// - Position update during valid tracking
    /// - Velocity-decay prediction during signal loss
    /// - Screen boundary clamping
    /// </summary>
    /// <param name="newRawPosition">Current raw gaze position</param>
    /// <param name="currentFilteredPosition">Last frame's filtered position</param>
    /// <param name="gazeVelocity">Current gaze velocity vector</param>
    /// <param name="deltaTime">Frame delta time</param>
    /// <param name="hasValidData">Whether current frame has valid data</param>
    /// <param name="timeSinceLastValidData">Duration since last valid data</param>
    /// <param name="predictionFactor">Velocity prediction weight (0-1)</param>
    /// <param name="smoothingFactor">Position smoothing coefficient (0-1)</param>
    /// <param name="signalLossTimeout">Max prediction duration</param>
    /// <returns>Filtered gaze position in screen space</returns>
    public Vector2 ApplyAccelerationFilter(
        Vector2 newRawPosition, 
        Vector2 currentFilteredPosition,
        Vector2 gazeVelocity, 
        float deltaTime, 
        bool hasValidData, 
        float timeSinceLastValidData,
        float predictionFactor, 
        float smoothingFactor, 
        float signalLossTimeout)
    {
        if (deltaTime <= 0) return currentFilteredPosition;

        Vector2 filteredPosition = currentFilteredPosition;

        if (hasValidData && newRawPosition != Vector2.zero)
        {
            filteredPosition = newRawPosition;
        }
        else if (newRawPosition == Vector2.zero) // Only handle when data is truly lost
        {
            // Handle signal loss with graceful decay using existing velocity
            if (timeSinceLastValidData < signalLossTimeout)
            {
                float timeRatio = timeSinceLastValidData / signalLossTimeout;
                float decelerationCurve = Mathf.Pow(1f - timeRatio, 2f); // Quadratic decay

                Vector2 decayedVelocity = gazeVelocity * decelerationCurve;
                Vector2 nextPosition = filteredPosition + decayedVelocity * deltaTime;

                filteredPosition = ClampToScreen(nextPosition);
            }
            // If timeout exceeded, maintain last filtered position
        }

        return filteredPosition;
    }

    /// <summary>
    /// Determines if gaze should be processed based on:
    /// - Eye confidence above blink threshold
    /// - Not in blink recovery state
    /// </summary>
    /// <param name="eye0Confidence">Left eye confidence (0-1)</param>
    /// <param name="eye1Confidence">Right eye confidence (0-1)</param>
    /// <param name="blinkConfidenceThreshold">Minimum valid confidence</param>
    /// <param name="inBlinkRecovery">Whether in blink recovery state</param>
    /// <returns>True if gaze data should be processed this frame</returns>
    public bool ShouldProcessGaze(
        float eye0Confidence, 
        float eye1Confidence,
        float blinkConfidenceThreshold, 
        bool inBlinkRecovery)
    {
        return !IsBlinking(eye0Confidence, eye1Confidence, blinkConfidenceThreshold) && !inBlinkRecovery;
    }
    #endregion

    #region Pupil Data Processing

    /// <summary>
    /// Extracts per-eye confidence metrics from pupil data
    /// </summary>
    /// <param name="dictionary">Raw pupil data dictionary</param>
    /// <param name="eye0Confidence">Output left eye confidence (0-1)</param>
    /// <param name="eye1Confidence">Output right eye confidence (0-1)</param>
    public void ProcessPupilConfidence(
        Dictionary<string, object> dictionary,
        ref float eye0Confidence, 
        ref float eye1Confidence)
    {
        if (!dictionary.ContainsKey("id") || !dictionary.ContainsKey("confidence"))
            return;

        int eyeId = Convert.ToInt32(dictionary["id"]);
        float confidence = Convert.ToSingle(dictionary["confidence"]);

        if (eyeId == 0)
        {
            eye0Confidence = confidence;
        }
        else if (eyeId == 1)
        {
            eye1Confidence = confidence;
        }
    }

    /// <summary>
    /// Parses surface tracking metrics from Pupil Labs dictionary
    /// </summary>
    /// <param name="dictionary">Raw surface data dictionary</param>
    /// <param name="numberOfTags">Output detected marker count</param>
    /// <param name="numberOfNeededTags">Output required marker count</param>
    /// <param name="accuracy">Output reported angular accuracy (degrees)</param>
    /// <param name="precision">Output reported angular precision (degrees)</param>
    public void ExtractSurfaceData(
        Dictionary<string, object> dictionary,
        out int numberOfTags, 
        out int numberOfNeededTags, 
        out float accuracy, 
        out float precision)
    {
        numberOfTags = Convert.ToInt32(dictionary["number_of_tags"]);
        numberOfNeededTags = Convert.ToInt32(dictionary["number_of_needed_tags"]);
        accuracy = Convert.ToSingle(dictionary["accuracy"]);
        precision = Convert.ToSingle(dictionary["precision"]);
    }
    #endregion

    #region Blink Detection & Recovery

    /// <summary>
    /// Detects blink state when both eyes' confidence drops below threshold
    /// </summary>
    /// <param name="eye0Confidence">Left eye confidence (0-1)</param>
    /// <param name="eye1Confidence">Right eye confidence (0-1)</param>
    /// <param name="blinkConfidenceThreshold">Minimum valid confidence</param>
    /// <returns>True if both eyes are below confidence threshold</returns>
    public bool IsBlinking(
        float eye0Confidence, 
        float eye1Confidence, 
        float blinkConfidenceThreshold)
    {
        return eye0Confidence < blinkConfidenceThreshold &&
               eye1Confidence < blinkConfidenceThreshold;
    }

    /// <summary>
    /// Manages blink recovery state machine:
    /// - Captures last valid gaze at blink start
    /// - Initiates recovery period at blink end
    /// - Times out recovery after fixed duration
    /// </summary>
    /// <param name="currentlyBlinking">Current blink state</param>
    /// <param name="wasBlinking">Previous frame blink state</param>
    /// <param name="currentRawGaze">Current raw gaze position</param>
    /// <param name="inBlinkRecovery">Output blink recovery state</param>
    /// <param name="recoveryFrameCount">Output frames since blink ended</param>
    /// <param name="lastValidGazeBeforeBlink">Output last valid pre-blink position</param>
    public void ProcessBlinkState(
        bool currentlyBlinking, 
        bool wasBlinking, 
        Vector2 currentRawGaze,
        ref bool inBlinkRecovery, 
        ref int recoveryFrameCount, 
        ref Vector2 lastValidGazeBeforeBlink)
    {
        // Blink start detection
        if (currentlyBlinking && !wasBlinking)
        {
            lastValidGazeBeforeBlink = currentRawGaze;
        }
        // Blink end detection
        else if (!currentlyBlinking && wasBlinking)
        {
            inBlinkRecovery = true;
            recoveryFrameCount = 0;
        }

        // Handle recovery period
        if (inBlinkRecovery)
        {
            recoveryFrameCount++;
            if (recoveryFrameCount >= 40) // ~0.33s at 12fps
            {
                inBlinkRecovery = false;
                recoveryFrameCount = 0;
            }
        }
    }
    #endregion

    #region Stability & Validation

    /// <summary>
    /// Evaluates surface tracking stability through:
    /// 1. Marker count verification
    /// 2. Confidence threshold checking (>0.95)
    /// 3. Geometric validity (82°-98° angles)
    /// 4. Eye tracking confidence (>0.9)
    /// </summary>
    /// <param name="numberOfTags">Detected marker count</param>
    /// <param name="numberOfNeededTags">Required marker count</param>
    /// <param name="markerConfidences">Marker confidence dictionary</param>
    /// <param name="markerAngles">Interior angles of marker quadrilateral</param>
    /// <param name="eye0Confidence">Left eye confidence (0-1)</param>
    /// <param name="eye1Confidence">Right eye confidence (0-1)</param>
    /// <returns>True if all stability criteria are met</returns>
    public bool CheckEyeTrackingStability(
        int numberOfTags, 
        int numberOfNeededTags,
        Dictionary<int, float> markerConfidences, 
        List<float> markerAngles,
        float eye0Confidence, 
        float eye1Confidence)
    {
        // Check marker count
        if (numberOfTags != numberOfNeededTags)
        {
            return false;
        }
        
        // Check marker confidence
        if (markerConfidences.Count < numberOfNeededTags || markerConfidences.Values.Any(c => c < 0.95f))
            return false;

        // Check marker angles (should be ~90° for square markers)
        if (markerAngles.Count != 4 || markerAngles.Any(a => a < 82f || a > 98f))
            return false;

        // Check eye tracking confidence
        if (eye0Confidence < 0.85f || eye1Confidence < 0.85f)
            return false;

        return true;
    }

    /// <summary>
    /// Verifies all required marker IDs are present in detection data
    /// </summary>
    /// <param name="centroids">Marker centroid dictionary</param>
    /// <param name="requiredIds">Required marker IDs</param>
    /// <returns>True if all required markers are detected</returns>
    public bool HasRequiredMarkers(
        Dictionary<int, Vector2> centroids, 
        int[] requiredIds)
    {
        return requiredIds.All(id => centroids.ContainsKey(id));
    }
    #endregion

    #region Mathematical Calculations

    /// <summary>
    /// Converts visual angle to screen pixels using:
    ///   pixels = tan(visual_angle) * distance_to_screen * DPI / 25.4
    /// </summary>
    /// <param name="angleDegrees">Visual angle in degrees</param>
    /// <param name="distanceToScreenMm">Viewing distance in millimeters</param>
    /// <returns>Equivalent screen radius in pixels</returns>
    public float CalculateRadiusInPixels(
        float angleDegrees, 
        float distanceToScreenMm)
    {
        if (angleDegrees <= 0f || distanceToScreenMm <= 0f)
            return 0f;

        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        float radiusMm = Mathf.Tan(angleRadians) * distanceToScreenMm;
        return radiusMm * Screen.dpi / 25.4f; // Convert mm to pixels
    }

    /// <summary>
    /// Constrains position to physical screen boundaries
    /// </summary>
    /// <param name="position">Input screen position</param>
    /// <returns>Position clamped within screen dimensions</returns>
    public Vector2 ClampToScreen(Vector2 position)
    {
        return new Vector2(
            Mathf.Clamp(position.x, 0, Screen.width),
            Mathf.Clamp(position.y, 0, Screen.height)
        );
    }
    #endregion

    #region Confidence Zone Calculations

    /// <summary>
    /// Calculates confusion zone radii using either:
    /// - Calibrated metrics (mean + 3σ) when available, or
    /// - Real-time gaze metrics converted to pixels
    /// </summary>
    /// <param name="gazeAccuracy">Reported angular accuracy (degrees)</param>
    /// <param name="gazePrecision">Reported angular precision (degrees)</param>
    /// <param name="calibratedStdDev">Post-calibration standard deviation (pixels)</param>
    /// <param name="calibratedMeanDistance">Post-calibration mean error (pixels)</param>
    /// <param name="useCalibratedValue">Prefer calibration metrics flag</param>
    /// <param name="distanceToScreenMm">Viewing distance (millimeters)</param>
    /// <param name="accuracyRadius">Output accuracy zone radius (pixels)</param>
    /// <param name="precisionRadius">Output precision zone radius (pixels)</param>
    public void CalculateConfusionZoneRadii(
        float gazeAccuracy, 
        float gazePrecision,
        float calibratedStdDev, 
        float calibratedMeanDistance, 
        bool useCalibratedValue,
        float distanceToScreenMm, 
        out float accuracyRadius, 
        out float precisionRadius)
    {
        if (useCalibratedValue && calibratedStdDev > 0)
        {
            // Use calibrated values: 3σ precision radius
            precisionRadius = calibratedStdDev * 3;
            accuracyRadius = calibratedMeanDistance + precisionRadius;
        }
        else if ((gazeAccuracy > 0 || gazePrecision > 0) && distanceToScreenMm > 0)
        {
            // Calculate from gaze metrics
            precisionRadius = CalculateRadiusInPixels(gazePrecision, distanceToScreenMm) * 3;
            accuracyRadius = CalculateRadiusInPixels(gazeAccuracy, distanceToScreenMm) + precisionRadius;
        }
        else
        {
            // Default fallback
            accuracyRadius = 0;
            precisionRadius = 0;
        }
    }
    #endregion

    #region Utility Functions

    /// <summary>
    /// Converts normalized [0,1] coordinates to screen pixels
    /// </summary>
    /// <param name="normalizedPosition">Normalized coordinates (x:0-1, y:0-1)</param>
    /// <returns>Screen position in pixels</returns>
    public Vector2 NormalizeToScreen(Vector2 normalizedPosition)
    {
        return new Vector2(
            normalizedPosition.x * Screen.width,
            normalizedPosition.y * Screen.height
        );
    }

    /// <summary>
    /// Converts screen pixels to normalized [0,1] coordinates
    /// </summary>
    /// <param name="screenPosition">Screen position in pixels</param>
    /// <returns>Normalized coordinates (x:0-1, y:0-1)</returns>
    public Vector2 ScreenToNormalized(Vector2 screenPosition)
    {
        return new Vector2(
            screenPosition.x / Screen.width,
            screenPosition.y / Screen.height
        );
    }
    #endregion
}