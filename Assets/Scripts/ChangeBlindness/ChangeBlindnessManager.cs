using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChangeBlindness
{
    /// <summary>
    /// Central manager for coordinating change blindness experiments.
    /// Manages the experiment lifecycle, monitors changeable objects, and triggers changes
    /// based on field of view conditions, distance constraints, and timing parameters.
    /// </summary>
    public class ChangeBlindnessManager : MonoBehaviour
    {
        /// <summary>
        /// Primary camera used for field of view calculations and distance measurements.
        /// </summary>
        [Header("Scene references")]
        [SerializeField] private Camera mainCamera;
        
        /// <summary>
        /// Collection of all objects in the scene that can undergo changes during the experiment.
        /// </summary>
        [SerializeField] public List<BaseExperiment> experiments;
        
        /// <summary>
        /// UI button to initiate the experiment (currently unused in favor of keyboard input).
        /// </summary>
        [Header("UI elements")]
        [SerializeField] private Button startExperimentButton;
        
        /// <summary>
        /// UI button to reset the experiment state (currently unused in favor of keyboard input).
        /// </summary>
        [SerializeField] private Button resetButton;
        
        /// <summary>
        /// Flag indicating whether an experiment session is currently active.
        /// </summary>
        private bool isExperimentRunning;
        
        /// <summary>
        /// List of objects that have already been changed during the current experiment session.
        /// Prevents the same object from being changed multiple times.
        /// </summary>
        private readonly List<BaseExperiment> activeChanges = new List<BaseExperiment>();
        
        /// <summary>
        /// Timestamp of the last change that was triggered used to enforce minimum time intervals
        /// between consecutive changes as defined in the experiment configuration.
        /// </summary>
        private float lastChangeTime;
        
        /// <summary>
        /// Minimum time interval (in seconds) that must elapse between consecutive changes.
        /// </summary>
        [SerializeField] private float minTimeBetweenChanges;

        /// <summary>
        /// Unity Start callback. Initializes the main camera reference if not assigned.
        /// </summary>
        private void Start()
        {
            if (!mainCamera) mainCamera = Camera.main;
        }

        /// <summary>
        /// Unity Update callback. Handles experiment state and continuously monitors
        /// changeable objects for triggering conditions when an experiment is running.
        /// </summary>
        private void Update()
        {
            if (!isExperimentRunning) return;
            
            foreach (var changeableObject in experiments)
            {
                if (activeChanges.Contains(changeableObject)) continue;
                bool isInFieldOfView = changeableObject.IsInFieldOfView(mainCamera);
                Debug.Log($"Object {changeableObject.name} in FoV: {isInFieldOfView}");

                // Check if the change type matches the current FoV condition
                bool isValidFoVCondition = (changeableObject.IsInFoVChange && isInFieldOfView) ||
                                        (!changeableObject.IsInFoVChange && !isInFieldOfView);

                // Adapt to the config
                if (isValidFoVCondition && Time.time - lastChangeTime >= minTimeBetweenChanges)
                {
                    TriggerChange(changeableObject);
                }
            }
        }

        /// <summary>
        /// Applies a change to the specified object and adds it to the active changes list.
        /// Updates the last change time to enforce timing constraints for subsequent changes.
        /// </summary>
        /// <param name="experiment">The object to apply the change to.</param>
        private void TriggerChange(BaseExperiment experiment)
        {
            experiment.ApplyChange();
            activeChanges.Add(experiment);
            lastChangeTime = Time.time;
        }

        /// <summary>
        /// Initiates a new experiment session.
        /// Resets all objects to their original state, clears active changes,
        /// and sets the experiment as running.
        /// </summary>
        public void StartExperiment()
        {
            Debug.Log("Experiment started");
            isExperimentRunning = true;
            activeChanges.Clear();
            ResetAllObjects();
            lastChangeTime = Time.time;
        }

        /// <summary>
        /// Stops the current experiment session and resets all objects to their original state.
        /// Clears the active changes list and marks the experiment as not running.
        /// </summary>
        public void ResetExperiment()
        {
            Debug.Log("Experiment reset");
            isExperimentRunning = false;
            ResetAllObjects();
            activeChanges.Clear();
        }

        /// <summary>
        /// Reverts all changeable objects back to their original state.
        /// Called during experiment start and reset operations.
        /// </summary>
        private void ResetAllObjects()
        {
            foreach (var changeableObject in experiments)
            {
                changeableObject.RevertChange();
            }
        }
    }
}