using UnityEngine;

namespace Illusions
{
    /// <summary>
    /// Manages UI interactions for visual illusion effects.
    /// Handles panel visibility and delegates commands to the IllusionManager.
    /// </summary>
    public class IllusionUI : MonoBehaviour
    {

        /// <summary>
        /// Manager controlling visual illusion effects and UI state coordination.
        /// </summary>
        [SerializeField] private EffectsManager illusionManager;

        /// <summary>
        /// UI panel containing effect controls and buttons for user interaction.
        /// </summary>
        [SerializeField] private GameObject effectsPanel;

        /// <summary>
        /// Toggles visibility of the effect selection panel
        /// </summary>
        public void ToggleEffectsPanel()
        {
            effectsPanel.SetActive(!effectsPanel.activeSelf);
        }

        /// <summary>
        /// Activates a specific visual effect and hides the UI panel
        /// </summary>
        /// <param name="effectName">Name of the effect to activate (must match IllusionManager's effect names)</param>
        public void EnableEffect(string effectName)
        {
            illusionManager.EnableEffect(effectName);
            effectsPanel.SetActive(false);
        }

        /// <summary>
        /// Resets visualization to the default state:
        /// 1. Disables all active effects
        /// 2. Restores default UI visibility
        /// </summary>
        public void DisableAllEffects()
        {
            illusionManager.DisableAllEffects();
        }

        /// <summary>
        /// Handles application exit procedure:
        /// 1. Cleans up visual effects
        /// 2. Closes the application
        /// </summary>
        public void QuitApplication()
        {
            illusionManager.QuitApplication();
        }
    }
}