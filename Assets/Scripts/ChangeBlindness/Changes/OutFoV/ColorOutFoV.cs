using UnityEngine;

namespace ChangeBlindness.Changes.OutFoV
{
    /// <summary>
    /// OutFoV experiment that changes an object's color when it's outside the field of view.
    /// The color change occurs randomly when the object is not visible, creating a noticeable difference
    /// when the user looks back at the object after it has been modified while out of sight.
    /// </summary>
    public class ColorOutFoV : BaseExperiment
    {
        /// <summary>
        /// The game object whose color will be changed during the experiment.
        /// This object serves as both the trigger for field of view detection and the target of the color change.
        /// Must have a Renderer component with a material that supports color modification.
        /// </summary>
        [SerializeField] private GameObject targetObject;
        
        /// <summary>
        /// Cached reference to the target object's Renderer component for efficient color manipulation.
        /// Used to apply color changes without repeatedly calling GetComponent.
        /// </summary>
        private Renderer targetObjectRenderer;
        
        /// <summary>
        /// The original color of the target object's material before any changes are applied.
        /// Used for reverting the object back to its initial appearance.
        /// </summary>
        private Color originalColor;

        /// <summary>
        /// Unity Start callback. Stores the original material color and caches the Renderer component.
        /// Establishes the baseline color that will be used for restoration during revert operations.
        /// </summary>
        private void Start()
        {
            targetObjectRenderer = targetObject.GetComponent<Renderer>();
            originalColor = targetObjectRenderer.material.color;
        }

        /// <summary>
        /// Applies a random color change to the target object's material.
        /// Generates a new color with random RGB values and applies it to the object.
        /// Sets the IsChanged flag to true to track the experiment state.
        /// </summary>
        public override void ApplyChange()
        {
            // Create a random color and applies it to the object when the user can't see it
            Color newColor = new Color(Random.value, Random.value, Random.value);
            targetObjectRenderer.material.SetColor("_BaseColor", newColor);
            IsChanged = true;
            Shader.SetGlobalInt("_ShouldChange", 1);
        }

        /// <summary>
        /// Reverts the target object's material back to its original color.
        /// Restores the color that was stored during the Start method.
        /// Sets the IsChanged flag to false to indicate the original state is restored.
        /// </summary>
        public override void RevertChange()
        {
            targetObjectRenderer.material.SetColor("_BaseColor", originalColor);
            IsChanged = false;
            Shader.SetGlobalInt("_ShouldChange", 0);
        }
        
        /// <summary>
        /// Determines if the target object is currently visible within the camera's field of view.
        /// For OutFoV experiments, the change should only be applied when this returns false,
        /// meaning the object is outside the visible area and the color change won't be immediately noticed.
        /// </summary>
        /// <param name="camera">The camera used for field of view calculations.</param>
        /// <returns>True if the object is visible in the camera's field of view, false otherwise.</returns>
        /// <remarks>
        /// The OutFoV experiment logic will trigger color changes when this returns false,
        /// ensuring the color modification happens while the object is not being observed.
        /// </remarks>
        public override bool IsInFieldOfView(Camera camera)
        {
            Vector3 screenPoint = camera.WorldToViewportPoint(targetObject.transform.position);
            bool isVisible = screenPoint.z > 0 && 
                             screenPoint.x > 0 && screenPoint.x < 1 && 
                             screenPoint.y > 0 && screenPoint.y < 1;
            
            return isVisible;
        }
    }
}