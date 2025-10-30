using OpenCvSharp;

namespace HoloCaliberationDemo;

internal partial class Program
{
    private static double[] SolveLeastSquares(double[,] A, double[] b)
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

    private static double[] GaussianElimination(double[,] A, double[] b)
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

    // Forward DFT amplitude, returned as float[] (size = width*height), DC at top-left
    public static float[] ComputeAmplitudeFloat(byte[] values, int width, int height)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        if (values.Length != width * height)
            throw new ArgumentException("values length != width*height");

        // 1) Create 8U mat and copy bytes in (⚠ specify T for SetArray)
        using var img = Mat.FromPixelData(height, width, MatType.CV_8UC1, values);

        // 2) To float (CV_32F)
        using var f32 = new Mat();
        img.ConvertTo(f32, MatType.CV_32F);

        // 3) Merge real/imag to complex (CV_32FC2)
        using var imag = Mat.Zeros(f32.Size(), MatType.CV_32F);
        using var complex = new Mat();
        Cv2.Merge(new Mat[] { f32, imag }, complex);

        // 4) DFT in-place
        Cv2.Dft(complex, complex, DftFlags.None);

        // 5) Magnitude = sqrt(re^2 + im^2)
        Cv2.Split(complex, out Mat[] ri);      // ri is an array (not IDisposable)
        using var re = ri[0];
        using var im = ri[1];
        using var mag = new Mat();
        Cv2.Magnitude(re, im, mag);            // CV_32F, same size as input

        // 6) Extract as float[] (⚠ use GetArray(out T[]), not 3-arg overload)
        mag.GetArray(out float[] amplitudes);
        return amplitudes;                      // length = width * height
    }
}