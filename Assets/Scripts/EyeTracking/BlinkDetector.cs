using System.Collections.Generic;
using PupilLabs;
using UnityEngine;

namespace EyeTracking
{
    /// <summary>
    /// Detects eye blinks using Pupil Labs eye tracking data.
    /// Subscribes to blink events and manages cooldown between valid blink detections.
    /// </summary>
    public class BlinkDetector : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Dependencies")]
        [Tooltip("Controller for managing Pupil Labs subscriptions")]
        public SubscriptionsController subscriptionsController;

        [Header("Blink Detection Settings")]
        [Tooltip("Minimum time between consecutive blink registrations (seconds)")]
        [SerializeField] private float blinkCooldown = 0.2f;

        [Header("Debug Info")]
        [Tooltip("Total number of blinks detected during session")]
        [HideInInspector] public int blinkCount;
        [HideInInspector] public bool isBlinking = false;

        #endregion

        #region Private Fields

        private RequestController requestCtrl;
        private float lastBlinkTime;

        #endregion

        #region Unity Lifecycle

        /// <summary>
        /// Initializes connection to Pupil Labs request controller
        /// </summary>
        private void Awake()
        {
            requestCtrl = subscriptionsController.requestCtrl;
        }

        /// <summary>
        /// Handle component enables event:
        /// 1. Subscribes to connection events
        /// 2. Starts blink subscription if already connected
        /// </summary>
        private void OnEnable()
        {
            requestCtrl.OnConnected += StartBlinkSubscription;

            if (requestCtrl.IsConnected)
            {
                StartBlinkSubscription();
            }
        }

        /// <summary>
        /// Handles component disable event:
        /// 1. Unsubscribes from connection events
        /// 2. Stops active blink subscription
        /// </summary>
        private void OnDisable()
        {
            requestCtrl.OnConnected -= StartBlinkSubscription;

            if (requestCtrl.IsConnected)
            {
                StopBlinkSubscription();
            }
        }

        /// <summary>
        /// Unity Update loop - called once per frame
        /// Manages the single-frame blink signal by resetting the blink state
        /// </summary>
        private void Update()
        {
            // Reset the blink flag after one frame to ensure isBlinking 
            if (isBlinking)
            {
                isBlinking = false;
            }
        }

        #endregion

        #region Blink Detection Logic

        /// <summary>
        /// Starts receiving blink notifications from Pupil Labs
        /// 1. Subscribes to "blinks" data stream
        /// 2. Activates blink detection plugin
        /// </summary>
        private void StartBlinkSubscription()
        {
            Debug.Log("Starting blink detection subscription");

            subscriptionsController.SubscribeTo("blinks", CustomReceiveData);

            requestCtrl.StartPlugin(
                "Blink_Detection",
                new Dictionary<string, object>
                {
                    { "history_length", 0.2f },
                    { "onset_confidence_threshold", 0.3f },
                    { "offset_confidence_threshold", 0.4f }
                }
            );
        }

        /// <summary>
        /// Stops blink detection services
        /// 1. Unsubscribes from "blinks" data stream
        /// 2. Deactivates blink detection plugin
        /// </summary>
        private void StopBlinkSubscription()
        {
            Debug.Log("Stopping blink detection subscription");

            requestCtrl.StopPlugin("Blink_Detection");
            subscriptionsController.UnsubscribeFrom("blinks", CustomReceiveData);
        }

        /// <summary>
        /// Processes incoming blink detection data
        /// </summary>
        /// <param name="topic">Event source (should be "blinks")</param>
        /// <param name="dictionary">Contains detection data including:
        /// - timestamp: Event time
        /// - type: "onset" or "offset"</param>
        /// <param name="thirdFrame">Unused image data</param>
        private void CustomReceiveData(string topic, Dictionary<string, object> dictionary, byte[] thirdFrame = null)
        {
            if (dictionary.ContainsKey("timestamp") && dictionary["type"].ToString() == "onset")
            {
                var currentTime = Time.time;

                // Apply cooldown filter
                if (currentTime - lastBlinkTime >= blinkCooldown)
                {
                    // This blinking boolean will be true during one frame, to be a sort of signal of a blink event
                    isBlinking = true;
                    blinkCount++;
                    lastBlinkTime = currentTime;
                }
            }
        }
        #endregion
    }
}
