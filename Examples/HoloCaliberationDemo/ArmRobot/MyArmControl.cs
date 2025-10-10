using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCartActivator;

namespace HoloCaliberationDemo
{
    // Configuration structure for JSON deserialization
    public class ArmControlParams
    {
        public float[] InitialPosition { get; set; } = new float[] { 0, 0, 0 };
        public float[] InitialRotation { get; set; } = new float[] { 0, 0, 0 };
        public byte DefaultSpeed { get; set; } = 50;
    }

    internal class MyArmControl : IDisposable
    {
        // Control AgileX Arm Robot.
        private AgileXCanInterface canInterface;
        private AgileXProtocol protocol;
        private Thread updateThread;
        private volatile bool shouldStop = false;
        private bool isInitialized = false;

        // Default position values (in mm)
        private float defaultX = 0.0f;
        private float defaultY = 0.0f;
        private float defaultZ = 0.0f;

        // Default rotation values (in degrees)
        private float defaultRX = 0.0f;
        private float defaultRY = 0.0f;
        private float defaultRZ = 0.0f;

        // Default movement speed (0-100 percent)
        private byte defaultSpeed = 50;

        /// <summary>
        /// Load configuration from params.json
        /// </summary>
        private bool LoadConfiguration()
        {
            try
            {
                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "params.json");
                
                if (!File.Exists(configPath))
                {
                    Console.WriteLine($"Warning: params.json not found at {configPath}");
                    Console.WriteLine("Using default values: Position(0,0,0), Rotation(0,0,0)");
                    return false;
                }

                string jsonContent = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                var config = JsonSerializer.Deserialize<ArmControlParams>(jsonContent, options);
                
                if (config != null)
                {
                    // Load initial position
                    if (config.InitialPosition != null && config.InitialPosition.Length >= 3)
                    {
                        defaultX = config.InitialPosition[0];
                        defaultY = config.InitialPosition[1];
                        defaultZ = config.InitialPosition[2];
                        Console.WriteLine($"Loaded default position: X={defaultX}, Y={defaultY}, Z={defaultZ}");
                    }
                    
                    // Load initial rotation
                    if (config.InitialRotation != null && config.InitialRotation.Length >= 3)
                    {
                        defaultRX = config.InitialRotation[0];
                        defaultRY = config.InitialRotation[1];
                        defaultRZ = config.InitialRotation[2];
                        Console.WriteLine($"Loaded default rotation: RX={defaultRX}°, RY={defaultRY}°, RZ={defaultRZ}°");
                    }
                    
                    // Load default speed
                    defaultSpeed = config.DefaultSpeed;
                    Console.WriteLine($"Loaded default speed: {defaultSpeed}%");
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                Console.WriteLine("Using default values");
                return false;
            }
        }

        /// <summary>
        /// Initialize the AgileX arm robot control
        /// </summary>
        public bool Initialize()
        {
            if (isInitialized)
                return true;

            try
            {
                // Step 1: Load configuration from params.json
                Console.WriteLine("Loading configuration from params.json...");
                LoadConfiguration();

                // Step 2: Create and initialize CAN interface
                canInterface = new AgileXCanInterface();
                if (!canInterface.Initialize())
                {
                    Console.WriteLine("Failed to initialize CAN interface");
                    return false;
                }

                // Step 3: Create and initialize protocol
                protocol = new AgileXProtocol(canInterface);
                if (!protocol.Initialize())
                {
                    Console.WriteLine("Failed to initialize AgileX protocol");
                    return false;
                }

                // Step 4: Start state update thread
                shouldStop = false;
                updateThread = new Thread(UpdateWorker)
                {
                    Name = "AgileX State Update",
                    IsBackground = true
                };
                updateThread.Start();

                isInitialized = true;
                Console.WriteLine("AgileX Arm Control initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during initialization: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Worker thread to continuously update robot state from CAN feedback
        /// </summary>
        private void UpdateWorker()
        {
            while (!shouldStop)
            {
                try
                {
                    protocol.UpdateState();
                    Thread.Sleep(10); // Update at ~100Hz
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in update worker: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Move the robot arm to the specified position
        /// </summary>
        /// <param name="position">Target position in mm (X, Y, Z)</param>
        public void Goto(Vector3 position)
        {
            Goto(position, defaultSpeed);
        }

        /// <summary>
        /// Move the robot arm to the specified position with custom speed
        /// </summary>
        /// <param name="position">Target position in mm (X, Y, Z)</param>
        /// <param name="speedPercent">Movement speed (0-100)</param>
        public void Goto(Vector3 position, byte speedPercent)
        {
            if (!isInitialized)
            {
                Console.WriteLine("Arm control not initialized. Call Initialize() first.");
                return;
            }

            // Use default rotation values
            Goto(position, defaultRX, defaultRY, defaultRZ, speedPercent);
        }

        /// <summary>
        /// Move the robot arm to the specified position with rotation
        /// </summary>
        /// <param name="position">Target position in mm (X, Y, Z)</param>
        /// <param name="rx">Rotation around X axis in degrees</param>
        /// <param name="ry">Rotation around Y axis in degrees</param>
        /// <param name="rz">Rotation around Z axis in degrees</param>
        /// <param name="speedPercent">Movement speed (0-100)</param>
        public void Goto(Vector3 position, float rx, float ry, float rz, byte speedPercent = 50)
        {
            if (!isInitialized)
            {
                Console.WriteLine("Arm control not initialized. Call Initialize() first.");
                return;
            }

            bool success = protocol.MoveToPosition(
                position.X, 
                position.Y, 
                position.Z, 
                rx, 
                ry, 
                rz, 
                speedPercent);

            if (!success)
            {
                Console.WriteLine($"Failed to send move command to position ({position.X}, {position.Y}, {position.Z})");
            }
        }

        /// <summary>
        /// Get current end effector position
        /// </summary>
        /// <returns>Current position in mm (X, Y, Z)</returns>
        public Vector3 GetPos()
        {
            if (!isInitialized)
            {
                Console.WriteLine("Arm control not initialized. Call Initialize() first.");
                return Vector3.Zero;
            }

            var pose = protocol.CurrentPose;
            return new Vector3(pose.X, pose.Y, pose.Z);
        }

        /// <summary>
        /// Get current end effector rotation
        /// </summary>
        /// <returns>Current rotation in degrees (RX, RY, RZ)</returns>
        public Vector3 GetRotation()
        {
            if (!isInitialized)
            {
                Console.WriteLine("Arm control not initialized. Call Initialize() first.");
                return Vector3.Zero;
            }

            var pose = protocol.CurrentPose;
            return new Vector3(pose.RX, pose.RY, pose.RZ);
        }

        /// <summary>
        /// Get current end effector pose including rotation
        /// </summary>
        /// <returns>Current pose (X, Y, Z, RX, RY, RZ)</returns>
        public EndEffectorPose GetPose()
        {
            if (!isInitialized)
            {
                Console.WriteLine("Arm control not initialized. Call Initialize() first.");
                return default;
            }

            return protocol.CurrentPose;
        }

        /// <summary>
        /// Wait until the robot reaches the target position and stabilizes
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds (0 = no timeout)</param>
        /// <param name="stabilityThreshold">Max variation in mm for stability (default 1.0mm)</param>
        /// <param name="posBufferLength">Number of samples for stability check (default 10)</param>
        /// <returns>True if target reached and stabilized, false if timeout</returns>
        public bool WaitForTarget(int timeoutMs = 0, float stabilityThreshold = 1.0f, int posBufferLength = 10)
        {
            if (!isInitialized)
                return false;

            DateTime startTime = DateTime.Now;
            Queue<Vector3> posBuffer = new Queue<Vector3>();

            while (true)
            {
                var pos = GetPos();
                posBuffer.Enqueue(pos);
                if (posBuffer.Count > posBufferLength)
                    posBuffer.Dequeue();

                if (posBuffer.Count == posBufferLength)
                {
                    float minX = posBuffer.Min(v => v.X);
                    float maxX = posBuffer.Max(v => v.X);
                    float minY = posBuffer.Min(v => v.Y);
                    float maxY = posBuffer.Max(v => v.Y);
                    float minZ = posBuffer.Min(v => v.Z);
                    float maxZ = posBuffer.Max(v => v.Z);

                    if ((maxX - minX) < stabilityThreshold &&
                        (maxY - minY) < stabilityThreshold &&
                        (maxZ - minZ) < stabilityThreshold)
                        return true; // position stabilized
                }

                if (timeoutMs > 0 && (DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                {
                    return false; // Timeout
                }

                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Check if robot has reached target position
        /// </summary>
        public bool HasReachedTarget()
        {
            return isInitialized && protocol.HasReachedTarget();
        }

        /// <summary>
        /// Check if robot is in normal state
        /// </summary>
        public bool IsInNormalState()
        {
            return isInitialized && protocol.IsInNormalState();
        }

        /// <summary>
        /// Emergency stop the robot
        /// </summary>
        public bool EmergencyStop()
        {
            if (!isInitialized)
                return false;

            return protocol.EmergencyStop();
        }

        /// <summary>
        /// Recover from emergency stop
        /// </summary>
        public bool RecoverFromStop()
        {
            if (!isInitialized)
                return false;

            return protocol.RecoverFromStop();
        }

        /// <summary>
        /// Get default position
        /// </summary>
        /// <returns>Default position (X, Y, Z) in mm</returns>
        public Vector3 GetDefaultPosition()
        {
            return new Vector3(defaultX, defaultY, defaultZ);
        }

        /// <summary>
        /// Get default rotation
        /// </summary>
        /// <returns>Default rotation (RX, RY, RZ) in degrees</returns>
        public Vector3 GetDefaultRotation()
        {
            return new Vector3(defaultRX, defaultRY, defaultRZ);
        }

        /// <summary>
        /// Move to default position with default rotation
        /// </summary>
        public void GotoDefault()
        {
            Goto(new Vector3(defaultX, defaultY, defaultZ), defaultRX, defaultRY, defaultRZ, defaultSpeed);
        }

        /// <summary>
        /// Set default position values
        /// </summary>
        public void SetDefaultPosition(float x, float y, float z)
        {
            defaultX = x;
            defaultY = y;
            defaultZ = z;
        }

        /// <summary>
        /// Set default rotation values for Goto method
        /// </summary>
        public void SetDefaultRotation(float rx, float ry, float rz)
        {
            defaultRX = rx;
            defaultRY = ry;
            defaultRZ = rz;
        }

        /// <summary>
        /// Set default movement speed
        /// </summary>
        public void SetDefaultSpeed(byte speedPercent)
        {
            defaultSpeed = Math.Min((byte)100, speedPercent);
        }

        /// <summary>
        /// Get current robot status
        /// </summary>
        public ArmStatus GetStatus()
        {
            if (!isInitialized)
                return default;

            return protocol.CurrentStatus;
        }

        /// <summary>
        /// Cleanup and dispose resources
        /// </summary>
        public void Dispose()
        {
            if (updateThread != null && updateThread.IsAlive)
            {
                shouldStop = true;
                updateThread.Join(2000);
            }

            if (canInterface != null)
            {
                canInterface.Dispose();
            }

            isInitialized = false;
        }
    }
}
