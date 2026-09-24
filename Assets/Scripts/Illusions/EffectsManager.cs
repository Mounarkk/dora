using UnityEngine;

namespace Illusions
{
    /// <summary>
    /// Manages visual illusion effects and UI state coordination.
    /// Controls post-processing effects and handles application lifecycle events.
    /// </summary>
    public class EffectsManager : MonoBehaviour
    {
        #region Inspector Fields
        /// <summary>
        /// Reference to the post-processing effect controller that handles individual visual effects.
        /// Auto-assigned from the same GameObject if not set in inspector.
        /// </summary>
        [SerializeField] private PostProcessManager postProcessManager;

        /// <summary>
        /// UI panel containing effect controls and buttons.
        /// Used to show/hide the effects interface during effect activation.
        /// </summary>
        [SerializeField] private GameObject effectsPanel;
        #endregion

        #region Private Fields
        /// <summary>
        /// 2D canvas element for visual indicators (found by name "Visualization Elements").
        /// Hidden when effects are active to avoid UI clutter.
        /// </summary>
        private Canvas canvasObject;
        
        /// <summary>
        /// 3D world-space indicator object (found by name "3D Crosshair").
        /// Provides spatial reference that's hidden during effect demonstrations.
        /// </summary>
        private GameObject indicator;
        #endregion

        #region Unity Lifecycle
        /// <summary>
        /// Initializes UI elements and post-processing system.
        /// Sets up default visibility states and locates required components.
        /// </summary>
        private void Start()
        {
            // Find UI components by name and initialize state
            canvasObject = GameObject.Find("Visualization Elements").GetComponent<Canvas>();
            indicator = GameObject.Find("3D Crosshair");
            canvasObject.enabled = true;
            indicator.SetActive(true);

            // Auto-get post-process manager if not set in inspector
            if (postProcessManager == null)
            {
                postProcessManager = GetComponent<PostProcessManager>();
            }
            
            postProcessManager.DisableAllEffects();
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Activates a specific visual effect and manages UI visibility.
        /// Hides visualization elements to provide a clean effect demonstration.
        /// Effect names must match PostProcessEffect enum values.
        /// </summary>
        /// <param name="effectName">Name of the effect to enable (case-insensitive enum parsing)</param>
        public void EnableEffect(string effectName)
        {
            // Hide UI elements when effect is active
            canvasObject.enabled = false;
            indicator.SetActive(false);

            // Try to parse and enable the requested effect
            if (System.Enum.TryParse<PostProcessManager.PostProcessEffect>(
                    effectName,
                    true,
                    out var effect))
            {
                postProcessManager.EnableEffect(effect);
            }
            else
            {
                Debug.LogWarning($"Unknown effect name: {effectName}");
            }
        }
        
        /// <summary>
        /// Resets to the default state by disabling all effects and restoring UI visibility.
        /// Returns the system to its initial configuration for normal operation.
        /// </summary>
        public void DisableAllEffects()
        {
            // Restore UI visibility
            canvasObject.enabled = true;
            indicator.SetActive(true);
            
            // Disable all active effects
            postProcessManager.DisableAllEffects();
        }
        
        /// <summary>
        /// Handles clean application shutdown.
        /// Ensures all visual effects are properly reset before terminating the application.
        /// </summary>
        public void QuitApplication()
        {
            DisableAllEffects();
            Application.Quit();
        }
        #endregion
    }
}