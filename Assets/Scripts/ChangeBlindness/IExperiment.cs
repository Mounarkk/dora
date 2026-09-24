
using ChangeBlindness;
using UnityEngine;

namespace ChangeBlindness
{
    /// <summary>
    /// Contract interface for objects that can undergo changes in change blindness experiments.
    /// Defines the essential capabilities required for an object to participate in the experiment system,
    /// including change application, state management, and spatial awareness.
    /// </summary>
    public interface IExperiment
    {
        /// <summary>
        /// Applies the specific change behavior defined by the implementing class.
        /// This method triggers the transformation that will be tested for detection in the experiment.
        /// </summary>
        void ApplyChange();
        
        /// <summary>
        /// Reverts the object back to its original state before any changes were applied.
        /// Ensures the object can be reset for subsequent experimental trials.
        /// </summary>
        void RevertChange();
        
        /// <summary>
        /// Determines if this object is currently visible within the specified camera's field of view.
        /// Critical for determining when changes should be applied based on visibility conditions.
        /// </summary>
        /// <param name="camera">The camera to test visibility against.</param>
        /// <returns>True if the object is visible in the camera's field of view, false otherwise.</returns>
        bool IsInFieldOfView(Camera camera);
        
        /// <summary>
        /// Gets the type of change this object performs.
        /// Determines the experimental conditions under which the change should be triggered.
        /// </summary>
        /// <value>The ChangeType enum value defining the change behavior and visibility requirements.</value>
        ChangeType ChangeType { get; }
    }
}