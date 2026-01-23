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


            new Thread(() =>
            {
                Terminal.RegisterRemotePanel(t => pb => { pb.Label("TEST");});
                LeastServer.AddServingFiles("/", "D:\\src\\CycleGUI\\Emscripten\\WebDebug");
                LeastServer.AddServingFiles("/files", Path.Join(AppDomain.CurrentDomain.BaseDirectory, "htdocs"));
                WebTerminal.Use(ico: icoBytes);
            }).Start();

            var path = "D:\\res\\glb";

            GUI.PromptPanel(pb =>
            {
                void Model(string name, Quaternion q, Vector3 v3, float scale,
                    Vector3 color_bias = default, float color_scale = 1, float brightness = 1,
                    SetCamera setcam = null, SetModelObjectProperty pty = null, bool force_dblface = false, float normal_shading = 0, SetAppearance app = null,
                    bool rotate = false, Vector3[] la = null, string tracking = null)
                {
                    if (pb.Button(name))
                    {
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes(Path.Join(path, $"{name}.glb")))
                            {
                                Center = v3,
                                Rotate = q,
                                Scale = scale,
                                ColorBias = color_bias,
                                ColorScale = color_scale,
                                Brightness = brightness,
                                ForceDblFace = force_dblface,
                                NormalShading = normal_shading
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                        { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToAllTerminals();

                        // set camera.
                        if (setcam == null)
                            new SetCamera()
                            {
                                azimuth = (float)(-Math.PI / 2),
                                altitude = (float)(Math.PI / 6),
                                lookAt = Vector3.Zero,
                                distance = 5,
                                world2phy = 100,
                                mmb_freelook = false
                            }.IssueToAllTerminals();
                        else
                            setcam.IssueToAllTerminals();

                        if (pty != null)
                        {
                            pty.namePattern = "glb1";
                            pty.IssueToAllTerminals();
                        }
                        if (app != null)
                            app.IssueToAllTerminals();

                        if (tracking != null)
                            Workspace.Prop(new SetObjectMoonTo() { earth = $"glb1::{tracking}", name = "me::camera" });
                        else
                            Workspace.Prop(new SetObjectMoonTo() { name = "me::camera" });
                    }
                }

                var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                Model("futuristic_hallway_with_patrolling_robot", rq, new Vector3(0, 0, 0), 1f,
                    setcam: new SetCamera() { azimuth = -1.534f, altitude = -0.052f, lookAt = new Vector3(-0.4156f, 5.7967f, 2.0581f), distance = 10.2414f, world2phy = 100f },
                    app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.25f });


            });

        }
    }
}