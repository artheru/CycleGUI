using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Encoder = System.Drawing.Imaging.Encoder;

namespace CycleGUI
{
    internal class Utilities
    {

        public static byte[] GetJPG(byte[] rgba, int w, int h, int quality)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return EncodeToJpegWindows(rgba, w, h, quality);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new NotImplementedException("tbd");
                // return EncodeToJpegLinux(rgba, w, h);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported OS");
            }
        }

        private static byte[] EncodeToJpegWindows(byte[] rgba, int w, int h, int quality)
        {
            using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            var rect = new Rectangle(0, 0, w, h);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            Marshal.Copy(rgba, 0, bmpData.Scan0, rgba.Length);
            bitmap.UnlockBits(bmpData);

            ImageCodecInfo GetEncoder(ImageFormat format)
            {
                var codecs = ImageCodecInfo.GetImageDecoders();
                foreach (var codec in codecs)
                {
                    if (codec.FormatID == format.Guid)
                    {
                        return codec;
                    }
                }
                return null;
            }
            var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

            using var ms = new MemoryStream();
            bitmap.Save(ms, jpegEncoder, encoderParameters);
            return ms.ToArray();
        }
        
        //Turbo jpeg.
        // private static byte[] EncodeToJpegLinux(byte[] rgba, int w, int h)
        // {
        //     using (var jpegCompressor = new TJCompressor())
        //     {
        //         var jpegBytes = jpegCompressor.Compress(rgba, w, 0, h, TJPixelFormat.RGBA, TJSubsamplingOption.Chrominance420, 75, TJFlags.None);
        //         return jpegBytes;
        //     }
        // }

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

                    return (rgbaData, width, height);
                }
            }

        }
    }
}
