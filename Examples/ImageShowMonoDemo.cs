using System;
using CycleGUI;

namespace ImageShowMonoDemo
{
    /// <summary>
    /// Example demonstrating the usage of UITools.ImageShowMono
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            // Initialize CycleGUI
            LocalTerminal.Start();
            LocalTerminal.SetTitle("ImageShowMono Demo");

            // Create some test monochrome images
            int width = 640;
            int height = 480;

            // Example 1: Gradient image
            byte[] gradientImage = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    gradientImage[y * width + x] = (byte)(x * 255 / width);
                }
            }

            // Display with Jet colormap (default)
            UITools.ImageShowMono("gradient-jet", gradientImage, width, height);

            // Display same image with HSV colormap
            UITools.ImageShowMono("gradient-hsv", gradientImage, width, height, UITools.ColorMapType.HSV);

            // Display with Hot colormap
            UITools.ImageShowMono("gradient-hot", gradientImage, width, height, UITools.ColorMapType.Hot);

            // Example 2: Radial pattern
            byte[] radialImage = new byte[width * height];
            int centerX = width / 2;
            int centerY = height / 2;
            float maxDist = (float)Math.Sqrt(centerX * centerX + centerY * centerY);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    radialImage[y * width + x] = (byte)((dist / maxDist) * 255);
                }
            }

            // Display with Viridis colormap
            UITools.ImageShowMono("radial-viridis", radialImage, width, height, UITools.ColorMapType.Viridis);

            // Display with Plasma colormap
            UITools.ImageShowMono("radial-plasma", radialImage, width, height, UITools.ColorMapType.Plasma);

            // Example 3: Update the same window
            Console.WriteLine("Updating gradient-jet window in 2 seconds...");
            System.Threading.Thread.Sleep(2000);

            // Create a different pattern
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    gradientImage[y * width + x] = (byte)(y * 255 / height);
                }
            }

            // Update existing window - it will be brought to front
            UITools.ImageShowMono("gradient-jet", gradientImage, width, height);

            Console.WriteLine("ImageShowMono Demo running. Press Ctrl+C to exit.");
            Console.WriteLine("Available colormaps: Jet, HSV, Hot, Cool, Gray, Viridis, Plasma");
            Console.WriteLine("You can close individual image windows by clicking the X button.");
            Console.WriteLine("\nDemo features:");
            Console.WriteLine("- Calling ImageShowMono with the same name updates the image");
            Console.WriteLine("- Calling with different dimensions recreates the window");
            Console.WriteLine("- Each window can be closed independently");
            Console.WriteLine("- Windows are brought to front when updated");

            // Keep the application running
            System.Threading.Thread.Sleep(-1);
        }
    }
}


