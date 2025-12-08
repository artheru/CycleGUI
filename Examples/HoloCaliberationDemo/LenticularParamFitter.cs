using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HoloCaliberationDemo
{
    internal static class LenticularParamFitter
    {
        private const double BinSizeMillimeters = 20.0;

        public static FitResult FitFromFile(string path, double zSearchStart, double zSearchEnd, Action<string>? logger = null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}", path);

            var raw = File.ReadAllText(path);
            return FitFromRaw(raw, zSearchStart, zSearchEnd, logger);
        }

        public static FitResult FitFromRaw(string rawSamples, double zSearchStart, double zSearchEnd, Action<string>? logger = null)
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

            var periodModel = PeriodModel.Fit(filteredSamples, zSearchStart, zSearchEnd, logger);
            logger?.Invoke(
                $"Period model => h1: {periodModel.H1:F6}, h2: {periodModel.H2:F6}, m: {periodModel.M:F6}, display height: {periodModel.DisplayHeight:F6}");
            var normalMagnitude = Math.Sqrt(
                periodModel.A * periodModel.A + periodModel.B * periodModel.B + periodModel.C * periodModel.C);
            logger?.Invoke(
                $"Period plane coefficients => a: {periodModel.A:F6}, b: {periodModel.B:F6}, c: {periodModel.C:F6} (|n|={normalMagnitude:F6})");
            logger?.Invoke($"Period Z bias => {periodModel.ZBias:F3}");

            var angleModel = AngleModel.Fit(filteredSamples, periodModel.ZBias);

            var biasFit = BiasModel.Fit(filteredSamples, periodModel, logger);
            var biasModel = biasFit.Model;

            logger?.Invoke(
                $"Bias model => bias = NormalizeToPeriod({biasModel.Scale:+0.000000;-0.000000;+0.000000} * T1(x,y,z,angle) + {biasModel.Offset:+0.000000;-0.000000;+0.000000}), where T1 = ((x - angle*y)/sqrt(1+angle^2)) * {biasModel.DisplayHeight:F6} / abs(z + {biasModel.ZBias:F6})");

            var calibration = new CalibrationParameters(periodModel, angleModel, biasModel);

            var biasResiduals = biasFit.Residuals;
            var sampleResiduals = new List<SampleResidual>(filteredSamples.Count);
            for (int i = 0; i < filteredSamples.Count; i++)
            {
                var sample = filteredSamples[i];
                double periodResidual = periodModel.ComputePeriod(sample.X, sample.Y, sample.Z) - sample.Period;
                double angleResidual = angleModel.ComputeAngle(sample.X, sample.Y, sample.Z) - sample.Angle;
                double biasResidual = i < biasResiduals.Length ? biasResiduals[i].ModularError : 0.0;

                sampleResiduals.Add(new SampleResidual(sample, periodResidual, angleResidual, biasResidual));
            }

            var periodResiduals = sampleResiduals.Select(r => r.PeriodResidual).ToArray();
            var periodStats = ResidualStatistics.Compute(periodResiduals);
            logger?.Invoke($"All period residuals => MAE: {periodStats.MAE:F6}, RMSE: {periodStats.RMSE:F6}, Max: {periodStats.MaxAbsolute:F6}");

            var angleResiduals = sampleResiduals.Select(r => r.AngleResidual).ToArray();
            var angleStats = ResidualStatistics.Compute(angleResiduals);
            logger?.Invoke($"All angle residuals => MAE: {angleStats.MAE:F6}, RMSE: {angleStats.RMSE:F6}, Max: {angleStats.MaxAbsolute:F6}");

            var biasResidualValues = sampleResiduals.Select(r => r.BiasResidual).ToArray();
            var biasStats = ResidualStatistics.Compute(biasResidualValues);
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
                // periodModel,
                // angleModel,
                // biasModel,
                calibration,
                sampleResiduals,
                periodStats,
                angleStats,
                biasStats);
        }

        internal sealed record FitResult(
            // PeriodModel PeriodModel,
            // AngleModel AngleModel,
            // BiasModel BiasModel,
            CalibrationParameters Calibration,
            IReadOnlyList<SampleResidual> SampleResiduals,
            ResidualStatistics PeriodStats,
            ResidualStatistics AngleStats,
            ResidualStatistics BiasStats)
        {
            public Prediction PredictWithSample(float x, float y, float z, float sigma)
            {
                var basePrediction = Calibration.Predict(x, y, z);

                if (SampleResiduals.Count == 0 || float.IsNaN(sigma) || float.IsInfinity(sigma))
                {
                    return basePrediction;
                }

                double sigmaValue = Math.Clamp(Math.Abs((double)sigma), 0.0, 1.0);
                if (sigmaValue < 1e-6)
                {
                    return basePrediction;
                }

                double queryX = x;
                double queryY = y;
                double queryZ = z;

                Span<OctantEntry> octants = new OctantEntry[8];
                for (int i = 0; i < octants.Length; i++)
                {
                    octants[i] = OctantEntry.Empty;
                }

                int populated = 0;
                foreach (var residual in SampleResiduals)
                {
                    var sample = residual.Sample;
                    double dx = sample.X - queryX;
                    double dy = sample.Y - queryY;
                    double dz = sample.Z - queryZ;
                    double distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq < 1e-12)
                    {
                        return new Prediction(
                            basePrediction.Period - sigmaValue * residual.PeriodResidual,
                            basePrediction.Bias - sigmaValue * residual.BiasResidual,
                            basePrediction.Angle - sigmaValue * residual.AngleResidual);
                    }

                    int sx = dx >= 0 ? 1 : 0;
                    int sy = dy >= 0 ? 1 : 0;
                    int sz = dz >= 0 ? 1 : 0;
                    int index = (sx << 2) | (sy << 1) | sz;

                    ref var entry = ref octants[index];
                    var candidate = OctantEntry.From(index, residual, dx, dy, dz, distSq);
                    if (!entry.HasValue)
                    {
                        entry = candidate;
                        populated++;
                    }
                    else if (distSq < entry.DistanceSquared)
                    {
                        entry = candidate;
                    }
                }

                if (populated == 0)
                {
                    return basePrediction;
                }

                EnsureOctantsPopulated(octants);

                bool hasPositiveX = false, hasNegativeX = false;
                bool hasPositiveY = false, hasNegativeY = false;
                bool hasPositiveZ = false, hasNegativeZ = false;

                double posX = double.PositiveInfinity, negX = double.PositiveInfinity;
                double posY = double.PositiveInfinity, negY = double.PositiveInfinity;
                double posZ = double.PositiveInfinity, negZ = double.PositiveInfinity;

                foreach (ref readonly var entry in octants)
                {
                    double absDx = Math.Abs(entry.DeltaX);
                    double absDy = Math.Abs(entry.DeltaY);
                    double absDz = Math.Abs(entry.DeltaZ);

                    if (entry.IsPositiveX)
                    {
                        hasPositiveX = true;
                        posX = Math.Min(posX, absDx);
                    }
                    else
                    {
                        hasNegativeX = true;
                        negX = Math.Min(negX, absDx);
                    }

                    if (entry.IsPositiveY)
                    {
                        hasPositiveY = true;
                        posY = Math.Min(posY, absDy);
                    }
                    else
                    {
                        hasNegativeY = true;
                        negY = Math.Min(negY, absDy);
                    }

                    if (entry.IsPositiveZ)
                    {
                        hasPositiveZ = true;
                        posZ = Math.Min(posZ, absDz);
                    }
                    else
                    {
                        hasNegativeZ = true;
                        negZ = Math.Min(negZ, absDz);
                    }
                }

                if (!hasPositiveX) posX = negX;
                if (!hasNegativeX) negX = posX;
                if (!hasPositiveY) posY = negY;
                if (!hasNegativeY) negY = posY;
                if (!hasPositiveZ) posZ = negZ;
                if (!hasNegativeZ) negZ = posZ;

                double tx = ComputeAxisWeight(hasNegativeX, negX, hasPositiveX, posX);
                double ty = ComputeAxisWeight(hasNegativeY, negY, hasPositiveY, posY);
                double tz = ComputeAxisWeight(hasNegativeZ, negZ, hasPositiveZ, posZ);

                double totalWeight = 0.0;
                double periodSum = 0.0;
                double angleSum = 0.0;
                double biasSum = 0.0;

                foreach (ref readonly var entry in octants)
                {
                    double wx = entry.IsPositiveX ? tx : 1.0 - tx;
                    double wy = entry.IsPositiveY ? ty : 1.0 - ty;
                    double wz = entry.IsPositiveZ ? tz : 1.0 - tz;
                    double weight = wx * wy * wz;

                    totalWeight += weight;
                    periodSum += weight * entry.Residual.PeriodResidual;
                    angleSum += weight * entry.Residual.AngleResidual;
                    biasSum += weight * entry.Residual.BiasResidual;
                }

                double periodAdjustment;
                double angleAdjustment;
                double biasAdjustment;

                if (totalWeight > 1e-12)
                {
                    double invTotal = 1.0 / totalWeight;
                    periodAdjustment = periodSum * invTotal;
                    angleAdjustment = angleSum * invTotal;
                    biasAdjustment = biasSum * invTotal;
                }
                else
                {
                    (periodAdjustment, angleAdjustment, biasAdjustment) = ComputeInverseDistanceAdjustments(octants);
                }

                periodAdjustment *= sigmaValue;
                angleAdjustment *= sigmaValue;
                biasAdjustment *= sigmaValue;

                return new Prediction(
                    basePrediction.Period - periodAdjustment,
                    basePrediction.Bias - biasAdjustment,
                    basePrediction.Angle - angleAdjustment);
            }

            private static void EnsureOctantsPopulated(Span<OctantEntry> octants)
            {
                Span<int> available = stackalloc int[8];
                int availableCount = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (octants[i].HasValue)
                    {
                        available[availableCount++] = i;
                    }
                }

                if (availableCount == 0)
                {
                    return;
                }

                for (int i = 0; i < 8; i++)
                {
                    if (octants[i].HasValue)
                    {
                        continue;
                    }

                    int bestIndex = available[0];
                    int bestDiff = CountBitDifferences(bestIndex, i);
                    double bestDistance = octants[bestIndex].DistanceSquared;

                    for (int j = 1; j < availableCount; j++)
                    {
                        int candidateIndex = available[j];
                        int diff = CountBitDifferences(candidateIndex, i);
                        double distance = octants[candidateIndex].DistanceSquared;

                        if (diff < bestDiff || (diff == bestDiff && distance < bestDistance))
                        {
                            bestDiff = diff;
                            bestDistance = distance;
                            bestIndex = candidateIndex;
                        }
                    }

                    octants[i] = octants[bestIndex].WithIndex(i);
                }
            }

            private static int CountBitDifferences(int a, int b)
            {
                int diff = a ^ b;
                int count = 0;
                while (diff != 0)
                {
                    count += diff & 1;
                    diff >>= 1;
                }

                return count;
            }

            private static (double period, double angle, double bias) ComputeInverseDistanceAdjustments(
                ReadOnlySpan<OctantEntry> entries)
            {
                double totalWeight = 0.0;
                double periodSum = 0.0;
                double angleSum = 0.0;
                double biasSum = 0.0;

                foreach (ref readonly var entry in entries)
                {
                    double distance = Math.Sqrt(entry.DistanceSquared);
                    double weight = distance < 1e-6 ? double.PositiveInfinity : 1.0 / Math.Pow(distance, 3.0);

                    if (double.IsPositiveInfinity(weight))
                    {
                        return (
                            entry.Residual.PeriodResidual,
                            entry.Residual.AngleResidual,
                            entry.Residual.BiasResidual);
                    }

                    totalWeight += weight;
                    periodSum += weight * entry.Residual.PeriodResidual;
                    angleSum += weight * entry.Residual.AngleResidual;
                    biasSum += weight * entry.Residual.BiasResidual;
                }

                if (totalWeight <= 1e-12)
                {
                    return (0.0, 0.0, 0.0);
                }

                double inv = 1.0 / totalWeight;
                return (periodSum * inv, angleSum * inv, biasSum * inv);
            }

            private static double ComputeAxisWeight(
                bool hasNegative,
                double negativeDistance,
                bool hasPositive,
                double positiveDistance)
            {
                if (hasNegative && hasPositive)
                {
                    double denom = negativeDistance + positiveDistance;
                    if (denom < 1e-9)
                    {
                        return 0.5;
                    }

                    return negativeDistance / denom;
                }

                if (hasNegative)
                {
                    return 0.0;
                }

                if (hasPositive)
                {
                    return 1.0;
                }

                return 0.5;
            }

            private readonly struct OctantEntry
            {
                private OctantEntry(
                    int index,
                    SampleResidual residual,
                    double dx,
                    double dy,
                    double dz,
                    double distanceSquared)
                {
                    OctantIndex = index;
                    Residual = residual;
                    DeltaX = dx;
                    DeltaY = dy;
                    DeltaZ = dz;
                    DistanceSquared = distanceSquared;
                    HasValue = true;
                }

                public static OctantEntry Empty => default;

                public static OctantEntry From(
                    int index,
                    SampleResidual residual,
                    double dx,
                    double dy,
                    double dz,
                    double distanceSquared)
                    => new(index, residual, dx, dy, dz, distanceSquared);

                public OctantEntry WithIndex(int newIndex)
                {
                    double absX = Math.Abs(DeltaX);
                    double absY = Math.Abs(DeltaY);
                    double absZ = Math.Abs(DeltaZ);

                    double signedX = (newIndex & 0b100) != 0 ? absX : -absX;
                    double signedY = (newIndex & 0b010) != 0 ? absY : -absY;
                    double signedZ = (newIndex & 0b001) != 0 ? absZ : -absZ;

                    return new OctantEntry(newIndex, Residual, signedX, signedY, signedZ, DistanceSquared);
                }

                public bool HasValue { get; }
                public int OctantIndex { get; }
                public SampleResidual Residual { get; }
                public double DeltaX { get; }
                public double DeltaY { get; }
                public double DeltaZ { get; }
                public double DistanceSquared { get; }

                public bool IsPositiveX => (OctantIndex & 0b100) != 0;
                public bool IsPositiveY => (OctantIndex & 0b010) != 0;
                public bool IsPositiveZ => (OctantIndex & 0b001) != 0;
            }
        }

        internal readonly struct ResidualStatistics
        {
            [JsonConstructor]
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
                var heights = samples
                    .Select(s =>
                    {
                        double adjustedZ = s.Z + model.ZBias;
                        if (Math.Abs(adjustedZ) < 1e-6 || Math.Abs(model.M) < 1e-9)
                        {
                            return double.NaN;
                        }

                        double ratio = s.Period / model.M - 1.0;
                        double inferredHeight = ratio * adjustedZ;
                        return inferredHeight;
                    })
                    .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                    .ToArray();

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
                if (parts.Length < 8)
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
            [JsonConstructor]
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

        internal readonly struct SampleResidual
        {
            [JsonConstructor]
            public SampleResidual(Sample sample, double periodResidual, double angleResidual, double biasResidual)
            {
                Sample = sample;
                PeriodResidual = periodResidual;
                AngleResidual = angleResidual;
                BiasResidual = biasResidual;
            }

            public Sample Sample { get; }
            public double PeriodResidual { get; }
            public double AngleResidual { get; }
            public double BiasResidual { get; }
        }

        internal struct PeriodModel
        {
            [JsonConstructor]
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

            public double A;
            public double B;
            public double C;
            public double H1;
            public double H2;
            public double M;
            public double ZBias;
            public double DisplayHeight;

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

            public static PeriodModel Fit(
                IReadOnlyList<Sample> samples,
                double zSearchStart,
                double zSearchEnd,
                Action<string>? logger)
            {
                if (samples.Count < 3)
                {
                    throw new InvalidOperationException("Need at least three samples to fit period model.");
                }

                if (double.IsNaN(zSearchStart) || double.IsInfinity(zSearchStart))
                {
                    throw new ArgumentOutOfRangeException(nameof(zSearchStart), "Search start must be finite.");
                }

                if (double.IsNaN(zSearchEnd) || double.IsInfinity(zSearchEnd))
                {
                    throw new ArgumentOutOfRangeException(nameof(zSearchEnd), "Search end must be finite.");
                }

                double searchMin = Math.Min(zSearchStart, zSearchEnd);
                double searchMax = Math.Max(zSearchStart, zSearchEnd);

                if (Math.Abs(searchMax - searchMin) < 1e-6)
                {
                    var (singleModel, _) = FitForZBias(samples, searchMin, verbose: logger != null, logger);
                    return singleModel;
                }

                double step = 1.0;
                int totalSteps = (int)Math.Max(1, Math.Ceiling((searchMax - searchMin) / step)) + 1;
                Log(logger, $"[PeriodFit] Searching zBias from {searchMin:F1} to {searchMax:F1} with {step:F1}mm steps ({totalSteps} candidates)...");

                bool hasBest = false;
                double bestZBias = searchMin;
                PeriodModel bestModel = default;
                FitEvaluation bestEvaluation = default;

                for (int i = 0; i < totalSteps; i++)
                {
                    double zBias = searchMin + i * step;
                    if (zBias > searchMax)
                    {
                        zBias = searchMax;
                    }

                    var (candidate, evaluation) = FitForZBias(samples, zBias, verbose: false, logger: null);

                    if (!hasBest || evaluation.Rmse < bestEvaluation.Rmse)
                    {
                        hasBest = true;
                        bestZBias = zBias;
                        bestModel = candidate;
                        bestEvaluation = evaluation;
                    }

                    if ((i + 1) % 100 == 0 || i + 1 == totalSteps)
                    {
                        Log(logger, $"[PeriodFit] Progress: {i + 1}/{totalSteps}, current best zBias: {bestZBias:F1}, RMSE: {bestEvaluation.Rmse:F6}");
                    }
                }

                if (!hasBest)
                {
                    throw new InvalidOperationException("Failed to find a valid model during zBias search.");
                }

                Log(logger, $"[PeriodFit] Search complete. Best zBias: {bestZBias:F1}, RMSE: {bestEvaluation.Rmse:F6}");

                var (finalModel, _) = FitForZBias(samples, bestZBias, verbose: logger != null, logger);
                return finalModel;
            }

            private static (PeriodModel Model, FitEvaluation Evaluation) FitForZBias(
                IReadOnlyList<Sample> samples,
                double zBias,
                bool verbose,
                Action<string>? logger)
            {
                double totalSamples = samples.Count;
                double m = samples.Sum(s => s.Period) / totalSamples;
                double h = ComputeInitialH(samples, m, zBias);

                var evaluation = Evaluate(samples, zBias, h, m);
                if (verbose)
                {
                    Log(logger,
                        $"[PeriodFit] init => rmse: {evaluation.Rmse:F6}, mae: {evaluation.Mae:F6}, max: {evaluation.MaxError:F6}, grad: {evaluation.GradientNorm:E3}");
                }

                double lambda = 1e-3;

                for (int iteration = 0; iteration < 80; iteration++)
                {
                    if (evaluation.GradientNorm < 1e-9)
                    {
                        if (verbose)
                        {
                            Log(logger, $"[PeriodFit] stop @ iter {iteration:D2} (gradient norm {evaluation.GradientNorm:E3}).");
                        }
                        break;
                    }

                    double previousLoss = evaluation.Loss;

                    var system = (double[,])evaluation.JtJ.Clone();
                    for (int i = 0; i < 2; i++)
                    {
                        system[i, i] += lambda;
                    }

                    var rhs = new double[2];
                    for (int i = 0; i < 2; i++)
                    {
                        rhs[i] = -evaluation.JtResidual[i];
                        }

                        var delta = LinearRegression.SolveLinearSystem(system, rhs);
                        if (delta.Any(double.IsNaN) || delta.Any(double.IsInfinity))
                        {
                        lambda *= 10.0;
                            continue;
                        }

                    double nextH = h + delta[0];
                    double nextM = m + delta[1];

                    if (double.IsNaN(nextM) || double.IsInfinity(nextM) || Math.Abs(nextM) < 1e-6)
                    {
                        lambda *= 4.0;
                        if (lambda > 1e9)
                        {
                            if (verbose)
                            {
                                Log(logger, "[PeriodFit] λ grew too large, stopping.");
                            }
                        break;
                        }

                        continue;
                    }

                    var candidateEval = Evaluate(samples, zBias, nextH, nextM);

                    if (candidateEval.Loss < evaluation.Loss)
                    {
                        h = nextH;
                        m = nextM;
                        evaluation = candidateEval;
                        lambda = Math.Max(lambda * 0.3, 1e-6);

                        double relativeImprovement = Math.Abs(previousLoss - evaluation.Loss) / Math.Max(previousLoss, 1e-12);
                        if (evaluation.GradientNorm < 1e-7 || relativeImprovement < 1e-6)
                        {
                            if (verbose)
                            {
                                Log(logger,
                                    $"[PeriodFit] stop @ iter {iteration:D2} (grad {evaluation.GradientNorm:E3}, Δloss {relativeImprovement:E3}).");
                            }
                            break;
                        }

                            continue;
                        }

                    lambda *= 4.0;
                    if (lambda > 1e9)
                    {
                        if (verbose)
                        {
                            Log(logger, "[PeriodFit] λ grew too large, stopping.");
                        }
                            break;
                        }
                }

                if (verbose)
                {
                    Log(logger,
                        $"[PeriodFit] final => rmse: {evaluation.Rmse:F6}, mae: {evaluation.Mae:F6}, max: {evaluation.MaxError:F6}");
                    LogWorstResiduals(samples, zBias, h, m, logger);
                }

                var model = BuildModel(h, m, zBias);
                return (model, evaluation);
            }

            private static PeriodModel BuildModel(double h, double m, double zBias)
            {
                double a = 0.0;
                double b = 0.0;
                double c = 1.0;
                double h1 = 0.0;
                double h2 = h;
                return new PeriodModel(a, b, c, h1, h2, m, zBias);
            }

            private static FitEvaluation Evaluate(
                IReadOnlyList<Sample> samples,
                double zBias,
                double h,
                double m)
            {
                double totalWeight = 0.0;
                double weightedLoss = 0.0;
                double weightedAbs = 0.0;
                double maxError = 0.0;
                var gradient = new double[2];
                var jtJ = new double[2, 2];
                var jtResidual = new double[2];

                foreach (var sample in samples)
                {
                    double weight = Math.Max(sample.Score, 1e-6);
                    double sqrtWeight = Math.Sqrt(weight);

                    double z1 = sample.Z + zBias;
                    double safeZ1 = Math.Abs(z1) < 1e-9 ? (z1 >= 0 ? 1e-9 : -1e-9) : z1;

                    double predicted = m * (1.0 + h / safeZ1);
                    double error = predicted - sample.Period;

                    double residual = sqrtWeight * error;
                    double invZ1 = 1.0 / safeZ1;

                    double jacH = sqrtWeight * (m * invZ1);
                    double jacM = sqrtWeight * (1.0 + h * invZ1);

                    jtResidual[0] += jacH * residual;
                    jtResidual[1] += jacM * residual;

                    jtJ[0, 0] += jacH * jacH;
                    jtJ[0, 1] += jacH * jacM;
                    jtJ[1, 1] += jacM * jacM;

                    weightedLoss += residual * residual;
                    weightedAbs += weight * Math.Abs(error);
                    maxError = Math.Max(maxError, Math.Abs(error));
                    totalWeight += weight;
                }

                if (totalWeight <= 0)
                {
                    return new FitEvaluation(
                        double.PositiveInfinity,
                        double.PositiveInfinity,
                        double.PositiveInfinity,
                        double.PositiveInfinity,
                        gradient,
                        0.0,
                        jtJ,
                        jtResidual);
                }

                jtJ[1, 0] = jtJ[0, 1];

                gradient[0] = 2.0 * jtResidual[0];
                gradient[1] = 2.0 * jtResidual[1];

                double rmse = Math.Sqrt(weightedLoss / totalWeight);
                double mae = weightedAbs / totalWeight;
                double gradientNorm = Math.Sqrt(gradient.Sum(value => value * value));

                return new FitEvaluation(weightedLoss, rmse, mae, maxError, gradient, gradientNorm, jtJ, jtResidual);
            }

            private static double ComputeInitialH(IReadOnlyList<Sample> samples, double m, double zBias)
            {
                double count = 0.0;
                double sum = 0.0;

                foreach (var sample in samples)
                {
                    double z1 = sample.Z + zBias;
                    if (Math.Abs(z1) < 1e-6 || Math.Abs(m) < 1e-9)
                    {
                        continue;
                    }

                    double ratio = sample.Period / m - 1.0;
                    sum += z1 * ratio;
                    count += 1.0;
                }

                if (count <= 0.0)
                {
                    return 1.0;
                }

                double initial = sum / count;
                if (double.IsNaN(initial) || double.IsInfinity(initial))
                {
                    return 1.0;
                }

                return Math.Clamp(initial, -200.0, 200.0);
            }

            private static void LogWorstResiduals(
                IReadOnlyList<Sample> samples,
                double zBias,
                double h,
                double m,
                Action<string>? logger)
            {
                var residuals = samples
                    .Select((sample, index) =>
                    {
                        double z1 = sample.Z + zBias;
                        double safeZ1 = Math.Abs(z1) < 1e-9 ? (z1 >= 0 ? 1e-9 : -1e-9) : z1;
                        double predicted = m * (1.0 + h / safeZ1);
                        double error = predicted - sample.Period;
                        return (sample, index, predicted, error);
                    })
                    .OrderByDescending(entry => Math.Abs(entry.error))
                    .Take(5)
                    .ToArray();

                if (residuals.Length == 0)
                {
                    Log(logger, "[PeriodFit] no residuals to report.");
                    return;
                }

                Log(logger, "[PeriodFit] worst residuals:");
                foreach (var entry in residuals)
                {
                    Log(logger,
                        $"  #{entry.index:D2} {entry.sample.Eye}: target={entry.sample.Period:F6}, pred={entry.predicted:F6}, error={entry.error:+0.000000;-0.000000;+0.000000}");
                }
            }

            private static void Log(Action<string>? logger, string message)
            {
                logger?.Invoke(message);
            }

            private readonly struct FitEvaluation
            {
                public FitEvaluation(
                    double loss,
                    double rmse,
                    double mae,
                    double maxError,
                    double[] gradient,
                    double gradientNorm,
                    double[,] jtJ,
                    double[] jtResidual)
                {
                    Loss = loss;
                    Rmse = rmse;
                    Mae = mae;
                    MaxError = maxError;
                    Gradient = gradient;
                    GradientNorm = gradientNorm;
                    JtJ = jtJ;
                    JtResidual = jtResidual;
                }

                public double Loss { get; }
                public double Rmse { get; }
                public double Mae { get; }
                public double MaxError { get; }
                public double[] Gradient { get; }
                public double GradientNorm { get; }
                public double[,] JtJ { get; }
                public double[] JtResidual { get; }
            }
        }

        internal readonly struct AngleModel
        {
            [JsonConstructor]
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

        internal class BiasModel
        {
            [JsonConstructor]
            public BiasModel(double scale, double offset, double displayHeight, double zBias)
            {
                Scale = (float)scale;
                Offset = (float)offset;
                DisplayHeight = displayHeight;
                ZBias = (float)zBias;
            }

            public float Scale;
            public float Offset;
            public double DisplayHeight { get; }
            public float ZBias;

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

        internal struct CalibrationParameters
        {
            [JsonConstructor]
            public CalibrationParameters(PeriodModel period, AngleModel angle, BiasModel bias)
            {
                Period = period;
                Angle = angle;
                Bias = bias;
            }

            public PeriodModel Period;
            public AngleModel Angle;
            public BiasModel Bias;

            public Prediction Predict(double x, double y, double z)
            {
                var period = Period.ComputePeriod(x, y, z);
                var angle = Angle.ComputeAngle(x, y, z);
                var bias = Bias.ComputeBias(x, y, z, period, angle);
                return new Prediction(period, bias, angle);
            }
        }

        internal struct Prediction(double period, double bias, double angle)
        {
            public bool obsolete = false;

            public double Period = period;
            public double Bias = bias;
            public double Angle = angle;
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


