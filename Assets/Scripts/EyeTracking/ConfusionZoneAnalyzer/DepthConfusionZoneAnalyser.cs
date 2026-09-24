using UnityEngine;
using System;
using System.Collections.Generic;
using EyeTracking;
using UnityEngine.Rendering;

namespace EyeTracking.ConfusionZoneAnalyzer
{
    /// <summary>
    /// Analyzes depth information in a gaze-based region to detect areas of potential visual confusion.
    /// Uses a compute shader to extract min/max linear depth from the user's gaze region.
    /// </summary>
    public class DepthConfusionZoneAnalyser : MonoBehaviour
    {
        [Header("Dependencies")]
        public ComputeShader depthConfusionZoneShader;
        public Camera mainCamera;
        public Camera depthCamera;
        public GazeManager gazeManager;
        private Texture _cameraDepthTexture;
        private RenderTexture depthRenderTexture;
        private int size = 1;
        private Vector2 gaze = Vector2.zero;
        public float minLinearDepth = 0f;
        public float maxLinearDepth = 0f;
        private ComputeBuffer minMaxBuffer;
        private ProcessingHelper helper;

        private static readonly Color[] planeColors = new Color[] {
            Color.red,       // Near
            Color.green,     // Far
            Color.blue,      // Right
            Color.yellow,    // Left
            Color.magenta,   // Top
            Color.cyan       // Bottom
        };

        #region Initialization

        /// <summary>
        /// Validates inputs, sets up depth render texture, and frustum visualization mesh.
        /// </summary>
        private void Start()
        {
            helper = new ProcessingHelper();
            if (depthConfusionZoneShader == null || mainCamera == null || depthCamera == null)
            {
                Debug.LogError("Missing references: ComputeShader, Material, or Cameras.");
                enabled = false;
                return;
            }

            depthCamera.depthTextureMode = DepthTextureMode.Depth;
            depthRenderTexture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth);
            depthRenderTexture.Create();
            depthCamera.targetTexture = depthRenderTexture;
            depthCamera.enabled = false;
        }

        #endregion

        #region Depth Processing Pipeline

        /// <summary>
        /// Synchronizes camera settings, captures depth, and triggers compute dispatch.
        /// </summary>
        private void LateUpdate()
        {
            SyncDepthCamera();
            RenderDepthTexture();
            Shader.SetGlobalFloat("_GazeRadius", gazeManager.CurrentAccuracyRadius / Screen.width);
            if (gazeManager.CurrentAccuracyRadius > 0f)
                size = Mathf.RoundToInt(gazeManager.CurrentAccuracyRadius + gazeManager.CurrentPrecisionRadius * 3f);
            gaze = gazeManager.confusionZone.Center;
            Vector2 NormalizedFilteredGaze = helper.ScreenToNormalized(gaze);
            Shader.SetGlobalVector("_GazeCenter", NormalizedFilteredGaze);
            SetupBufferAndDispatch();
        }

        /// <summary>
        /// Syncs the depth camera’s transform and projection settings with the main camera.
        /// </summary>
        private void SyncDepthCamera()
        {
            depthCamera.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
            depthCamera.fieldOfView = mainCamera.fieldOfView;
            depthCamera.nearClipPlane = mainCamera.nearClipPlane;
            depthCamera.farClipPlane = mainCamera.farClipPlane;
            depthCamera.aspect = mainCamera.aspect;
        }

        /// <summary>
        /// Triggers depth rendering from the secondary depth camera.
        /// </summary>
        private void RenderDepthTexture()
        {
            depthCamera.Render();
            _cameraDepthTexture = depthRenderTexture;
        }

        #endregion

        #region Compute Shader Logic

        /// <summary>
        /// Sets up compute buffer and shader parameters, dispatches compute shader, and requests GPU readback.
        /// </summary>
        private void SetupBufferAndDispatch()
        {
            minMaxBuffer?.Release();
            minMaxBuffer = new ComputeBuffer(2, sizeof(int));
            int sentinelMax = 0x7F7FFFFF;
            int sentinelMin = unchecked((int)0x80000000);
            minMaxBuffer.SetData(new int[] { sentinelMax, sentinelMin });

            int kernel = depthConfusionZoneShader.FindKernel("CSMain");
            depthConfusionZoneShader.SetBuffer(kernel, "MinMaxBuffer", minMaxBuffer);
            depthConfusionZoneShader.SetTexture(kernel, "_CameraDepthTexture", _cameraDepthTexture);

            Vector4 zParams = Shader.GetGlobalVector("_ZBufferParams");
            depthConfusionZoneShader.SetVector("_MyZBufferParams", zParams);

            int half = size / 2;
            int gazeX = Mathf.Clamp(Mathf.RoundToInt(gaze.x), half, Screen.width - half);
            int gazeY = Mathf.Clamp(Mathf.RoundToInt(gaze.y), half, Screen.height - half);
            depthConfusionZoneShader.SetInts("regionOffset", gazeX - half, gazeY - half);
            depthConfusionZoneShader.SetInts("regionSize", size, size);
            depthConfusionZoneShader.SetFloat("nearPlane", mainCamera.nearClipPlane);
            depthConfusionZoneShader.SetFloat("farPlane", mainCamera.farClipPlane);

            depthConfusionZoneShader.Dispatch(kernel,
                Mathf.CeilToInt(size / 32f),
                Mathf.CeilToInt(size / 32f),
                1);

            AsyncGPUReadback.Request(minMaxBuffer, OnReadbackComplete);
        }

        /// <summary>
        /// Callback for async GPU readback. Extracts min/max linear depth and updates frustum.
        /// </summary>
        /// <param name="request">The GPU readback request.</param>
        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                Debug.LogError("Depth readback failed");
                return;
            }

            var result = request.GetData<int>();
            float computedMin = OrderedIntToFloat(result[0]);
            float computedMax = OrderedIntToFloat(result[1]);

            bool foundValid = result[0] != 0x7F7FFFFF && result[1] != unchecked((int)0x80000000);
            minLinearDepth = foundValid ? computedMin : 0f;
            maxLinearDepth = foundValid ? computedMax : 0f;

            // Update visualization
            Vector3 pNear = mainCamera.ScreenToWorldPoint(new Vector3(gaze.x, gaze.y, minLinearDepth));
            Vector3 pFar = mainCamera.ScreenToWorldPoint(new Vector3(gaze.x, gaze.y, maxLinearDepth));
            Vector3 offN = mainCamera.ScreenToWorldPoint(new Vector3(gaze.x + size, gaze.y, minLinearDepth));
            Vector3 offF = mainCamera.ScreenToWorldPoint(new Vector3(gaze.x + size, gaze.y, maxLinearDepth));
            float rNear = (offN - pNear).magnitude;
            float rFar = (offF - pFar).magnitude;
        }

        #endregion


        #region Utility Methods

        /// <summary>
        /// Converts GPU-ordered int values back into floating-point depths.
        /// </summary>
        private float OrderedIntToFloat(int i)
        {
            const int sentinelMax = 0x7F7FFFFF;
            const int sentinelMin = unchecked((int)0x80000000);
            if (i == sentinelMax) return float.MaxValue;
            if (i == sentinelMin) return float.MinValue;
            int mask = unchecked((int)0x80000000);
            int decoded = (i >= 0) ? i : mask - i;
            return BitConverter.Int32BitsToSingle(decoded);
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Releases compute buffer and cleans up render textures and objects.
        /// </summary>
        private void OnDestroy()
        {
            minMaxBuffer?.Release();
            if (depthRenderTexture != null) depthRenderTexture.Release();
        }

        #endregion
    }
}
