using CycleGUI.API;
using CycleGUI;
using System.Numerics;
using OpenCvSharp;

namespace HoloCaliberationDemo
{
    internal partial class Program
    {
        private static void LenticularTuner()
        {
            // goto initial position.
            // tune.
            // step 1. goto random place.
            TuneOnce(false, true, true);
        }


        private static float base_row_increment = 0.18f, base_row_increment_search = 0.002f;
        private static float base_period = 5.25f;
        private static float[] base_period_steps = [0.008f, 0.001f];
        private static int[] base_period_iterations = [40, 12];

        private static float prior_bias_left = 0, prior_bias_right = 0;
        private static float bias_factor = 0.08f;
        private static int bias_scope = 8;

        private static int update_interval = 250;

        private static float grating_bright = 80;

        private static void TuneOnce(bool tune_angle, bool tune_period, bool tune_bias)
        {
            var tic = DateTime.Now;

            // Get Screen Saliency.
            new SetLenticularParams()
            {
                left_fill = new Vector4(1, 0, 0, 1),
                right_fill = new Vector4(1, 0, 0, 1),
                period_fill = 10,
                period_total = 10,
                phase_init_left = 0,
                phase_init_right = 0,
                phase_init_row_increment = 0.1f
            }.IssueToTerminal(GUI.localTerminal);

            Thread.Sleep(update_interval);

            var left_all_reds = new byte[leftCamera.width * leftCamera.height];
            for (int i = 0; i < leftCamera.height; ++i)
            for (int j = 0; j < leftCamera.width; ++j)
            {
                // only get bright red channel.
                var st = (i * leftCamera.width + j) * 4;
                var r = leftCamera.preparedData[st];
                var g = leftCamera.preparedData[st+1];
                var b = leftCamera.preparedData[st+2];
                if (r > grating_bright && r > g + 50 && r > b + 80)
                    left_all_reds[i * leftCamera.width + j] = r;
            }
            UITools.ImageShowMono("saliency_L", left_all_reds, leftCamera.width, leftCamera.height, terminal: remote);

            var right_all_reds = new byte[rightCamera.width * rightCamera.height];
            for (int i = 0; i < rightCamera.height; ++i)
            for (int j = 0; j < rightCamera.width; ++j)
            {
                // only get bright red channel.
                var st = (i * rightCamera.width + j) * 4;
                var r = rightCamera.preparedData[st];
                var g = rightCamera.preparedData[st + 1];
                var b = rightCamera.preparedData[st + 2];
                if (r > grating_bright && r > g + 50 && r > b + 80)
                    right_all_reds[i * rightCamera.width + j] = r;
            }
            UITools.ImageShowMono("saliency_R", right_all_reds, rightCamera.width, rightCamera.height, terminal: remote);

            // ========Tune row increment. ============

            // UI Tuner:
            if (false){
                float ri = 0.18f;
                float dragSpeed = -6.0f; // e^-6 ≈ 0.0025
            
                GUI.PromptAndWaitPanel(pb =>
                {
                    // Adjust speed control
                    pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -12.0f, 0.0f);
                    pb.Label($"Current Speed: {Math.Exp(dragSpeed):F6}");
            
                    float speed = (float)Math.Exp(dragSpeed);
            
                    // Lenticular parameter controls
                    bool paramsChanged = false;
                    paramsChanged |= pb.DragFloat("row increment", ref ri, speed, -1,
                        1);
                    paramsChanged |= pb.Button("Refresh");
                    
                    if (!paramsChanged) return;

                    new SetLenticularParams()
                    {
                        left_fill = new Vector4(1, 0, 0, 1),
                        right_fill = new Vector4(1, 0, 0, 0),
                        period_fill = 1,
                        period_total = 100,
                        phase_init_left = 0,
                        phase_init_right = 0,
                        phase_init_row_increment = ri
                    }.IssueToTerminal(GUI.localTerminal);

                    Thread.Sleep(200); // wait for grating param to apply

                    int H = leftCamera.height, W = leftCamera.width;
                    var values = new byte[W * H];

                    for (int i = 0; i < leftCamera.height; ++i)
                        for (int j = 0; j < leftCamera.width; ++j)
                        {
                            if (left_all_reds[i * leftCamera.width + j] > 150)
                                values[i * leftCamera.width + j] = leftCamera.preparedData[(i * leftCamera.width + j) * 4];
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
                        }
                    }
                    
                    UITools.ImageShowMono("values", values, leftCamera.width, leftCamera.height, terminal: remote);

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

                    UITools.ImageShowMono("fft", amplitudes, W, H);

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

                    pb.Plot2D("Polar", polar_amp_sum);

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

                    Console.WriteLine($"ri={ri}, illum={illum}");
                });
            }

            var st_bri = base_row_increment;
            if (tune_angle){
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
                            period_fill = 1,
                            period_total = 100,
                            phase_init_left = 0,
                            phase_init_right = 0,
                            phase_init_row_increment = ri
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

                        // Console.WriteLine($"k={k}, ri={ri}, illum={illum}");
                    }

                    // Console.WriteLine("=====");
                    st_bris *= 0.3f;
                    st_bri = max_illum_ri;
                }

                Console.WriteLine($"best row_increment={st_bri}");
                // Console.WriteLine($"---");
            }


            // ==================== tune period total to discover best fit.=================

            st_bri = 0.183528f;

            // manual
            if (false)
            {

                float period_total = 5.33f;
                float left_bias = 0;
                float dragSpeed = -8.0f; // e^-6 ≈ 0.0025

                int H = leftCamera.height, W = leftCamera.width;
                var values = new byte[W * H];

                GUI.PromptAndWaitPanel(pb =>
                {
                    // Adjust speed control
                    pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -12.0f, 0.0f);
                    pb.Label($"Current Speed: {Math.Exp(dragSpeed):F6}");

                    float speed = (float)Math.Exp(dragSpeed);

                    // Lenticular parameter controls
                    bool paramsChanged = false;
                    pb.DragFloat("Peroid total", ref period_total, speed, -100, 100);
                    pb.DragFloat("Left bias", ref left_bias, speed, -100, 100);
                    pb.Button("Refresh");

                    new SetLenticularParams()
                    {
                        left_fill = new Vector4(1, 0, 0, 1),
                        right_fill = new Vector4(0, 0, 0, 0),
                        period_fill = 1,
                        period_total = period_total,
                        phase_init_left = left_bias,
                        phase_init_right = 0,
                        phase_init_row_increment = st_bri
                    }.IssueToTerminal(GUI.localTerminal);
                    Thread.Sleep(200);

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
                            values[i * leftCamera.width + j] = leftCamera.preparedData[(i * leftCamera.width + j) * 4];
                        }
                    }

                    // Downscale values by 0.25x
                    using var srcMat = Mat.FromPixelData(H, W, MatType.CV_8UC1, values);
                    int newW = W / 4, newH = H / 4;
                    using var dstMat = new Mat();
                    Cv2.Resize(srcMat, dstMat, new OpenCvSharp.Size(newW, newH), 0, 0, InterpolationFlags.Area);
                    byte[] downscaledValues = new byte[newW * newH];
                    dstMat.GetArray(out downscaledValues);
                    
                    var amplitudes = ComputeAmplitudeFloat(downscaledValues, newW, newH);
                    amplitudes[0] = 0;

                    // Perform a 5x5 window mean blur on amplitudes
                    float[] blurred = new float[amplitudes.Length];
                    int kernel = 2;
                    for (int y = 0; y < newH; ++y)
                        for (int x = 0; x < newW; ++x)
                        {
                            float sum2 = 0;
                            int count = 0;
                            for (int dy = -kernel; dy <= kernel; ++dy)
                                for (int dx = -kernel; dx <= kernel; ++dx)
                                {
                                    int ny = y + dy, nx = x + dx;
                                    if (ny >= 0 && ny < newH && nx >= 0 && nx < newW)
                                    {
                                        sum2 += amplitudes[ny * newW + nx];
                                        count++;
                                    }
                                }

                            blurred[y * newW + x] = sum2 / count;
                        }

                    Array.Copy(blurred, amplitudes, amplitudes.Length);

                    var ss = 0d;
                    for (int y = 0; y < newH / 2; ++y)
                    {
                        for (int x = 0; x < newW / 2; ++x)
                        {
                            ss += amplitudes[y * newW + x] * Math.Sqrt(y * y + x * x) / newW / newH;
                        }
                    }

                    float mean = validCount > 0 ? sum / validCount : 0;
                    float std = validCount > 0 ? MathF.Sqrt(sumSq / validCount - mean * mean) : 0;

                    ss /= mean;

                    float score = (float)(std * ss);

                    Console.WriteLine(
                        $"{period_total}, Valid pixels: {validCount}, Mean: {mean:F2}, Std: {std:F2}, fft={ss}, score={std * ss}");

                });
            }

            var st_bias_left = prior_bias_left;
            var st_bias_right = prior_bias_right;
            var st_period = base_period;

            int pH = leftCamera.height, pW = leftCamera.width;
            var pvalues = new byte[pW * pH];
            float calcPeroidScore(float pi)
            {
                new SetLenticularParams()
                {
                    left_fill = new Vector4(1, 0, 0, 1),
                    right_fill = new Vector4(0, 0, 0, 0),
                    period_fill = 1,
                    period_total = pi,
                    phase_init_left = st_bias_left,
                    phase_init_right = 0,
                    phase_init_row_increment = st_bri
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
                amplitudes[0] = 0;

                // Perform a 5x5 window mean blur on amplitudes
                float[] blurred = new float[amplitudes.Length];
                int kernel = 2;
                for (int y = 0; y < newH; ++y)
                    for (int x = 0; x < newW; ++x)
                    {
                        float sum2 = 0;
                        int count = 0;
                        for (int dy = -kernel; dy <= kernel; ++dy)
                            for (int dx = -kernel; dx <= kernel; ++dx)
                            {
                                int ny = y + dy, nx = x + dx;
                                if (ny >= 0 && ny < newH && nx >= 0 && nx < newW)
                                {
                                    sum2 += amplitudes[ny * newW + nx];
                                    count++;
                                }
                            }

                        blurred[y * newW + x] = sum2 / count;
                    }

                Array.Copy(blurred, amplitudes, amplitudes.Length);

                var ss = 0d;
                for (int y = 3; y < newH / 2; ++y)
                {
                    for (int x = 3; x < newW / 2; ++x)
                    {
                        ss += amplitudes[y * newW + x] * Math.Sqrt(y * y + x * x) / newW / newH;
                    }
                }

                float mean = validCount > 0 ? sum / validCount : 0;
                float std = validCount > 0 ? MathF.Sqrt(sumSq / validCount - mean * mean) : 0;

                ss /= mean;

                float score = (float)(std * ss);

                Console.WriteLine($"{pi}, std={std}, ss={ss}, score={score:F2}");
                return score;
            }

            // auto.
            if (tune_period){
                for (int z = 0; z < base_period_steps.Length; ++z)
                {
                    var minscore = 99999999f;
                    var minscore_pi = 0f;

                    for (int k = 0; k < base_period_iterations[z]; ++k)
                    {
                        var pi = st_period + (k - base_period_iterations[z] / 2) * base_period_steps[z];
                        
                        float score = calcPeroidScore(pi);

                        if (minscore > score)
                        {
                            minscore = score;
                            minscore_pi = pi;
                        }
                    }

                    st_period = minscore_pi;
                    // Console.WriteLine("=====");
                }

                Console.WriteLine($"Fin1 period={st_period}");
            }

            // st_period = 5.331f;

            // ============ bias tuning:==============

            // manual
            if (false){
                float li = 0.18f;
                float dragSpeed = -6.0f; // e^-6 ≈ 0.0025
                GUI.PromptAndWaitPanel(pb =>
                {
                    // Adjust speed control
                    pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -12.0f, 0.0f);
                    pb.Label($"Current Speed: {Math.Exp(dragSpeed):F6}");

                    float speed = (float)Math.Exp(dragSpeed);

                    // Lenticular parameter controls
                    bool paramsChanged = false;
                    paramsChanged |= pb.DragFloat("left init", ref li, speed, -5,
                        5);
                    paramsChanged |= pb.DragFloat("Peroid total", ref st_period, speed * 0.1f, -100, 100);
                    paramsChanged |= pb.Button("Refresh");

                    if (!paramsChanged) return;

                    new SetLenticularParams()
                    {
                        left_fill = new Vector4(1, 0, 0, 1),
                        right_fill = new Vector4(1, 0, 0, 0),
                        period_fill = 1,
                        period_total = st_period,
                        phase_init_left = li,
                        phase_init_right = 0,
                        phase_init_row_increment = st_bri
                    }.IssueToTerminal(GUI.localTerminal);

                    Thread.Sleep(200); // wait for grating param to apply

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
                                sumL += r;
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
                        for (int j = 0; j < leftCamera.width; ++j)
                        {
                            if (right_all_reds[i * rightCamera.width + j] > 150)
                            {
                                var r = (rightCamera.preparedData[(i * rightCamera.width + j) * 4]) / 255f;
                                sumR += r;
                                sumSqR += r * r;
                                validCountR++;
                            }
                        }

                    float meanR = validCountR > 0 ? sumR / validCountR : 0;
                    float stdR = validCountR > 0 ? MathF.Sqrt(sumSqR / validCountR - meanR * meanR) : 0;

                    var score = stdL * stdR * (meanL - meanR);
                    Console.WriteLine($"{li}, Std={stdL:F4} : {stdR:F4}, Mean={meanL:F4} : {meanR:F4}  ==>{score}");
                });
            }

            // auto.
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
                        sumL += r;
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
                        sumR += r;
                        sumSqR += r * r;
                        validCountR++;
                    }
                }

                float meanR = validCountR > 0 ? sumR / validCountR : 0;
                float stdR = validCountR > 0 ? MathF.Sqrt(sumSqR / validCountR - meanR * meanR) : 0;
                return (meanL, stdL, meanR, stdR);
            }

            if (tune_bias)
            {
                for (int w = 0; w < 7; ++w)
                {
                    // left bias
                    {
                        var st_step = st_period / (16 + w * 5);
                        for (int z = 0; z < 2; ++z)
                        {
                            int numSamples = bias_scope * 2 + 1;
                            float[] scores = new float[numSamples];
                            float[] ilValues = new float[numSamples];
                            
                            // First, compute all scores and cache them
                            for (int k = 0; k < numSamples; ++k)
                            {
                                var v = k <= bias_scope ? k : bias_scope - k; //0~8,-1~-8.
                                var il = st_bias_left + v * st_step;
                                ilValues[k] = il;
                                
                                new SetLenticularParams()
                                {
                                    left_fill = new Vector4(1, 0, 0, 1),
                                    right_fill = new Vector4(1, 0, 0, 0),
                                    period_fill = 1,
                                    period_total = st_period,
                                    phase_init_left = il,
                                    phase_init_right = 0,
                                    phase_init_row_increment = st_bri
                                }.IssueToTerminal(GUI.localTerminal);

                                Thread.Sleep(update_interval);

                                var (meanL, stdL, meanR, stdR) = computeLR();

                                var score = (meanL - meanR + Math.Min(10, meanL / (meanR + 0.0001)) * 0.01) * meanL /
                                            stdL;
                                
                                scores[k] = (float)score;

                                Console.WriteLine(
                                    $"L> {il}, score={score:F4} mean/std=({meanL:f3}/{stdL:f3}),({meanR:f3}/{stdR:f3})");
                            }
                            
                            // Apply 5-window filter
                            float[] filteredScores = new float[numSamples];
                            for (int k = 0; k < numSamples; ++k)
                            {
                                float sum = 0;
                                for (int offset = -2; offset <= 2; ++offset)
                                {
                                    int idx = k + offset;
                                    // Border handling: copy border value
                                    if (idx < 0) idx = 0;
                                    if (idx >= numSamples) idx = numSamples - 1;
                                    sum += scores[idx];
                                }
                                filteredScores[k] = sum / 5.0f;
                            }
                            
                            // Find maximum score from filtered data
                            var maxScore = 0f;
                            var maxScoreIl = 0f;
                            for (int k = 0; k < numSamples; ++k)
                            {
                                if (maxScore < filteredScores[k])
                                {
                                    maxScore = filteredScores[k];
                                    maxScoreIl = ilValues[k];
                                }
                            }
                            Console.WriteLine($"..Select il={ilValues}, score={maxScore}");

                            st_bias_left = maxScoreIl;
                            st_step *= bias_factor;
                        }

                        Console.WriteLine($"temp left={st_bias_left}");
                    }


                    // redo period.
                    {
                        int scope = 15 - w;
                        int numSamples = scope * 2 + 1;
                        float[] scores = new float[numSamples];
                        float[] piValues = new float[numSamples];
                        
                        // First, compute all scores and cache them
                        for (int k = 0; k < numSamples; ++k)
                        {
                            var pi = st_period + (k - scope) * (0.0001f - 0.00001f * w);
                            piValues[k] = pi;
                            scores[k] = calcPeroidScore(pi);
                        }
                        
                        // Apply 5-window filter
                        float[] filteredScores = new float[numSamples];
                        for (int k = 0; k < numSamples; ++k)
                        {
                            float sum = 0;
                            for (int offset = -2; offset <= 2; ++offset)
                            {
                                int idx = k + offset;
                                // Border handling: copy border value
                                if (idx < 0) idx = 0;
                                if (idx >= numSamples) idx = numSamples - 1;
                                sum += scores[idx];
                            }
                            filteredScores[k] = sum / 5.0f;
                        }
                        
                        // Find minimum score from filtered data
                        var minscore = 99999999f;
                        var minscore_pi = 0f;
                        for (int k = 0; k < numSamples; ++k)
                        {
                            if (minscore > filteredScores[k])
                            {
                                minscore = filteredScores[k];
                                minscore_pi = piValues[k];
                            }
                        }

                        st_period = minscore_pi;
                        Console.WriteLine($"temp period={st_period}");
                    }
                }

                // left bias.
                {
                    var st_step = st_period / 24;
                    for (int z = 0; z < 1; ++z)
                    {
                        var maxScore = 0f;
                        var maxScoreIl = 0f;
                        for (int k = 0; k < bias_scope * 2 + 1; ++k)
                        {
                            var v = k <= bias_scope ? k : bias_scope - k; //0~8,-1~-8.
                            var il = st_bias_left + v * st_step;
                            new SetLenticularParams()
                            {
                                left_fill = new Vector4(1, 0, 0, 1),
                                right_fill = new Vector4(1, 0, 0, 0),
                                period_fill = 1,
                                period_total = st_period,
                                phase_init_left = il,
                                phase_init_right = 0,
                                phase_init_row_increment = st_bri
                            }.IssueToTerminal(GUI.localTerminal);

                            Thread.Sleep(
                                update_interval); // wait for grating param to apply, also consider auto-exposure.

                            var (meanL, stdL, meanR, stdR) = computeLR();

                            var score = (meanL - meanR + Math.Min(10, meanL / (meanR + 0.0001)) * 0.01) * meanL /
                                        stdL;

                            if (maxScore < score)
                            {
                                maxScore = (float)score;
                                maxScoreIl = il;
                            }

                            Console.WriteLine(
                                $"L> {il}, score={score:F4} mean/std=({meanL:f3}/{stdL:f3}),({meanR:f3}/{stdR:f3})");
                        }

                        st_bias_left = maxScoreIl;
                        st_step *= bias_factor;
                        Console.WriteLine($"*iterate on {st_bias_left}@{maxScore}");
                    }

                    Console.WriteLine($"fin2 left={st_bias_left}");
                }
                
                // right bias.
                {
                    var st_step = st_period / 9;
                    for (int z = 0; z < 3; ++z)
                    {
                        var maxScore = 0f;
                        var maxScoreIr = 0f;
                        for (int k = 0; k < bias_scope * 2 + 1; ++k)
                        {
                            var v = k <= bias_scope ? k : bias_scope - k; //0~8,-1~-8.
                            var ir = st_bias_right + v * st_step;
                            new SetLenticularParams()
                            {
                                left_fill = new Vector4(0, 0, 0, 0),
                                right_fill = new Vector4(1, 0, 0, 1),
                                period_fill = 1,
                                period_total = st_period,
                                phase_init_left = st_bias_left,
                                phase_init_right = ir,
                                phase_init_row_increment = st_bri
                            }.IssueToTerminal(GUI.localTerminal);

                            Thread.Sleep(update_interval); // wait for grating param to apply, also consider auto-exposure.

                            var (meanL, stdL, meanR, stdR) = computeLR();

                            var score = (meanR - meanL + Math.Min(10, meanR / (meanL + 0.0001)) * 0.01) * meanR /
                                        stdR;

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
                    }

                    Console.WriteLine($"fin right={st_bias_right}");

                }
            }






            // ======== finished ===========
            new SetLenticularParams()
            {
                left_fill = new Vector4(1, 0, 0, 1),
                right_fill = new Vector4(0, 0, 1, 1),
                period_fill = 1,
                period_total = st_period,
                phase_init_left = st_bias_left,
                phase_init_right = st_bias_right,
                phase_init_row_increment = st_bri
            }.IssueToTerminal(GUI.localTerminal);
            Console.WriteLine($"tuning time = {(DateTime.Now - tic).TotalSeconds:0.0}s");
            {
                float fill = 1f;
                float dragSpeed = -9.0f; // e^-6 ≈ 0.0025
                GUI.PromptPanel(pb =>
                {
                    pb.Panel.ShowTitle("results");

                    // Adjust speed control
                    pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -12.0f, 0.0f);
                    pb.Label($"Current Speed: {Math.Exp(dragSpeed):F6}");

                    float speed = (float)Math.Exp(dragSpeed);

                    // Lenticular parameter controls
                    bool paramsChanged = false;
                    paramsChanged |= pb.DragFloat("fill", ref fill, speed, -5, 5);
                    paramsChanged |= pb.DragFloat("Phase Init Left", ref st_bias_left, speed, -100, 100);
                    paramsChanged |= pb.DragFloat("Phase Init Right", ref st_bias_right, speed, -100, 100);
                    paramsChanged |= pb.Button("Refresh");

                    if (!paramsChanged) return;

                    new SetLenticularParams()
                    {
                        left_fill = new Vector4(1, 0, 0, 1),
                        right_fill = new Vector4(0, 0, 1, 1),
                        period_fill = fill,
                        period_total = st_period,
                        phase_init_left = st_bias_left,
                        phase_init_right = st_bias_right,
                        phase_init_row_increment = st_bri
                    }.IssueToTerminal(GUI.localTerminal);

                    if (pb.Closing())
                        pb.Panel.Exit();
                });
            }
        }
    }
}
