using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// Manages all visual feedback for gaze tracking system including:
/// - Crosshair cursors (filtered and raw)
/// - 3D gaze indicators
/// - Object highlighting
/// - Calibration analysis visualizations
/// - Confusion zone displays
/// </summary>
public class GazeVisualizer
{
    #region Visual Components
    private RectTransform _crosshair;                     // Stable/filtered gaze cursor
    private RectTransform _rawCrosshair;                 // Raw gaze data cursor
    private GameObject _gazeIndicator;                   // 3D world-space gaze pointer
    private Canvas _analysisCanvas;                      // Parent canvas for visualizations
    private List<GameObject> _visualizationObjects = new List<GameObject>(); // Active visualization objects
    
    // Visualization prefabs
    private GameObject _confusionAccuracyZonePrefab;     // Accuracy zone visualization
    private GameObject _confusionPrecisionZonePrefab;    // Precision zone visualization  
    private GameObject _meanGazePointPrefab;             // Calibration point marker
    private GameObject _vectorPrefab;                    // Calibration offset vector
    #endregion

    #region State Properties
    public bool isVisOn = false;                        // Visualization active flag
    #endregion

    #region Highlighting State
    private Dictionary<GameObject, Material[]> _originalMaterials = new Dictionary<GameObject, Material[]>();
    private Dictionary<GameObject, Material[]> _highlightMaterials = new Dictionary<GameObject, Material[]>();
    private HashSet<GameObject> _currentHighlightedObjects = new HashSet<GameObject>();
    #endregion

    /// <summary>
    /// Initializes visualizer with all required UI components
    /// </summary>
    public GazeVisualizer(RectTransform crosshair, RectTransform rawCrosshair,
                         GameObject gazeIndicator, Canvas analysisCanvas,
                         GameObject confusionZonePrefabAccuracy,
                         GameObject confusionZonePrefabPrecision,
                         GameObject meanGazePointPrefab,
                         GameObject vectorPrefab)
    {
        _crosshair = crosshair;
        _rawCrosshair = rawCrosshair;
        _gazeIndicator = gazeIndicator;
        _analysisCanvas = analysisCanvas;
        _confusionPrecisionZonePrefab = confusionZonePrefabAccuracy;
        _confusionAccuracyZonePrefab = confusionZonePrefabPrecision;
        _meanGazePointPrefab = meanGazePointPrefab;
        _vectorPrefab = vectorPrefab;
    }

    #region Cursor Updates
    /// <summary>
    /// Updates both filtered and raw gaze cursors
    /// </summary>
    /// <param name="stablePos">Filtered gaze position</param>
    /// <param name="rawPos">Raw gaze position</param>
    public void UpdateCrosshairs(Vector2 rawPos)
    {
        UpdateCrosshair(_rawCrosshair, rawPos);
    }

    /// <summary>
    /// Positions a crosshair UI element at specified screen position
    /// </summary>
    private void UpdateCrosshair(RectTransform crosshairUI, Vector2 position)
    {
        if (crosshairUI != null)
        {
            RectTransform canvasRect = crosshairUI.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, position, null, out Vector2 localPos))
            {
                crosshairUI.anchoredPosition = localPos;
            }
        }
    }
    #endregion

    #region Calibration Visualization
    /// <summary>
    /// Visualizes calibration results showing offsets between targets and gaze means
    /// </summary>
    public void VisualizeAnalysisResults(float meanStdDev, float meanDistance, 
                                       List<Vector2> targetPositions, 
                                       List<Vector2> meanGazePositions)
    {
        ClearVisualizations();

        if (_analysisCanvas == null)
        {
            Debug.LogError("Analysis Canvas not assigned!");
            return;
        }

        // Create visualization for each calibration point
        for (int i = 0; i < targetPositions.Count; i++)
        {
            CreateMeanPoint(meanGazePositions[i], targetPositions[i]);
            CreateVector(targetPositions[i], meanGazePositions[i]);
        }
        
        isVisOn = true;
    }

    /// <summary>
    /// Clears all calibration visualizations
    /// </summary>
    public void ClearVisualizations()
    {
        foreach (var obj in _visualizationObjects)
        {
            if (obj != null)
            {
                Object.Destroy(obj);
            }
        }
        _visualizationObjects.Clear();

        isVisOn = false;
    }

    /// <summary>
    /// Creates a mean gaze point visualization with directional indicator
    /// </summary>
    private void CreateMeanPoint(Vector2 meanGazePosition, Vector2 targetPosition)
    {
        GameObject meanPoint = Object.Instantiate(_meanGazePointPrefab, _analysisCanvas.transform, false);
        RectTransform meanPointRect = meanPoint.GetComponent<RectTransform>();
        meanPointRect.anchoredPosition = targetPosition;
        
        // Orient indicator towards offset direction
        Vector2 direction = targetPosition - meanGazePosition;
        float angle = Vector2.SignedAngle(Vector2.down, direction);
        meanPointRect.localEulerAngles = new Vector3(0, 0, angle);

        _visualizationObjects.Add(meanPoint);
    }

    /// <summary>
    /// Creates a vector visualization between target and mean gaze positions
    /// </summary>
    private void CreateVector(Vector2 fromPosition, Vector2 toPosition)
    {
        Vector2 direction = toPosition - fromPosition;
        GameObject vector = Object.Instantiate(_vectorPrefab, _analysisCanvas.transform, false);
        RectTransform vectorRect = vector.GetComponent<RectTransform>();
        
        // Position and rotate vector
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        vectorRect.anchoredPosition = fromPosition;
        vectorRect.localEulerAngles = new Vector3(0, 0, angle);
        vectorRect.sizeDelta = new Vector2(direction.magnitude, 5); // Length matches offset magnitude
        
        _visualizationObjects.Add(vector);
    }
    #endregion
}