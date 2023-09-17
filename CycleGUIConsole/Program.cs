using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using CycleGUI;
using FundamentalLib.Utilities;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using CycleGUI.API;

namespace VRenderConsole
{
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static class Program
    {

        static void Main(string[] args)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();

            if (args.Length != 0)
            {
                GUI.PromptPanel((pb =>
                {
                    var endpoint = pb.TextInput("cgui-server", "127.0.0.1:5432", "ip:port");
                    if (pb.Button("connect"))
                    {
                        LocalTerminal.DisplayRemote(endpoint);
                    }
                }));

                while (true)
                {
                    Thread.Sleep(500);
                }
            }

            Terminal.RegisterRemotePanel(pb =>
            {
                pb.Label("Welcome!");
                if (pb.Button("Click me"))
                {
                    Console.WriteLine("Clicked！");
                    pb.Label("You clicked, and i show");
                }

                var txt = pb.TextInput("Some text");
                if (pb.Button("Submit"))
                    Console.WriteLine(txt);
            });

            Task.Run(()=>
            {
                WebTerminal.Use();
                PlankHttpServer2.AddServingFiles("/files", "D:\\src\\CycleGUI\\Emscripten\\WebDebug");
                PlankHttpServer2.Listener(8081);
            });

            Task.Run(() =>
            {
                TCPTerminal.Serve();
            });

            bool test = true;

            object sync = new object();

            var loopNotifier = new PanelBuilder.GUINotify<int>();
            var rnd = new Random();
            int loops = 0;
            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(100);
                    var pp = Painter.GetPainter("test");
                    pp.Clear();
                    for (int i = 0; i < 1000; ++i)
                    {
                        pp.DrawDot(Color.Cyan,
                            new Vector3((float)(i * Math.Cos(loops / 100f)), (float)(i * Math.Sin(loops / 100f)),
                                (float)(i * Math.Sin(loops / 20f))), 2 + i / 100f);
                    }

                    loops += 1;
                }
            }).Start();
            Workspace.Prop(new PutPointCloud()
            {
                name = "test_putpc",
                xyzSzs = Enumerable.Range(0,1000).Select(p=>new Vector4((float)(p/100f * Math.Cos(p / 100f)), (float)(p / 100f * Math.Sin(p / 100f)),
                    (float)(p / 100f * Math.Sin(p / 20f)),2)).ToArray(),
                colors = Enumerable.Repeat(0xffffffff, 1000).ToArray()
            });


            var defaultAction = new SelectObject()
            {
                feedback = (tuples, _) =>
                {
                    if (tuples.Length == 0)
                        Console.WriteLine($"no selection");
                    else
                        Console.WriteLine($"selected {tuples[0].name}");
                },

            };
            defaultAction.Start();
            defaultAction.ChangeState(new SetAppearance { useGround = false, useBorder = false });
            defaultAction.ChangeState(new SetObjectSelectableOrNot() { name = "test_putpc" });

            GUI.PromptPanel(pb =>
            {
                pb.GetPanel.ShowTitle("TEST Grow");
                pb.Label($"iter={loops}");
                pb.GetPanel.Repaint();
            });
            GUI.PromptPanel(pb =>
            {
                pb.Label("Hello world");
                if (pb.Button("Add point"))
                {
                    var painter = Painter.GetPainter("testpainter");
                    painter.DrawDot(Color.White, new Vector3(rnd.NextSingle(), rnd.NextSingle(), rnd.NextSingle()) * 1000,
                        rnd.NextSingle() * 10 + 1);
                }
                if (pb.CheckBox("Test A", ref test))
                {
                    pb.Label($"Changed Checkbox, val={test}");
                }

                if (pb.Button("LoadModel"))
                {
                    if (File.Exists("model.glb"))
                    {
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("model.glb"))
                            {
                                Center = new Vector3(-1, 0, -0.2f),
                                Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI),
                                Scale = 0.001f
                            },
                            name = "model_glb"
                        });
                        Workspace.Prop(new PutModelObject() { clsName = "model_glb", name = "glb1" });
                    }
                }

                if (pb.Button("Close"))
                    pb.GetPanel.Exit();

                var txt=pb.TextInput("Some text");
                if (pb.Button("Submit"))
                    Console.WriteLine(txt);

                // var sel = pb.Listbox("test", new[] { "1", "2", "a", "b", "ccc", "ddd", "eee", "fgh", "1" });
                // pb.Label($"sel={sel}");

                if (pb.Button("Long procedure"))
                {
                    pb.GetPanel.Freeze();
                    if (UITools.Input("test", "xxx", out var val, "say anything"))
                        Console.WriteLine($"Result={val}");
                    var p = GUI.DeclarePanel();
                    for (int i = 0; i < 10; ++i)
                    {
                        Thread.Sleep(1000);
                        p.Define(pb2 =>
                        {
                            pb2.Label($"{i+1}/10");
                            pb2.Progress(i, 10);
                        });
                    }
                    Console.WriteLine("done");
                    Thread.Sleep(1000);
                    p.Exit();
                    pb.GetPanel.UnFreeze();
                }

                var eventObj = Statics<PanelBuilder.GUINotify>.Declare(() => new());
                if (pb.Button("async procedure"))
                {
                    eventObj.Clear();
                    pb.Label("Notify external");
                    new Thread(() =>
                    {
                        Console.WriteLine("perform time consuming operation");
                        Thread.Sleep(1000);
                        eventObj.Set();
                        Console.WriteLine("perform time complete");
                    }).Start();
                }
                
                
                if (pb.Notified(eventObj))
                {
                    pb.Label("Async procedure done");
                }
                else
                {
                    pb.Label("pending");
                }

                if (pb.Notified(loopNotifier, out var ln))
                    pb.Label($"loop n={ln}");
                else 
                    pb.Label("not yet notified");
            });

            // while (true)
            // {
            //     Thread.Sleep(500);
            //     loops += 1;
            //     loopNotifier.Set(loops * 2);
            // }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            // ApplicationConfiguration.Initialize();
            // Application.Run(new Form1());
        }
    }
}