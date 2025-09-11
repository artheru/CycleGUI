using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using CycleGUI;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using CycleGUI.API;
using CycleGUI.Terminals;
using FundamentalLib;
using NativeFileDialogSharp;
using static System.Net.Mime.MediaTypeNames;
using Path = System.IO.Path;
using GitHub.secile.Video;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Drawing.Imaging;
using Newtonsoft.Json;

namespace VRenderConsole
{
    
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static class Program
    {
        static unsafe void Main(string[] args)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();

            void load()
            {
                Bitmap bmp = new Bitmap("C:\\Users\\lessokaji\\Pictures\\av\\3F76044.jpg");

                // Convert to a standard 32-bit RGBA format to ensure consistent handling
                Bitmap standardBmp = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(standardBmp))
                {
                    g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                }
                bmp.Dispose();

                int width = standardBmp.Width;
                int height = standardBmp.Height;
                Rectangle rect = new Rectangle(0, 0, width, height);
                System.Drawing.Imaging.BitmapData bmpData =
                    standardBmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, standardBmp.PixelFormat);
                nint ptr = bmpData.Scan0;

                // Calculate the actual bytes needed for RGBA (4 bytes per pixel)
                int pixelCount = width * height;
                int rgbaBytes = pixelCount * 4;
                byte[] bgrValues = new byte[Math.Abs(bmpData.Stride) * height];
                Marshal.Copy(ptr, bgrValues, 0, bgrValues.Length);
                int stride = Math.Abs(bmpData.Stride);
                standardBmp.UnlockBits(bmpData);
                standardBmp.Dispose();

                byte[] rgba = new byte[rgbaBytes];
                int bytesPerPixel = 4; // Format32bppArgb uses 4 bytes per pixel

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIndex = y * stride + x * bytesPerPixel;
                        int dstIndex = (y * width + x) * 4;

                        // Convert BGRA to RGBA
                        rgba[dstIndex] = bgrValues[srcIndex + 2];     // Red
                        rgba[dstIndex + 1] = bgrValues[srcIndex + 1]; // Green 
                        rgba[dstIndex + 2] = bgrValues[srcIndex + 0]; // Blue
                        rgba[dstIndex + 3] = bgrValues[srcIndex + 3]; // Alpha
                    }
                }

                for (int i = 0; i < 300; ++i)
                {
                    Workspace.AddProp(new PutARGB()
                    {
                        height = height,
                        width = width,
                        name = $"{i}-th",
                        requestRGBA = () => rgba
                    });
                }
            }

            load();
            List<(string, string, string)> strs = new List<(string, string, string)>();
            for (int i = 0; i < 1000; ++i)
            {
                strs.Add(($"{i}-th", $"rgba{i}", "xxx"));
            }

            GUI.PromptPanel(pb =>
            {
                pb.ImageList("TEST alot", strs.ToArray());
            });

        }
    }
}