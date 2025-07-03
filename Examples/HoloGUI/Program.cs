using System.Drawing;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;
using GitHub.secile.Video;
using Newtonsoft.Json;
using static HoloExample.AngstrongHp60c;
using Path = System.IO.Path;

namespace HoloExample
{
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static partial class Program
    {
        private static UsbCamera camera;

        public class cam_calib
        {
            public Vector3 translation;
            public Quaternion rotation = Quaternion.Identity;
            public float fx = 250, fy = 250, fd=1;
            public float bfx = 0;
        }
        public struct MyPnt
        {
            public byte r, g, b;
            public ushort depth;
        }

        public class cam_frame
        {
            public MyPnt[] pnts;
            public int w, h;
        }

        static private cam_calib[] calibs=[];

        static unsafe void Main(string[] args)
        {
            // var anstrong = new AngstrongHp60c(); // low performance, no use.
            var hik = new MyHikSteoro();

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("HOLO_DEMO");
            LocalTerminal.Start();



            bool test = true;



            var sun = 0f;
            var wtp = 100f;
            var useCrossSection = false;
            var useEDL = true;
            var useSSAO = true;
            var useGround = true;
            var useBorder = true;
            var useBloom = true;
            var drawGrid = true;
            var drawGuizmo = true;
            var freelook = false;

            Vector3 campos = Vector3.Zero, lookat = Vector3.Zero;
            var ir = false;
            Vector3[] lookats = [];
            var li = 0;
            new Thread(() =>
            {
                while (true)
                {
                    if (ir)
                    {
                        Workspace.Prop(new TransformObject()
                        {
                            name = "glb1",
                            quat = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(Math.PI * 0.5)),
                            coord = TransformObject.Coord.Relative,
                            timeMs = 4000
                        });
                    }

                    if (lookats!=null && lookats.Length > 0)
                    {
                        li += 1;
                        if (li >= lookats.Length) li = 0;
                        Workspace.Prop(new TransformObject()
                        {
                            name = "glb1",
                            pos = lookats[0] - lookats[li],
                            coord = TransformObject.Coord.Absolute,
                            timeMs = 4000
                        });
                    }

                    Thread.Sleep(5000);
                }
            }).Start();


            Panel calib_panel = null;
            bool show_remote = false;
            string addr = "http://192.168.0.195:8007/get_cams";

            cam_frame[] frames = [];

            var painter = Painter.GetPainter("holo_points");

            void PaintRemote()
            {
                painter.Clear();

                for (int i = 0; i < frames.Length; ++i)
                {
                    var calib = calibs == null || i >= calibs.Length ? new cam_calib() : calibs[i];
                    var q = calibs[i].rotation;
                    var t = calibs[i].translation;

                    var RotationMatrix = Matrix4x4.CreateFromQuaternion(q);

                    var fac = 0.9f;
                    var cr = (int)(i == 0 ? 255*(1-fac) : 0);
                    var cg = (int)(i == 1 ? 255*(1-fac) : 0);
                    var cb = (int)(i == 2 ? 255*(1-fac) : 0);

                    for (int j = 0; j < frames[i].pnts.Length; ++j)
                    {
                        // if (j % 100 != 0) continue;
                        var cp = frames[i].pnts[j];
                        var xyzX = cp.depth * calib.fd;
                        xyzX -= calib.bfx / xyzX;
                        if (xyzX < 20 || xyzX > 2000) continue;

                        // ok fuck, we use 640x480.
                        var x = j % 320;
                        var y = j / 320;
                        var xyzY = -((float)x - 160) * xyzX / calib.fx;
                        var xyzZ = -((float)y - 120) * xyzX / calib.fy;

                        var xx = RotationMatrix.M11 * xyzX + RotationMatrix.M12 * xyzY +
                                 RotationMatrix.M13 * xyzZ;
                        var yy = RotationMatrix.M21 * xyzX + RotationMatrix.M22 * xyzY +
                                 RotationMatrix.M23 * xyzZ;
                        var zz = RotationMatrix.M31 * xyzX + RotationMatrix.M32 * xyzY +
                                 RotationMatrix.M33 * xyzZ;
                        painter.DrawDotMM(Color.FromArgb((int)(cp.b *fac + cr), (int)(cp.g *fac+ cg), (int)(cp.r *fac + cb)),
                            new Vector3(xx, yy, zz) + t, 0.5f);
                    }
                }
            }

            new Thread(() =>
            {
                var hc = new HttpClient();
                while (true)
                {
                    if (!show_remote)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    // grab remote and display.
                    var result = hc.GetByteArrayAsync(addr).Result;

                    using var msi = new MemoryStream(result);
                    using var mso = new MemoryStream();
                    
                    using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                        gs.CopyTo(mso);

                    using var ms=new MemoryStream(mso.ToArray());
                    using var br = new BinaryReader(ms);
                    int num = br.ReadInt32();
                    var ffs = new cam_frame[num];
                    for (int i = 0; i < num; i++)
                    {
                        var vf = new cam_frame();
                        var hh = vf.h = br.ReadInt32();
                        var ww = vf.w = br.ReadInt32();
                        vf.pnts = new MyPnt[hh * ww];
                        for (int j = 0; j < vf.pnts.Length; j++)
                        {
                            vf.pnts[j] = new MyPnt();
                            vf.pnts[j].r = br.ReadByte();
                            vf.pnts[j].g = br.ReadByte();
                            vf.pnts[j].b = br.ReadByte();
                            vf.pnts[j].depth = br.ReadUInt16();
                        }

                        ffs[i] = vf;
                    }

                    frames = ffs;

                    if (calibs.Length < frames.Length)
                    {
                        if (File.Exists("cam_calibs.json"))
                            calibs = JsonConvert.DeserializeObject<cam_calib[]>(File.ReadAllText("cam_calibs.json"));
                        else
                        {
                            calibs = new cam_calib[frames.Length];
                            for (int i = 0; i < calibs.Length; i++) calibs[i] = new();
                        }
                    }

                    // PaintRemote();
                }
            }){Name = "holo_remote"}.Start();


            GUI.PromptPanel(pb =>
            {
                if (pb.Button("Go Hologram"))
                {
                    new SetFullScreen().IssueToDefault();
                    new SetCamera() { displayMode = SetCamera.DisplayMode.EyeTrackedHolography }.IssueToDefault();
                }

                {
                    pb.CollapsingHeaderStart("Appearance Settings");

                    if (pb.Button("GetPos"))
                    {
                        new QueryViewportState()
                        {
                            callback = vs =>
                            {
                                campos = vs.CameraPosition;
                                lookat = vs.LookAt;
                                pb.Panel.Repaint();
                            }
                        }.IssueToDefault();

                    }

                    // Calculate azimuth and altitude from camera position and lookat
                    float azimuth = 0f, altitude = 0f;
                    if ((campos - lookat).Length() > 0.001f)
                    {
                        Vector3 direction = Vector3.Normalize(campos - lookat);
                        
                        // Calculate azimuth (horizontal angle) in degrees
                        azimuth = (float)(Math.Atan2(direction.Y, direction.X) );
                        
                        // Calculate altitude (vertical angle) in degrees
                        altitude = (float)(Math.Asin(direction.Z));
                    }
                    
                    pb.Label($"azimuth={azimuth:F1}, altitude={altitude:F1}");

                    string B(bool v) => v.ToString().ToLower();
                    pb.Label($"lookat={lookat}, d={(campos-lookat).Length()}");
                    pb.SelectableText("code", $"setcam:new SetCamera(){{azimuth = {azimuth:F3}f, altitude = {altitude:F3}f, lookAt = new Vector3({lookat.X:F4}f, {lookat.Y:F4}f, {lookat.Z:F4}f), " +
                                              $"distance = {(campos - lookat).Length():F4}f, world2phy={wtp}f}},\r\n" +
                                              $"app:new SetAppearance(){{useGround = {B(useGround)}, drawGrid = {B(useGround)}, " +
                                              $"drawGuizmo = {B(drawGuizmo)}, sun_altitude = {sun:F2}f}}, rotate:{B(ir)}");

                    var appearanceChanged = false;
                    appearanceChanged |= pb.CheckBox("Use EyeDomeLighting", ref useEDL);
                    appearanceChanged |= pb.CheckBox("Use SSAO", ref useSSAO);
                    appearanceChanged |= pb.CheckBox("Use Ground", ref useGround);
                    appearanceChanged |= pb.CheckBox("Use Border", ref useBorder);
                    appearanceChanged |= pb.CheckBox("Use Bloom", ref useBloom);
                    appearanceChanged |= pb.CheckBox("Draw Grid", ref drawGrid);
                    appearanceChanged |= pb.CheckBox("Draw Guizmo", ref drawGuizmo);
                    appearanceChanged |= pb.CheckBox("Rotate", ref ir);
                    appearanceChanged |= pb.DragFloat("sun", ref sun, 0.01f, 0f, 1.57f);
                    appearanceChanged |= pb.DragFloat("w2p", ref wtp, 1f, 1f, 1000f);
                    //...
                    appearanceChanged |= pb.CheckBox("freelook", ref freelook);


                    if (appearanceChanged)
                    {
                        new SetAppearance()
                        {
                            useEDL = useEDL,
                            useSSAO = useSSAO,
                            useGround = useGround,
                            useBorder = useBorder,
                            useBloom = useBloom,
                            drawGrid = drawGrid,
                            drawGuizmo = drawGuizmo,
                            sun_altitude = sun
                        }.Issue();
                        new SetCamera()
                        {
                            world2phy = wtp,
                            mmb_freelook = freelook,
                        }.IssueToDefault();
                    }
                    pb.CollapsingHeaderEnd();
                }


                var path = "D:\\res\\glb";
                var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);

                void Model(string name, Quaternion q, Vector3 v3, float scale,
                    Vector3 color_bias = default, float color_scale = 1, float brightness = 1,
                    SetCamera setcam = null, SetModelObjectProperty pty = null, bool force_dblface = false, float normal_shading = 0, SetAppearance app = null,
                    bool rotate = false, Vector3[] la = null, string tracking=null)
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
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

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
                            }.IssueToDefault();
                        else
                            setcam.IssueToDefault();

                        if (pty != null)
                        {
                            pty.namePattern = "glb1";
                            pty.IssueToDefault();
                        }
                        if (app != null)
                            app.IssueToDefault();

                        ir = rotate;
                        if (la != null)
                        {
                            lookats = [setcam==null?Vector3.Zero:setcam.lookAt, .. la];
                        }
                        else lookats = null;

                        if (tracking != null)
                            Workspace.Prop(new SetObjectMoonTo() { earth = $"glb1::{tracking}", name = "me::camera" });
                        else
                            Workspace.Prop(new SetObjectMoonTo() {name = "me::camera" });
                    }
                }

                {
                    pb.CollapsingHeaderStart("Selected Model Displaying");

                    pb.SeparatorText("Holo capability");
                    Model("futuristic_hallway_with_patrolling_robot", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.534f, altitude = -0.052f, lookAt = new Vector3(-0.4156f, 5.7967f, 2.0581f), distance = 10.2414f, world2phy = 100f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.25f });

                    Model("opposed_piston_engine_mechanism", rq, new Vector3(0, 0, 0), 1f, 
                        setcam: new SetCamera() { azimuth = 1.612f, altitude = -0.044f, lookAt = new Vector3(-0.0518f, -0.0123f, 0.0000f), distance = 0.0253f, world2phy = 927f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f }, rotate: false);

                    Model("12_animated_butterflies", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f,
                        setcam: new SetCamera() { azimuth = -1.511f, altitude = 0.359f, lookAt = new Vector3(-0.4562f, 9.9506f, -1.3176f), distance = 9.6200f, world2phy = 25f },
                        app: new SetAppearance() { useGround = true, drawGrid = false, drawGuizmo = false, sun_altitude = 0.12f }, rotate: true);

                    Model("game_pirate_adventure_map", rq, new Vector3(0, 0, 0), 0.001f,
                        setcam: new SetCamera()
                        {
                            azimuth = 1.598f,
                            altitude = -0.042f,
                            lookAt = new Vector3(0.4619f, -20.3686f, 0.9524f),
                            distance = 17.5073f,
                            world2phy = 136f
                        },
                        app: new SetAppearance()
                            { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f },
                        la: [new Vector3(0, -90, 1.0f)]);

                    Model("caterpillar_work_boot", rq, new Vector3(0, 0, 0), 3f, color_scale: 2.3f,
                        setcam: new SetCamera() { azimuth = -1.433f, altitude = 0.586f, lookAt = new Vector3(-0.0270f, 0.3658f, 0.0063f), distance = 0.5476f, world2phy = 288f },
                        app: new SetAppearance() { useGround = true, drawGrid = false, drawGuizmo = false, sun_altitude = 0.22f }, rotate: true);

                    Model("lymphatic_system_an_overview", Quaternion.Identity, new Vector3(0, 0, 1.5f), 0.002f,
                        setcam: new SetCamera() { azimuth = -0.009f, altitude = 1.178f, lookAt = new Vector3(-0.2174f, 0.2541f, 0.0000f), distance = 0.6687f, world2phy = 339 },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                        la: [new Vector3(-0.2837f, -0.5745f, 0), new Vector3(-0.24f, -1.83f, 0)]
                    );


                    pb.SeparatorText("Scene");
                    Model("space_loop_city", rq, new Vector3(0, 0, 0), 0.001f, setcam: new SetCamera() { azimuth = 0.642f, altitude = 0.987f, lookAt = new Vector3(3.5570f, -0.4249f, 0.0000f), distance = 7.5463f, world2phy = 104f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = true, sun_altitude = 0.00f }, rotate: false);
                    Model("LittlestTokyo", rq, new Vector3(0, 0, -2), 0.01f, setcam:new SetCamera()
                    {
                        azimuth = 2.8f, altitude = 0.1f, lookAt = new Vector3(1.36f, -1.19f, 0.7f), distance = 3.74f,
                        mmb_freelook = false, world2phy = 100,
                    }, app: new SetAppearance() { useSSAO = true, useBloom = true, drawGrid = true, drawGuizmo = false, useGround = true, sun_altitude = 0f });
                    Model("guernica-3d", rq, new Vector3(0, 0, 0), 1f, setcam:new SetCamera()
                    {
                        azimuth = -1.6f, altitude = -0.2f, lookAt =new Vector3(-0.15f, 3.7f, 1.486f), distance = 3.69f,
                        world2phy = 170
                    });
                    Model("sphere_explosion", rq, new Vector3(0, 0, 0), 0.03f,
                        setcam: new SetCamera() { azimuth = -1.585f, altitude = 0.055f, lookAt = new Vector3(0.1904f, 3.5741f, 2.8654f), distance = 4.5170f, world2phy = 133f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false
                    );
                    Model("truck_hit_brickwall_00_free", 
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -(float)(Math.PI / 2)) * rq, new Vector3(0, 0, 0), 1f,
                        setcam:new SetCamera(){azimuth = -3.1f, altitude = -0.2f, lookAt = new Vector3(1.128f, 0f, 0.907f), distance = 2.458f, world2phy = 185},
                        app:new SetAppearance(){useSSAO = true, useBloom = true, drawGrid = false, drawGuizmo = false, useGround = true, sun_altitude = 0f});

                    Model("akm_fps_animation", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = 1.499f, altitude = -0.015f, lookAt = new Vector3(-0.0711f, -1.2494f, 0.0020f), distance = 0.1090f, world2phy = 221f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f });

                    Model("bunny_swimsuit_black_pubg", rq, new Vector3(0, 0, 0), 1f, pty: new() { baseAnimId = 1 },
                        force_dblface: true,
                        setcam: new SetCamera() { azimuth = 0.001f, altitude = -0.010f, lookAt = new Vector3(-0.0303f, -1.3955f, 0.9011f), distance = 3.0737f, world2phy = 100f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.25f });

                    Model("character_fight", rq, new Vector3(0, 0, 0), 1f,
                        setcam:new SetCamera(){azimuth = -1.6f, altitude = -0.2f, lookAt = new Vector3(0.03f, 1.46f, 0.368f), distance = 1.268f, world2phy = 233},
                        app:new SetAppearance(){useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f});

                    //Model("ganyu_shake3", rq, new Vector3(0, 0, 0), 1f); sparse morphtargets not yet supported.


                    pb.SeparatorText("Masterpiece revival");
                    Model("isleworth-mona-lisa-3d", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.531f, altitude = 0.047f, lookAt = new Vector3(0.1314f, 3.9883f, 1.5768f), distance = 3.8713f, world2phy = 81f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f });
                    Model("reclining-nude-3d", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.492f, altitude = -0.233f, lookAt = new Vector3(-0.2552f, 2.0698f, 1.8678f), distance = 2.4158f, world2phy = 100f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f });
                    Model("persistence-of-memory-3d", rq, new Vector3(0, 0, 0), 1f, color_bias: new Vector3(0.05f),
                        color_scale: 1.2f,
                        setcam: new SetCamera() { azimuth = -1.571f, altitude = -0.097f, lookAt = new Vector3(0.2935f, 7.4459f, 2.1911f), distance = 7.2397f, world2phy = 100f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f });

                    pb.SeparatorText("Game");
                    Model("cuphead_-_hilda_berg_boss_fight", rq, new Vector3(0, 0, 0), 1f, tracking: "propeller_0",
                        setcam: new SetCamera() { azimuth = -3.042f, altitude = 0.052f, lookAt = new Vector3(4.9085f, 0.4343f, -0.2155f), distance = 4.8030f, world2phy = 466f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.06f }, rotate: false);
                    Model("pac-man_remaster", rq, new Vector3(0, 0, 0), 1f, tracking: "Object_957",
                        setcam: new SetCamera() { azimuth = -1.574f, altitude = 0.833f, lookAt = new Vector3(0.2429f, 1.6750f, -2.3863f), distance = 3.1820f, world2phy = 91f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f }, rotate: false);
                    Model("ftm", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = 1.538f, altitude = -0.146f, lookAt = new Vector3(2.5576f, 2.7484f, 0.0000f), distance = 2.4586f, world2phy = 44f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f });


                    pb.SeparatorText("Art-work");
                    Model("sea_keep_lonely_watcher", rq, new Vector3(0, 0, 0), 0.01f, setcam: new SetCamera() { azimuth = -1.941f, altitude = 0.571f, lookAt = new Vector3(0.9007f, 0.8088f, -0.0205f), distance = 2.6621f, world2phy = 69f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = true, sun_altitude = 0.00f },
                        rotate: true);
                    Model("rossbandiger", rq, new Vector3(0, 0, 0), 0.1f,
                        setcam: new SetCamera() { azimuth = -0.866f, altitude = -0.081f, lookAt = new Vector3(-1.9557f, 2.4057f, 1.2087f), distance = 3.4385f, world2phy = 142f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false);



                    pb.SeparatorText("Wall-paper scene");
                    Model("deja_vu_full_scene", rq, new Vector3(0, 0, 0), 0.01f,
                        setcam: new SetCamera() { azimuth = -2.024f, altitude = 0.141f, lookAt = new Vector3(4.0292f, 1.8159f, -0.5264f), distance = 3.1716f, world2phy = 69f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f });

                    Model("pika_girl", rq, new Vector3(0, 0, -2f), 0.3f, force_dblface: true,
                        setcam: new SetCamera() { azimuth = -1.546f, altitude = -0.167f, lookAt = new Vector3(0.2188f, 3.2804f, 1.3867f), distance = 3.4607f, world2phy = 100f },
                        app: new SetAppearance() { useGround = true, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);


                    pb.SeparatorText("Object show");

                    Model("sukhoi_su-35_fighter_jet", rq, new Vector3(0, 0, 0), 0.1f,
                        setcam: new SetCamera() { azimuth = -0.950f, altitude = -0.762f, lookAt = new Vector3(0.0952f, -0.1327f, 0.0640f), distance = 0.3891f, world2phy = 301f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.22f }, rotate: true);

                    Model("ship_in_a_bottle", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f, color_scale: 1.2f,
                        setcam: new SetCamera() { azimuth = -0.250f, altitude = 0.398f, lookAt = new Vector3(0.2728f, 0.3214f, 0.0031f), distance = 1.1198f, world2phy = 50f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.22f }, rotate: true);
                    
                    pb.SeparatorText("Horrors");

                    Model("demogorgon_rig", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.539f, altitude = -0.047f, lookAt = new Vector3(0.1473f, 3.3431f, 1.3490f), distance = 3.1742f, world2phy = 100 },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.22f }, rotate: false
                    );
                    Model("hallucination_huggy_-_poppy_playtime_chapter_3", rq, new Vector3(0, 0, 0), 7f,
                        setcam: new SetCamera() { azimuth = -1.541f, altitude = -0.139f, lookAt = new Vector3(-0.0615f, 0.2407f, 1.3110f), distance = 2.2819f, world2phy = 306f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.22f }, rotate: false);


                    pb.SeparatorText("Concept art");
                    Model("dreamsong", rq, new Vector3(0, 0, 0), 0.01f, brightness: 2.0f,
                        setcam: new SetCamera() { azimuth = -2.243f, altitude = 0.411f, lookAt = new Vector3(1.3211f, 1.3635f, 0.2820f), distance = 2.2895f, world2phy = 215f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f },
                        rotate: true);
                    Model("elaina_-_the_witchs_journeysummerwhitedress", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.379f, altitude = -0.039f, lookAt = new Vector3(-0.1140f, 1.3472f, 3.6802f), distance = 1.6533f, world2phy = 246f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                        la: [new Vector3(0, 1.3f, 0.64f)]);


                    pb.SeparatorText("Medical");
                    Model("injected-human-foetus-14-weeks-old-microct", rq, new Vector3(0, 0, 0), 0.1f, color_scale: 0.6f, normal_shading: 0.4f,
                        setcam: new SetCamera() { azimuth = 3.119f, altitude = 1.270f, lookAt = new Vector3(-0.2125f, -0.4736f, 0.0000f), distance = 0.2000f, world2phy = 47f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f });
                    Model("arteres_du_tronc", rq, new Vector3(0, 3, -5), 0.01f, color_bias: new Vector3(-0.1f),
                        setcam: new SetCamera() { azimuth = -1.595f, altitude = -0.241f, lookAt = new Vector3(0.8545f, 4.8215f, 4.5248f), distance = 4.8093f, world2phy = 62f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f }, rotate: true);
                    Model("visible_interactive_human_-_exploding_skull", rq, new Vector3(0, 0, 0), 0.1f,
                        setcam: new SetCamera() { azimuth = -1.477f, altitude = 0.189f, lookAt = new Vector3(-0.0949f, 2.6640f, -0.2433f), distance = 2.3287f, world2phy = 100f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f , useBloom = false}, rotate: true);

                    pb.SeparatorText("3D Reconstruction");
                    Model("mar_saba_monastery", rq, new Vector3(0, 0, 0), 1f, color_scale: 1.7f, 
                        setcam: new SetCamera() { azimuth = 1.334f, altitude = 0.252f, lookAt = new Vector3(-5.3135f, -40.7391f, 6.6314f), distance = 43.0146f, world2phy = 8f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                        la: [new Vector3(38.1968f, -51.7238f, 8.3257f), new Vector3(-100.3572f, -15.9295f, 0.1662f)]
                    );
                    Model("new_york_city._manhattan", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = 0.619f, altitude = 0.537f, lookAt = new Vector3(-2.4288f, -2.1959f, -2.4700f), distance = 4.0167f, world2phy = 72f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f }, rotate: false);

                    Model("skeleton_excavation_dataset", rq, new Vector3(0, 0, -3), 1f, color_scale: 1.2f,
                        setcam: new SetCamera() { azimuth = 3.109f, altitude = 1.157f, lookAt = new Vector3(-0.0753f, -0.4006f, -0.9518f), distance = 0.6747f, world2phy = 644f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false,
                        la: [new Vector3(0, 0.15f, -0.95f)]);

                    pb.SeparatorText("Various applications");
                    Model("black_honey_-_robotic_arm", rq, new Vector3(0, 0, 0), 1f, color_scale: 2f,
                        setcam: new SetCamera() { azimuth = -1.587f, altitude = 0.092f, lookAt = new Vector3(0.1865f, 6.7643f, 0.0911f), distance = 7.2657f, world2phy = 223f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false);
                    Model("airshaper_demo_beta_-_3d_annotations", rq, new Vector3(3, -3.5f, -1.0f), 1f, force_dblface: true,
                        setcam: new SetCamera() { azimuth = -1.559f, altitude = 0.526f, lookAt = new Vector3(-0.2166f, 0.1917f, 0.1191f), distance = 0.7298f, world2phy = 355f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                    Model("just_a_girl", rq, new Vector3(0, 0, 0), 0.005f,
                        setcam: new SetCamera() { azimuth = -1.577f, altitude = -0.228f, lookAt = new Vector3(0.0679f, 0.5771f, 0.4535f), distance = 0.6964f, world2phy = 646f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = true, sun_altitude = 0.00f }, rotate: true);
                    Model("sayuri_dance_fix", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.503f, altitude = -0.026f, lookAt = new Vector3(-0.4181f, 4.0394f, 1.3346f), distance = 4.0506f, world2phy = 85f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                    Model("momoi_sea-salt_summer__farlight_84_characters", rq, new Vector3(0, 0, 0), 0.5f,
                        color_scale: 1.3f, setcam: new SetCamera() { azimuth = -1.637f, altitude = -0.073f, lookAt = new Vector3(0.0567f, 0.4273f, 0.8764f), distance = 0.5258f, world2phy = 901f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false,
                        la: [new Vector3(0.0506f, 0.4128f, 0.2559f)]); 
                    Model("howcow", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.478f, altitude = 0.301f, lookAt = new Vector3(-0.2003f, 2.4462f, 5.4263f), distance = 3.0354f, world2phy = 85f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                        la: [new Vector3(0.12f, 0.55f, 0.779f)]);


                    pb.SeparatorText("Lewd");
                    Model("girl-body-scan-studio-5", rq, new Vector3(0, 0, 0), 0.1f,
                        setcam: new SetCamera() { azimuth = -1.897f, altitude = -0.092f, lookAt = new Vector3(0.4457f, 1.0407f, 0.9328f), distance = 1.4756f, world2phy = 142f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                    Model("tifa_piss", rq * rq, new Vector3(0, 0, 0), 0.001f,
                        setcam: new SetCamera() { azimuth = -1.583f, altitude = -0.016f, lookAt = new Vector3(0.1340f, 3.1133f, 2.0719f), distance = 4.2737f, world2phy = 100f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = true, sun_altitude = 0.00f }, rotate: false);
                    Model("uzuki_topless_panty", rq, new Vector3(0, 0, 0), 2f,
                        setcam: new SetCamera() { azimuth = -1.407f, altitude = -0.296f, lookAt = new Vector3(0.2848f, -0.2564f, 0.0219f), distance = 0.2766f, world2phy = 218f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                    Model("dahlia", rq, new Vector3(0, 0, 0), 2f, setcam: new SetCamera() { azimuth = -1.600f, altitude = 0.079f, lookAt = new Vector3(0.0466f, 0.8734f, 3.4591f), distance = 1.0342f, world2phy = 399f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.15f }, rotate: false,
                        la: [new Vector3(0.0655f, 0.7306f, 0.3776f),]);
                    Model("pole_dance", rq, new Vector3(0, 0, 0), 1f, setcam: new SetCamera() { azimuth = -1.658f, altitude = -0.571f, lookAt = new Vector3(0.1558f, 1.0428f, 2.0683f), distance = 1.3446f, world2phy = 411f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: true);

                    pb.SeparatorText("Porn");

                    Model("femme-fatale-illustrated-by-bruce-timm", rq, new Vector3(0, 0, 0), 2f,
                        setcam: new SetCamera() { azimuth = -1.620f, altitude = -0.068f, lookAt = new Vector3(0.3479f, 2.4976f, 1.1376f), distance = 3.1441f, world2phy = 123f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                    Model("dd-t12", rq, new Vector3(0, 0, 0), 0.001f,
                        setcam: new SetCamera() { azimuth = -1.253f, altitude = 0.435f, lookAt = new Vector3(-0.6351f, 3.7647f, 13.4011f), distance = 2.9439f, world2phy = 104f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                        la: [new Vector3(1.5f, 0.0f, 8.4011f), new Vector3(2f, 1f, 1.7f)]);
                    Model("sexy_girl_03", rq, new Vector3(0, 0, -0.5f), 0.0013f,
                        setcam: new SetCamera() { azimuth = -1.035f, altitude = 0.759f, lookAt = new Vector3(0.0252f, 0.4079f, 0.4730f), distance = 0.7825f, world2phy = 706f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                        la: [new Vector3(-0.56f, 0.5f, 0.5f)]);
                    Model("girl-scan-studio-1", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.489f, altitude = 1.189f, lookAt = new Vector3(-6.9545f, 5.1882f, 0.0000f), distance = 2.0878f, world2phy = 100f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                        la: [new Vector3(-6.72f, -4.11f, 0)]);
                    Model("girl-scan-studio-2", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = 0.904f, altitude = -0.086f, lookAt = new Vector3(-0.5279f, 0.1145f, 3.5366f), distance = 1.4571f, world2phy = 225f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                    Model("nude_dome_in_earth_orbit_baked", rq, new Vector3(0, 0, -0.5f), 1f,
                        setcam: new SetCamera() { azimuth = -1.710f, altitude = -0.042f, lookAt = new Vector3(-0.0211f, 6.5811f, 1.5335f), distance = 4.6493f, world2phy = 592f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);

                    pb.CollapsingHeaderEnd();
                }

                {
                    pb.CollapsingHeaderStart("Other models");

                    Model("sci-fi_space_station", rq, new Vector3(0, 0, 0), 0.001f);

                    pb.SeparatorText("Big scene");
                    Model("medieval_modular_city_realistic_-_wip", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("city-_shanghai-sandboxie", Quaternion.Identity, new Vector3(-10, 10, 0), 0.0001f,
                        setcam: new SetCamera() { azimuth = -1.375f, altitude = 0.798f, lookAt = new Vector3(-9.9780f, 10.0134f, 0.0000f), distance = 4.0703f, world2phy = 44f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false,
                        la: [new Vector3(-1.5582f, 3.7668f, 0.0000f)]);
                    Model("city", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("low_poly_city_pack", rq, new Vector3(0, 0, -1), 0.1f,
                        setcam: new SetCamera() { azimuth = 0.732f, altitude = 1.068f, lookAt = new Vector3(0.7813f, -0.6471f, 0.0000f), distance = 1.7459f, world2phy = 104f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false,
                        la: [new Vector3(-19.58f, -1, 0),new Vector3(-19.25f,8.7f,0)]);
                    //Model("nyc", rq, new Vector3(0, 0, 0), 0.1f); //oversized atlas.

                    Model("moonchild", rq, new Vector3(0, 0, 0), 0.03f,
                        setcam: new SetCamera() { azimuth = -1.524f, altitude = 0.788f, lookAt = new Vector3(-0.0995f, -0.7824f, 0.0000f), distance = 4.3857f, world2phy = 69f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f },
                        rotate: true);

                    Model("ship_in_clouds", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f, setcam: new SetCamera()
                    {
                        altitude = 0.3f,
                        azimuth = -1.8f,
                        lookAt = new Vector3(-0.17f, 5.22f, 0),
                        distance = 3.1f
                    });

                    pb.SeparatorText("Energytic Scene");
                    Model("bomb_crack_ground_00_free", rq, new Vector3(0, 0, 0), 0.01f);
                    
                    Model("bullet_physics_animation_9_reconstruction", rq, new Vector3(0, 0, 0), 1f);
                    Model("rinoa_-_final_fantasy_viii_-_attack", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("reap_the_whirlwind", rq, new Vector3(0, 0, 0), 0.01f,
                        setcam: new SetCamera() { azimuth = -1.449f, altitude = -0.037f, lookAt = new Vector3(-1.8337f, 13.1011f, 5.1155f), distance = 19.6199f, world2phy = 64f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 0.15f }, rotate: false);

                    pb.SeparatorText("Fight Scene");
                    Model("dummy_fight", rq, new Vector3(0, 0, 0), 1f);
                    Model("cyber_ronin_character", rq, new Vector3(0, 0, 0), 1f, color_scale: 2);


                    pb.SeparatorText("Large Movement Scene");
                    Model("court", rq, new Vector3(0, 0, 0), 1f);
                    Model("endless_floor_vr", rq, new Vector3(0, 0, 0), 1f);
                    Model("loops", rq, new Vector3(0, 0, 0), 1f);
                    Model("looping_sci_fi_tunnel_test", rq, new Vector3(0, 0, 0), 1f);
                    Model("insanity_in_motion", rq, new Vector3(0, 0, 0), 1f);
                    Model("spacedrive", rq, new Vector3(0, 0, 0), 1f);
                    Model("spacetravel", rq, new Vector3(0, 0, 0), 1f);

                    pb.SeparatorText("Game Scene");

                    Model("1st_person_pov_looping_tunnel_ride", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.607f, altitude = -0.060f, lookAt = new Vector3(0.0000f, 0.0000f, 0.0000f), distance = 5.0000f, world2phy = 136f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f });
                    Model("5v5_game_map", rq, new Vector3(0, 0, 0), 0.001f);
                    Model("sushi_restaurant_kit_100_models", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.574f, altitude = 0.833f, lookAt = new Vector3(0.2429f, 1.6750f, -2.3863f), distance = 3.1820f, world2phy = 34f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false);
                    Model("free_bee_club", rq, new Vector3(0, 0, 0), 1f);
                    Model("rollercoaster_diorama_cartoon_style", rq, new Vector3(0, 0, 0), 1f);
                    Model("p.u.c._security_bot_7", rq, new Vector3(0, 0, 0), 1f);
                    Model("windmill__handpainted_3d_environment", rq, new Vector3(0, 0, 0), 1f);
                    Model("frogger_3d_scene", rq, new Vector3(0, 0, 0), 1f);
                    Model("fps_butterfly_knife", rq, new Vector3(0, 0, 0), 1f, 
                        setcam: new SetCamera() { azimuth = -1.611f, altitude = -0.065f, lookAt = new Vector3(-0.0260f, 0.6569f, -0.0424f), distance = 0.3918f, world2phy = 868f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f });
                    Model("stylized_cannon_free_asset", rq*rq, new Vector3(0, 0, 0), 0.03f);
                    //Model("shutter_girl", rq, new Vector3(0, 0, 0), 0.001f); animation bad
                    Model("hey_good_lookin_-_vinnie", rq, new Vector3(0, 0, 0), 1f);
                    Model("tap", rq, new Vector3(0, 0, 0), 1f, color_scale: 1.4f);
                    Model("the_last_stronghold_animated", rq * rq, new Vector3(0, 0, 0), 0.001f);
                    Model("tropical_island", rq, new Vector3(0, 0, 0), 1f);

                    Model("route_66_adventure_-_sketchfab_challenge", rq, new Vector3(0, 0, 0), 1f);
                    Model("baba_yagas_hut", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = 2.043f, altitude = 0.607f, lookAt = new Vector3(-1.9344f, -3.6813f, 0.5163f), distance = 15.2065f, world2phy = 24f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f }, rotate: true);
                    Model("baker_and_the_bridge", rq, new Vector3(0, 0, 0), 1f);
                    Model("fighting_girl", rq, new Vector3(0, 0, 0), 1f);

                    pb.SeparatorText("Crowds");
                    Model("jack-o-lanterns", rq, new Vector3(0, 0, 0), 1f);
                    Model("ultimate_monsters_pack", rq, new Vector3(0, 0, 0), 0.5f, color_scale:3.0f);

                    pb.SeparatorText("Concept Art");
                    Model("still_life_based_on_heathers_artwork", rq, new Vector3(0, 0, 0), 1f, color_scale:0.5f);

                    pb.SeparatorText("Horror Scene");
                    Model("poppy_playtime_chapter_2_-_angry_mommy", rq, new Vector3(0, 0, 0), 0.03f);
                    Model("the_classic_monkey_bomb_-_cymbal_monkey", rq, new Vector3(0, 0, 0), 3f, brightness:1.5f);

                    pb.SeparatorText("Interior Scene");
                    Model("teascroll_clubhouse_scene", rq, new Vector3(0, 0, 0), 1f,
                        setcam: new SetCamera() { azimuth = -1.529f, altitude = -0.010f, lookAt = new Vector3(0.0011f, 3.1151f, 1.4723f), distance = 3.6613f, world2phy = 325f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.15f }, rotate: true);
                    Model("cozy", rq, new Vector3(0, 0, 0), 1f);
                    Model("coloured_apartment_-_unita_3", rq, new Vector3(0, 0, 0), 1f);
                    Model("cozy_living_room_baked", rq, new Vector3(0, 0, 0), 1f);
                    Model("futuristic_room", rq, new Vector3(0, 0, 0), 1f);
                    Model("mirrors_edge_apartment_-_interior_scene", rq, new Vector3(0, 0, 0), 1f,
                        setcam:new SetCamera()
                        {
                            altitude = 0.1f, azimuth = -0.7f, lookAt = new Vector3(-2.64f, 0.97f, -0.53f), distance = 4.19f, mmb_freelook = true
                        });

                    pb.SeparatorText("Things");
                    Model("2021_porsche_911_targa_4s_heritage_design_992", rq, new Vector3(0, 0, 0), 300f,
                        setcam: new SetCamera() { azimuth = -0.793f, altitude = 0.100f, lookAt = new Vector3(-6.4320f, 6.4370f, 1.4504f), distance = 12.0046f, world2phy = 28f },
                        app: new SetAppearance() { useGround = true, drawGrid = true, drawGuizmo = false, sun_altitude = 1.57f }, rotate: true);
                    Model("kawashaki_ninja_h2", rq, new Vector3(0, 0, 0), 1f);
                    Model("rx-0_full_armor_unicorn_gundam", rq, new Vector3(0, 0, 0), 1f);
                    Model("bilibili", rq, new Vector3(0, 0, 0), 1f);
                    Model("f4c5d36d100644b2a95ceec2e9b2b5dc", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("halloween", rq, new Vector3(0, 0, 0), 1f);
                    Model("spy-hypersport", rq, new Vector3(0, 0, 0), 1f);
                    Model("butterflies_-_collection_of_stanisaw_batkowski", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("war_plane", rq, new Vector3(0, 0, 0), 0.01f);

                    pb.SeparatorText("Medical");
                    Model("anatomy_of_the_airways", rq, new Vector3(0, 0, 0), 0.01f);


                    //too simple Model("kidney", rq, new Vector3(0, 0, 0), 0.1f, Vector3.One*0.5f);
                    Model("blood_vascular_system_of_domestic_duck_head", rq, new Vector3(0, 0, 0), 1f);
                    Model("craniofacial_anatomy_atlas", rq, new Vector3(0, 0, -2), 0.1f);
                    //Model("female_anatomy_by_chera_ones", rq, new Vector3(0, 0, 0), 0.2f);  //??
                    Model("blue_whale_skeleton", rq, new Vector3(0, 0, -3), 1f);
                    Model("heart0476_male_heart_annuloplasty_rings", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("thorax_and_abdomen_some_of_the_lymph_nodes", rq, new Vector3(0, 0, 0), 0.01f);

                    // regeneration:
                    pb.SeparatorText("Scanning");
                    Model("neogenesis__the_becoming_of_her", rq, new Vector3(0, 0, -2), 2f);
                    Model("jezek_-_hedgehog_public_art", rq, new Vector3(0, 0, -14.5f), 1f);
                    Model("the_great_drawing_room", rq, new Vector3(0, 0, -14.5f), 1f);
                    Model("mythical_beast_censer_c._1736-1795_ce", rq, new Vector3(0, 0, 0), 1f);

                    // simulations:
                    pb.SeparatorText("Generation & Simulation");
                    Model("lorenz_mod_2", rq, new Vector3(0, 0, -1.5f), 1f);
                    Model("julia_revolute_variation_2", rq, new Vector3(0, 0, -1.5f), 1f);
                    Model("flow_motion", rq, new Vector3(0, 0, -1.5f), 3f);
                    Model("fractal_gravity", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f);
                    Model("sci-fi_solar_power_tower_free-download", rq, new Vector3(0, 0, 0), 0.1f);

                    pb.SeparatorText("Dance");
                    Model("beautiful_asian_girl", Quaternion.CreateFromAxisAngle(Vector3.UnitY, -(float)Math.PI / 2), new Vector3(0, 0, 0), 1f);
                    Model("salsa_dance_basic_steps_-_lowpoly_style", rq, new Vector3(0, 0, 0), 0.03f);

                    pb.SeparatorText("Figure");
                    Model("beautiful_girl_sitting_on_a_chair", rq, new Vector3(0, 0, 0), 0.025f);
                    Model("body_character_love_seat_for_arch", rq, new Vector3(0, 0, 0), 0.025f);
                    Model("body_character_model", rq, new Vector3(0, 0, 0), .025f);
                    Model("cristy", rq, new Vector3(0, 0, 0), 1f, pty:new(){baseAnimId = 1});
                    Model("gothic_girl", rq, new Vector3(0, 0, 0), 1f);
                    Model("hayley", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("reiyu_guigui", rq, new Vector3(0, 0, 0), 1f);
                    Model("sci-fi_girl_v.02_walkcycle_test", rq, new Vector3(0, 0, 0), 1f);
                    Model("valerie_sitting-relax", rq*rq, new Vector3(0, 0, 0), 0.001f);
                    Model("witchapprentice", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("misaki", rq , new Vector3(0, 0, 0), 1);
                    Model("mitsu_oc_by_suizilla", rq, new Vector3(0, 0, 0), 1, setcam: new SetCamera() { azimuth = -2.035f, altitude = 0.005f, lookAt = new Vector3(1.6561f, 2.7462f, 0.7985f), distance = 2.8990f, world2phy = 119f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f }, rotate: true);
                    Model("shibahu", rq, new Vector3(0, 0, 0), 1, color_scale: 0.9f);
                    Model("sorceress", Quaternion.Identity , new Vector3(0, 0, 0), 0.001f, color_scale:0.7f);
                    Model("the_noble_craftsman", rq, new Vector3(0, 0, 0), 0.01f, 
                        setcam: new SetCamera() { azimuth = -1.103f, altitude = -0.309f, lookAt = new Vector3(-0.7867f, 1.2083f, 1.3067f), distance = 2.0763f, world2phy = 412f },
                        app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false);

                    pb.SeparatorText("NSFW");
                    Model("nude-female-seated-on-a-peacock-chair", rq, new Vector3(0, 0, 0), 2f);
                    Model("zelina_naked_riged_tpose", rq, new Vector3(0, 0, 0), 2f);
                    // Model("NSFW_1", Quaternion.Identity, new Vector3(-1, -2, -3.5f), 2f);
                    Model("girl-body-scan-studio-13", rq, new Vector3(0, 0, 0), 1f);


                    Model("cyber_sekes", rq, new Vector3(0, 0, 0), 1f);
                    Model("free_realistic_female_korean_naked", rq, new Vector3(0, 0, 0), 1f);
                    Model("Jessica_dance", rq, new Vector3(0, 0, 0), 1f);



                    pb.CollapsingHeaderEnd();
                }

                {
                    pb.CollapsingHeaderStart("Teleoperating");
                    if (pb.Button("Show Remote View"))
                    {
                        new SetCamera()
                        {
                            azimuth = -3.124f,
                            altitude = 1.456f,
                            lookAt = new Vector3(-0.3160f, 0.2817f, -0.3492f),
                            distance = 0.3521f, world2phy = 672f
                        }.Issue();
                        new SetAppearance()
                        {
                            useSSAO = useSSAO,
                            useGround = false,
                            drawGrid = false,
                        }.Issue();

                        if (calib_panel == null)
                        {
                            calib_panel = GUI.DeclarePanel();
                            calib_panel.Define(pb =>
                            {
                                show_remote = true;

                                if (calibs.Length == 0)
                                {
                                    pb.Panel.Repaint();
                                    pb.Label("Wait for camera frames...");
                                    return;
                                }

                                for (int i = 0; i < calibs.Length; i++)
                                {
                                    var X_rot = 0f;
                                    var Y_rot = 0f;
                                    var Z_rot = 0f;
                                    pb.SeparatorText($"Camera {i}");
                                    pb.DragFloat($"cam depth scale {i}", ref calibs[i].fd, 0.001f);
                                    pb.DragFloat($"cam fx {i}", ref calibs[i].fx, 0.1f);
                                    pb.DragFloat($"cam fy {i}", ref calibs[i].fy, 0.1f);
                                    pb.DragFloat($"cam bfx {i}", ref calibs[i].bfx, 0.1f);
                                    if (pb.DragFloat($"cam rotate X {i}", ref X_rot, 0.1f))
                                    {
                                        calibs[i].rotation *=
                                            Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(X_rot / 180 * Math.PI));
                                        Console.WriteLine($"modify x for {X_rot}");
                                    }

                                    if (pb.DragFloat($"cam rotate Y {i}", ref Y_rot, 0.1f))
                                        calibs[i].rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Y_rot / 180 * Math.PI));

                                    if (pb.DragFloat($"cam rotate Z {i}", ref Z_rot, 0.1f))
                                        calibs[i].rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(Z_rot / 180 * Math.PI));

                                    pb.DragFloat($"cam translate X {i}", ref calibs[i].translation.X, 1f);
                                    pb.DragFloat($"cam translate Y {i}", ref calibs[i].translation.Y, 1f);
                                    pb.DragFloat($"cam translate Z {i}", ref calibs[i].translation.Z, 1f);
                                }

                                PaintRemote();

                                if (pb.Button("SaveParam"))
                                    File.WriteAllText("cam_calibs.json", JsonConvert.SerializeObject(calibs));

                                pb.Panel.Repaint();

                                if (pb.Closing())
                                {
                                    show_remote = false;
                                    calib_panel = null;
                                    pb.Panel.Exit();
                                }
                            });
                        }
                    }
                    pb.CollapsingHeaderEnd();
                }

                Playback(pb);
            });
            
        }
    }
}