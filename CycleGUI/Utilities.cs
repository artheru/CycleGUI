using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CycleGUI
{
    public static class CGUI_Extensions
    {
        public static void Deconstruct<T>(this IList<T> list, out T first, out IList<T> rest)
        {
            first = list.Count > 0 ? list[0] : default; // or throw
            rest = list.Skip(1).ToList();
        }

        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out IList<T> rest)
        {
            first = list.Count > 0 ? list[0] : default; // or throw
            second = list.Count > 1 ? list[1] : default; // or throw
            rest = list.Skip(2).ToList();
        }

        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third, out IList<T> rest)
        {
            first = list.Count > 0 ? list[0] : default; // or throw
            second = list.Count > 1 ? list[1] : default; // or throw
            third = list.Count > 2 ? list[2] : default; // or throw
            rest = list.Skip(3).ToList();
        }

        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third, out T fourth, out IList<T> rest)
        {
            first = list.Count > 0 ? list[0] : default; // or throw
            second = list.Count > 1 ? list[1] : default; // or throw
            third = list.Count > 2 ? list[2] : default; // or throw
            fourth = list.Count > 3 ? list[3] : default; // or throw
            rest = list.Skip(4).ToList();
        }

        public static string[] Excluded = { "System", "Jint", "Newtonsoft" };
        public static string MyFormat(this Exception ex)
        {
            if (ex == null) return "";
            if (ex is AggregateException aex)
                return string.Join("\r\n", aex.InnerExceptions.Select(iex => iex.MyFormat()));
            var sts = ex.StackTrace?.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var str = "";
            bool system = false;
            for (var i = 0; i < sts?.Length; i++)
            {
                if (Excluded.Any(predicate => sts[i].Contains($" {predicate}.")))
                {
                    if (system) continue;
                    str += sts[i] + " ...hiding system stacks...\r\n";
                    system = true;
                }
                else
                {
                    str += sts[i] + "\r\n";
                    system = false;
                }
            }

            string exStr = $"* ({ex.GetType().Name}):{ex.Message}, stack:\r\n{str}\r\n";
            foreach (var iEx in ex.GetType().GetFields().Where(p => typeof(Exception).IsAssignableFrom(p.FieldType)))
            {
                var ie = iEx.GetValue(ex);
                if (ie == null) continue;
                exStr += $"  *f.{iEx.Name} " + ((Exception)ie).MyFormat();
            }

            foreach (var iEx in ex.GetType().GetProperties()
                .Where(p => typeof(Exception).IsAssignableFrom(p.PropertyType)))
            {
                var ie = iEx.GetValue(ex);
                if (ie == null) continue;
                exStr += $"  *p.{iEx.Name} " + ((Exception)ie).MyFormat();
            }

            foreach (var iEx in ex.GetType().GetProperties()
                .Where(p => typeof(Exception[]) == p.PropertyType))
            {
                var ies = (Exception[])iEx.GetValue(ex);
                if (ies == null) continue;
                for (var i = 0; i < ies.Length; i++)
                {
                    var ie = ies[i];
                    exStr += $"  *p.{iEx.Name}[{i}] " + ie.MyFormat();
                }
            }
            return exStr;
        }
    }    
    
    public class Utilities
    {

        public static byte[] GetJPG(byte[] rgba, int w, int h, int quality)
        {
            // Redirect to SoftwareBitmap + JpegCodec (dynamic GDI+/libgdiplus)
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (rgba.Length != w * h * 4) throw new ArgumentException("RGBA length mismatch");
            var bmp = new SoftwareBitmap(w, h, rgba);
            return ImageCodec.JpegCodec.SaveJpegToBytes(bmp, quality);
        }
        
        // Struct to store relevant data for each icon entry
        struct IconEntry
        {
            public int Width;
            public int Height;
            public int ImageSize;
            public int Offset;
        }

        public static (byte[] rgba, int width, int height) ConvertIcoBytesToRgba(byte[] icoBytes)
        {
            using (MemoryStream ms = new MemoryStream(icoBytes))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                // ICO header (6 bytes)
                reader.ReadUInt16(); // Reserved, always 0
                reader.ReadUInt16(); // Image type, 1 for icons
                int count = reader.ReadUInt16(); // Number of images

                int closestIndex = -1;
                int minDifference = int.MaxValue;
                int targetSize = 64;


                IconEntry[] iconEntries = new IconEntry[count];

                // Read directory entries to identify the closest match
                for (int i = 0; i < count; i++)
                {
                    int width = reader.ReadByte();
                    int height = reader.ReadByte();
                    reader.ReadByte(); // Color count
                    reader.ReadByte(); // Reserved
                    reader.ReadUInt16(); // Planes
                    reader.ReadUInt16(); // Bit count
                    int imageSize = reader.ReadInt32(); // Image size
                    int offset = reader.ReadInt32(); // Image offset

                    width = width == 0 ? 256 : width;
                    height = height == 0 ? 256 : height;

                    iconEntries[i] = new IconEntry
                    {
                        Width = width,
                        Height = height,
                        ImageSize = imageSize,
                        Offset = offset
                    };

                    // Determine how close the icon is to 64x64
                    int difference = Math.Abs(width - targetSize) + Math.Abs(height - targetSize);

                    if (difference < minDifference)
                    {
                        closestIndex = i;
                        minDifference = difference;
                    }
                }

                if (closestIndex == -1)
                    throw new InvalidOperationException("No valid icon found in the ICO file.");

                // Retrieve the selected icon entry
                IconEntry selectedIcon = iconEntries[closestIndex];
                ms.Seek(selectedIcon.Offset, SeekOrigin.Begin);

                // Read the bitmap header
                using (BinaryReader bmpReader = new BinaryReader(ms))
                {
                    // BITMAPINFOHEADER (14 bytes header + 40 bytes info)
                    int headerSize = bmpReader.ReadInt32();
                    int width = bmpReader.ReadInt32();
                    int height = bmpReader.ReadInt32() / 2; // The height is twice due to including the mask
                    bmpReader.ReadUInt16(); // Planes
                    int bitCount = bmpReader.ReadUInt16();

                    if (bitCount != 32)
                        throw new NotSupportedException("Only 32-bit icons are supported.");

                    bmpReader.ReadUInt32(); // Compression
                    int imageSize = bmpReader.ReadInt32();
                    bmpReader.ReadInt32(); // Horizontal resolution
                    bmpReader.ReadInt32(); // Vertical resolution
                    bmpReader.ReadUInt32(); // Colors used
                    bmpReader.ReadUInt32(); // Important colors

                    // Read the pixel data directly (BGRA)
                    byte[] pixelData = bmpReader.ReadBytes(selectedIcon.ImageSize - headerSize);

                    // Convert BGRA to RGBA
                    byte[] rgbaData = new byte[pixelData.Length];
                    for (int i = 0; i < pixelData.Length; i += 4)
                    {
                        rgbaData[i] = pixelData[i + 2]; // R
                        rgbaData[i + 1] = pixelData[i + 1]; // G
                        rgbaData[i + 2] = pixelData[i]; // B
                        rgbaData[i + 3] = pixelData[i + 3]; // A
                    }

                    return (FlipImageVertically(rgbaData,width,height), width, height);
                }
            }

        }
        public static byte[] FlipImageVertically(byte[] rgbaData, int width, int height)
        {
            int bytesPerPixel = 4; // Since it's RGBA, 4 channels per pixel
            byte[] flippedData = new byte[rgbaData.Length];

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    // Calculate the original and new pixel positions
                    int srcIndex = (row * width + col) * bytesPerPixel;
                    int destIndex = ((height - 1 - row) * width + col) * bytesPerPixel;

                    // Copy pixel data from the source to the destination
                    flippedData[destIndex] = rgbaData[srcIndex];         // R
                    flippedData[destIndex + 1] = rgbaData[srcIndex + 1]; // G
                    flippedData[destIndex + 2] = rgbaData[srcIndex + 2]; // B
                    flippedData[destIndex + 3] = rgbaData[srcIndex + 3]; // A
                }
            }

            return flippedData;
        }
    }

    public class G
    {
        public static ThreadLocal<List<(string name, long timestamp)>> ticking = new(() => new());

        public static void At(string name)
        {
            if (ticking.Value.Count > 0 && name == ticking.Value[0].name)
                ticking.Value.Clear();
            ticking.Value.Add((name, G.watch.ElapsedTicks));
        }

        public static Dictionary<string, long> Gather()
        {
            Dictionary<string, long> ret = new();
            for (int i = 0; i < ticking.Value.Count - 1; ++i)
            {
                ret[ticking.Value[i].name] = ticking.Value[i + 1].timestamp - ticking.Value[i].timestamp;
            }

            if (ticking.Value.Count > 0)
                ret[ticking.Value.Last().name] =
                    (G.watch.ElapsedTicks - ticking.Value.Last().timestamp) * 1000 / Stopwatch.Frequency;
            return ret;
        }

        public class DetourWatch
        {
            public Stopwatch watch = new Stopwatch();
            private long stMillis;

            public long ElapsedMilliseconds => watch.ElapsedTicks / (Stopwatch.Frequency / 1000) + stMillis;
            public long ElapsedMsFromStart => watch.ElapsedTicks / (Stopwatch.Frequency / 1000);
            public long ElapsedTicks => watch.ElapsedTicks;

            public double intervalMs(long tic)
            {
                return (double)(G.watch.ElapsedTicks - tic) / (Stopwatch.Frequency / 1000);
            }

            public void Start()
            {
                stMillis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                watch.Start();
            }
        }

        public static DetourWatch watch = new();
        public static Random rnd = new();

        static G()
        {
            watch.Start();
        }
    }

}
