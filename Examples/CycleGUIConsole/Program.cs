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
using System.Text;
using Newtonsoft.Json;

namespace VRenderConsole
{
    
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static class Program
    {
        private static float prior_row_increment = 0.183528f, base_row_increment_search = 0.002f;
        private static float prior_bias_left = 0, prior_bias_right = 0;
        private static float prior_period = 5.32f;

        static unsafe void Main(string[] args)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));

            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();


            double phi = (1 + Math.Sqrt(5)) / 2.0; // golden ratio
            var N = 1000;
            var radius = 1;
            var points = new List<Vector4>();

            for (int i = 0; i < N; i++)
            {
                double t = (i + 0.5) / N;
                double lat = Math.Acos(1 - 2 * t);
                double lon = 2 * Math.PI * i / phi;

                float x = radius * (float)(Math.Sin(lat) * Math.Cos(lon));
                float y = radius * (float)(Math.Sin(lat) * Math.Sin(lon));
                float z = radius * (float)Math.Cos(lat);

                points.Add(new Vector4(x, y, z, 1));
            }

            Workspace.AddProp(new PutPointCloud()
            {
                name = "sphere", colors = Enumerable.Repeat(0xff00ff00, N).ToArray(), xyzSzs = points.ToArray()
            });
            new SetOperatingGridAppearance()
            {
                pivot = new Vector3(0,0,1),
                unitX = Vector3.UnitX,
                unitY = Vector3.UnitY,
                show = true
            }.Issue();
            var defaultAction = new SelectObject()
            {
                feedback = (tuples, _) =>
                {
                    if (tuples.Length == 0)
                        Console.WriteLine($"no selection");
                    else
                    {
                        var bits = tuples[0].selector;
                        var str = new StringBuilder(bits.Length);
                        for (int i = 0; i < bits.Length; i++)
                            str.Append(bits[i] ? '1' : '0');
                        Console.WriteLine(
                            $"selected {tuples[0].name} - {tuples[0].firstSub}, arr={str}");
                    }
                },
            };
            defaultAction.Start();
            defaultAction.SetSelectionMode(SelectObject.SelectionMode.Paint, 20);
            defaultAction.SetObjectSubSelectable("sphere");
        }
    }
}