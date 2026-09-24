using System;
using System.IO;
using System.Globalization;
using UnityEngine;
/// <summary>
/// Handles logging of gaze tracking data to CSV files with timestamp information.
/// Manages recording sessions with start/stop markers and duration tracking.
/// </summary>
public class GazeDataLogger
{
    // File paths
    private string _filePath; // Full path to current data file
    private string gazeDataPath => Path.Combine(Application.dataPath, "Data.csv"); // Default path

    // Recording state
    private bool _isRecording;
    private float _prevX, _prevY;
    private float _recordingStartTime;

    /// <summary>
    /// Initializes a new gaze data logger with optional custom filename.
    /// </summary>
    public GazeDataLogger()
    {
        Debug.Log($"Gaze data will be saved to: {gazeDataPath}");
        _filePath = gazeDataPath;
        InitializeFile();
    }

    #region File Management

    /// <summary>
    /// Initializes a new CSV file with headers, deleting any existing file.
    /// </summary>
    private void InitializeFile()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                Debug.Log("Previous data file deleted.");
            }

            using (var sw = File.CreateText(_filePath))
            {
                sw.WriteLine("X_pos,Y_pos,Timestamp");
            }
            Debug.Log("Data file initialized successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize data file: {e.Message}");
        }
    }

    /// <summary>
    /// Clears all existing gaze data by deleting the data file.
    /// </summary>
    public void ClearData()
    {
        if (File.Exists(gazeDataPath))
        {
            try
            {
                File.Delete(gazeDataPath);
                Debug.Log("Previous calibration data cleared.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to clear previous data: {e.Message}");
            }
        }
    }

    #endregion

    #region Recording Control

    /// <summary>
    /// Begins a new recording session with initialization marker.
    /// </summary>
    public void StartRecording()
    {
        _isRecording = true;
        _recordingStartTime = Time.time;
        AppendLine("new_point,new_point,new_point");
        Debug.Log("Started recording gaze data.");
    }

    /// <summary>
    /// Ends current recording session with duration marker.
    /// </summary>
    public void StopRecording()
    {
        _isRecording = false;
        float duration = Time.time - _recordingStartTime;
        AppendLine("end_point,end_point,end_point");
        string durationLine = $"duration,{duration.ToString("F6", CultureInfo.InvariantCulture)},seconds";
        AppendLine(durationLine);
        Debug.Log($"Stopped recording. Duration: {duration:F3} seconds");
    }

    #endregion

    #region Data Recording

    /// <summary>
    /// Records a single gaze position frame if position has changed significantly.
    /// </summary>
    /// <param name="position">Current gaze position in screen coordinates</param>
    public void RecordFrame(Vector2 position)
    {
        if (!_isRecording) return;

        if (Mathf.Abs(position.x - _prevX) < 1f && Mathf.Abs(position.y - _prevY) < 1f)
            return;
        if(position == Vector2.zero )
            return; // Ignore out-of-bounds positions

        float timestamp = Time.time - _recordingStartTime;

        string line = $"{position.x.ToString("F2", CultureInfo.InvariantCulture)}," +
                      $"{position.y.ToString("F2", CultureInfo.InvariantCulture)}," +
                      $"{timestamp.ToString("F3", CultureInfo.InvariantCulture)}";

        AppendLine(line);
        _prevX = position.x;
        _prevY = position.y;
    }

    /// <summary>
    /// Appends a line of text to the data file with error handling.
    /// </summary>
    /// <param name="content">Line to append</param>
    private void AppendLine(string content)
    {
        try
        {
            File.AppendAllText(_filePath, content + Environment.NewLine);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write to data file: {e.Message}");
        }
    }


    #endregion
}
