using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using TestCartActivator;

namespace HoloCaliberationDemo
{
    internal class MyArmControl : IDisposable
    {
        // Control AgileX Arm Robot.
        private AgileXCanInterface canInterface;
        private AgileXProtocol protocol;
        private Thread updateThread;
        private volatile bool shouldStop = false;
        private bool isInitialized = false;

        // Default movement speed (0-100 percent)
        private byte defaultSpeed = 50;

        /// <summary>
        /// Initialize the AgileX arm robot control
        /// </summary>
        public bool Initialize()
        {
            if (isInitialized)
                return true;

            try
            {
                // Step 1: Apply in-memory configuration, skip as already applied.

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
                // Console.WriteLine("Arm control not initialized. Call Initialize() first.");
                return;
            }

            // Use default rotation values
            Goto(position, Program.config.InitialRotation[0], Program.config.InitialRotation[1], Program.config.InitialRotation[2], speedPercent);
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
                // Console.WriteLine("Arm control not initialized. Call Initialize() first.");
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
                //Console.WriteLine("Arm control not initialized. Call Initialize() first.");
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
                //Console.WriteLine("Arm control not initialized. Call Initialize() first.");
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
                //Console.WriteLine("Arm control not initialized. Call Initialize() first.");
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


        public bool EnterCANMode()
        {
            return protocol.EnterCanControlMode();
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


        public bool restoreRunning = false;
        /// <summary>
        /// Restore arm state - exits teaching mode, clears errors, enables motors, enters CAN control mode, 
        /// sets current position as target and enters MOVE P mode
        /// </summary>
        /// <param name="speedPercent">Speed percentage for MOVE P mode (default 30)</param>
        public bool RestoreArmState(byte speedPercent = 30)
        {
            if (!isInitialized)
            {
                Console.WriteLine("Cannot restore arm state: not initialized");
                return false;
            }

            if (restoreRunning)
            {
                Console.WriteLine("Restore already in progress, skipping...");
                return false;
            }

            restoreRunning = true;
            try
            {
                return protocol.RestoreArmState(speedPercent);
            }
            finally
            {
                restoreRunning = false;
            }
        }

        /// <summary>
        /// Check if there are any errors
        /// </summary>
        public bool HasErrors()
        {
            return isInitialized && protocol.HasErrors();
        }

        /// <summary>
        /// Get arm state description
        /// </summary>
        public string GetArmStateDescription()
        {
            if (!isInitialized)
                return "Not Initialized";

            return AgileXProtocol.GetArmStateDescription(protocol.CurrentStatus.ArmState);
        }

        /// <summary>
        /// Get control mode description
        /// </summary>
        public string GetControlModeDescription()
        {
            if (!isInitialized)
                return "Not Initialized";

            return AgileXProtocol.GetControlModeDescription(protocol.CurrentStatus.ControlMode);
        }

        /// <summary>
        /// Get teaching state description
        /// </summary>
        public string GetTeachingStateDescription()
        {
            if (!isInitialized)
                return "Not Initialized";

            return AgileXProtocol.GetTeachingStateDescription(protocol.CurrentStatus.TeachingState);
        }

        /// <summary>
        /// Get detailed fault information
        /// </summary>
        public List<string> GetFaultDetails()
        {
            if (!isInitialized)
                return new List<string> { "Not Initialized" };

            return AgileXProtocol.GetFaultDetails(protocol.CurrentStatus.FaultCode);
        }

        /// <summary>
        /// Get fault code
        /// </summary>
        public ushort GetFaultCode()
        {
            if (!isInitialized)
                return 0;

            return protocol.CurrentStatus.FaultCode;
        }

        /// <summary>
        /// Get default position
        /// </summary>
        /// <returns>Default position (X, Y, Z) in mm</returns>
        public Vector3 GetDefaultPosition()
        {
            return new Vector3(Program.config.InitialPosition[0], Program.config.InitialPosition[1], Program.config.InitialPosition[2]);
        }

        /// <summary>
        /// Get default rotation
        /// </summary>
        /// <returns>Default rotation (RX, RY, RZ) in degrees</returns>
        public Vector3 GetDefaultRotation()
        {
            return new Vector3(Program.config.InitialRotation[0], Program.config.InitialRotation[1], Program.config.InitialRotation[2]);
        }

        /// <summary>
        /// Move to default position with default rotation
        /// </summary>
        public void GotoDefault()
        {
            Goto(new Vector3(Program.config.InitialPosition[0], Program.config.InitialPosition[1], Program.config.InitialPosition[2]),
                Program.config.InitialRotation[0], Program.config.InitialRotation[1], Program.config.InitialRotation[2], defaultSpeed);
        }

        /// <summary>
        /// Set default movement speed
        /// </summary>
        public void SetDefaultSpeed(byte speedPercent)
        {
            defaultSpeed = Math.Min((byte)100, speedPercent);
        }

        public byte GetDefaultSpeed() => defaultSpeed;

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
