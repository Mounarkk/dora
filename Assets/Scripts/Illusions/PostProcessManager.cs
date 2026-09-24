using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using EyeTracking;
using EyeTracking.ConfusionZoneAnalyzer;
using HeadTracking;

namespace Illusions
{
    /// <summary>
    /// Manages post-processing effects and their real-time updates based on gaze data.
    /// Coordinates effect parameters with user attention patterns and provides effect lifecycle control.
    /// </summary>
    public class PostProcessManager : MonoBehaviour
    {
        /// <summary>
        /// Available post-processing effects
        /// </summary>
        public enum PostProcessEffect 
        { 
            None, 
            DepthOfField, 
            Vignette, 
            ChromaticAberration,
            Bloom,
            LensDistortion,
            DirectionalLightRotation
        }
        
        #region Inspector Fields
        /// <summary>
        /// Unity volume containing post-processing effects for visual illusions.
        /// </summary>
        [SerializeField] private Volume volume;

        /// <summary>
        /// Main camera reference for depth calculations and view-based effects.
        /// </summary>
        [SerializeField] private Camera mainCamera;

        /// <summary>
        /// Gaze detection system providing attention data for gaze-responsive effects.
        /// </summary>
        [SerializeField] private GazeManager gazeDetector;

        /// <summary>
        /// Smoothing factor for gradual focus distance transitions.
        /// </summary>
        [SerializeField] private float smoothSpeed = 5f;

        /// <summary>
        /// Current chromatic aberration intensity applied to the camera lens simulation.
        /// </summary>
        [SerializeField] private float currentAberrationIntensity;

        /// <summary>
        /// Edge smoothness transition of the vignette effect.
        /// </summary>
        [SerializeField][Range(0, 1)] private float vignetteSmoothness;

        /// <summary>
        /// Camera aperture setting for depth of field calculations.
        /// </summary>
        [SerializeField] private float aperture;

        /// <summary>
        /// Focal length parameter for realistic depth of field simulation.
        /// </summary>
        [SerializeField][Range(0, 350)] private float focalLength;

        /// <summary>
        /// Speed of vignette position adjustments based on gaze or head movement.
        /// </summary>
        [SerializeField][Range(1, 20)] private float vignetteSmoothingSpeed = 8f;

        /// <summary>
        /// Current bloom effect intensity for bright light simulation.
        /// </summary>
        [SerializeField][Range(0f, 10f)] private float currentBloomIntensity = 1f;

        /// <summary>
        /// Brightness threshold above which bloom effect activates.
        /// </summary>
        [SerializeField][Range(0.1f, 10f)] private float bloomThreshold = 1f;

        /// <summary>
        /// Current lens distortion intensity for barrel/pincushion effects.
        /// </summary>
        [SerializeField][Range(-1f, 1f)] private float currentDistortionIntensity = 0.5f;

        /// <summary>
        /// Analyzer providing depth range data for focus calculations based on gaze confusion zones.
        /// </summary>
        [SerializeField] private DepthConfusionZoneAnalyser depthConfusionZoneAnalyser;

        /// <summary>
        /// Main directional light for environment lighting control.
        /// </summary>
        [SerializeField] private Light directionalLight;

        /// <summary>
        /// Multiplier for light rotation speed in dynamic lighting effects.
        /// </summary>
        [SerializeField] private float lightRotationMultiplier = 6f;

        /// <summary>
        /// Head position controller for head-tracking based depth of field adjustments.
        /// </summary>
        private HeadPositionController headPositionController;
        #endregion

        #region Private Fields
        // Internal effect management
        private VolumeProfile profile;
        private DepthOfField depthOfField;
        private Vignette vignette;
        private ChromaticAberration chromaticAberration;
        private Bloom bloom;
        private LensDistortion lensDistortion;
        private readonly HashSet<PostProcessEffect> activeEffects = new HashSet<PostProcessEffect>();
        private Dictionary<PostProcessEffect, VolumeComponent> effectComponents;
        private Dictionary<PostProcessEffect, Action> effectUpdates;
        private ProcessingHelper helper;
        #endregion

        /// <summary>
        /// Initializes effect system components and dependencies
        /// </summary>
        private void Start()
        {
            helper = new ProcessingHelper();
            InitializeComponents();
            InitializeEffects();
            InitializeEffectDictionaries();
        }

        /// <summary>
        /// Updates active post-processing effects each frame.
        /// Iterates through enabled effects and executes their update methods.
        /// </summary>
        private void Update()
        {
            if (gazeDetector == null) return;

            foreach (var effect in activeEffects)
            {
                if (effectUpdates.TryGetValue(effect, out Action updateAction))
                {
                    updateAction?.Invoke();
                }
            }
        }

        /// <summary>
        /// Initializes core components with fallback search
        /// </summary>
        private void InitializeComponents()
        {
            if (!mainCamera) mainCamera = Camera.main;
            if (!volume) volume = GetComponent<Volume>();

            if (volume != null && volume.profile != null)
            {
                profile = volume.profile;
            }
            else
            {
                Debug.LogError("Volume component or profile missing!");
                enabled = false;
            }

            if (!gazeDetector) gazeDetector = FindAnyObjectByType<GazeManager>();
        }

        /// <summary>
        /// Acquires effect components from volume profile and resets to default state
        /// </summary>
        private void InitializeEffects()
        {
            profile.TryGet(out depthOfField);
            profile.TryGet(out vignette);
            profile.TryGet(out chromaticAberration);
            profile.TryGet(out lensDistortion);
            profile.TryGet(out bloom);
            
            DisableAllEffects();
        }

        /// <summary>
        /// Creates effect management dictionaries:
        /// 1. effectComponents: Links effect types to VolumeComponents
        /// 2. effectUpdates: Maps effects to their update methods
        /// </summary>
        private void InitializeEffectDictionaries()
        {
            effectComponents = new Dictionary<PostProcessEffect, VolumeComponent>
            {
                { PostProcessEffect.DepthOfField, depthOfField },
                { PostProcessEffect.Vignette, vignette },
                { PostProcessEffect.ChromaticAberration, chromaticAberration },
                { PostProcessEffect.Bloom, bloom },
                { PostProcessEffect.LensDistortion, lensDistortion },
            };

            effectUpdates = new Dictionary<PostProcessEffect, Action>
            {
                { PostProcessEffect.DepthOfField, UpdateDepthOfFieldEffect },
                { PostProcessEffect.Vignette, UpdateVignetteEffect },
                { PostProcessEffect.ChromaticAberration, UpdateChromaticAberrationEffect },
                { PostProcessEffect.Bloom , UpdateBloomEffect },
                { PostProcessEffect.LensDistortion, UpdateLensDistortionEffect },
                { PostProcessEffect.DirectionalLightRotation, UpdateDirectionalLightEffect}
            };
        }

        /// <summary>
        /// Activates a specific post-processing effect
        /// </summary>
        /// <param name="effect">Effect to enable (None clears all effects)</param>
        public void EnableEffect(PostProcessEffect effect)
        {
            if (effect == PostProcessEffect.None)
            {
                DisableAllEffects();
                return;
            }

            if (!activeEffects.Contains(effect))
            {
                activeEffects.Add(effect);
                if (effectComponents.TryGetValue(effect, out VolumeComponent component))
                {
                    SetEffectActive(component, true);
                }
            }
        }

        /// <summary>
        /// Resets all effects to default inactive state
        /// </summary>
        public void DisableAllEffects()
        {
            foreach (var effect in activeEffects)
            {
                if (effectComponents.TryGetValue(effect, out VolumeComponent component))
                {
                    SetEffectActive(component, false);
                }
            }
            activeEffects.Clear();
        }

        /// <summary>
        /// Updates depth of field effect parameters based on depth analysis from confusion zones.
        /// Calculates optimal focus settings using the minimum and maximum linear depths detected
        /// within the gaze confusion zone to create realistic bokeh focusing effects.
        /// </summary>
        /// <remarks>
        /// - Uses depth range from confusion zone analyzer to determine focus parameters
        /// - Calculates required aperture for desired depth of field coverage
        /// - Applies bokeh mode with 6-blade aperture for realistic blur characteristics
        /// - Focus distance is set to the midpoint between minimum and maximum depths
        /// </remarks>
       private void UpdateDepthOfFieldEffect()
        {
            if (depthOfField == null || depthConfusionZoneAnalyser == null)
                return;

            float sn = depthConfusionZoneAnalyser.minLinearDepth;  // Near focus limit
            if (sn>= 5f) sn = sn-5f; 
            float sf = depthConfusionZoneAnalyser.maxLinearDepth +5;  // Far focus limit
            float sm = (sf + sn) / 2f; // Midpoint focus distance
            float apertureRequired = focalLength * focalLength * (sn - sf) / (sm * sm * 2f * 3.5e-6f); 
            depthOfField.mode.value           = DepthOfFieldMode.Bokeh;
            depthOfField.focusDistance.value  = sm;
            depthOfField.focalLength.value    = focalLength;
            depthOfField.aperture.value       = apertureRequired;
            depthOfField.bladeCount.value     = 6;
            depthOfField.bladeCurvature.value = 0.6f;
            depthOfField.bladeRotation.value  = 0f;
        }

        /// <summary>
        /// Updates vignette effect center position to follow user's gaze.
        /// Smoothly interpolates the vignette center based on filtered gaze position
        /// to create a dynamic spotlight effect that follows visual attention.
        /// </summary>
        private void UpdateVignetteEffect()
        {
            if (vignette == null || gazeDetector == null) return;

            // Calculate target position in normalized screen coordinates
            Vector2 targetCenter = helper.ScreenToNormalized(gazeDetector.FilteredGazePosition);

            // Smoothly interpolate position
            vignette.center.value = Vector2.Lerp(
                vignette.center.value,
                targetCenter,
                Time.deltaTime * vignetteSmoothingSpeed
            );
            vignette.rounded.value = true;
        }

        /// <summary>
        /// Updates chromatic aberration intensity based on gaze distance from screen center.
        /// Simulates peripheral vision chromatic distortion by reducing aberration intensity
        /// when gaze is closer to the screen center and increasing it toward the edges.
        /// </summary>
        private void UpdateChromaticAberrationEffect()
        {
            if (chromaticAberration == null || gazeDetector == null || mainCamera == null)
                return;

            Vector2 gazeUV = helper.ScreenToNormalized(gazeDetector.FilteredGazePosition);

            Vector2 screenCenterUV = new Vector2(0.5f, 0.5f);
            float rawEccentricity = Vector2.Distance(gazeUV, screenCenterUV);

            const float maxEccentricity = 0.5f;
            float normalizedEccentricity = Mathf.Clamp01(rawEccentricity / maxEccentricity);
            float targetIntensity = 1f - normalizedEccentricity;
            
            currentAberrationIntensity = Mathf.Lerp(currentAberrationIntensity, targetIntensity, Time.deltaTime * smoothSpeed);
            chromaticAberration.intensity.value = currentAberrationIntensity;
        }
        
        /// <summary>
        /// Updates bloom effect intensity based on the average depth within the gaze confusion zone.
        /// Maps the depth range from the confusion zone analyzer to bloom intensity values,
        /// creating depth-responsive lighting effects that enhance visual depth perception.
        /// </summary>
        /// <remarks>
        /// - Uses average of minimum and maximum depths from confusion zone analysis
        /// - Maps depth range (near to far clip plane) to bloom intensity range (0-200)
        /// - Applies smooth transitions to prevent jarring visual changes
        /// - Bloom threshold remains constant while intensity varies with depth
        /// </remarks>
        private void UpdateBloomEffect()
        {
            if (bloom == null || depthConfusionZoneAnalyser == null) return;

            // Calculate average depth from confusion zone analyzer
            float avgDepth = (depthConfusionZoneAnalyser.minLinearDepth + depthConfusionZoneAnalyser.maxLinearDepth) * 0.5f;

            // Define depth range for bloom mapping (adjust as needed)
            float minDepth = mainCamera.nearClipPlane;
            float maxDepth = mainCamera.farClipPlane;

            // Clamp and map average depth to bloom intensity range
            float clampedDepth = Mathf.Clamp(avgDepth, minDepth, maxDepth);
            float targetBloom = LinearMap(clampedDepth, minDepth, maxDepth, 0f, 200f);

            // Smoothly transition bloom intensity
            currentBloomIntensity = Mathf.Lerp(
                currentBloomIntensity,
                targetBloom,
                Time.deltaTime * smoothSpeed
            );
            Debug.Log($"Bloom Threshold: {bloomThreshold}, Bloom Intensity: {currentBloomIntensity}");

            // Apply values to bloom effect
            bloom.threshold.value = bloomThreshold;
            bloom.intensity.value = currentBloomIntensity;
        }

        /// <summary>
        /// Updates lens distortion effect based on gaze position relative to screen center.
        /// Creates dynamic barrel/pincushion distortion that intensifies as gaze moves away
        /// from the center, simulating peripheral vision optical distortions.
        /// </summary>
        /// <remarks>
        /// - Calculates distance from screen center to current gaze position
        /// - Maps distance to distortion intensity using configurable ranges
        /// - Applies smooth interpolation to prevent abrupt visual transitions
        /// - Negative intensity values create pincushion effect, positive creates barrel effect
        /// </remarks>
        private void UpdateLensDistortionEffect()
        {
            float distanceFromCenter = (helper.ScreenToNormalized(gazeDetector.FilteredGazePosition) * 2f - new Vector2(1f, 1f)).magnitude;

            // Map distance to distortion strength, adjust scale as needed
            float minDist = 0f, maxDist = 0.9f;
            float minIntensity = -0.6f, maxIntensity = 0.6f;

            float distortion = LinearMap(distanceFromCenter, minDist, maxDist, minIntensity, maxIntensity);
            currentDistortionIntensity = Mathf.Lerp(currentDistortionIntensity, distortion, Time.deltaTime * smoothSpeed);
            
            lensDistortion.intensity.value = currentDistortionIntensity;
        }
        
        /// <summary>
        /// Updates directional light rotation to follow camera/head movement.
        /// Synchronizes environmental lighting direction with head tracking data
        /// to maintain consistent illumination relative to the user's viewpoint.
        /// </summary>
        private void UpdateDirectionalLightEffect()
        {
            if (directionalLight == null || mainCamera == null) return;

            // Get the current camera's Y rotation (yaw)
            float cameraYRotation = mainCamera.transform.eulerAngles.y;
    
            // Calculate target light rotation
            float targetLightY = cameraYRotation * lightRotationMultiplier;
    
            // Create target rotation (keeping X and Z as they are, or set to 0)
            Vector3 targetRotation = new Vector3(0, -targetLightY, 0);
    
            // Smooth the rotation transition
            directionalLight.transform.rotation = Quaternion.Euler(targetRotation);
        }

        /// <summary>
        /// Toggles a volume component's active state
        /// </summary>
        /// <param name="component">Effect component to modify</param>
        /// <param name="active">Desired activation state</param>
        private void SetEffectActive(VolumeComponent component, bool active)
        {
            if (component != null)
            {
                component.active = active;
            }
        }
        
        // Helper: Linear Mapping Function
        private float LinearMap(float input, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            // Clamp input for safety
            if (input < inputMin) input = inputMin;
            if (input > inputMax) input = inputMax;
            // Linear remap formula
            return outputMin + (input - inputMin) * (outputMax - outputMin) / (inputMax - inputMin);
        }

    }
}