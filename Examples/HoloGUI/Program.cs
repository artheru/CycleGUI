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

namespace VRenderConsole
{
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static class Program
    {
        private static UsbCamera camera;

        static unsafe void Main(string[] args)
        { 
           // var anstrong = new AngstrongHp60c(); // low performance, no use.

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();

            bool test = true;

            Workspace.Prop(new PutPointCloud()
            {
                name = "test_putpc1",
                xyzSzs = Enumerable.Range(0,1000).Select(p=>new Vector4((float)(p/100f * Math.Cos(p / 100f)), (float)(p / 100f * Math.Sin(p / 100f)),
                    (float)(p / 100f * Math.Sin(p / 20f) *0.3f),2)).ToArray(),
                colors = Enumerable.Repeat(0xffffffff, 1000).ToArray(),
                handleString = "\uf1ce" //fa-circle-o-notch
            });
            
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
                //     detail = new Workspace.ModelDetail(File.ReadAllBytes("LittlestTokyo.glb"))
                //     {
                //         Center = new Vector3(0, 0, -2),
                //         Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //         Scale = 0.01f
                //     },
                // //     name = "soldier"
                     // detail = new Workspace.ModelDetail(File.ReadAllBytes("LittlestTokyo.glb"))
                     // {
                     //     Center = new Vector3(0, 2, 0),
                     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                     //     Scale = 0.01f
                     // },
                //     // detail = new Workspace.ModelDetail(File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\RobotExpressive\\RobotExpressive.glb"))
                //     // {
                //     //     Center = new Vector3(0, 0, 0),
                //     //     Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                //     //     Scale = 1f
                //     // },
                detail = new Workspace.ModelDetail(
                    File.ReadAllBytes("D:\\ref\\three.js-master\\examples\\models\\gltf\\Horse.glb"))
                {
                    Center = new Vector3(0, 0, 0),
                    Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    Scale = 0.01f
                },
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
                Workspace.Prop(new PutModelObject() { clsName = "model_glb", name = "glb1" , newPosition = -Vector3.UnitX});
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

            GUI.PromptPanel(pb =>
            {
                if (pb.Button("full screen"))
                {
                    new SetFullScreen().IssueToDefault();
                }
                if (pb.Button("windowed"))
                {
                    new SetFullScreen(){fullscreen = false}.IssueToDefault();
                }

                int id = pb.ListBox("select cam mode", new[] { "normal", "vr", "et_hologram" });
                new SetCamera() { displayMode = (SetCamera.DisplayMode)id }.IssueToDefault();
            });
            
        }
    }
}