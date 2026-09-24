using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using System.Threading;
using System;
using Unity.Collections;

namespace EyeTracking.ConfusionZoneAnalyzer
{
    /// <summary>
    /// Analyzes gaze zones by rendering objects with unique color IDs and calculating
    /// the proportion of each object within a circular gaze zone. Uses asynchronous
    /// GPU readback and threaded processing for performance optimization.
    /// </summary>
    public class GazeZoneAnalyzer : MonoBehaviour
    {
        #region Inspector Fields
        /// <summary>
        /// Reference to the gaze detector component that provides gaze data.
        /// </summary>
        [Header("Dependencies")]
        public GazeManager gazeDetector;

        /// <summary>
        /// Main camera used as a reference for the segmentation camera setup.
        /// </summary>
        public Camera mainCamera;

        /// <summary>
        /// List of GameObjects to analyze for gaze zone coverage.
        /// </summary>
        public List<GameObject> analyzableObjects = new List<GameObject>();

        /// <summary>
        /// Parent hierarchy from which to automatically populate analyzable objects.
        /// </summary>
        [SerializeField]
        public GameObject parentHierarchy;

        /// <summary>
        /// Reference to the confusion zone manager providing the confusion circle data.
        /// </summary>
        private ConfusionZoneManager confusionZoneManager;
        #endregion

        #region Private Fields - Rendering System
        /// <summary>
        /// Render texture used for object ID segmentation mask.
        /// </summary>
        private RenderTexture segmentationMaskRenderTexture;

        /// <summary>
        /// Camera used to render the object ID segmentation mask.
        /// </summary>
        private Camera segmentationMaskCamera;

        /// <summary>
        /// Material used for rendering object IDs as colors.
        /// </summary>
        private Material segmentationMaskMaterial;

        /// <summary>
        /// Texture used for CPU readback of GPU data.
        /// </summary>
        private Texture2D readbackTexture;
        #endregion

        #region Private Fields - Object Mapping
        /// <summary>
        /// Maps object IDs to their corresponding GameObjects.
        /// </summary>
        private readonly Dictionary<int, GameObject> idToObjectMap = new Dictionary<int, GameObject>();

        /// <summary>
        /// Maps object IDs to their corresponding Renderer components.
        /// </summary>
        private readonly Dictionary<int, Renderer> idToObjectRendererMap = new Dictionary<int, Renderer>();

        /// <summary>
        /// Stores original materials for restoration after ID rendering.
        /// </summary>
        private readonly Dictionary<GameObject, Material[]> originalMaterials = new Dictionary<GameObject, Material[]>();

        /// <summary>
        /// Maps object IDs to their segmentation mask materials.
        /// </summary>
        private readonly Dictionary<int, Material[]> idToSegMaskMaterialMap = new Dictionary<int, Material[]>();

        /// <summary>
        /// Maps packed RGB color values to their corresponding object IDs for efficient pixel lookup.
        /// </summary>
        private readonly Dictionary<uint, int> packedColorToIdMap = new Dictionary<uint, int>();

        /// <summary>
        /// Tracks unknown colors encountered during analysis to prevent duplicate log messages.
        /// </summary>
        private readonly HashSet<uint> loggedUnknownColors = new HashSet<uint>();
        #endregion

        #region Private Fields - Asynchronous Processing
        /// <summary>
        /// Width of the render texture used for object ID segmentation.
        /// </summary>
        private int renderTextureWidth = 1920;

        /// <summary>
        /// Height of the render texture used for object ID segmentation.
        /// </summary>
        private int renderTextureHeight = 1080;

        /// <summary>
        /// Indicates whether a GPU readback operation is currently in progress.
        /// </summary>
        private bool readbackInProgress = false;

        /// <summary>
        /// Render texture used for asynchronous GPU readback operations.
        /// </summary>
        private RenderTexture asyncReadbackTexture;

        /// <summary>
        /// Cached results from the last completed analysis.
        /// </summary>
        private Dictionary<GameObject, float> lastAnalysisResults = new Dictionary<GameObject, float>();

        /// <summary>
        /// Lock object for thread-safe access to analysis results.
        /// </summary>
        private readonly object resultsLock = new object();

        /// <summary>
        /// Background thread for processing pixel data.
        /// </summary>
        private Thread processingThread;

        /// <summary>
        /// Flag to signal thread termination.
        /// </summary>
        private bool abortThread = false;
        #endregion

        #region Unity Lifecycle
        /// <summary>
        /// Initializes the gaze zone analyzer system on component start.
        /// </summary>
        void Start()
        {
            confusionZoneManager = gazeDetector.confusionZone;
            InitializeGazeZoneAnalyzer();
        }

        /// <summary>
        /// Cleans up resources when the component is destroyed.
        /// </summary>
        void OnDestroy()
        {
            CleanupResources();
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes all components of the gaze zone analyzer system.
        /// </summary>
        private void InitializeGazeZoneAnalyzer()
        {
            PopulateObjectsFromHierarchy();
            SetupObjectIDSystem();
            AssignObjectAndRenderersIDs();
            SetupAsyncReadbackTexture();
        }

        /// <summary>
        /// Sets up the render texture for asynchronous GPU readback operations.
        /// </summary>
        private void SetupAsyncReadbackTexture()
        {
            // Get the current screen size
            renderTextureWidth = Screen.width;
            renderTextureHeight = Screen.height;

            asyncReadbackTexture = new RenderTexture(renderTextureWidth, renderTextureHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            asyncReadbackTexture.filterMode = FilterMode.Point;
            asyncReadbackTexture.Create();
        }
        #endregion

        #region Object Population
        /// <summary>
        /// Automatically populates the analyzable objects list from the parent hierarchy.
        /// Only includes objects that have a Renderer component.
        /// </summary>
        void PopulateObjectsFromHierarchy()
        {
            analyzableObjects.Clear();

            Transform[] allChildren = parentHierarchy.GetComponentsInChildren<Transform>(false);

            foreach (Transform child in allChildren)
            {
                if (child == parentHierarchy.GetComponent<Transform>()) continue;

                if (child.GetComponent<Renderer>() != null)
                {
                    analyzableObjects.Add(child.gameObject);
                }
            }

            Debug.Log($"Auto-populated {analyzableObjects.Count} objects from hierarchy '{parentHierarchy.name}'");
        }
        #endregion

        #region Object ID System Setup
        /// <summary>
        /// Sets up the object ID system for rendering unique colors per object.
        /// Creates the segmentation camera and materials needed for ID rendering.
        /// </summary>
        void SetupObjectIDSystem()
        {
            CreateSegmentationRenderTexture();
            CreateSegmentationCamera();
            SetupCameraProperties();
            CreateSegmentationMaterial();
            CreateReadbackTexture();
        }

        /// <summary>
        /// Creates the render texture for object ID segmentation with high precision.
        /// </summary>
        private void CreateSegmentationRenderTexture()
        {
            segmentationMaskRenderTexture = new RenderTexture(
                renderTextureWidth,
                renderTextureHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear); // Must be ARGB32 for color encoding

            segmentationMaskRenderTexture.filterMode = FilterMode.Point;
            segmentationMaskRenderTexture.Create();
        }

        /// <summary>
        /// Creates and configures the segmentation camera for object ID rendering.
        /// </summary>
        private void CreateSegmentationCamera()
        {
            GameObject cameraGo = new GameObject("ObjectID Camera");
            cameraGo.transform.SetParent(transform);
            segmentationMaskCamera = cameraGo.AddComponent<Camera>();

            segmentationMaskCamera.CopyFrom(mainCamera);
            segmentationMaskCamera.targetTexture = segmentationMaskRenderTexture;
            segmentationMaskCamera.enabled = false;
        }

        /// <summary>
        /// Configures camera properties for precise color rendering without post-processing.
        /// </summary>
        private void SetupCameraProperties()
        {
            segmentationMaskCamera.clearFlags = CameraClearFlags.Color;
            segmentationMaskCamera.backgroundColor = Color.black;
            segmentationMaskCamera.allowHDR = false;
            segmentationMaskCamera.allowMSAA = false;
            segmentationMaskCamera.renderingPath = RenderingPath.Forward;

            ConfigureUniversalRenderPipeline();
        }

        /// <summary>
        /// Configures Universal Render Pipeline settings to disable post-processing.
        /// </summary>
        private void ConfigureUniversalRenderPipeline()
        {
            var cameraData = segmentationMaskCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = segmentationMaskCamera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            }
            cameraData.renderPostProcessing = false;
            cameraData.SetRenderer(1); // Use PC_Renderer_NoSSAO
        }

        /// <summary>
        /// Creates the material used for object ID rendering.
        /// </summary>
        private void CreateSegmentationMaterial()
        {
            Shader objectIDShader = Shader.Find("Custom/ObjectIDShader");
            if (objectIDShader == null)
            {
                Debug.LogError("ObjectIDShader not found! Make sure it's compiled correctly.");
                return;
            }

            segmentationMaskMaterial = new Material(objectIDShader);
        }

        /// <summary>
        /// Creates the texture used for CPU readback of GPU data.
        /// </summary>
        private void CreateReadbackTexture()
        {
            readbackTexture = new Texture2D(renderTextureWidth, renderTextureHeight, TextureFormat.RGBA32, false);
        }
        #endregion

        #region Object ID Assignment
        /// <summary>
        /// Assigns unique IDs to all analyzable objects and creates their corresponding
        /// color-encoded materials for segmentation rendering.
        /// </summary>
        void AssignObjectAndRenderersIDs()
        {
            for (int i = 0; i < analyzableObjects.Count; i++)
            {
                GameObject obj = analyzableObjects[i];
                if (obj == null) continue;

                int objectID = Mathf.Abs(i + 1) % 16777215; ; // Start from 1 (0 is reserved for the background)
                AssignObjectID(obj, objectID);
            }
        }

        private void AssignObjectID(GameObject obj, int objectID)
        {
            Color32 color = EncodeIDAsColor32(objectID);
            uint packedColor = PackColor32(color);

            packedColorToIdMap[packedColor] = objectID;
            idToObjectMap[objectID] = obj;

            SetupObjectRenderer(obj, objectID);
        }

        /// <summary>
        /// Sets up the renderer and materials for an object with the given ID.
        /// </summary>
        /// <param name="obj">The GameObject to set up</param>
        /// <param name="objectID">The unique ID for the object</param>
        private void SetupObjectRenderer(GameObject obj, int objectID)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Save original material for restoration
                originalMaterials[obj] = renderer.materials;
                idToObjectRendererMap[objectID] = renderer;

                // Create segmentation material
                CreateSegmentationMaterialForObject(obj, objectID);
            }
        }

        /// <summary>
        /// Creates a segmentation material for a specific object with encoded color ID.
        /// </summary>
        /// <param name="obj">The GameObject to create material for</param>
        /// <param name="objectID">The unique ID for the object</param>
        private void CreateSegmentationMaterialForObject(GameObject obj, int objectID)
        {
            Material segMaskMaterial = new Material(segmentationMaskMaterial);
            Color32 objectColor = EncodeIDAsColor32(objectID);

            // Set exact color values
            Debug.Log($"Setting color for object {obj.name} to {objectColor}");
            segMaskMaterial.SetInt("_ObjectColorR", objectColor.r);
            segMaskMaterial.SetInt("_ObjectColorG", objectColor.g);
            segMaskMaterial.SetInt("_ObjectColorB", objectColor.b);

            if (obj.name.Contains("Background"))
                segMaskMaterial.SetInt("_IsBackground", 1);

            idToSegMaskMaterialMap[objectID] = new Material[] { segMaskMaterial };
        }
        #endregion

        #region Gaze Zone Analysis
        /// <summary>
        /// Analyzes the gaze zone by rendering objects with unique IDs and calculating
        /// their proportional coverage within the gaze zone circle.
        /// </summary>
        /// <returns>Dictionary mapping GameObjects to their proportional coverage (0-1)</returns>
        public Dictionary<GameObject, float> AnalyzeGazeZone()
        {
            if (!IsAnalysisValid())
                return new Dictionary<GameObject, float>();

            PerformSegmentationRender();
            return AnalyzePixelsInCircle();
        }

        /// <summary>
        /// Validates that the analysis can be performed with current dependencies.
        /// </summary>
        /// <returns>True if analysis can proceed, false otherwise</returns>
        private bool IsAnalysisValid()
        {
            return gazeDetector != null && segmentationMaskCamera != null;
        }

        public bool save = false;

        /// <summary>
        /// Synchronizes the segmentation camera properties with the main camera.
        /// </summary>
        void UpdateSegCameraProperties()
        {
            segmentationMaskCamera.transform.position = mainCamera.transform.position;
            segmentationMaskCamera.transform.rotation = mainCamera.transform.rotation;
            segmentationMaskCamera.fieldOfView = mainCamera.fieldOfView;
            segmentationMaskCamera.nearClipPlane = mainCamera.nearClipPlane;
            segmentationMaskCamera.farClipPlane = mainCamera.farClipPlane;
        }


        /// <summary>
        /// Performs the segmentation render by temporarily replacing materials with ID materials.
        /// </summary>
        private void PerformSegmentationRender()
        {
            UpdateSegCameraProperties();
            SetObjectIDMaterials();
            segmentationMaskCamera.Render();
            
            // Save segmentation mask
            if (Input.GetKey(KeyCode.K))
            {
                save = true;
            }
            if (save) SaveSegmentationMaskToPNG();
            save = false;
            RestoreOriginalMaterials();
        }
        #endregion

        #region Material Management
        /// <summary>
        /// Temporarily replaces all object materials with their ID segmentation materials.
        /// </summary>
        void SetObjectIDMaterials()
        {
            foreach (var kvp in idToObjectMap)
            {
                int objectID = kvp.Key;
                ApplySegmentationMaterial(objectID);
            }
        }

        /// <summary>
        /// Applies the segmentation material to a specific object.
        /// </summary>
        /// <param name="objectID">The ID of the object to apply material to</param>
        private void ApplySegmentationMaterial(int objectID)
        {
            if (idToObjectRendererMap.TryGetValue(objectID, out Renderer renderer) && renderer != null)
            {
                if (idToSegMaskMaterialMap.TryGetValue(objectID, out Material[] segMaskMaterials))
                {
                    renderer.materials = segMaskMaterials; ;
                }
            }
        }

        /// <summary>
        /// Restores all objects to their original materials after ID rendering.
        /// </summary>
        void RestoreOriginalMaterials()
        {
            foreach (var kvp in originalMaterials)
            {
                GameObject obj = kvp.Key;
                Material[] originalMat = kvp.Value;

                int objID = FindObjectID(obj);
                RestoreObjectMaterial(objID, originalMat);
            }
        }

        /// <summary>
        /// Finds the object ID for a given GameObject.
        /// </summary>
        /// <param name="obj">The GameObject to find the ID for</param>
        /// <returns>The object ID, or 0 if not found</returns>
        private int FindObjectID(GameObject obj)
        {
            foreach (var keyValuePair in idToObjectMap)
            {
                if (keyValuePair.Value == obj)
                {
                    return keyValuePair.Key;
                }
            }
            return 0;
        }

        /// <summary>
        /// Restores the original material for a specific object.
        /// </summary>
        /// <param name="objID">The object ID</param>
        /// <param name="originalMat">The original material to restore</param>
        private void RestoreObjectMaterial(int objID, Material[] originalMat)
        {
            if (objID > 0 && idToObjectRendererMap.TryGetValue(objID, out Renderer renderer) && renderer != null)
            {
                renderer.materials = originalMat;
            }
        }
        #endregion

        #region Pixel Analysis
        /// <summary>
        /// Analyzes pixels within the gaze zone circle using asynchronous GPU readback.
        /// Returns cached results if readback is in progress, otherwise starts new analysis.
        /// </summary>
        /// <returns>Dictionary mapping GameObjects to their proportional coverage</returns>
        Dictionary<GameObject, float> AnalyzePixelsInCircle()
        {
            if (readbackInProgress)
            {
                return GetCachedResults();
            }

            StartNewAnalysis();
            return lastAnalysisResults;
        }

        /// <summary>
        /// Returns cached analysis results in a thread-safe manner.
        /// </summary>
        /// <returns>Cached analysis results</returns>
        private Dictionary<GameObject, float> GetCachedResults()
        {
            lock (resultsLock)
            {
                return lastAnalysisResults;
            }
        }

        /// <summary>
        /// Starts a new asynchronous analysis by copying render texture and requesting GPU readback.
        /// </summary>
        private void StartNewAnalysis()
        {
            readbackInProgress = true;
            Graphics.Blit(segmentationMaskRenderTexture, asyncReadbackTexture);
            AsyncGPUReadback.Request(asyncReadbackTexture, 0, OnReadbackComplete);
        }
        #endregion

        #region Asynchronous Processing
        /// <summary>
        /// Callback for when GPU readback operation completes.
        /// Starts a background thread for pixel data processing.
        /// </summary>
        /// <param name="request">The completed GPU readback request</param>
        void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            if (request.hasError || abortThread)
            {
                readbackInProgress = false;
                return;
            }

            // Create a COPY of the pixel data for the thread
            NativeArray<Color32> pixelDataCopy = new NativeArray<Color32>(request.GetData<Color32>(), Allocator.Persistent);
            StartPixelProcessingThread(pixelDataCopy);
        }

        /// <summary>
        /// Starts a background thread for processing pixel data.
        /// </summary>
        /// <param name="pixelData">The pixel data to process</param>
        private void StartPixelProcessingThread(NativeArray<Color32> pixelData)
        {
            try
            {
                TerminateExistingThread();

                if (!pixelData.IsCreated)
                {
                    Debug.LogError("Invalid pixel data for processing");
                    return;
                }

                abortThread = false;
                processingThread = new Thread(() =>
                {
                    try { ProcessPixelDataThreaded(pixelData); }
                    catch (Exception e) { Debug.LogError($"Processing thread error: {e}"); }
                });
                processingThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"Thread start failed: {e}");
                if (pixelData.IsCreated) pixelData.Dispose();
            }
        }

        /// <summary>
        /// Terminates any existing pixel processing thread.
        /// </summary>
        private void TerminateExistingThread()
        {
            if (processingThread != null && processingThread.IsAlive)
            {
                abortThread = true;
                processingThread.Join();
            }
        }

        /// <summary>
        /// Processes pixel data in a background thread to calculate object proportions
        /// within the gaze zone circle.
        /// </summary>
        /// <param name="pixels">The pixel data from GPU readback</param>
        private void ProcessPixelDataThreaded(NativeArray<Color32> pixels)
        {
            try
            {
                var pixelCounts = CountPixelsInCircle(pixels);
                var proportions = ConvertToGameObjectProportions(pixelCounts);

                lock (resultsLock)
                {
                    lastAnalysisResults = proportions;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Processing failed: {e}");
            }
            finally
            {
                if (pixels.IsCreated)
                    pixels.Dispose();
                readbackInProgress = false;
            }
        }

        /// <summary>
        /// Counts pixels for each object ID within the gaze zone circle.
        /// </summary>
        /// <param name="pixels">The pixel data to analyze</param>
        /// <returns>Dictionary mapping object IDs to pixel counts</returns>
        private Dictionary<int, int> CountPixelsInCircle(NativeArray<Color32> pixels)
        {
            var tempResults = new Dictionary<int, int>();

            // Get confusion circle parameters that are in pixels
            int radiusInt = (int)confusionZoneManager.CurrentAccuracyRadius;
            int centerX = (int)confusionZoneManager.Center.x;
            int centerY = (int)confusionZoneManager.Center.y;

            for (int x = -radiusInt; x <= radiusInt; x++)
            {
                if (abortThread) return tempResults;

                for (int y = -radiusInt; y <= radiusInt; y++)
                {
                    if (IsPixelInCircle(x, y, radiusInt))
                    {
                        ProcessPixelInCircle(pixels, centerX, centerY, x, y, tempResults);
                    }
                }
            }
            return tempResults;
        }

        /// <summary>
        /// Checks if a pixel coordinate is within the circular gaze zone.
        /// </summary>
        /// <param name="x">X offset from the center</param>
        /// <param name="y">Y offset from the center</param>
        /// <param name="radius">Circle radius</param>
        /// <returns>True if pixel is within circle</returns>
        private bool IsPixelInCircle(int x, int y, int radius)
        {
            return x * x + y * y <= radius * radius;
        }

        /// <summary>
        /// Processes a single pixel within the gaze zone circle.
        /// </summary>
        /// <param name="pixels">The pixel data array</param>
        /// <param name="centerX">Circle center X coordinate</param>
        /// <param name="centerY">Circle center Y coordinate</param>
        /// <param name="offsetX">X offset from the center</param>
        /// <param name="offsetY">Y offset from the center</param>
        /// <param name="results">Dictionary to store results</param>
        private void ProcessPixelInCircle(NativeArray<Color32> pixels, int centerX, int centerY,
        int offsetX, int offsetY, Dictionary<int, int> results)
        {
            int pixelX = centerX + offsetX;
            int pixelY = centerY + offsetY;

            if (pixelX >= 0 && pixelX < renderTextureWidth &&
                pixelY >= 0 && pixelY < renderTextureHeight)
            {
                Color32 color = pixels[pixelY * renderTextureWidth + pixelX];

                uint packedColor = PackColor32(color);

                // Background check (exact black)
                if (packedColor == 0)
                {
                    IncrementPixelCount(results, 0);
                }
                else if (packedColorToIdMap.TryGetValue(packedColor, out int objectID))
                {
                    IncrementPixelCount(results, objectID);
                }
                else
                {
                    // Only logs the first occurrence of unknown colors
                    if (!loggedUnknownColors.Contains(packedColor))
                    {
                        Debug.LogWarning($"Unregistered color: #{packedColor:X6} at ({pixelX},{pixelY}) with color {color}");
                        loggedUnknownColors.Add(packedColor);
                    }
                }
            }
        }

        /// <summary>
        /// Increments the pixel count for a specific object ID.
        /// </summary>
        /// <param name="results">Dictionary storing pixel counts</param>
        /// <param name="objectID">The object ID to increment</param>
        private void IncrementPixelCount(Dictionary<int, int> results, int objectID)
        {
            if (!results.TryAdd(objectID, 1))
                results[objectID]++;
        }

        /// <summary>
        /// Converts object ID pixel counts to GameObject proportions.
        /// </summary>
        /// <param name="pixelCounts">Dictionary of object IDs to pixel counts</param>
        /// <returns>Dictionary of GameObjects to their proportional coverage</returns>
        private Dictionary<GameObject, float> ConvertToGameObjectProportions(Dictionary<int, int> pixelCounts)
        {
            var newResults = new Dictionary<GameObject, float>();
            int totalPixels = pixelCounts.Values.Sum();

            if (totalPixels == 0) return newResults;

            foreach (var kvp in pixelCounts)
            {
                // We have to check if the object ID is valid, because 0 is reserved for the background
                if (kvp.Key != 0 && idToObjectMap.TryGetValue(kvp.Key, out GameObject obj))
                {
                    newResults[obj] = (float)kvp.Value / totalPixels;
                }
            }

            return newResults;
        }
        #endregion

        #region Color Encoding Utilities
        // New encoding method which guarantees unique, non-black colors
        private static Color32 EncodeIDAsColor32(int objectID)
        {
            return new Color32(
                (byte)((objectID >> 16) & 0xFF),  // Red
                (byte)((objectID >> 8) & 0xFF),   // Green
                (byte)(objectID & 0xFF),          // Blue
                255);                           // Alpha (always opaque)
        }

        // Helper to pack Color32 to uint (0xRRGGBB)
        private static uint PackColor32(Color32 color)
        {
            return ((uint)color.r << 16) | ((uint)color.g << 8) | color.b;
        }
        #endregion

        #region Resource Management
        /// <summary>
        /// Cleans up all allocated resources, including render textures and threads.
        /// </summary>
        private void CleanupResources()
        {
            CleanupRenderTextures();
            CleanupProcessingThread();
        }

        /// <summary>
        /// Releases all render textures and destroys readback texture.
        /// </summary>
        private void CleanupRenderTextures()
        {
            segmentationMaskRenderTexture?.Release();

            if (readbackTexture != null)
            {
                DestroyImmediate(readbackTexture);
            }
        }

        /// <summary>
        /// Terminates the processing thread if it's still running.
        /// </summary>
        private void CleanupProcessingThread()
        {
            abortThread = true;

            if (processingThread != null && processingThread.IsAlive)
            {
                if (!processingThread.Join(1000)) // Wait 1000ms
                {
                    processingThread.Abort(); // Force terminate if needed
                }
            }

            processingThread = null;
        }
        #endregion
        
        /// <summary>
        /// Saves both the segmentation mask and the real scene rendering for comparison.
        /// Creates two PNG files with the same timestamp for easy side-by-side analysis.
        /// </summary>
        private void SaveSegmentationMaskToPNG()
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string directoryPath = Path.Combine(Application.dataPath, "DebugOutput");
            
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Save segmentation mask
            SaveSegmentationMask(directoryPath, timestamp);
            
            // Save real scene rendering
            SaveRealSceneRendering(directoryPath, timestamp);
        }

        /// <summary>
        /// Saves the segmentation mask with enhanced contrast for better visibility.
        /// </summary>
        /// <param name="directoryPath">Directory to save the file</param>
        /// <param name="timestamp">Timestamp for file naming</param>
        private void SaveSegmentationMask(string directoryPath, string timestamp)
        {
            Texture2D tempTex = new Texture2D(
                segmentationMaskRenderTexture.width, 
                segmentationMaskRenderTexture.height, 
                TextureFormat.ARGB32, 
                false,
                false
            );

            // Read pixels from segmentation render texture
            RenderTexture.active = segmentationMaskRenderTexture;
            tempTex.ReadPixels(new Rect(0, 0, segmentationMaskRenderTexture.width, segmentationMaskRenderTexture.height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = null;

            // Apply color enhancement for distinct colors
            Color32[] pixels = tempTex.GetPixels32();
            pixels = EnhanceContrast(pixels);
            tempTex.SetPixels32(pixels);
            tempTex.Apply();

            // Save segmentation mask
            byte[] segBytes = tempTex.EncodeToPNG();
            string segFilePath = Path.Combine(directoryPath, $"SegmentationMask_{timestamp}.png");
            File.WriteAllBytes(segFilePath, segBytes);
            
            DestroyImmediate(tempTex);
            Debug.Log($"Segmentation mask saved: {segFilePath}");
        }

        /// <summary>
        /// Captures and saves the current real scene rendering from the main camera.
        /// </summary>
        /// <param name="directoryPath">Directory to save the file</param>
        /// <param name="timestamp">Timestamp for file naming</param>
        private void SaveRealSceneRendering(string directoryPath, string timestamp)
        {
            // Create temporary render texture matching the segmentation mask resolution
            RenderTexture tempRenderTexture = new RenderTexture(
                segmentationMaskRenderTexture.width,
                segmentationMaskRenderTexture.height,
                24,
                RenderTextureFormat.ARGB32
            );
            tempRenderTexture.Create();

            // Store original camera target
            RenderTexture originalTarget = mainCamera.targetTexture;
            
            // Render real scene to temporary texture
            mainCamera.targetTexture = tempRenderTexture;
            mainCamera.Render();
            mainCamera.targetTexture = originalTarget;

            // Create texture for readback
            Texture2D realSceneTex = new Texture2D(
                tempRenderTexture.width,
                tempRenderTexture.height,
                TextureFormat.ARGB32,
                false
            );

            // Read pixels from real scene render
            RenderTexture.active = tempRenderTexture;
            realSceneTex.ReadPixels(new Rect(0, 0, tempRenderTexture.width, tempRenderTexture.height), 0, 0);
            realSceneTex.Apply();
            RenderTexture.active = null;

            // Save real scene rendering
            byte[] realSceneBytes = realSceneTex.EncodeToPNG();
            string realSceneFilePath = Path.Combine(directoryPath, $"RealScene_{timestamp}.png");
            File.WriteAllBytes(realSceneFilePath, realSceneBytes);

            // Cleanup
            DestroyImmediate(realSceneTex);
            tempRenderTexture.Release();
            
            Debug.Log($"Real scene saved: {realSceneFilePath}");
        }

        /// <summary>
        /// Enhances the contrast of the given pixel array.
        /// </summary>
        /// <param name="pixels"></param>
        /// <param name="contrastFactor"></param>
        /// <returns></returns>
        private Color32[] EnhanceContrast(Color32[] pixels, float contrastFactor = 2.0f)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                float r = (pixels[i].r / 255.0f) * contrastFactor + 0.5f;
                float g = (pixels[i].g / 255.0f) * contrastFactor + 0.5f;
                float b = (pixels[i].b / 255.0f) * contrastFactor + 0.5f;
                
                pixels[i] = new Color32(
                    (byte)Mathf.Clamp(r * 255, 0, 255),
                    (byte)Mathf.Clamp(g * 255, 0, 255),
                    (byte)Mathf.Clamp(b * 255, 0, 255),
                    255
                );
            }
            
            return pixels;
        }
    }
}