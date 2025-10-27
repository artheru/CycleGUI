using System;
using System.Runtime.InteropServices;
using CycleGUI;
using CycleGUI.API;
using OpenCvSharp;

namespace HoloCaliberationDemo.Camera
{
    internal class MonoEyeCamera2
    {
        private VideoCapture camera;
        private PutRGBA streamer;
        private Action<byte[]> updater;

        private byte[][] cached = new byte[2][];
        private int cachedId = 0;

        public int width = 1280;
        public int height = 720;

        private volatile bool isRunning = false;
        
        // Frame statistics
        private int frameCount = 0;
        private DateTime lastFpsTime = DateTime.Now;

        public int FPS = 0;
        public bool IsActive { get; private set; } = false;
        public string Name { get; private set; }

        // Helper thread for async image processing
        private Thread captureThread;

        public MonoEyeCamera2(string name)
        {
            Name = name;
        }

        public void Initialize(int cameraIndex)
        {
            if (isRunning)
            {
                Console.WriteLine($"{Name}: Already initialized");
                return;
            }

            isRunning = true;

            Console.WriteLine($"{Name}: Initializing camera index {cameraIndex}");

            captureThread = new Thread(() =>
            {
                try
                {
                    // Initialize OpenCV VideoCapture
                    camera = new VideoCapture(cameraIndex);
                    camera.Set(VideoCaptureProperties.FrameWidth, width);
                    camera.Set(VideoCaptureProperties.FrameHeight, height);

                    if (!camera.IsOpened())
                    {
                        Console.WriteLine($"{Name}: Failed to open camera {cameraIndex}!");
                        IsActive = false;
                        return;
                    }

                    Console.WriteLine($"{Name}: Camera opened successfully");

                    // Initialize PutRGBA streamer
                    streamer = Workspace.AddProp(new PutRGBA()
                    {
                        height = height,
                        width = width,
                        name = Name,
                    });
                    updater = streamer.StartStreaming();
                    Console.WriteLine($"{Name}: Streamer initiated");

                    // Capture loop
                    using (var frame = new Mat())
                    {
                        while (isRunning)
                        {
                            if (camera.Read(frame) && !frame.Empty())
                            {
                                ProcessFrame(frame);
                                IsActive = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{Name}: Error in capture thread - {ex.Message}: stack={ex.StackTrace}");
                    IsActive = false;
                }

            }) { Name = $"{Name}_Capture", IsBackground = true };
            captureThread.Start();
        }

        public byte[] preparedData = null;
        private void ProcessFrame(Mat frame)
        {
            try
            {
                // Ensure frame is in BGR format (OpenCV default)
                if (frame.Channels() != 3)
                {
                    return;
                }

                // Get frame data
                byte[] frameData = new byte[frame.Total() * frame.ElemSize()];
                Marshal.Copy(frame.Data, frameData, 0, frameData.Length);

                preparedData = (cached[cachedId] ??= new byte[height * width * 4]);

                for (int i = 0; i < height * width; ++i)
                {
                    int pi = i * 3;  // Source position in BGR
                    int ci = i * 4;  // Destination position in RGBA

                    cached[cachedId][ci] = frameData[pi+2];         // B
                    cached[cachedId][ci + 1] = frameData[pi + 1]; // G
                    cached[cachedId][ci + 2] = frameData[pi]; // R
                    cached[cachedId][ci + 3] = 255;               // A
                }

                if (updater != null)
                    updater(cached[cachedId]);

                cachedId = 1 - cachedId;

                // Update FPS counter
                frameCount++;
                if ((DateTime.Now - lastFpsTime).TotalSeconds >= 1.0)
                {
                    FPS = (int)(frameCount / (DateTime.Now - lastFpsTime).TotalSeconds);
                    frameCount = 0;
                    lastFpsTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Name}: Error processing frame - {ex.Message}");
            }
        }

        public void Stop()
        {
            isRunning = false;

            try
            {
                camera?.Release();
                camera?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Name}: Error stopping - {ex.Message}");
            }

            Console.WriteLine($"{Name}: Stopped");
        }
    }
}


