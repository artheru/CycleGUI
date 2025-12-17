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
using System.Net.Sockets;

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

                points.Add(new Vector4(x, y, z, 3));
            }

            var painter = Painter.GetPainter("liness");
            painter.DrawLine(Color.OrangeRed, Vector3.One*2, Vector3.UnitZ, 2, Painter.ArrowType.End);
            painter.DrawLine(Color.Cyan, Vector3.One, Vector3.UnitZ, 20, Painter.ArrowType.End);
            painter.DrawLine(Color.GreenYellow, Vector3.One * 3, Vector3.UnitZ, 2, Painter.ArrowType.End);

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
                fineSelectOnPointClouds = true,
                feedback = (tuples, _) =>
                {
                    if (tuples.Length == 0)
                        Console.WriteLine($"no selection");
                    else
                    {
                        Console.WriteLine(
                            $"selected {tuples[0].name}");
                        // var bits = tuples[0].selector;
                        // var str = new StringBuilder(bits.Length);
                        // for (int i = 0; i < bits.Length; i++)
                        //     str.Append(bits[i] ? '1' : '0');
                        // Console.WriteLine(
                        //     $"selected {tuples[0].name} - {tuples[0].firstSub}, arr={str}");
                    }
                },
            };
            defaultAction.Start();
            defaultAction.SetSelectionMode(SelectObject.SelectionMode.Paint, 20);
            defaultAction.SetObjectSubSelectable(".*");

            var prev_state = false;
            var manipulation = new UseGesture();
            manipulation.ChangeState(new SetAppearance() { drawGuizmo = false });
            manipulation.AddWidget(new UseGesture.ToggleWidget()
            {
                name = $"tw",
                text = "Windowed Toggle",
                position = $"80%,5%",
                size = "9%,9%",
                keyboard = "f11",
                joystick = "",
                OnValue = (b) =>
                {
                    // if (b != prev_state)
                    //     new SetFullScreen() { screen_id = 1, fullscreen = b }.IssueToTerminal(GUI.localTerminal);
                    //Console.WriteLine($"button={b}");
                    prev_state = b;
                }
            });
            manipulation.AddWidget(new UseGesture.ButtonWidget()
            {
                name = $"bw",
                text = "Windowed Toggle",
                position = $"80%,20%",
                size = "9%,9%",
                keyboard = "f10",
                joystick = "",
                OnPressed = (b) =>
                {
                    // if (b != prev_state)
                    //     new SetFullScreen() { screen_id = 1, fullscreen = b }.IssueToTerminal(GUI.localTerminal);
                    //Console.WriteLine($"button={b}");
                    prev_state = b;
                }
            });
            manipulation.AddWidget(new UseGesture.StickWidget()
            {
                name = $"sw",
                text = "Stick",
                position = $"50%,50%",
                size = "9%,9%",
                keyboard = "W,S,A,D",
                joystick = "Axis0,Axis1",
                OnValue = (pos, manipulating) =>
                {
                    // Console.WriteLine($"pos={pos}, {manipulating}");
                }
            });
            // manipulation.Start();

            // new FollowMouse()
            // {
            //     method = FollowMouse.FollowingMethod.CircleOnGrid,
            //     realtime = true,
            //     feedback = (feedback, _) =>
            //     {
            //         Console.WriteLine($"Mouse moved on operational grid from {feedback.mouse_start_XYZ} to {feedback.mouse_end_XYZ}");
            //     },
            //     finished = () => Console.WriteLine("Follow mouse operation completed"),
            //     terminated = () => Console.WriteLine("Follow mouse operation cancelled")
            // }.Start();

            // new Thread(() =>
            // {
            //     var pp = Painter.GetPainter("vis");
            //     while (true)
            //     {
            //         pp.Clear();
            //         pp.DrawLine(Color.Red,new Vector3(0,0,0),new Vector3(3,3,3));
            //         pp.DrawLine(Color.Red, new Vector3(0, 0, 0), new Vector3(3, 0, 0));
            //         pp.DrawLine(Color.Red, new Vector3(0, 0, 0), new Vector3(0, 3, 0));
            //         pp.DrawLine(Color.Red, new Vector3(0, 0, 0), new Vector3(0, 0, 3));
            //         pp.DrawText(Color.Red,new Vector3(3,3,3),"HELLP");
            //         for (int i = 0; i < 1000; ++i)
            //             pp.DrawDot(Color.OrangeRed, new Vector3(i, i, i), 1);
            //         Thread.Sleep(10);
            //     }
            // }).Start();


            Viewport aux_vp1l = null, aux_vp2l = null;
            int selectedMode=0;
            GUI.PromptPanel(pb =>
            {
                if (pb.Button("Print", "f5"))
                {
                    Console.WriteLine("F5!");
                }
                // if (pb.Button("FS", "f11"))
                //     new SetFullScreen() { screen_id = 1, fullscreen = true }.IssueToTerminal(GUI.localTerminal);
                //
                // if (pb.Button("OFS", "ctrl+f11"))
                //     new SetFullScreen() { screen_id = 1, fullscreen = false }.IssueToTerminal(GUI.localTerminal);

                if (pb.Button("Open SubViewport1"))
                {
                    aux_vp1l ??= GUI.PromptWorkspaceViewport(panel => panel.ShowTitle(null));

                    new SetCamera()
                    {
                        distance = 1
                    }.IssueToTerminal(aux_vp1l);
                }

                if (pb.Button("Open SubViewport2"))
                    aux_vp2l ??= GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("TEST2"));

                string[] modes = new[] { "LineOnGrid", "RectOnGrid", "Line3D", "Rect3D", "Box3D", "Sphere3D", "PointOnGrid", "CircleOnGrid" };
                // store/read selection via a hidden dropdown to reuse state infra is not available; use RadioButtons directly
                pb.RadioButtons("Follow Mode", modes, ref selectedMode);

                if (pb.Button("Follow Mouse"))
                {
                    var fm = FollowMouse.FollowingMethod.LineOnGrid;
                    try { fm = (FollowMouse.FollowingMethod)selectedMode; } catch { }
                    Console.WriteLine($"Use {fm} to follow");
                    new FollowMouse()
                    {
                        method = fm,
                        realtime = true,
                        feedback = (feedback, _) =>
                        {
                            Console.WriteLine($"Mouse moved on operational grid from {feedback.mouse_start_XYZ} to {feedback.mouse_end_XYZ}");
                        },
                        finished = () => Console.WriteLine("Follow mouse operation completed"),
                        terminated = () => Console.WriteLine("Follow mouse operation cancelled")
                    }.StartOnTerminal(aux_vp1l);
                }
                if (pb.Button("HIDE"))
                {
                    aux_vp1l.UseOffscreenRender(true);
                }
                if (pb.Button("FUCK"))
                    new CaptureRenderedViewport()
                    {
                        callback = img =>
                        {
                            UITools.Alert("Aux-Viewport captured and saved as 'capture.jpg'");
                        }
                    }.IssueToTerminal(aux_vp1l);
            });
        }
    }
}