using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.PlatformSpecific.Windows;
using CycleGUI.Terminals;
using HoloCaliberationDemo.Camera;
using OpenCvSharp;
using JsonCommentHandling = System.Text.Json.JsonCommentHandling;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace HoloCaliberationDemo
{
    internal partial class Program
    {
        // ========= CALIBERATION VALUES =========
        private static Matrix4x4 cameraToActualMatrix = Matrix4x4.Identity;


        static MySH431ULSteoro sh431;
        static MyArmControl arm;
        static MonoEyeCamera leftCamera = new("left_camera");
        static MonoEyeCamera rightCamera = new("right_camera");

        private static WindowsTray wtray;
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;


        public class CaliberationRobotConfig
        {
            public float[] Bias { get; set; } = [0, 0, 0];
            public int LeftCameraIndex { get; set; } = 0;
            public int RightCameraIndex { get; set; } = 2;
        }

        public class CalibrationData
        {
            public float[] cam_mat { get; set; } = new float[16];
        }

        public static CaliberationRobotConfig config;

        private static string running = null;
        

        static bool LoadRobotConfiguration()
        {
            try
            {
                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "params.json");

                if (!File.Exists(configPath))
                    return false;

                string jsonContent = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                config = JsonSerializer.Deserialize<CaliberationRobotConfig>(jsonContent, options);

                if (config != null)
                {
                    // Load camera configuration
                    Console.WriteLine($"Camera config - Left Index: {config.LeftCameraIndex}, Right Index: {config.RightCameraIndex}");
                    if (config.Bias != null && config.Bias.Length >= 3)
                    {
                        Console.WriteLine($"Loaded Bias: [{config.Bias[0]}, {config.Bias[1]}, {config.Bias[2]}]");
                    }
                    
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

        static void SaveRobotConfigurations()
        {
            try
            {
                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "params.json");
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string jsonContent = JsonSerializer.Serialize(config, options);
                File.WriteAllText(configPath, jsonContent);
                
                Console.WriteLine($"Robot configuration saved to {configPath}");
                Console.WriteLine($"Bias: [{config.Bias[0]}, {config.Bias[1]}, {config.Bias[2]}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving robot configuration: {ex.Message}");
            }
        }

        static void SaveCalibrationMatrix()
        {
            try
            {
                string calibPath = Path.Combine(Directory.GetCurrentDirectory(), "caliberation_values.json");
                
                var calibData = new CalibrationData
                {
                    cam_mat = new float[16]
                    {
                        cameraToActualMatrix.M11, cameraToActualMatrix.M12, cameraToActualMatrix.M13, cameraToActualMatrix.M14,
                        cameraToActualMatrix.M21, cameraToActualMatrix.M22, cameraToActualMatrix.M23, cameraToActualMatrix.M24,
                        cameraToActualMatrix.M31, cameraToActualMatrix.M32, cameraToActualMatrix.M33, cameraToActualMatrix.M34,
                        cameraToActualMatrix.M41, cameraToActualMatrix.M42, cameraToActualMatrix.M43, cameraToActualMatrix.M44
                    },
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string jsonContent = JsonSerializer.Serialize(calibData, options);
                File.WriteAllText(calibPath, jsonContent);
                
                Console.WriteLine($"Calibration matrix saved to {calibPath}");
                Console.WriteLine($"Matrix:\n{cameraToActualMatrix}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving calibration matrix: {ex.Message}");
            }
        }

        static bool LoadCalibrationMatrix()
        {
            try
            {
                string calibPath = Path.Combine(Directory.GetCurrentDirectory(), "caliberation_values.json");

                if (!File.Exists(calibPath))
                {
                    Console.WriteLine($"Calibration file not found: {calibPath}");
                    return false;
                }

                string jsonContent = File.ReadAllText(calibPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var calibData = JsonSerializer.Deserialize<CalibrationData>(jsonContent, options);

                if (calibData != null && calibData.cam_mat != null && calibData.cam_mat.Length == 16)
                {
                    cameraToActualMatrix = new Matrix4x4(
                        calibData.cam_mat[0], calibData.cam_mat[1], calibData.cam_mat[2], calibData.cam_mat[3],
                        calibData.cam_mat[4], calibData.cam_mat[5], calibData.cam_mat[6], calibData.cam_mat[7],
                        calibData.cam_mat[8], calibData.cam_mat[9], calibData.cam_mat[10], calibData.cam_mat[11],
                        calibData.cam_mat[12], calibData.cam_mat[13], calibData.cam_mat[14], calibData.cam_mat[15]
                    );
                    
                    Console.WriteLine($"Calibration matrix loaded from {calibPath}");
                    Console.WriteLine($"Matrix:\n{cameraToActualMatrix}");
                    return true;
                }

                Console.WriteLine("Invalid calibration data format");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading calibration matrix: {ex.Message}");
                return false;
            }
        }

        private static Terminal remote;

        static void Main(string[] args)
        {
            LoadRobotConfiguration();

            arm = new MyArmControl();
            arm.Initialize();
            var dv = arm.GetDefaultPosition();
            arm.Goto(new Vector3(config.Bias[0] - 400, 0, config.Bias[2])); // standard caliberation place.

            // Initialize left and right cameras using configured indices
            int leftIdx = config?.LeftCameraIndex ?? 0;
            int rightIdx = config?.RightCameraIndex ?? 2;

            Console.WriteLine($"Initializing cameras - Left: index {leftIdx}, Right: index {rightIdx}");
            leftCamera.Initialize(leftIdx);
            rightCamera.Initialize(rightIdx);

            sh431 = new MySH431ULSteoro();


            // First read the .ico file from assembly, and then extract it as byte array.
            var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(Assembly.GetExecutingAssembly()
                    .GetManifestResourceNames()
                    .First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);


            LocalTerminal.Start();
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Holo Caliberation DEMO");

            new SetCamera() { displayMode = SetCamera.DisplayMode.EyeTrackedLenticular }.IssueToDefault();
            new SetAppearance(){useGround = false, drawGuizmo = false}.IssueToDefault();
            new SetFullScreen().IssueToDefault();
            new SetLenticularParams()
            {
                left_fill = new Vector4(1,0,0,1), right_fill = new Vector4(0,0,1,1), 
                period_fill = 5,period_total = 9,phase_init_left = 5,phase_init_right = 5,phase_init_row_increment = 1
            }.IssueToDefault();

            Terminal.RegisterRemotePanel(t =>
            {
                remote = t;

                var v3i = arm.GetPos();
                float sx = v3i.X, sy = v3i.Y, sz = v3i.Z;

                // Lenticular parameters
                float fill = 1;
                float period_total = 5.33f;
                float phase_init_left = 0;
                float phase_init_right = 0;
                float phase_init_row_increment = 0.183528f;
                float dragSpeed = -6.0f; // e^-6 ≈ 0.0025
                int fillColorMode = 0; // 0: LRed/RBlue, 1: none, 2: AllRed, 3: LRed only, 4: RBlue only

                return pb =>
                {
                    pb.Panel.ShowTitle("Caliberator");

                    pb.Panel.Repaint();

                    // Camera status
                    pb.SeparatorText("Camera status");
                    pb.Label($"Left Camera: {(leftCamera.IsActive ? "Active" : "Inactive")} ({leftCamera.FPS} FPS)");
                    pb.Label($"Right Camera: {(rightCamera.IsActive ? "Active" : "Inactive")} ({rightCamera.FPS} FPS)");

                    pb.SeparatorText("Eye tracker status");
                    pb.Label($"sh431::left={sh431.original_left}");
                    pb.Label($"sh431::right={sh431.original_right}");
                    var sv3c = 0.5f * (sh431.original_right + sh431.original_left);
                    pb.Label($"sh431={sv3c}");
                    var transformed = TransformPoint(cameraToActualMatrix, sv3c);
                    pb.Label($"sh431.screen={transformed.X:0.0}, {transformed.Y:0.0}, {transformed.Z:0.0}");

                    //
                    pb.SeparatorText("Arm status");
                    var v3 = arm.GetPos();
                    pb.Label($"robot={v3}");
                    pb.DragFloat("bias2screen.X", ref config.Bias[0], 0.1f, -500, 500);
                    pb.DragFloat("bias2screen.Y", ref config.Bias[1], 0.1f, -500, 500);
                    pb.DragFloat("bias2screen.Z", ref config.Bias[2], 0.1f, -500, 500);
                    pb.Label($"robot2screen={-config.Bias[1]-v3.Y:0.0},{v3.Z-config.Bias[2]},{config.Bias[0]-v3.X}");

                    if (pb.Button("Save Robot Configurations"))
                        SaveRobotConfigurations();

                    pb.DragFloat("X", ref sx, 0.1f, -500, 500);
                    pb.DragFloat("Y", ref sy, 0.1f, -500, 500);
                    pb.DragFloat("Z", ref sz, 0.1f, -500, 500);
                    if (pb.Button("Send"))
                    {
                        Console.WriteLine($"Goto {v3}...");
                        arm.Goto(new Vector3(sx, sy, sz));
                    }

                    pb.SeparatorText("Caliberation");
                    if (running != null)
                    {
                        pb.Label(running);
                        pb.DelegateUI();
                    }

                    if (running==null && pb.Button("Caliberate EyeTracker Camera"))
                    {
                        new Thread(EyeTrackCaliberationProcedure).Start();
                    }
                    if (running == null && pb.Button("Tune Lenticular Parameters"))
                    {
                        new Thread(LenticularTuner).Start();
                    }


                    if (pb.Button("Save Calibration Matrix"))
                    {
                        SaveCalibrationMatrix();
                    }
                    
                    if (pb.Button("Load Calibration Matrix"))
                    {
                        LoadCalibrationMatrix();
                    }
                    
                    if (pb.Button("Display Left Camera"))
                    {
                        GUI.PromptOrBringToFront(pb2 =>
                        {
                            pb2.Panel.ShowTitle("Left Camera");
                            pb2.Image("Left Camera", "left_camera");
                            if (pb2.Closing()) pb2.Panel.Exit();
                        }, t);
                    }
                    
                    if (pb.Button("Display Right Camera"))
                    {
                        GUI.PromptOrBringToFront(pb2 =>
                        {
                            pb2.Panel.ShowTitle("Right Camera");
                            pb2.Image("Right Camera", "right_camera");
                            if (pb2.Closing()) pb2.Panel.Exit();
                        }, t);
                    }

                    pb.Separator();
                    pb.SeparatorText("Lenticular Parameters");
                    
                    // Adjust speed control
                    if (pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -15.0f, 0.0f))
                    {
                        // Speed is e^dragSpeed, range from e^-9 ≈ 0.0001 to e^0 = 1
                    }
                    pb.Label($"Current Speed: {Math.Exp(dragSpeed):F6}");
                    
                    float speed = (float)Math.Exp(dragSpeed);
                    
                    // Fill color mode selection
                    if (pb.RadioButtons("Fill Color Mode", new[] { "LRed/RBlue", "None", "AllRed", "LRed only", "RBlue only" }, ref fillColorMode))
                    {
                        Vector4 leftFill, rightFill;
                        switch (fillColorMode)
                        {
                            case 0: // LRed/RBlue
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(0, 0, 1, 1);
                                break;
                            case 1: // None
                                leftFill = new Vector4(0, 0, 0, 0);
                                rightFill = new Vector4(0, 0, 0, 0);
                                break;
                            case 2: // AllRed
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(1, 0, 0, 1);
                                break;
                            case 3: // LRed only
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(0, 0, 0, 0);
                                break;
                            case 4: // RBlue only
                                leftFill = new Vector4(0, 0, 0, 0);
                                rightFill = new Vector4(0, 0, 1, 1);
                                break;
                            default:
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(0, 0, 1, 1);
                                break;
                        }
                        
                        new SetLenticularParams()
                        {
                            left_fill = leftFill,
                            right_fill = rightFill,
                            period_fill = fill,
                            period_total = period_total,
                            phase_init_left = phase_init_left,
                            phase_init_right = phase_init_right,
                            phase_init_row_increment = phase_init_row_increment
                        }.IssueToTerminal(GUI.localTerminal);
                    }
                    
                    // Lenticular parameter controls
                    bool paramsChanged = false;
                    paramsChanged |= pb.DragFloat("Period Empty", ref fill, speed, 0, 100);
                    paramsChanged |= pb.DragFloat("Period Total", ref period_total, speed, 0, 100);
                    paramsChanged |= pb.DragFloat("Phase Init Left", ref phase_init_left, speed, -100, 100);
                    paramsChanged |= pb.DragFloat("Phase Init Right", ref phase_init_right, speed, -100, 100);
                    paramsChanged |= pb.DragFloat("Phase Init Row Increment", ref phase_init_row_increment, speed, -100, 100);
                    
                    if (paramsChanged)
                    {
                        Vector4 leftFill, rightFill;
                        switch (fillColorMode)
                        {
                            case 0: // LRed/RBlue
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(0, 0, 1, 1);
                                break;
                            case 1: // None
                                leftFill = new Vector4(0, 0, 0, 0);
                                rightFill = new Vector4(0, 0, 0, 0);
                                break;
                            case 2: // AllRed
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(1, 0, 0, 1);
                                break;
                            case 3: // LRed only
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(0, 0, 0, 0);
                                break;
                            case 4: // RBlue only
                                leftFill = new Vector4(0, 0, 0, 0);
                                rightFill = new Vector4(0, 0, 1, 1);
                                break;
                            default:
                                leftFill = new Vector4(1, 0, 0, 1);
                                rightFill = new Vector4(0, 0, 1, 1);
                                break;
                        }
                        
                        new SetLenticularParams()
                        {
                            left_fill = leftFill,
                            right_fill = rightFill,
                            period_fill = fill,
                            period_total = period_total,
                            phase_init_left = phase_init_left,
                            phase_init_right = phase_init_right,
                            phase_init_row_increment = phase_init_row_increment
                        }.IssueToTerminal(GUI.localTerminal);
                    }
                    
                    pb.Separator();
                    if (pb.Button("Exit Program"))
                    {
                        Environment.Exit(0);
                    }
                };
            });

            Task.Run(() => {
                WebTerminal.Use(ico: icoBytes);
            });
        }
    }
}
