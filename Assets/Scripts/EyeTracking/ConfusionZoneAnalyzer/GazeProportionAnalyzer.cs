using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace EyeTracking.ConfusionZoneAnalyzer
{
    /// <summary>
    /// Periodically queries the GazeZoneAnalyzer for gaze distribution proportions
    /// and logs or exposes significant results above a given threshold.
    /// </summary>
    public class GazeProportionAnalyzer : MonoBehaviour
    {
        #region Inspector Fields
        /// <summary>
        /// Reference to the GazeZoneAnalyzer that provides per-object gaze proportions.
        /// </summary>
        [Header("Dependencies")]
        [Tooltip("Analyzer providing per-object gaze zone proportions.")]
        public GazeZoneAnalyzer gazeZoneAnalyzer;

        /// <summary>
        /// Time interval (in seconds) between consecutive gaze analyses.
        /// </summary>
        [Header("Analysis Settings")]
        [Tooltip("Time interval (in seconds) between analyses.")]
        public float analysisInterval = 2f;

        /// <summary>
        /// Minimum proportion (0-1) required to consider an object significant.
        /// </summary>
        [Tooltip("Minimum proportion threshold to report (e.g., 0.01 = 1%).")]
        public float minimumProportionThreshold = 0.01f;
        #endregion

        #region Private Fields
        /// <summary>
        /// Timestamp of the last analysis run (Time.time).
        /// </summary>
        private float lastAnalysisTime;
        #endregion

        #region Unity Lifecycle
        /// <summary>
        /// Unity Start callback: finds the GazeZoneAnalyzer in the scene if not set.
        /// </summary>
        private void Start()
        {
            if (gazeZoneAnalyzer == null)
                gazeZoneAnalyzer = FindAnyObjectByType<GazeZoneAnalyzer>();
        }

        /// <summary>
        /// Unity Update callback: triggers PerformGazeAnalysis at defined intervals.
        /// </summary>
        private void Update()
        {
            if (Time.time - lastAnalysisTime >= analysisInterval)
            {
                lastAnalysisTime = Time.time;
                PerformGazeAnalysis();
            }
        }
        #endregion

        #region Analysis Method
        /// <summary>
        /// Queries the GazeZoneAnalyzer for current proportions, filters by threshold,
        /// and logs significant gaze distribution results to the console.
        /// </summary>
        private void PerformGazeAnalysis()
        {
            if (gazeZoneAnalyzer == null)
                return;

            // Retrieve per-object gaze proportions
            var proportions = gazeZoneAnalyzer.AnalyzeGazeZone();

            // Filter objects meeting the minimum threshold and sort by descending proportion
            var significantObjects = proportions
                .Where(kvp => kvp.Value >= minimumProportionThreshold)
                .OrderByDescending(kvp => kvp.Value);

            Debug.Log($"Gaze Zone Analysis: {proportions.Count} objects in zone");
            
            if (significantObjects.Any())
            {
                Debug.Log("=== Gaze Zone Analysis ===");
                foreach (var kvp in significantObjects)
                {
                    string objectName = kvp.Key.name;
                    float percentage = kvp.Value * 100f;
                    Debug.Log($"{objectName}: {percentage:F1}% of gaze zone");
                }
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Provides the current gaze proportions for all objects from the GazeZoneAnalyzer.
        /// </summary>
        /// <returns>
        /// Dictionary mapping each GameObject to its fraction of the gaze zone.
        /// </returns>
        public Dictionary<GameObject, float> GetCurrentGazeProportions()
        {
            return gazeZoneAnalyzer != null
                ? gazeZoneAnalyzer.AnalyzeGazeZone()
                : new Dictionary<GameObject, float>();
        }
        #endregion
    }
}
