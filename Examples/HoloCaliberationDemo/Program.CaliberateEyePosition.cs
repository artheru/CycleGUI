using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HoloCaliberationDemo
{
    internal partial class Program
    {
        // Delivering functions:


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

        private static bool eye_tracker_caliberated = false;

        private static Vector3 TransformPoint(Matrix4x4 matrix, Vector3 point)
        {
            // Convert a camera position into screen space position.
            // Manual matrix-vector multiplication: result = matrix * [x, y, z, 1]^T
            // Treating point as column vector in homogeneous coordinates
            float x = matrix.M11 * point.X + matrix.M12 * point.Y + matrix.M13 * point.Z + matrix.M14;
            float y = matrix.M21 * point.X + matrix.M22 * point.Y + matrix.M23 * point.Z + matrix.M24;
            float z = matrix.M31 * point.X + matrix.M32 * point.Y + matrix.M33 * point.Z + matrix.M34;
            return new Vector3(x, y, z);
        }

        private static void EyeTrackCaliberationProcedure()
        {
            var data = new List<(Vector3 Camera, Vector3 Actual)>();
            var prevPos = arm.GetPos();
            if (running != null) return;
            running = "Eye Tracker Caliberation Camera";
            float[] Xs = [150, 250, 350, 450]; //30cm
            float[] Ys = [-200, -100, 0, 100, 200]; //40cm
            float[] Zs = [200, 300, 400, 500]; //30cm

            Console.WriteLine("=== Starting Calibration Data Collection ===");
            for (int j = 0; j < Ys.Length; ++j)
            for (int i = 0; i < Xs.Length; ++i)
            for (int k = 0; k < Zs.Length; ++k)
            {
                var target = new Vector3(Xs[i], Ys[j], Zs[k]);
                Console.WriteLine($"goto {target}...");
                arm.Goto(target);

                // Use corrected WaitForTarget
                arm.WaitForTarget();
                Thread.Sleep(1000);

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
                        new Vector3(-config.Bias[1] - apos.Y, apos.Z - config.Bias[2], config.Bias[0] - apos.X)));
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
                float x = Xs.Min() + (float)rand.NextDouble() * (Xs.Max() - Xs.Min()); // 150-450
                float y = Ys.Min() + (float)rand.NextDouble() * (Ys.Max() - Ys.Min()); // -200-200
                float z = Zs.Min() + (float)rand.NextDouble() * (Zs.Max() - Zs.Min()); // 200-500

                var target = new Vector3(x, y, z);
                Console.WriteLine($"\nTest {i + 1}/{numTests}: goto {target}...");
                arm.Goto(target);
                arm.WaitForTarget();
                Thread.Sleep(500);

                var iv = 0.5f * (sh431.original_right + sh431.original_left);
                var apos = arm.GetPos();

                if (iv.X != 0 && iv.Y != 0 && iv.Z != 0)
                {
                    var actual = new Vector3(-config.Bias[1] - apos.Y, apos.Z - config.Bias[2], config.Bias[0] - apos.X);
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

            eye_tracker_caliberated = true;
        }

        private static Matrix4x4 FitTransformationMatrix(List<(Vector3 Camera, Vector3 Actual)> data)
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

        private static float ComputeRMSE(Matrix4x4 matrix, List<(Vector3 Camera, Vector3 Actual)> data)
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
    }
}