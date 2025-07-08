using CycleGUI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CycleGUI.API;

namespace LearnCycleGUI.Demo
{
    internal class DemoPlotHandler
    {
        private static float _time = 0.0f;
        private static float _demoValue = 0.0f;
        private static bool _freeze = false;

        public static PanelBuilder.CycleGUIHandler PreparePanel()
        {
            void load(string name, string rgbname)
            {
                Bitmap bmp = new Bitmap(name);
                
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

                Workspace.AddProp(new PutARGB()
                {
                    height = height,
                    width = width,
                    name = rgbname,
                    requestRGBA = () => rgba
                });
            }

            load("ganyu.png", "rgb1");
            load("kokomi.png", "rgb2");
            load("av0.jpg", "rgb3");
            load("av1.jpg", "rgb4");
            load("av2.jpg", "rgb5");


            return pb =>
            {
                if (pb.Closing())
                {
                    Program.CurrentPanels.Remove(pb.Panel);
                    pb.Panel.Exit();
                    return;
                }

                // Update demo value with a sine wave
                if (!_freeze)
                {
                    _time += 0.016f; // Assuming 60 FPS
                    _demoValue = (float)Math.Sin(_time);
                }

                // Realtime plot demo
                string[] buttons = new[] { "Reset", "Clear" };
                int btnClicked = pb.RealtimePlot("Sine Wave", _demoValue, _freeze, buttons);
                pb.Panel.Repaint(); // realtime plot needs to refresh panel on each updates.

                if (btnClicked == 0) // Reset button clicked
                {
                    _time = 0.0f;
                    _demoValue = 0.0f;
                }

                // Toggle freeze state
                pb.Toggle("Freeze Plot", ref _freeze);

                pb.Image("Plot picture rgb1", "rgb2");

                var selpic = pb.ImageList("Album", [
                    ("rgb1", "upper", "lower"),
                    ("rgb2", "kokomi lovely girl", "kokomi2"),
                    ("rgb3", "av0", "kokomi2"),
                    ("rgb4", "av1", "kokomi2"),
                    ("rgb5", "av2as dfas dfas asd fasd fa sdf ", "kokomi2")
                ], persistentSelecting:true);
                if (selpic>-1)
                {
                    pb.Label($"Selected {selpic}-th picture");
                };
            };
        }
    }
}
