using System.Numerics;
using System.Reflection;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;
using GitHub.secile.Video;
using Path = System.IO.Path;

namespace HoloExample
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

            Vector3 campos = Vector3.Zero, lookat = Vector3.Zero;

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

                    pb.Label($"lookat={lookat}, d={(campos-lookat).Length()}");

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

                    void Model(string name, Quaternion q, Vector3 v3, float scale,
                        Vector3 color_bias = default, float color_scale=1,float brightness=1, 
                        SetCamera setcam=null, SetModelObjectProperty pty=null, bool force_dblface=false, float normal_shading=0)
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
                                    NormalShading=normal_shading
                                },
                                name = "model_glb"
                            });
                            //

                            Workspace.Prop(new PutModelObject()
                                { clsName = "model_glb", name = "glb1" });
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
                        }
                    }

                    pb.SeparatorText("Scene");
                    Model("LittlestTokyo", rq, new Vector3(0, 0, -2), 0.01f, setcam:new SetCamera()
                    {
                        azimuth = 2.8f, altitude = 0.1f, lookAt = new Vector3(1.36f, -1.19f, 1.66f), distance = 3.74f,
                        mmb_freelook = false, world2phy = 100,
                    });
                    Model("low_poly_city_pack", rq, new Vector3(0, 0, -1), 0.1f);

                    pb.SeparatorText("Famous Art");
                    Model("isleworth-mona-lisa-3d", rq, new Vector3(0, 0, 0), 1f);
                    Model("guernica-3d", rq, new Vector3(0, 0, 0), 1f);
                    Model("reclining-nude-3d", rq, new Vector3(0, 0, 0), 1f);
                    Model("persistence-of-memory-3d", rq, new Vector3(0, 0, 0), 1f, color_bias: new Vector3(0.05f),
                        color_scale: 1.2f);

                    pb.SeparatorText("Big scene");
                    Model("medieval_modular_city_realistic_-_wip", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("city-_shanghai-sandboxie", Quaternion.Identity, new Vector3(-10, 10, 0), 0.0001f);
                    Model("city", rq, new Vector3(0, 0, 0), 0.1f);
                    //Model("nyc", rq, new Vector3(0, 0, 0), 0.1f); //oversized atlas.

                    pb.SeparatorText("Energytic Scene");
                    Model("bomb_crack_ground_00_free", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("truck_hit_brickwall_00_free", rq, new Vector3(0, 0, 0), 1f);
                    Model("bullet_physics_animation_9_reconstruction", rq, new Vector3(0, 0, 0), 1f);
                    Model("sphere_explosion", rq, new Vector3(0, 0, 0), 0.03f);
                    Model("wood_crack_00_free", rq, new Vector3(0, 0, 0), 1f);
                    Model("rinoa_-_final_fantasy_viii_-_attack", rq, new Vector3(0, 0, 0), 0.01f);

                    pb.SeparatorText("Fight Scene");
                    Model("character_fight", rq, new Vector3(0, 0, 0), 1f);
                    Model("dummy_fight", rq, new Vector3(0, 0, 0), 1f);
                    Model("cyber_ronin_character", rq, new Vector3(0, 0, 0), 1f, color_scale: 2);


                    pb.SeparatorText("Large Movement Scene");
                    Model("bunny_swimsuit_black_pubg", rq, new Vector3(0, 0, 0), 1f, pty: new() { baseAnimId = 1 },
                        force_dblface: true);
                    Model("futuristic_hallway_with_patrolling_robot", rq, new Vector3(0, 0, 0), 1f);

                    pb.SeparatorText("Game Scene");
                    Model("pac-man_remaster", rq, new Vector3(0, 0, 0), 1f);
                    Model("p.u.c._security_bot_7", rq, new Vector3(0, 0, 0), 1f);
                    Model("windmill__handpainted_3d_environment", rq, new Vector3(0, 0, 0), 1f);
                    Model("frogger_3d_scene", rq, new Vector3(0, 0, 0), 1f);
                    Model("akm_fps_animation", rq, new Vector3(0, 0, 0), 1f);
                    Model("fps_butterfly_knife", rq, new Vector3(0, 0, 0), 1f);
                    Model("stylized_cannon_free_asset", rq*rq, new Vector3(0, 0, 0), 0.03f);
                    Model("cuphead_-_hilda_berg_boss_fight", rq, new Vector3(0, 0, 0), 1f);
                    //Model("shutter_girl", rq, new Vector3(0, 0, 0), 0.001f); animation bad
                    Model("ftm", rq, new Vector3(0, 0, 0), 1f);
                    Model("game_pirate_adventure_map", rq, new Vector3(0, 0, 0), 0.001f);
                    Model("hey_good_lookin_-_vinnie", rq, new Vector3(0, 0, 0), 1f);
                    Model("ship_in_clouds", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f, setcam:new SetCamera()
                    {
                        altitude = 0.3f, azimuth=-1.8f, lookAt=new Vector3(-0.17f,5.22f,0), distance=3.1f
                    });
                    Model("tap", rq, new Vector3(0, 0, 0), 1f, color_scale: 1.4f);
                    Model("the_last_stronghold_animated", rq * rq, new Vector3(0, 0, 0), 0.001f);
                    Model("1st_person_pov_looping_tunnel_ride", rq, new Vector3(0, 0, 0), 1f);
                    Model("tropical_island", rq, new Vector3(0, 0, 0), 1f);
                    Model("sea_keep_lonely_watcher", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("route_66_adventure_-_sketchfab_challenge", rq, new Vector3(0, 0, 0), 1f);
                    Model("12_animated_butterflies", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f);
                    Model("baba_yagas_hut", rq, new Vector3(0, 0, 0), 1f);
                    Model("baker_and_the_bridge", rq, new Vector3(0, 0, 0), 1f);
                    Model("deja_vu_full_scene", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("fighting_girl", rq, new Vector3(0, 0, 0), 1f);

                    pb.SeparatorText("Crowds");
                    Model("jack-o-lanterns", rq, new Vector3(0, 0, 0), 1f);
                    Model("ultimate_monsters_pack", rq, new Vector3(0, 0, 0), 0.5f, color_scale:3.0f);

                    pb.SeparatorText("Concept Art");
                    Model("dreamsong", rq, new Vector3(0, 0, 0), 0.01f, brightness:2.0f);
                    Model("moonchild", rq, new Vector3(0, 0, 0), 0.03f);
                    Model("still_life_based_on_heathers_artwork", rq, new Vector3(0, 0, 0), 1f, color_scale:0.5f);

                    pb.SeparatorText("Horror Scene");
                    Model("demogorgon_rig", rq, new Vector3(0, 0, 0), 1f);
                    Model("hallucination_huggy_-_poppy_playtime_chapter_3", rq, new Vector3(0, 0, 0), 7f);
                    Model("poppy_playtime_chapter_2_-_angry_mommy", rq, new Vector3(0, 0, 0), 0.03f);
                    Model("the_classic_monkey_bomb_-_cymbal_monkey", rq, new Vector3(0, 0, 0), 3f, brightness:1.5f);

                    pb.SeparatorText("Interior Scene");
                    Model("futuristic_room", rq, new Vector3(0, 0, 0), 1f);
                    Model("mirrors_edge_apartment_-_interior_scene", rq, new Vector3(0, 0, 0), 1f,
                        setcam:new SetCamera()
                        {
                            altitude = 0.1f, azimuth = -0.7f, lookAt = new Vector3(-2.64f, 0.97f, -0.53f), distance = 4.19f, mmb_freelook = true
                        });

                    pb.SeparatorText("Things");
                    Model("2021_porsche_911_targa_4s_heritage_design_992", rq, new Vector3(0, 0, 0), 300f);
                    Model("kawashaki_ninja_h2", rq, new Vector3(0, 0, 0), 1f);
                    Model("rx-0_full_armor_unicorn_gundam", rq, new Vector3(0, 0, 0), 1f);
                    Model("caterpillar_work_boot", rq, new Vector3(0, 0, 0), 3f, color_scale:2.3f);
                    Model("bilibili", rq, new Vector3(0, 0, 0), 1f);
                    Model("f4c5d36d100644b2a95ceec2e9b2b5dc", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("halloween", rq, new Vector3(0, 0, 0), 1f);
                    Model("ship_in_a_bottle", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f, brightness: 1.5f);
                    Model("spy-hypersport", rq, new Vector3(0, 0, 0), 1f);
                    Model("sukhoi_su-35_fighter_jet", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("butterflies_-_collection_of_stanisaw_batkowski", rq, new Vector3(0, 0, 0), 0.01f);

                    pb.SeparatorText("Medical");
                    Model("anatomy_of_the_airways", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("injected-human-foetus-14-weeks-old-microct", rq, new Vector3(0, 0, 0), 0.1f, color_scale:0.6f, normal_shading:0.4f);
                    //too simple Model("kidney", rq, new Vector3(0, 0, 0), 0.1f, Vector3.One*0.5f);
                    Model("craniofacial_anatomy_atlas", rq, new Vector3(0, 0, -2), 0.1f, Vector3.One * 0.3f);
                    Model("lymphatic_system_an_overview", Quaternion.Identity, new Vector3(0, 0, 1.5f), 0.002f);
                    Model("arteres_du_tronc", rq, new Vector3(0, 0, -3f), 0.01f, color_bias: new Vector3(-0.1f));
                    //Model("female_anatomy_by_chera_ones", rq, new Vector3(0, 0, 0), 0.2f);  //??
                    Model("blue_whale_skeleton", rq, new Vector3(0, 0, -3), 1f);
                    Model("heart0476_male_heart_annuloplasty_rings", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("thorax_and_abdomen_some_of_the_lymph_nodes", rq, new Vector3(0, 0, 0), 0.01f);
                    Model("visible_interactive_human_-_exploding_skull", rq, new Vector3(0, 0, 0), 0.1f);

                    // regeneration:
                    pb.SeparatorText("Scanning");
                    Model("neogenesis__the_becoming_of_her", rq, new Vector3(0, 0, -2), 2f);
                    Model("rossbandiger", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("jezek_-_hedgehog_public_art", rq, new Vector3(0, 0, -14.5f), 1f);
                    Model("the_great_drawing_room", rq, new Vector3(0, 0, -14.5f), 1f);
                    Model("girl-body-scan-studio-5", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("mythical_beast_censer_c._1736-1795_ce", rq, new Vector3(0, 0, 0), 1f);
                    Model("skeleton_excavation_dataset", rq, new Vector3(0, 0, -3), 1f);

                    // simulations:
                    pb.SeparatorText("Generation & Simulation");
                    Model("lorenz_mod_2", rq, new Vector3(0, 0, -1.5f), 1f);
                    Model("julia_revolute_variation_2", rq, new Vector3(0, 0, -1.5f), 1f);
                    Model("flow_motion", rq, new Vector3(0, 0, -1.5f), 3f);
                    Model("airshaper_demo_beta_-_3d_annotations", rq, new Vector3(3, -3.5f, -1.0f), 1f, force_dblface:true);
                    Model("fractal_gravity", Quaternion.Identity, new Vector3(0, 0, 0), 0.01f);
                    Model("black_honey_-_robotic_arm", rq, new Vector3(0, 0, 0), 1f, color_scale:2f);
                    Model("opposed_piston_engine_mechanism", rq, new Vector3(0, 0, 0), 1f);
                    Model("sci-fi_solar_power_tower_free-download", rq, new Vector3(0, 0, 0), 0.1f);

                    pb.SeparatorText("Dance");
                    Model("beautiful_asian_girl", Quaternion.CreateFromAxisAngle(Vector3.UnitY, -(float)Math.PI / 2), new Vector3(0, 0, 0), 1f);
                    Model("momoi_sea-salt_summer__farlight_84_characters", rq, new Vector3(0, 0, 0), 0.5f,
                        color_scale: 1.3f);
                    Model("sayuri_dans", rq, new Vector3(0, 0, 0), 1f);
                    Model("salsa_dance_basic_steps_-_lowpoly_style", rq, new Vector3(0, 0, 0), 0.03f);

                    pb.SeparatorText("Figure");
                    Model("elaina_-_the_witchs_journeysummerwhitedress", rq, new Vector3(0, 0, 0), 1f);
                    Model("pika_girl", rq, new Vector3(0, 0, -2f), 0.3f, force_dblface: true);
                    Model("beautiful_girl_sitting_on_a_chair", rq, new Vector3(0, 0, 0), 0.025f);
                    Model("body_character_love_seat_for_arch", rq, new Vector3(0, 0, 0), 0.025f);
                    Model("body_character_model", rq, new Vector3(0, 0, 0), .025f);
                    Model("cristy", rq, new Vector3(0, 0, 0), 1f, pty:new(){baseAnimId = 1});
                    Model("gothic_girl", rq, new Vector3(0, 0, 0), 1f);
                    Model("hayley", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("reiyu_guigui", rq, new Vector3(0, 0, 0), 1f);
                    Model("sci-fi_girl_v.02_walkcycle_test", rq, new Vector3(0, 0, 0), 1f);
                    Model("valerie_sitting-relax", rq*rq, new Vector3(0, 0, 0), 0.001f);
                    Model("howcow", rq , new Vector3(0, 0, 0), 1f);
                    Model("witchapprentice", rq, new Vector3(0, 0, 0), 0.1f);
                    Model("just_a_girl", rq , new Vector3(0, 0, 0), 0.005f);
                    Model("misaki", rq , new Vector3(0, 0, 0), 1);
                    Model("mitsu_oc_by_suizilla", rq, new Vector3(0, 0, 0), 1);
                    Model("shibahu", rq, new Vector3(0, 0, 0), 1, color_scale: 0.9f);
                    Model("sorceress", Quaternion.Identity , new Vector3(0, 0, 0), 0.001f);
                    Model("the_noble_craftsman", rq, new Vector3(0, 0, 0), 0.01f);

                    pb.SeparatorText("Lewd");
                    Model("tifa_piss", rq * rq, new Vector3(0, 0, 0), 0.001f);
                    Model("uzuki_topless_panty", rq, new Vector3(0, 0, 0), 2f);

                    pb.SeparatorText("NSFW");
                    Model("nude-female-seated-on-a-peacock-chair", rq, new Vector3(0, 0, 0), 2f);
                    Model("femme-fatale-illustrated-by-bruce-timm", rq, new Vector3(0, 0, 0), 2f);
                    Model("dd-t12", rq, new Vector3(0, 0, 0), 0.001f);
                    Model("dahlia", rq, new Vector3(0, 0, 0), 2f);
                    Model("zelina_naked_riged_tpose", rq, new Vector3(0, 0, 0), 2f);
                    // Model("NSFW_1", Quaternion.Identity, new Vector3(-1, -2, -3.5f), 2f);
                    Model("sexy_girl_03", rq, new Vector3(0, 0, -0.5f), 0.0013f);
                    Model("girl-body-scan-studio-13", rq, new Vector3(0, 0, 0), 1f);
                    Model("girl-scan-studio-1", rq, new Vector3(0, 0, 0), 1f);
                    Model("girl-scan-studio-2", rq, new Vector3(0, 0, 0), 1f);
                    Model("nude_dome_in_earth_orbit_baked", rq, new Vector3(0, 0, -0.5f), 1f);
                    Model("cyber_sekes", rq, new Vector3(0, 0, 0), 1f);
                    Model("free_realistic_female_korean_naked", rq, new Vector3(0, 0, 0), 1f);
                    Model("Jessica_dance", rq, new Vector3(0, 0, 0), 1f);



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