using UnityEngine;

namespace HeadTracking
{
    /// <summary>
    /// Provides smooth head position tracking with motion continuation during signal loss.
    /// Uses exponential smoothing for velocity estimation and predictive extrapolation when tracking fails.
    /// </summary>
    public class HeadPositionController : MonoBehaviour
    {
        #region Inspector Fields
        /// <summary>
        /// Speed factor for position interpolation during normal tracking.
        /// Higher values = faster convergence to tracked position.
        /// </summary>
        [SerializeField] private float smoothingSpeed = 10f;
        
        /// <summary>
        /// Exponential smoothing factor for velocity estimation (0-1).
        /// Higher values make velocity more responsive to recent changes.
        /// </summary>
        [SerializeField] private float velocityAlpha = 0.3f;

        /// <summary>
        /// Enables predictive motion continuation during tracking loss.
        /// </summary>
        [SerializeField] private bool useMotionContinuation = true;
        
        /// <summary>
        /// Maximum duration to continue motion prediction before stopping.
        /// </summary>
        [SerializeField] private float maxContinuationTime = 1.5f;
        
        /// <summary>
        /// Exponential decay rate applied to velocity during tracking loss.
        /// Higher values cause faster deceleration.
        /// </summary>
        [SerializeField] private float velocityDecayRate = 2f;

        /// <summary>
        /// Smoothing speed when rejoining after tracking recovery.
        /// </summary>
        [SerializeField] private float rejoinSmoothingSpeed = 5f;
        
        /// <summary>
        /// Distance threshold to exit rejoining mode and resume normal tracking.
        /// </summary>
        [SerializeField] private float rejoinThreshold = 0.1f;

        /// <summary>
        /// UDP receiver providing raw tracking data from the external head tracking system.
        /// </summary>
        [SerializeField] private UDPReceiver tracker;
        
        /// <summary>
        /// Minimum number of tracking points required for valid tracking data.
        /// </summary>
        [SerializeField] private int requiredPointCount = 3;
        #endregion

        #region Private Fields
        /// <summary>
        /// Last confirmed valid position from the tracker.
        /// </summary>
        private Vector3 lastValidPosition;
        
        /// <summary>
        /// Current smoothed position output by this controller.
        /// </summary>
        private Vector3 currentPosition;
        
        /// <summary>
        /// Exponentially smoothed velocity used for motion continuation.
        /// Calculated as: smoothedVelocity = lerp(smoothedVelocity, instantVelocity, velocityAlpha)
        /// </summary>
        private Vector3 smoothedVelocity;
        
        /// <summary>
        /// Whether we have received at least one valid position from the tracker.
        /// </summary>
        private bool hasValidPosition;
        
        /// <summary>
        /// Whether we're in rejoining mode after tracking recovery.
        /// During rejoining, we smoothly interpolate back to the tracked position.
        /// </summary>
        private bool isRejoining;

        /// <summary>
        /// Timestamp when tracking was lost (Time.time).
        /// Used to calculate continuation duration and apply velocity decay.
        /// </summary>
        private float trackingLostTime;
        #endregion

        #region Public Properties
        /// <summary>
        /// Current smoothed head position in world coordinates.
        /// This is the primary output used by other systems.
        /// </summary>
        public Vector3 Position => currentPosition;
        
        /// <summary>
        /// Whether the tracker is currently providing valid data.
        /// Based on UDP connection status and minimum required tracking points.
        /// </summary>
        public bool IsTrackingActive => tracker != null && tracker.NbPointsTracked == requiredPointCount;
        #endregion

        #region Unity Lifecycle
        /// <summary>
        /// Initializes tracker reference and resets the tracking state.
        /// </summary>
        private void Start()
        {
            if (tracker == null)
                tracker = FindAnyObjectByType<UDPReceiver>();

            ResetTracking();
        }

        /// <summary>
        /// Main update loop handling tracking state transitions and position updates.
        /// Flow: Check tracking status → Process position data → Apply smoothing/continuation
        /// </summary>
        private void Update()
        {
            if (tracker == null) return;

            float currentTime = Time.time;
            float deltaTime = Time.deltaTime;

            if (IsTrackingActive)
            {
                // Get raw position (no scaling applied here - handled by tracker)
                Vector3 rawPosition = tracker.Position;
                Vector3 scaledPosition = new Vector3(
                    rawPosition.x,
                    rawPosition.y,
                    rawPosition.z
                );
                
                if (!hasValidPosition)
                {
                    InitializeTracking(scaledPosition, currentTime);
                }
                else if (isRejoining)
                {
                    HandleRejoining(scaledPosition, deltaTime);
                }
                else
                {
                    UpdateNormalTracking(scaledPosition, currentTime, deltaTime);
                }
            }
            else
            {
                HandleTrackingLoss(currentTime, deltaTime);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets up initial tracking state when first valid data is received.
        /// </summary>
        private void InitializeTracking(Vector3 scaledPosition, float currentTime)
        {
            lastValidPosition = scaledPosition;
            currentPosition = scaledPosition;
            hasValidPosition = true;
            isRejoining = false;
            smoothedVelocity = Vector3.zero;
            trackingLostTime = 0f;
        }

        /// <summary>
        /// Updates position and velocity during normal tracking operation.
        /// Calculates instantaneous velocity and applies exponential smoothing.
        /// </summary>
        private void UpdateNormalTracking(Vector3 scaledPosition, float currentTime, float deltaTime)
        {
            // Calculate instantaneous velocity from position change
            Vector3 positionDelta = scaledPosition - lastValidPosition;
            Vector3 instantVelocity = positionDelta / deltaTime;
            
            // Apply exponential smoothing: new = old * (1-α) + instant * α
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, instantVelocity, velocityAlpha);
            
            // Update tracking state
            lastValidPosition = scaledPosition;
            trackingLostTime = 0f; // Reset tracking lost time
            
            // Smoothly interpolate current position toward tracked position
            currentPosition = Vector3.Lerp(currentPosition, lastValidPosition, deltaTime * smoothingSpeed);
        }

        /// <summary>
        /// Handles smooth rejoining when tracking resumes after signal loss.
        /// Continues velocity updates while interpolating back to tracked position.
        /// </summary>
        private void HandleRejoining(Vector3 scaledPosition, float deltaTime)
        {
            // Continue updating velocity even during rejoining
            Vector3 positionDelta = scaledPosition - lastValidPosition;
            Vector3 instantVelocity = positionDelta / deltaTime;
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, instantVelocity, velocityAlpha);
            
            // Update the last valid position and time
            lastValidPosition = scaledPosition;
            trackingLostTime = 0f;
            
            // Smooth position interpolation toward tracked position
            currentPosition = Vector3.Lerp(currentPosition, scaledPosition, deltaTime * rejoinSmoothingSpeed);
            
            // Check if we're close enough to exit rejoining mode
            if (Vector3.Distance(currentPosition, scaledPosition) < rejoinThreshold)
            {
                isRejoining = false;
            }
        }

        /// <summary>
        /// Handles motion continuation during tracking loss using exponential velocity decay.
        /// Formula: position += velocity * exp(-decayRate * timeLost) * deltaTime
        /// </summary>
        private void HandleTrackingLoss(float currentTime, float deltaTime)
        {
            if (!hasValidPosition) return;

            // Initialize tracking lost time on the first loss
            if (trackingLostTime == 0f)
                trackingLostTime = currentTime;

            float timeSinceLost = currentTime - trackingLostTime;
            
            if (useMotionContinuation && timeSinceLost < maxContinuationTime)
            {
                // Apply exponential decay to velocity: v(t) = v0 * e^(-λt)
                float decayFactor = Mathf.Exp(-velocityDecayRate * timeSinceLost);
                Vector3 currentVelocity = smoothedVelocity * decayFactor;
                
                // Extrapolate position using decayed velocity
                Vector3 extrapolation = currentVelocity * deltaTime;
                currentPosition += extrapolation;
            }

            // Prepare for rejoining when tracking returns
            if (!isRejoining)
                isRejoining = true;
        }

        /// <summary>
        /// Resets all tracking states to initial values.
        /// </summary>
        private void ResetTracking()
        {
            lastValidPosition = Vector3.zero;
            currentPosition = Vector3.zero;
            smoothedVelocity = Vector3.zero;
            hasValidPosition = false;
            isRejoining = false;
            trackingLostTime = 0f;
        }
        #endregion
    }
}