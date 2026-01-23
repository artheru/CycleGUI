using System.Numerics;
using CycleGUI;
using CycleGUI.API;

namespace HoloCaliberationDemo;

internal static partial class Program
{
    private static Panel gltfPanel = null;
    private static Panel displayAssetsPanel = null;
    private static string gltfFilename = "/";
    private static bool gltfModelLoaded = false;
    
    // Model parameters
    private static float gltfCenterX = 0f, gltfCenterY = 0f, gltfCenterZ = 0f;
    private static float gltfScale = 0.1f;
    private static float gltfColorScale = 1.0f;
    private static float gltfBrightness = 1.0f;
    private static float gltfNormalShading = 0f;
    private static bool gltfDoubleSided = false;
    private static int gltfRotation = 1;  // 0=None, 1=X, 2=Y, 3=2X
    
    // Camera parameters
    private static float gltfWorld2Phy = 100f;
    
    // Tracking parameters
    private static bool gltfEnableTracking = false;
    private static string gltfTrackingObjectName = "";
    private static string gltfCurrentTrackedObject = "";
    private static string gltfLastSelectedSubObject = "";
    
    // Selection for tracking
    private static SelectObject gltfSelectAction = null;
    private static bool gltfSelectSubObjectMode = false;

    private static readonly Quaternion[] GltfRotations = new[]
    {
        Quaternion.Identity,
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI / 2),
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI),
    };

    public static void GltfViewer(PanelBuilder pb)
    {
        pb.CollapsingHeaderStart("Custom GLTF Viewer");
        
        if (pb.Button("Open GLTF Viewer Panel"))
        {
            if (gltfPanel == null)
            {
                // Set up camera and appearance for model viewing
                gltfWorld2Phy = 100f;
                new SetCamera()
                {
                    azimuth = -(float)(Math.PI / 2),
                    altitude = 0.1f,
                    lookAt = new Vector3(0f, 0f, 0f),
                    distance = 3.0f,
                    world2phy = gltfWorld2Phy
                }.IssueToDefault();
                
                new SetAppearance()
                {
                    useGround = false,
                    drawGrid = false,
                    drawGuizmo = true,
                    sun_altitude = 1.0f
                }.IssueToDefault();

                gltfPanel = GUI.PromptPanel(pbv =>
                {
                    pbv.Panel.ShowTitle("GLTF Viewer");
                    
                    if (pbv.Button("Show Reverspective",distinct:"rv"))
                    {
                        gltfModelLoaded = true;
                        SetCamera setcam = new SetCamera()
                        {
                            azimuth = -1.637f,
                            altitude = -0.073f,
                            lookAt = new Vector3(0.0567f, 0.4273f, 0.8764f),
                            distance = 0.5258f,
                            world2phy = 70f
                        };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("reverspective_painting.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = rq,
                                Scale = 1f,
                                ColorBias = default,
                                ColorScale = 1.0f,
                                Brightness = 1,
                                ForceDblFace = false,
                                NormalShading = 0
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                        { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

                        // set camera.
                        setcam.IssueToAllTerminals();
                        app.IssueToAllTerminals();
                    }
                    
                    if (pbv.Button("Show Warplane"))
                    {
                        gltfModelLoaded = true;
                        SetCamera setcam = new SetCamera()
                        {
                            azimuth = -1.637f,
                            altitude = -0.073f,
                            lookAt = new Vector3(0.0567f, 0.4273f, 0.8764f),
                            distance = 0.5258f,
                            world2phy = 100f
                        };
                        SetAppearance app = new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f };

                        var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes("war_plane.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = rq,
                                Scale = 1f,
                                ColorBias = default,
                                ColorScale = 1.0f,
                                Brightness = 1,
                                ForceDblFace = false,
                                NormalShading = 0
                            },
                            name = "model_glb"
                        });
                        //

                        Workspace.Prop(new PutModelObject()
                        { clsName = "model_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();

                        // set camera.
                        setcam.IssueToAllTerminals();
                        app.IssueToAllTerminals();
                    }

                    // Handle panel close - hide model
                    if (pbv.Closing())
                    {
                        if (gltfModelLoaded)
                        {
                            // Remove the model object
                            WorkspaceProp.RemoveNamePattern("glb1");
                            gltfModelLoaded = false;
                        }
                        // End selection action if active
                        gltfSelectAction?.End();
                        gltfSelectAction = null;
                        gltfSelectSubObjectMode = false;
                        
                        gltfPanel = null;
                        pbv.Panel.Exit();
                        return;
                    }

                    // Model transform controls
                    bool paramsChanged = false;
                    paramsChanged |= pbv.DragFloat("Center X", ref gltfCenterX, 0.01f, -30, 30);
                    paramsChanged |= pbv.DragFloat("Center Y", ref gltfCenterY, 0.01f, -30, 30);
                    paramsChanged |= pbv.DragFloat("Center Z", ref gltfCenterZ, 0.01f, -30, 30);
                    paramsChanged |= pbv.DragFloat("Scale", ref gltfScale, 0.001f, 0.001f, 100);
                    paramsChanged |= pbv.DragFloat("Color Scale", ref gltfColorScale, 0.01f, 0.1f, 10);
                    paramsChanged |= pbv.DragFloat("Brightness", ref gltfBrightness, 0.01f, 0.1f, 10);
                    paramsChanged |= pbv.DragFloat("Normal Shading", ref gltfNormalShading, 0.01f, 0f, 1f);
                    paramsChanged |= pbv.CheckBox("Double Sided", ref gltfDoubleSided);
                    paramsChanged |= pbv.RadioButtons("Rotation", ["None", "X 90°", "Y 90°", "X 180°"], ref gltfRotation, true);

                    // Update model if params changed and model is loaded
                    if (paramsChanged && gltfModelLoaded)
                    {
                        Workspace.AddProp(new ReloadModel()
                        {
                            detail = new Workspace.ModelDetail([])
                            {
                                Center = new Vector3(gltfCenterX, gltfCenterY, gltfCenterZ),
                                Rotate = GltfRotations[gltfRotation],
                                Scale = gltfScale,
                                Brightness = gltfBrightness,
                                ColorScale = gltfColorScale,
                                NormalShading = gltfNormalShading,
                                ForceDblFace = gltfDoubleSided
                            },
                            name = "model_glb"
                        });
                    }

                    pbv.Separator();
                    pbv.Label($"Current file: {gltfFilename}");
                    
                    // Load button
                    if (pbv.Button("Load GLB/GLTF File"))
                    {
                        if (UITools.FileBrowser("Select GLTF/GLB file", out var filename, selectDir: false, t: pbv.Panel.Terminal, actionName: "Open", defaultFileName: "", filter: "glb"))
                        {
                            gltfFilename = filename;
                            LoadGltfModel(filename);
                        }
                    }
                    
                    pbv.SameLine();
                    if (pbv.Button("📦 Browse Preset Models"))
                    {
                        OpenDisplayAssetsPanel(GUI.defaultTerminal);
                    }

                    // Show/Hide button when model is loaded
                    if (gltfModelLoaded)
                    {
                        pbv.Separator();
                        if (pbv.Button("🗑️ Hide Model"))
                        {
                            WorkspaceProp.RemoveNamePattern("glb1");
                            gltfModelLoaded = false;
                            gltfEnableTracking = false;
                            gltfCurrentTrackedObject = "";
                            gltfSelectAction?.End();
                            gltfSelectAction = null;
                            gltfSelectSubObjectMode = false;
                            Workspace.Prop(new SetObjectMoonTo() { name = "me::camera" }); // Cancel tracking
                        }
                        pbv.SameLine();
                        if (pbv.Button("🔄 Reset View"))
                        {
                            // Reset camera to default view
                            gltfWorld2Phy = 100f;
                            new SetCamera()
                            {
                                azimuth = -(float)(Math.PI / 2),
                                altitude = 0.1f,
                                lookAt = new Vector3(0f, 0f, 0f),
                                distance = 3.0f,
                                world2phy = 100f
                            }.IssueToDefault();
                        }
                        
                        // World to Physical ratio (virtual world scale)
                        pbv.SeparatorText("Camera Settings");
                        if (pbv.DragFloat("World2Phy Ratio", ref gltfWorld2Phy, 1f, 1f, 2000f))
                        {
                            new SetCamera() { world2phy = gltfWorld2Phy }.IssueToDefault();
                        }
                        pbv.Label("(虚拟世界与真实世界比率，数值越大物体越小)");
                        
                        // Camera tracking section
                        pbv.SeparatorText("Camera Tracking");
                        
                        if (!string.IsNullOrEmpty(gltfCurrentTrackedObject))
                        {
                            pbv.Label($"📍 Currently tracking: {gltfCurrentTrackedObject}");
                            
                            if (pbv.Button("❌ Cancel Tracking"))
                            {
                                Workspace.Prop(new SetObjectMoonTo() { name = "me::camera" });
                                gltfCurrentTrackedObject = "";
                                gltfEnableTracking = false;
                                Console.WriteLine("Camera tracking cancelled");
                            }
                        }
                        else
                        {
                            // Sub-object selection mode for tracking
                            if (pbv.CheckBox("🎯 Click to Select Sub-Object", ref gltfSelectSubObjectMode))
                            {
                                if (gltfSelectSubObjectMode)
                                {
                                    // Start selection action
                                    if (gltfSelectAction == null)
                                    {
                                        gltfSelectAction = new SelectObject()
                                        {
                                            terminal = pbv.Panel.Terminal,
                                            feedback = (tuples, _) =>
                                            {
                                                if (tuples != null && tuples.Length > 0)
                                                {
                                                    var selected = tuples[0];
                                                    if (!string.IsNullOrEmpty(selected.firstSub))
                                                    {
                                                        gltfLastSelectedSubObject = selected.firstSub;
                                                        gltfTrackingObjectName = selected.firstSub;
                                                        pbv.Panel.Repaint();
                                                        Console.WriteLine($"Selected sub-object: {selected.name}::{selected.firstSub}");
                                                    }
                                                    else
                                                    {
                                                        gltfLastSelectedSubObject = selected.name;
                                                        Console.WriteLine($"Selected object: {selected.name}");
                                                    }
                                                }
                                            }
                                        };
                                        gltfSelectAction.Start();
                                        gltfSelectAction.SetObjectSubSelectable("glb1");
                                    }
                                }
                                else
                                {
                                    // End selection action
                                    gltfSelectAction?.End();
                                    gltfSelectAction = null;
                                }
                            }
                            
                            if (gltfSelectSubObjectMode)
                            {
                                pbv.Label("Click on any part of the model to select it.");
                            }
                            
                            if (!string.IsNullOrEmpty(gltfLastSelectedSubObject))
                            {
                                pbv.Label($"Last selected: {gltfLastSelectedSubObject}");
                            }
                            
                            pbv.Separator();
                            pbv.Label("Or enter sub-object name manually:");
                            var (inputText, _) = pbv.TextInput("Sub-Object Name", gltfTrackingObjectName, "e.g. Object_957", alwaysReturnString: true);
                            gltfTrackingObjectName = inputText;
                            
                            if (pbv.Button("🎥 Track This Object", disabled: string.IsNullOrWhiteSpace(gltfTrackingObjectName)))
                            {
                                var targetObject = $"glb1::{gltfTrackingObjectName}";
                                Workspace.Prop(new SetObjectMoonTo() { earth = targetObject, name = "me::camera" });
                                gltfCurrentTrackedObject = targetObject;
                                gltfEnableTracking = true;
                                Console.WriteLine($"Camera now tracking: {targetObject}");
                                
                                // End selection mode after tracking is set
                                gltfSelectSubObjectMode = false;
                                gltfSelectAction?.End();
                                gltfSelectAction = null;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(gltfFilename) && gltfFilename != "/" && File.Exists(gltfFilename))
                    {
                        if (pbv.Button("Show Model"))
                        {
                            LoadGltfModel(gltfFilename);
                        }
                    }

                }, GUI.localTerminal);
            }
            else
            {
                gltfPanel.BringToFront();
            }
        }
        
        pb.CollapsingHeaderEnd();
    }

    private static void LoadGltfModel(string filename)
    {
        try
        {
            var modelData = File.ReadAllBytes(filename);
            
            // Load model class
            Workspace.Prop(new LoadModel()
            {
                detail = new Workspace.ModelDetail(modelData)
                {
                    Center = new Vector3(gltfCenterX, gltfCenterY, gltfCenterZ),
                    Rotate = GltfRotations[gltfRotation],
                    Scale = gltfScale,
                    ColorScale = gltfColorScale,
                    Brightness = gltfBrightness,
                    NormalShading = gltfNormalShading,
                    ForceDblFace = gltfDoubleSided
                },
                name = "model_glb"
            });
            
            // Place model object in scene
            Workspace.Prop(new PutModelObject()
            {
                clsName = "model_glb",
                name = "glb1",
                newPosition = Vector3.Zero,
                newQuaternion = Quaternion.Identity
            });
            
            // Enable animation if available
            new SetModelObjectProperty()
            {
                namePattern = "glb1",
                baseAnimId = 0
            }.IssueToDefault();
            
            gltfModelLoaded = true;
            gltfLastSelectedSubObject = "";
            Console.WriteLine($"Loaded GLTF model: {filename}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load GLTF model: {ex.Message}");
            gltfModelLoaded = false;
        }
    }
    
    private static void OpenDisplayAssetsPanel(Terminal t)
    {
        if (displayAssetsPanel == null)
        {
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
            var rq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2);
            var ir = false;

            displayAssetsPanel = GUI.PromptPanel(pb =>
            {
                {
                    pb.CollapsingHeaderStart("Appearance Settings");


                    Vector3 campos = Vector3.Zero, lookat = Vector3.Zero;

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
                        }.IssueToTerminal(GUI.localTerminal);
                    }

                    // Calculate azimuth and altitude from camera position and lookat
                    float azimuth = 0f, altitude = 0f;
                    if ((campos - lookat).Length() > 0.001f)
                    {
                        Vector3 direction = Vector3.Normalize(campos - lookat);

                        // Calculate azimuth (horizontal angle) in degrees
                        azimuth = (float)(Math.Atan2(direction.Y, direction.X));

                        // Calculate altitude (vertical angle) in degrees
                        altitude = (float)(Math.Asin(direction.Z));
                    }

                    pb.Label($"azimuth={azimuth:F1}, altitude={altitude:F1}");

                    string B(bool v) => v.ToString().ToLower();
                    pb.Label($"lookat={lookat}, d={(campos - lookat).Length()}");
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

                pb.Panel.ShowTitle("📦 Display Assets - Preset 3D Models");
                
                if (pb.Closing())
                {
                    displayAssetsPanel = null;
                    pb.Panel.Exit();
                    return;
                }
                
                pb.Label("Click any model to load it in the GLTF Viewer");
                pb.Separator();

                var path = "D:\\assets";
                Vector3[] lookats = [];

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

                        ir = rotate;
                        if (la != null)
                        {
                            lookats = [setcam == null ? Vector3.Zero : setcam.lookAt, .. la];
                        }
                        else lookats = null;

                        if (tracking != null)
                            Workspace.Prop(new SetObjectMoonTo() { earth = $"glb1::{tracking}", name = "me::camera" });
                        else
                            Workspace.Prop(new SetObjectMoonTo() { name = "me::camera" });
                    }
                }

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
                Model("LittlestTokyo", rq, new Vector3(0, 0, -2), 0.01f, setcam: new SetCamera()
                {
                    azimuth = 2.8f,
                    altitude = 0.1f,
                    lookAt = new Vector3(1.36f, -1.19f, 0.7f),
                    distance = 3.74f,
                    mmb_freelook = false,
                    world2phy = 100,
                }, app: new SetAppearance() { useSSAO = true, useBloom = true, drawGrid = true, drawGuizmo = false, useGround = true, sun_altitude = 0f });
                Model("guernica-3d", rq, new Vector3(0, 0, 0), 1f, setcam: new SetCamera()
                {
                    azimuth = -1.6f,
                    altitude = -0.2f,
                    lookAt = new Vector3(-0.15f, 3.7f, 1.486f),
                    distance = 3.69f,
                    world2phy = 170
                });
                Model("sphere_explosion", rq, new Vector3(0, 0, 0), 0.03f,
                    setcam: new SetCamera() { azimuth = -1.585f, altitude = 0.055f, lookAt = new Vector3(0.1904f, 3.5741f, 2.8654f), distance = 4.5170f, world2phy = 133f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: false
                );
                Model("truck_hit_brickwall_00_free",
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -(float)(Math.PI / 2)) * rq, new Vector3(0, 0, 0), 1f,
                    setcam: new SetCamera() { azimuth = -3.1f, altitude = -0.2f, lookAt = new Vector3(1.128f, 0f, 0.907f), distance = 2.458f, world2phy = 185 },
                    app: new SetAppearance() { useSSAO = true, useBloom = true, drawGrid = false, drawGuizmo = false, useGround = true, sun_altitude = 0f });

                Model("character_fight", rq, new Vector3(0, 0, 0), 1f,
                    setcam: new SetCamera() { azimuth = -1.6f, altitude = -0.2f, lookAt = new Vector3(0.03f, 1.46f, 0.368f), distance = 1.268f, world2phy = 233 },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 1.57f });

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
                Model("arteres_du_tronc", rq*rq, new Vector3(0, 3, -5), 0.01f, color_bias: new Vector3(-0.1f),
                    setcam: new SetCamera() { azimuth = -1.595f, altitude = -0.241f, lookAt = new Vector3(0.8545f, 4.8215f, 4.5248f), distance = 4.8093f, world2phy = 62f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 0.00f }, rotate: true);
                Model("visible_interactive_human_-_exploding_skull", rq, new Vector3(0, 0, 0), 0.1f,
                    setcam: new SetCamera() { azimuth = -1.477f, altitude = 0.189f, lookAt = new Vector3(-0.0949f, 2.6640f, -0.2433f), distance = 2.3287f, world2phy = 100f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f, useBloom = false }, rotate: true);

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
               Model("uzuki_topless_panty", rq, new Vector3(0, 0, 0), 2f,
                    setcam: new SetCamera() { azimuth = -1.407f, altitude = -0.296f, lookAt = new Vector3(0.2848f, -0.2564f, 0.0219f), distance = 0.2766f, world2phy = 218f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                Model("pole_dance", rq, new Vector3(0, 0, 0), 1f, setcam: new SetCamera() { azimuth = -1.658f, altitude = -0.571f, lookAt = new Vector3(0.1558f, 1.0428f, 2.0683f), distance = 1.3446f, world2phy = 411f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = true, sun_altitude = 1.57f }, rotate: true);

                pb.SeparatorText("Porn");

                Model("femme-fatale-illustrated-by-bruce-timm", rq, new Vector3(0, 0, 0), 2f,
                    setcam: new SetCamera() { azimuth = -1.620f, altitude = -0.068f, lookAt = new Vector3(0.3479f, 2.4976f, 1.1376f), distance = 3.1441f, world2phy = 123f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                Model("girl-scan-studio-1", rq, new Vector3(0, 0, 0), 1f,
                    setcam: new SetCamera() { azimuth = -1.489f, altitude = 1.189f, lookAt = new Vector3(-6.9545f, 5.1882f, 0.0000f), distance = 2.0878f, world2phy = 100f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: false,
                    la: [new Vector3(-6.72f, -4.11f, 0)]);
                Model("girl-scan-studio-2", rq, new Vector3(0, 0, 0), 1f,
                    setcam: new SetCamera() { azimuth = 0.904f, altitude = -0.086f, lookAt = new Vector3(-0.5279f, 0.1145f, 3.5366f), distance = 1.4571f, world2phy = 225f },
                    app: new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }, rotate: true);
                
            },t);
        }
        else
        {
            displayAssetsPanel.BringToFront();
        }
    }
    
    private static void PresetModelButton(PanelBuilder pb, string filename, string displayName, 
        Vector3? center = null, float scale = 1f, int rotation = 1, string tracking = null)
    {
        bool fileExists = File.Exists(filename);
        string buttonText = fileExists ? displayName : $"{displayName} (not found)";
        
        if (pb.Button(buttonText, disabled: !fileExists))
        {
            // Set parameters
            gltfCenterX = center?.X ?? 0f;
            gltfCenterY = center?.Y ?? 0f;
            gltfCenterZ = center?.Z ?? 0f;
            gltfScale = scale;
            gltfRotation = rotation;
            gltfFilename = filename;
            
            // Reset other parameters to defaults
            gltfColorScale = 1.0f;
            gltfBrightness = 1.0f;
            gltfNormalShading = 0f;
            gltfDoubleSided = false;
            
            // End any existing selection action
            gltfSelectAction?.End();
            gltfSelectAction = null;
            gltfSelectSubObjectMode = false;
            
            // Load the model
            LoadGltfModel(filename);
            
            // Open GLTF Viewer panel if not already open
            if (gltfPanel == null)
            {
                Console.WriteLine("Model loaded. Open GLTF Viewer to see controls.");
            }
            
            // Set tracking if specified (directly use the known sub-object name)
            if (!string.IsNullOrEmpty(tracking))
            {
                gltfEnableTracking = true;
                gltfTrackingObjectName = tracking;
                
                // Wait a bit for model to load, then set tracking
                new Thread(() =>
                {
                    Thread.Sleep(500); // Give model time to load
                    
                    var targetObject = $"model_glb::{tracking}";
                    Workspace.Prop(new SetObjectMoonTo() { earth = targetObject, name = "me::camera" });
                    gltfCurrentTrackedObject = targetObject;
                    Console.WriteLine($"Auto-tracking: {targetObject}");
                }).Start();
            }
            else
            {
                gltfEnableTracking = false;
                gltfCurrentTrackedObject = "";
            }
        }
    }
}
