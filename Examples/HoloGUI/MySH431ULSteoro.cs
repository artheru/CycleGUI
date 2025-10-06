using CycleGUI;
using CycleGUI.API;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using static HoloExample.AngstrongHp60c;
using static System.Formats.Asn1.AsnWriter;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using GitHub.secile.Video;
using System.Diagnostics;

namespace HoloExample
{
    internal class MySH431ULSteoro
    {
        private Panel wp;
        private Vector3 left, right;
        private ret cur;
        private float rotateX, rotateY, rotateZ, translateX, translateY, translateZ, scaleZ=1, scaleXY = 1;

        bool debug_plc = false;
        bool issue = true;

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
        // private CameraDebugHelper debugHelper;

        public unsafe void Polling()
        {
            while (isRunning)
            {
                try
                {
                    LogDebug("Starting camera polling cycle");
                    
                    var CameraList = SH431ULCamera.FindDevices().Select(str => str.Replace(" ", "_")).ToArray();
                    LogDebug($"Found {CameraList.Length} cameras: {string.Join(", ", CameraList)}");

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

                    LogDebug($"Using camera index: {tu} ({CameraList[tu]})");

                    SH431ULCamera.VideoFormat[] formats = SH431ULCamera.GetVideoFormat(tu);
                    LogDebug($"Found {formats.Length} video formats");
                    
                    if (formats.Length <= 5)
                    {
                        LogError($"Not enough video formats found: {formats.Length}");
                        Thread.Sleep(2000);
                        continue;
                    }

                    var format = formats[5];
                    LogDebug($"Using format: {format}");
                    
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
                                LogDebug("Disposing previous camera");
                                currentCamera.Stop();
                                currentCamera.Release();
                            }
                            catch (Exception ex)
                            {
                                LogError($"Error disposing previous camera: {ex.Message}");
                            }
                            currentCamera = null;
                        }

                        LogDebug("Creating new camera instance");
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

                                    var rleft = new Vector3(lx * scaleXY, ly * scaleXY, lz * scaleXY) / 10.0f;
                                    var rright = new Vector3(rx * scaleXY, ry * scaleXY, rz * scaleXY) /10.0f;

                                    // for each eye, rotate and translate and scale.
                                    // Create rotation matrix from Euler angles
                                    var rotMatrix = Matrix4x4.CreateRotationX(rotateX) *
                                                    Matrix4x4.CreateRotationY(rotateY) *
                                                    Matrix4x4.CreateRotationZ(rotateZ);

                                    // Transform left eye position
                                    var transformedLeft = Vector3.Transform(rleft, rotMatrix);
                                    transformedLeft += new Vector3(translateX, translateY, translateZ);

                                    // Transform right eye position  
                                    var transformedRight = Vector3.Transform(rright, rotMatrix);
                                    transformedRight += new Vector3(translateX, translateY, translateZ);

                                    transformedRight.Z *= scaleZ;
                                    transformedLeft.Z *= scaleZ;

                                    left = transformedLeft;
                                    right = transformedRight;

                                    if (issue)
                                        new SetHoloViewEyePosition
                                        {
                                            leftEyePos = left,
                                            rightEyePos = right
                                        }.IssueToDefault();

                                    var pp = Painter.GetPainter("Eye");
                                    pp.Clear();
                                    if (debug_plc)
                                    {
                                        pp.DrawDotMM(Color.Red, new Vector3(left.X, -left.Y, -left.Z), 2);
                                        pp.DrawDotMM(Color.Green, new Vector3(right.X, -right.Y, -right.Z), 2);
                                        pp.DrawDotMM(Color.Orange, new Vector3(left.X, -left.Y, 0), 1);
                                        pp.DrawDotMM(Color.Cyan, new Vector3(right.X, -right.Y, 0), 1);
                                    }

                                    wp.Repaint();

                                    frames += 1;
                                    if (lasTime.AddSeconds(1) < DateTime.Now)
                                    {
                                        var fps = frames / ((DateTime.Now - lasTime).TotalSeconds);
                                        LogDebug($"Eye FPS={fps:0.00}, Total frames: {totalFramesReceived}");
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

                        LogDebug("Starting camera");
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
                            LogDebug($"Camera active, total frames: {totalFramesReceived}, last data: {(DateTime.Now - lastDataReceived).TotalSeconds:0.1}s ago");
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
                                LogDebug("Stopping camera");
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

            LogDebug("Polling thread ended");
        }

        private void LogDebug(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId = Thread.CurrentThread.ManagedThreadId;
            //Console.WriteLine($"[{timestamp}] [DEBUG] [T{threadId}] {message}");
            Debug.WriteLine($"[{timestamp}] [DEBUG] [T{threadId}] {message}");
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
            LogDebug("Initializing MySH431ULSteoro");

            // Load calibration if exists
            if (File.Exists("sh431_eyetracker_calibration.json"))
            {
                try
                {
                    string json = File.ReadAllText("sh431_eyetracker_calibration.json");
                    var calibration = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
                    rotateX = calibration.GetProperty("rotateX").GetSingle();
                    rotateY = calibration.GetProperty("rotateY").GetSingle();
                    rotateZ = calibration.GetProperty("rotateZ").GetSingle();
                    translateX = calibration.GetProperty("translateX").GetSingle();
                    translateY = calibration.GetProperty("translateY").GetSingle();
                    translateZ = calibration.GetProperty("translateZ").GetSingle();
                    scaleXY = calibration.GetProperty("scaleXY").GetSingle();
                    scaleZ = calibration.GetProperty("scaleZ").GetSingle();
                    LogDebug("Calibration loaded successfully");
                }
                catch (Exception ex)
                {
                    LogError($"Error loading calibration: {ex.Message}");
                }
            }

            wp = GUI.PromptPanel(pb =>
            {
                pb.Panel.ShowTitle("SH431");
                pb.CheckBox("issue", ref issue);
                pb.Separator();
                
                // rotate and translate and scale params:
                pb.DragFloat("rotate X", ref rotateX, 0.001f);
                pb.DragFloat("rotate Y", ref rotateY, 0.001f);
                pb.DragFloat("rotate Z", ref rotateZ, 0.001f);
                pb.DragFloat("translate X", ref translateX, 0.03f);
                pb.DragFloat("translate Y", ref translateY, 0.03f);
                pb.DragFloat("translate Z", ref translateZ, 0.03f);
                pb.DragFloat("scale Z", ref scaleZ, 0.0001f);
                pb.DragFloat("scale XY", ref scaleXY, 0.0001f);

                // save to file.
                if (pb.Button("Save Calibration"))
                {
                    var calibration = new
                    {
                        rotateX,
                        rotateY,
                        rotateZ,
                        translateX,
                        translateY,
                        translateZ,
                        scaleXY,
                        scaleZ
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(calibration, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText("sh431_eyetracker_calibration.json", json);
                    LogDebug("Calibration saved");
                }

                pb.Separator();
                pb.Label($"left v3:{left.X:0.0}, {left.Y:0.0}, {left.Z:0.0}");
                pb.Label($"right v3:{right.X:0.0}, {right.Y:0.0}, {right.Z:0.0}");
                pb.Label($"left delta:{left.X-MyHikSteoro.left.X:0.0}, {left.Y - MyHikSteoro.left.Y:0.0}, {left.Z - MyHikSteoro.left.Z:0.0}");
                pb.Label($"right delta:{right.X-MyHikSteoro.right.X:0.0}, {right.Y - MyHikSteoro.right.Y:0.0}, {right.Z - MyHikSteoro.right.Z:0.0}");
                if (pb.CheckBox("show place", ref debug_plc) && debug_plc)
                {
                    new SetCamera(){lookAt = new Vector3(0,0,-1), 
                        world2phy = 1000,
                        distance = 1, azimuth = (float)(-Math.PI/2), altitude = (float)(Math.PI/2)}.Issue();
                }
            });

            pollingThread = new Thread(Polling) { Name = "SH431UL_Polling", IsBackground = false };
            pollingThread.Start();
            LogDebug("Polling thread started");
        }
    }
}
