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
    }
} 