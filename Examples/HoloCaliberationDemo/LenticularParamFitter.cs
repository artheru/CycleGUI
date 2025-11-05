using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HoloCaliberationDemo
{
    internal static class LenticularParamFitter
    {
        private const double BinSizeMillimeters = 20.0;

        public static FitResult FitFromFile(string path, Action<string>? logger = null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}", path);

            var raw = File.ReadAllText(path);
            return FitFromRaw(raw, logger);
        }

        public static FitResult FitFromRaw(string rawSamples, Action<string>? logger = null)
        {
            var allSamples = ParseSamples(rawSamples);
            logger?.Invoke($"Loaded {allSamples.Count} raw samples.");

            var binnedSamples = ApplySpatialBinning(allSamples, BinSizeMillimeters);
            logger?.Invoke($"Samples after {BinSizeMillimeters}mm spatial binning: {binnedSamples.Count}.");

            var filteredSamples = RemoveLowScoreSamples(binnedSamples, 0.2, 0.3, out var scoreThreshold);
            logger?.Invoke($"Global score threshold at bottom 20%: {scoreThreshold:F4}");
            logger?.Invoke($"Remaining calibration samples: {filteredSamples.Count}");

            if (filteredSamples.Count == 0)
                throw new InvalidOperationException("No calibration samples available after filtering.");

            var periodModel = PeriodModel.Fit(filteredSamples);
            logger?.Invoke(
                $"Period model => h1: {periodModel.H1:F6}, h2: {periodModel.H2:F6}, m: {periodModel.M:F6}, display height: {periodModel.DisplayHeight:F6}");
            var normalMagnitude = Math.Sqrt(
                periodModel.A * periodModel.A + periodModel.B * periodModel.B + periodModel.C * periodModel.C);
            logger?.Invoke(
                $"Period plane coefficients => a: {periodModel.A:F6}, b: {periodModel.B:F6}, c: {periodModel.C:F6} (|n|={normalMagnitude:F6})");
            logger?.Invoke($"Period Z bias => {periodModel.ZBias:F3}");
            logger?.Invoke($"Period offset midpoint => {periodModel.BiasOffset:F6}");

            var angleModel = AngleModel.Fit(filteredSamples, periodModel.ZBias);

            var biasFit = BiasModel.Fit(filteredSamples, periodModel, logger);
            var biasModel = biasFit.Model;

            logger?.Invoke(
                $"Bias model => bias = NormalizeToPeriod({biasModel.Scale:+0.000000;-0.000000;+0.000000} * T1(x,y,z,angle) + {biasModel.Offset:+0.000000;-0.000000;+0.000000}), where T1 = ((x - angle*y)/sqrt(1+angle^2)) * {biasModel.DisplayHeight:F6} / abs(z + {biasModel.ZBias:F6})");

            var calibration = new CalibrationParameters(periodModel, angleModel, biasModel);

            var periodResiduals = filteredSamples
                .Select(s => periodModel.ComputePeriod(s.X, s.Y, s.Z) - s.Period)
                .ToArray();
            var periodStats = ResidualStatistics.Compute(periodResiduals);
            logger?.Invoke($"All period residuals => MAE: {periodStats.MAE:F6}, RMSE: {periodStats.RMSE:F6}, Max: {periodStats.MaxAbsolute:F6}");

            var angleResiduals = filteredSamples
                .Select(s => angleModel.ComputeAngle(s.X, s.Y, s.Z) - s.Angle)
                .ToArray();
            var angleStats = ResidualStatistics.Compute(angleResiduals);
            logger?.Invoke($"All angle residuals => MAE: {angleStats.MAE:F6}, RMSE: {angleStats.RMSE:F6}, Max: {angleStats.MaxAbsolute:F6}");

            var biasResiduals = biasFit.Residuals.Select(r => r.ModularError).ToArray();
            var biasStats = ResidualStatistics.Compute(biasResiduals);
            logger?.Invoke($"All bias residuals => MAE: {biasStats.MAE:F6}, RMSE: {biasStats.RMSE:F6}, Max: {biasStats.MaxAbsolute:F6}");

            var heightStats = HeightStatistics.Compute(filteredSamples, periodModel);
            logger?.Invoke(
                $"All height residuals => mean: {heightStats.Mean:F6} (bias {heightStats.Bias:+0.000000;-0.000000;+0.000000}), std: {heightStats.StdDev:F6}, min: {heightStats.Min:F6}, max: {heightStats.Max:F6}");

            if (biasFit.TopResiduals.Length > 0)
            {
                foreach (var info in biasFit.TopResiduals)
                {
                    logger?.Invoke(
                        $"  Bias sample {info.Sample.Eye} ({info.Sample.X:F1}, {info.Sample.Y:F1}, {info.Sample.Z:F1}) -> target: {info.TargetBias:F4}, pred: {info.PredictedBias:F4}, wrapped error: {info.ModularError:F4}");
                }
            }

            return new FitResult(
                allSamples,
                binnedSamples,
                filteredSamples,
                scoreThreshold,
                periodModel,
                angleModel,
                biasModel,
                calibration,
                periodResiduals,
                angleResiduals,
                biasResiduals,
                biasFit.TopResiduals,
                heightStats,
                biasFit.BiasPairs,
                biasFit.SlopeFit,
                biasFit.ScaleOffsetFit);
        }

        internal sealed record FitResult(
            IReadOnlyList<Sample> RawSamples,
            IReadOnlyList<Sample> BinnedSamples,
            IReadOnlyList<Sample> FilteredSamples,
            double ScoreThreshold,
            PeriodModel PeriodModel,
            AngleModel AngleModel,
            BiasModel BiasModel,
            CalibrationParameters Calibration,
            IReadOnlyList<double> PeriodResiduals,
            IReadOnlyList<double> AngleResiduals,
            IReadOnlyList<double> BiasResiduals,
            IReadOnlyList<BiasResidual> TopBiasResiduals,
            HeightStatistics HeightStats,
            IReadOnlyList<PairObservation> BiasPairs,
            BiasModel.SlopeFitResult SlopeFit,
            BiasModel.ScaleOffsetFitResult ScaleOffsetFit);

        internal readonly struct ResidualStatistics
        {
            public ResidualStatistics(double mae, double rmse, double maxAbsolute)
            {
                MAE = mae;
                RMSE = rmse;
                MaxAbsolute = maxAbsolute;
            }

            public double MAE { get; }
            public double RMSE { get; }
            public double MaxAbsolute { get; }

            public static ResidualStatistics Compute(IReadOnlyList<double> residuals)
            {
                if (residuals.Count == 0)
                    return new ResidualStatistics(0, 0, 0);

                double mae = residuals.Average(r => Math.Abs(r));
                double rmse = Math.Sqrt(residuals.Average(r => r * r));
                double max = residuals.Max(r => Math.Abs(r));
                return new ResidualStatistics(mae, rmse, max);
            }
        }

        internal readonly struct HeightStatistics
        {
            public HeightStatistics(double mean, double stdDev, double min, double max, double bias)
            {
                Mean = mean;
                StdDev = stdDev;
                Min = min;
                Max = max;
                Bias = bias;
            }

            public double Mean { get; }
            public double StdDev { get; }
            public double Min { get; }
            public double Max { get; }
            public double Bias { get; }

            public static HeightStatistics Compute(IEnumerable<Sample> samples, PeriodModel model)
            {
                var heights = samples.Select(s =>
                {
                    double plane = model.EvaluatePlane(s.X, s.Y, s.Z);
                    double numerator = (s.Period / model.M) * (plane + model.H2);
                    double inferredH2 = numerator - plane;
                    return inferredH2 - model.H1;
                }).ToArray();

                if (heights.Length == 0)
                    return new HeightStatistics(0, 0, 0, 0, 0);

                double mean = heights.Average();
                double variance = heights.Average(h => Math.Pow(h - mean, 2));
                double std = Math.Sqrt(variance);
                double min = heights.Min();
                double max = heights.Max();
                double bias = mean - model.DisplayHeight;
                return new HeightStatistics(mean, std, min, max, bias);
            }
        }

        private static List<Sample> ParseSamples(string raw)
        {
            var samples = new List<Sample>();
            using var reader = new StringReader(raw);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length != 8)
                    continue;

                var eyeToken = parts[0].Trim();
                var eye = eyeToken.TrimStart('*');

                double ParseDouble(string text)
                    => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

                samples.Add(new Sample(
                    eye,
                    ParseDouble(parts[1]),
                    ParseDouble(parts[2]),
                    ParseDouble(parts[3]),
                    ParseDouble(parts[4]),
                    ParseDouble(parts[5]),
                    ParseDouble(parts[6]),
                    ParseDouble(parts[7])));
            }

            return samples;
        }

        private static List<Sample> ApplySpatialBinning(IEnumerable<Sample> samples, double binSize)
        {
            var bestByBin = new Dictionary<(string eye, int bx, int by, int bz), Sample>();

            foreach (var sample in samples)
            {
                var bx = (int)Math.Floor(sample.X / binSize);
                var by = (int)Math.Floor(sample.Y / binSize);
                var bz = (int)Math.Floor(sample.Z / binSize);
                var key = (sample.Eye, bx, by, bz);

                if (!bestByBin.TryGetValue(key, out var existing) || sample.Score > existing.Score)
                {
                    bestByBin[key] = sample;
                }
            }

            return bestByBin.Values.ToList();
        }

        private static List<Sample> RemoveLowScoreSamples(IEnumerable<Sample> samples, double fraction, double minScore, out double thresholdScore)
        {
            var sampleList = samples.ToList();
            var indexed = sampleList.Select((sample, index) => (sample, index)).ToList();
            var ordered = indexed.OrderByDescending(pair => pair.sample.Score).ToList();
            int total = ordered.Count;
            int bottomCount = (int)Math.Round(total * fraction, MidpointRounding.AwayFromZero);
            bottomCount = Math.Clamp(bottomCount, 0, total);

            if (bottomCount == 0)
            {
                thresholdScore = ordered[^1].sample.Score;
                return sampleList;
            }

            thresholdScore = ordered[^bottomCount].sample.Score;
            var removalSet = new HashSet<int>();
            for (int i = total - bottomCount; i < total; i++)
            {
                var entry = ordered[i];
                if (entry.sample.Score < minScore)
                {
                    removalSet.Add(entry.index);
                }
            }

            var filtered = sampleList
                .Where((sample, index) => !removalSet.Contains(index))
                .ToList();

            return filtered;
        }

        internal readonly struct Sample
        {
            public Sample(string eye, double x, double y, double z, double period, double bias, double angle, double score)
            {
                Eye = eye;
                X = x;
                Y = y;
                Z = z;
                Period = period;
                Bias = bias;
                Angle = angle;
                Score = score;
            }

            public string Eye { get; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double Period { get; }
            public double Bias { get; }
            public double Angle { get; }
            public double Score { get; }
        }

        internal readonly struct PeriodModel
        {
            public PeriodModel(double a, double b, double c, double h1, double h2, double m, double zBias)
            {
                A = a;
                B = b;
                C = c;
                H1 = h1;
                H2 = h2;
                M = m;
                ZBias = zBias;
                DisplayHeight = h2 - h1;
            }

            public double A { get; }
            public double B { get; }
            public double C { get; }
            public double H1 { get; }
            public double H2 { get; }
            public double M { get; }
            public double ZBias { get; }
            public double DisplayHeight { get; }
            public double BiasOffset => 0.5 * (H1 + H2);

            public double EvaluatePlane(double x, double y, double z) => A * x + B * y + C * (z + ZBias);

            public double ComputePeriod(double x, double y, double z)
            {
                double s = EvaluatePlane(x, y, z);
                double numerator = M * (s + H2);
                double denominator = s + H1;
                if (Math.Abs(denominator) < 1e-6)
                {
                    denominator = denominator >= 0 ? 1e-6 : -1e-6;
                }
                return numerator / denominator;
            }

            public static PeriodModel Fit(IReadOnlyList<Sample> samples)
            {
                if (samples.Count < 3)
                {
                    throw new InvalidOperationException("Need at least three samples to fit period model.");
                }

                double bestRmse = double.PositiveInfinity;
                double bestBias = 0.0;
                PeriodModel bestModel = default;
                bool hasModel = false;

                void EvaluateRange(double min, double max, double step)
                {
                    for (double bias = min; bias <= max + 1e-9; bias += step)
                    {
                        var candidate = FitForZBias(samples, bias);
                        double rmse = ComputeWeightedPeriodRmse(samples, candidate);
                        if (!hasModel || rmse < bestRmse)
                        {
                            hasModel = true;
                            bestRmse = rmse;
                            bestBias = bias;
                            bestModel = candidate;
                        }
                    }
                }

                EvaluateRange(-50.0, 50.0, 1.0);
                double refinedMin = Math.Max(-50.0, bestBias - 1.0);
                double refinedMax = Math.Min(50.0, bestBias + 1.0);
                EvaluateRange(refinedMin, refinedMax, 0.1);

                if (!hasModel)
                {
                    throw new InvalidOperationException("Failed to identify a valid period model.");
                }

                return bestModel;
            }

            private static PeriodModel FitForZBias(IReadOnlyList<Sample> samples, double zBias)
            {
                double a = 0.0;
                double b = 0.0;
                double c = 1.0;
                double bias = 0.0;
                double height = 0.6;
                double m = 5.33;

                OptimizeSixParameter(samples, zBias, ref a, ref b, ref c, ref bias, ref height, ref m);

                double fixedHeight = height;
                OptimizeFiveParameterWithFixedHeight(samples, zBias, fixedHeight, ref a, ref b, ref c, ref bias, ref m);

                double h1 = bias - fixedHeight * 0.5;
                double h2 = bias + fixedHeight * 0.5;
                double normalNorm = Math.Sqrt(a * a + b * b + c * c);
                if (normalNorm > 1e-9)
                {
                    a /= normalNorm;
                    b /= normalNorm;
                    c /= normalNorm;
                    h1 /= normalNorm;
                    h2 /= normalNorm;
                }

                return new PeriodModel(a, b, c, h1, h2, m, zBias);
            }

            private static double ComputeWeightedPeriodRmse(IReadOnlyList<Sample> samples, PeriodModel model)
            {
                double totalWeight = 0.0;
                double weightedError = 0.0;

                foreach (var sample in samples)
                {
                    double predicted = model.ComputePeriod(sample.X, sample.Y, sample.Z);
                    double diff = predicted - sample.Period;
                    double weight = Math.Max(sample.Score, 1e-6);
                    weightedError += weight * diff * diff;
                    totalWeight += weight;
                }

                return totalWeight > 1e-12 ? Math.Sqrt(weightedError / totalWeight) : double.PositiveInfinity;
            }

            private static void OptimizeSixParameter(
                IReadOnlyList<Sample> samples,
                double zBias,
                ref double a,
                ref double b,
                ref double c,
                ref double bias,
                ref double height,
                ref double m)
            {
                double loss = ComputeFullLoss(samples, a, b, c, bias, height, m, zBias);
                double lambda = 1e-3;

                for (int iteration = 0; iteration < 80; iteration++)
                {
                    BuildNormalEquationsFull(samples, a, b, c, bias, height, m, zBias, out var jtJ, out var jtR, out _);

                    bool updated = false;
                    double deltaNorm = 0.0;
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        var system = (double[,])jtJ.Clone();
                        for (int d = 0; d < 6; d++)
                        {
                            system[d, d] += lambda;
                        }

                        var rhs = new double[6];
                        for (int d = 0; d < 6; d++)
                        {
                            rhs[d] = -jtR[d];
                        }

                        var delta = LinearRegression.SolveLinearSystem(system, rhs);
                        if (delta.Any(double.IsNaN) || delta.Any(double.IsInfinity))
                        {
                            lambda *= 10;
                            continue;
                        }

                        double na = a + delta[0];
                        double nb = b + delta[1];
                        double nc = c + delta[2];
                        double nbias = bias + delta[3];
                        double nheight = height + delta[4];
                        double nm = m + delta[5];

                        if (nheight <= 1e-6)
                        {
                            lambda *= 5;
                            continue;
                        }

                        double candidateLoss = ComputeFullLoss(samples, na, nb, nc, nbias, nheight, nm, zBias);
                        if (candidateLoss < loss)
                        {
                            a = na;
                            b = nb;
                            c = nc;
                            bias = nbias;
                            height = nheight;
                            m = nm;
                            loss = candidateLoss;
                            lambda = Math.Max(lambda * 0.3, 1e-6);
                            deltaNorm = Math.Sqrt(delta.Sum(d => d * d));
                            updated = true;
                            break;
                        }

                        lambda *= 5;
                    }

                    if (!updated)
                    {
                        break;
                    }

                    if (deltaNorm < 1e-6)
                    {
                        break;
                    }
                }

            }

            private static void OptimizeFiveParameterWithFixedHeight(
                IReadOnlyList<Sample> samples,
                double zBias,
                double height,
                ref double a,
                ref double b,
                ref double c,
                ref double bias,
                ref double m)
            {
                double loss = ComputeFixedHeightLoss(samples, a, b, c, bias, height, m, zBias);
                double lambda = 1e-3;

                for (int iteration = 0; iteration < 60; iteration++)
                {
                    BuildNormalEquationsFixedHeight(samples, a, b, c, bias, height, m, zBias, out var jtJ, out var jtR, out _);

                    bool updated = false;
                    double deltaNorm = 0.0;
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        var system = (double[,])jtJ.Clone();
                        for (int d = 0; d < 5; d++)
                        {
                            system[d, d] += lambda;
                        }

                        var rhs = new double[5];
                        for (int d = 0; d < 5; d++)
                        {
                            rhs[d] = -jtR[d];
                        }

                        var delta = LinearRegression.SolveLinearSystem(system, rhs);
                        if (delta.Any(double.IsNaN) || delta.Any(double.IsInfinity))
                        {
                            lambda *= 10;
                            continue;
                        }

                        double na = a + delta[0];
                        double nb = b + delta[1];
                        double nc = c + delta[2];
                        double nbias = bias + delta[3];
                        double nm = m + delta[4];

                        double candidateLoss = ComputeFixedHeightLoss(samples, na, nb, nc, nbias, height, nm, zBias);
                        if (candidateLoss < loss)
                        {
                            a = na;
                            b = nb;
                            c = nc;
                            bias = nbias;
                            m = nm;
                            loss = candidateLoss;
                            lambda = Math.Max(lambda * 0.3, 1e-6);
                            deltaNorm = Math.Sqrt(delta.Sum(d => d * d));
                            updated = true;
                            break;
                        }

                        lambda *= 5;
                    }

                    if (!updated)
                    {
                        break;
                    }

                    if (deltaNorm < 1e-6)
                    {
                        break;
                    }
                }
            }

            private static void BuildNormalEquationsFull(
                IReadOnlyList<Sample> samples,
                double a,
                double b,
                double c,
                double bias,
                double height,
                double m,
                double zBias,
                out double[,] jtJ,
                out double[] jtR,
                out double loss)
            {
                jtJ = new double[6, 6];
                jtR = new double[6];
                loss = 0.0;

                double h1 = bias - height * 0.5;
                double h2 = bias + height * 0.5;

                foreach (var sample in samples)
                {
                    double weight = Math.Max(sample.Score, 1e-6);
                    double adjustedZ = sample.Z + zBias;
                    double s = a * sample.X + b * sample.Y + c * adjustedZ;
                    double residual = sample.Period * (s + h1) - m * (s + h2);
                    double wResidual = Math.Sqrt(weight) * residual;
                    loss += wResidual * wResidual;

                    double diff = sample.Period - m;

                    double[] jac =
                    {
                        diff * sample.X,
                        diff * sample.Y,
                        diff * adjustedZ,
                        diff,
                        -0.5 * (sample.Period + m),
                        -(s + h2)
                    };

                    for (int row = 0; row < 6; row++)
                    {
                        double wr = weight * jac[row];
                        jtR[row] += wr * residual;
                        for (int col = row; col < 6; col++)
                        {
                            jtJ[row, col] += wr * jac[col];
                        }
                    }
                }

                AddPrior(jtJ, jtR, ref loss, 0, a, 0.0, 2.0);
                AddPrior(jtJ, jtR, ref loss, 1, b, 0.0, 2.0);
                AddPrior(jtJ, jtR, ref loss, 2, c, 1.0, 2.0);
                AddPrior(jtJ, jtR, ref loss, 3, bias, 0.0, 1.0);
                AddPrior(jtJ, jtR, ref loss, 4, height, 0.598, 5.0);
                AddPrior(jtJ, jtR, ref loss, 5, m, 5.33, 1e-2);

                for (int row = 0; row < 6; row++)
                {
                    for (int col = 0; col < row; col++)
                    {
                        jtJ[row, col] = jtJ[col, row];
                    }
                }
            }

            private static void BuildNormalEquationsFixedHeight(
                IReadOnlyList<Sample> samples,
                double a,
                double b,
                double c,
                double bias,
                double height,
                double m,
                double zBias,
                out double[,] jtJ,
                out double[] jtR,
                out double loss)
            {
                jtJ = new double[5, 5];
                jtR = new double[5];
                loss = 0.0;

                double h1 = bias - height * 0.5;
                double h2 = bias + height * 0.5;

                foreach (var sample in samples)
                {
                    double weight = Math.Max(sample.Score, 1e-6);
                    double adjustedZ = sample.Z + zBias;
                    double s = a * sample.X + b * sample.Y + c * adjustedZ;
                    double residual = sample.Period * (s + h1) - m * (s + h2);
                    double wResidual = Math.Sqrt(weight) * residual;
                    loss += wResidual * wResidual;

                    double diff = sample.Period - m;

                    double[] jac =
                    {
                        diff * sample.X,
                        diff * sample.Y,
                        diff * adjustedZ,
                        diff,
                        -(s + h2)
                    };

                    for (int row = 0; row < 5; row++)
                    {
                        double wr = weight * jac[row];
                        jtR[row] += wr * residual;
                        for (int col = row; col < 5; col++)
                        {
                            jtJ[row, col] += wr * jac[col];
                        }
                    }
                }

                AddPrior(jtJ, jtR, ref loss, 0, a, 0.0, 2.0);
                AddPrior(jtJ, jtR, ref loss, 1, b, 0.0, 2.0);
                AddPrior(jtJ, jtR, ref loss, 2, c, 1.0, 2.0);
                AddPrior(jtJ, jtR, ref loss, 3, bias, 0.0, 1.0);
                AddPrior(jtJ, jtR, ref loss, 4, m, 5.33, 1e-2);

                for (int row = 0; row < 5; row++)
                {
                    for (int col = 0; col < row; col++)
                    {
                        jtJ[row, col] = jtJ[col, row];
                    }
                }
            }

            private static void AddPrior(double[,] jtJ, double[] jtR, ref double loss, int index, double value, double target, double weight)
            {
                double residual = value - target;
                loss += weight * residual * residual;
                jtJ[index, index] += weight;
                jtR[index] += weight * residual;
            }

            private static double ComputeFullLoss(
                IReadOnlyList<Sample> samples,
                double a,
                double b,
                double c,
                double bias,
                double height,
                double m,
                double zBias)
            {
                double total = 0.0;
                double h1 = bias - height * 0.5;
                double h2 = bias + height * 0.5;
                foreach (var sample in samples)
                {
                    double weight = Math.Max(sample.Score, 1e-6);
                    double adjustedZ = sample.Z + zBias;
                    double s = a * sample.X + b * sample.Y + c * adjustedZ;
                    double residual = sample.Period * (s + h1) - m * (s + h2);
                    total += weight * residual * residual;
                }

                total += 2.0 * (a * a + b * b) + 2.0 * Math.Pow(c - 1.0, 2);
                total += 1.0 * Math.Pow(bias, 2);
                total += 5.0 * Math.Pow(height - 0.598, 2);
                total += 1e-2 * Math.Pow(m - 5.33, 2);
                if (height <= 1e-6)
                {
                    total += 1e6 * Math.Pow(1e-6 - height, 2);
                }
                return total;
            }

            private static double ComputeFixedHeightLoss(
                IReadOnlyList<Sample> samples,
                double a,
                double b,
                double c,
                double bias,
                double height,
                double m,
                double zBias)
            {
                double total = 0.0;
                double h1 = bias - height * 0.5;
                double h2 = bias + height * 0.5;
                foreach (var sample in samples)
                {
                    double weight = Math.Max(sample.Score, 1e-6);
                    double adjustedZ = sample.Z + zBias;
                    double s = a * sample.X + b * sample.Y + c * adjustedZ;
                    double residual = sample.Period * (s + h1) - m * (s + h2);
                    total += weight * residual * residual;
                }

                total += 2.0 * (a * a + b * b) + 2.0 * Math.Pow(c - 1.0, 2);
                total += 1.0 * Math.Pow(bias, 2);
                total += 1e-2 * Math.Pow(m - 5.33, 2);
                return total;
            }
        }

        internal readonly struct AngleModel
        {
            public AngleModel(double ax, double by, double cz, double bias, double zBias)
            {
                Ax = ax;
                By = by;
                Cz = cz;
                Bias = bias;
                ZBias = zBias;
            }

            public double Ax { get; }
            public double By { get; }
            public double Cz { get; }
            public double Bias { get; }
            public double ZBias { get; }

            public double ComputeAngle(double x, double y, double z)
                => Ax * x + By * y + Cz * (z + ZBias) + Bias;

            public static AngleModel Fit(IReadOnlyList<Sample> samples, double zBias)
            {
                var design = new double[samples.Count][];
                var targets = new double[samples.Count];

                for (int i = 0; i < samples.Count; i++)
                {
                    var sample = samples[i];
                    var weight = Math.Sqrt(Math.Max(sample.Score, 1e-6));
                    double adjustedZ = sample.Z + zBias;
                    design[i] = new[] { sample.X * weight, sample.Y * weight, adjustedZ * weight, 1.0 * weight };
                    targets[i] = sample.Angle * weight;
                }

                var solution = LinearRegression.Solve(design, targets);
                return new AngleModel(solution[0], solution[1], solution[2], solution[3], zBias);
            }
        }

        internal sealed class BiasModel
        {
            public BiasModel(double scale, double offset, double displayHeight, double zBias)
            {
                Scale = scale;
                Offset = offset;
                DisplayHeight = displayHeight;
                ZBias = zBias;
            }

            public double Scale { get; }
            public double Offset { get; }
            public double DisplayHeight { get; }
            public double ZBias { get; }

            public double ComputeBias(double x, double y, double z, double period, double angle)
            {
                double t1 = ComputeT1(x, y, z, angle, DisplayHeight, ZBias);
                double raw = Scale * t1 + Offset;
                return NormalizeToPeriod(raw, period);
            }

            internal sealed record FitResult(
                BiasModel Model,
                BiasResidual[] Residuals,
                BiasResidual[] TopResiduals,
                PairObservation[] BiasPairs,
                SlopeFitResult SlopeFit,
                ScaleOffsetFitResult ScaleOffsetFit);

            public static FitResult Fit(IReadOnlyList<Sample> samples, PeriodModel periodModel, Action<string>? logger)
            {
                if (samples.Count == 0)
                {
                    throw new InvalidOperationException("No samples available for bias fit.");
                }

                var pairs = BuildPairs(samples);
                if (pairs.Count == 0)
                {
                    logger?.Invoke("Bias pair dataset => none detected.");
                    var zeroModel = new BiasModel(0.0, 0.0, periodModel.DisplayHeight, periodModel.ZBias);
                    return new FitResult(zeroModel, Array.Empty<BiasResidual>(), Array.Empty<BiasResidual>(), Array.Empty<PairObservation>(),
                        new SlopeFitResult(0.0, double.NaN, double.NaN, double.NaN),
                        new ScaleOffsetFitResult(0.0, 0.0, double.NaN));
                }

                var slopeFit = FitSlope(samples, pairs, periodModel);
                double scale = slopeFit.Slope;

                logger?.Invoke("Bias pair dataset (ΔT1 => Δbias):");
                for (int i = 0; i < pairs.Count; i++)
                {
                    var pair = pairs[i];
                    var left = samples[pair.IndexLeft];
                    var right = samples[pair.IndexRight];
                    double leftT1 = ComputeT1(left.X, left.Y, left.Z, left.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double rightT1 = ComputeT1(right.X, right.Y, right.Z, right.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double deltaT1 = WrapToPeriod(rightT1 - leftT1, pair.PeriodAverage);
                    logger?.Invoke(
                        $"  Pair {i:D2}: t1L={leftT1:+0.000000;-0.000000;+0.000000}, t1R={rightT1:+0.000000;-0.000000;+0.000000}, ΔT1={deltaT1:+0.000000;-0.000000;+0.000000}, Δbias={pair.BiasDiff:+0.000000;-0.000000;+0.000000}, weight={pair.Weight:0.000}");
                }

                logger?.Invoke($"Bias pair regression => slope={scale:+0.000000;-0.000000;+0.000000}, MAE: {slopeFit.Mae:F6}, RMSE: {slopeFit.Rmse:F6}, Max: {slopeFit.MaxError:F6}");

                var refined = FindBestScaleAndOffset(samples, periodModel, scale);
                scale = refined.Scale;
                double offset = refined.Offset;
                logger?.Invoke($"Bias scale refinement => scale={scale:+0.000000;-0.000000;+0.000000}, offset={offset:+0.000000;-0.000000;+0.000000}, loss={refined.Loss:F6}");
                logger?.Invoke("Bias offset training samples:");
                for (int i = 0; i < samples.Count; i++)
                {
                    var sample = samples[i];
                    double t1 = ComputeT1(sample.X, sample.Y, sample.Z, sample.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double baseValue = scale * t1;
                    double baseNorm = NormalizeToPeriod(baseValue, sample.Period);
                    double targetBias = NormalizeToPeriod(sample.Bias, sample.Period);
                    double diff = WrapToPeriod(targetBias - baseNorm, sample.Period);
                    double predicted = NormalizeToPeriod(baseNorm + offset, sample.Period);
                    double residual = WrapToPeriod(predicted - targetBias, sample.Period);
                    double weight = Math.Sqrt(Math.Max(sample.Score, 1e-6));
                    logger?.Invoke(
                        $"  Sample {i:D2} {sample.Eye}: score={sample.Score:0.000}, weight={weight:0.000} => T1={t1:+0.000000;-0.000000;+0.000000}, base={baseNorm:+0.000000;-0.000000;+0.000000}, target={targetBias:+0.000000;-0.000000;+0.000000}, diff={diff:+0.000000;-0.000000;+0.000000}, after={residual:+0.000000;-0.000000;+0.000000}");
                }

                var model = new BiasModel(scale, offset, periodModel.DisplayHeight, periodModel.ZBias);

                var residuals = new BiasResidual[samples.Count];
                for (int i = 0; i < samples.Count; i++)
                {
                    var sample = samples[i];
                    double period = periodModel.ComputePeriod(sample.X, sample.Y, sample.Z);
                    double predictedBias = model.ComputeBias(sample.X, sample.Y, sample.Z, period, sample.Angle);
                    double targetBias = NormalizeToPeriod(sample.Bias, sample.Period);
                    double diff = predictedBias - targetBias;
                    double modularError = diff - Math.Round(diff / period) * period;
                    residuals[i] = new BiasResidual(sample, predictedBias, targetBias, modularError);
                }

                double sampleMae = residuals.Average(r => Math.Abs(r.ModularError));
                double sampleRmse = Math.Sqrt(residuals.Average(r => r.ModularError * r.ModularError));
                double sampleMax = residuals.Max(r => Math.Abs(r.ModularError));
                logger?.Invoke($"Bias sample residuals => MAE: {sampleMae:F6}, RMSE: {sampleRmse:F6}, Max: {sampleMax:F6}");

                var topResiduals = residuals
                    .OrderByDescending(r => Math.Abs(r.ModularError))
                    .Take(3)
                    .ToArray();

                return new FitResult(model, residuals, topResiduals, pairs.ToArray(), slopeFit, refined);
            }

            private static double ComputeT1(double x, double y, double z, double angle, double displayHeight, double zBias)
            {
                double adjustedZ = z + zBias;
                double safeZ = Math.Max(Math.Abs(adjustedZ), 1e-6);
                double norm = Math.Sqrt(1.0 + angle * angle);
                double lateral = (x - angle * y) / norm;
                return lateral / safeZ * displayHeight;
            }

            private static SlopeFitResult FitSlope(IReadOnlyList<Sample> samples, IReadOnlyList<PairObservation> pairs, PeriodModel periodModel)
            {
                var design = new double[pairs.Count][];
                var targets = new double[pairs.Count];

                for (int i = 0; i < pairs.Count; i++)
                {
                    var pair = pairs[i];
                    var left = samples[pair.IndexLeft];
                    var right = samples[pair.IndexRight];
                    double leftT1 = ComputeT1(left.X, left.Y, left.Z, left.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double rightT1 = ComputeT1(right.X, right.Y, right.Z, right.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double deltaT1 = WrapToPeriod(rightT1 - leftT1, pair.PeriodAverage);
                    double weight = Math.Sqrt(Math.Max(pair.Weight, 1e-6));
                    design[i] = new[] { deltaT1 * weight };
                    targets[i] = pair.BiasDiff * weight;
                }

                var solution = LinearRegression.Solve(design, targets);
                double slope = solution[0];

                double totalWeight = 0.0;
                double weightedAbs = 0.0;
                double weightedSq = 0.0;
                double maxError = 0.0;

                for (int i = 0; i < pairs.Count; i++)
                {
                    var pair = pairs[i];
                    var left = samples[pair.IndexLeft];
                    var right = samples[pair.IndexRight];
                    double leftT1 = ComputeT1(left.X, left.Y, left.Z, left.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double rightT1 = ComputeT1(right.X, right.Y, right.Z, right.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double deltaT1 = WrapToPeriod(rightT1 - leftT1, pair.PeriodAverage);
                    double predicted = WrapToPeriod(slope * deltaT1, pair.PeriodAverage);
                    double diff = WrapToPeriod(predicted - pair.BiasDiff, pair.PeriodAverage);
                    double weight = pair.Weight;
                    weightedAbs += weight * Math.Abs(diff);
                    weightedSq += weight * diff * diff;
                    totalWeight += weight;
                    maxError = Math.Max(maxError, Math.Abs(diff));
                }

                if (totalWeight < 1e-12)
                {
                    return new SlopeFitResult(0.0, double.NaN, double.NaN, double.NaN);
                }

                double mae = weightedAbs / totalWeight;
                double rmse = Math.Sqrt(weightedSq / totalWeight);
                return new SlopeFitResult(slope, mae, rmse, maxError);
            }

            private static ScaleOffsetFitResult FindBestScaleAndOffset(IReadOnlyList<Sample> samples, PeriodModel periodModel, double initialScale)
            {
                double spread = Math.Max(0.5, Math.Abs(initialScale) * 0.5);
                double minScale = initialScale - spread;
                double maxScale = initialScale + spread;

                var bestResult = new ScaleOffsetFitResult(initialScale, 0.0, double.PositiveInfinity);

                for (int iteration = 0; iteration < 3; iteration++)
                {
                    int steps = iteration == 0 ? 400 : 200;
                    double stepSize = steps > 0 ? (maxScale - minScale) / steps : 0.0;
                    if (Math.Abs(stepSize) < 1e-9)
                    {
                        break;
                    }

                    for (int step = 0; step <= steps; step++)
                    {
                        double candidateScale = minScale + step * stepSize;
                        var offsetResult = FindBestOffset(samples, periodModel, candidateScale);
                        if (offsetResult.Loss < bestResult.Loss)
                        {
                            bestResult = new ScaleOffsetFitResult(candidateScale, offsetResult.Offset, offsetResult.Loss);
                        }
                    }

                    double window = Math.Max((maxScale - minScale) * 0.25, Math.Abs(bestResult.Scale) * 0.1 + 0.05);
                    minScale = bestResult.Scale - window;
                    maxScale = bestResult.Scale + window;
                }

                if (double.IsPositiveInfinity(bestResult.Loss))
                {
                    return new ScaleOffsetFitResult(initialScale, 0.0, double.PositiveInfinity);
                }

                return bestResult;
            }

            private static OffsetFitResult FindBestOffset(IReadOnlyList<Sample> samples, PeriodModel periodModel, double scale)
            {
                if (samples.Count == 0)
                {
                    return new OffsetFitResult(0.0, double.PositiveInfinity);
                }

                double avgPeriod = samples.Average(sample => sample.Period);
                double minOffset = -avgPeriod;
                double maxOffset = avgPeriod;
                double bestOffset = 0.0;
                double bestLoss = double.PositiveInfinity;

                for (int iteration = 0; iteration < 3; iteration++)
                {
                    int steps = iteration == 0 ? 720 : 240;
                    double stepSize = (maxOffset - minOffset) / steps;
                    for (int step = 0; step <= steps; step++)
                    {
                        double candidate = minOffset + step * stepSize;
                        double loss = ComputeOffsetLoss(samples, periodModel, scale, candidate);
                        if (loss < bestLoss)
                        {
                            bestLoss = loss;
                            bestOffset = candidate;
                        }
                    }

                    double window = (maxOffset - minOffset) * 0.25;
                    minOffset = bestOffset - window;
                    maxOffset = bestOffset + window;
                }

                return new OffsetFitResult(bestOffset, bestLoss);
            }

            private static double ComputeOffsetLoss(IReadOnlyList<Sample> samples, PeriodModel periodModel, double scale, double offset)
            {
                double totalWeight = 0.0;
                double weightedLoss = 0.0;

                foreach (var sample in samples)
                {
                    double t1 = ComputeT1(sample.X, sample.Y, sample.Z, sample.Angle, periodModel.DisplayHeight, periodModel.ZBias);
                    double predicted = NormalizeToPeriod(scale * t1 + offset, sample.Period);
                    double target = NormalizeToPeriod(sample.Bias, sample.Period);
                    double diff = WrapToPeriod(predicted - target, sample.Period);
                    double weight = Math.Sqrt(Math.Max(sample.Score, 1e-6));
                    weightedLoss += weight * diff * diff;
                    totalWeight += weight;
                }

                return totalWeight > 1e-12 ? weightedLoss / totalWeight : double.PositiveInfinity;
            }

            internal readonly struct SlopeFitResult
            {
                public SlopeFitResult(double slope, double mae, double rmse, double maxError)
                {
                    Slope = slope;
                    Mae = mae;
                    Rmse = rmse;
                    MaxError = maxError;
                }

                public double Slope { get; }
                public double Mae { get; }
                public double Rmse { get; }
                public double MaxError { get; }
            }

            internal readonly struct OffsetFitResult
            {
                public OffsetFitResult(double offset, double loss)
                {
                    Offset = offset;
                    Loss = loss;
                }

                public double Offset { get; }
                public double Loss { get; }
            }

            internal readonly struct ScaleOffsetFitResult
            {
                public ScaleOffsetFitResult(double scale, double offset, double loss)
                {
                    Scale = scale;
                    Offset = offset;
                    Loss = loss;
                }

                public double Scale { get; }
                public double Offset { get; }
                public double Loss { get; }
            }
        }

        internal readonly struct BiasResidual
        {
            public BiasResidual(Sample sample, double predictedBias, double targetBias, double modularError)
            {
                Sample = sample;
                PredictedBias = predictedBias;
                TargetBias = targetBias;
                ModularError = modularError;
            }

            public Sample Sample { get; }
            public double PredictedBias { get; }
            public double TargetBias { get; }
            public double ModularError { get; }
        }

        internal readonly struct PairObservation
        {
            public PairObservation(int indexLeft, int indexRight, double biasDiff, double weight, double periodAverage)
            {
                IndexLeft = indexLeft;
                IndexRight = indexRight;
                BiasDiff = biasDiff;
                Weight = weight;
                PeriodAverage = periodAverage;
            }

            public int IndexLeft { get; }
            public int IndexRight { get; }
            public double BiasDiff { get; }
            public double Weight { get; }
            public double PeriodAverage { get; }
        }

        private static List<PairObservation> BuildPairs(IReadOnlyList<Sample> samples)
        {
            var list = new List<PairObservation>();
            for (int i = 0; i < samples.Count - 1; i++)
            {
                var left = samples[i];
                var right = samples[i + 1];
                if (!(IsLeft(left.Eye) && IsRight(right.Eye)))
                {
                    continue;
                }

                double scoreThreshold = Math.Min(0.2 * right.Score, 0.3);
                if (left.Score < scoreThreshold || right.Score < scoreThreshold)
                {
                    continue;
                }

                double periodAvg = (left.Period + right.Period) * 0.5;
                double biasLeft = NormalizeToPeriod(left.Bias, periodAvg);
                double biasRight = NormalizeToPeriod(right.Bias, periodAvg);
                double biasDiff = WrapToPeriod(biasRight - biasLeft, periodAvg);
                double weight = Math.Sqrt(Math.Max(left.Score, 1e-6) * Math.Max(right.Score, 1e-6));

                list.Add(new PairObservation(i, i + 1, biasDiff, weight, periodAvg));
            }

            return list;
        }

        private static bool IsLeft(string eye) => string.Equals(NormalizeEye(eye), "L", StringComparison.OrdinalIgnoreCase);
        private static bool IsRight(string eye) => string.Equals(NormalizeEye(eye), "R", StringComparison.OrdinalIgnoreCase);
        private static string NormalizeEye(string eye) => eye.TrimStart('*');

        internal readonly struct CalibrationParameters
        {
            public CalibrationParameters(PeriodModel period, AngleModel angle, BiasModel bias)
            {
                Period = period;
                Angle = angle;
                Bias = bias;
            }

            public PeriodModel Period { get; }
            public AngleModel Angle { get; }
            public BiasModel Bias { get; }

            public Prediction Predict(double x, double y, double z)
            {
                var period = Period.ComputePeriod(x, y, z);
                var angle = Angle.ComputeAngle(x, y, z);
                var bias = Bias.ComputeBias(x, y, z, period, angle);
                return new Prediction(period, bias, angle);
            }
        }

        internal readonly struct Prediction
        {
            public Prediction(double period, double bias, double angle)
            {
                Period = period;
                Bias = bias;
                Angle = angle;
            }

            public double Period { get; }
            public double Bias { get; }
            public double Angle { get; }
        }

        internal static double NormalizeToPeriod(double value, double period)
        {
            var wrapped = value % period;
            if (wrapped < 0)
            {
                wrapped += period;
            }
            return wrapped;
        }

        internal static double WrapToPeriod(double value, double period)
        {
            if (period < 1e-6)
            {
                return 0.0;
            }

            double wrapped = value - Math.Round(value / period) * period;
            if (wrapped > period * 0.5)
            {
                wrapped -= period;
            }
            else if (wrapped < -period * 0.5)
            {
                wrapped += period;
            }

            return wrapped;
        }

        private static class LinearRegression
        {
            public static double[] Solve(double[][] design, double[] targets)
            {
                int rows = design.Length;
                if (rows == 0)
                {
                    throw new InvalidOperationException("No data to fit.");
                }

                int cols = design[0].Length;
                var ata = new double[cols, cols];
                var atb = new double[cols];

                for (int r = 0; r < rows; r++)
                {
                    var row = design[r];
                    double target = targets[r];
                    for (int i = 0; i < cols; i++)
                    {
                        atb[i] += row[i] * target;
                        for (int j = 0; j < cols; j++)
                        {
                            ata[i, j] += row[i] * row[j];
                        }
                    }
                }

                return SolveLinearSystem(ata, atb);
            }

            public static double[] SolveLinearSystem(double[,] matrix, double[] vector)
            {
                int n = vector.Length;
                var augmented = new double[n, n + 1];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        augmented[i, j] = matrix[i, j];
                    }

                    augmented[i, n] = vector[i];
                }

                for (int pivot = 0; pivot < n; pivot++)
                {
                    int bestRow = pivot;
                    double bestVal = Math.Abs(augmented[pivot, pivot]);
                    for (int row = pivot + 1; row < n; row++)
                    {
                        double val = Math.Abs(augmented[row, pivot]);
                        if (val > bestVal)
                        {
                            bestVal = val;
                            bestRow = row;
                        }
                    }

                    if (bestVal < 1e-9)
                    {
                        throw new InvalidOperationException("Singular matrix encountered while solving regression.");
                    }

                    if (bestRow != pivot)
                    {
                        SwapRows(augmented, pivot, bestRow);
                    }

                    NormalizeRow(augmented, pivot, pivot);

                    for (int row = 0; row < n; row++)
                    {
                        if (row == pivot)
                        {
                            continue;
                        }

                        double factor = augmented[row, pivot];
                        if (Math.Abs(factor) < 1e-12)
                        {
                            continue;
                        }

                        for (int col = pivot; col <= n; col++)
                        {
                            augmented[row, col] -= factor * augmented[pivot, col];
                        }
                    }
                }

                var solution = new double[n];
                for (int i = 0; i < n; i++)
                {
                    solution[i] = augmented[i, n];
                }

                return solution;
            }

            private static void SwapRows(double[,] matrix, int a, int b)
            {
                if (a == b)
                {
                    return;
                }

                int columns = matrix.GetLength(1);
                for (int col = 0; col < columns; col++)
                {
                    (matrix[a, col], matrix[b, col]) = (matrix[b, col], matrix[a, col]);
                }
            }

            private static void NormalizeRow(double[,] matrix, int row, int pivotColumn)
            {
                double pivotValue = matrix[row, pivotColumn];
                int columns = matrix.GetLength(1);
                for (int col = pivotColumn; col < columns; col++)
                {
                    matrix[row, col] /= pivotValue;
                }
            }
        }
    }
}


