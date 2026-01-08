using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using CycleGUI.API;
using CycleGUI;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using static System.Formats.Asn1.AsnWriter;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace HoloCaliberationDemo
{
    internal partial class Program
    {
        private const string ScreenParametersFileName = "screen_parameters.json";
        private static readonly JsonSerializerSettings ScreenParametersSerializerSettings = new()
        {
            Formatting = Formatting.Indented,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            Culture = CultureInfo.InvariantCulture
        };

        private static bool coarse_iteration = true, check_result = false;
        private static bool check_low_score = true; // If check_result is on, also check when score < threshold
        private static float check_score_threshold = 0.3f;
        private static readonly char[] TuneDataSeparators = ['\t', ' ', ',', ';'];
        private static float fill_rate = 1, passing_brightness = 0.8f;

        private sealed class TuneReplayEntry
        {
            public int Index { get; init; }
            public Vector3? LeftEye { get; set; }
            public Vector3? RightEye { get; set; }
            public float? LeftPeriod { get; set; }
            public float? RightPeriod { get; set; }
            public float? LeftBias { get; set; }
            public float? RightBias { get; set; }
            public float? LeftAngle { get; set; }
            public float? RightAngle { get; set; }
            public float? LeftScore { get; set; }
            public float? RightScore { get; set; }
            public Vector3? ArmTargetPosition { get; set; }
            public Vector3? ArmTargetRotation { get; set; }

            public bool HasLeftParameters => LeftPeriod.HasValue && LeftBias.HasValue && LeftAngle.HasValue;
            public bool HasRightParameters => RightPeriod.HasValue && RightBias.HasValue && RightAngle.HasValue;

            private static string FormatVector(Vector3? vec)
            {
                if (!vec.HasValue)
                    return "n/a";
                var v = vec.Value;
                return FormattableString.Invariant($"{v.X:0.0},{v.Y:0.0},{v.Z:0.0}");
            }

            private static string FormatParameters(string label, float? period, float? bias, float? angle, float? score)
            {
                if (!(period.HasValue || bias.HasValue || angle.HasValue))
                    return $"{label}: n/a";

                string Format(float? value, string format)
                    => value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "n/a";

                return $"{label} P{Format(period, "0.000")} B{Format(bias, "0.000")} A{Format(angle, "0.000")} S{Format(score, "0.00")}";
            }

            public string BuildButtonLabel()
            {
                var armPos = FormatVector(ArmTargetPosition);
                var armRot = FormatVector(ArmTargetRotation);
                var left = FormatParameters("L", LeftPeriod, LeftBias, LeftAngle, LeftScore);
                var right = FormatParameters("R", RightPeriod, RightBias, RightAngle, RightScore);
                return FormattableString.Invariant($"Replay #{Index + 1} | Pos {armPos} Rot {armRot} | {left} | {right}");
            }
        }

        // Cubic Bezier helpers on the current curved screen control points
        static float BezierP(float x)
        {
            // Control points from Program.cs (curved_screen_curve and endpoints)
            var p0 = new Vector2(0f, Program.curved_start_y);
            var p1 = new Vector2(Program.curved_screen_curve.X, Program.curved_screen_curve.Y);
            var p2 = new Vector2(Program.curved_screen_curve.Z, Program.curved_screen_curve.W);
            var p3 = new Vector2(1f, Program.curved_end_y);

            // Newton-Raphson to invert x(t) to find t for given x in [0,1]
            float t = x; // initial guess
            for (int i = 0; i < 8; i++)
            {
                // x(t)
                float xt = BezierEval(p0.X, p1.X, p2.X, p3.X, t);
                // dx/dt
                float dxt = BezierDeriv(p0.X, p1.X, p2.X, p3.X, t);
                float diff = xt - x;
                if (MathF.Abs(diff) < 1e-4f || MathF.Abs(dxt) < 1e-5f) break;
                t -= diff / dxt;
                t = MathF.Min(1f, MathF.Max(0f, t));
            }
            // return y(t)
            return BezierEval(p0.Y, p1.Y, p2.Y, p3.Y, t);
        }

        static float BezierK(float x)
        {
            var p0 = new Vector2(0f, Program.curved_start_y);
            var p1 = new Vector2(Program.curved_screen_curve.X, Program.curved_screen_curve.Y);
            var p2 = new Vector2(Program.curved_screen_curve.Z, Program.curved_screen_curve.W);
            var p3 = new Vector2(1f, Program.curved_end_y);

            float t = x;
            for (int i = 0; i < 8; i++)
            {
                float xt = BezierEval(p0.X, p1.X, p2.X, p3.X, t);
                float dxt = BezierDeriv(p0.X, p1.X, p2.X, p3.X, t);
                float diff = xt - x;
                if (MathF.Abs(diff) < 1e-4f || MathF.Abs(dxt) < 1e-5f) break;
                t -= diff / dxt;
                t = MathF.Min(1f, MathF.Max(0f, t));
            }
            float dyt = BezierDeriv(p0.Y, p1.Y, p2.Y, p3.Y, t);
            float dxt_final = BezierDeriv(p0.X, p1.X, p2.X, p3.X, t);
            if (MathF.Abs(dxt_final) < 1e-6f) return 0f;
            return dyt / dxt_final; // dy/dx at x
        }

        static float BezierEval(float p0, float p1, float p2, float p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 +
                   3f * u * u * t * p1 +
                   3f * u * t * t * p2 +
                   t * t * t * p3;
        }

        static float BezierDeriv(float p0, float p1, float p2, float p3, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (p1 - p0) +
                   6f * u * t * (p2 - p1) +
                   3f * t * t * (p3 - p2);
        }

        // Generate bias map from curved profile; bias0 is base bias, h controls range
        static (float[], int, int) GetCurvedDisplayParams(float bias0, float h)
        {
            // Simple 1D LUT stretched to 2D: width = 256 samples of x in [0,1], height = 1
            const int w = 256;
            const int htex = 1;
            float[] vals = new float[w * htex];
            for (int i = 0; i < w; i++)
            {
                float x = i / (float)(w - 1);
                float y = BezierP(x);
                float k = BezierK(x);
                // bias = base + scaled curve; here k is slope factor if needed; for now bias = bias0 + h*y
                vals[i] = bias0 + h * y;
            }
            return (vals, w, htex);
        }


        private static bool stop_now = false;
        private static void LenticularTunerUI(PanelBuilder pb)
        {
            var v3 = arm.GetPos();
            var vr = arm.GetRotation();

            pb.DragFloat("grating_brightness", ref grating_bright, 0.01f, 0, 255);
            pb.DragFloat("Saliency fill rate", ref fill_rate, 0.001f, 0, 1);
            pb.DragFloat("Passing brightness", ref passing_brightness, 0.001f, 0, 1);
            pb.CheckBox("Check results", ref check_result);
            if (check_result)
            {
                pb.SameLine();
                pb.CheckBox("Check if score < threshold", ref check_low_score);
                if (check_low_score)
                    pb.DragFloat("Score threshold", ref check_score_threshold, 0.01f, 0, 1);
            }
            pb.CheckBox("Coarse Tune", ref coarse_iteration);
            if (running == null && pb.Button("Tune Lenticular Parameters"))
            {
                stop_now = false;
                new Thread(()=>
                {
                    try
                    {
                        LenticularTuner();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"lenticular tuner stopped, ex={ex.MyFormat()}");
                    }
                }).Start();
            }

            if (pb.Button("Fit Tuned Parameters"))
            {
                ShowFitTunedParametersPanel();
            }

            if (pb.Button("Replay tuned parameters"))
            {
                ShowReplayTunedParametersPanel();
            }

            if (pb.Button("Break tuning"))
                stop_now = true;
        }

        private static List<TuneReplayEntry> LoadTuneReplayEntries(List<string> warnings, out string? errorMessage)
        {
            var entries = new List<TuneReplayEntry>();
            var logPath = "tune_data.log";

            if (!File.Exists(logPath))
            {
                errorMessage = "tune_data.log not found.";
                return entries;
            }

            try
            {
                var lines = File.ReadAllLines(logPath);
                TuneReplayEntry? current = null;
                int lineIndex = 0;

                bool TryParseFloat(string text, out float value)
                    => float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out value);

                foreach (var rawLine in lines)
                {
                    lineIndex++;
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    var parts = rawLine.Split(TuneDataSeparators, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        warnings.Add($"Line {lineIndex}: not enough values.");
                        continue;
                    }

                    string prefix = parts[0];
                    if (!prefix.Equals("L", StringComparison.OrdinalIgnoreCase) &&
                        !prefix.Equals("R", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Line {lineIndex}: unknown prefix '{prefix}'.");
                        continue;
                    }

                    if (parts.Length < 8)
                    {
                        warnings.Add($"Line {lineIndex}: expected at least 8 fields.");
                        continue;
                    }

                    bool TryParseVector(int startIndex, out Vector3 vector)
                    {
                        vector = default;
                        if (startIndex + 2 >= parts.Length)
                            return false;
                        if (!TryParseFloat(parts[startIndex], out var x) ||
                            !TryParseFloat(parts[startIndex + 1], out var y) ||
                            !TryParseFloat(parts[startIndex + 2], out var z))
                            return false;
                        vector = new Vector3(x, y, z);
                        return true;
                    }

                    bool TryParseParams(out Vector3 pos, out float period, out float bias, out float angle, out float score,
                        out Vector3? armPos, out Vector3? armRot)
                    {
                        pos = default;
                        period = bias = angle = score = 0;
                        armPos = null;
                        armRot = null;
                        if (!TryParseVector(1, out pos))
                            return false;
                        if (!TryParseFloat(parts[4], out period) ||
                            !TryParseFloat(parts[5], out bias) ||
                            !TryParseFloat(parts[6], out angle))
                            return false;
                        if (parts.Length > 7 && TryParseFloat(parts[7], out var sc))
                            score = sc;
                        else
                            score = float.NaN;

                        if (parts.Length >= 14)
                        {
                            if (TryParseFloat(parts[8], out var ax) &&
                                TryParseFloat(parts[9], out var ay) &&
                                TryParseFloat(parts[10], out var az) &&
                                TryParseFloat(parts[11], out var arx) &&
                                TryParseFloat(parts[12], out var ary) &&
                                TryParseFloat(parts[13], out var arz))
                            {
                                armPos = new Vector3(ax, ay, az);
                                armRot = new Vector3(arx, ary, arz);
                            }
                            else
                            {
                                warnings.Add($"Line {lineIndex}: failed to parse robot pose fields.");
                            }
                        }
                        else if (parts.Length > 8)
                        {
                            warnings.Add($"Line {lineIndex}: expected 6 arm pose values after score.");
                        }
                        return true;
                    }

                    if (!TryParseParams(out var eyePos, out var period, out var bias, out var angle, out var score,
                        out var armPos, out var armRot))
                    {
                        warnings.Add($"Line {lineIndex}: failed to parse numeric values.");
                        continue;
                    }

                    if (prefix.Equals("L", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new TuneReplayEntry
                        {
                            Index = entries.Count,
                            LeftEye = eyePos,
                            LeftPeriod = period,
                            LeftBias = bias,
                            LeftAngle = angle,
                            LeftScore = float.IsNaN(score) ? null : score,
                        };
                        if (armPos.HasValue)
                            current.ArmTargetPosition = armPos;
                        if (armRot.HasValue)
                            current.ArmTargetRotation = armRot;
                        entries.Add(current);
                    }
                    else
                    {
                        if (current == null || current.HasRightParameters)
                        {
                            current = entries.Count > 0 ? entries[^1] : null;
                            if (current == null || current.HasRightParameters)
                            {
                                current = new TuneReplayEntry { Index = entries.Count };
                                entries.Add(current);
                            }
                        }

                        current.RightEye = eyePos;
                        current.RightPeriod = period;
                        current.RightBias = bias;
                        current.RightAngle = angle;
                        current.RightScore = float.IsNaN(score) ? null : score;
                        if (armPos.HasValue)
                            current.ArmTargetPosition = armPos;
                        if (armRot.HasValue)
                            current.ArmTargetRotation = armRot;
                    }
                }

                errorMessage = null;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to read tune_data.log: {ex.Message}";
            }

            return entries;
        }

        private static void ShowReplayTunedParametersPanel()
        {
            List<string> warnings = new();
            string? errorMessage;
            var entries = LoadTuneReplayEntries(warnings, out errorMessage);

            GUI.PromptOrBringToFront(pb =>
            {
                pb.Panel.ShowTitle("Replay tuned parameters");
                pb.Panel.Repaint();

                if (pb.Closing())
                {
                    pb.Panel.Exit();
                    return;
                }

                if (errorMessage != null)
                {
                    pb.Label(errorMessage);
                    if (pb.Button("Retry loading"))
                    {
                        entries = LoadTuneReplayEntries(warnings, out errorMessage);
                    }
                    return;
                }

                if (warnings.Count > 0)
                {
                    foreach (var warning in warnings)
                        pb.Label($"Warning: {warning}");
                }

                if (entries.Count == 0)
                {
                    pb.Label("No entries found in tune_data.log.");
                    if (pb.Button("Reload entries"))
                    {
                        entries = LoadTuneReplayEntries(warnings, out errorMessage);
                    }
                    return;
                }

                for (int i = 0; i < entries.Count; ++i)
                {
                    var entry = entries[i];
                    if (pb.Button(entry.BuildButtonLabel()))
                    {
                        ReplayTuneEntry(entry);
                    }
                    pb.Separator();
                }

                if (pb.Button("Reload entries"))
                {
                    entries = LoadTuneReplayEntries(warnings, out errorMessage);
                }
            }, remote);
        }

        private static void ReplayTuneEntry(TuneReplayEntry entry)
        {
            if (entry == null)
                return;

            if (entry.ArmTargetPosition.HasValue && entry.ArmTargetRotation.HasValue)
            {
                var pos = entry.ArmTargetPosition.Value;
                var rot = entry.ArmTargetRotation.Value;
                Console.WriteLine($"Replaying entry #{entry.Index + 1}: goto {pos} rot {rot}.");
                arm.Goto(pos, rot.X, rot.Y, rot.Z);
                arm.WaitForTarget();
            }
            else
            {
                Console.WriteLine($"Entry #{entry.Index + 1}: no arm target recorded; skipping arm movement.");
            }

            if (!entry.HasLeftParameters || !entry.HasRightParameters)
            {
                UITools.Alert($"Entry #{entry.Index + 1} is missing lenticular parameters.");
                return;
            }

            new SetLenticularParams
            {
                left_fill = Color.Red,
                right_fill = Color.Blue,
                period_fill_left = period_fill,
                period_fill_right = period_fill,
                period_total_left = entry.LeftPeriod!.Value,
                period_total_right = entry.RightPeriod!.Value,
                phase_init_left = entry.LeftBias!.Value,
                phase_init_right = entry.RightBias!.Value,
                phase_init_row_increment_left = entry.LeftAngle!.Value,
                phase_init_row_increment_right = entry.RightAngle ?? entry.LeftAngle!.Value
            }.IssueToTerminal(GUI.localTerminal);
        }

        static LenticularParamFitter.FitResult fitResult = null;
        private static bool testEnabled = false;
        private static float dbg_lvl = 1;
        private static float sigma = 1.0f;
        private static LenticularParamFitter.InterpolationDebugInfo sample_residual_l = null;
        private static LenticularParamFitter.InterpolationDebugInfo sample_residual_r = null;

        // Predictive filtering for eye positions
        private static readonly List<(DateTime timestamp, Vector3 leftEye, Vector3 rightEye)> eyeHistory = new();
        private static readonly object eyeHistoryLock = new();

        private static void ClearEyePredictionHistory()
        {
            lock (eyeHistoryLock)
            {
                eyeHistory.Clear();
            }
        }

        private static (Vector3 predictedLeft, Vector3 predictedRight) PredictEyePositions(
            Vector3 currentLeft, Vector3 currentRight, float predictiveMs)
        {
            lock (eyeHistoryLock)
            {
                var now = DateTime.Now;
                var windowMs = predictiveMs + 30;

                // Add current sample
                eyeHistory.Add((now, currentLeft, currentRight));

                // Remove old samples outside the window
                var cutoff = now.AddMilliseconds(-windowMs);
                eyeHistory.RemoveAll(e => e.timestamp < cutoff);

                if (eyeHistory.Count < 2)
                {
                    // Not enough data for prediction, return current
                    return (currentLeft, currentRight);
                }

                // Linear regression for prediction
                // Use time in ms as x, position components as y
                var n = eyeHistory.Count;
                double sumT = 0, sumT2 = 0;
                double sumLX = 0, sumLY = 0, sumLZ = 0;
                double sumTLX = 0, sumTLY = 0, sumTLZ = 0;
                double sumRX = 0, sumRY = 0, sumRZ = 0;
                double sumTRX = 0, sumTRY = 0, sumTRZ = 0;

                var t0 = eyeHistory[0].timestamp;
                foreach (var (ts, l, r) in eyeHistory)
                {
                    var t = (ts - t0).TotalMilliseconds;
                    sumT += t;
                    sumT2 += t * t;
                    sumLX += l.X; sumLY += l.Y; sumLZ += l.Z;
                    sumTLX += l.X * t; sumTLY += l.Y * t; sumTLZ += l.Z * t;
                    sumRX += r.X; sumRY += r.Y; sumRZ += r.Z;
                    sumTRX += r.X * t; sumTRY += r.Y * t; sumTRZ += r.Z * t;
                }

                var denom = n * sumT2 - sumT * sumT;
                if (Math.Abs(denom) < 1e-10)
                {
                    return (currentLeft, currentRight);
                }

                var predictTime = (now - t0).TotalMilliseconds + predictiveMs;

                // For left eye: y = intercept + slope * t
                var slopeLX = (n * sumTLX - sumT * sumLX) / denom;
                var slopeLY = (n * sumTLY - sumT * sumLY) / denom;
                var slopeLZ = (n * sumTLZ - sumT * sumLZ) / denom;
                var interceptLX = (sumLX - slopeLX * sumT) / n;
                var interceptLY = (sumLY - slopeLY * sumT) / n;
                var interceptLZ = (sumLZ - slopeLZ * sumT) / n;
                var predictedL = new Vector3(
                    (float)(interceptLX + slopeLX * predictTime),
                    (float)(interceptLY + slopeLY * predictTime),
                    (float)(interceptLZ + slopeLZ * predictTime));

                // For right eye
                var slopeRX = (n * sumTRX - sumT * sumRX) / denom;
                var slopeRY = (n * sumTRY - sumT * sumRY) / denom;
                var slopeRZ = (n * sumTRZ - sumT * sumRZ) / denom;
                var interceptRX = (sumRX - slopeRX * sumT) / n;
                var interceptRY = (sumRY - slopeRY * sumT) / n;
                var interceptRZ = (sumRZ - slopeRZ * sumT) / n;
                var predictedR = new Vector3(
                    (float)(interceptRX + slopeRX * predictTime),
                    (float)(interceptRY + slopeRY * predictTime),
                    (float)(interceptRZ + slopeRZ * predictTime));

                return (predictedL, predictedR);
            }
        }

        private static void ShowFitTunedParametersPanel()
        {
            string errorMessage = null;
            string origin = "Disk(screen_parameters.json)";

            float periodZSearchStart = -100;
            float periodZSearchEnd = 100;

            float predictiveMs = 60; //60ms.

            void RefreshFit(bool fromDisk)
            {
                try
                {
                    if (fromDisk)
                    {
                        if (!File.Exists(ScreenParametersFileName))
                        {
                            throw new System.IO.FileNotFoundException($"Screen parameters file not found: {ScreenParametersFileName}", ScreenParametersFileName);
                        }

                        fitResult = LoadScreenParametersFromJson(ScreenParametersFileName);
                        origin = "Disk(screen_parameters.json)";
                    }
                    else
                    {
                        fitResult = LenticularParamFitter.FitFromFile("tune_data.log", periodZSearchStart, periodZSearchEnd, Console.WriteLine);
                        SaveScreenParametersToJson(fitResult, ScreenParametersFileName);
                        origin = "Fit";
                    }

                    errorMessage = null;
                }
                catch (Exception ex)
                {
                    fitResult = null;
                    errorMessage = ex.MyFormat();
                }
            }

            if (File.Exists("screen_parameters.json"))
            {
                RefreshFit(true);
            }
            else
            {
                errorMessage = "Not fitted";
                origin = "Fit";
            }

            LenticularParamFitter.Prediction leftPrediction=default, rightPrediction=default;
            GUI.PromptOrBringToFront(pb =>
            {
                pb.Panel.ShowTitle("Fit tuned parameters");

                if (pb.Closing())
                {
                    pb.Panel.Exit();
                    return;
                }

                pb.DragFloat("Period Z search start", ref periodZSearchStart, 0.1f, -5000, 5000);
                pb.DragFloat("Period Z search end", ref periodZSearchEnd, 0.1f, -5000, 5000);

                if (fitResult == null)
                {
                    pb.Label("No fit available.");
                    if (pb.Button("Reload fit"))
                    {
                        RefreshFit(false);
                    }
                    return;
                }

                if (errorMessage != null)
                {
                    pb.Label($"Error: {errorMessage}");
                    return;
                }


                pb.Label($"Origin:{origin}");
                pb.Label($"Using samples: {fitResult.SampleResiduals.Count}");
                pb.Label($"RMSE: period={fitResult.PeriodStats.RMSE}, angle={fitResult.AngleStats.RMSE}, bias={fitResult.BiasStats.RMSE}");

                pb.Separator();

                pb.Label($"Period => M={fitResult.Calibration.Period.M:F6}, DisplayHeight={fitResult.Calibration.Period.DisplayHeight:F6}, ZBias={fitResult.Calibration.Period.ZBias:F3}");
                pb.Label($"Formula: period = M * (1 + DisplayHeight / (z + ZBias))");
                pb.Label($"Angle => Ax={fitResult.Calibration.Angle.Ax:F6}, By={fitResult.Calibration.Angle.By:F6}, Cz={fitResult.Calibration.Angle.Cz:F6}, Bias={fitResult.Calibration.Angle.Bias:F6}");
                pb.Label($"Bias => Scale={fitResult.Calibration.Bias.Scale:+0.000000;-0.000000;+0.000000}, Offset={fitResult.Calibration.Bias.Offset:+0.000000;-0.000000;+0.000000}");

                pb.Separator();


                pb.Separator();
                if (pb.CheckBox("Test fitted parameters", ref testEnabled))
                {
                    if (testEnabled)
                        sh431.act = (original_left, original_right) =>
                        {
                            var leftEye = TransformPoint(cameraToActualMatrix, original_left);
                            var rightEye = TransformPoint(cameraToActualMatrix, original_right);

                            if (leftEye.X == 0)
                            {
                                leftPrediction.obsolete = true;
                                ClearEyePredictionHistory();
                                return;
                            }

                            // Predictive filtering: predict eye positions after predictiveMs
                            var (predictedLeft, predictedRight) = PredictEyePositions(leftEye, rightEye, predictiveMs);

                            (leftPrediction, sample_residual_l) = fitResult.PredictWithSample(predictedLeft.X,
                                predictedLeft.Y, predictedLeft.Z, sigma);
                            (rightPrediction, sample_residual_r) =
                                fitResult.PredictWithSample(predictedRight.X, predictedRight.Y, predictedRight.Z,
                                    sigma);

                            new SetLenticularParams
                            {
                                left_fill = Color.FromArgb((int)(dbg_lvl * 255), 255, 0, 0),
                                right_fill = Color.FromArgb((int)(dbg_lvl * 255), 0, 0, 255),
                                period_fill_left = period_fill,
                                period_fill_right = period_fill,
                                period_total_left = (float)leftPrediction.Period,
                                period_total_right = (float)rightPrediction.Period,
                                phase_init_left = (float)leftPrediction.Bias,
                                phase_init_right = (float)rightPrediction.Bias,
                                phase_init_row_increment_left = (float)leftPrediction.Angle,
                                phase_init_row_increment_right = (float)rightPrediction.Angle
                            }.IssueToTerminal(GUI.localTerminal);

                            // Use predicted positions for holo view as well
                            var holoLeft = predictedLeft;
                            var holoRight = predictedRight;
                            holoLeft.Y *= -1;
                            holoRight.Y *= -1;

                            var api=new SetHoloViewEyePosition
                            {
                                leftEyePos = holoLeft + new Vector3(0, 0, (float)fitResult.Calibration.Period.ZBias),
                                rightEyePos = holoRight + new Vector3(0, 0, (float)fitResult.Calibration.Period.ZBias)
                            };
                            // if (curved_screen)
                            // {
                            //     var (vals, w, h) = GetCurvedDisplayParams();
                            //     api.biasFixVals = vals;
                            //     api.biasFixWidth = w;
                            //     api.biasFixHeight = h;
                            // }
                            api.IssueToTerminal(GUI.localTerminal);
                        };
                    else
                    {
                        sh431.act = null;
                        ClearEyePredictionHistory();
                    }
                }

                if (testEnabled)
                {
                    pb.DragFloat("Predictive Ms", ref predictiveMs, 1f, 0, 500);
                    pb.DragFloat("Manual Bias scale", ref fitResult.Calibration.Bias.Scale, 0.001f, -100, 100);
                    pb.DragFloat("Manual Bias offset", ref fitResult.Calibration.Bias.Offset, 0.001f, -100, 100);
                    pb.DragFloat("Manual Bias Zoffset", ref fitResult.Calibration.Bias.ZBias, 0.001f, -100, 10000);
                    pb.DragFloat("Sample sigma", ref sigma, 0.01f, 0.0f, 1.0f);

                    pb.DragFloat("Debug level", ref dbg_lvl, 0.01f, 0, 1);


                    if (leftPrediction.obsolete)
                        pb.Label("No eyes detected");
                    else
                    {
                        pb.Label(
                            $"left(p,b,i)=\r\n{leftPrediction.Period}\r\n{leftPrediction.Bias}\r\n{leftPrediction.Angle}");
                        pb.Label(
                            $"right(p,b,i)=\r\n{rightPrediction.Period}\r\n{rightPrediction.Bias}\r\n{rightPrediction.Angle}");
                    }

                    pb.Panel.Repaint();
                }

                if (pb.Button("Reload fit"))
                    RefreshFit(false);

            }, remote);
        }

        private static LenticularParamFitter.FitResult LoadScreenParametersFromJson(string path)
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<LenticularParamFitter.FitResult>(json, ScreenParametersSerializerSettings)
                ?? throw new InvalidOperationException($"Failed to parse {path}.");
        }

        private static void SaveScreenParametersToJson(LenticularParamFitter.FitResult result, string path)
        {
            var json = JsonConvert.SerializeObject(result, ScreenParametersSerializerSettings);
            File.WriteAllText(path, json);
        }

        private static void LenticularTuner()
        {
            ConcurrentQueue<string> logs = new();
            GUI.PromptOrBringToFront(pb =>
            {
                pb.Panel.ShowTitle("Caliberation Status");
                while (logs.Count > 10)
                    logs.TryDequeue(out _);
                foreach (var log in logs)
                    pb.Label(log);

                pb.Panel.Repaint();
            }, remote);

            // Prepend the *current* robot pose as the first tuning place so the initial state is valid
            // and we don't move the arm before the first measurement.
            var places = new List<(bool isCurrent, Vector3 pos, Vector3 rot)>();
            // try
            // {
            //     places.Add((true, arm.GetPos(), arm.GetRotation()));
            // }
            // catch (Exception ex)
            // {
            //     logs.Enqueue($"failed to read current arm pose: {ex.Message}");
            // }

            if (File.Exists("tuning_places.txt"))
            {
                var lines = File.ReadAllLines("tuning_places.txt");
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(new char[] { '\t', ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 6)
                    {
                        logs.Enqueue($"skip line {i + 1}: not enough values");
                        continue;
                    }

                    if (!float.TryParse(parts[0], out float x) ||
                        !float.TryParse(parts[1], out float y) ||
                        !float.TryParse(parts[2], out float z) ||
                        !float.TryParse(parts[3], out float rx) ||
                        !float.TryParse(parts[4], out float ry) ||
                        !float.TryParse(parts[5], out float rz))
                    {
                        logs.Enqueue($"skip line {i + 1}: parse error");
                        continue;
                    }

                    places.Add((false, new Vector3(x, y, z), new Vector3(rx, ry, rz)));
                }
            }
            else
            {
                logs.Enqueue("tuning_places.txt not found; only tuning current robot pose.");
            }

            for (int placeIndex = 0; placeIndex < places.Count; placeIndex++)
            {
                var placeStartTime = DateTime.Now;
                var (isCurrent, target, rotv3) = places[placeIndex];
                float rx = rotv3.X, ry = rotv3.Y, rz = rotv3.Z;

                if (!isCurrent)
                {
                    Console.WriteLine($"goto {target}, rotate XYZ {rx:0.00},{ry:0.00},{rz:0.00}");
                    logs.Enqueue($"goto {target}, rotate {rx},{ry},{rz}");

                    arm.Goto(target, rx, ry, rz);
                    arm.WaitForTarget();
                    var apos = arm.GetPos();
                    if ((target - apos).Length() > 20)
                    {
                        Console.WriteLine("Robot not going to target position... skip");
                        logs.Enqueue("invalid position, robot doesn't actuate.");
                        continue;
                    }

                    Console.WriteLine($"actually goto {arm.GetPos()}, {arm.GetRotation()}");
                    Thread.Sleep(500);
                }
                else
                {
                    Console.WriteLine($"use current pose as first tuning place: pos={target}, rot={rotv3}");
                    logs.Enqueue($"use current pose as first place: pos={target}, rot={rotv3}");
                }

                while (sh431.framedt.AddMilliseconds(100) < DateTime.Now)
                {
                    logs.Enqueue("wait for sh431");
                    Thread.Sleep(1000);
                }

                var or=sh431.original_right;
                var ol=sh431.original_left;
                if (or.X == 0 || or.Y == 0 || or.Z == 0 || ol.X == 0 || ol.Y == 0 || ol.Z == 0)
                {
                    logs.Enqueue("Invalid eye position... revise places.");
                    continue;
                }

                // Main-region saliency masks (whole screen if TuneFineBias is off)
                int mainCols = tune_fine_bias ? fine_bias_cols : 1;
                int mainRows = tune_fine_bias ? fine_bias_rows : 1;
                var mainRect = tune_fine_bias
                    ? new BlockRect(main_rect_x0, main_rect_y0, main_rect_x1, main_rect_y1)
                    : new BlockRect(0, 0, 0, 0);
                var (mainMaskL, mainMaskR) = CaptureSaliencyMasksForRect(mainRect, mainCols, mainRows);
                UITools.ImageShowMono("saliency_L", mainMaskL, leftCamera.width, leftCamera.height, terminal: remote);
                UITools.ImageShowMono("saliency_R", mainMaskR, rightCamera.width, rightCamera.height, terminal: remote);

                new SetHoloViewEyePosition
                {
                    updateEyePos = false,
                    clearBiasFix = true
                }.IssueToTerminal(GUI.localTerminal);

                var (scoreL, scoreR, periodl, periodr, bl, br, angl, angr) = TuneOnce(coarse_iteration, true, mainMaskL, mainMaskR);

                or = sh431.original_right;
                ol = sh431.original_left;
                var lv3 = TransformPoint(cameraToActualMatrix, ol);
                var rv3 = TransformPoint(cameraToActualMatrix, or);

                if (scoreL == 0 || scoreR == 0)
                {
                    logs.Enqueue("Sanity check failed, might be bad place, revise places.");
                    continue;
                }

                if (scoreL > scoreR && scoreL > 0.5)
                {
                    prior_period = periodl;
                    prior_row_increment = angl;
                    Console.WriteLine("use left as new prior");
                }
                if (scoreR > scoreL && scoreR > 0.5)
                {
                    prior_period = periodr;
                    prior_row_increment = angr;
                    Console.WriteLine("use right as new prior");
                }

                coarse_iteration = false;
                
                // Variable to hold fine bias JSON string for tune_data.log
                string? fineBiasJsonStr = null;

                // Fine bias fix per-block (per place)
                if (tune_fine_bias)
                {
                    Console.WriteLine("Start fine tuning...");
                    int cols = fine_bias_cols;
                    int rows = fine_bias_rows;
                    int expected = cols * rows;

                    var leftBias = new float[expected];
                    var rightBias = new float[expected];

                    // Region states: 0=Invalid, 1=Pending, 2=Valid
                    const int STATE_INVALID = 0;
                    const int STATE_PENDING = 1;
                    const int STATE_VALID = 2;
                    var regionState = new int[expected]; // Initially all invalid
                    var hasSaliency = new bool[expected]; // Has enough saliency pixels

                    // Reference scores from coarse tuning
                    float refScoreL = scoreL;
                    float refScoreR = scoreR;
                    float validThreshold = 0.3f;

                    // 1) Capture saliency masks for all regions
                    // Use doubled grid (cols*2 x rows*2) for saliency, each cell's influence covers 4x4 sub-cells
                    int saliencyCols = cols * 2;
                    int saliencyRows = rows * 2;
                    
                    var masksL = new byte[expected][];
                    var masksR = new byte[expected][];

                    int CountValid(byte[] m)
                    {
                        int c = 0;
                        for (int ii = 0; ii < m.Length; ii++)
                            if (m[ii] > grating_bright) c++;
                        return c;
                    }

                    Console.WriteLine($"Capturing saliency masks with doubled grid: {saliencyCols}x{saliencyRows}");
                    Console.WriteLine($"Reference scores: L={refScoreL:F3}, R={refScoreR:F3}");
                    
                    for (int y0 = 0; y0 < rows; y0++)
                    for (int x0 = 0; x0 < cols; x0++)
                    {
                        int sx0 = Math.Max(0, 2 * x0 - 1);
                        int sy0 = Math.Max(0, 2 * y0 - 1);
                        int sx1 = Math.Min(saliencyCols - 1, 2 * x0 + 2);
                        int sy1 = Math.Min(saliencyRows - 1, 2 * y0 + 2);
                        
                        var rect = new BlockRect(sx0, sy0, sx1, sy1);
                        var (maskL, maskR) = CaptureSaliencyMasksForRect(rect, saliencyCols, saliencyRows);
                        int idx = y0 * cols + x0;
                        masksL[idx] = maskL;
                        masksR[idx] = maskR;

                        var pixels = CountValid(maskL) + CountValid(maskR);
                        hasSaliency[idx] = pixels >= 1000;
                        if (!hasSaliency[idx])
                            Console.WriteLine($"fine_bias ({x0},{y0}) saliency rect ({sx0},{sy0})-({sx1},{sy1}) too few pixels={pixels}");
                        else
                            Console.WriteLine($"fine_bias ({x0},{y0}) saliency rect ({sx0},{sy0})-({sx1},{sy1}) pixels={pixels}");
                    }

                    // Disable rect mask for optimization runs
                    ApplyBlockRect(0, saliencyCols, saliencyRows, new BlockRect(0, 0, 0, 0));

                    // For uploading to GPU
                    float[] leftUpload = leftBias.ToArray();
                    float[] rightUpload = rightBias.ToArray();

                    // upload initial guess
                    UploadBiasMatrixLR(cols, rows, leftUpload, rightUpload);

                    // Helper: get 8-connected neighbor indices
                    List<int> GetNeighbors(int idx)
                    {
                        int x = idx % cols;
                        int y = idx / cols;
                        var neighbors = new List<int>();
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx >= 0 && nx < cols && ny >= 0 && ny < rows)
                                neighbors.Add(ny * cols + nx);
                        }
                        return neighbors;
                    }

                    // Helper: check if score is valid
                    bool IsScoreValid(float score, bool isLeft)
                    {
                        if (float.IsNaN(score)) return false;
                        return score > validThreshold;
                    }

                    // Visualize saliency regions for a specific group
                    void ShowGroupSaliencyMaps(int group, List<int> groupCells)
                    {
                        var compositeSaliencyL = new byte[leftCamera.width * leftCamera.height];
                        var compositeSaliencyR = new byte[rightCamera.width * rightCamera.height];
                        int cellIndex = 0;
                        foreach (var idx in groupCells)
                        {
                            cellIndex++;
                            byte regionValue = (byte)(255f / cellIndex);
                            for (int p = 0; p < masksL[idx].Length; p++)
                            {
                                if (masksL[idx][p] > grating_bright)
                                    compositeSaliencyL[p] = regionValue;
                            }
                            for (int p = 0; p < masksR[idx].Length; p++)
                            {
                                if (masksR[idx][p] > grating_bright)
                                    compositeSaliencyR[p] = regionValue;
                            }
                        }
                        UITools.ImageShowMono("saliency_L", compositeSaliencyL, leftCamera.width, leftCamera.height, terminal: remote);
                        UITools.ImageShowMono("saliency_R", compositeSaliencyR, rightCamera.width, rightCamera.height, terminal: remote);
                        Console.WriteLine($"  Group {group} saliency: {groupCells.Count} cells displayed");
                    }

                    // Get cells belonging to a specific group (0-3) that are valid or pending
                    List<int> GetGroupCells(int group)
                    {
                        int gx = group % 2;
                        int gy = group / 2;
                        var cells = new List<int>();
                        for (int y0 = 0; y0 < rows; y0++)
                        for (int x0 = 0; x0 < cols; x0++)
                        {
                            if (x0 % 2 == gx && y0 % 2 == gy)
                            {
                                int idx = y0 * cols + x0;
                                // Include valid and pending cells (not invalid)
                                if (hasSaliency[idx] && regionState[idx] != STATE_INVALID)
                                    cells.Add(idx);
                            }
                        }
                        return cells;
                    }

                    void TuneCellGroup(bool isLeftEye, int group, List<int> groupCells)
                    {
                        if (groupCells.Count == 0) return;
                        
                        string eyeLabel = isLeftEye ? "LEFT" : "RIGHT";
                        // Console.WriteLine($"  --- {eyeLabel} Group {group} ({groupCells.Count} cells) ---");

                        float period = 0.33f * (isLeftEye ? periodl : periodr);
                        float step = period / bias_scope;

                        // 2 iterations per group
                        for (int iter = 0; iter < 2; iter++)
                        {
                            // Console.WriteLine($"    Iter {iter + 1}/2, step={step:F6}");
                            
                            var bestK = new Dictionary<int, int>();
                            var bestScore = new Dictionary<int, float>();
                            foreach (var idx in groupCells)
                            {
                                bestK[idx] = 0;
                                bestScore[idx] = float.NegativeInfinity;
                            }

                            for (int k = -bias_scope; k <= bias_scope; k++)
                            {
                                foreach (var idx in groupCells)
                                {
                                    if (isLeftEye) leftUpload[idx] = leftBias[idx] + k * step;
                                    else rightUpload[idx] = rightBias[idx] + k * step;
                                }

                                UploadBiasMatrixLR(cols, rows, leftUpload, rightUpload);

                                new SetLenticularParams
                                {
                                    left_fill = isLeftEye ? Color.Lime : Color.FromArgb(0, 0, 255, 0),
                                    right_fill = isLeftEye ? Color.FromArgb(0, 0, 255, 0) : Color.Lime,
                                    period_fill_left = period_fill,
                                    period_fill_right = period_fill,
                                    period_total_left = periodl,
                                    period_total_right = periodr,
                                    phase_init_left = bl,
                                    phase_init_right = br,
                                    phase_init_row_increment_left = angl,
                                    phase_init_row_increment_right = angr
                                }.IssueToTerminal(GUI.localTerminal);

                                Thread.Sleep(update_interval);

                                var cellScores = new List<string>();
                                foreach (var idx in groupCells)
                                {
                                    int cellX = idx % cols;
                                    int cellY = idx / cols;
                                    var (mL, sL, mR, sR) = ComputeLR(masksL[idx], masksR[idx]);
                                    var score = ComputeScore(mL, sL, mR, sR, isLeftEye ? 0 : 1);
                                    cellScores.Add($"({cellX},{cellY}):{score:F3}");
                                    if (float.IsNaN(score)) continue;
                                    if (score > bestScore[idx])
                                    {
                                        bestScore[idx] = score;
                                        bestK[idx] = k;
                                    }
                                }
                                // Console.WriteLine($"      k={k,3}: {string.Join(" ", cellScores)}");
                            }

                            Console.WriteLine($"    Best results for group {group} iter {iter + 1}:");
                            foreach (var idx in groupCells)
                            {
                                int cellX = idx % cols;
                                int cellY = idx / cols;
                                float newBias = isLeftEye 
                                    ? leftBias[idx] + bestK[idx] * step 
                                    : rightBias[idx] + bestK[idx] * step;
                                Console.WriteLine($"      Cell({cellX},{cellY}): bestK={bestK[idx]}, bestScore={bestScore[idx]:F4}, newBias={newBias:F6}");
                            }

                            foreach (var idx in groupCells)
                            {
                                if (isLeftEye) leftBias[idx] = leftBias[idx] + bestK[idx] * step;
                                else rightBias[idx] = rightBias[idx] + bestK[idx] * step;
                            }

                            foreach (var idx in groupCells)
                            {
                                leftUpload[idx] = leftBias[idx];
                                rightUpload[idx] = rightBias[idx];
                            }
                            step *= bias_factor;
                        }
                    }

                    // Propagate bias from valid neighbors to invalid cells, mark as pending
                    // Then propagate from pending/invalid-referenced to remaining invalid cells
                    int PropagateFromValidNeighbors(bool isLeftEye)
                    {
                        // State: INVALID(0) -> INVALID_REFERENCED(3) -> PENDING(1) -> VALID(2)
                        const int STATE_INVALID_REF = 3;
                        
                        int totalPropagated = 0;
                        
                        // Step 1: Propagate from VALID to INVALID -> mark as PENDING
                        for (int idx = 0; idx < expected; idx++)
                        {
                            if (!hasSaliency[idx]) continue;
                            if (regionState[idx] != STATE_INVALID) continue;

                            var neighbors = GetNeighbors(idx);
                            float sumBias = 0;
                            int validCount = 0;
                            foreach (var nidx in neighbors)
                            {
                                if (regionState[nidx] == STATE_VALID)
                                {
                                    sumBias += isLeftEye ? leftBias[nidx] : rightBias[nidx];
                                    validCount++;
                                }
                            }

                            if (validCount > 0)
                            {
                                float avgBias = sumBias / validCount;
                                if (isLeftEye)
                                {
                                    leftBias[idx] = avgBias;
                                    leftUpload[idx] = avgBias;
                                }
                                else
                                {
                                    rightBias[idx] = avgBias;
                                    rightUpload[idx] = avgBias;
                                }
                                regionState[idx] = STATE_PENDING;
                                int x = idx % cols;
                                int y = idx / cols;
                                Console.WriteLine($"    Propagated ({x},{y}): avgBias={avgBias:F6} from {validCount} VALID neighbors -> PENDING");
                                totalPropagated++;
                            }
                        }
                        
                        // Step 2: Iteratively propagate from PENDING/INVALID_REF to remaining INVALID
                        // This ensures all INVALID regions get reasonable initial values
                        int iterCount = 0;
                        const int maxPropagationIters = 20;
                        
                        while (iterCount < maxPropagationIters)
                        {
                            iterCount++;
                            int propagatedThisRound = 0;
                            
                            for (int idx = 0; idx < expected; idx++)
                            {
                                if (!hasSaliency[idx]) continue;
                                if (regionState[idx] != STATE_INVALID) continue;

                                var neighbors = GetNeighbors(idx);
                                float sumBias = 0;
                                int refCount = 0;
                                
                                // Reference from PENDING or INVALID_REF neighbors
                                foreach (var nidx in neighbors)
                                {
                                    if (regionState[nidx] == STATE_PENDING || regionState[nidx] == STATE_INVALID_REF)
                                    {
                                        sumBias += isLeftEye ? leftBias[nidx] : rightBias[nidx];
                                        refCount++;
                                    }
                                }

                                if (refCount > 0)
                                {
                                    float avgBias = sumBias / refCount;
                                    if (isLeftEye)
                                    {
                                        leftBias[idx] = avgBias;
                                        leftUpload[idx] = avgBias;
                                    }
                                    else
                                    {
                                        rightBias[idx] = avgBias;
                                        rightUpload[idx] = avgBias;
                                    }
                                    regionState[idx] = STATE_INVALID_REF;
                                    int x = idx % cols;
                                    int y = idx / cols;
                                    Console.WriteLine($"    Propagated ({x},{y}): avgBias={avgBias:F6} from {refCount} PENDING/REF neighbors -> INVALID_REF");
                                    propagatedThisRound++;
                                    totalPropagated++;
                                }
                            }
                            
                            if (propagatedThisRound == 0)
                                break; // No more regions to propagate
                        }
                        
                        // Step 3: Convert all INVALID_REF back to INVALID
                        // These regions have initial values but will not participate in tuning
                        for (int idx = 0; idx < expected; idx++)
                        {
                            if (regionState[idx] == STATE_INVALID_REF)
                            {
                                regionState[idx] = STATE_INVALID;
                            }
                        }
                        
                        Console.WriteLine($"    Total propagation iterations: {iterCount}, total regions initialized: {totalPropagated}");
                        
                        return totalPropagated;
                    }

                    // Check scores and update region states
                    void UpdateRegionStates(bool isLeftEye)
                    {
                        // First, measure all regions
                        UploadBiasMatrixLR(cols, rows, leftUpload, rightUpload);
                        
                        new SetLenticularParams
                        {
                            left_fill = isLeftEye ? Color.Lime : Color.FromArgb(0, 0, 255, 0),
                            right_fill = isLeftEye ? Color.FromArgb(0, 0, 255, 0) : Color.Lime,
                            period_fill_left = period_fill,
                            period_fill_right = period_fill,
                            period_total_left = periodl,
                            period_total_right = periodr,
                            phase_init_left = bl,
                            phase_init_right = br,
                            phase_init_row_increment_left = angl,
                            phase_init_row_increment_right = angr
                        }.IssueToTerminal(GUI.localTerminal);

                        Thread.Sleep(update_interval);

                        Console.WriteLine($"  Checking region scores (ref: {(isLeftEye ? refScoreL : refScoreR):F3}):");
                        for (int idx = 0; idx < expected; idx++)
                        {
                            if (!hasSaliency[idx]) continue;
                            
                            var (mL, sL, mR, sR) = ComputeLR(masksL[idx], masksR[idx]);
                            var score = ComputeScore(mL, sL, mR, sR, isLeftEye ? 0 : 1);
                            int x = idx % cols;
                            int y = idx / cols;
                            
                            bool isValid = IsScoreValid(score, isLeftEye);
                            if (isValid && regionState[idx] != STATE_VALID)
                            {
                                regionState[idx] = STATE_VALID;
                                Console.WriteLine($"    ({x},{y}): score={score:F3} -> VALID");
                            }
                            else if (!isValid && regionState[idx] == STATE_INVALID)
                            {
                                Console.WriteLine($"    ({x},{y}): score={score:F3} -> still INVALID");
                            }
                        }
                    }

                    void TuneAllCells(bool isLeftEye)
                    {
                        string eyeLabel = isLeftEye ? "LEFT" : "RIGHT";
                        Console.WriteLine($"=== TuneAllCells: {eyeLabel} eye (iterative propagation) ===");

                        // Reset states for this eye
                        for (int idx = 0; idx < expected; idx++)
                            regionState[idx] = STATE_INVALID;

                        int iteration = 0;
                        const int maxIterations = 10;

                        while (iteration < maxIterations)
                        {
                            iteration++;
                            Console.WriteLine($"\n--- Iteration {iteration} ---");

                            // Step 1: Check scores and mark valid regions
                            UpdateRegionStates(isLeftEye);

                            // Count states
                            int validCount = 0, pendingCount = 0, invalidCount = 0;
                            for (int idx = 0; idx < expected; idx++)
                            {
                                if (!hasSaliency[idx]) continue;
                                if (regionState[idx] == STATE_VALID) validCount++;
                                else if (regionState[idx] == STATE_PENDING) pendingCount++;
                                else invalidCount++;
                            }
                            Console.WriteLine($"  States: Valid={validCount}, Pending={pendingCount}, Invalid={invalidCount}");

                            if (invalidCount == 0)
                            {
                                Console.WriteLine($"  All regions valid! Stopping.");
                                break;
                            }

                            // Step 2: Propagate from valid neighbors to invalid cells
                            int propagated = PropagateFromValidNeighbors(isLeftEye);
                            Console.WriteLine($"  Propagated bias to {propagated} cells");

                            if (propagated == 0 && iteration > 1)
                            {
                                Console.WriteLine($"  No more cells can be propagated. Stopping.");
                                break;
                            }

                            // Step 3: Tune all groups (valid + pending cells)
                            for (int group = 0; group < 4; group++)
                            {
                                var groupCells = GetGroupCells(group);
                                if (groupCells.Count == 0)
                                {
                                    Console.WriteLine($"  Group {group}: no cells to tune");
                                    continue;
                                }
                                Console.WriteLine($"  Group {group}: {groupCells.Count} cells");
                                ShowGroupSaliencyMaps(group, groupCells);
                                TuneCellGroup(isLeftEye, group, groupCells);
                            }

                            // Step 4: Promote pending to valid
                            for (int idx = 0; idx < expected; idx++)
                            {
                                if (regionState[idx] == STATE_PENDING)
                                    regionState[idx] = STATE_VALID;
                            }
                        }
                        
                        Console.WriteLine($"=== TuneAllCells: {eyeLabel} eye DONE (iterations: {iteration}) ===");
                    }

                    // Tune all regions with iterative propagation per eye
                    TuneAllCells(true);
                    TuneAllCells(false);

                    // Final upload (invalid regions now have propagated values from neighbors)
                    UploadBiasMatrixLR(cols, rows, leftUpload, rightUpload);

                    // 4) Mark regions without saliency as NaN in saved results
                    for (int ii = 0; ii < expected; ii++)
                    {
                        if (!hasSaliency[ii])
                        {
                            leftBias[ii] = float.NaN;
                            rightBias[ii] = float.NaN;
                        }
                    }

                    // parameters show left:
                    fine_bias_coarse_vals = leftBias.ToArray();

                    // Clear rect mask (back to full screen) but keep the tuned bias texture
                    ApplyBlockRect(0, cols, rows, new BlockRect(0, 0, 0, 0));

                    // Recalculate scores after fine bias tuning (scoreLF, scoreRF)
                    // Need to capture each eye separately in non-stripe mode
                    Console.WriteLine("Recalculating scores after fine bias tuning...");
                    
                    // Show LEFT eye only (non-stripe)
                    new SetLenticularParams()
                    {
                        left_fill = Color.Lime,
                        right_fill = Color.FromArgb(0, 0, 255, 0),
                        period_fill_left = period_fill,
                        period_fill_right = period_fill,
                        period_total_left = periodl,
                        period_total_right = periodr,
                        phase_init_left = bl,
                        phase_init_right = br,
                        phase_init_row_increment_left = angl,
                        phase_init_row_increment_right = angr,
                        stripe = false // Non-stripe mode for accurate measurement
                    }.IssueToTerminal(GUI.localTerminal);
                    Thread.Sleep(update_interval);
                    var (mL_leftOn, sL_leftOn, mR_leftOn, sR_leftOn) = ComputeLR(mainMaskL, mainMaskR);
                    
                    // Show RIGHT eye only (non-stripe)
                    new SetLenticularParams()
                    {
                        left_fill = Color.FromArgb(0, 0, 255, 0),
                        right_fill = Color.Lime,
                        period_fill_left = period_fill,
                        period_fill_right = period_fill,
                        period_total_left = periodl,
                        period_total_right = periodr,
                        phase_init_left = bl,
                        phase_init_right = br,
                        phase_init_row_increment_left = angl,
                        phase_init_row_increment_right = angr,
                        stripe = false // Non-stripe mode for accurate measurement
                    }.IssueToTerminal(GUI.localTerminal);
                    Thread.Sleep(update_interval);
                    var (mL_rightOn, sL_rightOn, mR_rightOn, sR_rightOn) = ComputeLR(mainMaskL, mainMaskR);
                    
                    // Compute scores: LEFT eye score uses left-on capture, RIGHT eye score uses right-on capture
                    float scoreLF = ComputeScore(mL_leftOn, sL_leftOn, mR_leftOn, sR_leftOn, 0);
                    float scoreRF = ComputeScore(mL_rightOn, sL_rightOn, mR_rightOn, sR_rightOn, 1);
                    Console.WriteLine($"Scores after fine bias: scoreLF={scoreLF:F3}, scoreRF={scoreRF:F3}");
                    Console.WriteLine($"  Left-on capture: mL={mL_leftOn:F3}, sL={sL_leftOn:F3}, mR={mR_leftOn:F3}, sR={sR_leftOn:F3}");
                    Console.WriteLine($"  Right-on capture: mL={mL_rightOn:F3}, sL={sL_rightOn:F3}, mR={mR_rightOn:F3}, sR={sR_rightOn:F3}");
                    logs.Enqueue($"Fine bias scores: L={scoreLF:F3}, R={scoreRF:F3}");
                    
                    // Use fine bias scores for decision
                    scoreL = scoreLF;
                    scoreR = scoreRF;
                    
                    // Store fine bias values as JSON string for tune_data.log
                    var fineBiasJson = JsonConvert.SerializeObject(new { cols, rows, leftBias, rightBias }, 
                        new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.Symbol, Formatting = Formatting.None });
                    fineBiasJsonStr = fineBiasJson;
                }
                else
                {
                    // Ensure mask mode is off after main saliency capture (the 1x1 rect is whole screen, but disable anyway)
                    ApplyBlockRect(0, 1, 1, new BlockRect(0, 0, 0, 0));
                    fineBiasJsonStr = null;
                }

                new SetLenticularParams()
                {
                    left_fill = Color.Red,
                    right_fill = Color.Blue,
                    period_fill_left = period_fill,
                    period_fill_right = period_fill,
                    period_total_left = periodl,
                    period_total_right = periodr,
                    phase_init_left = bl,
                    phase_init_right = br,
                    phase_init_row_increment_left = angl,
                    phase_init_row_increment_right = angr,

                    stripe = true
                }.IssueToTerminal(GUI.localTerminal);
                Thread.Sleep(update_interval);

                // Calculate elapsed time for this place
                var placeElapsedTime = DateTime.Now - placeStartTime;
                var timeStr = $"{placeElapsedTime.TotalSeconds:F1}s";
                Console.WriteLine($"Place {placeIndex} calibration time: {timeStr}");
                logs.Enqueue($"Place {placeIndex} time: {timeStr}, scores: L={scoreL:0.00}/R={scoreR:0.00}");

                // Write to tune_data.log with fine bias included
                var logLinesBasic = new[]
                {
                    $"L\t{lv3.X}\t{lv3.Y}\t{lv3.Z}\t{periodl}\t{bl}\t{angl}\t{scoreL:0.00}\t{target.X}\t{target.Y}\t{target.Z}\t{rx}\t{ry}\t{rz}",
                    $"R\t{rv3.X}\t{rv3.Y}\t{rv3.Z}\t{periodr}\t{br}\t{angr}\t{scoreR:0.00}\t{target.X}\t{target.Y}\t{target.Z}\t{rx}\t{ry}\t{rz}"
                };
                var logLines = fineBiasJsonStr != null 
                    ? logLinesBasic.Concat(new[] { $"#FINE_BIAS\t{fineBiasJsonStr}" }).ToArray()
                    : logLinesBasic;
                File.AppendAllLines("tune_data.log", logLines);
                
                Directory.CreateDirectory("results");

                // Create merged LR image
                int mergedWidth = leftCamera.width + rightCamera.width;
                int mergedHeight = Math.Max(leftCamera.height, rightCamera.height);
                byte[] mergedData = new byte[mergedWidth * mergedHeight * 4];
                
                // Copy left image
                for (int m = 0; m < leftCamera.height; m++)
                {
                    for (int n = 0; n < leftCamera.width; n++)
                    {
                        int srcIdx = (m * leftCamera.width + n) * 4;
                        int dstIdx = (m * mergedWidth + n) * 4;
                        mergedData[dstIdx] = leftCamera.preparedData[srcIdx];
                        mergedData[dstIdx + 1] = leftCamera.preparedData[srcIdx + 1];
                        mergedData[dstIdx + 2] = leftCamera.preparedData[srcIdx + 2];
                        mergedData[dstIdx + 3] = leftCamera.preparedData[srcIdx + 3];
                    }
                }
                
                // Copy right image
                for (int m = 0; m < rightCamera.height; m++)
                {
                    for (int n = 0; n < rightCamera.width; n++)
                    {
                        int srcIdx = (m * rightCamera.width + n) * 4;
                        int dstIdx = (m * mergedWidth + (leftCamera.width + n)) * 4;
                        mergedData[dstIdx] = rightCamera.preparedData[srcIdx];
                        mergedData[dstIdx + 1] = rightCamera.preparedData[srcIdx + 1];
                        mergedData[dstIdx + 2] = rightCamera.preparedData[srcIdx + 2];
                        mergedData[dstIdx + 3] = rightCamera.preparedData[srcIdx + 3];
                    }
                }
                
                File.WriteAllBytes($"results\\{placeIndex}LR.jpg", ImageCodec.SaveJpegToBytes(
                    new SoftwareBitmap(mergedWidth, mergedHeight, mergedData), 60));

                // Determine if we should prompt for check based on check_result and score threshold
                bool shouldCheck = check_result;
                if (check_result && check_low_score)
                {
                    // Only check if score is below threshold
                    shouldCheck = scoreL < check_score_threshold || scoreR < check_score_threshold;
                    if (shouldCheck)
                        Console.WriteLine($"Low score detected (L={scoreL:F2}, R={scoreR:F2} < {check_score_threshold:F2}), prompting check...");
                }

                if (shouldCheck)
                {
                    mainpb.Repaint();
                    if (!GUI.WaitPanelResult<bool>(pb2 =>
                        {
                            pb2.Panel.TopMost(true).InitSize(320, 0).AutoSize(true).ShowTitle("Alert");
                            pb2.Label($"{placeIndex}-th done, time: {timeStr}");
                            pb2.Label($"Scores: L={scoreL:0.00}, R={scoreR:0.00}");
                            if (scoreL < check_score_threshold || scoreR < check_score_threshold)
                                pb2.Label($"WARNING: Score below threshold ({check_score_threshold:F2})!");
                            if (pb2.Button("Continue"))
                                pb2.Exit(true);
                            if (pb2.Button("Exit and check"))
                            {
                                pb2.Exit(false);
                            }
                        }, remote))
                        return;
                }
                new SetLenticularParams()
                {
                    stripe = false
                }.IssueToTerminal(GUI.localTerminal);
            }


            // var fitResult = LenticularParamFitter.FitFromFile("tune_data.log", periodZSearchStart, periodZSearchEnd, Console.WriteLine);
            // File.WriteAllText(ScreenParametersFileName, JsonConvert.SerializeObject(fitResult, ScreenParametersSerializerSettings));

            arm.GotoDefault();
            Console.WriteLine("Done");
        }



        private static float[] coarse_period_step = [0.0025f, 0.00015f];
        private static int[] coarse_base_period_searchs = [40, 12];

        private static float bias_factor = 0.08f;
        private static int bias_scope = 8;

        private static int update_interval = 220;

        private static float grating_bright = 80;
        private static float retries_limit = 4;

        private readonly record struct BlockRect(int X0, int Y0, int X1, int Y1)
        {
            public int XMin => Math.Min(X0, X1);
            public int XMax => Math.Max(X0, X1);
            public int YMin => Math.Min(Y0, Y1);
            public int YMax => Math.Max(Y0, Y1);
        }

        private static void ApplyBlockRect(int mode, int cols, int rows, BlockRect rect)
        {
            new SetLenticularParams
            {
                block_mode = mode,
                block_cols = cols,
                block_rows = rows,
                block_x0 = rect.X0,
                block_y0 = rect.Y0,
                block_x1 = rect.X1,
                block_y1 = rect.Y1
            }.IssueToTerminal(GUI.localTerminal);
        }

        private static (byte[] maskL, byte[] maskR) CaptureSaliencyMasksForRect(BlockRect rect, int cols, int rows)
        {
            // Limit rendering to this rect and paint strong red for saliency detection.
            ApplyBlockRect(1, cols, rows, rect);

            var left_all_red = new byte[leftCamera.width * leftCamera.height];
            var right_all_red = new byte[rightCamera.width * rightCamera.height];

            // Get Screen Saliency for this rect only (outside is black by shader mask).
            new SetLenticularParams()
            {
                left_fill = Color.FromArgb((int)(fill_rate * 255), 255, 0, 0),
                right_fill = Color.FromArgb((int)(fill_rate * 255), 255, 0, 0),
                period_fill_left = 10,
                period_fill_right = 10,
                period_total_left = 10,
                period_total_right = 10,
                phase_init_left = 0,
                phase_init_right = 0,
                phase_init_row_increment_left = 0.1f,
                phase_init_row_increment_right = 0.1f
            }.IssueToTerminal(GUI.localTerminal);

            Thread.Sleep(update_interval * 2);

            for (int i = 0; i < leftCamera.height; ++i)
            for (int j = 0; j < leftCamera.width; ++j)
            {
                var st = (i * leftCamera.width + j) * 4;
                var r = leftCamera.preparedData[st];
                var g = leftCamera.preparedData[st + 1];
                var b = leftCamera.preparedData[st + 2];
                if (r > grating_bright && r > b + grating_bright * 0.3 && r > g + grating_bright * 0.3)
                    left_all_red[i * leftCamera.width + j] = r;
            }

            for (int i = 0; i < rightCamera.height; ++i)
            for (int j = 0; j < rightCamera.width; ++j)
            {
                var st = (i * rightCamera.width + j) * 4;
                var r = rightCamera.preparedData[st];
                var g = rightCamera.preparedData[st + 1];
                var b = rightCamera.preparedData[st + 2];
                if (r > grating_bright && r > b + grating_bright * 0.3 && r > g + grating_bright * 0.3)
                    right_all_red[i * rightCamera.width + j] = r;
            }

            return (left_all_red, right_all_red);
        }

        private static void UploadBiasMatrixLR(int cols, int rows, float[] leftBias, float[] rightBias)
        {
            var pix = cols * rows;
            var interleaved = new float[pix * 2];
            for (int i = 0; i < pix; i++)
            {
                interleaved[i * 2 + 0] = leftBias[i];
                interleaved[i * 2 + 1] = rightBias[i];
            }
            new SetHoloViewEyePosition
            {
                updateEyePos = false,
                biasFixVals = interleaved,
                biasFixWidth = cols,
                biasFixHeight = rows
            }.IssueToTerminal(GUI.localTerminal);
        }

        private static (float meanL, float stdL, float meanR, float stdR) ComputeLR(byte[] maskL, byte[] maskR)
        {
            float sum2L = 0, sumL = 0;
            float sumSqL = 0;
            int validCountL = 0;

            for (int i = 0; i < leftCamera.height; ++i)
            for (int j = 0; j < leftCamera.width; ++j)
            {
                if (maskL[i * leftCamera.width + j] > grating_bright)
                {
                    var idx = (i * leftCamera.width + j) * 4;
                    var r = Math.Min(1, leftCamera.preparedData[idx..(idx + 3)].Max() / 255f / passing_brightness);
                    sumL += r;
                    sum2L += MathF.Pow(r, 2f);
                    sumSqL += r * r;
                    validCountL++;
                }
            }

            float mean2L = validCountL > 0 ? sum2L / validCountL : 0;
            float meanL = validCountL > 0 ? sumL / validCountL : 0;
            float stdL = validCountL > 0 ? MathF.Sqrt(sumSqL / validCountL - meanL * meanL) : 0;

            float sum2R = 0, sumR = 0;
            float sumSqR = 0;
            int validCountR = 0;

            for (int i = 0; i < rightCamera.height; ++i)
            for (int j = 0; j < rightCamera.width; ++j)
            {
                if (maskR[i * rightCamera.width + j] > grating_bright)
                {
                    var idx = (i * rightCamera.width + j) * 4;
                    var r = Math.Min(1, rightCamera.preparedData[idx..(idx + 3)].Max() / 255f / passing_brightness);
                    sumR += r;
                    sum2R += MathF.Pow(r, 2f);
                    sumSqR += r * r;
                    validCountR++;
                }
            }

            float mean2R = validCountR > 0 ? sum2R / validCountR : 0;
            float meanR = validCountR > 0 ? sumR / validCountR : 0;
            float stdR = validCountR > 0 ? MathF.Sqrt(sumSqR / validCountR - meanR * meanR) : 0;

            return (mean2L, stdL, mean2R, stdR);
        }

        private static float ComputeScore(float meanL, float stdL, float meanR, float stdR, int lr)
        {
            float myMean = lr == 0 ? meanL : meanR;
            float otherMean = lr == 0 ? meanR : meanL;
            float myStd = lr == 0 ? stdL : stdR;
            var score = (myMean - otherMean + Math.Min(10, myMean / (otherMean + 0.0001f)) * 0.01f) *
                        MathF.Pow(myMean, 2) / Math.Max(myStd, 0.1f);
            return score;
        }

        private static (float scoreL, float scoreR,
            float periodL, float peroidR,
            float leftbias, float rightbias, 
            float degL, float degR) TuneOnce(bool coarse_tune, bool tune_ang, byte[] left_all_red, byte[] right_all_red)
        {
            var tic = DateTime.Now;

            var retries = 0;
            beginning:
            retries += 1;
            if (retries > retries_limit)
            {
                Console.WriteLine("FUCKED UP");
                return (0, 0, 0, 0, 0, 0, 0, 0);
            }

            // Saliency masks are provided by caller (typically from a selected block-rect).

            // ========Tune row increment. ============
            var st_bri = prior_row_increment;
            if (coarse_tune){
                var st_bris = base_row_increment_search;
                var iterations = 5;
                for (int z = 0; z < iterations; ++z)
                {
                    var max_illum = 0f;
                    var max_illum_ri = 0f;
                    for (int k = -5; k < 5; ++k)
                    {
                        var ri = st_bri + k * st_bris;
                        new SetLenticularParams()
                        {
                            left_fill = Color.Red,
                            right_fill = Color.FromArgb(0, 255, 0, 0),
                            period_fill_left = 1,
                            period_total_left = 100,
                            phase_init_left = 0,
                            phase_init_right = 0,
                            phase_init_row_increment_left = ri
                        }.IssueToTerminal(GUI.localTerminal);

                        Thread.Sleep(update_interval); // wait for grating param to apply

                        int H = leftCamera.height, W = leftCamera.width;
                        var values = new byte[W * H];

                        for (int i = 0; i < leftCamera.height; ++i)
                        for (int j = 0; j < leftCamera.width; ++j)
                        {
                            if (left_all_red[i * leftCamera.width + j] > grating_bright)
                                values[i * leftCamera.width + j] =
                                    leftCamera.preparedData[(i * leftCamera.width + j) * 4];
                        }

                        // Efficient median-based rescaling
                        int[] histogram = new int[256];
                        int totalPixels = 0;
                        byte maxValue = 0;

                        // Build histogram and find max in single pass 
                        for (int i = 0; i < values.Length; i++)
                        {
                            byte val = values[i];
                            histogram[val]++;
                            if (val > 0) totalPixels++;
                            if (val > maxValue) maxValue = val;
                        }

                        // Find median using histogram
                        int medianCount = totalPixels / 2;
                        int sum1 = 0;
                        byte median = 0;
                        for (int i = 0; i < 256; i++)
                        {
                            sum1 += histogram[i];
                            if (sum1 >= medianCount)
                            {
                                median = (byte)i;
                                break;
                            }
                        }

                        // Rescale: discard values under median, rescale [median, max] to [0, 255]
                        if (maxValue > median)
                        {
                            float scale = 255.0f / (maxValue - median);
                            for (int i = 0; i < values.Length; i++)
                            {
                                if (values[i] < median)
                                    values[i] = 0;
                                else
                                    values[i] = (byte)Math.Min(255, (values[i] - median) * scale);
                                if (values[i] < 150) values[i] = 0;
                            }
                        }

                        // UITools.ImageShowMono("values", values, leftCamera.width, leftCamera.height, terminal: remote);

                        var amplitudes = ComputeAmplitudeFloat(values, W, H);
                        amplitudes[0] = 0;

                        // Perform a 5x5 window mean blur on amplitudes
                        float[] blurred = new float[amplitudes.Length];
                        int kernel = 2;
                        for (int y = 0; y < H; ++y)
                        for (int x = 0; x < W; ++x)
                        {
                            float sum2 = 0;
                            int count = 0;
                            for (int dy = -kernel; dy <= kernel; ++dy)
                            for (int dx = -kernel; dx <= kernel; ++dx)
                            {
                                int ny = y + dy, nx = x + dx;
                                if (ny >= 0 && ny < H && nx >= 0 && nx < W)
                                {
                                    sum2 += amplitudes[ny * W + nx];
                                    count++;
                                }
                            }

                            blurred[y * W + x] = sum2 / count;
                        }

                        Array.Copy(blurred, amplitudes, amplitudes.Length);

                        // Perform FFT shift to center the DC component
                        float[] shifted = new float[amplitudes.Length];
                        int halfW = W / 2;
                        int halfH = H / 2;
                        for (int y = 0; y < H; ++y)
                        {
                            for (int x = 0; x < W; ++x)
                            {
                                int newX = (x + halfW) % W;
                                int newY = (y + halfH) % H;
                                shifted[newY * W + newX] = amplitudes[y * W + x];
                            }
                        }
                        Array.Copy(shifted, amplitudes, amplitudes.Length);

                        // Center coordinates
                        float centerX = W / 2.0f;
                        float centerY = H / 2.0f;

                        // Create theta bins from 0 to 180 degrees with 0.5 degree spacing
                        float thetaStep = 0.5f;
                        int numBins = (int)(180f / thetaStep) + 1; // 361 bins
                        float[] polar_amp_sum = new float[numBins];

                        // Maximum radius to consider (half the smaller dimension)
                        float maxRadius = Math.Min(halfW, halfH);

                        // For each amplitude, compute theta with respect to center
                        for (int y = 0; y < H; ++y)
                        {
                            for (int x = 0; x < W; ++x)
                            {
                                // Compute position relative to center
                                float dx = x - centerX;
                                float dy = y - centerY;
                                
                                // Skip origin
                                if (dx == 0 && dy == 0) continue;

                                // Check if within radius
                                float radius = MathF.Sqrt(dx * dx + dy * dy);
                                if (radius > maxRadius) continue;

                                // Compute angle in degrees (0 to 180)
                                // Use absolute value of dx to map full circle to 0-180 range
                                float thetaDeg = MathF.Atan2(MathF.Abs(dy), MathF.Abs(dx)) * 180f / MathF.PI;

                                // Find the bin index (continuous)
                                float binIndex = thetaDeg / thetaStep;
                                int bin1 = (int)MathF.Floor(binIndex);
                                int bin2 = (int)MathF.Ceiling(binIndex);

                                // Clamp to valid range
                                bin1 = Math.Max(0, Math.Min(bin1, numBins - 1));
                                bin2 = Math.Max(0, Math.Min(bin2, numBins - 1));

                                float ampValue = amplitudes[y * W + x];

                                // Interpolate to nearest 2 bins
                                if (bin1 == bin2)
                                {
                                    polar_amp_sum[bin1] += ampValue;
                                }
                                else
                                {
                                    float weight2 = binIndex - bin1;
                                    float weight1 = 1f - weight2;
                                    polar_amp_sum[bin1] += ampValue * weight1;
                                    polar_amp_sum[bin2] += ampValue * weight2;
                                }
                            }
                        }

                        // Compute statistics: mean, std, max of polar_amp_sum
                        // Compute statistics for polar_amp_sum
                        float sum = 0;
                        float maxVal = float.MinValue;
                        for (int idx = 0; idx < polar_amp_sum.Length; ++idx)
                        {
                            sum += polar_amp_sum[idx];
                            if (polar_amp_sum[idx] > maxVal) maxVal = polar_amp_sum[idx];
                        }

                        float mean = sum / polar_amp_sum.Length;

                        float sumSq = 0;
                        for (int idx = 0; idx < polar_amp_sum.Length; ++idx)
                        {
                            float diff = polar_amp_sum[idx] - mean;
                            sumSq += diff * diff;
                        }

                        float std = (float)Math.Sqrt(sumSq / polar_amp_sum.Length);

                        var illum = (maxVal - mean) / std;


                        // ALSO do this for original amplitudes array -- illum_amp
                        float sum_amp = 0;
                        float maxVal_amp = float.MinValue;
                        int amp_len = amplitudes.Length;

                        for (int idx = 0; idx < amp_len; ++idx)
                        {
                            sum_amp += amplitudes[idx];
                            if (amplitudes[idx] > maxVal_amp) maxVal_amp = amplitudes[idx];
                        }

                        float mean_amp = sum_amp / amp_len;

                        float sumSq_amp = 0;
                        for (int idx = 0; idx < amp_len; ++idx)
                        {
                            float diff = amplitudes[idx] - mean_amp;
                            sumSq_amp += diff * diff;
                        }

                        float std_amp = (float)Math.Sqrt(sumSq_amp / amp_len);
                        var illum_amp = (maxVal_amp - mean_amp) / std_amp;

                        illum *= illum_amp;

                        if (max_illum < illum)
                        {
                            max_illum = illum;
                            max_illum_ri = ri;
                        }

                        Console.WriteLine($"k={k}, ri={ri}, illum={illum}");
                    }

                    Console.WriteLine($"sel {max_illum_ri}, illum={max_illum}");
                    st_bris *= 0.5f;
                    st_bri = max_illum_ri;
                }

                Console.WriteLine($"best row_increment={st_bri}");
                // Console.WriteLine($"---");
            }


            // ============= coarse tune period =================
            var st_bias_left = prior_bias_left;
            var st_bias_right = prior_bias_right;
            var st_period = prior_period;

            int pH = leftCamera.height, pW = leftCamera.width;
            var pvalues = new byte[pW * pH];
            (float std, float ss) calcPeroidScore(float pi)
            {
                new SetLenticularParams()
                {
                    left_fill = Color.Lime,
                    right_fill = Color.Transparent,
                    period_fill_left = 1,
                    period_total_left = pi,
                    phase_init_left = st_bias_left,
                    phase_init_right = 0,
                    phase_init_row_increment_left = st_bri
                }.IssueToTerminal(GUI.localTerminal);

                Thread.Sleep(update_interval);

                // Compute std for valid pixels in single pass
                float sum = 0;
                float sumSq = 0;
                int validCount = 0;

                for (int i = 0; i < leftCamera.height; ++i)
                    for (int j = 0; j < leftCamera.width; ++j)
                    {
                        if (left_all_red[i * leftCamera.width + j] > grating_bright)
                        {
                            var r = leftCamera.preparedData[(i * leftCamera.width + j) * 4] / 256f;
                            sum += r;
                            sumSq += r * r;
                            validCount++;
                            pvalues[i * leftCamera.width + j] = (byte)Math.Min(255,
                                (float)leftCamera.preparedData[(i * leftCamera.width + j) * 4] /
                                left_all_red[i * leftCamera.width + j] * 256f);
                        }
                    }

                // Downscale values by 0.25x
                using var srcMat = Mat.FromPixelData(pH, pW, MatType.CV_8UC1, pvalues);
                int newW = pW / 4, newH = pH / 4;
                using var dstMat = new Mat();
                Cv2.Resize(srcMat, dstMat, new OpenCvSharp.Size(newW, newH), 0, 0, InterpolationFlags.Area);
                byte[] downscaledValues = new byte[newW * newH];
                dstMat.GetArray(out downscaledValues);

                var amplitudes = ComputeAmplitudeFloat(downscaledValues, newW, newH);

                var ss = 0d;
                var ss2 = 0d;
                for (int y = 0; y < newH / 2; ++y)
                for (int x = 0; x < newW; ++x)
                {
                    // d1, d2, d3, d4 for xy to 4 corners distance
                    double d1 = Math.Sqrt(x * x + y * y); // Top-left (0,0)
                    double d2 = Math.Sqrt((x - (newW - 1)) * (x - (newW - 1)) + y * y); // Top-right (newW-1,0)
                    double d3 = Math.Sqrt(x * x + (y - (newH - 1)) * (y - (newH - 1))); // Bottom-left (0,newH-1)
                    double d4 = Math.Sqrt((x - (newW - 1)) * (x - (newW - 1)) + (y - (newH - 1)) * (y - (newH - 1))); // Bottom-right (newW-1,newH-1)
                    // get min d
                    double min_d = Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
                    // Use a sigmoid-like factor for border exclusion: <5 almost 0, >15 almost 1
                    // Sigmoid function: sigmf(x, [a, c]) ~= 1/(1 + exp(-a*(x-c)))
                    double sig_factor = 1.0 / (1.0 + Math.Exp(-2 * (min_d - 3))); // ~0 at min_d=5, ~1 at min_d=15
                    var idx = y * newW + x;
                    ss += amplitudes[idx] * sig_factor * min_d;
                    ss2 += amplitudes[idx] * (1 - sig_factor);
                }

                float mean = validCount > 0 ? sum / validCount : 0;
                float std = validCount > 0 ? MathF.Sqrt(sumSq / validCount - mean * mean) : 0;

                float ssv = (float)(ss / ss2) * mean;

                // todo: revise score, when looking angle is super big.

                Console.WriteLine($"{pi}, std={std}, ss={ssv}");
                return (std, ssv);
            }

            // auto.
            if (coarse_tune)
            {
                var minscore = 99999999f;
                for (int z = 0; z < coarse_period_step.Length; ++z)
                {
                    minscore = 99999999f;
                    var minscore_pi = 0f;

                    for (int k = 0; k < coarse_base_period_searchs[z]; ++k)
                    {
                        var pi = st_period + (k - coarse_base_period_searchs[z] / 2) * coarse_period_step[z];

                        var (std, ss) = calcPeroidScore(pi);
                        var score = std * ss;
                        if (minscore > score)
                        {
                            minscore = score;
                            minscore_pi = pi;
                        }
                    }

                    st_period = minscore_pi;
                    Console.WriteLine($"=>curmin={minscore}@{minscore_pi}");
                }

                Console.WriteLine($"Fin1 period={st_period}");
            }


            
            // judgement function
            (float meanL, float stdL, float meanR, float stdR) computeLR(byte[] maskL, byte[] maskR)
            {
                // Compute std for valid pixels in single pass
                float sum2L = 0, sumL = 0;
                float sumSqL = 0;
                int validCountL = 0;

                for (int i = 0; i < leftCamera.height; ++i)
                for (int j = 0; j < leftCamera.width; ++j)
                {
                    if (maskL[i * leftCamera.width + j] > grating_bright)
                    {
                        var idx= (i * leftCamera.width + j) *4;
                        var r = Math.Min(1,leftCamera.preparedData[idx..(idx + 3)].Max() / 255f /passing_brightness);
                        sumL += r;
                        sum2L += MathF.Pow(r, 2f);
                        sumSqL += r * r;
                        validCountL++;
                    }
                }

                float mean2L = validCountL > 0 ? sum2L / validCountL : 0;
                float meanL = validCountL > 0 ? sumL / validCountL : 0;
                float stdL = validCountL > 0 ? MathF.Sqrt(sumSqL / validCountL - meanL * meanL) : 0;


                float sum2R = 0, sumR = 0;
                float sumSqR = 0;
                int validCountR = 0;

                for (int i = 0; i < rightCamera.height; ++i)
                for (int j = 0; j < rightCamera.width; ++j)
                {
                    if (maskR[i * rightCamera.width + j] > grating_bright)
                    {
                        var idx = (i * rightCamera.width + j) * 4;
                        var r = Math.Min(1, rightCamera.preparedData[idx..(idx + 3)].Max() / 255f / passing_brightness);
                        sumR += r;
                        sum2R += MathF.Pow(r, 2f);
                        sumSqR += r * r;
                        validCountR++;
                    }
                }               

                float mean2R = validCountR > 0 ? sum2R / validCountR : 0;
                float meanR = validCountR > 0 ? sumR / validCountR : 0;
                float stdR = validCountR > 0 ? MathF.Sqrt(sumSqR / validCountR - meanR * meanR) : 0;
                return (mean2L, stdL, mean2R, stdR);
            }

            float computeScore(float meanL, float stdL, float meanR, float stdR, int lr) //lr=0, left, lr=1, right.
            {
                float myMean=lr==0?meanL:meanR;
                float otherMean = lr == 0 ? meanR : meanL;
                float myStd = lr == 0 ? stdL : stdR;
                float otherStd = lr == 0 ? stdR : stdL;
                var score = (myMean - otherMean + Math.Min(10, myMean / (otherMean + 0.0001)) * 0.01) *
                    MathF.Pow(myMean, 2) / Math.Max(myStd, 0.1);
                // Console.WriteLine($"mean(my={myMean},ot={otherMean}), std(my={myStd},ot={otherStd}) => {score}");
                return (float)score;
            }
            
            var scoreLeft = 0f;
            var scoreRight = 0f;
            var period_left = 0f;
            var period_right = 0f;
            var ang_left = 0f;
            var ang_right = 0f;

            {
                // coarse left.
                {
                    float[] weights = [0.1f, 0.2f, 0.4f, 0.2f, 0.1f];

                    // left bias
                    var st_step = st_period / bias_scope;
                    for (int z = 0; z < 2; ++z)
                    {
                        var maxScore = 0f;
                        var maxScoreIl = 0f;
                        // First, compute all scores and cache them
                        for (int k = -bias_scope; k <= bias_scope; ++k)
                        {
                            var il = st_bias_left + k * st_step;
                                
                            new SetLenticularParams()
                            {
                                left_fill = Color.Lime,
                                right_fill = Color.FromArgb(0, 0, 255, 0),
                                period_fill_left = period_fill,
                                period_total_left = st_period,
                                phase_init_left = il,
                                phase_init_right = 0,
                                phase_init_row_increment_left = st_bri
                            }.IssueToTerminal(GUI.localTerminal);

                            Thread.Sleep(update_interval);

                            var (meanL, stdL, meanR, stdR) = computeLR(left_all_red, right_all_red);

                            var score = computeScore(meanL, stdL, meanR, stdR, 0);

                            if (maxScore < score)
                            {
                                maxScore = (float)score;
                                maxScoreIl = il;
                            }

                            Console.WriteLine(
                                $"L> {il}, score={score:F4} mean/std=({meanL:f3}/{stdL:f3}),({meanR:f3}/{stdR:f3})");
                        }

                        Console.WriteLine($"..Select il={maxScoreIl}, score={scoreLeft = maxScore}");

                        st_bias_left = maxScoreIl;
                        st_step *= bias_factor;
                    }

                    Console.WriteLine($"initial left={st_bias_left}, score={scoreLeft}");
                    if (scoreLeft < 0)
                    {
                        Console.WriteLine("Sanity check failed, retry");
                        goto beginning;
                    }
                }

                // anneal tuning left
                void FineTuneEye(ref float st_bias, ref float scorem, bool left)
                {
                    float period_step = 0.0001f;
                    float bias_step = 0.001f;
                    float bi_step = 0.00002f;
                    var rnd = new Random();

                    Vector3 GenRnd() => new(rnd.NextSingle() * 2 - 1, rnd.NextSingle() * 2 - 1,
                        coarse_tune || tune_ang ? rnd.NextSingle() * 2 - 1 : 0);

                    Vector3 step_v3 = GenRnd();
                    step_v3 /= step_v3.Length();
                    float mag = 1;
                    var streak = 0;
                    for (int i = 0; i < 100; ++i)
                    {
                        var test_period = (float)(st_period + period_step * step_v3.X * mag);
                        var test_bias = (float)(st_bias + bias_step * step_v3.Y * mag);
                        var test_bi = (float)(st_bri + bi_step * step_v3.Z * mag);
                        new SetLenticularParams()
                        {
                            left_fill = Color.FromArgb(left ? 255 : 0, 255, 255, 255),
                            right_fill = Color.FromArgb(left ? 0 : 255, 255, 255, 255),
                            period_fill_left = period_fill,
                            period_fill_right = period_fill,
                            period_total_left = test_period,
                            period_total_right = test_period,
                            phase_init_left = test_bias,
                            phase_init_right = test_bias,
                            phase_init_row_increment_left  = test_bi,
                            phase_init_row_increment_right = test_bi
                        }.IssueToTerminal(GUI.localTerminal);

                        Thread.Sleep(update_interval); // wait for grating param to apply, also consider auto-exposure.

                        var (meanL, stdL, meanR, stdR) = computeLR(left_all_red, right_all_red);
                        var score = computeScore(meanL, stdL, meanR, stdR, left?0:1);
                        if (float.IsNaN(score))
                        {
                            Console.WriteLine("Score is NaN?");
                            score = 0;
                        }

                        if (score + Math.Min(0.01, score * 0.01) < scorem)
                        {
                            step_v3 = GenRnd();
                            Console.WriteLine(
                                $"{i}:neg {scorem}/{score}: [{st_period}, {st_bias}, {st_bri}], mag={mag}");
                            if (mag > 1) mag = 1;
                            mag *= 0.9f;
                            if (mag < 0.1) mag = 0.1f;
                            streak += 1;
                        }
                        else
                        {
                            st_period = test_period;
                            st_bias = test_bias;
                            st_bri = test_bi;
                            scorem = Math.Max(score, scorem);
                            Console.WriteLine(
                                $"{i}:good {scorem}->{score}: [{st_period}, {st_bias}, {st_bri}], mag={mag}");
                            streak = 0;
                            mag *= 1.05f;
                        }

                        if (streak > (coarse_tune || tune_ang ? 12 : 6) && scorem > 0.3) break;
                        if (stop_now) throw new Exception("Stopped");
                    }
                }
                FineTuneEye(ref st_bias_left, ref scoreLeft, true);
                period_left = st_period;
                ang_left = st_bri;

                // right bias.
                {
                    var st_step = st_period / bias_scope;
                    for (int z = 0; z < 3; ++z)
                    {
                        var maxScore = 0f;
                        var maxScoreIr = 0f;
                        for (int k = 0; k < bias_scope; ++k)
                        {
                            var ir = st_bias_right + k * st_step;
                            new SetLenticularParams()
                            {
                                left_fill = Color.FromArgb(0, 0, 255, 0),
                                right_fill = Color.Lime,
                                period_fill_right = period_fill,
                                period_total_right = st_period,
                                phase_init_left = st_bias_left,
                                phase_init_right = ir,
                                phase_init_row_increment_right = st_bri
                            }.IssueToTerminal(GUI.localTerminal);

                            Thread.Sleep(update_interval); // wait for grating param to apply, also consider auto-exposure.

                            var (meanL, stdL, meanR, stdR) = computeLR(left_all_red, right_all_red);

                            var score = computeScore(meanL, stdL, meanR, stdR, 1);

                            if (maxScore < score)
                            {
                                maxScore = (float)score;
                                maxScoreIr = ir;
                            }

                            Console.WriteLine($"R> {ir}, score={score:F4} mean/std=({meanL:f3}/{stdL:f3}),({meanR:f3}/{stdR:f3})");
                        }

                        st_bias_right = maxScoreIr;
                        st_step *= bias_factor;
                        Console.WriteLine($"*iterate on {st_bias_right}@{maxScore}");
                        scoreRight = maxScore;
                    }

                    Console.WriteLine($"fin right={st_bias_right}");
                }

                FineTuneEye(ref st_bias_right, ref scoreRight, false);
                period_right = st_period;
                ang_right = st_bri;

                Console.WriteLine($"left({scoreLeft})=[{period_left}, {st_bias_left}, {ang_left}]");
                Console.WriteLine($"right({scoreRight})=[{period_right},{st_bias_right},{ang_right}]");
            }

            Console.WriteLine($"tuning time = {(DateTime.Now - tic).TotalSeconds:0.0}s");

            return (scoreLeft, scoreRight, 
                period_left, period_right, 
                st_bias_left, st_bias_right, 
                ang_left, ang_right);
        }
    }
}
