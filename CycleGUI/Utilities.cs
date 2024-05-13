using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace CycleGUI
{
    internal class Utilities
    {
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
