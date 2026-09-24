using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Globalization;
using System.Linq;

/// <summary>
/// Manages gaze calibration sessions, including data recording, analysis, and offset interpolation.
/// Handles target sequencing, gaze data processing, and calibration validation.
/// </summary>

namespace EyeTracking
{
    public class CalibrationManager
    {
        // Public properties for calibration results
        public float CalibratedStdDev { get; private set; }       // Standard deviation of gaze samples
        public float CalibratedMeanDistance { get; private set; } // Mean distance between gaze and targets
        public bool HasCalibrationData { get; private set; }      // True if full 9-point calibration succeeded
        public float RecordingStartTime { get; private set; }     // Timestamp when recording began
        public float CalibratedMeanZPosition { get; private set; } // Mean Z position during calibration

        // Private fields
        private readonly Transform[] _targetPoints;               // Array of target transforms
        private readonly GazeDataLogger _logger;                  // Reference to gaze data logger
        private readonly float _maxRecordingDuration;             // Max allowed recording time per target
        private int _currentTargetIndex = -1;                     // Current target index (-1 = not started)

        // Calibration data storage
        private Vector2[] _calibrationScreenPositions = new Vector2[9];  // Screen positions of targets
        private Vector2[] _offsetVectors = new Vector2[9];                // Gaze offsets per target
        private List<Vector2>[] _rawGazeSamples;                          // Raw gaze samples per target
        private List<Vector2> _targetScreenPositions = new List<Vector2>(); // Target positions (centered)
        private List<Vector2> _meanGazePositions = new List<Vector2>();     // Mean gaze positions (centered)
        private List<float> _zPositionSamples = new List<float>();           // Z position samples during calibration

        // File paths
        private string _path => Path.Combine(Application.dataPath, "Data.csv");                   // Raw gaze data

        public bool CanCalibrate = false; // Flag to enable/disable calibration

        /// <summary>
        /// Initializes calibration manager with target points, data logger, and recording constraints.
        /// </summary>
        /// <param name="targets">Array of target transforms for calibration</param>
        /// <param name="logger">Gaze data logger instance for recording</param>
        /// <param name="maxRecordingDuration">Maximum allowed recording duration per target</param>
        public CalibrationManager(Transform[] targets, GazeDataLogger logger, float maxRecordingDuration)
        {
            _targetPoints = targets;
            _logger = logger;
            _maxRecordingDuration = maxRecordingDuration;

            // Initialize gaze sample storage
            _rawGazeSamples = new List<Vector2>[targets.Length];
            for (int i = 0; i < _rawGazeSamples.Length; i++)
            {
                _rawGazeSamples[i] = new List<Vector2>();
            }
            HideAllTargets(); // Start with all targets hidden
        }

        #region Public Session Control Methods

        /// <summary>
        /// Starts a new calibration session, resetting state and showing the first target.
        /// </summary>
        public void StartSession()
        {
            ResetSession();
            _currentTargetIndex = -1;
            ShowNextTarget();
            Debug.Log("Calibration session started. Navigate through targets with 'R' key.");
        }


        /// <summary>
        /// Begins recording gaze data for the current target.
        /// </summary>
        public void StartRecording()
        {
            RecordingStartTime = Time.time;
            _logger.StartRecording();
            _zPositionSamples.Clear(); // Clear Z position samples for new recording session
        }

        /// <summary>
        /// Records a Z position sample during calibration for later analysis
        /// </summary>
        /// <param name="zPosition">Current Z position from head tracker</param>
        public void RecordZPositionSample(float zPosition)
        {
            if (zPosition > 0f) // Only record valid Z positions
            {
                _zPositionSamples.Add(zPosition);
            }
        }

        /// <summary>
        /// Stops recording and advances to the next target (or ends session if complete).
        /// </summary>
        public void StopRecording()
        {
            _logger.StopRecording();
            ShowNextTarget();
        }

        /// <summary>
        /// Hides all calibration targets (typically called at session end).
        /// </summary>
        public void HideAllTargets()
        {
            foreach (var t in _targetPoints)
                t?.gameObject.SetActive(false);
        }

        #endregion

        #region Data Analysis Methods

        /// <summary>
        /// Parses recorded gaze data from file and computes calibration metrics.
        /// </summary>
        public void AnalyzeDataFile()
        {
            HideAllTargets();
            ResetCalibrationData();

            if (!File.Exists(_path))
            {
                Debug.LogError($"Data file not found at: {_path}");
                return;
            }

            string[] lines = File.ReadAllLines(_path);
            Debug.Log($"Reading {lines.Length} lines from data file");

            List<List<Vector2>> groupedGazePoints = ParseGazeDataFromFile(lines);
            Debug.Log($"Parsed {groupedGazePoints.Count} groups of gaze data");

            if (groupedGazePoints.Count == 0)
            {
                Debug.LogWarning("No gaze data groups found!");
                return;
            }

            ProcessCalibrationResults(groupedGazePoints);
        }

        /// <summary>
        /// Computes interpolated gaze offset for runtime correction.
        /// Uses bilinear interpolation between the nearest calibration points.
        /// </summary>
        /// <param name="gazePoint">Current gaze position in screen space</param>
        /// <returns>Interpolated offset vector</returns>
        public Vector2 GetInterpolatedOffset(Vector2 gazePoint)
        {
            if (!HasCalibrationData) return Vector2.zero;

            // Determine which calibration quadrant contains the gaze point
            Vector2 center = _calibrationScreenPositions[0];
            bool inBottom = gazePoint.y < center.y;
            bool inLeft = gazePoint.x < center.x;

            // Select calibration rectangle vertices based on quadrant
            int rectIndex = 0;
            if (inBottom && inLeft) rectIndex = 2;      // Bottom-left
            else if (inBottom && !inLeft) rectIndex = 3; // Bottom-right
            else if (!inBottom && inLeft) rectIndex = 0; // Top-left
            else rectIndex = 1;                         // Top-right

            // Define rectangle corners and their offsets
            Vector2 tl, tr, bl, br;
            Vector2 offsetTL, offsetTR, offsetBL, offsetBR;

            // Assign vertices based on calibration point layout
            switch (rectIndex)
            {
                case 0: // Top-left rectangle (points 7,2,3,0)
                    tl = _calibrationScreenPositions[7];
                    tr = _calibrationScreenPositions[2];
                    bl = _calibrationScreenPositions[3];
                    br = _calibrationScreenPositions[0];
                    offsetTL = _offsetVectors[7];
                    offsetTR = _offsetVectors[2];
                    offsetBL = _offsetVectors[3];
                    offsetBR = _offsetVectors[0];
                    break;
                case 1: // Top-right rectangle (points 2,6,0,1)
                    tl = _calibrationScreenPositions[2];
                    tr = _calibrationScreenPositions[6];
                    bl = _calibrationScreenPositions[0];
                    br = _calibrationScreenPositions[1];
                    offsetTL = _offsetVectors[2];
                    offsetTR = _offsetVectors[6];
                    offsetBL = _offsetVectors[0];
                    offsetBR = _offsetVectors[1];
                    break;
                case 2: // Bottom-left rectangle (points 3,0,8,4)
                    tl = _calibrationScreenPositions[3];
                    tr = _calibrationScreenPositions[0];
                    bl = _calibrationScreenPositions[8];
                    br = _calibrationScreenPositions[4];
                    offsetTL = _offsetVectors[3];
                    offsetTR = _offsetVectors[0];
                    offsetBL = _offsetVectors[8];
                    offsetBR = _offsetVectors[4];
                    break;
                default: // Bottom-right rectangle (points 0,1,4,5)
                    tl = _calibrationScreenPositions[0];
                    tr = _calibrationScreenPositions[1];
                    bl = _calibrationScreenPositions[4];
                    br = _calibrationScreenPositions[5];
                    offsetTL = _offsetVectors[0];
                    offsetTR = _offsetVectors[1];
                    offsetBL = _offsetVectors[4];
                    offsetBR = _offsetVectors[5];
                    break;
            }

            // Calculate normalized position within rectangle
            float width = tr.x - tl.x;
            float height = bl.y - tl.y;

            if (Mathf.Approximately(width, 0)) width = 0.001f;
            if (Mathf.Approximately(height, 0)) height = 0.001f;

            float u = Mathf.Clamp01((gazePoint.x - tl.x) / width);
            float v = Mathf.Clamp01((gazePoint.y - tl.y) / height);

            // Bilinear interpolation of offsets
            Vector2 topOffset = Vector2.Lerp(offsetTL, offsetTR, u);
            Vector2 bottomOffset = Vector2.Lerp(offsetBL, offsetBR, u);
            return Vector2.Lerp(topOffset, bottomOffset, v);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Visualizes calibration results by creating mean points and vectors for each target.
        /// </summary>
        private void ShowNextTarget()
        {
            HideAllTargets();
            _currentTargetIndex++;
            if (_currentTargetIndex < _targetPoints.Length && _targetPoints[_currentTargetIndex] != null)
            {
                _targetPoints[_currentTargetIndex].gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Resets the calibration session state, clearing all data and hiding targets.
        /// </summary>
        private void ResetSession()
        {
            for (int i = 0; i < _rawGazeSamples.Length; i++)
            {
                _rawGazeSamples[i].Clear();
            }
            _logger.ClearData();
            HideAllTargets();
            Debug.Log("Calibration session reset.");
        }

        /// <summary>
        /// Resets all calibration data to initial state.
        /// </summary>
        private void ResetCalibrationData()
        {
            HasCalibrationData = false;
            CalibratedMeanDistance = 0f;
            _targetScreenPositions.Clear();
            _meanGazePositions.Clear();
            System.Array.Clear(_calibrationScreenPositions, 0, 9);
            System.Array.Clear(_offsetVectors, 0, 9);
        }

        /// <summary>
        /// Parses gaze data from the file and groups it by calibration points.
        /// </summary>
        /// <returns>List of grouped gaze points for each calibration target</returns>
        /// <param name="lines">Lines read from the data file</param>
        /// <returns>List of lists containing gaze points for each calibration target</returns>
        private List<List<Vector2>> ParseGazeDataFromFile(string[] lines)
        {
            List<List<Vector2>> groupedGazePoints = new List<List<Vector2>>();
            List<Vector2> currentGroup = null;
            int lineCount = 0;

            foreach (var line in lines)
            {
                lineCount++;
                string l = line.Trim();

                if (string.IsNullOrWhiteSpace(l) || l.StartsWith("X_pos"))
                    continue;

                if (l.StartsWith("new_point"))
                {
                    if (currentGroup != null && currentGroup.Count > 0)
                    {
                        Debug.Log($"Implicitly ending group with {currentGroup.Count} points");
                        groupedGazePoints.Add(currentGroup);
                    }

                    currentGroup = new List<Vector2>();
                    Debug.Log($"Starting new group at line {lineCount}");
                }
                else if (l.StartsWith("duration"))
                {
                    if (currentGroup != null && currentGroup.Count > 0)
                    {
                        Debug.Log($"Ending group with {currentGroup.Count} points due to duration marker");
                        groupedGazePoints.Add(currentGroup);
                        currentGroup = null;
                    }
                }
                else if (currentGroup != null)
                {
                    ParseGazePointFromLine(l, currentGroup);
                }
            }

            if (currentGroup != null && currentGroup.Count > 0)
            {
                Debug.Log($"Adding final group with {currentGroup.Count} points");
                groupedGazePoints.Add(currentGroup);
            }

            return groupedGazePoints;
        }
        /// <summary>
        /// Parses a single gaze point line and adds it to the current group.
        /// </summary>
        /// <param name="line">Line containing gaze point data</param>
        /// <param name="currentGroup">Current group of gaze points</param>
        /// <returns>True if parsing was successful, false otherwise</returns>
        private bool ParseGazePointFromLine(string line, List<Vector2> currentGroup)
        {
            string[] parts = line.Split(',');
            if (parts.Length >= 2)
            {
                string xStr = parts[0].Replace(',', '.');
                string yStr = parts[1].Replace(',', '.');

                if (float.TryParse(xStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                {
                    currentGroup.Add(new Vector2(x, y));
                    return true;
                }
                else
                {
                    Debug.LogWarning($"Failed to parse gaze point: {line}");
                }
            }
            return false;
        }
        /// <summary>
        /// Processes the parsed calibration results to compute mean positions and standard deviation.
        /// </summary>
        /// <param name="groupedGazePoints">List of grouped gaze points for each calibration target</param>
        private void ProcessCalibrationResults(List<List<Vector2>> groupedGazePoints)
        {
            float meanStdDev = 0;
            float totalMeanDistance = 0;
            _targetScreenPositions.Clear();
            _meanGazePositions.Clear();

            int pointCount = Mathf.Min(groupedGazePoints.Count, _targetPoints.Length);
            Debug.Log($"Processing {pointCount} calibration points");

            for (int i = 0; i < pointCount; i++)
            {
                if (_targetPoints[i] == null)
                {
                    Debug.LogError($"Target point {i} is null!");
                    continue;
                }

                List<Vector2> group = groupedGazePoints[i];
                if (group.Count == 0)
                {
                    Debug.LogWarning($"Group {i} has no data points!");
                    continue;
                }

                Debug.Log($"Processing target {i} with {group.Count} gaze samples");
                float meanDistanceForThisPoint = ProcessCalibrationPoint(i, group, ref meanStdDev);
                totalMeanDistance += meanDistanceForThisPoint;
            }

            if (pointCount > 0)
            {
                meanStdDev /= pointCount;
                CalibratedStdDev = meanStdDev;
                CalibratedMeanDistance = totalMeanDistance / pointCount;
                HasCalibrationData = pointCount == 9;
                
                // Calculate mean Z position from all samples collected during calibration
                if (_zPositionSamples.Count > 0)
                {
                    CalibratedMeanZPosition = _zPositionSamples.Average();
                    Debug.Log($"Calibration completed with mean Z position: {CalibratedMeanZPosition:F2}mm from {_zPositionSamples.Count} samples");
                }
                else
                {
                    CalibratedMeanZPosition = 0f;
                    Debug.LogWarning("No Z position samples collected during calibration");
                }
            }
        }
        /// <summary>
        /// Processes a single calibration point's gaze data to compute mean position and standard deviation.
        /// </summary>
        /// <param name="index">Index of the calibration point</param>
        /// <param name="group">List of gaze points for this calibration target</param>
        /// <param name="meanStdDev">Reference to the mean standard deviation value</param>
        /// <returns>Mean distance from the gaze points to the target position</returns>
        private float ProcessCalibrationPoint(int index, List<Vector2> group, ref float meanStdDev)
        {
            Vector2 meanPos = CalculateMeanPosition(group);
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, _targetPoints[index].position);

            _calibrationScreenPositions[index] = screenPos;
            _offsetVectors[index] = meanPos - screenPos;

            Vector2 centeredScreenPos = new Vector2(
                screenPos.x - Screen.width / 2f,
                screenPos.y - Screen.height / 2f
            );

            float meanDistToTarget = Vector2.Distance(meanPos, screenPos);
            float stdDev = CalculateStandardDeviation(group, meanPos);

            meanPos = new Vector2(
                meanPos.x - Screen.width / 2f,
                meanPos.y - Screen.height / 2f
            );

            _targetScreenPositions.Add(centeredScreenPos);
            _meanGazePositions.Add(meanPos);
            meanStdDev += stdDev;

            return meanDistToTarget;
        }
        /// <summary>
        /// Calculates the mean position of a list of gaze points.
        /// </summary>
        /// <param name="positions">List of gaze positions</param>
        /// <returns>Mean position as a Vector2</returns>
        private Vector2 CalculateMeanPosition(List<Vector2> positions)
        {
            Vector2 meanPos = Vector2.zero;
            foreach (var pos in positions) meanPos += pos;
            return meanPos / positions.Count;
        }
        /// <summary>
        /// Calculates the standard deviation of gaze positions relative to their mean. 
        /// Uses the formula for variance and returns the square root.
        /// /// </summary>
        /// <param name="positions">List of gaze positions</param>
        /// <param name="mean">Mean position of the gaze points</param>
        /// <returns>Standard deviation as a float</returns>
        private float CalculateStandardDeviation(List<Vector2> positions, Vector2 mean)
        {
            float variance = 0f;
            foreach (var pos in positions)
            {
                variance += (pos - mean).sqrMagnitude;
            }
            variance /= positions.Count;
            return Mathf.Sqrt(variance);
        }

        /// <summary>
        /// Returns analysis results for visualization.
        /// </summary>
        public AnalysisResults GetAnalysisResults()
        {
            return new AnalysisResults
            {
                targetPositions = _targetScreenPositions,
                meanGazePositions = _meanGazePositions
            };
        }

        /// <summary>
        /// Container for calibration visualization data.
        /// </summary>
        public struct AnalysisResults
        {
            public List<Vector2> targetPositions;    // Target positions in screen space
            public List<Vector2> meanGazePositions; // Corresponding mean gaze positions
        }

        #endregion
    }
}
