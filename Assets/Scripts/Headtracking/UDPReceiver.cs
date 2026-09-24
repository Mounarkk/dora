using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HeadTracking
{
    /// <summary>
    /// Component responsible for receiving data from a UDP connection with OpenTrack.
    /// Parses the received data and exposes position and rotation vectors.
    /// </summary>
    public class UDPReceiver : MonoBehaviour
    {
        /// <summary>
        /// Scale factor applied to gaze coordinates for coordinate system transformation.
        /// Converts between different coordinate spaces (e.g., screen pixels to world units).
        /// </summary>
        [Header("Transformation Settings")]
        public float scaleFactor = 0.01f;

        /// <summary>
        /// Rotation angle in degrees applied around the X-axis for coordinate alignment.
        /// Used to correct orientation differences between coordinate systems.
        /// </summary>
        public float xRotationAngleDeg = 0.0f;

        /// <summary>
        /// 2D translation offset applied to transformed coordinates.
        /// Adjusts the final position after scaling and rotation transformations.
        /// </summary>
        public Vector2 translation = Vector2.zero;
        
        /// <summary>
        /// The UDP port used for receiving data.
        /// </summary>
        public int port;
        
        /// <summary>
        /// The UDP client used for receiving data.
        /// </summary>
        private UdpClient udpClient;

        /// <summary>
        /// The endpoint for receiving UDP data.
        /// </summary>
        private IPEndPoint endPoint;

        /// <summary>
        /// The thread responsible for receiving data.
        /// </summary>
        private Thread receiveThread;

        /// <summary>
        /// Indicates whether data reception is currently active.
        /// </summary>
        private bool canReceive = false;


        /// <summary>
        /// Indicates whether data reception is currently happening.
        /// </summary>
        private bool isReceiving = false;

        /// <summary>
        /// The latest received position vector.
        /// </summary>
        private Vector3 position;
        
        /// <summary>
        /// The latest received timestamp.
        /// </summary>

        /// <summary>
        /// The number of infrared points currently tracked by the camera.
        /// </summary>
        private int nbTrackedPoints;

        /// <summary>
        /// Latest position data received via UDP.
        /// </summary>
        public Vector3 Position => position;
        
        /// <summary>
        /// Latest points tracked data received via UDP.
        /// </summary>
        public int NbPointsTracked => nbTrackedPoints;
        
        /// <summary>
        /// Latest timestamp data received via UDP.
        /// </summary>
        public double Timestamp => lastReceivedTimestamp;

                
        /// <summary>
        /// Returns whether data reception is currently happening.
        /// </summary>
        public bool IsReceiving => isReceiving;
        
        /// <summary>
        /// Timestamp of the last received network data packet for latency calculations.
        /// </summary>
        private double lastReceivedTimestamp;

        /// <summary>
        /// Unix epoch reference point (January 1, 1970) for timestamp conversions.
        /// </summary>
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// High-precision timestamp captured when the Stopwatch timing system started.
        /// Used for accurate time difference calculations.
        /// </summary>
        private static readonly long StopwatchStartTimestamp = Stopwatch.GetTimestamp();

        /// <summary>
        /// UTC time in seconds when the Stopwatch started, relative to Unix epoch.
        /// Enables conversion between Stopwatch ticks and absolute timestamps.
        /// </summary>
        private static readonly double UtcNowAtStopwatchStart = DateTime.UtcNow.Subtract(UnixEpoch).TotalSeconds;

        /// <summary>
        /// Called when the component is enabled. Starts the data reception thread.
        /// </summary>
        private void Start()
        {
            StartReceiver();
        }

        /// <summary>
        /// Called when the component is disabled. Stops the data reception thread.
        /// </summary>
        private void OnDisable()
        {
            StopReceiver();
        }

        /// <summary>
        /// Initializes the UDP client and starts the data reception thread.
        /// </summary>
        private void StartReceiver()
        {
            if (canReceive) return;

            endPoint = new IPEndPoint(IPAddress.Any, 4242);
            udpClient = new UdpClient(endPoint);
            udpClient.Client.ReceiveBufferSize = 512;

            canReceive = true;
            receiveThread = new Thread(ReceiveData) { IsBackground = true };
            receiveThread.Start();
        }

        /// <summary>
        /// Stops the UDP receiver thread.
        /// </summary>
        private void StopReceiver()
        {
            canReceive = false;

            if (receiveThread != null && receiveThread.IsAlive)
            {
                // Wait for the thread to join for maximum 1 second
                bool joined = receiveThread.Join(1000);
                if (!joined)
                {
                    Debug.LogWarning("UDP receiver thread did not terminate gracefully since it didn't connect to OpenTrack");
                }
            }

            if (udpClient != null)
            {
                udpClient.Close();
                udpClient = null;
            }
        }

        /// <summary>
        /// Continuously receives data via UDP and updates raw position and rotation values.
        /// </summary>
        /// <remarks>
        /// Expected Data Layout (48 bytes total):
        /// - The data is structured as six consecutive 64-bit (8-byte) double values:
        /// 
        /// | Index | Data           | Size | Description                          |
        /// |-------|----------------|------|--------------------------------------|
        /// | 0     | X Position     | 8    | World X Position                     |
        /// | 1     | Y Position     | 8    | World Y Position                     |
        /// | 2     | Z Position     | 8    | World Z Position                     |
        /// | 3     | Y Rotation     | 8    | Yaw (Rotation around Y-axis)         |
        /// | 4     | Timestamp      | 8    | Timestamp of when the packet was sent|
        /// | 5     | Points tracked | 8    | Nb of points tracked by cam          |
        /// 
        /// Data is received as a continuous 48-byte array. Each 8-byte segment is interpreted as a double.
        /// Positions are inverted on the x and z axes to align with Unity's coordinate system.
        /// </remarks>
        private void ReceiveData()
        {
            while (canReceive)
            {
                try
                {
                    byte[] data = udpClient.Receive(ref endPoint);
                    isReceiving = true;
                    
                    if (data.Length != 48) continue;

                    double[] values = new double[6];
                    for (int i = 0; i < 6; i++)
                    {
                        values[i] = BitConverter.ToDouble(data, i * 8);
                    }

                    // Tracks the number of infrared points currently tracked by the camera in Opentrack
                    nbTrackedPoints = (int)values[5];

                    // Store the timestamp from the packet
                    lastReceivedTimestamp = values[4];
                    
                    //Debug.Log($"OpenTrack send time (UTC): {UnixTimeStampToDateTime(lastReceivedTimestamp).ToString("HH:mm:ss.fff")}");

                    position = ApplyTransformations(new Vector3((float)values[0], (float)values[1], (float)values[2]));
                }
                catch (Exception e)
                {
                    isReceiving = false;
                    Debug.LogWarning($"UDP Receiver Error: {e.Message}");
                }
            }
        }
        
        private Vector3 ApplyTransformations(Vector3 rawPos)
        {
            // Apply scaling
            Vector3 positionScaled = rawPos;
            Vector3 translationScaled = translation * scaleFactor;

            // Apply rotation around X-axis (right-handed coordinates)
            float rad = xRotationAngleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            float y = positionScaled.y * cos - positionScaled.z * sin;
            float z = positionScaled.y * sin + positionScaled.z * cos;

            // Apply translation (adjusted for coordinate conversion)
            Vector3 translated = new Vector3(
                positionScaled.x + translationScaled.x,
                y + translationScaled.y,
                z
            );

            // Convert to Unity coordinates (invert X and Z)
            return new Vector3(-translated.x, translated.y, -translated.z);
        }

        private double GetCurrentUtcTimestampSeconds()
        {
            long currentTimestamp = Stopwatch.GetTimestamp();
            double elapsedSeconds = (double)(currentTimestamp - StopwatchStartTimestamp) / Stopwatch.Frequency;
            return UtcNowAtStopwatchStart + elapsedSeconds;
        }
        
        // Helper to convert Unix timestamp (double seconds since epoch) to DateTime
        private DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(unixTimeStamp);
        }
    }
}
