using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TestCartActivator
{
    // CAN Frame structure matching the C++ definition
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CanFrame
    {
        public uint echo_id;         // 0: echo; 0xFFFFFFFF: not echo
        public uint can_id;          // CAN ID
        public byte can_dlc;         // Data Length Code
        public byte channel;         // CAN channel
        public byte flags;           // CAN Rx FIFO Overflow indicator
        public byte reserved;        // Reserved byte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] data;          // CAN data bytes
        public uint timestamp_us;    // Timestamp in microseconds
    }

    // Bit timing structure
    [StructLayout(LayoutKind.Sequential)]
    public struct CanBitTiming
    {
        public uint prop_seg;
        public uint phase_seg1;
        public uint phase_seg2;
        public uint sjw;
        public uint brp;
    }

    // CAN error codes
    [Flags]
    public enum CanErrorCode : uint
    {
        CAN_ERR_BUSOFF = 0x00000001U,        // bus off
        CAN_ERR_RX_TX_WARNING = 0x00000002U, // reached warning level for RX/TX errors
        CAN_ERR_RX_TX_PASSIVE = 0x00000004U, // reached error passive status RX/TX
        CAN_ERR_OVERLOAD = 0x00000008U,      // bus overload
        CAN_ERR_STUFF = 0x00000010U,         // bit stuffing error
        CAN_ERR_FORM = 0x00000020U,          // frame format error
        CAN_ERR_ACK = 0x00000040U,           // received no ACK on transmission
        CAN_ERR_BIT_RECESSIVE = 0x00000080U, // bit recessive error
        CAN_ERR_BIT_DOMINANT = 0x00000100U,  // bit dominant error
        CAN_ERR_CRC = 0x00000200U,           // crc error
    }

    // CAN mode flags
    [Flags]
    public enum CanModeFlags : uint
    {
        CANDO_MODE_NORMAL = 0,
        CANDO_MODE_LISTEN_ONLY = (1 << 0),
        CANDO_MODE_LOOP_BACK = (1 << 1),
        CANDO_MODE_ONE_SHOT = (1 << 3),
        CANDO_MODE_NO_ECHO_BACK = (1 << 8),
    }

    // CAN ID flags
    public static class CanIdFlags
    {
        public const uint CANDO_ID_EXTENDED = 0x80000000;
        public const uint CANDO_ID_RTR = 0x40000000;
        public const uint CANDO_ID_ERR = 0x20000000;
        public const uint CANDO_ID_MASK = 0x1FFFFFFF;
    }

    // CAN interface wrapper for cando.dll
    public class AgileXCanInterface : IDisposable
    {
        // Import cando.dll functions
        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_list_malloc(out IntPtr list);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_list_free(IntPtr list);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_list_scan(IntPtr list);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_list_num(IntPtr list, out byte num);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_malloc(IntPtr list, byte index, out IntPtr hdev);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_free(IntPtr hdev);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_open(IntPtr hdev);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_close(IntPtr hdev);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_set_timing(IntPtr hdev, ref CanBitTiming timing);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_start(IntPtr hdev, uint mode);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_stop(IntPtr hdev);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_frame_send(IntPtr hdev, ref CanFrame frame);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_frame_read(IntPtr hdev, out CanFrame frame, uint timeout_ms);

        // Additional DLL functions for debugging
        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool cando_get_dev_info(IntPtr hdev, out uint fw_version, out uint hw_version);

        [DllImport("cando.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr cando_get_serial_number_str(IntPtr hdev);

        private IntPtr listHandle = IntPtr.Zero;
        private IntPtr deviceHandle = IntPtr.Zero;
        private bool isConnected = false;
        
        // Threading for CAN frame reading
        private Thread readThread;
        private volatile bool shouldStop = false;
        private readonly ConcurrentQueue<CanFrame> receivedFrames = new ConcurrentQueue<CanFrame>();
        private readonly object lockObject = new object();
        
        // Statistics
        public int TotalFramesReceived { get; private set; } = 0;
        public int TotalFramesSent { get; private set; } = 0;
        public DateTime LastFrameTime { get; private set; } = DateTime.MinValue;

        public bool Initialize()
        {
            try
            {
                // Console.WriteLine("=== AgileX CAN Interface Initialization ===");
                
                // Step 1: Malloc list handle
                // Console.WriteLine("Step 1: Allocating device list...");
                if (!cando_list_malloc(out listHandle))
                {
                    // Console.WriteLine("✗ FAILED: Could not allocate device list");
                    return false;
                }
                // Console.WriteLine($"✓ Device list allocated: Handle=0x{listHandle.ToInt64():X}");

                // Step 2: Scan for devices
                // Console.WriteLine("Step 2: Scanning for CAN devices...");
                if (!cando_list_scan(listHandle))
                {
                    // Console.WriteLine("✗ FAILED: Device scan failed");
                    return false;
                }
                // Console.WriteLine("✓ Device scan completed");

                // Step 3: Check device count
                // Console.WriteLine("Step 3: Checking available devices...");
                byte deviceCount;
                if (!cando_list_num(listHandle, out deviceCount))
                {
                    // Console.WriteLine("✗ FAILED: Could not get device count");
                    return false;
                }
                // Console.WriteLine($"✓ Found {deviceCount} CAN device(s)");
                
                if (deviceCount == 0)
                {
                    // Console.WriteLine("✗ FAILED: No CAN devices found");
                    // Console.WriteLine("  • Check USB connection");
                    // Console.WriteLine("  • Verify driver installation");
                    // Console.WriteLine("  • Try reconnecting the device");
                    return false;
                }

                // Step 4: List all available devices
                // Console.WriteLine("Step 4: Enumerating available devices...");
                for (byte i = 0; i < deviceCount; i++)
                {
                    // Console.WriteLine($"  Device [{i}]: Available");
                }

                // Step 5: Allocate first device
                // Console.WriteLine("Step 5: Allocating device handle...");
                if (!cando_malloc(listHandle, 0, out deviceHandle))
                {
                    // Console.WriteLine("✗ FAILED: Could not allocate device handle for device 0");
                    return false;
                }
                // Console.WriteLine($"✓ Device handle allocated: Handle=0x{deviceHandle.ToInt64():X}");

                // Step 6: Open device
                // Console.WriteLine("Step 6: Opening CAN device...");
                if (!cando_open(deviceHandle))
                {
                    // Console.WriteLine("✗ FAILED: Could not open CAN device");
                    // Console.WriteLine("  • Device may be in use by another application");
                    // Console.WriteLine("  • Check device permissions");
                    // Console.WriteLine("  • Try restarting the application");
                    return false;
                }
                // Console.WriteLine("✓ CAN device opened successfully");

                // Step 7: Get device information
                // Console.WriteLine("Step 7: Reading device information...");
                uint fwVersion, hwVersion;
                if (cando_get_dev_info(deviceHandle, out fwVersion, out hwVersion))
                {
                    // Console.WriteLine($"✓ Device Info - FW: 0x{fwVersion:X8}, HW: 0x{hwVersion:X8}");
                }
                else
                {
                    // Console.WriteLine("⚠ Could not read device version info");
                }

                // Try to get serial number
                try
                {
                    IntPtr serialPtr = cando_get_serial_number_str(deviceHandle);
                    if (serialPtr != IntPtr.Zero)
                    {
                        string serial = Marshal.PtrToStringUni(serialPtr);
                        // Console.WriteLine($"✓ Device Serial: {serial}");
                    }
                    else
                    {
                        // Console.WriteLine("⚠ Could not read device serial number");
                    }
                }
                catch
                {
                    // Console.WriteLine("⚠ Serial number read failed");
                }

                // Step 8: Configure bit timing for 1000K baud
                // Console.WriteLine("Step 8: Configuring CAN bit timing...");
                var timing = new CanBitTiming
                {
                    brp = 3,          // Bit rate prescaler
                    phase_seg1 = 12, // Phase segment 1
                    phase_seg2 = 2,  // Phase segment 2
                    sjw = 1,         // Synchronization jump width
                    prop_seg = 1,    // Propagation segment
                };

                // Console.WriteLine($"  • Bit Rate Prescaler (BRP): {timing.brp}");
                // Console.WriteLine($"  • Propagation Segment: {timing.prop_seg}");
                // Console.WriteLine($"  • Phase Segment 1: {timing.phase_seg1}");
                // Console.WriteLine($"  • Phase Segment 2: {timing.phase_seg2}");
                // Console.WriteLine($"  • Sync Jump Width: {timing.sjw}");
                
                // Calculate theoretical baud rate
                uint totalTimeQuanta = timing.prop_seg + timing.phase_seg1 + timing.phase_seg2 + 1; // +1 for sync segment
                // Console.WriteLine($"  • Total Time Quanta: {totalTimeQuanta}");
                // Console.WriteLine($"  • Target Baud Rate: 1000 kbps");

                if (!cando_set_timing(deviceHandle, ref timing))
                {
                    // Console.WriteLine("✗ FAILED: Could not set CAN bit timing");
                    // Console.WriteLine("  • Timing parameters may be invalid");
                    // Console.WriteLine("  • Device may not support 1000K baud rate");
                    return false;
                }
                // Console.WriteLine("✓ CAN bit timing configured for 1000K baud");

                // Step 9: Start CAN interface
                // Console.WriteLine("Step 9: Starting CAN interface...");
                uint mode = (uint)CanModeFlags.CANDO_MODE_NORMAL;
                // Console.WriteLine($"  • Mode: NORMAL (0x{mode:X})");
                
                if (!cando_start(deviceHandle, mode))
                {
                    // Console.WriteLine("✗ FAILED: Could not start CAN interface");
                    // Console.WriteLine("  • Check CAN bus termination");
                    // Console.WriteLine("  • Verify other nodes on the bus");
                    // Console.WriteLine("  • Check cable connections");
                    return false;
                }
                // Console.WriteLine("✓ CAN interface started successfully");

                // Step 10: Start reading thread
                // Console.WriteLine("Step 10: Starting CAN frame reading thread...");
                shouldStop = false;
                readThread = new Thread(ReadFramesWorker)
                {
                    Name = "CAN Frame Reader",
                    IsBackground = true
                };
                readThread.Start();
                // Console.WriteLine("✓ CAN reading thread started");

                isConnected = true;
                // Console.WriteLine("=== CAN Interface Initialization SUCCESSFUL ===");
                // Console.WriteLine($"  • Device: Ready for communication");
                // Console.WriteLine($"  • Baud Rate: 1000 kbps");
                // Console.WriteLine($"  • Mode: Normal operation");
                // Console.WriteLine($"  • Reading Thread: Active");
                // Console.WriteLine("==============================================");
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ CRITICAL ERROR during CAN initialization: {ex.Message}");
                Console.WriteLine($"  • Exception Type: {ex.GetType().Name}");
                Console.WriteLine($"  • Stack Trace: {ex.StackTrace}");
                return false;
            }
        }

        private void ReadFramesWorker()
        {
            // Console.WriteLine("CAN Reading Thread: Started");
            CanFrame frame;
            int consecutiveTimeouts = 0;
            const int maxTimeouts = 200; // Allow more timeouts before warning
            
            while (!shouldStop)
            {
                try
                {
                    if (cando_frame_read(deviceHandle, out frame, 50)) // 50ms timeout
                    {
                        // Successfully received a frame
                        receivedFrames.Enqueue(frame);
                        TotalFramesReceived++;
                        LastFrameTime = DateTime.Now;
                        consecutiveTimeouts = 0;
                        
                        // Minimal debug output - only for unknown frames
                        // uint canId = frame.can_id & CanIdFlags.CANDO_ID_MASK;
                        // // Console.WriteLine($"RX: ID=0x{canId:X3} DLC={frame.can_dlc}");
                    }
                    else
                    {
                        // Timeout occurred
                        consecutiveTimeouts++;
                        if (consecutiveTimeouts == maxTimeouts)
                        {
                            // Console.WriteLine($"CAN Reading: {consecutiveTimeouts} timeouts (no CAN traffic)");
                            consecutiveTimeouts = 0; // Reset to avoid spam
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Console.WriteLine($"CAN Reading Thread Error: {ex.Message}");
                    Thread.Sleep(100); // Wait before retrying
                }
            }
            
            // Console.WriteLine("CAN Reading Thread: Stopped");
        }

        public bool SendFrame(uint canId, byte[] data)
        {
            if (!isConnected || data.Length > 8)
            {
                // Console.WriteLine($"TX FAILED: Not connected or data too long (len={data.Length})");
                return false;
            }

            var frame = new CanFrame
            {
                echo_id = 0,
                can_id = canId,
                can_dlc = (byte)data.Length,
                channel = 0,
                flags = 0,
                reserved = 0,
                data = new byte[8],
                timestamp_us = 0
            };

            // Copy data to frame
            Array.Copy(data, frame.data, data.Length);
            
            lock (lockObject)
            {
                var success = cando_frame_send(deviceHandle, ref frame);
                if (success)
                {
                    TotalFramesSent++;
                }
                return success;
            }
        }

        public bool GetNextReceivedFrame(out CanFrame frame)
        {
            return receivedFrames.TryDequeue(out frame);
        }

        public int GetReceivedFrameCount()
        {
            return receivedFrames.Count;
        }

        public void PrintStatistics()
        {
            // Console.WriteLine("=== CAN Interface Statistics ===");
            // Console.WriteLine($"Frames Sent: {TotalFramesSent}");
            // Console.WriteLine($"Frames Received: {TotalFramesReceived}");
            // Console.WriteLine($"Queued Frames: {receivedFrames.Count}");
            // Console.WriteLine($"Last Frame: {(LastFrameTime == DateTime.MinValue ? "Never" : LastFrameTime.ToString("HH:mm:ss.fff"))}");
            // Console.WriteLine($"Thread Status: {(readThread?.IsAlive == true ? "Running" : "Stopped")}");
            // Console.WriteLine("================================");
        }

        public void Dispose()
        {
            // Console.WriteLine("=== CAN Interface Shutdown ===");
            
            // Stop reading thread
            if (readThread != null && readThread.IsAlive)
            {
                // Console.WriteLine("Stopping CAN reading thread...");
                shouldStop = true;
                if (!readThread.Join(2000)) // Wait up to 2 seconds
                {
                    // Console.WriteLine("⚠ Reading thread did not stop gracefully");
                }
                else
                {
                    // Console.WriteLine("✓ Reading thread stopped");
                }
            }

            // Stop and close CAN interface
            if (isConnected && deviceHandle != IntPtr.Zero)
            {
                // Console.WriteLine("Stopping CAN interface...");
                cando_stop(deviceHandle);
                // Console.WriteLine("Closing CAN device...");
                cando_close(deviceHandle);
                isConnected = false;
                // Console.WriteLine("✓ CAN interface closed");
            }

            // Free device handle
            if (deviceHandle != IntPtr.Zero)
            {
                // Console.WriteLine("Freeing device handle...");
                cando_free(deviceHandle);
                deviceHandle = IntPtr.Zero;
                // Console.WriteLine("✓ Device handle freed");
            }

            // Free list handle
            if (listHandle != IntPtr.Zero)
            {
                // Console.WriteLine("Freeing device list...");
                cando_list_free(listHandle);
                listHandle = IntPtr.Zero;
                // Console.WriteLine("✓ Device list freed");
            }

            PrintStatistics();
            // Console.WriteLine("=== CAN Interface Shutdown Complete ===");
        }
    }
} 