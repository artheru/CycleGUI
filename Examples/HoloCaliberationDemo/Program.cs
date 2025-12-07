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
        
        // RGB subpixel offsets
        private static Vector2 subpx_R = new Vector2(0.0f, 0.0f);
        private static Vector2 subpx_G = new Vector2(1.0f / 3.0f, 0.0f);
        private static Vector2 subpx_B = new Vector2(2.0f / 3.0f, 0.0f);
        
        // Stripe parameter: 0 = no stripe, 1 = show diagonal stripe
        private static float stripe = 0.0f;

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
            var manipulation = new UseGesture();
            manipulation.ChangeState(new SetAppearance() { drawGuizmo = false });
            manipulation.AddWidget(new UseGesture.ToggleWidget()
            {
                name = $"fs",
                text = "WindowToggle",
                position = $"80%,5%",
                size = "9%,9%",
                keyboard = "f11",
                OnValue = (b) =>
                {
                    if (b != prev_state)
                        new SetFullScreen() { screen_id = 1, fullscreen = b }.IssueToTerminal(GUI.localTerminal);
                    prev_state = b;
                }
            });
            manipulation.Start();

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

                    // Position information
                    pb.Label($"实际位置 Position: X={v3.X:F1}, Y={v3.Y:F1}, Z={v3.Z:F1} mm");
                    pb.Label($"实际姿态 Rotation: RX={vr.X:F1}°, RY={vr.Y:F1}°, RZ={vr.Z:F1}°");
                    pb.Label($"robot2screen={-config.Bias[1]-v3.Y:0.0},{v3.Z-config.Bias[2]},{config.Bias[0]-v3.X}");
                    pb.CheckBox("Modify Bias", ref modbias);
                    if (modbias)
                    {
                        pb.DragFloat("bias2screen.X", ref config.Bias[0], 0.1f, -500, 1000);
                        pb.DragFloat("bias2screen.Y", ref config.Bias[1], 0.1f, -500, 1000);
                        pb.DragFloat("bias2screen.Z", ref config.Bias[2], 0.1f, -500, 1000);
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
                        
                        // Stripe parameter
                        pb.SeparatorText("Diagonal Stripe");
                        paramsChanged |= pb.DragFloat("Stripe (0=off, 1=on)", ref stripe, 0.1f, 0, 1);

                        if (paramsChanged || init)
                        {
                            new SetLenticularParams()
                            {
                                left_fill = leftC.Vector4(),
                                right_fill = rightC.Vector4(),
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
                                stripe = stripe
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
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

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
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

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
