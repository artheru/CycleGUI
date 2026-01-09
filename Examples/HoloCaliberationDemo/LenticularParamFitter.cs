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

        /// <summary>
        /// Compute T1 parameter: normalized lateral position relative to distance.
        /// T1 = ((x - angle*y) / sqrt(1+angle^2)) * displayHeight / |z + zBias|
        /// </summary>
        internal static double ComputeT1(double x, double y, double z, double angle, double displayHeight, double zBias)
        {
            double adjustedZ = z + zBias;
            double safeZ = Math.Max(Math.Abs(adjustedZ), 1e-6);
            double norm = Math.Sqrt(1.0 + angle * angle);
            double lateral = (x - angle * y) / norm;
            return lateral / safeZ * displayHeight;
        }

        public static FitResult FitFromFile(string path, double zBias, Action<string>? logger = null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}", path);

            var raw = File.ReadAllText(path);
            return FitFromRaw(raw, zBias, logger);
        }

        public static FitResult FitFromRaw(string rawSamples, double zBias, Action<string>? logger = null)
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

            logger?.Invoke($"Using pre-calibrated zBias = {zBias}");
            var periodModel = PeriodModel.Fit(filteredSamples, zBias, logger);
            logger?.Invoke(
                $"Period model => M: {periodModel.M:F6}, DisplayHeight: {periodModel.DisplayHeight:F6}, ZBias: {periodModel.ZBias:F3}");
            logger?.Invoke(
                $"Period formula => period = {periodModel.M:F6} * (1 + {periodModel.DisplayHeight:F6} / (z + {periodModel.ZBias:F3}))");

            var angleModel = AngleModel.Fit(filteredSamples, periodModel.ZBias);

            var biasFit = BiasModel.Fit(filteredSamples, periodModel, logger);
            var biasModel = biasFit.Model;

            logger?.Invoke(
                $"Bias model => bias = NormalizeToPeriod({biasModel.Scale:+0.000000;-0.000000;+0.000000} * T1(x,y,z,angle) + {biasModel.Offset:+0.000000;-0.000000;+0.000000}), where T1 = ((x - angle*y)/sqrt(1+angle^2)) * {biasModel.DisplayHeight:F6} / abs(z + {biasModel.ZBias:F6})");

            // Parse and fit fine-bias model if data is available
            var fineBiasSamples = ParseFineBiasSamples(rawSamples);
            FineBiasModel? fineBiasModel = null;
            FineBiasResidual[]? fineBiasResiduals = null;
            
            if (fineBiasSamples.Count > 0)
            {
                // Determine grid size from first sample
                int fbCols = fineBiasSamples[0].Cols;
                int fbRows = fineBiasSamples[0].Rows;
                
                fineBiasModel = FineBiasModel.Fit(fineBiasSamples, zBias, fbCols, fbRows, logger);
                fineBiasResiduals = fineBiasModel.ComputeResiduals(fineBiasSamples);
                
                // Log fine-bias residual statistics
                if (fineBiasResiduals.Length > 0)
                {
                    var fbErrors = fineBiasResiduals.Select(r => r.Error).ToArray();
                    var fbStats = ResidualStatistics.Compute(fbErrors);
                    logger?.Invoke($"Fine-bias residuals => MAE: {fbStats.MAE:F6}, RMSE: {fbStats.RMSE:F6}, Max: {fbStats.MaxAbsolute:F6}");
                }
            }
            else
            {
                logger?.Invoke("No fine-bias data found, skipping fine-bias model fitting.");
            }

            var calibration = new CalibrationParameters(periodModel, angleModel, biasModel, fineBiasModel);

            // Build lookup for fine-bias samples by eye position (approximate match)
            var fineBiasByPosition = new Dictionary<(string eye, int x10, int y10, int z10), FineBiasSample>();
            foreach (var fb in fineBiasSamples)
            {
                var key = (fb.Eye, (int)(fb.X * 10), (int)(fb.Y * 10), (int)(fb.Z * 10));
                fineBiasByPosition[key] = fb;
            }

            var biasResiduals = biasFit.Residuals;
            var sampleResiduals = new List<SampleResidual>(filteredSamples.Count);
            for (int i = 0; i < filteredSamples.Count; i++)
            {
                var sample = filteredSamples[i];
                double periodResidual = periodModel.ComputePeriod(sample.X, sample.Y, sample.Z) - sample.Period;
                double angleResidual = angleModel.ComputeAngle(sample.X, sample.Y, sample.Z) - sample.Angle;
                double biasResidual = i < biasResiduals.Length ? biasResiduals[i].ModularError : 0.0;

                // Compute fine-bias residuals for this sample if available
                double[,]? fbResiduals = null;
                if (fineBiasModel != null)
                {
                    var key = (sample.Eye, (int)(sample.X * 10), (int)(sample.Y * 10), (int)(sample.Z * 10));
                    if (fineBiasByPosition.TryGetValue(key, out var fbSample))
                    {
                        int cols = fineBiasModel.Cols;
                        int rows = fineBiasModel.Rows;
                        fbResiduals = new double[cols, rows];
                        for (int c = 0; c < cols && c < fbSample.Cols; c++)
                        {
                            for (int r = 0; r < rows && r < fbSample.Rows; r++)
                            {
                                double actual = fbSample.FineBiasGrid[r, c];
                                double predicted = fineBiasModel.ComputeFineBias(c, r, sample.X, sample.Y, sample.Z);
                                fbResiduals[c, r] = actual - predicted;
                            }
                        }
                    }
                }

                sampleResiduals.Add(new SampleResidual(sample, periodResidual, angleResidual, biasResidual, fbResiduals));
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

            // Compute fine-bias statistics
            ResidualStatistics? fineBiasStats = null;
            if (fineBiasResiduals != null && fineBiasResiduals.Length > 0)
            {
                var fbErrors = fineBiasResiduals.Select(r => r.Error).ToArray();
                fineBiasStats = ResidualStatistics.Compute(fbErrors);
            }

            return new FitResult(
                calibration,
                sampleResiduals,
                periodStats,
                angleStats,
                biasStats,
                fineBiasStats,
                fineBiasResiduals,
                fineBiasSamples);
        }

        // Debug info for interpolation
        internal sealed class InterpolationDebugInfo
        {
            public string Mode { get; init; } = "";  // "tetra", "face", "edge", "single", "exact", "none"
            public int[] SampleIndices { get; init; } = Array.Empty<int>();
            public double[] Weights { get; init; } = Array.Empty<double>();
            public double PeriodAdjustment { get; init; }
            public double AngleAdjustment { get; init; }
            public double BiasAdjustment { get; init; }
            public double[,]? FineBiasAdjustment { get; init; }  // Per-cell fine-bias residual adjustment

            public override string ToString()
            {
                var indices = string.Join(",", SampleIndices);
                var weights = string.Join(",", Weights.Select(w => w.ToString("F3")));
                return $"{Mode}[{indices}] w=[{weights}] adj=(P:{PeriodAdjustment:F4},A:{AngleAdjustment:F4},B:{BiasAdjustment:F4})";
            }
        }

        internal sealed class FitResult
        {
            public CalibrationParameters Calibration { get; }
            public IReadOnlyList<SampleResidual> SampleResiduals { get; }
            public ResidualStatistics PeriodStats { get; }
            public ResidualStatistics AngleStats { get; }
            public ResidualStatistics BiasStats { get; }
            public ResidualStatistics? FineBiasStats { get; }
            public IReadOnlyList<FineBiasResidual>? FineBiasResiduals { get; }
            
            // Raw fine-bias samples for interpolation-based residual fix
            public IReadOnlyList<FineBiasSample>? FineBiasSamples { get; }

            // 3D Delaunay tetrahedralization data (lazy initialized)
            private List<TetrahedronIndices>? _tetrahedra;
            private bool _tetrahedraBuilt;

            public FitResult(
                CalibrationParameters calibration,
                IReadOnlyList<SampleResidual> sampleResiduals,
                ResidualStatistics periodStats,
                ResidualStatistics angleStats,
                ResidualStatistics biasStats,
                ResidualStatistics? fineBiasStats = null,
                IReadOnlyList<FineBiasResidual>? fineBiasResiduals = null,
                IReadOnlyList<FineBiasSample>? fineBiasSamples = null)
            {
                Calibration = calibration;
                SampleResiduals = sampleResiduals;
                PeriodStats = periodStats;
                AngleStats = angleStats;
                BiasStats = biasStats;
                FineBiasStats = fineBiasStats;
                FineBiasResiduals = fineBiasResiduals;
                FineBiasSamples = fineBiasSamples;
            }

            /// <summary>
            /// Interpolate fine-bias grid for a given eye position using IDW (Inverse Distance Weighted) 
            /// interpolation on the raw fine-bias samples. Falls back to model prediction if no samples.
            /// </summary>
            /// <param name="x">Eye X position</param>
            /// <param name="y">Eye Y position</param>
            /// <param name="z">Eye Z position</param>
            /// <param name="isLeftEye">Whether this is for left eye</param>
            /// <param name="sigma">Interpolation strength (0=model only, 1=full interpolation)</param>
            /// <returns>Fine-bias grid [row * cols + col] in row-major order, or null if no fine-bias model</returns>
            public float[]? InterpolateFineBiasGrid(double x, double y, double z, bool isLeftEye, double sigma = 1.0)
            {
                if (Calibration.FineBias == null)
                    return null;

                int cols = Calibration.FineBias.Cols;
                int rows = Calibration.FineBias.Rows;
                var result = new float[cols * rows];

                // If no samples for interpolation, use model prediction only
                if (FineBiasSamples == null || FineBiasSamples.Count == 0)
                {
                    for (int row = 0; row < rows; row++)
                    for (int col = 0; col < cols; col++)
                    {
                        int idx = row * cols + col;
                        result[idx] = (float)Calibration.FineBias.ComputeFineBias(col, row, x, y, z);
                    }
                    return result;
                }

                // Filter samples by eye
                string eyeFilter = isLeftEye ? "L" : "R";
                var samples = FineBiasSamples.Where(s => s.Eye == eyeFilter).ToList();
                
                if (samples.Count == 0)
                {
                    // Fallback to model
                    for (int row = 0; row < rows; row++)
                    for (int col = 0; col < cols; col++)
                    {
                        int idx = row * cols + col;
                        result[idx] = (float)Calibration.FineBias.ComputeFineBias(col, row, x, y, z);
                    }
                    return result;
                }

                // Compute distances and weights using IDW
                var distances = new double[samples.Count];
                var weights = new double[samples.Count];
                double totalWeight = 0;
                
                for (int i = 0; i < samples.Count; i++)
                {
                    var s = samples[i];
                    double dx = s.X - x;
                    double dy = s.Y - y;
                    double dz = s.Z - z;
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    distances[i] = dist;
                    
                    // IDW with power=2, add small epsilon to avoid division by zero
                    double w = 1.0 / Math.Max(dist * dist, 1e-6);
                    weights[i] = w;
                    totalWeight += w;
                }

                // Normalize weights
                for (int i = 0; i < weights.Length; i++)
                    weights[i] /= totalWeight;

                // Interpolate each cell
                for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    int idx = row * cols + col;
                    
                    // Model prediction
                    double modelPred = Calibration.FineBias.ComputeFineBias(col, row, x, y, z);
                    
                    // IDW interpolation of actual values
                    double interpValue = 0;
                    for (int i = 0; i < samples.Count; i++)
                    {
                        var s = samples[i];
                        if (row < s.Rows && col < s.Cols)
                        {
                            interpValue += weights[i] * s.FineBiasGrid[row, col];
                        }
                        else
                        {
                            // Sample doesn't have this cell, use model prediction weighted
                            interpValue += weights[i] * modelPred;
                        }
                    }
                    
                    // Blend model prediction and interpolation based on sigma
                    result[idx] = (float)(modelPred * (1 - sigma) + interpValue * sigma);
                }

                return result;
            }

            // Tetrahedron indices structure for 3D Delaunay
            private class TetrahedronIndices
            {
                public int I1, I2, I3, I4;

                public TetrahedronIndices(int i1, int i2, int i3, int i4)
                {
                    I1 = i1;
                    I2 = i2;
                    I3 = i3;
                    I4 = i4;
                }

                public bool Contains(int index) => I1 == index || I2 == index || I3 == index || I4 == index;
            }

            // Face structure for hole boundary during insertion
            private readonly struct Face : IEquatable<Face>
            {
                public readonly int A, B, C;

                public Face(int a, int b, int c)
                {
                    // Sort indices to make face comparison order-independent
                    if (a > b) (a, b) = (b, a);
                    if (b > c) (b, c) = (c, b);
                    if (a > b) (a, b) = (b, a);
                    A = a; B = b; C = c;
                }

                public bool Equals(Face other) => A == other.A && B == other.B && C == other.C;
                public override bool Equals(object? obj) => obj is Face f && Equals(f);
                public override int GetHashCode() => HashCode.Combine(A, B, C);
            }

            private void EnsureTetrahedralization()
            {
                if (_tetrahedraBuilt) return;
                _tetrahedra = BuildDelaunayTetrahedralization();
                _tetrahedraBuilt = true;
            }

            private List<TetrahedronIndices> BuildDelaunayTetrahedralization()
            {
                var tetrahedra = new List<TetrahedronIndices>();
                int n = SampleResiduals.Count;

                if (n < 4)
                    return tetrahedra;

                // Find bounds of points
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

                for (int i = 0; i < n; i++)
                {
                    var sample = SampleResiduals[i].Sample;
                    minX = Math.Min(minX, sample.X);
                    minY = Math.Min(minY, sample.Y);
                    minZ = Math.Min(minZ, sample.Z);
                    maxX = Math.Max(maxX, sample.X);
                    maxY = Math.Max(maxY, sample.Y);
                    maxZ = Math.Max(maxZ, sample.Z);
                }

                double dx = maxX - minX;
                double dy = maxY - minY;
                double dz = maxZ - minZ;
                double dmax = Math.Max(Math.Max(dx, dy), dz) + 1e-6;
                double midX = (minX + maxX) / 2;
                double midY = (minY + maxY) / 2;
                double midZ = (minZ + maxZ) / 2;

                // Create super-tetrahedron vertices (virtual points at indices n, n+1, n+2, n+3)
                // Make it large enough to contain all points
                var superTetraPoints = new (double X, double Y, double Z)[4];
                double scale = 100;
                superTetraPoints[0] = (midX - scale * dmax, midY - scale * dmax, midZ - scale * dmax);
                superTetraPoints[1] = (midX + scale * dmax, midY - scale * dmax, midZ - scale * dmax);
                superTetraPoints[2] = (midX, midY + scale * dmax, midZ - scale * dmax);
                superTetraPoints[3] = (midX, midY, midZ + scale * dmax);

                // Start with super-tetrahedron
                tetrahedra.Add(new TetrahedronIndices(n, n + 1, n + 2, n + 3));

                // Helper to get point coordinates
                (double X, double Y, double Z) GetPoint(int index)
                {
                    if (index >= n)
                        return superTetraPoints[index - n];
                    var sample = SampleResiduals[index].Sample;
                    return (sample.X, sample.Y, sample.Z);
                }

                // Add all points one by one (Bowyer-Watson algorithm)
                for (int i = 0; i < n; i++)
                {
                    var pi = GetPoint(i);
                    var badTetrahedra = new List<TetrahedronIndices>();

                    // Find tetrahedra whose circumsphere contains point i
                    foreach (var tet in tetrahedra)
                    {
                        var p1 = GetPoint(tet.I1);
                        var p2 = GetPoint(tet.I2);
                        var p3 = GetPoint(tet.I3);
                        var p4 = GetPoint(tet.I4);

                        if (IsPointInCircumsphere(pi.X, pi.Y, pi.Z, p1, p2, p3, p4))
                        {
                            badTetrahedra.Add(tet);
                        }
                    }

                    // Find boundary faces of the hole (faces that appear exactly once)
                    var faceCounts = new Dictionary<Face, int>();
                    foreach (var tet in badTetrahedra)
                    {
                        var faces = new[]
                        {
                            new Face(tet.I1, tet.I2, tet.I3),
                            new Face(tet.I1, tet.I2, tet.I4),
                            new Face(tet.I1, tet.I3, tet.I4),
                            new Face(tet.I2, tet.I3, tet.I4)
                        };
                        foreach (var face in faces)
                        {
                            if (!faceCounts.TryAdd(face, 1))
                                faceCounts[face]++;
                        }
                    }

                    // Remove bad tetrahedra
                    foreach (var bad in badTetrahedra)
                        tetrahedra.Remove(bad);

                    // Create new tetrahedra from boundary faces to point i
                    foreach (var kvp in faceCounts)
                    {
                        if (kvp.Value == 1) // Boundary face
                        {
                            tetrahedra.Add(new TetrahedronIndices(kvp.Key.A, kvp.Key.B, kvp.Key.C, i));
                        }
                    }
                }

                // Remove tetrahedra connected to super-tetrahedron vertices
                tetrahedra.RemoveAll(tet => tet.I1 >= n || tet.I2 >= n || tet.I3 >= n || tet.I4 >= n);

                return tetrahedra;
            }

            private static bool IsPointInCircumsphere(
                double px, double py, double pz,
                (double X, double Y, double Z) a,
                (double X, double Y, double Z) b,
                (double X, double Y, double Z) c,
                (double X, double Y, double Z) d)
            {
                // Using the determinant method for circumsphere test
                // Point p is inside circumsphere if det > 0 (assuming positive orientation)
                double ax = a.X - px, ay = a.Y - py, az = a.Z - pz;
                double bx = b.X - px, by = b.Y - py, bz = b.Z - pz;
                double cx = c.X - px, cy = c.Y - py, cz = c.Z - pz;
                double dx = d.X - px, dy = d.Y - py, dz = d.Z - pz;

                double aSq = ax * ax + ay * ay + az * az;
                double bSq = bx * bx + by * by + bz * bz;
                double cSq = cx * cx + cy * cy + cz * cz;
                double dSq = dx * dx + dy * dy + dz * dz;

                // 4x4 determinant
                double det =
                    ax * (by * (cz * dSq - cSq * dz) - bz * (cy * dSq - cSq * dy) + bSq * (cy * dz - cz * dy)) -
                    ay * (bx * (cz * dSq - cSq * dz) - bz * (cx * dSq - cSq * dx) + bSq * (cx * dz - cz * dx)) +
                    az * (bx * (cy * dSq - cSq * dy) - by * (cx * dSq - cSq * dx) + bSq * (cx * dy - cy * dx)) -
                    aSq * (bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx));

                // The sign depends on the orientation of the tetrahedron
                // We need to check if det has the same sign as the tetrahedron orientation
                double orient = TetrahedronOrientation(a, b, c, d);
                return det * orient > 0;
            }

            private static double TetrahedronOrientation(
                (double X, double Y, double Z) a,
                (double X, double Y, double Z) b,
                (double X, double Y, double Z) c,
                (double X, double Y, double Z) d)
            {
                // Compute orientation using 3x3 determinant of (b-a, c-a, d-a)
                double bax = b.X - a.X, bay = b.Y - a.Y, baz = b.Z - a.Z;
                double cax = c.X - a.X, cay = c.Y - a.Y, caz = c.Z - a.Z;
                double dax = d.X - a.X, day = d.Y - a.Y, daz = d.Z - a.Z;

                return bax * (cay * daz - caz * day) -
                       bay * (cax * daz - caz * dax) +
                       baz * (cax * day - cay * dax);
            }

            private static bool IsPointInTetrahedron(
                double px, double py, double pz,
                (double X, double Y, double Z) a,
                (double X, double Y, double Z) b,
                (double X, double Y, double Z) c,
                (double X, double Y, double Z) d)
            {
                // Check if point is on the same side of all 4 faces
                var p = (px, py, pz);

                double sign0 = SignedVolumeSign(a, b, c, d);
                double sign1 = SignedVolumeSign(p, b, c, d);
                double sign2 = SignedVolumeSign(a, p, c, d);
                double sign3 = SignedVolumeSign(a, b, p, d);
                double sign4 = SignedVolumeSign(a, b, c, p);

                // Point is inside if all signs match the tetrahedron orientation
                bool sameSign = (sign1 * sign0 >= 0) && (sign2 * sign0 >= 0) &&
                                (sign3 * sign0 >= 0) && (sign4 * sign0 >= 0);
                return sameSign;
            }

            private static double SignedVolumeSign(
                (double X, double Y, double Z) a,
                (double X, double Y, double Z) b,
                (double X, double Y, double Z) c,
                (double X, double Y, double Z) d)
            {
                double bax = b.X - a.X, bay = b.Y - a.Y, baz = b.Z - a.Z;
                double cax = c.X - a.X, cay = c.Y - a.Y, caz = c.Z - a.Z;
                double dax = d.X - a.X, day = d.Y - a.Y, daz = d.Z - a.Z;

                return bax * (cay * daz - caz * day) -
                       bay * (cax * daz - caz * dax) +
                       baz * (cax * day - cay * dax);
            }

            // 3D barycentric interpolation within tetrahedron
            private (double period, double angle, double bias, double[,]? fineBias, double[] weights) InterpolateWithinTetrahedron(
                double px, double py, double pz,
                SampleResidual r1, SampleResidual r2, SampleResidual r3, SampleResidual r4)
            {
                var a = (r1.Sample.X, r1.Sample.Y, r1.Sample.Z);
                var b = (r2.Sample.X, r2.Sample.Y, r2.Sample.Z);
                var c = (r3.Sample.X, r3.Sample.Y, r3.Sample.Z);
                var d = (r4.Sample.X, r4.Sample.Y, r4.Sample.Z);
                var p = (px, py, pz);

                // Compute barycentric coordinates using volume ratios
                double totalVol = Math.Abs(SignedVolumeSign(a, b, c, d));
                if (totalVol < 1e-20)
                {
                    // Degenerate tetrahedron
                    double avgP = (r1.PeriodResidual + r2.PeriodResidual + r3.PeriodResidual + r4.PeriodResidual) / 4.0;
                    double avgA = (r1.AngleResidual + r2.AngleResidual + r3.AngleResidual + r4.AngleResidual) / 4.0;
                    double avgB = (r1.BiasResidual + r2.BiasResidual + r3.BiasResidual + r4.BiasResidual) / 4.0;
                    var avgFB = InterpolateFineBiasGrids(new[] { 0.25, 0.25, 0.25, 0.25 }, 
                        r1.FineBiasResiduals, r2.FineBiasResiduals, r3.FineBiasResiduals, r4.FineBiasResiduals);
                    return (avgP, avgA, avgB, avgFB, new[] { 0.25, 0.25, 0.25, 0.25 });
                }

                // w1 = Vol(P,B,C,D) / Vol(A,B,C,D), etc.
                double w1 = SignedVolumeSign(p, b, c, d) / SignedVolumeSign(a, b, c, d);
                double w2 = SignedVolumeSign(a, p, c, d) / SignedVolumeSign(a, b, c, d);
                double w3 = SignedVolumeSign(a, b, p, d) / SignedVolumeSign(a, b, c, d);
                double w4 = 1.0 - w1 - w2 - w3;

                double period = w1 * r1.PeriodResidual + w2 * r2.PeriodResidual + w3 * r3.PeriodResidual + w4 * r4.PeriodResidual;
                double angle = w1 * r1.AngleResidual + w2 * r2.AngleResidual + w3 * r3.AngleResidual + w4 * r4.AngleResidual;
                double bias = w1 * r1.BiasResidual + w2 * r2.BiasResidual + w3 * r3.BiasResidual + w4 * r4.BiasResidual;
                
                var fineBias = InterpolateFineBiasGrids(new[] { w1, w2, w3, w4 },
                    r1.FineBiasResiduals, r2.FineBiasResiduals, r3.FineBiasResiduals, r4.FineBiasResiduals);

                return (period, angle, bias, fineBias, new[] { w1, w2, w3, w4 });
            }
            
            // Interpolate fine-bias residual grids using weights
            private static double[,]? InterpolateFineBiasGrids(double[] weights, params double[,]?[] grids)
            {
                // Find first non-null grid to get dimensions
                int cols = 0, rows = 0;
                foreach (var g in grids)
                {
                    if (g != null)
                    {
                        cols = g.GetLength(0);
                        rows = g.GetLength(1);
                        break;
                    }
                }
                if (cols == 0 || rows == 0) return null;
                
                var result = new double[cols, rows];
                for (int c = 0; c < cols; c++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        double sum = 0;
                        double weightSum = 0;
                        for (int i = 0; i < grids.Length && i < weights.Length; i++)
                        {
                            if (grids[i] != null && c < grids[i]!.GetLength(0) && r < grids[i]!.GetLength(1))
                            {
                                sum += weights[i] * grids[i]![c, r];
                                weightSum += weights[i];
                            }
                        }
                        result[c, r] = weightSum > 0 ? sum / weightSum : 0;
                    }
                }
                return result;
            }
            
            // Scale fine-bias grid by a factor
            private static double[,]? ScaleFineBiasGrid(double[,]? grid, double scale)
            {
                if (grid == null) return null;
                int cols = grid.GetLength(0);
                int rows = grid.GetLength(1);
                var result = new double[cols, rows];
                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                        result[c, r] = grid[c, r] * scale;
                return result;
            }

            // Find closest face for extrapolation
            private (int[] indices, double[] weights) FindClosestFaceAndInterpolate(double px, double py, double pz)
            {
                if (SampleResiduals.Count < 3)
                    return FindClosestPointsAndInterpolate(px, py, pz);

                if (_tetrahedra == null || _tetrahedra.Count == 0)
                    return FindClosestPointsAndInterpolate(px, py, pz);

                // Find closest sample
                int closestIdx = 0;
                double minDistSq = double.MaxValue;
                for (int i = 0; i < SampleResiduals.Count; i++)
                {
                    var s = SampleResiduals[i].Sample;
                    double dSq = (s.X - px) * (s.X - px) + (s.Y - py) * (s.Y - py) + (s.Z - pz) * (s.Z - pz);
                    if (dSq < minDistSq)
                    {
                        minDistSq = dSq;
                        closestIdx = i;
                    }
                }

                // Find tetrahedra containing closest point and pick best face
                var closestSample = SampleResiduals[closestIdx].Sample;
                double queryDirX = px - closestSample.X;
                double queryDirY = py - closestSample.Y;
                double queryDirZ = pz - closestSample.Z;
                double queryDirLen = Math.Sqrt(queryDirX * queryDirX + queryDirY * queryDirY + queryDirZ * queryDirZ);

                if (queryDirLen < 1e-10)
                    return (new[] { closestIdx }, new[] { 1.0 });

                queryDirX /= queryDirLen;
                queryDirY /= queryDirLen;
                queryDirZ /= queryDirLen;

                // Find best tetrahedron face for extrapolation
                int bestI1 = closestIdx, bestI2 = -1, bestI3 = -1;
                double bestAlignment = double.MinValue;

                foreach (var tet in _tetrahedra)
                {
                    if (!tet.Contains(closestIdx)) continue;

                    // Get other vertices
                    var others = new List<int>();
                    if (tet.I1 != closestIdx) others.Add(tet.I1);
                    if (tet.I2 != closestIdx) others.Add(tet.I2);
                    if (tet.I3 != closestIdx) others.Add(tet.I3);
                    if (tet.I4 != closestIdx) others.Add(tet.I4);

                    // Try each face containing closestIdx
                    for (int fi = 0; fi < others.Count; fi++)
                    {
                        for (int fj = fi + 1; fj < others.Count; fj++)
                        {
                            int i2 = others[fi], i3 = others[fj];
                            var s2 = SampleResiduals[i2].Sample;
                            var s3 = SampleResiduals[i3].Sample;

                            // Compute face centroid direction from closestIdx
                            double cx = (closestSample.X + s2.X + s3.X) / 3.0 - closestSample.X;
                            double cy = (closestSample.Y + s2.Y + s3.Y) / 3.0 - closestSample.Y;
                            double cz = (closestSample.Z + s2.Z + s3.Z) / 3.0 - closestSample.Z;
                            double cLen = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                            if (cLen > 1e-10)
                            {
                                cx /= cLen; cy /= cLen; cz /= cLen;
                                double alignment = cx * queryDirX + cy * queryDirY + cz * queryDirZ;
                                if (alignment > bestAlignment)
                                {
                                    bestAlignment = alignment;
                                    bestI2 = i2;
                                    bestI3 = i3;
                                }
                            }
                        }
                    }
                }

                if (bestI2 < 0 || bestI3 < 0)
                    return (new[] { closestIdx }, new[] { 1.0 });

                // Use barycentric extrapolation on the face (2D in the face plane)
                var r1 = SampleResiduals[bestI1];
                var r2 = SampleResiduals[bestI2];
                var r3 = SampleResiduals[bestI3];

                // Project query point onto the plane of the face and compute barycentric coords
                var (weights, valid) = ComputeFaceBarycentricWeights(px, py, pz, r1.Sample, r2.Sample, r3.Sample);

                if (!valid)
                    return (new[] { closestIdx }, new[] { 1.0 });

                return (new[] { bestI1, bestI2, bestI3 }, weights);
            }

            private (double[] weights, bool valid) ComputeFaceBarycentricWeights(
                double px, double py, double pz,
                Sample s1, Sample s2, Sample s3)
            {
                // Compute barycentric coordinates by projecting query onto face plane
                double x1 = s1.X, y1 = s1.Y, z1 = s1.Z;
                double x2 = s2.X, y2 = s2.Y, z2 = s2.Z;
                double x3 = s3.X, y3 = s3.Y, z3 = s3.Z;

                // Face normal
                double e1x = x2 - x1, e1y = y2 - y1, e1z = z2 - z1;
                double e2x = x3 - x1, e2y = y3 - y1, e2z = z3 - z1;
                double nx = e1y * e2z - e1z * e2y;
                double ny = e1z * e2x - e1x * e2z;
                double nz = e1x * e2y - e1y * e2x;
                double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz);

                if (nLen < 1e-20)
                    return (new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 }, false);

                // Project query point onto face plane
                double t = ((x1 - px) * nx + (y1 - py) * ny + (z1 - pz) * nz) / (nLen * nLen);
                double projX = px + t * nx;
                double projY = py + t * ny;
                double projZ = pz + t * nz;

                // Compute barycentric coordinates using areas
                double totalArea = nLen / 2;
                double a1 = TriangleArea3D(projX, projY, projZ, x2, y2, z2, x3, y3, z3);
                double a2 = TriangleArea3D(x1, y1, z1, projX, projY, projZ, x3, y3, z3);
                double a3 = TriangleArea3D(x1, y1, z1, x2, y2, z2, projX, projY, projZ);

                double w1 = a1 / totalArea;
                double w2 = a2 / totalArea;
                double w3 = a3 / totalArea;

                // For extrapolation, allow weights outside [0,1]
                // Re-compute using signed areas
                double signedW1 = SignedTriangleArea3D(projX, projY, projZ, x2, y2, z2, x3, y3, z3, nx, ny, nz) / totalArea;
                double signedW2 = SignedTriangleArea3D(x1, y1, z1, projX, projY, projZ, x3, y3, z3, nx, ny, nz) / totalArea;
                double signedW3 = 1.0 - signedW1 - signedW2;

                return (new[] { signedW1, signedW2, signedW3 }, true);
            }

            private static double TriangleArea3D(
                double x1, double y1, double z1,
                double x2, double y2, double z2,
                double x3, double y3, double z3)
            {
                double e1x = x2 - x1, e1y = y2 - y1, e1z = z2 - z1;
                double e2x = x3 - x1, e2y = y3 - y1, e2z = z3 - z1;
                double cx = e1y * e2z - e1z * e2y;
                double cy = e1z * e2x - e1x * e2z;
                double cz = e1x * e2y - e1y * e2x;
                return Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2;
            }

            private static double SignedTriangleArea3D(
                double x1, double y1, double z1,
                double x2, double y2, double z2,
                double x3, double y3, double z3,
                double nx, double ny, double nz)
            {
                double e1x = x2 - x1, e1y = y2 - y1, e1z = z2 - z1;
                double e2x = x3 - x1, e2y = y3 - y1, e2z = z3 - z1;
                double cx = e1y * e2z - e1z * e2y;
                double cy = e1z * e2x - e1x * e2z;
                double cz = e1x * e2y - e1y * e2x;
                double dot = cx * nx + cy * ny + cz * nz;
                return Math.Sign(dot) * Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2;
            }

            private (int[] indices, double[] weights) FindClosestPointsAndInterpolate(double px, double py, double pz)
            {
                if (SampleResiduals.Count == 0)
                    return (Array.Empty<int>(), Array.Empty<double>());
                if (SampleResiduals.Count == 1)
                    return (new[] { 0 }, new[] { 1.0 });

                // Find 4 closest points for inverse distance weighting
                var distances = new List<(int idx, double dist)>();
                for (int i = 0; i < SampleResiduals.Count; i++)
                {
                    var s = SampleResiduals[i].Sample;
                    double dist = Math.Sqrt((s.X - px) * (s.X - px) + (s.Y - py) * (s.Y - py) + (s.Z - pz) * (s.Z - pz));
                    distances.Add((i, dist));
                }
                distances.Sort((a, b) => a.dist.CompareTo(b.dist));

                int count = Math.Min(4, distances.Count);
                var indices = new int[count];
                var weights = new double[count];
                double totalWeight = 0;

                for (int i = 0; i < count; i++)
                {
                    indices[i] = distances[i].idx;
                    weights[i] = distances[i].dist < 1e-10 ? 1e10 : 1.0 / distances[i].dist;
                    totalWeight += weights[i];
                }

                for (int i = 0; i < count; i++)
                    weights[i] /= totalWeight;

                return (indices, weights);
            }

            public (Prediction prediction, InterpolationDebugInfo debugInfo) PredictWithSample(float x, float y, float z, float sigma)
            {
                var basePrediction = Calibration.Predict(x, y, z);

                if (SampleResiduals.Count == 0 || float.IsNaN(sigma) || float.IsInfinity(sigma))
                {
                    return (basePrediction, new InterpolationDebugInfo { Mode = "none" });
                }

                double sigmaValue = Math.Clamp(Math.Abs((double)sigma), 0.0, 1.0);
                if (sigmaValue < 1e-6)
                {
                    return (basePrediction, new InterpolationDebugInfo { Mode = "sigma_zero" });
                }

                double queryX = x;
                double queryY = y;
                double queryZ = z;

                // Check if query point is very close to any sample
                for (int i = 0; i < SampleResiduals.Count; i++)
                {
                    var residual = SampleResiduals[i];
                    var sample = residual.Sample;
                    double dx = sample.X - queryX;
                    double dy = sample.Y - queryY;
                    double dz = sample.Z - queryZ;
                    double distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq < 1e-12)
                    {
                        var exactPrediction = new Prediction(
                            basePrediction.Period - sigmaValue * residual.PeriodResidual,
                            basePrediction.Bias - sigmaValue * residual.BiasResidual,
                            basePrediction.Angle - sigmaValue * residual.AngleResidual);
                        return (exactPrediction, new InterpolationDebugInfo
                        {
                            Mode = "exact",
                            SampleIndices = new[] { i },
                            Weights = new[] { 1.0 },
                            PeriodAdjustment = sigmaValue * residual.PeriodResidual,
                            AngleAdjustment = sigmaValue * residual.AngleResidual,
                            BiasAdjustment = sigmaValue * residual.BiasResidual
                        });
                    }
                }

                // Ensure tetrahedralization is built
                EnsureTetrahedralization();

                // Try 3D Delaunay interpolation if we have tetrahedra
                if (_tetrahedra != null && _tetrahedra.Count > 0)
                {
                    foreach (var tet in _tetrahedra)
                    {
                        var r1 = SampleResiduals[tet.I1];
                        var r2 = SampleResiduals[tet.I2];
                        var r3 = SampleResiduals[tet.I3];
                        var r4 = SampleResiduals[tet.I4];

                        var a = (r1.Sample.X, r1.Sample.Y, r1.Sample.Z);
                        var b = (r2.Sample.X, r2.Sample.Y, r2.Sample.Z);
                        var c = (r3.Sample.X, r3.Sample.Y, r3.Sample.Z);
                        var d = (r4.Sample.X, r4.Sample.Y, r4.Sample.Z);

                        if (IsPointInTetrahedron(queryX, queryY, queryZ, a, b, c, d))
                        {
                            var (period, angle, bias, fineBias, weights) = InterpolateWithinTetrahedron(queryX, queryY, queryZ, r1, r2, r3, r4);
                            double periodAdj = period * sigmaValue;
                            double angleAdj = angle * sigmaValue;
                            double biasAdj = bias * sigmaValue;
                            var fineBiasAdj = ScaleFineBiasGrid(fineBias, sigmaValue);

                            var tetPrediction = new Prediction(
                                basePrediction.Period - periodAdj,
                                basePrediction.Bias - biasAdj,
                                basePrediction.Angle - angleAdj);

                            return (tetPrediction, new InterpolationDebugInfo
                            {
                                Mode = "tetra",
                                SampleIndices = new[] { tet.I1, tet.I2, tet.I3, tet.I4 },
                                Weights = weights,
                                PeriodAdjustment = periodAdj,
                                AngleAdjustment = angleAdj,
                                BiasAdjustment = biasAdj,
                                FineBiasAdjustment = fineBiasAdj
                            });
                        }
                    }
                }

                // Point is outside all tetrahedra - extrapolate using closest face
                var (indices, weights2) = FindClosestFaceAndInterpolate(queryX, queryY, queryZ);

                double periodSum = 0, angleSum = 0, biasSum = 0;
                var fineBiasGrids = new List<double[,]?>();
                for (int i = 0; i < indices.Length; i++)
                {
                    var r = SampleResiduals[indices[i]];
                    periodSum += weights2[i] * r.PeriodResidual;
                    angleSum += weights2[i] * r.AngleResidual;
                    biasSum += weights2[i] * r.BiasResidual;
                    fineBiasGrids.Add(r.FineBiasResiduals);
                }
                var fineBiasSum = InterpolateFineBiasGrids(weights2, fineBiasGrids.ToArray());

                double periodAdj2 = periodSum * sigmaValue;
                double angleAdj2 = angleSum * sigmaValue;
                double biasAdj2 = biasSum * sigmaValue;
                var fineBiasAdj2 = ScaleFineBiasGrid(fineBiasSum, sigmaValue);

                var extrapPrediction = new Prediction(
                    basePrediction.Period - periodAdj2,
                    basePrediction.Bias - biasAdj2,
                    basePrediction.Angle - angleAdj2);

                string mode = indices.Length switch
                {
                    1 => "single",
                    2 => "edge",
                    3 => "face",
                    _ => "idw"
                };

                return (extrapPrediction, new InterpolationDebugInfo
                {
                    Mode = mode,
                    SampleIndices = indices,
                    Weights = weights2,
                    PeriodAdjustment = periodAdj2,
                    AngleAdjustment = angleAdj2,
                    BiasAdjustment = biasAdj2,
                    FineBiasAdjustment = fineBiasAdj2
                });
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
                
                // Skip comment lines (manually marked bad data)
                if (line.StartsWith("//"))
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

        /// <summary>
        /// Parse fine-bias samples from raw data, associating each with preceding L/R sample pair.
        /// </summary>
        private static List<FineBiasSample> ParseFineBiasSamples(string raw)
        {
            var fineBiasSamples = new List<FineBiasSample>();
            using var reader = new StringReader(raw);
            string? line;
            
            Sample? lastLeftSample = null;
            Sample? lastRightSample = null;
            
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                
                // Skip comment lines (manually marked bad data)
                if (line.StartsWith("//"))
                    continue;

                if (line.StartsWith("#FINE_BIAS"))
                {
                    // Parse fine-bias JSON
                    var parts = line.Split('\t', 2);
                    if (parts.Length < 2)
                        continue;
                    
                    try
                    {
                        var json = parts[1];
                        var fb = JsonConvert.DeserializeObject<FineBiasJson>(json);
                        if (fb == null || fb.leftBias == null || fb.rightBias == null)
                            continue;
                        
                        int cols = fb.cols;
                        int rows = fb.rows;
                        
                        // Convert flat arrays to 2D grids
                        var leftGrid = new double[rows, cols];
                        var rightGrid = new double[rows, cols];
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                int idx = r * cols + c;
                                leftGrid[r, c] = fb.leftBias[idx];
                                rightGrid[r, c] = fb.rightBias[idx];
                            }
                        }
                        
                        // Create samples for left and right eye
                        if (lastLeftSample != null)
                        {
                            fineBiasSamples.Add(new FineBiasSample
                            {
                                Eye = "L",
                                X = lastLeftSample.Value.X,
                                Y = lastLeftSample.Value.Y,
                                Z = lastLeftSample.Value.Z,
                                Angle = lastLeftSample.Value.Angle,
                                Cols = cols,
                                Rows = rows,
                                FineBiasGrid = leftGrid
                            });
                        }
                        
                        if (lastRightSample != null)
                        {
                            fineBiasSamples.Add(new FineBiasSample
                            {
                                Eye = "R",
                                X = lastRightSample.Value.X,
                                Y = lastRightSample.Value.Y,
                                Z = lastRightSample.Value.Z,
                                Angle = lastRightSample.Value.Angle,
                                Cols = cols,
                                Rows = rows,
                                FineBiasGrid = rightGrid
                            });
                        }
                        
                        lastLeftSample = null;
                        lastRightSample = null;
                    }
                    catch
                    {
                        // Skip malformed fine-bias entries
                    }
                }
                else if (!line.StartsWith("#"))
                {
                    // Parse regular sample
                    var parts = line.Split('\t');
                    if (parts.Length < 8)
                        continue;

                    var eyeToken = parts[0].Trim();
                    var eye = eyeToken.TrimStart('*');

                    double ParseDouble(string text)
                        => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

                    var sample = new Sample(
                        eye,
                        ParseDouble(parts[1]),
                        ParseDouble(parts[2]),
                        ParseDouble(parts[3]),
                        ParseDouble(parts[4]),
                        ParseDouble(parts[5]),
                        ParseDouble(parts[6]),
                        ParseDouble(parts[7]));
                    
                    if (eye.Equals("L", StringComparison.OrdinalIgnoreCase))
                        lastLeftSample = sample;
                    else if (eye.Equals("R", StringComparison.OrdinalIgnoreCase))
                        lastRightSample = sample;
                }
            }

            return fineBiasSamples;
        }

        private class FineBiasJson
        {
            public int cols { get; set; }
            public int rows { get; set; }
            public double[]? leftBias { get; set; }
            public double[]? rightBias { get; set; }
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
            public SampleResidual(Sample sample, double periodResidual, double angleResidual, double biasResidual, 
                double[,]? fineBiasResiduals = null)
            {
                Sample = sample;
                PeriodResidual = periodResidual;
                AngleResidual = angleResidual;
                BiasResidual = biasResidual;
                FineBiasResiduals = fineBiasResiduals;  // [col, row] grid of residuals
            }

            public Sample Sample { get; }
            public double PeriodResidual { get; }
            public double AngleResidual { get; }
            public double BiasResidual { get; }
            public double[,]? FineBiasResiduals { get; }  // Per-cell fine-bias residuals (actual - predicted)
        }

        internal struct PeriodModel
        {
            [JsonConstructor]
            public PeriodModel(double a, double b, double c, double m, double displayHeight, double zBias)
            {
                A = a;
                B = b;
                C = c;
                M = m;
                DisplayHeight = displayHeight;
                ZBias = zBias;
            }
            
            // Legacy constructor for backward compatibility with old JSON files
            public PeriodModel(double a, double b, double c, double h1, double h2, double m, double zBias)
                : this(a, b, c, m, h2 - h1, zBias)
            {
            }

            public double A;
            public double B;
            public double C;
            public double M;
            public double DisplayHeight;
            public double ZBias;

            public double EvaluatePlane(double x, double y, double z) => A * x + B * y + C * (z + ZBias);

            /// <summary>
            /// Compute period using simplified formula: period = M * (1 + DisplayHeight / s)
            /// where s = z + ZBias (when A=0, B=0, C=1)
            /// </summary>
            public double ComputePeriod(double x, double y, double z)
            {
                double s = EvaluatePlane(x, y, z);
                double safeS = Math.Abs(s) < 1e-6 ? (s >= 0 ? 1e-6 : -1e-6) : s;
                return M * (1.0 + DisplayHeight / safeS);
            }

            public static PeriodModel Fit(
                IReadOnlyList<Sample> samples,
                double zBias,
                Action<string>? logger)
            {
                if (samples.Count < 3)
                {
                    throw new InvalidOperationException("Need at least three samples to fit period model.");
                }

                if (double.IsNaN(zBias) || double.IsInfinity(zBias))
                {
                    throw new ArgumentOutOfRangeException(nameof(zBias), "zBias must be finite.");
                }

                Log(logger, $"[PeriodFit] Using provided zBias = {zBias:F1}");
                var (model, evaluation) = FitForZBias(samples, zBias, verbose: logger != null, logger);
                Log(logger, $"[PeriodFit] Complete. RMSE: {evaluation.Rmse:F6}");
                return model;
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

            private static PeriodModel BuildModel(double displayHeight, double m, double zBias)
            {
                // Simple model: period = M * (1 + displayHeight / (z + zBias))
                // A=0, B=0, C=1 means s = z + zBias
                return new PeriodModel(a: 0.0, b: 0.0, c: 1.0, m: m, displayHeight: displayHeight, zBias: zBias);
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
                // Note: The pair regression may give wrong sign due to wrapping.
                // We do a global search over a wide range including both positive and negative slopes.
                // The bias relationship is: bias = scale * T1 + offset (mod period)
                // where T1 = lateral * displayHeight / |z + zBias|
                // Typical scale values are in range [-10, +10].
                
                double globalMin = -10.0;
                double globalMax = +10.0;
                
                var bestResult = new ScaleOffsetFitResult(initialScale, 0.0, double.PositiveInfinity);

                // First pass: coarse global search
                {
                    int steps = 400;
                    double stepSize = (globalMax - globalMin) / steps;
                    for (int step = 0; step <= steps; step++)
                    {
                        double candidateScale = globalMin + step * stepSize;
                        var offsetResult = FindBestOffset(samples, periodModel, candidateScale);
                        if (offsetResult.Loss < bestResult.Loss)
                        {
                            bestResult = new ScaleOffsetFitResult(candidateScale, offsetResult.Offset, offsetResult.Loss);
                        }
                    }
                }

                // Refinement passes: narrow down around best
                double minScale = bestResult.Scale;
                double maxScale = bestResult.Scale;
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    double window = Math.Max(0.5 / (iteration + 1), Math.Abs(bestResult.Scale) * 0.1 + 0.05);
                    minScale = bestResult.Scale - window;
                    maxScale = bestResult.Scale + window;
                    
                    int steps = 200;
                    double stepSize = (maxScale - minScale) / steps;
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

        /// <summary>
        /// Per-cell coefficients for fine-bias model (7 parameters per cell).
        /// Formula: fine_bias = A + B*(x/z) + C*(x/z)² + D*(y/z) + E*(y/z)² + F*(1/z) + G*(1/z)²
        /// where z = z_eye + zBias
        /// </summary>
        internal struct FineBiasCellCoeffs
        {
            [JsonConstructor]
            public FineBiasCellCoeffs(double a, double b, double c, double d, double e, double f, double g)
            {
                A = a; B = b; C = c; D = d; E = e; F = f; G = g;
            }

            public double A { get; }  // constant
            public double B { get; }  // x/z
            public double C { get; }  // (x/z)²
            public double D { get; }  // y/z
            public double E { get; }  // (y/z)²
            public double F { get; }  // 1/z (distance effect)
            public double G { get; }  // 1/z² (quadratic distance)

            public double Compute(double x, double y, double z, double zBias)
            {
                double zAdj = z + zBias;
                double xz = x / zAdj;
                double yz = y / zAdj;
                double invZ = 1.0 / zAdj;
                return A + B * xz + C * xz * xz + D * yz + E * yz * yz + F * invZ + G * invZ * invZ;
            }
        }

        /// <summary>
        /// Fine-bias model: per-cell correction for bias at different screen positions.
        /// Each cell (col, row) has its own 7-parameter model due to installation tolerances.
        /// Formula per cell: fine_bias = A + B*(x/z) + C*(x/z)² + D*(y/z) + E*(y/z)² + F*(1/z) + G*(1/z)²
        /// </summary>
        internal class FineBiasModel
        {
            [JsonConstructor]
            public FineBiasModel(FineBiasCellCoeffs[,] cellCoeffs, double zBias, int cols, int rows, double rmse)
            {
                CellCoeffs = cellCoeffs;
                ZBias = zBias;
                Cols = cols;
                Rows = rows;
                RMSE = rmse;
            }

            public FineBiasCellCoeffs[,] CellCoeffs { get; }  // [col, row] coefficients
            public double ZBias { get; }
            public int Cols { get; }  // Grid columns (typically 5)
            public int Rows { get; }  // Grid rows (typically 3)
            public double RMSE { get; }  // Overall fitting RMSE

            /// <summary>
            /// Compute fine-bias correction for a given screen position and eye position.
            /// col and row are texture coordinates (col: 0~Cols-1, row: 0~Rows-1).
            /// </summary>
            public double ComputeFineBias(int col, int row, double x, double y, double z)
            {
                if (col < 0 || col >= Cols || row < 0 || row >= Rows)
                    return 0.0;
                return CellCoeffs[col, row].Compute(x, y, z, ZBias);
            }

            /// <summary>
            /// Fit per-cell fine-bias models from collected data.
            /// </summary>
            public static FineBiasModel Fit(IReadOnlyList<FineBiasSample> samples, double zBias,
                                           int cols, int rows, Action<string>? logger)
            {
                if (samples.Count == 0)
                {
                    logger?.Invoke("No fine-bias data available, using default model.");
                    var defaultCoeffs = new FineBiasCellCoeffs[cols, rows];
                    for (int c = 0; c < cols; c++)
                        for (int r = 0; r < rows; r++)
                            defaultCoeffs[c, r] = new FineBiasCellCoeffs(0, 0, 0, 0, 0, 0, 0);
                    return new FineBiasModel(defaultCoeffs, zBias, cols, rows, 0.0);
                }

                // Collect data per cell
                var cellData = new List<(double x, double y, double z, double fineBias)>[cols, rows];
                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                        cellData[c, r] = new List<(double, double, double, double)>();

                foreach (var sample in samples)
                {
                    for (int r = 0; r < sample.Rows && r < rows; r++)
                    {
                        for (int c = 0; c < sample.Cols && c < cols; c++)
                        {
                            double fb = sample.FineBiasGrid[r, c];
                            cellData[c, r].Add((sample.X, sample.Y, sample.Z, fb));
                        }
                    }
                }

                var cellCoeffs = new FineBiasCellCoeffs[cols, rows];
                double totalSumSq = 0;
                int totalPoints = 0;

                for (int col = 0; col < cols; col++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        var data = cellData[col, row];
                        if (data.Count < 7)
                        {
                            cellCoeffs[col, row] = new FineBiasCellCoeffs(0, 0, 0, 0, 0, 0, 0);
                            continue;
                        }

                        // Build design matrix: [1, x/z, (x/z)², y/z, (y/z)², 1/z, 1/z²]
                        var design = new double[data.Count][];
                        var targets = new double[data.Count];

                        for (int i = 0; i < data.Count; i++)
                        {
                            var (x, y, z, fb) = data[i];
                            double zAdj = z + zBias;
                            double xz = x / zAdj;
                            double yz = y / zAdj;
                            double invZ = 1.0 / zAdj;

                            design[i] = new[] { 1.0, xz, xz * xz, yz, yz * yz, invZ, invZ * invZ };
                            targets[i] = fb;
                        }

                        // Use Ridge regularization (lambda=1e-6) for numerical stability
                        var coeffs = LinearRegression.Solve(design, targets, ridgeLambda: 1e-6);
                        cellCoeffs[col, row] = new FineBiasCellCoeffs(
                            coeffs[0], coeffs[1], coeffs[2], coeffs[3], coeffs[4], coeffs[5], coeffs[6]);

                        // Calculate residuals for this cell
                        for (int i = 0; i < data.Count; i++)
                        {
                            double pred = 0;
                            for (int j = 0; j < 7; j++)
                                pred += design[i][j] * coeffs[j];
                            double err = targets[i] - pred;
                            totalSumSq += err * err;
                            totalPoints++;
                        }
                    }
                }

                double rmse = totalPoints > 0 ? Math.Sqrt(totalSumSq / totalPoints) : 0.0;
                logger?.Invoke($"Fine-bias per-cell model: {cols}x{rows} cells, {totalPoints} data points, RMSE = {rmse:F6}");

                return new FineBiasModel(cellCoeffs, zBias, cols, rows, rmse);
            }

            /// <summary>
            /// Compute residuals for all fine-bias samples.
            /// </summary>
            public FineBiasResidual[] ComputeResiduals(IReadOnlyList<FineBiasSample> samples)
            {
                var residuals = new List<FineBiasResidual>();
                foreach (var sample in samples)
                {
                    for (int r = 0; r < sample.Rows && r < Rows; r++)
                    {
                        for (int c = 0; c < sample.Cols && c < Cols; c++)
                        {
                            double actual = sample.FineBiasGrid[r, c];
                            double predicted = ComputeFineBias(c, r, sample.X, sample.Y, sample.Z);
                            residuals.Add(new FineBiasResidual(c, r, sample.X, sample.Y, sample.Z, actual, predicted));
                        }
                    }
                }
                return residuals.ToArray();
            }
        }

        /// <summary>
        /// Fine-bias residual for a single cell measurement.
        /// </summary>
        internal struct FineBiasResidual
        {
            public FineBiasResidual(int col, int row, double x, double y, double z, double actual, double predicted)
            {
                Col = col; Row = row; X = x; Y = y; Z = z; Actual = actual; Predicted = predicted;
            }
            public int Col { get; }
            public int Row { get; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double Actual { get; }
            public double Predicted { get; }
            public double Error => Actual - Predicted;
        }

        /// <summary>
        /// Sample containing fine-bias grid data for one eye position.
        /// </summary>
        internal class FineBiasSample
        {
            public string Eye { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Angle { get; set; }
            public int Cols { get; set; }
            public int Rows { get; set; }
            public double[,] FineBiasGrid { get; set; } = new double[0, 0];
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
            public CalibrationParameters(PeriodModel period, AngleModel angle, BiasModel bias, FineBiasModel? fineBias = null)
            {
                Period = period;
                Angle = angle;
                Bias = bias;
                FineBias = fineBias;
            }

            public PeriodModel Period;
            public AngleModel Angle;
            public BiasModel Bias;
            public FineBiasModel? FineBias;

            public Prediction Predict(double x, double y, double z)
            {
                var period = Period.ComputePeriod(x, y, z);
                var angle = Angle.ComputeAngle(x, y, z);
                var bias = Bias.ComputeBias(x, y, z, period, angle);
                return new Prediction(period, bias, angle);
            }

            /// <summary>
            /// Predict with fine-bias correction at a specific screen grid position.
            /// </summary>
            public Prediction PredictWithFineBias(double x, double y, double z, int col, int row)
            {
                var period = Period.ComputePeriod(x, y, z);
                var angle = Angle.ComputeAngle(x, y, z);
                var baseBias = Bias.ComputeBias(x, y, z, period, angle);
                
                double fineBiasCorrection = 0;
                if (FineBias != null)
                {
                    fineBiasCorrection = FineBias.ComputeFineBias(col, row, x, y, z);
                }
                
                var totalBias = NormalizeToPeriod(baseBias + fineBiasCorrection, period);
                return new Prediction(period, totalBias, angle);
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
            /// <summary>
            /// Solve linear regression with optional Ridge regularization (L2).
            /// Ridge helps with singular matrices and collinear features.
            /// </summary>
            public static double[] Solve(double[][] design, double[] targets, double ridgeLambda = 1e-6)
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

                // Add Ridge regularization term (L2) to diagonal
                for (int i = 0; i < cols; i++)
                {
                    ata[i, i] += ridgeLambda;
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



