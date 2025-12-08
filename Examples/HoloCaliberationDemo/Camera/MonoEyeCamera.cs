using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CycleGUI;
using CycleGUI.API;

namespace HoloCaliberationDemo.Camera
{
    internal class MonoEyeCamera
    {
        internal static bool IsWindowsOS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private UsbCamera camera;
        private PutRGBA streamer;
        private Action<byte[]> updater;

        private byte[][] cached = new byte[2][];
        private int cachedId = 0;

        public int width;
        public int height;

        private volatile bool isRunning = false;

        // Frame statistics
        private int frameCount = 0;
        private DateTime lastFpsTime = DateTime.Now;

        public int FPS = 0;
        public bool IsActive { get; private set; } = false;
        public string Name { get; private set; }

        // Helper thread for async image processing
        private Thread helperThread;
        private Action pendingAct = null;
        private readonly object actLock = new object();

        public DateTime framedt = DateTime.Now;

        public MonoEyeCamera(string name)
        {
            Name = name;
        }

        public unsafe void Initialize(int cameraIndex, UsbCamera.VideoFormat format = null)
        {
            if (isRunning)
            {
                Console.WriteLine($"{Name}: Already initialized");
                return;
            }

            isRunning = true;

            // Start helper thread for async processing
            helperThread = new Thread(() =>
            {
                while (isRunning)
                {
                    Action currentAct = null;
                    lock (actLock)
                    {
                        currentAct = pendingAct;
                        if (currentAct != null)
                            pendingAct = null;
                    }

                    if (currentAct != null)
                    {
                        try
                        {
                            currentAct();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"{Name} helper error: {ex.Message}");
                        }
                    }
                    else
                    {
                        Thread.Sleep(30);
                    }
                }
            })
            { Name = $"{Name}_Helper", IsBackground = true };
            helperThread.Start();

            Console.WriteLine($"{Name}: Initializing camera index {cameraIndex}");

            new Thread(() =>
            {
                try
                {
                    Thread.Sleep(100);

                    var formats = UsbCamera.GetVideoFormat(cameraIndex);
                    var selectedFormat = format ?? formats[0]; // Use first format if not specified

                    width = selectedFormat.Size.Width;
                    height = selectedFormat.Size.Height;

                    var sw = new Stopwatch();
                    sw.Start();

                    camera = new UsbCamera(cameraIndex, selectedFormat, new UsbCamera.GrabberExchange()
                    {
                        action = (d, ptr, flen) =>
                        {
                            ProcessFrame(ptr, flen);
                            IsActive = true;
                        }
                    });
                    Console.WriteLine($"{Name}: starting...");
                    camera.Start();
                    Console.WriteLine($"{Name}: Started");

                    // Initialize PutRGBA streamer
                    streamer = Workspace.AddProp(new PutRGBA()
                    {
                        height = height,
                        width = width,
                        name = Name,
                    });
                    updater = streamer.StartStreaming();
                    Console.WriteLine($"{Name}: initiated");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{Name}: Error initializing - {ex.Message}: stack={ex.StackTrace}");
                    IsActive = false;
                }

            }).Start();
        }

        private void EnqueueAct(Action action)
        {
            if (!isRunning)
                return;

            lock (actLock)
            {
                pendingAct = action;
            }
        }

        byte[] frameData = null;
        public byte[] preparedData = null;
        private unsafe void ProcessFrame(IntPtr ptr, int flen)
        {
            try
            {
                // Detect pixel format by buffer size
                int pxCount = width * height;
                int channels = 0;

                if (flen == pxCount * 3)
                {
                    channels = 3; // RGB24
                }
                else if (flen == pxCount)
                {
                    channels = 1; // GRAY8
                }
                else if (flen == pxCount * 2)
                {
                    channels = 1; // RAW16
                }
                else
                {
                    // Unknown format
                    return;
                }

                // Only process RGB24 frames for visualization
                if (channels == 3)
                {
                    frameData ??= new byte[flen];
                    Marshal.Copy(ptr, frameData, 0, flen);

                    // Convert to ARGB and send to streamer
                    EnqueueAct(() =>
                    {
                        preparedData = (cached[cachedId] ??= new byte[height * width * 4]);

                        for (int i = 0; i < height; ++i)
                        {
                            var pi = (i * width) * 3;
                            var ci = IsWindowsOS
                                ? ((height - 1 - i) * width) * 4
                                : i * width * 4; // Windows upside down, Linux normal

                            if (IsWindowsOS)
                            {
                                for (int j = 0; j < width; ++j)
                                {
                                    cached[cachedId][ci] = frameData[pi + 2];     // B
                                    cached[cachedId][ci + 1] = frameData[pi + 1]; // G
                                    cached[cachedId][ci + 2] = frameData[pi];     // R
                                    cached[cachedId][ci + 3] = 255;               // A
                                    pi += 3;
                                    ci += 4;
                                }
                            }
                            else
                            {
                                for (int j = 0; j < width; ++j)
                                {
                                    cached[cachedId][ci] = frameData[pi];         // R
                                    cached[cachedId][ci + 1] = frameData[pi + 1]; // G
                                    cached[cachedId][ci + 2] = frameData[pi + 2]; // B
                                    cached[cachedId][ci + 3] = 255;               // A
                                    pi += 3;
                                    ci += 4;
                                }
                            }
                        }

                        if (updater != null)
                            updater(cached[cachedId]);

                        cachedId = 1 - cachedId;
                    });
                }

                // Update FPS counter
                frameCount++;
                if ((DateTime.Now - lastFpsTime).TotalSeconds >= 1.0)
                {
                    FPS = (int)(frameCount / (DateTime.Now - lastFpsTime).TotalSeconds);
                    frameCount = 0;
                    lastFpsTime = DateTime.Now;
                }

                framedt = DateTime.Now;
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
                camera?.Stop();
                camera?.Release();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Name}: Error stopping - {ex.Message}");
            }

            Console.WriteLine($"{Name}: Stopped");
        }
    }
}



