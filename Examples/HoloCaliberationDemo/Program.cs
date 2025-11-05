using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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
        
        // RGB subpixel offsets
        private static Vector2 subpx_R = new Vector2(0.0f, 0.0f);
        private static Vector2 subpx_G = new Vector2(1.0f / 3.0f, 0.0f);
        private static Vector2 subpx_B = new Vector2(2.0f / 3.0f, 0.0f);

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
            arm.Goto(new Vector3(config.Bias[0] - 400, 0, config.Bias[2])); // standard caliberation place.
            arm.WaitForTarget();


            var CameraList = UsbCamera.FindDevices().Select(str => str.Replace(" ", "_")).ToArray();
            Console.WriteLine($"Found {CameraList.Length} cameras: {string.Join(", ", CameraList)}");

            // Initialize left and right cameras using configured indices
            int leftIdx = config?.LeftCameraIndex ?? 0;
            int rightIdx = config?.RightCameraIndex ?? 2;

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
                            Console.WriteLine($"Found right camera: {CameraList[i]} at index {i}");
                            break;
                        }
                    }
                }
            }

            Console.WriteLine($"Initializing cameras - Left: index {leftIdx}, Right: index {rightIdx}");
            leftCamera.Initialize(leftIdx);
            var leftFormats = UsbCamera.GetVideoFormat(leftIdx);
            var leftFormat = leftFormats.FirstOrDefault(f => f.Size.Height == 720) ??
                             leftFormats[0];
            leftCamera.Initialize(leftIdx, leftFormat);

            var rightFormats = UsbCamera.GetVideoFormat(rightIdx);
            var rightFormat = rightFormats.FirstOrDefault(f => f.Size.Height == 720) ??
                              rightFormats[0];
            rightCamera.Initialize(rightIdx, rightFormat);

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
            new SetFullScreen().IssueToDefault();

            LoadCalibrationMatrix();

            Terminal.RegisterRemotePanel(t =>
            {
                remote = t;

                var v3i = arm.GetPos();
                float sx = v3i.X, sy = v3i.Y, sz = v3i.Z;
                var r3i = arm.GetRotation();
                float rx = r3i.X, ry = r3i.Y, rz = r3i.Z;

                // Lenticular parameters
                float dragSpeed = -6f; // e^-6 ≈ 0.0025
                Color leftC = Color.Red, rightC = Color.Blue;

                return pb =>
                {
                    pb.Panel.ShowTitle("Caliberator");

                    pb.Panel.Repaint();

                    // Camera status
                    pb.CollapsingHeaderStart("Status");
                    pb.SeparatorText("Camera status");
                    pb.Label($"Left Camera: {(leftCamera.IsActive ? "Active" : "Inactive")} ({leftCamera.FPS} FPS)");
                    pb.Label($"Right Camera: {(rightCamera.IsActive ? "Active" : "Inactive")} ({rightCamera.FPS} FPS)");
                    if (pb.Button("Swap Left Right camera"))
                    {
                        (leftCamera, rightCamera) = (rightCamera, leftCamera);
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
                    pb.DragFloat("bias2screen.X", ref config.Bias[0], 0.1f, -500, 500);
                    pb.DragFloat("bias2screen.Y", ref config.Bias[1], 0.1f, -500, 500);
                    pb.DragFloat("bias2screen.Z", ref config.Bias[2], 0.1f, -500, 500);
                    pb.Label($"robot2screen={-config.Bias[1]-v3.Y:0.0},{v3.Z-config.Bias[2]},{config.Bias[0]-v3.X}");


                    pb.DragFloat("X", ref sx, 0.1f, -500, 500);
                    pb.DragFloat("Y", ref sy, 0.1f, -500, 500);
                    pb.DragFloat("Z", ref sz, 0.1f, -500, 500);
                    pb.DragFloat("rX", ref rx, 0.1f, -500, 500);
                    pb.DragFloat("rY", ref ry, 0.1f, -500, 500);
                    pb.DragFloat("rZ", ref rz, 0.1f, -500, 500);

                    if (pb.Button("Send"))
                    {
                        Console.WriteLine($"Goto {sx},{sy},{sz}({rx},{ry},{rz})...");
                        arm.Goto(new Vector3(sx, sy, sz), rx, ry, rz);
                    }
                    pb.CollapsingHeaderEnd();

                    pb.SeparatorText("Caliberation");
                    if (running != null)
                    {
                        pb.Label(running);
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
                    paramsChanged |= pb.DragFloat("Phase Init Left", ref _priorBiasLeft, speed*10, -100, 100, edited_bl);
                    paramsChanged |= pb.DragFloat("Phase Init Right", ref _priorBiasRight, speed*10, -100, 100, edited_br);
                    paramsChanged |= pb.DragFloat("Phase Init Row Increment", ref prior_row_increment, speed, -100, 100);

                    edited_p = edited_bl = edited_br = false;

                    // RGB Subpixel Location controls
                    pb.SeparatorText("RGB Subpixel Offsets");
                    paramsChanged |= pb.DragVector2("Subpixel R Offset", ref subpx_R, speed, -5, 5);
                    paramsChanged |= pb.DragVector2("Subpixel G Offset", ref subpx_G, speed, -5, 5);
                    paramsChanged |= pb.DragVector2("Subpixel B Offset", ref subpx_B, speed, -5, 5);

                    if (paramsChanged)
                    { 
                        new SetLenticularParams()
                        {
                            left_fill = leftC.Vector4(),
                            right_fill = rightC.Vector4(),
                            period_fill_left = period_fill,
                            period_fill_right = period_fill,
                            period_total_left  = prior_period,
                            period_total_right = prior_period,
                            phase_init_left = prior_bias_left,
                            phase_init_right = prior_bias_right,
                            phase_init_row_increment_left = prior_row_increment,
                            phase_init_row_increment_right = prior_row_increment,
                            subpx_R = subpx_R,
                            subpx_G = subpx_G,
                            subpx_B = subpx_B
                        }.IssueToTerminal(GUI.localTerminal);

                        config.PriorPeriod = prior_period;
                        config.PriorFill = period_fill;
                        config.PriorBiasLeft = prior_bias_left;
                        config.PriorBiasRight = prior_bias_right;
                        config.PriorRowIncrement = prior_row_increment;
                    }
                    
                    pb.CollapsingHeaderEnd();
                    if (pb.Button("Save Configurations"))
                        SaveConfigurations();

                    pb.SeparatorText("Lenticular Tuner");
                    LenticularTunerUI(pb);

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
