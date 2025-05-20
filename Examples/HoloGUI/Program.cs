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
            LocalTerminal.SetTitle("HOLO_DEMO");
            LocalTerminal.Start();



            bool test = true;



            var sun = 0f;
            var useCrossSection = false;
            var useEDL = true;
            var useSSAO = true;
            var useGround = true;
            var useBorder = true;
            var useBloom = true;
            var drawGrid = true;
            var drawGuizmo = true;

            GUI.PromptPanel(pb =>
            {
                if (pb.Button("Go Hologram"))
                {
                    new SetFullScreen().IssueToDefault();
                    new SetCamera() { displayMode = SetCamera.DisplayMode.EyeTrackedHolography }.IssueToDefault();
                }

                {
                    pb.CollapsingHeaderStart("Appearance Settings");
                    var appearanceChanged = false;
                    appearanceChanged |= pb.CheckBox("Use EyeDomeLighting", ref useEDL);
                    appearanceChanged |= pb.CheckBox("Use SSAO", ref useSSAO);
                    appearanceChanged |= pb.CheckBox("Use Ground", ref useGround);
                    appearanceChanged |= pb.CheckBox("Use Border", ref useBorder);
                    appearanceChanged |= pb.CheckBox("Use Bloom", ref useBloom);
                    appearanceChanged |= pb.CheckBox("Draw Grid", ref drawGrid);
                    appearanceChanged |= pb.CheckBox("Draw Guizmo", ref drawGuizmo);
                    appearanceChanged |= pb.DragFloat("sun", ref sun, 0.01f, 0f, 1.57f);

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
                    }
                    pb.CollapsingHeaderEnd();
                }

                {
                    pb.CollapsingHeaderStart("Model Displaying");

                    var path = "D:\\res\\glb";
                    var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                    (string name, Quaternion q, Vector3 v3, float scale)[]
                        models = [
                            ("LittlestTokyo", rq, new Vector3(0, 0, -2), 0.01f),
                            ("low_poly_city_pack", rq, new Vector3(0, 0, -1), 0.1f),
                            
                            ("medieval_modular_city_realistic_-_wip", rq, new Vector3(0, 0, 0), 0.1f), // transparent quad not good.
                            ("city-_shanghai-sandboxie", Quaternion.Identity, new Vector3(-10, 10, 0), 0.0001f), 
                            ("city", rq, new Vector3(0, 0, -2), 0.1f), 
                            
                            ("kawashaki_ninja_h2", rq, new Vector3(0, 0, 0), 1f), 
                            ("rx-0_full_armor_unicorn_gundam", rq, new Vector3(0, 0, 0), 1f), 
                            
                            ("kidney", Quaternion.Identity, new Vector3(0, 0, 0), 0.1f),
                            ("craniofacial_anatomy_atlas", Quaternion.Identity, new Vector3(0, 0, -2), 0.1f),
                            ("lymphatic_system_an_overview", Quaternion.Identity, new Vector3(0, 0, 1.5f), 0.002f),
                            ("arteres_du_tronc", rq, new Vector3(0, 0, -3f), 0.01f),
                            ("female_anatomy_by_chera_ones", Quaternion.Identity, new Vector3(0, 0, -2), 0.1f),  //??
                            ("blue_whale_skeleton", rq, new Vector3(0, 0, -3), 1f),

                            // scrupts:
                            ("neogenesis__the_becoming_of_her", rq, new Vector3(0, 0, -2), 2f), 
                            ("rossbandiger", rq, new Vector3(0, 0, 0), 0.1f),
                            ("jezek_-_hedgehog_public_art", rq, new Vector3(0, 0, -14.5f), 1f),

                            // simulations:
                            //("relativity_of_simultaneity_abstract_art", rq, new Vector3(0, 0, 0), 0.1f), // bad displayu
                            //("lorenz_mod_2", rq, new Vector3(0, 0, 0), 0.1f), // bad displayu
                            ("julia_revolute_variation_2", rq, new Vector3(0, 0, -1.5f), 1f),
                            ("flow_motion", Quaternion.Identity, new Vector3(0, 0, -1.5f), 3f),
                            // ("airshaper_demo_beta_-_3d_annotations", Quaternion.Identity, new Vector3(0, 0, -1.5f), 1f), bad show
                            ("fractal_gravity", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f),


                            ("momoi_sea-salt_summer__farlight_84_characters", rq, new Vector3(0, 0, 0), 1f),
                            ("sayuri_dans", rq, new Vector3(0, 0, 0), 1f),
                            //("zelina_naked_riged_tpose", rq*rq, new Vector3(0, 0, 0), 2f),
                            //("NSFW_1", Quaternion.Identity, new Vector3(-1, -2, -3.5f), 2f),
                            ("sexy_girl_03", rq, new Vector3(0, 0, -0.5f), 0.0013f),
                            ("nude_dome_in_earth_orbit_baked", rq, new Vector3(0, 0, -0.5f), 1f),
                            // ("cyber_sekes", rq, new Vector3(0, 0, 0), 1f), wrong
                            // ("Jessica_dance", Quaternion.Identity, new Vector3(0, 0, -3), 1f),
                        ];

                    foreach (var model in models)
                    {
                        if (pb.Button(model.name))
                        {
                            Workspace.Prop(new LoadModel()
                            {
                                detail = new Workspace.ModelDetail(File.ReadAllBytes(Path.Join(path, $"{model.name}.glb")))
                                {
                                    Center = model.v3,
                                    Rotate = model.q,
                                    Scale = model.scale
                                },
                                name = "model_glb"
                            });
                            //

                            Workspace.Prop(new PutModelObject()
                                { clsName = "model_glb", name = "glb1" });
                            new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();
                        }
                    }

                    pb.CollapsingHeaderEnd();
                }

                {
                    pb.CollapsingHeaderStart("Teleoperating");
                    if (pb.Button("Holographic teleoperation demo"))
                    {

                    }
                    pb.CollapsingHeaderEnd();
                }

            });
            
        }
    }
}