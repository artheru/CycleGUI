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
        private static float fine_bias_search_range = 0.4f; // Multiplier for search range (0.3~1.5)
        private static float[] fine_bias_coarse_vals = new float[5 * 3];
        
        // RGB subpixel offsets
        private static Vector2 subpx_R = new Vector2(0.0f, 0.0f);
        private static Vector2 subpx_G = new Vector2(1.0f / 3.0f, 0.0f);
        private static Vector2 subpx_B = new Vector2(2.0f / 3.0f, 0.0f);
        
        // Stripe parameter: 0 = no stripe, 1 = show diagonal stripe
        private static bool stripe = false;
        private static int disp_type;

        // Tuning places navigation
        private static List<(Vector3 pos, Vector3 rot)> tuningPlaces = new();
        private static int currentPlaceIndex = 0;

        private static void ReloadTuningPlaces()
        {
            tuningPlaces.Clear();
            currentPlaceIndex = 0;
            
            if (!File.Exists("tuning_places.txt"))
                return;
            
            try
            {
                var lines = File.ReadAllLines("tuning_places.txt");
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    
                    var parts = line.Split(new char[] { '\t', ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 6)
                        continue;
                    
                    if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float rx) &&
                        float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float ry) &&
                        float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float rz))
                    {
                        tuningPlaces.Add((new Vector3(x, y, z), new Vector3(rx, ry, rz)));
                    }
                }
                Console.WriteLine($"Loaded {tuningPlaces.Count} tuning places.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading tuning places: {ex.Message}");
            }
        }

        private static void SaveTuningPlaces()
        {
            try
            {
                var lines = tuningPlaces.Select(p => 
                    FormattableString.Invariant($"{p.pos.X} {p.pos.Y} {p.pos.Z} {p.rot.X} {p.rot.Y} {p.rot.Z}"));
                File.WriteAllLines("tuning_places.txt", lines);
                Console.WriteLine($"Saved {tuningPlaces.Count} tuning places.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving tuning places: {ex.Message}");
            }
        }

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

        /// <summary>
        /// Run fitting on tune_data.log and output results
        /// </summary>
        private static void RunFitTuneData(string tuneDataPath)
        {
            Console.WriteLine($"=== Lenticular Parameter Fitting ===");
            Console.WriteLine($"Input file: {tuneDataPath}");
            
            if (!File.Exists(tuneDataPath))
            {
                Console.WriteLine($"ERROR: File not found: {tuneDataPath}");
                return;
            }

            try
            {
                // Read and display file info
                var rawData = File.ReadAllText(tuneDataPath);
                var lines = rawData.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")).ToArray();
                Console.WriteLine($"Found {lines.Length} data lines (excluding comments/fine-bias)");
                
                // Pre-calibrated zBias value (no longer searching)
                double zBias = 0.0;
                
                Console.WriteLine($"\nUsing pre-calibrated zBias = {zBias}");
                Console.WriteLine("\n--- Starting Fit ---\n");

                var fitResult = LenticularParamFitter.FitFromRaw(rawData, zBias, Console.WriteLine);

                Console.WriteLine("\n--- Fit Complete ---\n");
                
                // Output results
                var cal = fitResult.Calibration;
                Console.WriteLine("=== CALIBRATION RESULTS ===");
                Console.WriteLine($"\n[Period Model]");
                Console.WriteLine($"  M (base period at infinity): {cal.Period.M:F6}");
                Console.WriteLine($"  DisplayHeight: {cal.Period.DisplayHeight:F6}");
                Console.WriteLine($"  ZBias: {cal.Period.ZBias:F3}");
                Console.WriteLine($"  Formula: period = M * (1 + DisplayHeight / (z + ZBias))");
                Console.WriteLine($"         = {cal.Period.M:F6} * (1 + {cal.Period.DisplayHeight:F6} / (z + {cal.Period.ZBias:F3}))");
                
                Console.WriteLine($"\n[Angle Model]");
                Console.WriteLine($"  Ax: {cal.Angle.Ax:F6}");
                Console.WriteLine($"  By: {cal.Angle.By:F6}");
                Console.WriteLine($"  Cz: {cal.Angle.Cz:F6}");
                Console.WriteLine($"  Bias: {cal.Angle.Bias:F6}");
                Console.WriteLine($"  Formula: angle = Ax*x + By*y + Cz*(z+ZBias) + Bias");
                
                Console.WriteLine($"\n[Bias Model]");
                Console.WriteLine($"  Scale: {cal.Bias.Scale:F6}");
                Console.WriteLine($"  Offset: {cal.Bias.Offset:F6}");
                
                if (cal.FineBias != null)
                {
                    Console.WriteLine($"\n[Fine-Bias Model] (Per-cell, 7 params each)");
                    Console.WriteLine($"  Grid: {cal.FineBias.Cols}x{cal.FineBias.Rows} cells");
                    Console.WriteLine($"  ZBias: {cal.FineBias.ZBias}");
                    Console.WriteLine($"  RMSE: {cal.FineBias.RMSE:F6}");
                    Console.WriteLine($"  Formula per cell: fb = A + B*(x/z) + C*(x/z)² + D*(y/z) + E*(y/z)² + F*(1/z) + G*(1/z)²");
                    Console.WriteLine($"  Cell coefficients (center cell [{cal.FineBias.Cols/2},{cal.FineBias.Rows/2}]):");
                    var centerCoeffs = cal.FineBias.CellCoeffs[cal.FineBias.Cols/2, cal.FineBias.Rows/2];
                    Console.WriteLine($"    A={centerCoeffs.A:+0.0000;-0.0000}, B={centerCoeffs.B:+0.0000;-0.0000}, C={centerCoeffs.C:+0.0000;-0.0000}");
                    Console.WriteLine($"    D={centerCoeffs.D:+0.0000;-0.0000}, E={centerCoeffs.E:+0.0000;-0.0000}");
                    Console.WriteLine($"    F={centerCoeffs.F:+0.0000;-0.0000}, G={centerCoeffs.G:+0.0000;-0.0000}");
                }
                
                Console.WriteLine($"\n[Residual Statistics]");
                Console.WriteLine($"  Period   - MAE: {fitResult.PeriodStats.MAE:F6}, RMSE: {fitResult.PeriodStats.RMSE:F6}, Max: {fitResult.PeriodStats.MaxAbsolute:F6}");
                Console.WriteLine($"  Angle    - MAE: {fitResult.AngleStats.MAE:F6}, RMSE: {fitResult.AngleStats.RMSE:F6}, Max: {fitResult.AngleStats.MaxAbsolute:F6}");
                Console.WriteLine($"  Bias     - MAE: {fitResult.BiasStats.MAE:F6}, RMSE: {fitResult.BiasStats.RMSE:F6}, Max: {fitResult.BiasStats.MaxAbsolute:F6}");
                if (fitResult.FineBiasStats.HasValue)
                {
                    var fbStats = fitResult.FineBiasStats.Value;
                    Console.WriteLine($"  FineBias - MAE: {fbStats.MAE:F6}, RMSE: {fbStats.RMSE:F6}, Max: {fbStats.MaxAbsolute:F6}");
                }
                
                // Save results to JSON
                string outputPath = Path.ChangeExtension(tuneDataPath, ".fit.json");
                var json = JsonConvert.SerializeObject(fitResult, new JsonSerializerSettings 
                { 
                    Formatting = Formatting.Indented,
                    FloatFormatHandling = FloatFormatHandling.Symbol
                });
                File.WriteAllText(outputPath, json);
                Console.WriteLine($"\nResults saved to: {outputPath}");
                
                // Self-test: predict for each sample and show error
                Console.WriteLine($"\n=== SELF TEST (Period/Angle/Bias base model) ===");
                Console.WriteLine("Sample predictions vs actual:");
                int sampleIdx = 0;
                foreach (var sr in fitResult.SampleResiduals.Take(5)) // Show first 5
                {
                    var s = sr.Sample;
                    var pred = cal.Predict(s.X, s.Y, s.Z);
                    Console.WriteLine($"  [{sampleIdx++}] {s.Eye} pos=({s.X:F1},{s.Y:F1},{s.Z:F1})");
                    Console.WriteLine($"       Period: actual={s.Period:F4}, pred={pred.Period:F4}, err={sr.PeriodResidual:+0.0000;-0.0000}");
                    Console.WriteLine($"       Angle:  actual={s.Angle:F4}, pred={pred.Angle:F4}, err={sr.AngleResidual:+0.0000;-0.0000}");
                    Console.WriteLine($"       Bias:   actual={s.Bias:F4}, pred={pred.Bias:F4}, err={sr.BiasResidual:+0.0000;-0.0000}");
                }
                if (fitResult.SampleResiduals.Count > 5)
                    Console.WriteLine($"  ... and {fitResult.SampleResiduals.Count - 5} more samples");

                // Fine-bias self-test: show actual vs predicted for each cell
                if (fitResult.FineBiasResiduals != null && fitResult.FineBiasResiduals.Count > 0)
                {
                    Console.WriteLine($"\n=== FINE-BIAS SELF TEST (per-cell model) ===");
                    Console.WriteLine("Fine-bias predictions vs actual (first few samples per cell):");
                    
                    // Group by cell and show a few samples from each
                    var cellGroups = fitResult.FineBiasResiduals
                        .GroupBy(r => (r.Col, r.Row))
                        .OrderBy(g => g.Key.Col * 10 + g.Key.Row);
                    
                    foreach (var group in cellGroups.Take(6)) // Show first 6 cells
                    {
                        var (col, row) = group.Key;
                        var samples = group.Take(3).ToList();
                        var cellRmse = Math.Sqrt(group.Average(r => r.Error * r.Error));
                        Console.WriteLine($"  Cell [{col},{row}] RMSE={cellRmse:F4}:");
                        foreach (var r in samples)
                        {
                            Console.WriteLine($"    pos=({r.X:F1},{r.Y:F1},{r.Z:F1}): actual={r.Actual:+0.000;-0.000}, pred={r.Predicted:+0.000;-0.000}, err={r.Error:+0.000;-0.000}");
                        }
                    }
                    Console.WriteLine($"  ... {cellGroups.Count() - 6} more cells");
                }
                
                // Tetrahedralization info
                Console.WriteLine($"\n=== INTERPOLATION INFO ===");
                Console.WriteLine($"  Samples: {fitResult.SampleResiduals.Count}, Tetrahedra: {fitResult.TetrahedraCount}");
                
                // Test jump detection between (-40,0,463) and (-40,0,478)
                Console.WriteLine($"\n=== JUMP DETECTION TEST ===");
                float testX = -40f, testY = 0f;
                float[] testZs = { 460, 463, 466, 469, 472, 475, 478, 481, 484 };
                
                Console.WriteLine("Testing Z trajectory at X=-40, Y=0:");
                Console.WriteLine("Z       | Period  | Angle   | Bias    | Mode   | Vertices");
                Console.WriteLine("--------|---------|---------|---------|--------|----------");
                
                double? prevPeriod = null, prevAngle = null, prevBias = null;
                foreach (var z in testZs)
                {
                    var (pred, info) = fitResult.PredictWithSample(testX, testY, z, 1.0f);
                    
                    string jumpMark = "";
                    if (prevPeriod.HasValue)
                    {
                        double dP = Math.Abs(pred.Period - prevPeriod.Value);
                        double dA = Math.Abs(pred.Angle - prevAngle.Value);
                        double dB = Math.Abs(pred.Bias - prevBias.Value);
                        if (dP > 0.001 || dA > 0.001 || dB > 0.1)
                            jumpMark = $" *** JUMP: dP={dP:F4}, dA={dA:F4}, dB={dB:F3}";
                    }
                    
                    // Get vertex indices used
                    string vertexInfo = info.Mode;
                    if (info.Weights != null)
                    {
                        var nonZeroWeights = info.Weights.Select((w, i) => (w, i))
                            .Where(x => x.w > 0.001)
                            .Select(x => $"v{x.i}:{x.w:F2}")
                            .ToArray();
                        vertexInfo += $" [{string.Join(",", nonZeroWeights)}]";
                    }
                    
                    Console.WriteLine($"{z,7:F0} | {pred.Period:F5} | {pred.Angle:F5} | {pred.Bias,7:F4} | {vertexInfo}{jumpMark}");
                    
                    prevPeriod = pred.Period;
                    prevAngle = pred.Angle;
                    prevBias = pred.Bias;
                }
                
                // Also check the fine-bias values at these points
                Console.WriteLine("\nFine-bias at same positions:");
                foreach (var z in new[] { 463f, 478f })
                {
                    var (pred, info) = fitResult.PredictWithSample(testX, testY, z, 1.0f);
                    Console.WriteLine($"Z={z}: FineBias grid:");
                    if (info.FineBiasAdjustment != null)
                    {
                        int fbCols = info.FineBiasAdjustment.GetLength(0);
                        int fbRows = info.FineBiasAdjustment.GetLength(1);
                        Console.WriteLine($"  FineBias adjustment ({fbCols}x{fbRows}):");
                        for (int row = 0; row < fbRows; row++)
                        {
                            var rowVals = Enumerable.Range(0, fbCols)
                                .Select(col => info.FineBiasAdjustment[col, row].ToString("F3"))
                                .ToArray();
                            Console.WriteLine($"    Row {row}: [{string.Join(", ", rowVals)}]");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  No FineBias adjustment available");
                    }
                }
                
                // Check fine-bias for jumps along Z trajectory
                Console.WriteLine("\nFine-bias trajectory check (cell 4,0):");
                int checkCol = 4, checkRow = 0;
                double? prevFB = null;
                foreach (var z in testZs)
                {
                    var (pred, info) = fitResult.PredictWithSample(testX, testY, z, 1.0f);
                    if (info.FineBiasAdjustment != null && 
                        checkCol < info.FineBiasAdjustment.GetLength(0) && 
                        checkRow < info.FineBiasAdjustment.GetLength(1))
                    {
                        double fb = info.FineBiasAdjustment[checkCol, checkRow];
                        string jumpMark = "";
                        if (prevFB.HasValue)
                        {
                            double d = Math.Abs(fb - prevFB.Value);
                            if (d > 0.1) jumpMark = $" *** JUMP: d={d:F3}";
                        }
                        Console.WriteLine($"  Z={z}: FB[{checkCol},{checkRow}]={fb:F4}{jumpMark}");
                        prevFB = fb;
                    }
                }
                
                // High resolution Z scan to find any discontinuities
                Console.WriteLine("\n=== HIGH-RES Z SCAN (looking for discontinuities) ===");
                float hrX = -40f, hrY = 0f;
                float hrZStart = 450f, hrZEnd = 500f, hrZStep = 1f;
                double? prevP = null, prevA = null, prevB = null;
                string? prevMode = null;
                List<string> discontinuities = new List<string>();
                
                for (float z = hrZStart; z <= hrZEnd; z += hrZStep)
                {
                    var (pred, info) = fitResult.PredictWithSample(hrX, hrY, z, 1.0f);
                    if (prevP.HasValue)
                    {
                        double dP = Math.Abs(pred.Period - prevP.Value);
                        double dA = Math.Abs(pred.Angle - prevA.Value);
                        double dB = Math.Abs(pred.Bias - prevB.Value);
                        
                        // Check for unusual jumps (more than expected for 1mm change)
                        bool hasJump = dP > 0.0001 || dA > 0.0001 || dB > 0.05;
                        bool modeChanged = prevMode != info.Mode;
                        
                        if (hasJump || modeChanged)
                        {
                            discontinuities.Add($"Z={z-hrZStep:F0}->{z:F0}: dP={dP:F5}, dA={dA:F5}, dB={dB:F4}, mode: {prevMode}->{info.Mode}");
                        }
                    }
                    prevP = pred.Period;
                    prevA = pred.Angle;
                    prevB = pred.Bias;
                    prevMode = info.Mode;
                }
                
                if (discontinuities.Count > 0)
                {
                    Console.WriteLine($"Found {discontinuities.Count} potential discontinuities:");
                    foreach (var d in discontinuities.Take(10))
                        Console.WriteLine($"  {d}");
                }
                else
                {
                    Console.WriteLine("No discontinuities found in Z=450-500 range.");
                }
                
                // Test: Take midpoint of L/R pairs, should be inside tetra and give average values
                Console.WriteLine($"\n=== L/R MIDPOINT INTERPOLATION TEST ===");
                var lSamples = fitResult.SampleResiduals.Where(sr => sr.Sample.Eye == "L").ToList();
                var rSamples = fitResult.SampleResiduals.Where(sr => sr.Sample.Eye == "R").ToList();
                Console.WriteLine($"  L samples: {lSamples.Count}, R samples: {rSamples.Count}");
                
                // Find L/R pairs at similar positions (within 100mm)
                int pairsTested = 0;
                double totalMidErr = 0;
                foreach (var lsr in lSamples.Take(10))
                {
                    var ls = lsr.Sample;
                    // Find closest R sample
                    var closestR = rSamples
                        .Select(rsr => new { rsr, dist = Math.Sqrt(
                            Math.Pow(rsr.Sample.X - ls.X, 2) + 
                            Math.Pow(rsr.Sample.Y - ls.Y, 2) + 
                            Math.Pow(rsr.Sample.Z - ls.Z, 2)) })
                        .OrderBy(x => x.dist)
                        .FirstOrDefault();
                    
                    if (closestR == null || closestR.dist > 100) continue;
                    
                    var rs = closestR.rsr.Sample;
                    
                    // Midpoint
                    float midX = (float)((ls.X + rs.X) / 2);
                    float midY = (float)((ls.Y + rs.Y) / 2);
                    float midZ = (float)((ls.Z + rs.Z) / 2);
                    
                    // Get predictions at L, R, and midpoint
                    var (predL, infoL) = fitResult.PredictWithSample((float)ls.X, (float)ls.Y, (float)ls.Z, 1.0f);
                    var (predR, infoR) = fitResult.PredictWithSample((float)rs.X, (float)rs.Y, (float)rs.Z, 1.0f);
                    var (predMid, infoMid) = fitResult.PredictWithSample(midX, midY, midZ, 1.0f);
                    
                    // Expected midpoint values (average of L and R)
                    double expectedPeriod = (predL.Period + predR.Period) / 2;
                    double expectedAngle = (predL.Angle + predR.Angle) / 2;
                    double expectedBias = (predL.Bias + predR.Bias) / 2;
                    
                    double periodErr = Math.Abs(predMid.Period - expectedPeriod);
                    double angleErr = Math.Abs(predMid.Angle - expectedAngle);
                    double biasErr = Math.Abs(predMid.Bias - expectedBias);
                    
                    if (pairsTested < 3)
                    {
                        Console.WriteLine($"  Pair {pairsTested + 1}: L({ls.X:F1},{ls.Y:F1},{ls.Z:F1}) <-> R({rs.X:F1},{rs.Y:F1},{rs.Z:F1}), dist={closestR.dist:F1}mm");
                        Console.WriteLine($"    Midpoint ({midX:F1},{midY:F1},{midZ:F1}): mode={infoMid.Mode}");
                        Console.WriteLine($"    L.Period={predL.Period:F4}, R.Period={predR.Period:F4}, Mid.Period={predMid.Period:F4}, Expected={(expectedPeriod):F4}");
                        Console.WriteLine($"    Period err={periodErr:F6}, Angle err={angleErr:F6}, Bias err={biasErr:F4}");
                        if (infoMid.Weights != null && infoMid.Weights.Length > 0)
                        {
                            Console.WriteLine($"    Weights: [{string.Join(", ", infoMid.Weights.Take(4).Select(w => w.ToString("F3")))}]");
                        }
                    }
                    
                    totalMidErr += periodErr + angleErr + biasErr;
                    pairsTested++;
                }
                Console.WriteLine($"  Tested {pairsTested} L/R pairs, total error sum: {totalMidErr:F6}");
                if (pairsTested > 0 && totalMidErr / pairsTested < 0.01)
                {
                    Console.WriteLine($"  OK: Midpoint interpolation gives expected average values.");
                }
                else if (pairsTested > 0)
                {
                    Console.WriteLine($"  NOTE: Midpoint values differ from simple L/R average (expected for barycentric interpolation).");
                }
                
                // Residual interpolation self-test: verify that PredictWithSample at sample points gives back actual values
                Console.WriteLine($"\n=== RESIDUAL INTERPOLATION SELF TEST (sigma=1.0) ===");
                Console.WriteLine("At each sample point, PredictWithSample(sigma=1) should return actual sample values:");
                int resTestIdx = 0;
                double maxPeriodErr = 0, maxAngleErr = 0, maxBiasErr = 0;
                double sumPeriodErrSq = 0, sumAngleErrSq = 0, sumBiasErrSq = 0;
                foreach (var sr in fitResult.SampleResiduals)
                {
                    var s = sr.Sample;
                    var (pred, debugInfo) = fitResult.PredictWithSample((float)s.X, (float)s.Y, (float)s.Z, 1.0f);
                    
                    double periodErr = pred.Period - s.Period;
                    double angleErr = pred.Angle - s.Angle;
                    double biasErr = pred.Bias - s.Bias;
                    // Bias error should be wrapped to period
                    biasErr = biasErr - Math.Round(biasErr / s.Period) * s.Period;
                    
                    maxPeriodErr = Math.Max(maxPeriodErr, Math.Abs(periodErr));
                    maxAngleErr = Math.Max(maxAngleErr, Math.Abs(angleErr));
                    maxBiasErr = Math.Max(maxBiasErr, Math.Abs(biasErr));
                    sumPeriodErrSq += periodErr * periodErr;
                    sumAngleErrSq += angleErr * angleErr;
                    sumBiasErrSq += biasErr * biasErr;
                    
                    // Show first 5 samples with details
                    if (resTestIdx < 5)
                    {
                        Console.WriteLine($"  [{resTestIdx}] {s.Eye} pos=({s.X:F1},{s.Y:F1},{s.Z:F1}) mode={debugInfo.Mode}");
                        Console.WriteLine($"       Period: actual={s.Period:F4}, pred={pred.Period:F4}, err={periodErr:+0.0000;-0.0000}");
                        Console.WriteLine($"       Angle:  actual={s.Angle:F6}, pred={pred.Angle:F6}, err={angleErr:+0.000000;-0.000000}");
                        Console.WriteLine($"       Bias:   actual={s.Bias:F4}, pred={pred.Bias:F4}, err={biasErr:+0.0000;-0.0000}");
                        if (debugInfo.Weights != null && debugInfo.Weights.Length > 0)
                        {
                            Console.WriteLine($"       Weights: [{string.Join(", ", debugInfo.Weights.Select(w => w.ToString("F4")))}]");
                            Console.WriteLine($"       Adjustments: P={debugInfo.PeriodAdjustment:+0.0000;-0.0000}, A={debugInfo.AngleAdjustment:+0.000000;-0.000000}, B={debugInfo.BiasAdjustment:+0.0000;-0.0000}");
                            if (debugInfo.FineBiasAdjustment != null)
                            {
                                // Show fine-bias adjustment grid (first 2x2)
                                var fb = debugInfo.FineBiasAdjustment;
                                Console.WriteLine($"       FineBiasAdj[0:2,0:2]: [{fb[0,0]:+0.000;-0.000},{fb[1,0]:+0.000;-0.000}],[{fb[0,1]:+0.000;-0.000},{fb[1,1]:+0.000;-0.000}]");
                            }
                        }
                    }
                    resTestIdx++;
                }
                int n = fitResult.SampleResiduals.Count;
                double rmsePeriod = Math.Sqrt(sumPeriodErrSq / n);
                double rmseAngle = Math.Sqrt(sumAngleErrSq / n);
                double rmseBias = Math.Sqrt(sumBiasErrSq / n);
                Console.WriteLine($"  ... tested {n} samples total");
                Console.WriteLine($"  Summary: Period RMSE={rmsePeriod:F6}, Max={maxPeriodErr:F6}");
                Console.WriteLine($"  Summary: Angle  RMSE={rmseAngle:F6}, Max={maxAngleErr:F6}");
                Console.WriteLine($"  Summary: Bias   RMSE={rmseBias:F6}, Max={maxBiasErr:F6}");
                if (rmsePeriod > 1e-6 || rmseAngle > 1e-6 || rmseBias > 0.01)
                {
                    Console.WriteLine($"  WARNING: Residual interpolation has significant errors at sample points!");
                    Console.WriteLine($"           This indicates a bug in the interpolation logic.");
                }
                else
                {
                    Console.WriteLine($"  OK: Residual interpolation correctly returns sample values at sample points.");
                }
                
                // Check fine-bias residual storage
                Console.WriteLine($"\n=== FINE-BIAS RESIDUAL STORAGE CHECK ===");
                int samplesWithFbResiduals = 0;
                int samplesWithoutFbResiduals = 0;
                foreach (var sr in fitResult.SampleResiduals)
                {
                    if (sr.FineBiasResiduals != null)
                        samplesWithFbResiduals++;
                    else
                        samplesWithoutFbResiduals++;
                }
                Console.WriteLine($"  Samples with fine-bias residuals: {samplesWithFbResiduals}");
                Console.WriteLine($"  Samples without fine-bias residuals: {samplesWithoutFbResiduals}");
                
                // Show first sample's fine-bias residual grid if available
                var firstWithFb = fitResult.SampleResiduals.FirstOrDefault(sr => sr.FineBiasResiduals != null);
                if (firstWithFb.FineBiasResiduals != null)
                {
                    var fb = firstWithFb.FineBiasResiduals;
                    int cols = fb.GetLength(0);
                    int rows = fb.GetLength(1);
                    Console.WriteLine($"  First sample's fine-bias residual grid ({cols}x{rows}):");
                    for (int r = 0; r < Math.Min(rows, 3); r++)
                    {
                        var rowVals = new List<string>();
                        for (int c = 0; c < Math.Min(cols, 5); c++)
                        {
                            rowVals.Add($"{fb[c, r]:+0.000;-0.000}");
                        }
                        Console.WriteLine($"    Row {r}: [{string.Join(", ", rowVals)}]");
                    }
                }
                
                // Fine-bias interpolation verification: at sample points, corrected fine-bias should match actual
                // Note: Only samples with exact position matches in SampleResiduals will get perfect results
                if (fitResult.FineBiasSamples != null && fitResult.FineBiasSamples.Count > 0 && 
                    fitResult.Calibration.FineBias != null)
                {
                    Console.WriteLine($"\n=== FINE-BIAS INTERPOLATION VERIFICATION ===");
                    var fbModel = fitResult.Calibration.FineBias;
                    double exactTotalErrSq = 0, exactMaxErr = 0;
                    int exactCount = 0;
                    double interpTotalErrSq = 0, interpMaxErr = 0;
                    int interpCount = 0;
                    
                    foreach (var fbSample in fitResult.FineBiasSamples.Take(5))
                    {
                        var (pred, debugInfo) = fitResult.PredictWithSample((float)fbSample.X, (float)fbSample.Y, (float)fbSample.Z, 1.0f);
                        var fbAdj = debugInfo.FineBiasAdjustment;
                        bool isExact = debugInfo.Mode == "exact" || 
                            (debugInfo.Weights != null && debugInfo.Weights.Length > 0 && debugInfo.Weights[0] > 0.999);
                        
                        Console.WriteLine($"  Sample {fbSample.Eye} pos=({fbSample.X:F1},{fbSample.Y:F1},{fbSample.Z:F1}) mode={debugInfo.Mode} exact={isExact}:");
                        
                        for (int r = 0; r < Math.Min(fbSample.Rows, 2) && r < fbModel.Rows; r++)
                        {
                            var errStr = new List<string>();
                            for (int c = 0; c < Math.Min(fbSample.Cols, 4) && c < fbModel.Cols; c++)
                            {
                                double actual = fbSample.FineBiasGrid[r, c];
                                double modelPred = fbModel.ComputeFineBias(c, r, fbSample.X, fbSample.Y, fbSample.Z);
                                double adjustment = fbAdj != null ? fbAdj[c, r] : 0;
                                double corrected = modelPred - adjustment;  // Apply residual fix
                                double err = corrected - actual;
                                
                                if (isExact)
                                {
                                    exactTotalErrSq += err * err;
                                    exactMaxErr = Math.Max(exactMaxErr, Math.Abs(err));
                                    exactCount++;
                                }
                                else
                                {
                                    interpTotalErrSq += err * err;
                                    interpMaxErr = Math.Max(interpMaxErr, Math.Abs(err));
                                    interpCount++;
                                }
                                errStr.Add($"e={err:+0.00;-0.00}");
                            }
                            Console.WriteLine($"    Row {r}: {string.Join(", ", errStr)}");
                        }
                    }
                    
                    double exactRmse = exactCount > 0 ? Math.Sqrt(exactTotalErrSq / exactCount) : 0;
                    double interpRmse = interpCount > 0 ? Math.Sqrt(interpTotalErrSq / interpCount) : 0;
                    Console.WriteLine($"  Exact matches ({exactCount} cells): RMSE={exactRmse:F4}, Max={exactMaxErr:F4}");
                    Console.WriteLine($"  Interpolated ({interpCount} cells): RMSE={interpRmse:F4}, Max={interpMaxErr:F4}");
                    
                    if (exactRmse > 0.01)
                    {
                        Console.WriteLine($"  WARNING: Exact matches have errors - bug in residual logic!");
                    }
                    else if (exactCount > 0)
                    {
                        Console.WriteLine($"  OK: Exact matches correctly return sample values.");
                    }
                }
                
                Console.WriteLine("\n=== DONE ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR during fitting: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void Main(string[] args)
        {
            // Handle --fit argument for offline fitting of tune_data.log
            if (args.Length >= 1 && args[0] == "--fit")
            {
                string tuneDataPath = args.Length >= 2 ? args[1] : "tune_data.log";
                RunFitTuneData(tuneDataPath);
                return;
            }
            
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
            ReloadTuningPlaces();

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

                    // Tuning places management
                    if (pb.Button("Save Current Place"))
                    {
                        File.AppendAllLines("tuning_places.txt", [$"{v3.X} {v3.Y} {v3.Z} {vr.X} {vr.Y} {vr.Z}"]);
                        ReloadTuningPlaces();
                    }
                    pb.SameLine();
                    if (pb.Button("Reload Places"))
                    {
                        ReloadTuningPlaces();
                    }
                    
                    if (tuningPlaces.Count > 0)
                    {
                        pb.Label($"Current: {currentPlaceIndex + 1} / {tuningPlaces.Count}");
                        
                        var (pos, rot) = tuningPlaces[currentPlaceIndex];
                        pb.Label($"Pos: ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})");
                        pb.Label($"Rot: ({rot.X:F1}, {rot.Y:F1}, {rot.Z:F1})");
                        
                        // Navigation buttons
                        if (pb.Button("◀ Prev", disabled: currentPlaceIndex <= 0))
                        {
                            currentPlaceIndex = Math.Max(0, currentPlaceIndex - 1);
                        }
                        pb.SameLine();
                        if (pb.Button("Next ▶", disabled: currentPlaceIndex >= tuningPlaces.Count - 1))
                        {
                            currentPlaceIndex = Math.Min(tuningPlaces.Count - 1, currentPlaceIndex + 1);
                        }

                        if (pb.Button("Go to This Place"))
                        {
                            var (targetPos, targetRot) = tuningPlaces[currentPlaceIndex];
                            Console.WriteLine($"Going to place {currentPlaceIndex + 1}: pos={targetPos}, rot={targetRot}");
                            arm.Goto(targetPos, targetRot.X, targetRot.Y, targetRot.Z);
                        }
                        
                        if (pb.Button("Delete This Place"))
                        {
                            tuningPlaces.RemoveAt(currentPlaceIndex);
                            SaveTuningPlaces();
                            if (currentPlaceIndex >= tuningPlaces.Count && tuningPlaces.Count > 0)
                                currentPlaceIndex = tuningPlaces.Count - 1;
                        }
                    }
                    else
                    {
                        pb.Label("No places loaded.");
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
                            pb.DragFloat("Search Range", ref fine_bias_search_range, 0.01f, 0.1f, 1.5f);

                            var expected = fine_bias_cols * fine_bias_rows;
                            if (fine_bias_coarse_vals == null || fine_bias_coarse_vals.Length != expected)
                            {
                                var next = new float[expected];
                                if (fine_bias_coarse_vals != null)
                                    Array.Copy(fine_bias_coarse_vals, next, Math.Min(fine_bias_coarse_vals.Length, next.Length));
                                fine_bias_coarse_vals = next;
                                fineCfgChanged = true;
                            }

                            if (pb.Button("Reset"))
                            {
                                fine_bias_coarse_vals = new float[expected];
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

                    if (pb.Button("Show Pac-Man"))
                    {
                        SetCamera setcam = new SetCamera()
                        {
                            azimuth = -1.574f,
                            altitude = 0.833f,
                            lookAt = new Vector3(0.2429f, 1.6750f, -2.3863f),
                            distance = 3.1820f,
                            world2phy = 91f
                        };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("pac-man_remaster.glb"))
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
                            { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity });
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

                        // Set camera tracking to Object_957 (Pac-Man)
                        Workspace.Prop(new SetObjectMoonTo() { earth = "glb1::Object_957", name = "me::camera" });

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

                    // Custom GLTF Viewer
                    GltfViewer(pb);

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
