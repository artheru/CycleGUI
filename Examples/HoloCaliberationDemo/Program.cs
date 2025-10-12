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
using JsonCommentHandling = System.Text.Json.JsonCommentHandling;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace HoloCaliberationDemo
{
    internal class Program
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


        public class ArmToScreen
        {
            public float[] Bias { get; set; } = new float[] { 0, 0, 0 };
            public string LeftCameraName { get; set; } = "";
            public string RightCameraName { get; set; } = "";
            public int format { get; set; } = 0;
        }

        public class CalibrationData
        {
            public float[] cam_mat { get; set; } = new float[16];
        }

        public static Vector3 bias2screen;
        public static ArmToScreen config;

        private static string running = null;
        
        // Lenticular parameters
        private static float fill = 5;
        private static float period_total = 9;
        private static float phase_init_left = 5;
        private static float phase_init_right = 5;
        private static float phase_init_row_increment = 1;
        private static float dragSpeed = -6.0f; // e^-6 ≈ 0.0025
        private static int fillColorMode = 0; // 0: LRed/RBlue, 1: none, 2: AllRed, 3: LRed only, 4: RBlue only

        static Matrix4x4 FitTransformationMatrix(List<(Vector3 Camera, Vector3 Actual)> data)
        {
            // Solve for affine transformation: Actual = M * Camera (in homogeneous coordinates)
            // Using least squares: min ||AX - B||^2
            int n = data.Count;
            
            // Build matrices for least squares (solve for each output dimension separately)
            // We need to solve: [x y z 1] * [m00 m10 m20 m30; m01 m11 m21 m31; m02 m12 m22 m32; m03 m13 m23 m33]^T = [ax ay az]
            
            double[,] A = new double[n, 4]; // Camera points in homogeneous coords
            double[] Bx = new double[n];
            double[] By = new double[n];
            double[] Bz = new double[n];

            for (int i = 0; i < n; i++)
            {
                A[i, 0] = data[i].Camera.X;
                A[i, 1] = data[i].Camera.Y;
                A[i, 2] = data[i].Camera.Z;
                A[i, 3] = 1.0;

                Bx[i] = data[i].Actual.X;
                By[i] = data[i].Actual.Y;
                Bz[i] = data[i].Actual.Z;
            }

            // Solve least squares for each dimension
            double[] mx = SolveLeastSquares(A, Bx);
            double[] my = SolveLeastSquares(A, By);
            double[] mz = SolveLeastSquares(A, Bz);

            // Build transformation matrix (column-major for Matrix4x4)
            return new Matrix4x4(
                (float)mx[0], (float)mx[1], (float)mx[2], (float)mx[3],
                (float)my[0], (float)my[1], (float)my[2], (float)my[3],
                (float)mz[0], (float)mz[1], (float)mz[2], (float)mz[3],
                0, 0, 0, 1
            );
        }

        static double[] SolveLeastSquares(double[,] A, double[] b)
        {
            // Solve A^T * A * x = A^T * b
            int n = A.GetLength(0);
            int m = A.GetLength(1);

            double[,] AtA = new double[m, m];
            double[] Atb = new double[m];

            // Compute A^T * A
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < n; k++)
                        sum += A[k, i] * A[k, j];
                    AtA[i, j] = sum;
                }
            }

            // Compute A^T * b
            for (int i = 0; i < m; i++)
            {
                double sum = 0;
                for (int k = 0; k < n; k++)
                    sum += A[k, i] * b[k];
                Atb[i] = sum;
            }

            // Solve using Gaussian elimination with partial pivoting
            return GaussianElimination(AtA, Atb);
        }

        static double[] GaussianElimination(double[,] A, double[] b)
        {
            int n = b.Length;
            double[,] Ab = new double[n, n + 1];

            // Create augmented matrix
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    Ab[i, j] = A[i, j];
                Ab[i, n] = b[i];
            }

            // Forward elimination with partial pivoting
            for (int i = 0; i < n; i++)
            {
                // Find pivot
                int maxRow = i;
                for (int k = i + 1; k < n; k++)
                {
                    if (Math.Abs(Ab[k, i]) > Math.Abs(Ab[maxRow, i]))
                        maxRow = k;
                }

                // Swap rows
                for (int k = i; k < n + 1; k++)
                {
                    double tmp = Ab[maxRow, k];
                    Ab[maxRow, k] = Ab[i, k];
                    Ab[i, k] = tmp;
                }

                // Eliminate column
                for (int k = i + 1; k < n; k++)
                {
                    double factor = Ab[k, i] / Ab[i, i];
                    for (int j = i; j < n + 1; j++)
                        Ab[k, j] -= factor * Ab[i, j];
                }
            }

            // Back substitution
            double[] x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = Ab[i, n];
                for (int j = i + 1; j < n; j++)
                    x[i] -= Ab[i, j] * x[j];
                x[i] /= Ab[i, i];
            }

            return x;
        }

        static Vector3 TransformPoint(Matrix4x4 matrix, Vector3 point)
        {
            // Manual matrix-vector multiplication: result = matrix * [x, y, z, 1]^T
            // Treating point as column vector in homogeneous coordinates
            float x = matrix.M11 * point.X + matrix.M12 * point.Y + matrix.M13 * point.Z + matrix.M14;
            float y = matrix.M21 * point.X + matrix.M22 * point.Y + matrix.M23 * point.Z + matrix.M24;
            float z = matrix.M31 * point.X + matrix.M32 * point.Y + matrix.M33 * point.Z + matrix.M34;
            return new Vector3(x, y, z);
        }

        static float ComputeRMSE(Matrix4x4 matrix, List<(Vector3 Camera, Vector3 Actual)> data)
        {
            double sumSquaredError = 0;
            foreach (var pair in data)
            {
                Vector3 predicted = TransformPoint(matrix, pair.Camera);
                Vector3 error = predicted - pair.Actual;
                Console.WriteLine($"... Actual: {pair.Actual}, Predicted: {predicted}, Error: {error}");
                sumSquaredError += error.LengthSquared();
            }
            return (float)Math.Sqrt(sumSquaredError / data.Count);
        }

        static void CaliberationProcedure()
        {
            var data = new List<(Vector3 Camera, Vector3 Actual)>();
            var prevPos = arm.GetPos();
            if (running!=null) return;
            running = "Caliberation Camera";
            float[] Xs = [150, 250, 350, 450];       //30cm
            float[] Ys = [-200, -100, 0, 100, 200];  //40cm
            float[] Zs = [200, 300, 400, 500];       //30cm

            Console.WriteLine("=== Starting Calibration Data Collection ===");
            for (int j = 0; j < Ys.Length; ++j)
            for (int i=0; i<Xs.Length; ++i)
            for (int k = 0; k < Zs.Length; ++k)
            {
                var target = new Vector3(Xs[i], Ys[j], Zs[k]);
                Console.WriteLine($"goto {target}...");
                arm.Goto(target);

                // Use corrected WaitForTarget
                arm.WaitForTarget();
                Thread.Sleep(500);

                var iv = 0.5f * (sh431.original_right + sh431.original_left);
                var apos = arm.GetPos();
                if ((prevPos - apos).Length() < 20)
                {
                    Console.WriteLine("Robot not even move... skip");
                    continue;
                }

                if (iv.X != 0 && iv.Y != 0 && iv.Z != 0)
                {
                    data.Add((iv,
                        new Vector3(-bias2screen.Y - apos.Y, apos.Z - bias2screen.Z, bias2screen.X - apos.X)));
                    Console.WriteLine($"data point {data.Count}: Camera={iv}, Actual={data.Last().Actual}");
                }
                else
                {
                    Console.WriteLine("Camera no data, skip...");
                }

                prevPos = apos;
            }

            Console.WriteLine($"\n=== Collected {data.Count} calibration points ===");
            using (var writer = new System.IO.StreamWriter("cam_calib.csv"))
            {
                writer.WriteLine("Camera_X,Camera_Y,Camera_Z,Actual_X,Actual_Y,Actual_Z");
                foreach (var pair in data)
                {
                    writer.WriteLine(
                        $"{pair.Camera.X},{pair.Camera.Y},{pair.Camera.Z},{pair.Actual.X},{pair.Actual.Y},{pair.Actual.Z}"
                    );
                }
            }
            Console.WriteLine("Calibration data dumped to cam_calib.csv");

            if (data.Count < 4)
            {
                Console.WriteLine("ERROR: Not enough calibration points (need at least 4)");
                running = null;
                arm.GotoDefault();
                return;
            }

            // Fit transformation matrix
            Console.WriteLine("\n=== Fitting Transformation Matrix ===");
            cameraToActualMatrix = FitTransformationMatrix(data);
            Console.WriteLine($"Transformation Matrix:\n{cameraToActualMatrix}");

            // Compute fit error on calibration data
            float trainRMSE = ComputeRMSE(cameraToActualMatrix, data);
            Console.WriteLine($"\nTraining RMSE: {trainRMSE:F3} mm");
            
            // Show max error
            float maxError = 0;
            (Vector3 Camera, Vector3 Actual) worstPoint = default;
            foreach (var pair in data)
            {
                Vector3 predicted = TransformPoint(cameraToActualMatrix, pair.Camera);
                float error = (predicted - pair.Actual).Length();
                if (error > maxError)
                {
                    maxError = error;
                    worstPoint = pair;
                }
            }
            Console.WriteLine($"Max Training Error: {maxError:F3} mm at Camera={worstPoint.Camera}");

            // Test with random positions
            Console.WriteLine("\n=== Testing with Random Positions ===");
            var testData = new List<(Vector3 Camera, Vector3 Actual)>();
            Random rand = new Random();
            int numTests = 10;

            for (int i = 0; i < numTests; i++)
            {
                float x = Xs.Min() + (float)rand.NextDouble() * (Xs.Max() - Xs.Min());  // 150-450
                float y = Ys.Min() + (float)rand.NextDouble() * (Ys.Max() - Ys.Min());  // -200-200
                float z = Zs.Min() + (float)rand.NextDouble() * (Zs.Max() - Zs.Min());  // 200-500
                
                var target = new Vector3(x, y, z);
                Console.WriteLine($"\nTest {i+1}/{numTests}: goto {target}...");
                arm.Goto(target);
                arm.WaitForTarget();
                Thread.Sleep(500);

                var iv = 0.5f * (sh431.original_right + sh431.original_left);
                var apos = arm.GetPos();
                
                if (iv.X != 0 && iv.Y != 0 && iv.Z != 0)
                {
                    var actual = new Vector3(-bias2screen.Y - apos.Y, apos.Z - bias2screen.Z, bias2screen.X - apos.X);
                    testData.Add((iv, actual));
                    
                    Vector3 predicted = TransformPoint(cameraToActualMatrix, iv);
                    float error = (predicted - actual).Length();
                    Console.WriteLine($"  Camera: {iv}");
                    Console.WriteLine($"  Actual: {actual}");
                    Console.WriteLine($"  Predicted: {predicted}");
                    Console.WriteLine($"  Error: {error:F3} mm");
                }
            }

            if (testData.Count > 0)
            {
                float testRMSE = ComputeRMSE(cameraToActualMatrix, testData);
                Console.WriteLine($"\n=== Test Results ===");
                Console.WriteLine($"Test RMSE: {testRMSE:F3} mm");
                Console.WriteLine($"Test points: {testData.Count}/{numTests}");
            }

            arm.GotoDefault();
            running = null;
            Console.WriteLine("\n=== Calibration Complete ===");
        }

        static bool LoadConfiguration()
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

                config = JsonSerializer.Deserialize<ArmToScreen>(jsonContent, options);

                if (config != null)
                {
                    // Load initial position
                    if (config.Bias != null && config.Bias.Length >= 3)
                    {
                        bias2screen.X = config.Bias[0];
                        bias2screen.Y = config.Bias[1];
                        bias2screen.Z = config.Bias[2];
                        Console.WriteLine($"Loaded bias2screen:{bias2screen}");
                    }
                    
                    // Load camera configuration
                    Console.WriteLine($"Camera config - Left: {config.LeftCameraName}, Right: {config.RightCameraName}");
                    
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
                    }
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

        static void Main(string[] args)
        {
            LoadConfiguration();

            arm = new MyArmControl();
            arm.Initialize();
            var dv = arm.GetDefaultPosition();
            arm.Goto(new Vector3(bias2screen.X - 400, 0, bias2screen.Z)); // standard caliberation place.

            var CameraList = UsbCamera.FindDevices().Select(str => str.Replace(" ", "_")).ToArray();
            Console.WriteLine($"Found {CameraList.Length} cameras: {string.Join(", ", CameraList)}");

            // Initialize left and right cameras
            int leftIdx = 0;
            int rightIdx = 1;

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

            if (true){
                Console.WriteLine($"Initializing cameras - Left: index {leftIdx}, Right: index {rightIdx}, format={config.format}");
            
                // Initialize cameras with delay to reduce USB bandwidth issues
                // Try to find a lower resolution/MJPEG format to reduce bandwidth
                var leftFormats = UsbCamera.GetVideoFormat(leftIdx);
                foreach (var videoFormat in leftFormats)
                {
                    Console.WriteLine($"**>>{videoFormat}");
                }
                // var leftFormat = leftFormats.FirstOrDefault(f => f.Size.Height == 720) ??
                //                  leftFormats[0];
                leftCamera.Initialize(leftIdx, leftFormats[config.format]);
                // Console.WriteLine($"Left camera format: {leftFormat}");
                //
                var rightFormats = UsbCamera.GetVideoFormat(rightIdx);
                // var rightFormat = rightFormats.FirstOrDefault(f => f.Size.Height == 720) ??
                //                   rightFormats[0];
                // Console.WriteLine($"Right camera format: {rightFormat}");
                rightCamera.Initialize(rightIdx, rightFormats[config.format]);
            
                sh431 = new MySH431ULSteoro();
            }



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

            // new SetFullScreen().IssueToDefault();
            new SetCamera() { displayMode = SetCamera.DisplayMode.EyeTrackedHolography2 }.IssueToDefault();
            new SetLenticularParams()
            {
                left_fill = new Vector4(1,0,0,1), right_fill = new Vector4(0,0,1,1), 
                period_fill = 5,period_total = 9,phase_init_left = 5,phase_init_right = 5,phase_init_row_increment = 1
            }.IssueToDefault();

            Terminal.RegisterRemotePanel(t =>
            {
                var v3i = arm.GetPos();
                float sx = v3i.X, sy = v3i.Y, sz = v3i.Z;
                return pb =>
                {
                    pb.Panel.ShowTitle("Caliberator");

                    pb.Panel.Repaint();
                    
                    // Camera status
                    pb.Label($"Left Camera: {(leftCamera.IsActive ? "Active" : "Inactive")} ({leftCamera.FPS} FPS)");
                    pb.Label($"Right Camera: {(rightCamera.IsActive ? "Active" : "Inactive")} ({rightCamera.FPS} FPS)");
                    pb.Separator();
                    
                    pb.Label($"sh431::left={sh431.original_left}");
                    pb.Label($"sh431::right={sh431.original_right}");
                    var sv3c = 0.5f * (sh431.original_right + sh431.original_left);
                    pb.Label($"sh431={sv3c}");
                    //
                    var v3 = arm.GetPos();
                    pb.Label($"robot={v3}");

                    pb.DragFloat("X", ref sx, 0.1f, -500, 500);
                    pb.DragFloat("Y", ref sy, 0.1f, -500, 500);
                    pb.DragFloat("Z", ref sz, 0.1f, -500, 500);
                    if (pb.Button("Send"))
                    {
                        Console.WriteLine($"Goto {v3}...");
                        arm.Goto(new Vector3(sx, sy, sz));
                    }

                    if (running != null)
                        pb.Label(running);

                    if (running==null && pb.Button("Caliberate Camera"))
                    {
                        new Thread(CaliberationProcedure).Start();
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
                    if (pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -9.0f, 0.0f))
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

                    if (pb.Button("capture_all_red"))
                    {
                        left_all_reds = new byte[leftCamera.width * leftCamera.height];
                        for(int i=0; i<leftCamera.height; ++i)
                        for (int j = 0; j < leftCamera.width; ++j)
                            left_all_reds[i * leftCamera.width + j] =
                                leftCamera.preparedData[(i * leftCamera.width + j) * 4]; // only get red channel.
                        UITools.ImageShowMono("saliency", left_all_reds, leftCamera.width, leftCamera.height);
                    }

                    if (left_all_reds != null)
                    {
                        if (pb.Button("Compute Illum"))
                        {
                            float illum = 0;
                            for (int i = 0; i < leftCamera.height; ++i)
                            for (int j = 0; j < leftCamera.width; ++j)
                                illum += left_all_reds[i * leftCamera.width + j] *
                                         leftCamera.preparedData[(i * leftCamera.width + j) * 4] / 65535.0f; // only get red channel.
                            UITools.Alert($"Current illum={illum}");
                        }
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

        private static byte[] left_all_reds;
    }
}
