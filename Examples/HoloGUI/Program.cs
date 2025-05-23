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
using HoloExample;
using System.Drawing.Imaging;
using static HoloExample.AngstrongHp60c;

namespace VRenderConsole
{
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static class Program
    {
        private static UsbCamera camera;

        static unsafe void Main(string[] args)
        {
            // var anstrong = new AngstrongHp60c(); // low performance, no use.
            var hik = new MyHikSteoro();

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();

            Terminal.RegisterRemotePanel(t=>
            {
                SelectObject defaultAction = null;
                defaultAction = new SelectObject()
                {
                    terminal = t,
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

                var vst = GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("TEST aux Viewport"), t);

                return pb =>
                {
                    pb.Label("Welcome!");
                    if (pb.Button("Click me"))
                    {
                        Console.WriteLine("Clicked��");
                        pb.Label("You clicked, and i show");
                    }

                    if (pb.Button("Throw an error"))
                    {
                        throw new Exception("Holy shit");
                    }

                    var txt = pb.TextInput("Some text");
                    if (pb.Button("Submit"))
                        Console.WriteLine(txt);
                    // new SetAppearance() { bring2front_onhovering = false, useGround = false }.IssueToTerminal(pb.Panel.Terminal);
                };
            });

            Task.Run(() =>
            {
                
                LeastServer.AddServingFiles("/debug", "D:\\src\\CycleGUI\\Emscripten\\WebDebug");
                LeastServer.AddServingFiles("/files", Path.Join(AppDomain.CurrentDomain.BaseDirectory, "htdocs"));

                WebTerminal.Use(ico: icoBytes);
            });


            bool test = true;

            // Workspace.Prop(new PutPointCloud()
            // {
            //     name = "test_putpc1",
            //     xyzSzs = Enumerable.Range(0,1000).Select(p=>new Vector4((float)(p/100f * Math.Cos(p / 100f)), (float)(p / 100f * Math.Sin(p / 100f)),
            //         (float)(p / 100f * Math.Sin(p / 20f) *0.3f),2)).ToArray(),
            //     colors = Enumerable.Repeat(0xffffffff, 1000).ToArray(),
            //     handleString = "\uf1ce" //fa-circle-o-notch
            // });
            
            PutModelObject s1;
            {
                // Workspace.Prop(new LoadModel()
                // {
                //     detail = new Workspace.ModelDetail(
                //         File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Soldier.glb"))
                //     {
                //         Center = new Vector3(0, 0, 0),
                //         Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //         Scale = 1f
                //     },
                //     name = "soldier"
                // });
                // s1 = Workspace.AddProp(new PutModelObject()
                // {
                //     clsName = "soldier", name = "s1", newPosition = new Vector3(1, 0, 0f),
                //     newQuaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(Math.PI))
                // });

                
                Workspace.Prop(new LoadModel()
                {
                    //sunshine_airportsunset.glb
                    // detail = new Workspace.ModelDetail(File.ReadAllBytes("sunshine_airportsunset.glb"))
                    // {
                    //     Center = new Vector3(0, 0, 0),
                    //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    //     Scale = 0.1f
                    // },

                    detail = new Workspace.ModelDetail(File.ReadAllBytes("LittlestTokyo.glb"))
                    {
                        Center = new Vector3(0, 0, -2),
                        Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                        Scale = 0.01f
                    },
                // //     name = "soldier"
                     // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\assets\\glb\\Bread.glb"))
                     // {
                     //     Center = new Vector3(0, 2, 0),
                     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                     //     Scale = 1f
                     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\RobotExpressive\\RobotExpressive.glb"))
                //     // {
                //     //     Center = new Vector3(0, 0, 0),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     //     Scale = 1f
                //     // },
                // detail = new Workspace.ModelDetail(
                //     File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Horse.glb"))
                // {
                //     Center = new Vector3(0, 0, 0),
                //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     Scale = 0.01f
                // },
                //     //detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\facecap.glb"))
                //     //{
                //     //    Center = new Vector3(0, 0, 0),
                //     //    Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     //    Scale = 1f
                //     //},
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("armstreching.glb"))
                //     // {
                //     //     Center = new Vector3(0, 0, 0),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     //     Scale = 1f
                //     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Michelle.glb"))
                //     // {
                //     //     Center = new Vector3(0, 0, 0),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     //     Scale = 2f
                //     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Soldier.glb"))
                //     // {
                //     //     Center = new Vector3(0, 0, 0),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     //     Scale = 1f
                //     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Parrot.glb"))
                //     // {
                //     //     Scale=0.01f
                //     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("model.glb"))
                //     // {
                //     //     Center = new Vector3(-0.5f, -1.25f, -0.55f),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     //     Scale = 0.001f
                //     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("model---.glb"))
                //     // {
                //     //     Center = new Vector3(-1, 0, -0.2f),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI),
                //     //     Scale = 0.001f
                //     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("absurd2.glb"))
                //     // {
                //     //     Center = new Vector3(0, 0, 0f),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI/2),
                //     //     Scale = 1f
                //     // },
                    name = "model_glb"
                });
                //
                Workspace.Prop(new PutModelObject() { clsName = "model_glb", name = "glb1" , newPosition = Vector3.UnitY});
                ;
                // Workspace.Prop(new LoadModel()
                // {
                //     detail = new Workspace.ModelDetail(
                //         File.ReadAllBytes("forklifter.glb"))
                //     {
                //         Center = new Vector3(0, 0, 0),
                //         Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //         Scale = 1f
                //     },
                //     name = "kiva"
                // });
                // Workspace.Prop(new PutModelObject()
                //     { clsName = "kiva", name = "glb2", newPosition = new Vector3(2, 2, 0) });
            }

            int radio = 0;
            int dropdown = 0;
            bool shine = false;
            GUI.PromptPanel(pb =>
            {
                if (pb.Button("Import PCD cloud"))
                {
                    if (pb.OpenFile("Import PCD Cloud", "PCD File(*.PCD)|*.pcd", out var fn))
                    {
                        Console.WriteLine($"OK:{fn}");
                    }
                }

                pb.RadioButtons("radios", ["AAA", "BBB"], ref radio);
                pb.DropdownBox("dropdown", ["drop 0", "drop 1"], ref dropdown);
                if (pb.Button("full screen"))
                {
                    new SetFullScreen().IssueToDefault();
                }
                if (pb.Button("windowed"))
                {
                    new SetFullScreen(){fullscreen = false}.IssueToDefault();
                }

                if (pb.CheckBox("shine", ref shine))
                {
                    new SetObjectApperance() { namePattern = "glb1", shine_color = shine ? Color.Red.RGBA8() : 0 }
                        .IssueToDefault();
                }
                if (pb.Button("get cam pos"))
                {
                    new QueryViewportState(){callback = (state =>
                    {
                        Console.WriteLine("POS=" + state.CameraPosition);
                    })}.IssueToDefault();
                }

                if (pb.Button("Load Model"))
                {
                    if (UITools.FileBrowser("select model", out string fn))
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes(fn))
                            {
                                Center = new Vector3(0, 2, 0),
                                Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                                Scale = 1f
                            },
                            name = "model_glb"
                        });
                }
                pb.Image("ImageTest","rgb1");

                if (pb.Button("capture"))
                {
                    new CaptureRenderedViewport()
                    {
                        callback = (img =>
                        {
                            void GenJpg(byte[] rgb, int w, int h)
                            {
                                using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                                var rect = new Rectangle(0, 0, w, h);
                                var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                                IntPtr ptr = bmpData.Scan0;
                                for (var i = 0; i < h; i++)
                                    Marshal.Copy(rgb, w * i * 3, ptr + bmpData.Stride * i, w * 3);

                                bitmap.UnlockBits(bmpData);

                                bitmap.Save("capture.jpg");
                            }
                            GenJpg(img.bytes, img.width, img.height);
                        })
                    }.IssueToDefault();
                }
                int id = pb.ListBox("select cam mode", new[] { "normal", "vr", "et_hologram" });
                new SetCamera() { displayMode = (SetCamera.DisplayMode)id }.IssueToDefault();
            });
            
        }
    }
}