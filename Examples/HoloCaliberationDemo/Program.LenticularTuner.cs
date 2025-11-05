using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using CycleGUI.API;
using CycleGUI;
using System.Numerics;
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

        private static void LenticularTunerUI(PanelBuilder pb)
        {
            var v3 = arm.GetPos();
            var vr = arm.GetRotation();
            pb.Label($"robot={v3} R{vr}");
            if (pb.Button("Save Tuning Place"))
            {
                File.AppendAllLines("tuning_places.txt", [$"{v3.X} {v3.Y} {v3.Z} {vr.X} {vr.Y} {vr.Z}"]);
            }
            if (running == null && pb.Button("Tune Lenticular Parameters"))
            {
                new Thread(LenticularTuner).Start();
            }

            if (pb.Button("Fit Tuned Parameters"))
            {
                ShowFitTunedParametersPanel();
            }
        }

        private static void ShowFitTunedParametersPanel()
        {
            LenticularParamFitter.FitResult fitResult = null;
            string errorMessage = null;
            string origin = "Disk(screen_parameters.json)";
            bool testEnabled = false;

            float sigma = 30;

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
                        fitResult = LenticularParamFitter.FitFromFile("tune_data.log", Console.WriteLine);
                        SaveScreenParametersToJson(fitResult, ScreenParametersFileName);
                        origin = "Fit";
                    }

                    errorMessage = null;
                }
                catch (Exception ex)
                {
                    fitResult = null;
                    errorMessage = ex.Message;
                }
            }

            if (File.Exists("screen_parameters.json"))
            {
                RefreshFit(true);
            }
            else
            {
                RefreshFit(false);
                origin = "Fit";
            }


            GUI.PromptOrBringToFront(pb =>
            {
                pb.Panel.ShowTitle("Fit tuned parameters");

                if (pb.Closing())
                {
                    pb.Panel.Exit();
                    return;
                }

                if (errorMessage != null)
                {
                    pb.Label($"Error: {errorMessage}");
                    if (pb.Button("Retry loading"))
                    {
                        RefreshFit(false);
                    }
                    return;
                }

                var result = fitResult;
                if (result == null)
                {
                    pb.Label("No fit available.");
                    if (pb.Button("Reload fit"))
                    {
                        RefreshFit(false);
                    }
                    return;
                }

                pb.Label($"Origin:{origin}");
                pb.Label($"Using samples: {result.SampleResiduals.Count}");
                pb.Label($"RMSE: period={result.PeriodStats.RMSE}, angle={result.AngleStats.RMSE}, bias={result.BiasStats.RMSE}");

                pb.Separator();

                pb.Label($"Period => M={result.PeriodModel.M:F6}, H1={result.PeriodModel.H1:F6}, H2={result.PeriodModel.H2:F6}");
                pb.Label($"Plane normal => ({result.PeriodModel.A:F6}, {result.PeriodModel.B:F6}, {result.PeriodModel.C:F6}), ZBias={result.PeriodModel.ZBias:F3}");
                pb.Label($"Angle => Ax={result.AngleModel.Ax:F6}, By={result.AngleModel.By:F6}, Cz={result.AngleModel.Cz:F6}, Bias={result.AngleModel.Bias:F6}");
                pb.Label($"Bias => Scale={result.BiasModel.Scale:+0.000000;-0.000000;+0.000000}, Offset={result.BiasModel.Offset:+0.000000;-0.000000;+0.000000}");

                pb.Separator();

                if (pb.Button("Reload fit"))
                {
                    RefreshFit(false);
                    return;
                }

                pb.Separator();
                pb.CheckBox("Test fitted parameters", ref testEnabled);

                if (testEnabled)
                {
                    pb.DragFloat("Sample sigma", ref sigma, 0.1f, 0, 200);

                    var leftEye = TransformPoint(cameraToActualMatrix, sh431.original_left);
                    var rightEye = TransformPoint(cameraToActualMatrix, sh431.original_right);

                    var leftPrediction = fitResult.PredictWithSample(leftEye.X, leftEye.Y, leftEye.Z, sigma);
                    var rightPrediction = fitResult.PredictWithSample(rightEye.X, rightEye.Y, rightEye.Z, sigma);

                    new SetLenticularParams
                    {
                        left_fill = new Vector4(1, 0, 0, 1),
                        right_fill = new Vector4(0, 0, 1, 1),
                        period_fill_left = 1,
                        period_fill_right = 1,
                        period_total_left = (float)leftPrediction.Period,
                        period_total_right = (float)rightPrediction.Period,
                        phase_init_left = (float)leftPrediction.Bias,
                        phase_init_right = (float)rightPrediction.Bias,
                        phase_init_row_increment_left = (float)leftPrediction.Angle,
                        phase_init_row_increment_right = (float)rightPrediction.Angle
                    }.IssueToTerminal(GUI.localTerminal);

                    pb.Panel.Repaint();
                }
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
            if (!File.Exists("tuning_places.txt"))
            {
                UITools.Alert("AgileX Robotic arm must record Tuning places in main panel!");
                return;
            }

            var lines = File.ReadAllLines("tuning_places.txt");

            bool coarse_iteration = true;

            for (int i=0; i<lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(new char[]{'\t',' ',',',';'}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6)
                {
                    logs.Enqueue($"skip line {i+1}: not enough values");
                    continue;
                }

                if (!float.TryParse(parts[0], out float x) ||
                    !float.TryParse(parts[1], out float y) ||
                    !float.TryParse(parts[2], out float z) ||
                    !float.TryParse(parts[3], out float rx) ||
                    !float.TryParse(parts[4], out float ry) ||
                    !float.TryParse(parts[5], out float rz))
                {
                    logs.Enqueue($"skip line {i+1}: parse error");
                    continue;
                }

                var target = new Vector3(x, y, z);

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
                //continue; // just mock.

                var or=sh431.original_right;
                var ol=sh431.original_left;
                var iv = 0.5f * (or + ol);
                if (iv.X == 0 || iv.Y == 0 || iv.Z == 0)
                {
                    logs.Enqueue("Invalid eye position... revise places.");
                    return;
                }
                var lv3 = TransformPoint(cameraToActualMatrix, ol);
                var rv3 = TransformPoint(cameraToActualMatrix, or);

                var (scoreL, scoreR, periodl, periodr, bl, br, angl, angr) = TuneOnce(coarse_iteration, true);

                if (scoreL == 0 || scoreR == 0)
                {
                    logs.Enqueue("Sanity check failed, might be bad place, revise places.");
                    return;
                }

                if (coarse_iteration)
                {
                    prior_period = periodl;
                    prior_row_increment = angl;
                }
                coarse_iteration = false;

                new SetLenticularParams()
                {
                    left_fill = new Vector4(1, 0, 0, 1),
                    right_fill = new Vector4(0, 0, 1, 1),
                    period_fill_left = 1,
                    period_fill_right = 1,
                    period_total_left = periodl,
                    period_total_right = periodr,
                    phase_init_left = bl,
                    phase_init_right = br,
                    phase_init_row_increment_left  = angl,
                    phase_init_row_increment_right = angr
                }.IssueToTerminal(GUI.localTerminal);
                Thread.Sleep(update_interval);


                logs.Enqueue($"Output data ({lv3})->{scoreL:0.00}/{scoreR:0.00}");
                File.AppendAllLines("tune_data.log", [
                    $"L\t{lv3.X}\t{lv3.Y}\t{lv3.Z}\t{periodl}\t{bl}\t{angl}\t{scoreL:0.00}",
                    $"R\t{rv3.X}\t{rv3.Y}\t{rv3.Z}\t{periodr}\t{br}\t{angr}\t{scoreR:0.00}"
                ]);
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
                
                File.WriteAllBytes($"results\\{i}LR.jpg", ImageCodec.SaveJpegToBytes(
                    new SoftwareBitmap(mergedWidth, mergedHeight, mergedData), 60));
                
                File.WriteAllLines($"results\\{i}.txt",
                [
                    $"L\t{lv3.X}\t{lv3.Y}\t{lv3.Z}\t{periodl}\t{bl}\t{scoreL:0.00}",
                    $"*R\t{rv3.X}\t{rv3.Y}\t{rv3.Z}\t{periodr}\t{br}\t{scoreR:0.00}"
                ]);
            }


            var fitResult = LenticularParamFitter.FitFromFile("tune_data.log", Console.WriteLine);
            File.WriteAllText(ScreenParametersFileName, JsonConvert.SerializeObject(fitResult, ScreenParametersSerializerSettings));

            arm.GotoDefault();
        }



        private static float[] coarse_period_step = [0.0025f, 0.00015f];
        private static int[] coarse_base_period_searchs = [40, 12];

        private static float bias_factor = 0.08f;
        private static int bias_scope = 8;

        private static int update_interval = 220;

        private static float grating_bright = 80;
        private static float retries_limit = 4;


        private static (float scoreL, float scoreR,
            float periodL, float peroidR,
            float leftbias, float rightbias, 
            float degL, float degR) TuneOnce(bool coarse_tune, bool tune_ang)
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

            var left_all_reds = new byte[leftCamera.width * leftCamera.height];
            var right_all_reds = new byte[rightCamera.width * rightCamera.height];

            // Get Screen Saliency.
            new SetLenticularParams()
            {
                left_fill = new Vector4(1, 0, 0, 1),
                right_fill = new Vector4(1, 0, 0, 1),
                period_fill_left = 10,
                period_fill_right = 10,
                period_total_left  = 10,
                period_total_right = 10,
                phase_init_left = 0,
                phase_init_right = 0,
                phase_init_row_increment_left = 0.1f,
                phase_init_row_increment_right = 0.1f
            }.IssueToTerminal(GUI.localTerminal);

            Thread.Sleep(update_interval);

            for (int i = 0; i < leftCamera.height; ++i)
            for (int j = 0; j < leftCamera.width; ++j)
            {
                // only get bright red channel.
                var st = (i * leftCamera.width + j) * 4;
                var r = leftCamera.preparedData[st];
                var g = leftCamera.preparedData[st+1];
                var b = leftCamera.preparedData[st+2];
                if (r > grating_bright && r > g + grating_bright * 0.4 && r > b + grating_bright * 0.5)
                    left_all_reds[i * leftCamera.width + j] = r;
            }
            UITools.ImageShowMono("saliency_L", left_all_reds, leftCamera.width, leftCamera.height, terminal: remote);

            for (int i = 0; i < rightCamera.height; ++i)
            for (int j = 0; j < rightCamera.width; ++j)
            {
                // only get bright red channel.
                var st = (i * rightCamera.width + j) * 4;
                var r = rightCamera.preparedData[st];
                var g = rightCamera.preparedData[st + 1];
                var b = rightCamera.preparedData[st + 2];
                if (r > grating_bright && r > g + grating_bright * 0.4 && r > b + grating_bright * 0.5)
                    right_all_reds[i * rightCamera.width + j] = r;
            }
            UITools.ImageShowMono("saliency_R", right_all_reds, rightCamera.width, rightCamera.height, terminal: remote);

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
                            left_fill = new Vector4(1, 0, 0, 1),
                            right_fill = new Vector4(1, 0, 0, 0),
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
                            if (left_all_reds[i * leftCamera.width + j] > grating_bright)
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

                        // Take only upper half of amplitudes
                        int halfH = H / 2;

                        // Create theta bins from 0 to 90 degrees with 0.5 degree spacing
                        float thetaStep = 0.5f;
                        int numBins = (int)(90f / thetaStep) + 1; // 181 bins
                        float[] polar_amp_sum = new float[numBins];

                        // For each amplitude in upper half, compute theta and bin it
                        for (int y = 0; y < halfH; ++y)
                        {
                            for (int x = 0; x < W; ++x)
                            {
                                if (x == 0 && y == 0) continue; // Skip origin

                                if (Math.Sqrt(y * y + x * x) > halfH) continue;

                                // Compute angle in degrees (0 to 90)
                                float thetaDeg = MathF.Atan2(y, x) * 180f / MathF.PI;

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
                    st_bris *= 0.3f;
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
                    left_fill = new Vector4(1, 0, 0, 1),
                    right_fill = new Vector4(0, 0, 0, 0),
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
                        if (left_all_reds[i * leftCamera.width + j] > grating_bright)
                        {
                            var r = leftCamera.preparedData[(i * leftCamera.width + j) * 4] / 256f;
                            sum += r;
                            sumSq += r * r;
                            validCount++;
                            pvalues[i * leftCamera.width + j] = (byte)Math.Min(255,
                                (float)leftCamera.preparedData[(i * leftCamera.width + j) * 4] /
                                left_all_reds[i * leftCamera.width + j] * 256f);
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
            (float meanL, float stdL, float meanR, float stdR) computeLR()
            {
                // Compute std for valid pixels in single pass
                float sumL = 0;
                float sumSqL = 0;
                int validCountL = 0;

                for (int i = 0; i < leftCamera.height; ++i)
                for (int j = 0; j < leftCamera.width; ++j)
                {
                    if (left_all_reds[i * leftCamera.width + j] > 150)
                    {
                        var r = (leftCamera.preparedData[(i * leftCamera.width + j) * 4]) / 255f;
                        sumL += MathF.Pow(r, 2f);
                        sumSqL += r * r;
                        validCountL++;
                    }
                }

                float meanL = validCountL > 0 ? sumL / validCountL : 0;
                float stdL = validCountL > 0 ? MathF.Sqrt(sumSqL / validCountL - meanL * meanL) : 0;


                float sumR = 0;
                float sumSqR = 0;
                int validCountR = 0;

                for (int i = 0; i < rightCamera.height; ++i)
                for (int j = 0; j < rightCamera.width; ++j)
                {
                    if (right_all_reds[i * rightCamera.width + j] > 150)
                    {
                        var r = (rightCamera.preparedData[(i * rightCamera.width + j) * 4]) / 255f;
                        sumR += MathF.Pow(r, 2f);
                        sumSqR += r * r;
                        validCountR++;
                    }
                }               

                float meanR = validCountR > 0 ? sumR / validCountR : 0;
                float stdR = validCountR > 0 ? MathF.Sqrt(sumSqR / validCountR - meanR * meanR) : 0;
                return (meanL, stdL, meanR, stdR);
            }

            float computeScore(float meanL, float stdL, float meanR, float stdR, int lr) //lr=0, left, lr=1, right.
            {
                float myMean=lr==0?meanL:meanR;
                float otherMean = lr == 0 ? meanR : meanL;
                float myStd = lr == 0 ? stdL : stdR;
                float otherStd = lr == 0 ? stdR : stdL;

                var score = (myMean - otherMean + Math.Min(10, myMean / (otherMean + 0.0001)) * 0.01) * MathF.Pow(myMean,2) / myStd;
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
                                left_fill = new Vector4(1, 0, 0, 1),
                                right_fill = new Vector4(1, 0, 0, 0),
                                period_fill_left = 1,
                                period_total_left = st_period,
                                phase_init_left = il,
                                phase_init_right = 0,
                                phase_init_row_increment_left = st_bri
                            }.IssueToTerminal(GUI.localTerminal);

                            Thread.Sleep(update_interval);

                            var (meanL, stdL, meanR, stdR) = computeLR();

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
                    float bi_step = 0.00003f;
                    var rnd = new Random();

                    Vector3 GenRnd() => new(rnd.NextSingle() * 2 - 1, rnd.NextSingle() * 2 - 1,
                        coarse_tune || tune_ang ? rnd.NextSingle() * 2 - 1 : 0);

                    Vector3 step_v3 = GenRnd();
                    step_v3 /= step_v3.Length();
                    float mag = 1;
                    var streak = 0;
                    for (int i = 0; i < 200; ++i)
                    {
                        var test_period = (float)(st_period + period_step * step_v3.X * mag);
                        var test_bias = (float)(st_bias + bias_step * step_v3.Y * mag);
                        var test_bi = (float)(st_bri + bi_step * step_v3.Z * mag);
                        new SetLenticularParams()
                        {
                            left_fill = new Vector4(1, 0, 0, left?1:0),
                            right_fill = new Vector4(1, 0, 0, left?0:1),
                            period_fill_left = 1,
                            period_fill_right = 1,
                            period_total_left = test_period,
                            period_total_right = test_period,
                            phase_init_left = left?test_bias:0,
                            phase_init_right = left?0:test_bias,
                            phase_init_row_increment_left  = test_bi,
                            phase_init_row_increment_right = test_bi
                        }.IssueToTerminal(GUI.localTerminal);

                        Thread.Sleep(update_interval); // wait for grating param to apply, also consider auto-exposure.

                        var (meanL, stdL, meanR, stdR) = computeLR();
                        var score = computeScore(meanL, stdL, meanR, stdR, left?0:1);
                        if (score * 1.01f < scorem)
                        {
                            step_v3 = GenRnd();
                            Console.WriteLine(
                                $"{i}:neg {scorem}/{score}: [{st_period}, {st_bias}], mag={mag}");
                            if (mag > 1) mag = 1;
                            mag *= 0.9f;
                            if (mag < 0.01) mag = 0.01f;
                            streak += 1;
                        }
                        else
                        {
                            st_period = test_period;
                            st_bias = test_bias;
                            st_bri = test_bi;
                            scorem = Math.Max(score, scorem);
                            Console.WriteLine(
                                $"{i}:good {scorem}->{score}: [{st_period}, {st_bias}], mag={mag}");
                            streak = 0;
                            mag *= 1.05f;
                        }

                        if (streak > (coarse_tune || tune_ang ? 20 : 10) && scorem > 0.15) break;
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
                                left_fill = new Vector4(0, 0, 0, 0),
                                right_fill = new Vector4(1, 0, 0, 1),
                                period_fill_right = 1,
                                period_total_right = st_period,
                                phase_init_left = st_bias_left,
                                phase_init_right = ir,
                                phase_init_row_increment_right = st_bri
                            }.IssueToTerminal(GUI.localTerminal);

                            Thread.Sleep(update_interval); // wait for grating param to apply, also consider auto-exposure.

                            var (meanL, stdL, meanR, stdR) = computeLR();

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

                Console.WriteLine($"scores[l,r]=[{scoreLeft},{scoreRight}]");
                Console.WriteLine($"results[p,l,r]=[{st_period},{st_bias_left},{st_bias_right}]");
            }

            Console.WriteLine($"tuning time = {(DateTime.Now - tic).TotalSeconds:0.0}s");

            return (scoreLeft, scoreRight, 
                period_left, period_right, 
                st_bias_left, st_bias_right, 
                ang_left, ang_right);
        }
    }
}
