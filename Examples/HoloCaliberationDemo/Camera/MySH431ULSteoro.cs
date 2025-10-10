using System.Numerics;
using System.Diagnostics;
using GitHub.secile.Video2;

namespace HoloCaliberationDemo.Camera
{
    internal class MySH431ULSteoro
    {
        public Vector3 original_left, original_right;

        // Debug and monitoring fields
        private Thread pollingThread;
        private volatile bool isRunning = true;
        private volatile bool cameraActive = false;
        private DateTime lastDataReceived = DateTime.Now;
        private int reconnectAttempts = 0;
        private const int MAX_RECONNECT_ATTEMPTS = 5;
        private const int DATA_TIMEOUT_SECONDS = 5;
        private SH431ULCamera currentCamera;
        private readonly object cameraLock = new object();
        private long totalFramesReceived = 0;
        private DateTime lastLogTime = DateTime.Now;

        public unsafe void Polling()
        {
            while (isRunning)
            {
                try
                {
                    // LogDebug("Starting camera polling cycle");

                    var CameraList = SH431ULCamera.FindDevices().Select(str => str.Replace(" ", "_")).ToArray();
                    // LogDebug($"Found {CameraList.Length} cameras: {string.Join(", ", CameraList)}");

                    if (CameraList.Length == 0)
                    {
                        LogError("No cameras found");
                        Thread.Sleep(2000);
                        continue;
                    }

                    var tu = 0;
                    for (int i = 0; i < CameraList.Length; ++i)
                    {
                        if (CameraList[i].Contains("SH431UL"))
                        {
                            tu = i;
                            break;
                        }
                    }

                    // LogDebug($"Using camera index: {tu} ({CameraList[tu]})");

                    SH431ULCamera.VideoFormat[] formats = SH431ULCamera.GetVideoFormat(tu);
                    // LogDebug($"Found {formats.Length} video formats");

                    if (formats.Length <= 5)
                    {
                        LogError($"Not enough video formats found: {formats.Length}");
                        Thread.Sleep(2000);
                        continue;
                    }

                    var format = formats[5];

                    var n = format.Size.Width * format.Size.Height * 2;
                    var cached = new byte[n];

                    var frames = 0;
                    DateTime lasTime = DateTime.Now;
                    lastDataReceived = DateTime.Now;

                    lock (cameraLock)
                    {
                        // Dispose previous camera if exists
                        if (currentCamera != null)
                        {
                            try
                            {
                                currentCamera.Stop();
                                currentCamera.Release();
                            }
                            catch (Exception ex)
                            {
                                LogError($"Error disposing previous camera: {ex.Message}");
                            }
                            currentCamera = null;
                        }

                        currentCamera = new SH431ULCamera(tu, format, new SH431ULCamera.GrabberExchange()
                        {
                            action = (d, ptr, arg3) =>
                            {
                                try
                                {
                                    lastDataReceived = DateTime.Now;
                                    cameraActive = true;
                                    totalFramesReceived++;

                                    byte* pbr = (byte*)ptr;

                                    // Validate buffer size
                                    if (arg3 < 52)
                                    {
                                        LogError($"Buffer too small: {arg3} bytes, expected at least 52");
                                        return;
                                    }

                                    for (int i = 0; i < 52; ++i)
                                        cached[i] = pbr[i];

                                    var rx = -BitConverter.ToInt16(cached, 32);
                                    var ry = -BitConverter.ToInt16(cached, 28);
                                    var rz = BitConverter.ToInt16(cached, 36);

                                    var lx = -BitConverter.ToInt16(cached, 44);
                                    var ly = -BitConverter.ToInt16(cached, 40);
                                    var lz = BitConverter.ToInt16(cached, 48);

                                    original_left = new Vector3(lx, ly , lz);
                                    original_right = new Vector3(rx , ry, rz);

                                    // if (issue)
                                    //     new SetHoloViewEyePosition
                                    //     {
                                    //         leftEyePos = left,
                                    //         rightEyePos = right
                                    //     }.IssueToDefault();


                                    frames += 1;
                                    if (lasTime.AddSeconds(1) < DateTime.Now)
                                    {
                                        var fps = frames / (DateTime.Now - lasTime).TotalSeconds;
                                        lasTime = DateTime.Now;
                                        frames = 0;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogError($"Error in camera callback: {ex.Message}\n{ex.StackTrace}");
                                }
                            }
                        });

                        currentCamera.Start();
                        cameraActive = true;
                        reconnectAttempts = 0;
                    }

                    // Monitor camera health
                    while (isRunning && cameraActive)
                    {
                        Thread.Sleep(1000);

                        // Check if data flow has stopped
                        if (DateTime.Now - lastDataReceived > TimeSpan.FromSeconds(DATA_TIMEOUT_SECONDS))
                        {
                            LogError($"Camera data timeout after {DATA_TIMEOUT_SECONDS} seconds");
                            cameraActive = false;
                            break;
                        }

                        // Log periodic status
                        if (DateTime.Now - lastLogTime > TimeSpan.FromSeconds(10))
                        {
                            lastLogTime = DateTime.Now;
                        }
                    }

                    LogWarning("Camera monitoring loop exited");

                }
                catch (Exception ex)
                {
                    LogError($"Error in polling thread: {ex.Message}\n{ex.StackTrace}");
                    cameraActive = false;
                }
                finally
                {
                    // Cleanup
                    lock (cameraLock)
                    {
                        if (currentCamera != null)
                        {
                            try
                            {
                                currentCamera.Stop();
                                currentCamera.Release();
                            }
                            catch (Exception ex)
                            {
                                LogError($"Error stopping camera: {ex.Message}");
                            }
                            currentCamera = null;
                        }
                    }
                }

                if (!isRunning) break;

                reconnectAttempts++;
                if (reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
                {
                    LogError($"Max reconnection attempts ({MAX_RECONNECT_ATTEMPTS}) reached");
                    break;
                }

                LogWarning($"Attempting to reconnect camera (attempt {reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS})");
                Thread.Sleep(2000); // Wait before retry
            }
        }

        private void LogWarning(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId = Thread.CurrentThread.ManagedThreadId;
            Console.WriteLine($"[{timestamp}] [WARN] [T{threadId}] {message}");
            Debug.WriteLine($"[{timestamp}] [WARN] [T{threadId}] {message}");
        }

        private void LogError(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId = Thread.CurrentThread.ManagedThreadId;
            Console.WriteLine($"[{timestamp}] [ERROR] [T{threadId}] {message}");
            Debug.WriteLine($"[{timestamp}] [ERROR] [T{threadId}] {message}");
        }

        public MySH431ULSteoro()
        {
            pollingThread = new Thread(Polling) { Name = "SH431UL_Polling", IsBackground = false };
            pollingThread.Start();
        }
    }
}
