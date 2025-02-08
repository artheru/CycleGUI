using CycleGUI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CycleGUI.API;

namespace LearnCycleGUI.DemoPlot
{
    internal class DemoPlotHandler
    {
        private static float _time = 0.0f;
        private static float _demoValue = 0.0f;
        private static bool _freeze = false;

        public static PanelBuilder.CycleGUIHandler PreparePanel()
        {
            System.Drawing.Bitmap bmp = new Bitmap("ganyu.png");
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            IntPtr ptr = bmpData.Scan0;
            int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
            byte[] bgrValues = new byte[bytes];
            Marshal.Copy(ptr, bgrValues, 0, bytes);
            bmp.UnlockBits(bmpData);

            byte[] rgb = new byte[bytes];
            for (int i = 0; i < bytes; i += 4)
            {
                // Keep red channel, zero out others
                rgb[i] = bgrValues[i + 2]; // Red
                rgb[i + 1] = bgrValues[i + 1]; // Green 
                rgb[i + 2] = bgrValues[i + 0]; // Blue
                rgb[i + 3] = bgrValues[i + 3]; // Alpha
            }

            Workspace.AddProp(new PutARGB()
            {
                height = bmp.Height,
                width = bmp.Width,
                name = "rgb1",
                requestRGBA = (() => rgb)
            });

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

                pb.Image("Plot picture rgb1", "rgb1");
            };
        }
    }
}
