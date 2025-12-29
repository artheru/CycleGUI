using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.PlatformSpecific.Windows;
using CycleGUI.Terminals;
using HoloCaliberationDemo.Camera;
using Newtonsoft.Json;
using OpenCvSharp;

namespace HoloCaliberationDemo
{
    internal partial class Program
    {
        // ========= CALIBERATION VALUES =========
        private static Matrix4x4 cameraToActualMatrix = Matrix4x4.Identity;


        private static float prior_row_increment = 0.183528f, base_row_increment_search = 0.002f;

        private static float _priorBiasLeft = 0;
        private static float _priorBiasRight = 0;
        private static float _priorPeriod = 5.32f;

        private static float prior_bias_left
        {
            get => _priorBiasLeft;
            set { _priorBiasLeft = value;
                edited_bl = true;
            }
        }

        private static float prior_bias_right
        {
            get => _priorBiasRight;
            set { _priorBiasRight = value;
                edited_br = true;
            }
        }

        private static float prior_period
        {
            get => _priorPeriod;
            set { _priorPeriod = value;
                edited_p = true;
            }
        }

        private static bool edited_bl, edited_br, edited_p;
        private static float period_fill = 1;
        private static bool curved_screen = false;
        private static Vector4 curved_screen_curve = new Vector4(0.35f, 0.0f, 0.65f, 0.0f);
        private static float curved_start_y, curved_end_y, curve_width = 1000;
        // (legacy) removed: old 8-value fine bias tuning UI

        // Fine-bias tuning (per-block)
        private static bool tune_fine_bias = false;
        private static float fine_bias_cols_f = 5, fine_bias_rows_f = 3; // UI uses DragFloat, cast to int
        private static int fine_bias_cols => Math.Max(1, (int)fine_bias_cols_f);
        private static int fine_bias_rows => Math.Max(1, (int)fine_bias_rows_f);
        private static float main_rect_x0_f = 1, main_rect_y0_f = 0, main_rect_x1_f = 3, main_rect_y1_f = 2;
        private static int main_rect_x0 => (int)main_rect_x0_f;
        private static int main_rect_y0 => (int)main_rect_y0_f;
        private static int main_rect_x1 => (int)main_rect_x1_f;
        private static int main_rect_y1 => (int)main_rect_y1_f;
        private static float[] fine_bias_coarse_vals = new float[5 * 3];
        
        // RGB subpixel offsets
        private static Vector2 subpx_R = new Vector2(0.0f, 0.0f);
        private static Vector2 subpx_G = new Vector2(1.0f / 3.0f, 0.0f);
        private static Vector2 subpx_B = new Vector2(2.0f / 3.0f, 0.0f);
        
        // Stripe parameter: 0 = no stripe, 1 = show diagonal stripe
        private static bool stripe = false;
        private static int disp_type;

        static MySH431ULSteoro sh431;
        static MyArmControl arm;
        static MonoEyeCamera leftCamera = new("left_camera");
        static MonoEyeCamera rightCamera = new("right_camera");


        public class CaliberationRobotConfig
        {
            public float[] Bias { get; set; } = [0, 0, 0];
            public int LeftCameraIndex { get; set; } = 0;
            public int RightCameraIndex { get; set; } = 1;
            public string LeftCameraName { get; set; } = "";
            public string RightCameraName { get; set; } = "";

            public float[] InitialPosition { get; set; } = [0, 0, 0];
            public float[] InitialRotation { get; set; } = [0, 0, 0];

            public float PriorPeriod { get; set; } = 5.32f;
            public float PriorFill { get; set; } = 1f;
            public float PriorBiasLeft { get; set; } = 0f;
            public float PriorBiasRight { get; set; } = 0f;
            public float PriorRowIncrement { get; set; } = 0.183528f;
            
            // RGB subpixel offsets
            public float[] SubpxR { get; set; } = [0.0f, 0.0f];
            public float[] SubpxG { get; set; } = [1.0f / 3.0f, 0.0f];
            public float[] SubpxB { get; set; } = [2.0f / 3.0f, 0.0f];

            public bool IsCurvedScreen { get; set; } = false;
            public float[] CurvedControlPoints { get; set; } = [0.35f, 0.0f, 0.65f, 0.0f];
            public float CurvedStartY { get; set; } = 0.0f;
            public float CurvedEndY { get; set; } = 0.0f;
            public float CurvedScreenWidth { get; set; } = 1000.0f;

            // Fine-bias block tuning config
            public bool TuneFineBias { get; set; } = false;
            public int FineBiasCols { get; set; } = 5;
            public int FineBiasRows { get; set; } = 3;
            public int MainRectX0 { get; set; } = 1;
            public int MainRectY0 { get; set; } = 0;
            public int MainRectX1 { get; set; } = 3;
            public int MainRectY1 { get; set; } = 2;
            public float[] FineBiasCoarseVals { get; set; } = new float[5 * 3];
        }

        public class CalibrationData
        {
            public float[] cam_mat { get; set; } = new float[16];
        }

        private static readonly JsonSerializerSettings ConfigSerializerSettings = new()
        {
            Formatting = Formatting.Indented,
            Culture = CultureInfo.InvariantCulture
        };

        public static CaliberationRobotConfig config = new();

        private static string running = null;

        private static void LoadConfigurations()
        {
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "params.json");

            if (File.Exists(configPath))
            {
                try
                {
                    var jsonContent = File.ReadAllText(configPath);
                    config = JsonConvert.DeserializeObject<CaliberationRobotConfig>(jsonContent);
                    prior_period = config.PriorPeriod;
                    period_fill = config.PriorFill;
                    prior_row_increment = config.PriorRowIncrement;
                    
                    // Load subpixel offsets
                    if (config.SubpxR != null && config.SubpxR.Length == 2)
                        subpx_R = new Vector2(config.SubpxR[0], config.SubpxR[1]);
                    if (config.SubpxG != null && config.SubpxG.Length == 2)
                        subpx_G = new Vector2(config.SubpxG[0], config.SubpxG[1]);
                    if (config.SubpxB != null && config.SubpxB.Length == 2)
                        subpx_B = new Vector2(config.SubpxB[0], config.SubpxB[1]);

                    curved_screen = config.IsCurvedScreen;
                    if (config.CurvedControlPoints != null && config.CurvedControlPoints.Length == 4)
                        curved_screen_curve = new Vector4(config.CurvedControlPoints[0], config.CurvedControlPoints[1],
                            config.CurvedControlPoints[2], config.CurvedControlPoints[3]);
                    curved_start_y = config.CurvedStartY;
                    curved_end_y = config.CurvedEndY;
                    curve_width = config.CurvedScreenWidth;

                    tune_fine_bias = config.TuneFineBias;
                    fine_bias_cols_f = config.FineBiasCols;
                    fine_bias_rows_f = config.FineBiasRows;
                    main_rect_x0_f = config.MainRectX0;
                    main_rect_y0_f = config.MainRectY0;
                    main_rect_x1_f = config.MainRectX1;
                    main_rect_y1_f = config.MainRectY1;
                    var expected = Math.Max(1, config.FineBiasCols) * Math.Max(1, config.FineBiasRows);
                    if (config.FineBiasCoarseVals != null && config.FineBiasCoarseVals.Length == expected)
                        fine_bias_coarse_vals = config.FineBiasCoarseVals.ToArray();
                    else
                        fine_bias_coarse_vals = new float[expected];
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading configuration: {ex.Message}");
                    Console.WriteLine("Using default values");
                }
            }
        }

        private static void SaveConfigurations()
        {
            try
            {
                // Update config with current subpixel values
                config.SubpxR = [subpx_R.X, subpx_R.Y];
                config.SubpxG = [subpx_G.X, subpx_G.Y];
                config.SubpxB = [subpx_B.X, subpx_B.Y];
                config.IsCurvedScreen = curved_screen;
                config.CurvedControlPoints = [curved_screen_curve.X, curved_screen_curve.Y,
                    curved_screen_curve.Z, curved_screen_curve.W];
                config.CurvedStartY = curved_start_y;
                config.CurvedEndY = curved_end_y;
                config.CurvedScreenWidth = curve_width;

                config.TuneFineBias = tune_fine_bias;
                config.FineBiasCols = fine_bias_cols;
                config.FineBiasRows = fine_bias_rows;
                config.MainRectX0 = main_rect_x0;
                config.MainRectY0 = main_rect_y0;
                config.MainRectX1 = main_rect_x1;
                config.MainRectY1 = main_rect_y1;
                config.FineBiasCoarseVals = fine_bias_coarse_vals.ToArray();

                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "params.json");
                var jsonContent = JsonConvert.SerializeObject(config, ConfigSerializerSettings);
                File.WriteAllText(configPath, jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        private static Terminal remote;

        private static Panel mainpb = null;

        static void Main(string[] args)
        {
            if (args.Length == 2)
            {
                using (var vid1 = new VideoCapture(int.Parse(args[0])))
                using (var vid2 = new VideoCapture(int.Parse(args[1])))
                using (var frame1 = new Mat())
                using (var frame2 = new Mat())
                {
                    vid1.Set(VideoCaptureProperties.FrameWidth, 640);
                    vid2.Set(VideoCaptureProperties.FrameHeight, 480);

                    if (!vid1.IsOpened())
                    {
                        Console.WriteLine("Failed to open camera 1!");
                        return;
                    }

                    if (!vid2.IsOpened())
                    {
                        Console.WriteLine("Failed to open camera 2!");
                        return;
                    }

                    while (true)
                    {
                        // Capture the video frame
                        if (!vid1.Read(frame1))
                        {
                            Console.WriteLine("Failed to read camera 1!");
                            break;
                        }

                        if (!vid2.Read(frame2))
                        {
                            Console.WriteLine("Failed to read camera 2!");
                            break;
                        }

                        // Display the frame
                        Cv2.ImShow("frame1", frame1);
                        Cv2.ImShow("frame2", frame2);

                        if (Cv2.WaitKey(1) == 'q')
                            break;
                    }

                }

                return;
            }

            LoadConfigurations();

            arm = new MyArmControl();
            arm.Initialize();
            var dv = arm.GetDefaultPosition();
            arm.Goto(new Vector3(200, 0, 300));
            // arm.Goto(new Vector3(config.Bias[0] - 400, 0, config.Bias[2])); // standard caliberation place.
            arm.WaitForTarget();


            var CameraList = UsbCamera.FindDevices().Select(str => str.Replace(" ", "_")).ToArray();
            Console.WriteLine($"Found {CameraList.Length} cameras: {string.Join(", ", CameraList)}");
            var EyeCameras = CameraList.Select((p, i) => (p, i)).Where(p => p.p.Contains("USB_Camera")).Select(p=>(p.i,false)).ToDictionary();

            // Initialize left and right cameras using configured indices
            if (config?.LeftCameraIndex == -1)
            {
                Console.WriteLine("pass camera...");
            }
            else
            {
                int leftIdx = config?.LeftCameraIndex ?? 0;
                int rightIdx = config?.RightCameraIndex ?? 2;

                bool leftOK = false;
                bool rightOK = false;
                if (config != null)
                {
                    // Find camera indices by name if specified
                    if (!string.IsNullOrEmpty(config.LeftCameraName))
                    {
                        for (int i = 0; i < CameraList.Length; i++)
                        {
                            if (CameraList[i].Contains(config.LeftCameraName))
                            {
                                leftIdx = i;
                                EyeCameras[i] = true;
                                leftOK = true;
                                Console.WriteLine($"Found left camera: {CameraList[i]} at index {i}");
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(config.RightCameraName))
                    {
                        for (int i = 0; i < CameraList.Length; i++)
                        {
                            if (CameraList[i].Contains(config.RightCameraName))
                            {
                                rightIdx = i;
                                EyeCameras[i] = true;
                                rightOK = true;
                                Console.WriteLine($"Found right camera: {CameraList[i]} at index {i}");
                                break;
                            }
                        }
                    }
                }

                if (EyeCameras.Count == 2 && (leftOK || rightOK))
                {
                    if (!leftOK)
                        leftIdx = EyeCameras.First(pl => pl.Value == false).Key;
                    if (!rightOK)
                        leftIdx = EyeCameras.First(pl => pl.Value == false).Key;
                }


                Console.WriteLine($"Initializing cameras - Left: index {leftIdx}, Right: index {rightIdx}");
                try
                {
                    leftCamera.Initialize(leftIdx);
                    var leftFormats = UsbCamera.GetVideoFormat(leftIdx);
                    var leftFormat = leftFormats.FirstOrDefault(f => f.Size.Height == 720) ??
                                     leftFormats[0];
                    leftCamera.Initialize(leftIdx, leftFormat);

                    var rightFormats = UsbCamera.GetVideoFormat(rightIdx);
                    var rightFormat = rightFormats.FirstOrDefault(f => f.Size.Height == 720) ??
                                      rightFormats[0];
                    rightCamera.Initialize(rightIdx, rightFormat);
                }
                catch
                {
                    Console.WriteLine("No cameras");
                }
            }

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
            new SetAppearance(){useGround = false, drawGuizmo = false, useBloom = false, useSSAO = false, 
                useEDL = false, useBorder = false, drawGroundGrid = false}.IssueToDefault();

            new SetFullScreen() { screen_id = 1 }.IssueToDefault();

            var prev_state = false;
            // var manipulation = new UseGesture();
            // manipulation.ChangeState(new SetAppearance() { drawGuizmo = false });
            // manipulation.AddWidget(new UseGesture.ToggleWidget()
            // {
            //     name = $"fs",
            //     text = "WindowToggle",
            //     position = $"80%,5%",
            //     size = "9%,9%",
            //     keyboard = "f11",
            //     OnValue = (b) =>
            //     {
            //         if (b != prev_state)
            //             new SetFullScreen() { screen_id = 1, fullscreen = b }.IssueToTerminal(GUI.localTerminal);
            //         prev_state = b;
            //     }
            // });
            // manipulation.Start();

            LoadCalibrationMatrix();

            Terminal.RegisterRemotePanel(t =>
            {
                remote = t;

                var init = true;

                var v3i = arm.GetPos();
                float sx = v3i.X, sy = v3i.Y, sz = v3i.Z;
                var r3i = arm.GetRotation();
                float rx = r3i.X, ry = r3i.Y, rz = r3i.Z;

                // Lenticular parameters
                float dragSpeed = -6f; // e^-6 ≈ 0.0025
                bool edit = true, modbias = false;
                Color leftC = Color.Red, rightC = Color.Blue;

                float monitor_inches = 13.3f, world2phy=100;

                return pb =>
                {
                    mainpb = pb.Panel;
                    pb.Panel.ShowTitle("Caliberator");

                    pb.Panel.Repaint();

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

                    if (pb.Toggle("Windowed", ref prev_state))
                    {
                        Console.WriteLine($"set Windowed={prev_state}");
                        new SetFullScreen() { screen_id = 1, fullscreen = !prev_state }.IssueToTerminal(
                            GUI.localTerminal);
                    }

                    // Camera status
                    pb.SeparatorText("Basic Status");
                    pb.Label($"SH431 FPS={sh431.FPS}");
                    pb.Label($"Left Camera: {(leftCamera.IsActive ? "Active" : "Inactive")} ({leftCamera.FPS} FPS)");
                    pb.Label($"Right Camera: {(rightCamera.IsActive ? "Active" : "Inactive")} ({rightCamera.FPS} FPS)");
                    
                    pb.CollapsingHeaderStart("Detail Status and settings");
                    if (pb.Button("Swap Left Right camera"))
                    {
                        (leftCamera, rightCamera) = (rightCamera, leftCamera);
                    }

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
                    var vr = arm.GetRotation();

                    if (pb.Button("Save Tuning Place"))
                    {
                        File.AppendAllLines("tuning_places.txt", [$"{v3.X} {v3.Y} {v3.Z} {vr.X} {vr.Y} {vr.Z}"]);
                    }

                    // Position information
                    pb.Label($"实际位置 Position: X={v3.X:F1}, Y={v3.Y:F1}, Z={v3.Z:F1} mm");
                    pb.Label($"实际姿态 Rotation: RX={vr.X:F1}°, RY={vr.Y:F1}°, RZ={vr.Z:F1}°");
                    pb.Label($"robot2screen={-config.Bias[1]-v3.Y:0.0},{v3.Z-config.Bias[2]},{config.Bias[0]-v3.X}");
                    pb.CheckBox("Modify Bias", ref modbias);
                    if (modbias)
                    {
                        pb.DragFloat("bias2screen.X", ref config.Bias[0], 0.1f, -500, 1500);
                        pb.DragFloat("bias2screen.Y", ref config.Bias[1], 0.1f, -500, 1500);
                        pb.DragFloat("bias2screen.Z", ref config.Bias[2], 0.1f, -500, 1500);
                    }

                    pb.DragFloat("X", ref sx, 0.1f, -500, 500);
                    pb.DragFloat("Y", ref sy, 0.1f, -500, 500);
                    pb.DragFloat("Z", ref sz, 0.1f, -100, 800);
                    pb.DragFloat("rX", ref rx, 0.1f, -500, 500);
                    pb.DragFloat("rY", ref ry, 0.1f, -500, 500);
                    pb.DragFloat("rZ", ref rz, 0.1f, -500, 500);

                    if (pb.Button("Send"))
                    {
                        Console.WriteLine($"Goto {sx},{sy},{sz}({rx},{ry},{rz})...");
                        arm.Goto(new Vector3(sx, sy, sz), rx, ry, rz);
                    }
                    pb.Separator();
                    
                    // Status information
                    var armStatus = arm.GetStatus();
                    pb.Label($"控制模式: {arm.GetControlModeDescription()}");
                    pb.Label($"机械臂状态: {arm.GetArmStateDescription()}");
                    pb.Label($"示教状态: {arm.GetTeachingStateDescription()}");
                    pb.Label($"运动状态: {(armStatus.MovementState == 0 ? "已到达 (Reached)" : "运动中 (Moving)")}");
                    
                    // Error display
                    bool hasErrors = arm.HasErrors();
                    if (hasErrors)
                    {
                        pb.Separator();
                        pb.Label("⚠ 错误信息 (Errors):");
                        var faults = arm.GetFaultDetails();
                        if (faults.Count > 0)
                        {
                            foreach (var fault in faults)
                            {
                                pb.Label($"  • {fault}");
                            }
                        }
                        else if (armStatus.ArmState != 0)
                        {
                            pb.Label($"  • {arm.GetArmStateDescription()}");
                        }
                        pb.Label($"故障码: 0x{arm.GetFaultCode():X4}");
                    }
                    else
                    {
                        pb.Label("✓ 无错误 (No Errors)");
                    }
                    
                    pb.Separator();
                    
                    // Restore button
                    if (arm.restoreRunning)
                    {
                        pb.Label("⏳ 正在恢复中... (Restoring...)");
                    }
                    if (pb.Button("恢复机械臂状态 (Restore Arm State)", disabled: arm.restoreRunning))
                    {
                        Console.WriteLine("User requested arm state restore...");
                        new Thread(() =>
                        {
                            arm.RestoreArmState();
                        }).Start();
                    }

                    pb.CollapsingHeaderEnd();

                    pb.SeparatorText("Caliberation");
                    if (running != null)
                    {
                        pb.Label("Running=" + running);
                        pb.DelegateUI();
                    }

                    pb.CollapsingHeaderStart("EyeTracker Caliberation");
                    if (running==null && pb.Button("Caliberate EyeTracker Camera"))
                    {
                        new Thread(EyeTrackCaliberationProcedure).Start();
                    }

                    if (eye_tracker_caliberated && pb.Button("Save Calibration Matrix"))
                    {
                        SaveCalibrationMatrix();
                    }
                    
                    if (pb.Button("Load Calibration Matrix"))
                    {
                        LoadCalibrationMatrix();
                    }
                    pb.CollapsingHeaderEnd();
                    
                    pb.CollapsingHeaderStart("Coarse Parameters Tuning");

                    pb.CheckBox("Check", ref edit);

                    if (edit)
                    {
                        // Adjust speed control
                        pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -15.0f, 0.0f);
                        pb.Label($"Current Speed: {Math.Exp(dragSpeed):F6}");

                        var speed = (float)Math.Exp(dragSpeed);
                        // Fill color mode selection
                        // Lenticular parameter controls
                        var paramsChanged = pb.ColorEdit("Left Color", ref leftC);
                        paramsChanged |= pb.ColorEdit("Reft Color", ref rightC);

                        paramsChanged |= pb.DragFloat("Period Fill", ref period_fill, speed, 0, 100);
                        paramsChanged |= pb.DragFloat("Period Total", ref _priorPeriod, speed, 0, 100, edited_p);
                        paramsChanged |= pb.DragFloat("Phase Init Left", ref _priorBiasLeft, speed * 100, -100, 100,
                            edited_bl);
                        paramsChanged |= pb.DragFloat("Phase Init Right", ref _priorBiasRight, speed * 100, -100, 100,
                            edited_br);
                        paramsChanged |= pb.DragFloat("Phase Init Row Increment", ref prior_row_increment, speed, -100,
                            100);

                        edited_p = edited_bl = edited_br = false;

                        // RGB Subpixel Location controls
                        pb.SeparatorText("RGB Subpixel Offsets");
                        paramsChanged |= pb.DragVector2("Subpixel R Offset", ref subpx_R, speed, -5, 5);
                        paramsChanged |= pb.DragVector2("Subpixel G Offset", ref subpx_G, speed, -5, 5);
                        paramsChanged |= pb.DragVector2("Subpixel B Offset", ref subpx_B, speed, -5, 5);
                        
                        // Fine bias texture (8x1)
                        // (legacy) removed: old 8-value fine bias tuning UI

                        // Fine-bias tuning (block-based)
                        pb.SeparatorText("Fine Bias Fix (Block Tuning)");
                        if (pb.CheckBox("Tune Fine Bias", ref tune_fine_bias))
                        {
                            config.TuneFineBias = tune_fine_bias;
                            // mimic legacy behavior: disabling clears the bias-fix texture
                            if (!tune_fine_bias)
                            {
                                new SetHoloViewEyePosition
                                {
                                    updateEyePos = false,
                                    clearBiasFix = true
                                }.IssueToTerminal(GUI.localTerminal);
                            }
                        }
                        if (tune_fine_bias)
                        {
                            var fineCfgChanged = false;
                            fineCfgChanged |= pb.DragFloat("Bias Grid Cols", ref fine_bias_cols_f, 1, 1, 64);
                            fineCfgChanged |= pb.DragFloat("Bias Grid Rows", ref fine_bias_rows_f, 1, 1, 64);

                            // Clamp rect to current grid (top-left origin y=0 top)
                            fineCfgChanged |= pb.DragFloat("MainRect x0", ref main_rect_x0_f, 1, 0, fine_bias_cols - 1);
                            fineCfgChanged |= pb.DragFloat("MainRect y0", ref main_rect_y0_f, 1, 0, fine_bias_rows - 1);
                            fineCfgChanged |= pb.DragFloat("MainRect x1", ref main_rect_x1_f, 1, 0, fine_bias_cols - 1);
                            fineCfgChanged |= pb.DragFloat("MainRect y1", ref main_rect_y1_f, 1, 0, fine_bias_rows - 1);

                            var expected = fine_bias_cols * fine_bias_rows;
                            if (fine_bias_coarse_vals == null || fine_bias_coarse_vals.Length != expected)
                            {
                                var next = new float[expected];
                                if (fine_bias_coarse_vals != null)
                                    Array.Copy(fine_bias_coarse_vals, next, Math.Min(fine_bias_coarse_vals.Length, next.Length));
                                fine_bias_coarse_vals = next;
                                fineCfgChanged = true;
                            }

                            pb.Label("Coarse bias matrix (row-major, y=0 top):");
                            fineCfgChanged |= pb.DragMatrix(fine_bias_rows, fine_bias_cols, fine_bias_coarse_vals);

                            if (fineCfgChanged)
                            {
                                config.TuneFineBias = tune_fine_bias;
                                config.FineBiasCols = fine_bias_cols;
                                config.FineBiasRows = fine_bias_rows;
                                config.MainRectX0 = main_rect_x0;
                                config.MainRectY0 = main_rect_y0;
                                config.MainRectX1 = main_rect_x1;
                                config.MainRectY1 = main_rect_y1;
                                config.FineBiasCoarseVals = fine_bias_coarse_vals.ToArray();
                            }

                            // Preview apply (like legacy 8-bias tuning): when matrix changes, upload it immediately.
                            if (fineCfgChanged)
                            {
                                int cols = fine_bias_cols;
                                int rows = fine_bias_rows;
                                int pix = cols * rows;
                                var lr = new float[pix * 2];
                                for (int ii = 0; ii < pix; ii++)
                                {
                                    var v = fine_bias_coarse_vals[ii];
                                    lr[ii * 2 + 0] = v; // L
                                    lr[ii * 2 + 1] = v; // R (preview duplicates)
                                }

                                new SetHoloViewEyePosition
                                {
                                    updateEyePos = false,
                                    biasFixVals = lr,
                                    biasFixWidth = cols,
                                    biasFixHeight = rows
                                }.IssueToTerminal(GUI.localTerminal);
                            }
                        }
                        
                        // Curved screen controls
                        var curveChanged = false;
                        if (pb.CheckBox("Is Curved Screen", ref curved_screen))
                        {
                            config.IsCurvedScreen = curved_screen;
                            curveChanged = true;
                        }
                        if (curved_screen)
                        {
                            if (pb.BezierEditor("Curved Screen Profile", ref curved_screen_curve, ref curved_start_y, ref curved_end_y))
                            {
                                config.CurvedControlPoints = [curved_screen_curve.X, curved_screen_curve.Y,
                                    curved_screen_curve.Z, curved_screen_curve.W];
                                config.CurvedStartY = curved_start_y;
                                config.CurvedEndY = curved_end_y;
                                config.CurvedScreenWidth = curve_width;
                                curveChanged = true;
                            }
                        }

                        if (curveChanged)
                        {
                            // var (vals, w, h) = GetCurvedDisplayParams();
                            // new SetHoloViewEyePosition
                            // {
                            //     updateEyePos = false,
                            //     biasFixVals = vals,
                            //     biasFixWidth = w,
                            //     biasFixHeight = h
                            // }.IssueToTerminal(GUI.localTerminal);
                        }

                        if (paramsChanged || init)
                        {
                            new SetLenticularParams()
                            {
                                left_fill = leftC,
                                right_fill = rightC,
                                period_fill_left = period_fill,
                                period_fill_right = period_fill,
                                period_total_left = prior_period,
                                period_total_right = prior_period,
                                phase_init_left = prior_bias_left,
                                phase_init_right = prior_bias_right,
                                phase_init_row_increment_left = prior_row_increment,
                                phase_init_row_increment_right = prior_row_increment,
                                subpx_R = subpx_R,
                                subpx_G = subpx_G,
                                subpx_B = subpx_B,
                            }.IssueToTerminal(GUI.localTerminal);

                            config.PriorPeriod = prior_period;
                            config.PriorFill = period_fill;
                            config.PriorBiasLeft = prior_bias_left;
                            config.PriorBiasRight = prior_bias_right;
                            config.PriorRowIncrement = prior_row_increment;
                            init = false;
                        }
                    }

                    pb.CollapsingHeaderEnd();
                    if (pb.Button("Save Configurations"))
                        SaveConfigurations();

                    pb.SeparatorText("Lenticular Tuner");

                    LenticularTunerUI(pb);

                    pb.SeparatorText("Test Caliberated 3D screen");

                    if (pb.DragFloat("Screen size", ref monitor_inches, 0.01f, 1, 100))
                        new SetCamera() { monitor_inches = monitor_inches }.IssueToTerminal(GUI.localTerminal);
                    if (pb.DragFloat("World2phy", ref world2phy, 0.1f, 1, 1000))
                        new SetCamera() { world2phy = world2phy }.IssueToTerminal(GUI.localTerminal);

                    // Stripe parameter
                    if (pb.CheckBox("Stripe (0=off, 1=on)", ref stripe))
                        new SetLenticularParams()
                        {
                            stripe = stripe
                        }.IssueToTerminal(GUI.localTerminal);

                    // Stripe parameter
                    if (pb.RadioButtons("Display Mode", ["Line", "Bulk"], ref disp_type))
                    {
                        new SetLenticularParams()
                        {
                            mode = (SetLenticularParams.Mode)disp_type
                        }.IssueToTerminal(GUI.localTerminal);
                    }


                    if (pb.Button("Show exploding 3D object"))
                    {
                        SetCamera setcam = new SetCamera() { azimuth = -1.585f, altitude = 0.055f, lookAt = new Vector3(0.1904f, 3.5741f, 2.8654f), distance = 4.5170f, world2phy = 133f };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("sphere_explosion.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = rq,
                                Scale = 0.03f,
                                ColorBias = default,
                                ColorScale = 1,
                                Brightness = 1,
                                ForceDblFace = false,
                                NormalShading = 0
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                            { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToAllTerminals();

                        // set camera.
                        setcam.IssueToAllTerminals();
                        app.IssueToAllTerminals();
                    }

                    if (pb.Button("Show guernica"))
                    {
                        SetCamera setcam = new SetCamera()
                        {
                            azimuth = -1.6f,
                            altitude = -0.2f,
                            lookAt = new Vector3(-0.15f, 3.7f, 1.486f),
                            distance = 3.69f,
                            world2phy = 80
                        };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("guernica-3d.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = rq,
                                Scale = 1f,
                                ColorBias = default,
                                ColorScale = 1.0f,
                                Brightness = 1,
                                ForceDblFace = false,
                                NormalShading = 0
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                            { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        
                        // set camera.
                        setcam.IssueToTerminal(GUI.localTerminal);
                        app.IssueToTerminal(GUI.localTerminal);
                    }

                    if (pb.Button("Show Reverspective"))
                    {
                        SetCamera setcam = new SetCamera()
                        {
                            azimuth = -1.637f,
                            altitude = -0.073f,
                            lookAt = new Vector3(0.0567f, 0.4273f, 0.8764f),
                            distance = 0.5258f,
                            world2phy = 70f
                        };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("reverspective_painting.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = rq,
                                Scale = 1f,
                                ColorBias = default,
                                ColorScale = 1.0f,
                                Brightness = 1,
                                ForceDblFace = false,
                                NormalShading = 0
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                            { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

                        // set camera.
                        setcam.IssueToAllTerminals();
                        app.IssueToAllTerminals();
                    }

                    if (pb.Button("Show sayuri"))
                    {
                        SetCamera setcam = new SetCamera()
                        {
                            azimuth = -1.637f, altitude = -0.073f, lookAt = new Vector3(0.0567f, 0.4273f, 0.8764f),
                            distance = 0.5258f, world2phy = 100f
                        };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("sayuri_dance_fix.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = rq,
                                Scale = 1f,
                                ColorBias = default,
                                ColorScale = 1.0f,
                                Brightness = 1,
                                ForceDblFace = false,
                                NormalShading = 0
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                            { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToAllTerminals();

                        // set camera.
                        setcam.IssueToAllTerminals();
                        app.IssueToAllTerminals();
                    }

                    if (pb.Button("Show Warplane"))
                    {
                        SetCamera setcam = new SetCamera()
                        {
                            azimuth = -1.637f,
                            altitude = -0.073f,
                            lookAt = new Vector3(0.0567f, 0.4273f, 0.8764f),
                            distance = 0.5258f,
                            world2phy = 100f
                        };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("war_plane.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = rq,
                                Scale = 1f,
                                ColorBias = default,
                                ColorScale = 1.0f,
                                Brightness = 1,
                                ForceDblFace = false,
                                NormalShading = 0
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                            { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

                        // set camera.
                        setcam.IssueToAllTerminals();
                        app.IssueToAllTerminals();
                    }


                    Playback(pb);

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
