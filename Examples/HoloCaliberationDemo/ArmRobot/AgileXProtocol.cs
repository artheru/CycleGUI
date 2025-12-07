using System;

namespace TestCartActivator
{
    // AgileX robot arm status feedback structure
    public struct ArmStatus
    {
        public byte ControlMode;         // Control mode (0x00: standby, 0x01: CAN control, etc.)
        public byte ArmState;           // Arm state (0x00: normal, 0x01: emergency stop, etc.)
        public byte ModeResponse;       // Move mode response (0x00: MOVE P, 0x01: MOVE J, etc.)
        public byte TeachingState;      // Teaching state
        public byte MovementState;      // Movement state (0x00: reached, 0x01: not reached)
        public byte TrajectoryPoint;    // Current trajectory point number
        public ushort FaultCode;        // Fault code
    }

    // End effector position structure
    public struct EndEffectorPose
    {
        public float X;     // X coordinate in mm
        public float Y;     // Y coordinate in mm  
        public float Z;     // Z coordinate in mm
        public float RX;    // RX rotation in degrees
        public float RY;    // RY rotation in degrees
        public float RZ;    // RZ rotation in degrees
    }

    // Joint angles structure
    public struct JointAngles
    {
        public float J1;    // Joint 1 angle in degrees
        public float J2;    // Joint 2 angle in degrees
        public float J3;    // Joint 3 angle in degrees
        public float J4;    // Joint 4 angle in degrees
        public float J5;    // Joint 5 angle in degrees
        public float J6;    // Joint 6 angle in degrees
    }

    // Gripper state structure
    public struct GripperState
    {
        public float Position;   // Gripper position in mm
        public float Torque;     // Gripper torque in N/m
        public byte StatusCode;  // Status code
    }

    // AgileX protocol implementation
    public class AgileXProtocol
    {
        private AgileXCanInterface canInterface;

        // CAN IDs from the protocol specification
        private const uint ARM_STATUS_FEEDBACK = 0x2A1;
        private const uint END_POSE_FEEDBACK_1 = 0x2A2;
        private const uint END_POSE_FEEDBACK_2 = 0x2A3;
        private const uint END_POSE_FEEDBACK_3 = 0x2A4;
        private const uint ARM_JOINT_FEEDBACK_12 = 0x2A5;
        private const uint ARM_JOINT_FEEDBACK_34 = 0x2A6;
        private const uint ARM_JOINT_FEEDBACK_56 = 0x2A7;
        private const uint GRIPPER_FEEDBACK = 0x2A8;

        private const uint QUICK_STOP_CMD = 0x150;
        private const uint CONTROL_MODE_CMD = 0x151;
        private const uint CARTESIAN_CMD_1 = 0x152;
        private const uint CARTESIAN_CMD_2 = 0x153;
        private const uint CARTESIAN_CMD_3 = 0x154;
        private const uint JOINT_CMD_12 = 0x155;
        private const uint JOINT_CMD_34 = 0x156;
        private const uint JOINT_CMD_56 = 0x157;
        private const uint ARC_POINT_CMD = 0x158;
        private const uint GRIPPER_CMD = 0x159;
        private const uint MOTOR_ENABLE_CMD = 0x471;
        private const uint JOINT_SETTING_CMD = 0x475;

        // Current robot state
        public ArmStatus CurrentStatus { get; private set; }
        public EndEffectorPose CurrentPose { get; private set; }
        public JointAngles CurrentJoints { get; private set; }
        public GripperState CurrentGripper { get; private set; }

        // Statistics
        public int ProcessedFramesCount { get; private set; } = 0;
        public DateTime LastFeedbackTime { get; private set; } = DateTime.MinValue;

        public AgileXProtocol(AgileXCanInterface canInterface)
        {
            this.canInterface = canInterface;
        }

        // Initialize the robot arm
        public bool Initialize()
        {
            Console.WriteLine("Initializing AgileX Protocol...");
            
            // Step 1: Enable all joint motors
            if (!EnableAllMotors())
            {
                Console.WriteLine("✗ FAILED: Could not enable motors");
                return false;
            }

            // Wait for motor enable to take effect
            System.Threading.Thread.Sleep(500);

            // Step 2: Enter CAN control mode
            if (!EnterCanControlMode())
            {
                Console.WriteLine("✗ FAILED: Could not enter CAN control mode");
                return false;
            }

            // Wait for mode change to take effect
            System.Threading.Thread.Sleep(500);

            Console.WriteLine("✓ AgileX Protocol Ready");
            return true;
        }

        // Enable all joint motors (ID: 0x471)
        public bool EnableAllMotors()
        {
            var data = new byte[8];
            data[0] = 7;    // Joint number (7 = all joints)
            data[1] = 2;    // Enable (2 = enable, 1 = disable)
            // data[2-7] = 0 (reserved)

            return canInterface.SendFrame(MOTOR_ENABLE_CMD, data);
        }

        // Enter CAN control mode (ID: 0x151)
        public bool EnterCanControlMode()
        {
            var data = new byte[8];
            data[0] = 1;    // Control mode (1 = CAN control mode)
            // data[1-7] = 0 (default values)

            return canInterface.SendFrame(CONTROL_MODE_CMD, data);
        }

        // Emergency stop (ID: 0x150)
        public bool EmergencyStop()
        {
            var data = new byte[8];
            data[0] = 1;    // Quick stop (1 = emergency stop)
            // data[1-7] = 0

            // Console.WriteLine("EMERGENCY STOP: Sending emergency stop command");
            return canInterface.SendFrame(QUICK_STOP_CMD, data);
        }

        // Recover from emergency stop (ID: 0x150)
        public bool RecoverFromStop()
        {
            var data = new byte[8];
            data[0] = 2;    // Recover (2 = recover from stop)
            // data[1-7] = 0

            // Console.WriteLine("RECOVERY: Sending recovery command");
            return canInterface.SendFrame(QUICK_STOP_CMD, data);
        }

        // Move to cartesian position (Point mode)
        public bool MoveToPosition(float x, float y, float z, float rx, float ry, float rz, byte speedPercent = 50)
        {
            // Send cartesian coordinates
            if (!SendCartesianPosition(x, y, z, rx, ry, rz))
            {
                return false;
            }

            // Start MOVE P mode
            var data = new byte[8];
            data[0] = 1;    // Control mode (1 = CAN control)
            data[1] = 0;    // MOVE mode (0 = MOVE P)
            data[2] = speedPercent; // Speed percentage (0-100)
            // data[3-7] = 0

            return canInterface.SendFrame(CONTROL_MODE_CMD, data);
        }

        // Move to joint angles (Joint mode)
        public bool MoveToJointAngles(float j1, float j2, float j3, float j4, float j5, float j6, byte speedPercent = 50)
        {
            // Send joint angles
            if (!SendJointAngles(j1, j2, j3, j4, j5, j6))
            {
                return false;
            }

            // Start MOVE J mode
            var data = new byte[8];
            data[0] = 1;    // Control mode (1 = CAN control)
            data[1] = 1;    // MOVE mode (1 = MOVE J)
            data[2] = speedPercent; // Speed percentage (0-100)
            // data[3-7] = 0

            return canInterface.SendFrame(CONTROL_MODE_CMD, data);
        }

        // Send cartesian position commands
        private bool SendCartesianPosition(float x, float y, float z, float rx, float ry, float rz)
        {
            // Convert to protocol units (0.001mm for position, 0.001° for rotation)
            int xInt = (int)(x * 1000);
            int yInt = (int)(y * 1000);
            int zInt = (int)(z * 1000);
            int rxInt = (int)(rx * 1000);
            int ryInt = (int)(ry * 1000);
            int rzInt = (int)(rz * 1000);

            //Console.WriteLine($"Cartesian conversion: X={xInt}, Y={yInt}, Z={zInt}, RX={rxInt}, RY={ryInt}, RZ={rzInt}");

            // Send X, Y coordinates (ID: 0x152)
            var data1 = new byte[8];
            Array.Copy(BitConverter.GetBytes(xInt), 0, data1, 0, 4);
            Array.Copy(BitConverter.GetBytes(yInt), 0, data1, 4, 4);
            // Convert to big-endian (Motorola format)
            ReverseBytes(data1, 0, 4);
            ReverseBytes(data1, 4, 4);
            var success1 = canInterface.SendFrame(CARTESIAN_CMD_1, data1);
            //Console.WriteLine($"Cartesian CMD 1 (X,Y): {(success1 ? "SUCCESS" : "FAILED")}");

            // Send Z, RX coordinates (ID: 0x153)
            var data2 = new byte[8];
            Array.Copy(BitConverter.GetBytes(zInt), 0, data2, 0, 4);
            Array.Copy(BitConverter.GetBytes(rxInt), 0, data2, 4, 4);
            // Convert to big-endian (Motorola format)
            ReverseBytes(data2, 0, 4);
            ReverseBytes(data2, 4, 4);
            var success2 = canInterface.SendFrame(CARTESIAN_CMD_2, data2);
            //Console.WriteLine($"Cartesian CMD 2 (Z,RX): {(success2 ? "SUCCESS" : "FAILED")}");

            // Send RY, RZ coordinates (ID: 0x154)
            var data3 = new byte[8];
            Array.Copy(BitConverter.GetBytes(ryInt), 0, data3, 0, 4);
            Array.Copy(BitConverter.GetBytes(rzInt), 0, data3, 4, 4);
            // Convert to big-endian (Motorola format)
            ReverseBytes(data3, 0, 4);
            ReverseBytes(data3, 4, 4);
            var success3 = canInterface.SendFrame(CARTESIAN_CMD_3, data3);
            //Console.WriteLine($"Cartesian CMD 3 (RY,RZ): {(success3 ? "SUCCESS" : "FAILED")}");
            
            return success1 && success2 && success3;
        }

        // Send joint angle commands
        private bool SendJointAngles(float j1, float j2, float j3, float j4, float j5, float j6)
        {
            // Convert to protocol units (0.001°)
            int j1Int = (int)(j1 * 1000);
            int j2Int = (int)(j2 * 1000);
            int j3Int = (int)(j3 * 1000);
            int j4Int = (int)(j4 * 1000);
            int j5Int = (int)(j5 * 1000);
            int j6Int = (int)(j6 * 1000);

            // Console.WriteLine($"Joint conversion: J1={j1Int}, J2={j2Int}, J3={j3Int}, J4={j4Int}, J5={j5Int}, J6={j6Int}");

            // Send J1, J2 angles (ID: 0x155)
            var data1 = new byte[8];
            Array.Copy(BitConverter.GetBytes(j1Int), 0, data1, 0, 4);
            Array.Copy(BitConverter.GetBytes(j2Int), 0, data1, 4, 4);
            ReverseBytes(data1, 0, 4);
            ReverseBytes(data1, 4, 4);
            var success1 = canInterface.SendFrame(JOINT_CMD_12, data1);
            // Console.WriteLine($"Joint CMD 1 (J1,J2): {(success1 ? "SUCCESS" : "FAILED")}");

            // Send J3, J4 angles (ID: 0x156)
            var data2 = new byte[8];
            Array.Copy(BitConverter.GetBytes(j3Int), 0, data2, 0, 4);
            Array.Copy(BitConverter.GetBytes(j4Int), 0, data2, 4, 4);
            ReverseBytes(data2, 0, 4);
            ReverseBytes(data2, 4, 4);
            var success2 = canInterface.SendFrame(JOINT_CMD_34, data2);
            // Console.WriteLine($"Joint CMD 2 (J3,J4): {(success2 ? "SUCCESS" : "FAILED")}");

            // Send J5, J6 angles (ID: 0x157)
            var data3 = new byte[8];
            Array.Copy(BitConverter.GetBytes(j5Int), 0, data3, 0, 4);
            Array.Copy(BitConverter.GetBytes(j6Int), 0, data3, 4, 4);
            ReverseBytes(data3, 0, 4);
            ReverseBytes(data3, 4, 4);
            var success3 = canInterface.SendFrame(JOINT_CMD_56, data3);
            // Console.WriteLine($"Joint CMD 3 (J5,J6): {(success3 ? "SUCCESS" : "FAILED")}");
            
            return success1 && success2 && success3;
        }

        // Update robot state by reading feedback from the threaded interface
        public bool UpdateState()
        {
            CanFrame frame;
            int processedThisUpdate = 0; 
            
            // Process all available frames from the queue
            while (canInterface.GetNextReceivedFrame(out frame))
            {
                ProcessFeedback(frame);
                processedThisUpdate++;
                ProcessedFramesCount++;
                LastFeedbackTime = DateTime.Now;
            }
            
            // Optionally log if we processed frames
            if (processedThisUpdate > 0)
            {
                // Console.WriteLine($"Protocol: Processed {processedThisUpdate} feedback frames (Total: {ProcessedFramesCount})");
            }
            
            return true;
        }

        // Process received feedback frames
        private void ProcessFeedback(CanFrame frame)
        {
            uint canId = frame.can_id & CanIdFlags.CANDO_ID_MASK;

            switch (canId)
            {
                case ARM_STATUS_FEEDBACK:
                    ProcessArmStatusFeedback(frame.data);
                    // Only log status changes, not every frame
                    break;
                case END_POSE_FEEDBACK_1:
                    ProcessEndPoseFeedback1(frame.data);
                    break;
                case END_POSE_FEEDBACK_2:
                    ProcessEndPoseFeedback2(frame.data);
                    break;
                case END_POSE_FEEDBACK_3:
                    ProcessEndPoseFeedback3(frame.data);
                    // Only log complete pose updates
                    break;
                case ARM_JOINT_FEEDBACK_12:
                    ProcessJointFeedback12(frame.data);
                    break;
                case ARM_JOINT_FEEDBACK_34:
                    ProcessJointFeedback34(frame.data);
                    break;
                case ARM_JOINT_FEEDBACK_56:
                    ProcessJointFeedback56(frame.data);
                    // Only log complete joint updates
                    break;
                case GRIPPER_FEEDBACK:
                    ProcessGripperFeedback(frame.data);
                    break;
                default:
                    // Don't log unknown frames - too verbose
                    break;
            }
        }

        private void ProcessArmStatusFeedback(byte[] data)
        {
            var status = CurrentStatus;
            status.ControlMode = data[0];
            status.ArmState = data[1];
            status.ModeResponse = data[2];
            status.TeachingState = data[3];
            status.MovementState = data[4];
            status.TrajectoryPoint = data[5];
            status.FaultCode = (ushort)((data[6] << 8) | data[7]);
            CurrentStatus = status;
        }

        private void ProcessEndPoseFeedback1(byte[] data)
        {
            var pose = CurrentPose;
            // Extract X, Y coordinates (big-endian format, 0.001mm units)
            pose.X = ReadBigEndianInt32(data, 0) / 1000.0f;
            pose.Y = ReadBigEndianInt32(data, 4) / 1000.0f;
            CurrentPose = pose;
        }

        private void ProcessEndPoseFeedback2(byte[] data)
        {
            var pose = CurrentPose;
            // Extract Z, RX coordinates (big-endian format)
            pose.Z = ReadBigEndianInt32(data, 0) / 1000.0f;
            pose.RX = ReadBigEndianInt32(data, 4) / 1000.0f;
            CurrentPose = pose;
        }

        private void ProcessEndPoseFeedback3(byte[] data)
        {
            var pose = CurrentPose;
            // Extract RY, RZ coordinates (big-endian format)
            pose.RY = ReadBigEndianInt32(data, 0) / 1000.0f;
            pose.RZ = ReadBigEndianInt32(data, 4) / 1000.0f;
            CurrentPose = pose;
        }

        private void ProcessJointFeedback12(byte[] data)
        {
            var joints = CurrentJoints;
            // Extract J1, J2 angles (big-endian format, 0.001° units)
            joints.J1 = ReadBigEndianInt32(data, 0) / 1000.0f;
            joints.J2 = ReadBigEndianInt32(data, 4) / 1000.0f;
            CurrentJoints = joints;
        }

        private void ProcessJointFeedback34(byte[] data)
        {
            var joints = CurrentJoints;
            // Extract J3, J4 angles (big-endian format)
            joints.J3 = ReadBigEndianInt32(data, 0) / 1000.0f;
            joints.J4 = ReadBigEndianInt32(data, 4) / 1000.0f;
            CurrentJoints = joints;
        }

        private void ProcessJointFeedback56(byte[] data)
        {
            var joints = CurrentJoints;
            // Extract J5, J6 angles (big-endian format)
            joints.J5 = ReadBigEndianInt32(data, 0) / 1000.0f;
            joints.J6 = ReadBigEndianInt32(data, 4) / 1000.0f;
            CurrentJoints = joints;
        }

        private void ProcessGripperFeedback(byte[] data)
        {
            var gripper = CurrentGripper;
            // Extract gripper position and torque (big-endian format)
            gripper.Position = ReadBigEndianInt32(data, 0) / 1000.0f;
            gripper.Torque = ReadBigEndianInt16(data, 4) / 1000.0f;
            gripper.StatusCode = data[6];
            CurrentGripper = gripper;
        }

        // Helper methods for big-endian data conversion
        private int ReadBigEndianInt32(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private short ReadBigEndianInt16(byte[] data, int offset)
        {
            return (short)((data[offset] << 8) | data[offset + 1]);
        }

        private void ReverseBytes(byte[] array, int offset, int length)
        {
            Array.Reverse(array, offset, length);
        }

        // Check if robot has reached target position
        public bool HasReachedTarget()
        {
            return CurrentStatus.MovementState == 0; // 0 = reached target position
        }

        // Check if robot is in normal state
        public bool IsInNormalState()
        {
            return CurrentStatus.ArmState == 0; // 0 = normal state
        }

        // Get protocol statistics
        public void PrintStatistics()
        {
            // Console.WriteLine("=== AgileX Protocol Statistics ===");
            // Console.WriteLine($"Processed Frames: {ProcessedFramesCount}");
            // Console.WriteLine($"Last Feedback: {(LastFeedbackTime == DateTime.MinValue ? "Never" : LastFeedbackTime.ToString("HH:mm:ss.fff"))}");
            // Console.WriteLine($"Current Status: Mode={CurrentStatus.ControlMode}, State={CurrentStatus.ArmState}, Moving={CurrentStatus.MovementState}");
            // Console.WriteLine($"Current Position: X={CurrentPose.X:F1}, Y={CurrentPose.Y:F1}, Z={CurrentPose.Z:F1}");
            // Console.WriteLine($"Current Rotation: RX={CurrentPose.RX:F1}°, RY={CurrentPose.RY:F1}°, RZ={CurrentPose.RZ:F1}°");
            // Console.WriteLine("==================================");
        }

        // Control gripper (ID: 0x159)
        public bool ControlGripper(float position, float force)
        {
            // Convert to protocol units (0.001mm for position, 0.001N for force)
            int positionInt = (int)(position * 1000);
            int forceInt = (int)(force * 1000);

            var data = new byte[8];
            Array.Copy(BitConverter.GetBytes(positionInt), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes((short)forceInt), 0, data, 4, 2);
            // data[6-7] = 0 (reserved)

            // Convert to big-endian (Motorola format)
            ReverseBytes(data, 0, 4);
            ReverseBytes(data, 4, 2);
            data[6] = 1;

            return canInterface.SendFrame(GRIPPER_CMD, data);
        }

        // Clear joint errors (ID: 0x475)
        public bool ClearJointErrors(byte jointNumber = 7)
        {
            var data = new byte[8];
            data[0] = jointNumber;  // Joint number (7 = all joints)
            data[1] = 0;            // Not setting zero point
            data[2] = 0;            // Acceleration setting not effective
            data[3] = 0x7F;         // Invalid value for max acceleration H
            data[4] = 0xFF;         // Invalid value for max acceleration L
            data[5] = 0xAE;         // Clear joint error code (valid value)
            // data[6-7] = 0 (reserved)

            Console.WriteLine($"Clearing errors for joint(s): {(jointNumber == 7 ? "ALL" : jointNumber.ToString())}");
            return canInterface.SendFrame(JOINT_SETTING_CMD, data);
        }

        // Exit teaching mode (ID: 0x150)
        public bool ExitTeachingMode()
        {
            var data = new byte[8];
            data[0] = 0;    // No quick stop
            data[1] = 0;    // No trajectory command
            data[2] = 0x06; // Terminate teaching execution
            // data[3-7] = 0

            Console.WriteLine("Exiting teaching mode...");
            return canInterface.SendFrame(QUICK_STOP_CMD, data);
        }

        // Enter standby mode (ID: 0x151)
        public bool EnterStandbyMode()
        {
            var data = new byte[8];
            data[0] = 0;    // Standby mode
            // data[1-7] = 0

            Console.WriteLine("Entering standby mode...");
            return canInterface.SendFrame(CONTROL_MODE_CMD, data);
        }

        // Restore arm state - comprehensive recovery function
        public bool RestoreArmState(byte speedPercent = 30)
        {
            Console.WriteLine("=== Restoring Arm State ===");
            bool success = true;

            // Step 1: Exit teaching mode if in teaching state
            if (CurrentStatus.TeachingState != 0 || CurrentStatus.ControlMode == 0x02)
            {
                Console.WriteLine("Step 1: Exiting teaching mode...");
                if (!ExitTeachingMode())
                {
                    Console.WriteLine("Warning: Failed to exit teaching mode");
                    success = false;
                }
                System.Threading.Thread.Sleep(200);
            }
            else
            {
                Console.WriteLine("Step 1: Not in teaching mode, skipping...");
            }

            // Step 2: Recover from emergency stop if in emergency stop state
            if (CurrentStatus.ArmState == 0x01) // Emergency stop state
            {
                Console.WriteLine("Step 2: Recovering from emergency stop...");
                if (!RecoverFromStop())
                {
                    Console.WriteLine("Warning: Failed to recover from emergency stop");
                    success = false;
                }
                System.Threading.Thread.Sleep(200);
            }
            else
            {
                Console.WriteLine("Step 2: Not in emergency stop, skipping...");
            }

            // Step 3: Clear all joint errors
            Console.WriteLine("Step 3: Clearing joint errors...");
            if (!ClearJointErrors(7))
            {
                Console.WriteLine("Warning: Failed to clear joint errors");
                success = false;
            }
            System.Threading.Thread.Sleep(200);

            // Step 4: Enable all motors
            Console.WriteLine("Step 4: Enabling all motors...");
            if (!EnableAllMotors())
            {
                Console.WriteLine("Warning: Failed to enable motors");
                success = false;
            }
            System.Threading.Thread.Sleep(500);

            // Step 5: Enter CAN control mode (0x151 byte0=1, byte1~byte7=0)
            Console.WriteLine("Step 5: Entering CAN control mode...");
            if (!EnterCanControlMode())
            {
                Console.WriteLine("Warning: Failed to enter CAN control mode");
                success = false;
            }
            System.Threading.Thread.Sleep(200);

            // Step 6: Send current position as target position (0x152, 0x153, 0x154)
            // This maintains the arm at its current position
            Console.WriteLine("Step 6: Setting current position as target...");
            var currentPose = CurrentPose;
            Console.WriteLine($"  Current pose: X={currentPose.X:F1}, Y={currentPose.Y:F1}, Z={currentPose.Z:F1}, " +
                              $"RX={currentPose.RX:F1}, RY={currentPose.RY:F1}, RZ={currentPose.RZ:F1}");
            if (!SendCartesianPositionPublic(currentPose.X, currentPose.Y, currentPose.Z, 
                                              currentPose.RX, currentPose.RY, currentPose.RZ))
            {
                Console.WriteLine("Warning: Failed to set current position as target");
                success = false;
            }
            System.Threading.Thread.Sleep(100);

            // Step 7: Enter MOVE P mode with speed percentage (0x151 byte0=1, byte1=0, byte2=speed)
            Console.WriteLine($"Step 7: Entering MOVE P mode with speed {speedPercent}%...");
            if (!EnterMovePMode(speedPercent))
            {
                Console.WriteLine("Warning: Failed to enter MOVE P mode");
                success = false;
            }
            System.Threading.Thread.Sleep(200);

            Console.WriteLine($"=== Arm State Restore {(success ? "COMPLETED" : "COMPLETED WITH WARNINGS")} ===");
            return success;
        }

        // Enter MOVE P mode with speed (ID: 0x151)
        public bool EnterMovePMode(byte speedPercent = 50)
        {
            var data = new byte[8];
            data[0] = 1;              // Control mode (1 = CAN control mode)
            data[1] = 0;              // MOVE mode (0 = MOVE P)
            data[2] = speedPercent;   // Speed percentage (0-100)
            // data[3-7] = 0 (default values)

            Console.WriteLine($"Entering MOVE P mode with speed {speedPercent}%");
            return canInterface.SendFrame(CONTROL_MODE_CMD, data);
        }

        // Public wrapper for SendCartesianPosition
        public bool SendCartesianPositionPublic(float x, float y, float z, float rx, float ry, float rz)
        {
            return SendCartesianPosition(x, y, z, rx, ry, rz);
        }

        // Get arm state description
        public static string GetArmStateDescription(byte armState)
        {
            return armState switch
            {
                0x00 => "正常 (Normal)",
                0x01 => "急停 (Emergency Stop)",
                0x02 => "无解 (No Solution)",
                0x03 => "奇异点 (Singularity)",
                0x04 => "目标角度超限 (Target Angle Exceeded)",
                0x05 => "关节通信异常 (Joint Communication Error)",
                0x06 => "关节抱闸未打开 (Joint Brake Not Released)",
                0x07 => "机械臂发生碰撞 (Collision Detected)",
                0x08 => "拖动示教时超速 (Teaching Overspeed)",
                0x09 => "关节状态异常 (Joint State Abnormal)",
                0x0A => "其它异常 (Other Error)",
                0x0B => "示教记录 (Teaching Recording)",
                0x0C => "示教执行 (Teaching Executing)",
                0x0D => "示教暂停 (Teaching Paused)",
                0x0E => "主控NTC过温 (Controller Overtemp)",
                0x0F => "释放电阻NTC过温 (Resistor Overtemp)",
                _ => $"未知状态 (Unknown: 0x{armState:X2})"
            };
        }

        // Get control mode description
        public static string GetControlModeDescription(byte controlMode)
        {
            return controlMode switch
            {
                0x00 => "待机模式 (Standby)",
                0x01 => "CAN控制模式 (CAN Control)",
                0x02 => "示教模式 (Teaching)",
                0x03 => "以太网控制 (Ethernet)",
                0x04 => "WiFi控制 (WiFi)",
                0x05 => "遥控器控制 (Remote)",
                0x06 => "联动示教输入 (Linked Teaching)",
                0x07 => "离线轨迹模式 (Offline Trajectory)",
                _ => $"未知模式 (Unknown: 0x{controlMode:X2})"
            };
        }

        // Get teaching state description
        public static string GetTeachingStateDescription(byte teachingState)
        {
            return teachingState switch
            {
                0x00 => "关闭 (Closed)",
                0x01 => "开始示教记录 (Recording)",
                0x02 => "结束示教记录 (Record Ended)",
                0x03 => "执行示教轨迹 (Executing)",
                0x04 => "暂停执行 (Paused)",
                0x05 => "继续执行 (Continuing)",
                0x06 => "终止执行 (Terminated)",
                0x07 => "运动到轨迹起点 (Moving to Start)",
                _ => $"未知 (Unknown: 0x{teachingState:X2})"
            };
        }

        // Get detailed fault information from fault code
        public static List<string> GetFaultDetails(ushort faultCode)
        {
            var faults = new List<string>();
            
            // byte[7] - Joint communication errors (lower byte)
            byte commErrors = (byte)(faultCode >> 8);
            for (int i = 0; i < 6; i++)
            {
                if ((commErrors & (1 << i)) != 0)
                    faults.Add($"关节{i + 1}通信异常 (J{i + 1} Comm Error)");
            }

            // byte[6] - Joint angle limit exceeded (upper byte)
            byte limitErrors = (byte)(faultCode & 0xFF);
            for (int i = 0; i < 6; i++)
            {
                if ((limitErrors & (1 << i)) != 0)
                    faults.Add($"关节{i + 1}角度超限 (J{i + 1} Angle Limit)");
            }

            return faults;
        }

        // Check if there are any errors
        public bool HasErrors()
        {
            return CurrentStatus.ArmState != 0x00 || CurrentStatus.FaultCode != 0;
        }
    }
} 