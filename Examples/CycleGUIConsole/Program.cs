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

namespace VRenderConsole
{
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static class Program
    {
        private static UsbCamera camera;

        static unsafe void Main(string[] args)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();
            // return;
            if (args.Length != 0)
            {
                GUI.PromptPanel((pb =>
                {
                    var (endpoint, inputEp) = pb.TextInput("cgui-server", "127.0.0.1:5432", "ip:port");
                    if (pb.Button("connect") || inputEp)
                    {
                        LocalTerminal.DisplayRemote(endpoint);
                    }
                }));

                while (true)
                {
                    Thread.Sleep(500);
                }
            }

            string fn2down=null;
            Terminal.RegisterRemotePanel(pb =>
            {
                var defaultAction = new SelectObject()
                {
                    terminal = pb.Panel.Terminal,
                    feedback = (tuples, _) =>
                    {
                        if (tuples.Length == 0)
                            Console.WriteLine($"no selection");
                        else
                            Console.WriteLine($"selected {tuples[0].name}");
                    },
                };
                defaultAction.Start();
                defaultAction.SetObjectSubSelectable("glb1");

                pb.Label("Welcome!");
                if (pb.Button("Click me"))
                {
                    Console.WriteLine("Clicked！");
                    pb.Label("You clicked, and i show");
                }
                if (pb.Button("Throw an error"))
                {
                    throw new Exception("Holy shit");
                }

                if (pb.Button("select file"))
                {
                    if (UITools.FileBrowser("Select File to download", out fn2down))
                    {
                        pb.Label(fn2down);
                        pb.DisplayFileLink(fn2down, "download it!");
                    }
                }

                if (fn2down != null)
                {

                }

                var txt = pb.TextInput("Some text");
                if (pb.Button("Submit"))
                    Console.WriteLine(txt);
            });

            Task.Run(()=>
            {
                LeastServer.AddServingFiles("/debug", "D:\\src\\CycleGUI\\Emscripten\\WebDebug");
                LeastServer.AddServingFiles("/files", Path.Join(AppDomain.CurrentDomain.BaseDirectory, "htdocs"));
                WebTerminal.Use(ico: icoBytes);
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
                    // for (int i = 0; i < 2000; ++i)
                    // {
                    //     pp.DrawDot(Color.FromArgb((int)(Math.Sin(i / 100) * 128) + 127, 255, 255),
                    //         new Vector3((float)(i * Math.Cos(loops / 100f - i / 250f)),
                    //             (float)(i * Math.Sin(loops / 100f + i / 300f)),
                    //             (float)(i * Math.Sin(loops / 20f))), 2 + i / 100f);
                    // }
                    //
                    var vec = new Vector3((float)(1100 * Math.Cos(loops / 100f)),
                        (float)(1100 * Math.Sin(loops / 100f)),
                        (float)(1100 * Math.Sin(loops / 20f)));
                    pp.DrawText(Color.YellowGreen, vec, $"L{loops}");

                    double TriangleWave(double t, double period, double maxAmplitude)
                    {
                        return 2 * maxAmplitude / period * (period - Math.Abs((t + period / 2) % period - period / 2));
                    }

                    var xyzs = Enumerable.Range((int)(TriangleWave(loops, 100, 100)), 100).Select(p =>
                        new Vector3((float)(p / 10f * Math.Cos(p / 10f)),
                            (float)(p / 10f * Math.Sin(p / 2f)),
                            (float)(p / 10f * Math.Sin(p / 10f)))).ToArray();
                    
                    for (int i=0; i< 99; ++i)
                        pp.DrawLine(Color.Red, xyzs[i], xyzs[i+1], 1, Painter.ArrowType.End);

                    loops += 1;
                }
            }).Start();
            Workspace.Prop(new PutPointCloud()
            {
                name = "test_putpc1",
                xyzSzs = Enumerable.Range(0,1000).Select(p=>new Vector4((float)(p/100f * Math.Cos(p / 100f)), (float)(p / 100f * Math.Sin(p / 100f)),
                    (float)(p / 100f * Math.Sin(p / 20f)),2)).ToArray(),
                colors = Enumerable.Repeat(0xffffffff, 1000).ToArray(),
                handleString = "\uf1ce" //fa-circle-o-notch
            });
            //
            // Workspace.Prop(new PutPointCloud()
            // {
            //     name = "test_putpc2",
            //     xyzSzs = Enumerable.Range(0, 1000).Select(p => new Vector4((float)(p / 200f * Math.Cos(p / 200f)), (float)(p / 200f * Math.Sin(p / 200f)),
            //         (float)(p / 200f * Math.Sin(p / 10f)), 2)).ToArray(),
            //     colors = Enumerable.Repeat(0xffff00ff, 1000).ToArray(),
            //     newPosition = new Vector3(1,5,2),
            //     handleString = "\uf1ce" //fa-circle-o-notch
            // });

            
            
            // Test User defined mesh
            {
                void CreateSphereMesh(int resolution, out float[] positions)
                {
                    var vertices = new List<float>();
                    
                    // Generate triangles directly
                    for (int lat = 0; lat < resolution; lat++)
                    {
                        float theta1 = lat * MathF.PI / resolution;
                        float theta2 = (lat + 1) * MathF.PI / resolution;
                        
                        for (int lon = 0; lon < resolution; lon++)
                        {
                            float phi1 = lon * 2 * MathF.PI / resolution;
                            float phi2 = (lon + 1) * 2 * MathF.PI / resolution;

                            // Calculate vertices for both triangles
                            float x1 = MathF.Sin(theta1) * MathF.Cos(phi1);
                            float y1 = MathF.Cos(theta1);
                            float z1 = MathF.Sin(theta1) * MathF.Sin(phi1);

                            float x2 = MathF.Sin(theta2) * MathF.Cos(phi1);
                            float y2 = MathF.Cos(theta2);
                            float z2 = MathF.Sin(theta2) * MathF.Sin(phi1);

                            float x3 = MathF.Sin(theta1) * MathF.Cos(phi2);
                            float y3 = MathF.Cos(theta1);
                            float z3 = MathF.Sin(theta1) * MathF.Sin(phi2);

                            float x4 = MathF.Sin(theta2) * MathF.Cos(phi2);
                            float y4 = MathF.Cos(theta2);
                            float z4 = MathF.Sin(theta2) * MathF.Sin(phi2);

                            // First triangle
                            vertices.AddRange(new[] { x1, y1, z1 });
                            vertices.AddRange(new[] { x3, y3, z3 });
                            vertices.AddRange(new[] { x2, y2, z2 });

                            // Second triangle
                            vertices.AddRange(new[] { x2, y2, z2 });
                            vertices.AddRange(new[] { x3, y3, z3 });
                            vertices.AddRange(new[] { x4, y4, z4 });
                        }
                    }

                    positions = vertices.ToArray();
                }

                CreateSphereMesh(16, out var positions);
                
                Workspace.Prop(new DefineMesh() {
                    clsname = "custom1",
                    positions = positions,
                    color = 0xFF8844FF,  // Orange color with full alpha
                    smooth = true        // Use smooth normals
                });

                Workspace.Prop(new PutModelObject() {
                    clsName = "custom1", 
                    name = "sphere1",
                    newPosition = new Vector3(0, 0, 0)
                });

                float[] CreatePlaneMesh()
                {
                    return new List<float>()
                    {
                        -3f, -3f, 0f, // Bottom-left
                        3f, -3f, 0f, // Bottom-right 
                        3f, 3f, 0f, // Top-right
                        -3f, -3f, 0f, // Bottom-left
                        3f, 3f, 0f, // Top-right
                        -3f, 3f, 0f // Top-left
                    }.ToArray();
                }

                // Workspace.Prop(new DefineMesh()
                // {
                //     clsname = "custom2",
                //     positions = CreatePlaneMesh(),
                //     color = 0xFF4488FF, 
                //     smooth = true        // Use smooth normals
                // });
                // Workspace.Prop(new PutModelObject()
                // {
                //     clsName = "custom2",
                //     name = "plane",
                //     newPosition = new Vector3(0, 0, 0)
                // });

                // Create update thread
                new Thread(() => {
                    bool highRes = false;
                    while(true) {
                        Thread.Sleep(2000);
                        highRes = !highRes;
                        CreateSphereMesh(highRes ? 32 : 8, out positions);
                        
                        Workspace.Prop(new DefineMesh() {
                            clsname = "custom1",
                            positions = positions,
                            color = 0xFF8844FF,
                            smooth = highRes
                        });
                    }
                }).Start();
            }
            {
                Workspace.Prop(new LoadModel()
                {
                    detail = new Workspace.ModelDetail(
                        File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Soldier.glb"))
                    {
                        Center = new Vector3(0, 0, 0),
                        Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                        Scale = 1f
                    },
                    name = "soldier"
                });
                Workspace.Prop(new PutModelObject()
                    { clsName = "soldier", name = "s1", newPosition = new Vector3(1, 0, 0f) });
                
                Workspace.Prop(new LoadModel()
                {
                //     detail = new Workspace.ModelDetail(File.ReadAllBytes("LittlestTokyo.glb"))
                //     {
                //         Center = new Vector3(0, 0, -2),
                //         Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //         Scale = 0.01f
                //     },
                //     name = "soldier"
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("LittlestTokyo.glb"))
                    // {
                    //     Center = new Vector3(0, 0, -2),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //     Scale = 0.01f
                    // },
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\RobotExpressive\\RobotExpressive.glb"))
                    // {
                    //     Center = new Vector3(0, 0, 0),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //     Scale = 1f
                    // },
                    detail = new Workspace.ModelDetail(
                        File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Horse.glb"))
                    {
                        Center = new Vector3(0, 0, 0),
                        Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                        Scale = 0.01f
                    },
                    //detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\facecap.glb"))
                    //{
                    //    Center = new Vector3(0, 0, 0),
                    //    Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //    Scale = 1f
                    //},
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("armstreching.glb"))
                    // {
                    //     Center = new Vector3(0, 0, 0),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //     Scale = 1f
                    // },
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Michelle.glb"))
                    // {
                    //     Center = new Vector3(0, 0, 0),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //     Scale = 2f
                    // },
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Soldier.glb"))
                    // {
                    //     Center = new Vector3(0, 0, 0),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //     Scale = 1f
                    // },
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Parrot.glb"))
                    // {
                    //     Scale=0.01f
                    // },
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("model.glb"))
                    // {
                    //     Center = new Vector3(-0.5f, -1.25f, -0.55f),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //     Scale = 0.001f
                    // },
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("model---.glb"))
                    // {
                    //     Center = new Vector3(-1, 0, -0.2f),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI),
                    //     Scale = 0.001f
                    // },
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("absurd2.glb"))
                    // {
                    //     Center = new Vector3(0, 0, 0f),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI/2),
                    //     Scale = 1f
                    // },
                    name = "model_glb"
                });
                Workspace.Prop(new PutModelObject() { clsName = "model_glb", name = "glb1" , newPosition = -Vector3.UnitX});
                ;
                Workspace.Prop(new PutModelObject()
                    { clsName = "model_glb", name = "glb2", newPosition = new Vector3(2, 0, 0) });
            }
            
            System.Drawing.Bitmap bmp = new Bitmap("ganyu.png");
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            IntPtr ptr = bmpData.Scan0;
            int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
            byte[] rgbValues = new byte[bytes];
            Marshal.Copy(ptr, rgbValues, 0, bytes);
            bmp.UnlockBits(bmpData);

            Workspace.AddProp(new PutARGB()
            {
                height=bmp.Height,
                width = bmp.Width,
                name = "rgb1",
                requestRGBA = (() => rgbValues)
            });



            var CameraList = UsbCamera.FindDevices().Select(str => str.Replace(" ", "_")).ToArray();
            Console.WriteLine($"Cameras:\r\n{string.Join("\r\n", CameraList)}");
            UsbCamera.VideoFormat[] formats = UsbCamera.GetVideoFormat(0);
            var format = formats[0];
            var cached = new byte[format.Size.Height * format.Size.Width * 4];
            var fn = 0;
            var dynamicrgba = Workspace.AddProp(new PutARGB()
            {
                height = format.Size.Height,
                width = format.Size.Width,
                name = "rgba",
            });

            //PutRGBA.SetFFMpegPath("D:\\software\\ffmpeg\\ffmpeg-2024-05-13-git-37db0454e4-full_build\\bin\\ffmpeg.exe");

            var streamer = Workspace.AddProp(new PutARGB()
            {
                height = format.Size.Height,
                width = format.Size.Width,
                name = "rgbs",
            });
            var updater = streamer.StartStreaming();
            var frame = 0;
            camera = new UsbCamera(0, format, new UsbCamera.GrabberExchange()
            {
                action = (d, ptr, arg3) =>
                {
                    byte* pbr = (byte*)ptr;
                    for(int i=0; i<format.Size.Height; ++i)
                    for (int j = 0; j < format.Size.Width; ++j)
                    {
                        cached[((format.Size.Height-1-i) * format.Size.Width + j) * 4] = pbr[(i * format.Size.Width + j) * 3];
                        cached[((format.Size.Height-1-i) * format.Size.Width + j) * 4 + 1] = pbr[(i * format.Size.Width + j) * 3 + 1];
                        cached[((format.Size.Height-1-i) * format.Size.Width + j) * 4 + 2] = pbr[(i * format.Size.Width + j) * 3+2];
                        cached[((format.Size.Height-1-i) * format.Size.Width + j) * 4 + 3] = 255;
                    }

                    if (frame == 0)
                        dynamicrgba.UpdateRGBA(cached);
                    frame += 1;
                    updater(cached);
                }
            });
            camera.Start();


            Workspace.Prop(new PutImage()
            {
                name = "lskj",
                displayType =  PutImage.DisplayType.World_Billboard,
                rgbaName = "rgb1",
                newPosition = new Vector3(4, 0, 1),
                displayH = 64, //if billboard, displayH is pixel.
                displayW = 64,
            });

            Workspace.Prop(new PutImage()
            {
                name = "lskjz",
                rgbaName = "rgb1",
                newPosition = new Vector3(-4, 3, 0),
                newQuaternion = Quaternion.CreateFromYawPitchRoll(0,(float)Math.PI/2,0),
                displayH = 1, //if perspective, displayH is metric.
                displayW = 1,
            });

            Workspace.Prop(new PutImage()
            {
                name = "lskjp",
                rgbaName = "rgba",
                newPosition = new Vector3(-4,0,0),
                displayH = 1, //if perspective, displayH is metric.
                displayW = 1,
            });
            Workspace.Prop(new PutImage()
            {
                name = "lskjp2",
                rgbaName = "rgbs",
                newPosition = new Vector3(-4, -1, 0),
                displayH = 1, //if perspective, displayH is metric.
                displayW = 1,
            });

            // Workspace.Prop(new PutShape()
            // {
            //     name = "tl",
            //     billboard = true,
            //     shape=blahblah,
            // });
            // Workspace.Prop(new PutExtrudedGeometry()
            // {
            //     name = "tl",
            //     extrude=10,
            //     color=Color.Red,
            //     shape = blahblah,
            // });

            Workspace.Prop(new PutStraightLine()
            {
                name = "tl",
                propStart = "glb1",
                end = Vector3.Zero,
                width = 20,
                arrowType = Painter.ArrowType.End,
                color = Color.Red
            });
            SelectObject defaultAction = null;
            defaultAction = new SelectObject()
            {
                feedback = (tuples, _) =>
                {
                    if (tuples.Length == 0)
                        Console.WriteLine($"no selection");
                    else
                    {
                        Console.WriteLine($"selected {tuples[0].name}");
                        new GuizmoAction()
                        {
                            type = GuizmoAction.GuizmoType.MoveXYZ,
                            finished = () =>
                            {
                                Console.WriteLine("OKOK...");
                                defaultAction.SetSelection([]);
                            },
                            terminated = () =>
                            {
                                Console.WriteLine("Forget it...");
                                defaultAction.SetSelection([]);
                            }
                        }.Start();
                    }
                },
            };
            defaultAction.Start();
            defaultAction.SetObjectSelectable("test_putpc");
            defaultAction.SetObjectSelectable("glb1");
            defaultAction.SetObjectSelectable("glb2");
            new SetAppearance { useGround = true, useBorder = false }.Issue();

            // defaultAction.ChangeState(new SetObjectSubSelectableOrNot() { name = "glb2" });

            float fov = 45;
            bool use_cs = false;
            bool showglb1 = true;
            bool soilder_cs = true;
            bool btfh = true;
            GUI.PromptPanel(pb =>
            {
                if (pb.ButtonGroups("button group", ["A", "OK", "Cancel"], out var sel))
                {
                    Console.WriteLine(sel);
                    throw new Exception($"selected {sel} and throw exception!");
                }
                if (pb.Button("show alert"))
                {
                    UITools.Alert("this is a test alert diaglog. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.", t:pb.Panel.Terminal);
                }
                if (pb.Button("cyclegui select file"))
                {
                    if (UITools.FileBrowser("Select File to open", out var fn, t: pb.Panel.Terminal))
                    {
                        pb.Label(fn);
                        pb.DisplayFileLink(fn, "Open in explorer");
                    }
                }

                if (pb.Button("go 1m(ctrl+up)", shortcut:"ctrl+up"))
                {
                    Workspace.Prop(new TransformObject(){coord = TransformObject.Coord.Relative, type = TransformObject.Type.Pos, name="lskjp", pos = Vector3.UnitY, timeMs = 1000});
                }

                if (pb.Button("rot 90(left)", shortcut:"left"))
                {
                    Workspace.Prop(new TransformObject()
                    {
                        coord = TransformObject.Coord.Relative, type = TransformObject.Type.Rot, name = "lskjp",
                        quat = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(Math.PI / 2)), timeMs = 1000
                    });
                }
                if (pb.Button("rot -90(right)", shortcut: "right"))
                {
                    Workspace.Prop(new TransformObject()
                    {
                        coord = TransformObject.Coord.Relative,
                        type = TransformObject.Type.Rot,
                        name = "lskjp",
                        quat = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(Math.PI / 2)),
                        timeMs = 1000
                    });
                }

                if (pb.Button("Select Folder"))
                    if (pb.SelectFolder("TEST", out var dir))
                        Console.WriteLine(dir);
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
                    pb.Panel.Exit();

                var txt=pb.TextInput("Some text");
                if (pb.Button("Submit"))
                    Console.WriteLine(txt);

                // var sel = pb.Listbox("test", new[] { "1", "2", "a", "b", "ccc", "ddd", "eee", "fgh", "1" });
                // pb.Label($"sel={sel}");

                if (pb.Button("Long procedure"))
                {
                    pb.Panel.Freeze();
                    pb.Label("SHOW LABEL!");
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
                    pb.Panel.UnFreeze();
                }

                // var eventObj = Statics<PanelBuilder.GUINotify>.Declare(() => new());
                // if (pb.Button("async procedure"))
                // {
                //     eventObj.Clear();
                //     pb.Label("Notify external");
                //     new Thread(() =>
                //     {
                //         Console.WriteLine("perform time consuming operation");
                //         Thread.Sleep(1000);
                //         eventObj.Set();
                //         Console.WriteLine("perform time complete");
                //     }).Start();
                // }
                //
                //
                // if (pb.Notified(eventObj))
                // {
                //     pb.Label("Async procedure done");
                // }
                // else
                // {
                //     pb.Label("pending");
                // }

                if (pb.Notified(loopNotifier, out var ln))
                    pb.Label($"loop n={ln}");
                else 
                    pb.Label("not yet notified");
                if (pb.Button("Reset camera"))
                {
                    new SetCamera() {lookAt = Vector3.Zero, altitude = (float)(Math.PI/4), azimuth = 0, distance = 2}.Issue();
                };
                if (pb.DragFloat("Set fov(deg)", ref fov, 0.1f, 10, 140))
                {
                    new SetCamera() { fov = fov }.Issue();
                }

                if (pb.CheckBox("Use CrossSection", ref use_cs))
                    new SetAppearance() { useCrossSection = use_cs, clippingDirection = -Vector3.UnitY}.Issue();

                if (use_cs)
                {
                    float ycs = 0;
                    if (pb.DragFloat("Set Y", ref ycs, 0.01f, -10, 10))
                    {
                        new SetAppearance()
                        {
                            crossSectionPlanePos = new Vector3(0, ycs, 0)
                        }.Issue();
                    }
                }

                if (pb.CheckBox("btfh", ref btfh))
                {
                    new SetAppearance() { bring2front_onhovering = btfh}.Issue();
                }
                if (pb.CheckBox("Soldier show crosssection", ref soilder_cs))
                {
                    new SetPropApplyCrossSection() { namePattern = "s1", apply = soilder_cs }.Issue();
                }
                if (pb.CheckBox("show glb1", ref showglb1))
                {
                    new SetPropShowHide() { namePattern = "glb1", show = showglb1 }.Issue();
                }

                if (pb.Button("remove glb2"))
                {
                    WorkspaceProp.RemoveNamePattern("glb2");
                }
            });

            GUI.PromptPanel(pb =>
            {
                pb.Panel.SetDefaultDocking(Panel.Docking.None).ShowTitle(null).FixSize(240,36).InitPos(true, 0,64,1,1,1,1);
                pb.Label($"wnd2 iter={loops}");
                pb.Panel.Repaint();
            });

            GUI.PromptPanel(pb =>
            {
                pb.Panel.SetDefaultDocking(Panel.Docking.Bottom).ShowTitle("TEST Grow").InitSize(h: 36);
                pb.Label($"iter={loops}");
                pb.Panel.Repaint();
            });
            
        }
    }
}