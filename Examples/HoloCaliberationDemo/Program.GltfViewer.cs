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
    
    // Tracking parameters
    private static bool gltfEnableTracking = false;
    private static string[] gltfSubObjects = Array.Empty<string>();
    private static int gltfSelectedSubObjectIndex = 0;
    private static string gltfCurrentTrackedObject = "";

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
                new SetCamera()
                {
                    azimuth = -(float)(Math.PI / 2),
                    altitude = 0.1f,
                    lookAt = new Vector3(0f, 0f, 0f),
                    distance = 3.0f,
                    world2phy = 100f
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
                    
                    // Handle panel close - hide model
                    if (pbv.Closing())
                    {
                        if (gltfModelLoaded)
                        {
                            // Remove the model object
                            WorkspaceProp.RemoveNamePattern("custom_glb_obj");
                            gltfModelLoaded = false;
                        }
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
                            name = "custom_glb"
                        });
                    }

                    pbv.Separator();
                    pbv.Label($"Current file: {gltfFilename}");
                    
                    // Load button
                    if (pbv.Button("Load GLB/GLTF File"))
                    {
                        if (pbv.OpenFile("Select GLTF/GLB file", "glb,gltf", out var filename))
                        {
                            gltfFilename = filename;
                            LoadGltfModel(filename);
                        }
                    }
                    
                    pbv.SameLine();
                    if (pbv.Button("📦 Browse Preset Models"))
                    {
                        OpenDisplayAssetsPanel();
                    }

                    // Show/Hide button when model is loaded
                    if (gltfModelLoaded)
                    {
                        pbv.Separator();
                        if (pbv.Button("🗑️ Hide Model"))
                        {
                            WorkspaceProp.RemoveNamePattern("custom_glb_obj");
                            gltfModelLoaded = false;
                            gltfEnableTracking = false;
                            gltfSubObjects = Array.Empty<string>();
                            Workspace.Prop(new SetObjectMoonTo() { name = "me::camera" }); // Cancel tracking
                        }
                        pbv.SameLine();
                        if (pbv.Button("🔄 Reset View"))
                        {
                            // Reset camera to default view
                            new SetCamera()
                            {
                                azimuth = -(float)(Math.PI / 2),
                                altitude = 0.1f,
                                lookAt = new Vector3(0f, 0f, 0f),
                                distance = 3.0f,
                                world2phy = 100f
                            }.IssueToDefault();
                        }
                        
                        // Camera tracking section
                        pbv.SeparatorText("Camera Tracking");
                        
                        if (pbv.CheckBox("Enable Tracking", ref gltfEnableTracking))
                        {
                            if (!gltfEnableTracking)
                            {
                                // Disable tracking - release camera
                                Workspace.Prop(new SetObjectMoonTo() { name = "me::camera" });
                                gltfCurrentTrackedObject = "";
                            }
                        }
                        
                        if (gltfEnableTracking)
                        {
                            // List sub-objects button
                            if (pbv.Button("Refresh Sub-Objects List"))
                            {
                                QueryModelSubObjects();
                            }
                            
                            if (gltfSubObjects.Length > 0)
                            {
                                pbv.Label($"Found {gltfSubObjects.Length} sub-objects:");
                                
                                // Use ComboBox or RadioButtons to select sub-object
                                // if (pbv.Combo("Select Sub-Object", gltfSubObjects, ref gltfSelectedSubObjectIndex))
                                // {
                                //     // Selection changed
                                // }
                                
                                pbv.Label($"Selected: {gltfSubObjects[gltfSelectedSubObjectIndex]}");
                                
                                if (pbv.Button("Track Selected Object"))
                                {
                                    var targetObject = $"custom_glb_obj::{gltfSubObjects[gltfSelectedSubObjectIndex]}";
                                    Workspace.Prop(new SetObjectMoonTo() { earth = targetObject, name = "me::camera" });
                                    gltfCurrentTrackedObject = targetObject;
                                    Console.WriteLine($"Camera now tracking: {targetObject}");
                                }
                                
                                if (!string.IsNullOrEmpty(gltfCurrentTrackedObject))
                                {
                                    pbv.Label($"Currently tracking: {gltfCurrentTrackedObject}");
                                    
                                    if (pbv.Button("Cancel Tracking"))
                                    {
                                        Workspace.Prop(new SetObjectMoonTo() { name = "me::camera" });
                                        gltfCurrentTrackedObject = "";
                                        Console.WriteLine("Camera tracking cancelled");
                                    }
                                }
                            }
                            else
                            {
                                pbv.Label("No sub-objects found. Click 'Refresh' to scan.");
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

                }, remote);
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
                name = "custom_glb"
            });
            
            // Place model object in scene
            Workspace.Prop(new PutModelObject()
            {
                clsName = "custom_glb",
                name = "custom_glb_obj",
                newPosition = Vector3.Zero,
                newQuaternion = Quaternion.Identity
            });
            
            // Enable animation if available
            new SetModelObjectProperty()
            {
                namePattern = "custom_glb_obj",
                baseAnimId = 0
            }.IssueToDefault();
            
            gltfModelLoaded = true;
            Console.WriteLine($"Loaded GLTF model: {filename}");
            
            // Automatically query sub-objects after loading
            QueryModelSubObjects();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load GLTF model: {ex.Message}");
            gltfModelLoaded = false;
        }
    }
    
    private static void QueryModelSubObjects()
    {
        try
        {
            // Query the model object hierarchy
            // new QueryObjects()
            // {
            //     pattern = "custom_glb_obj",
            //     callback = objects =>
            //     {
            //         if (objects != null && objects.Count > 0)
            //         {
            //             var subObjectsList = new List<string>();
            //             
            //             // Extract sub-object names from the first object (which is our model)
            //             foreach (var obj in objects)
            //             {
            //                 if (obj.SubObjects != null && obj.SubObjects.Count > 0)
            //                 {
            //                     foreach (var subObj in obj.SubObjects)
            //                     {
            //                         if (!string.IsNullOrEmpty(subObj.Name))
            //                         {
            //                             subObjectsList.Add(subObj.Name);
            //                         }
            //                     }
            //                 }
            //             }
            //             
            //             gltfSubObjects = subObjectsList.ToArray();
            //             Console.WriteLine($"Found {gltfSubObjects.Length} sub-objects in the model");
            //             
            //             if (gltfSubObjects.Length > 0)
            //             {
            //                 Console.WriteLine("Sub-objects: " + string.Join(", ", gltfSubObjects.Take(10)));
            //                 if (gltfSubObjects.Length > 10)
            //                     Console.WriteLine($"... and {gltfSubObjects.Length - 10} more");
            //             }
            //         }
            //         else
            //         {
            //             gltfSubObjects = Array.Empty<string>();
            //             Console.WriteLine("No sub-objects found");
            //         }
            //     }
            // }.IssueToDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to query model sub-objects: {ex.Message}");
            gltfSubObjects = Array.Empty<string>();
        }
    }
    
    private static void OpenDisplayAssetsPanel()
    {
        if (displayAssetsPanel == null)
        {
            displayAssetsPanel = GUI.PromptPanel(pb =>
            {
                pb.Panel.ShowTitle("📦 Display Assets - Preset 3D Models");
                
                if (pb.Closing())
                {
                    displayAssetsPanel = null;
                    pb.Panel.Exit();
                    return;
                }
                
                pb.Label("Click any model to load it in the GLTF Viewer");
                pb.Separator();
                
                // Preset models section
                pb.SeparatorText("Holographic Demos");
                PresetModelButton(pb, "pac-man_remaster.glb", "🎮 Pac-Man", 
                    tracking: "Object_957");
                PresetModelButton(pb, "opposed_piston_engine_mechanism.glb", "⚙️ Piston Engine");
                PresetModelButton(pb, "12_animated_butterflies.glb", "🦋 Butterflies", scale: 0.01f);
                PresetModelButton(pb, "game_pirate_adventure_map.glb", "🏴‍☠️ Pirate Map", scale: 0.001f);
                
                pb.SeparatorText("Scenes");
                PresetModelButton(pb, "LittlestTokyo.glb", "🏯 Littlest Tokyo", 
                    center: new Vector3(0, 0, -2), scale: 0.01f);
                PresetModelButton(pb, "guernica-3d.glb", "🎨 Guernica 3D");
                PresetModelButton(pb, "sphere_explosion.glb", "💥 Sphere Explosion", scale: 0.03f);
                PresetModelButton(pb, "futuristic_hallway_with_patrolling_robot.glb", "🤖 Futuristic Hallway");
                PresetModelButton(pb, "space_loop_city.glb", "🌌 Space Loop City", scale: 0.001f);
                
                pb.SeparatorText("Game Assets");
                PresetModelButton(pb, "cuphead_-_hilda_berg_boss_fight.glb", "☁️ Cuphead Boss", 
                    tracking: "propeller_0");
                PresetModelButton(pb, "akm_fps_animation.glb", "🔫 AKM FPS");
                PresetModelButton(pb, "caterpillar_work_boot.glb", "👢 Work Boot", scale: 3f);
                PresetModelButton(pb, "truck_hit_brickwall_00_free.glb", "🚚 Truck Crash");
                
                pb.SeparatorText("Characters");
                PresetModelButton(pb, "bunny_swimsuit_black_pubg.glb", "👯 Bunny PUBG");
                PresetModelButton(pb, "sayuri_dance_fix.glb", "💃 Sayuri Dance");
                PresetModelButton(pb, "character_fight.glb", "🥊 Character Fight");
                PresetModelButton(pb, "pika_girl.glb", "⚡ Pika Girl", 
                    center: new Vector3(0, 0, -2), scale: 0.3f);
                
                pb.SeparatorText("Art & Masterpieces");
                PresetModelButton(pb, "isleworth-mona-lisa-3d.glb", "👩‍🎨 Mona Lisa 3D");
                PresetModelButton(pb, "reclining-nude-3d.glb", "🖼️ Reclining Nude 3D");
                PresetModelButton(pb, "persistence-of-memory-3d.glb", "🕰️ Persistence of Memory");
                PresetModelButton(pb, "dreamsong.glb", "🎼 Dreamsong", scale: 0.01f);
                PresetModelButton(pb, "sea_keep_lonely_watcher.glb", "🏰 Sea Keep", scale: 0.01f);
                
                pb.SeparatorText("Medical");
                PresetModelButton(pb, "lymphatic_system_an_overview.glb", "🫁 Lymphatic System", 
                    center: new Vector3(0, 0, 1.5f), scale: 0.002f, rotation: 0);
                PresetModelButton(pb, "visible_interactive_human_-_exploding_skull.glb", "💀 Exploding Skull", scale: 0.1f);
                PresetModelButton(pb, "arteres_du_tronc.glb", "❤️ Arteries", 
                    center: new Vector3(0, 3, -5), scale: 0.01f);
                PresetModelButton(pb, "injected-human-foetus-14-weeks-old-microct.glb", "👶 Foetus 14wk", scale: 0.1f);
                
                pb.SeparatorText("3D Reconstruction");
                PresetModelButton(pb, "mar_saba_monastery.glb", "⛪ Mar Saba Monastery");
                PresetModelButton(pb, "skeleton_excavation_dataset.glb", "🦴 Skeleton Excavation", 
                    center: new Vector3(0, 0, -3));
                PresetModelButton(pb, "new_york_city._manhattan.glb", "🗽 NYC Manhattan");
                
                pb.SeparatorText("Vehicles & Objects");
                PresetModelButton(pb, "sukhoi_su-35_fighter_jet.glb", "✈️ Sukhoi SU-35", scale: 0.1f);
                PresetModelButton(pb, "ship_in_a_bottle.glb", "⛵ Ship in Bottle", scale: 0.01f, rotation: 0);
                PresetModelButton(pb, "2021_porsche_911_targa_4s_heritage_design_992.glb", "🏎️ Porsche 911", scale: 300f);
                PresetModelButton(pb, "war_plane.glb", "🛩️ War Plane", scale: 0.01f);
                
                pb.SeparatorText("Horror");
                PresetModelButton(pb, "demogorgon_rig.glb", "👹 Demogorgon");
                PresetModelButton(pb, "hallucination_huggy_-_poppy_playtime_chapter_3.glb", "🧸 Huggy Wuggy", scale: 7f);
                
            }, remote);
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
            
            // Load the model
            LoadGltfModel(filename);
            
            // Open GLTF Viewer panel if not already open
            if (gltfPanel == null)
            {
                // Will be opened by user manually, or auto-open here
                Console.WriteLine("Model loaded. Open GLTF Viewer to see controls.");
            }
            
            // Set tracking if specified
            if (!string.IsNullOrEmpty(tracking))
            {
                gltfEnableTracking = true;
                // Wait a bit for model to load, then find and track the object
                new Thread(() =>
                {
                    Thread.Sleep(500); // Give model time to load
                    
                    // Query sub-objects to find the tracking target
                    QueryModelSubObjects();
                    
                    Thread.Sleep(200); // Wait for query to complete
                    
                    // Find the tracking object in the list
                    var trackIndex = Array.FindIndex(gltfSubObjects, obj => obj == tracking);
                    if (trackIndex >= 0)
                    {
                        gltfSelectedSubObjectIndex = trackIndex;
                        var targetObject = $"custom_glb_obj::{tracking}";
                        Workspace.Prop(new SetObjectMoonTo() { earth = targetObject, name = "me::camera" });
                        gltfCurrentTrackedObject = targetObject;
                        Console.WriteLine($"Auto-tracking: {targetObject}");
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Tracking object '{tracking}' not found in model");
                    }
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
