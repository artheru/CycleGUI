using CycleGUI;
using CycleGUI.API;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using IconFonts;
using LearnCycleGUI.Utilities;

namespace LearnCycleGUI.Demo
{
    internal class DemoWorkspaceHandler
    {
        public static unsafe PanelBuilder.CycleGUIHandler PreparePanel()
        {
            // Point Cloud
            PutPointCloud putPointCloud = null;
            var pointCloudOn = false;
            var pointCloudNumber = 5000f;
            var pointCloudColorR = 0f;
            var pointCloudColorG = 255f;
            var pointCloudColorB = 255f;
            var pointCloudColorA = 255f;

            // Picture

            // USB Camera
            PutImage displayImage = null;
            PutRGBA streamer = null;
            Action<byte[]> updater;
            var usbCameraOn = false;
            UsbCamera camera;

            // Mesh
            var meshShown = false;
            var meshResolution = 32f;
            var meshSmooth = false;
            PutModelObject meshModelObject = null;
            var meshColorR = 0f;
            var meshColorG = 117f;
            var meshColorB = 100f;
            var meshColorA = 255f;
            var enableCrossSection = false;

            // 3D Model
            PutModelObject putModelObject = null;
            var model3dLoaded = false;
            var obj_placed = false;
            var model3dX = 0f;
            var model3dY = 0f;
            var model3dSelectable = false;
            var model3dSelectInfo = "Not selected yet.";
            var selectionModes = new string[] { "Click", "Rectangle", "Paint" };
            var currentSelectionMode = 0; // Default to Click mode
            var paintRadius = 10f;
            SelectObject model3dSelectAction = null;
            var t2trans = 0.5f;


            var animation3d = false;
            var stop_at_end = false;
            var use_as_base = false;
            var asap = true;

            // Viewport Manipulation
            var cameraLookAtX = 0f;
            var cameraLookAtY = 0f;
            var cameraAltitude = (float)Math.PI / 2;
            var cameraAzimuth = -(float)Math.PI / 2;
            var cameraDistance = 10f;
            var cameraFov = 45f;
            var sun = 0f;
            var useCrossSection = false;
            var useEDL = true;
            var camModeOrth = false;
            var useSSAO = true;
            var useGround = true;
            var useBorder = true;
            var useBloom = true;
            var drawGrid = true;
            var drawGuizmo = true;
            var btfOnHovering = true;

            int prop_display_type = 1;

            // Bezier Curves and Lines Demo
            Vector3 lineStart = new Vector3(0, 0, 0);
            Vector3 lineEnd = new Vector3(2, 0, 0);
            Vector3 controlPoint1 = new Vector3(0.5f, 1, 0);
            Vector3 controlPoint2 = new Vector3(1.5f, 1, 0);
            float lineWidth = 2;
            bool showArrow = false;
            Color lineColor = Color.Red;
            Color curveColor = Color.Green;
            float dashDensity = 0;
            PutStraightLine currentLine = null;
            PutBezierCurve currentCurve = null;
            bool showLines = false;
            PutPointCloud stPnt = null, edPnt = null, ctrlPnt1 = null, ctrlPnt2 = null;
            SelectObject pntSelect = null;

            SelectObject hndSelect = null;

            int selectedMode = 0;

            bool show_op_grid = false;

            bool BuildPalette(PanelBuilder pb, string label, ref float a, ref float b, ref float g, ref float r)
            {
                var interaction = pb.DragFloat($"{label}: A", ref a, 1, 0, 255);
                interaction |= pb.DragFloat($"{label}: B", ref b, 1, 0, 255);
                interaction |= pb.DragFloat($"{label}: G", ref g, 1, 0, 255);
                interaction |= pb.DragFloat($"{label}: R", ref r, 1, 0, 255);
                return interaction;
            }

            uint ConcatHexABGR(float a, float b, float g, float r)
            {
                return ((uint)a << 24) + ((uint)b << 16) + ((uint)g << 8) + (uint)r;
            }

            void GenJpg(byte[] rgb, int w, int h)
            {
                using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                var rect = new Rectangle(0, 0, w, h);
                var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                nint ptr = bmpData.Scan0;
                for (var i = 0; i < h; i++)
                    Marshal.Copy(rgb, w * i * 3, ptr + bmpData.Stride * i, w * 3);

                bitmap.UnlockBits(bmpData);

                bitmap.Save("capture.jpg");
            }

            return pb =>
            {
                if (pb.Closing())
                {
                    Program.CurrentPanels.Remove(pb.Panel);
                    pb.Panel.Exit();
                    return;
                }

                {
                    pb.CollapsingHeaderStart("Painter");
                    if (pb.Button("Draw"))
                    {
                        var p = Painter.GetPainter("TEST");
                        for (int i = 0; i < 100; ++i)
                            p.DrawVector(new Vector3(i * 0.1f - 5, 0, 0),
                                new Vector3(0, (float)Math.Cos(i * 0.1), (float)Math.Sin(i * 0.1)), Color.Wheat,
                                1, (int)(20 + Math.Sin(i * 0.04) * 10));

                    }
                    if (pb.Button("Clear"))
                        Painter.GetPainter("TEST").Clear();

                    pb.CollapsingHeaderEnd();
                }
                // Point Cloud
                {
                    pb.CollapsingHeaderStart("Point Cloud");

                    if (!pointCloudOn)
                    {
                        pb.DragFloat("Number of Points", ref pointCloudNumber, 1000, 1000, 10000000);
                        pb.SeparatorText("Point Cloud Color");
                        BuildPalette(pb, "PC", ref pointCloudColorA, ref pointCloudColorB, ref pointCloudColorG,
                            ref pointCloudColorR);

                        var colorVal = ConcatHexABGR(pointCloudColorA, pointCloudColorB, pointCloudColorG,
                            pointCloudColorR);
                        pb.Label($"0x{colorVal:x8}");

                        if (pb.Button("Add Point Cloud"))
                        {
                            Workspace.Prop(putPointCloud = new PutPointCloud()
                            {
                                name = "test_point_cloud",
                                xyzSzs = Enumerable.Range(0, (int)pointCloudNumber).Select(p =>
                                    new Vector4((float)(p / 2000f * Math.Cos(p / 200f)), (float)(p / 2000f * Math.Sin(p / 200f)),
                                        (float)(p / 5000f * Math.Sin(p / 10f)), 2)).ToArray(),
                                colors = Enumerable.Repeat(colorVal, (int)pointCloudNumber).ToArray(),
                                // newPosition = new Vector3(1, 5, 2),
                                handleString = "\uf1ce" //fa-circle-o-notch
                            });
                            pointCloudOn = true;
                        }
                    }

                    if (pointCloudOn)
                    {
                        pb.Label($"Displaying {pointCloudNumber} points");
                        if (pb.Button("Remove Point Cloud"))
                        {
                            putPointCloud?.Remove();
                            pointCloudOn = false;
                        }
                    }

                    pb.CollapsingHeaderEnd();
                }

                // Picture
                // pb.CollapsingHeaderStart("Picture");
                //
                // pb.CollapsingHeaderEnd();

                // USB Camera
                {
                    pb.CollapsingHeaderStart("Sprites");

                    if (pb.Button("Add SVG"))
                    {
                        Workspace.AddProp(new DeclareSVG()
                        {
                            name = "tiger",
                            svgContent = File.ReadAllText("tiger.svg")
                        });
                        Workspace.AddProp(new PutImage()
                        {
                            newPosition = new Vector3(0, 0, 0),
                            displayH = 1,
                            displayW = 1,
                            name = "svg",
                            rgbaName = "svg:tiger",
                        });
                    }

                    if (!usbCameraOn && pb.Button("Start Capturing USB Camera"))
                    {
                        var cameraList = UsbCamera.FindDevices().Select(str => str.Replace(" ", "_")).ToArray();
                        if (cameraList.Length == 0) UITools.Alert("No USB camera available!");
                        else
                        {
                            var format = UsbCamera.GetVideoFormat(0)[0];
                            var cached = new byte[format.Size.Height * format.Size.Width * 4];
                            displayImage = new PutImage()
                            {
                                newPosition = new Vector3(0, 0, 0),
                                displayH = 1,
                                displayW = format.Size.Width / (float)format.Size.Height,
                                name = "usbCamera",
                                rgbaName = "cameraRgba",
                            };

                            Workspace.Prop(displayImage);

                            streamer = Workspace.AddProp(new PutRGBA()
                            {
                                height = format.Size.Height,
                                width = format.Size.Width,
                                name = "cameraRgba",
                            });

                            updater = streamer.StartStreaming();

                            camera = new UsbCamera(0, format, new UsbCamera.GrabberExchange()
                            {
                                action = (_, ptr, _) =>
                                {
                                    byte* pbr = (byte*)ptr;
                                    for (int i = 0; i < format.Size.Height; ++i)
                                        for (int j = 0; j < format.Size.Width; ++j)
                                        {
                                            cached[((format.Size.Height - 1 - i) * format.Size.Width + j) * 4] =
                                                pbr[(i * format.Size.Width + j) * 3 + 2];
                                            cached[((format.Size.Height - 1 - i) * format.Size.Width + j) * 4 + 1] =
                                                pbr[(i * format.Size.Width + j) * 3 + 1];
                                            cached[((format.Size.Height - 1 - i) * format.Size.Width + j) * 4 + 2] =
                                                pbr[(i * format.Size.Width + j) * 3 + 0];
                                            cached[((format.Size.Height - 1 - i) * format.Size.Width + j) * 4 + 3] = 255;
                                        }

                                    updater(cached);
                                }
                            });
                            camera.Start();
                            usbCameraOn = true;
                        }
                    }

                    if (usbCameraOn && pb.Button("Stop Capturing"))
                    {
                        displayImage?.Remove();
                        displayImage = null;
                        usbCameraOn = false;
                    }

                    pb.CollapsingHeaderEnd();
                }

                // Mesh and Cross-Section View
                {
                    pb.CollapsingHeaderStart("Mesh and Cross-Section View");

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

                    void UpdateMesh(int res, bool highRes, uint color)
                    {
                        CreateSphereMesh(res, out var positions);
                        Workspace.Prop(new DefineMesh()
                        {
                            clsname = "custom1",
                            positions = positions,
                            color = color,
                            smooth = highRes
                        });

                        Workspace.Prop(meshModelObject = new PutModelObject()
                        {
                            clsName = "custom1",
                            name = "sphere1",
                            newPosition = new Vector3(0, 0, 0)
                        });
                    }

                    if (!meshShown && pb.Button("Show Mesh"))
                    {
                        UpdateMesh((int)meshResolution, meshSmooth,
                            ConcatHexABGR(meshColorA, meshColorB, meshColorG, meshColorR));
                        meshShown = true;
                    }

                    if (meshShown)
                    {
                        if (pb.Button("Stop Showing Mesh"))
                        {
                            meshModelObject?.Remove();
                            meshShown = false;
                        }
                        else
                        {
                            var toUpdate = pb.DragFloat("Resolution", ref meshResolution, 1, 8, 64);
                            toUpdate |= BuildPalette(pb, "mesh", ref meshColorA, ref meshColorB, ref meshColorG, ref meshColorR);
                            toUpdate |= pb.CheckBox("Smooth Normal", ref meshSmooth);

                            if (toUpdate)
                                UpdateMesh((int)meshResolution, meshSmooth,
                                    ConcatHexABGR(meshColorA, meshColorB, meshColorG, meshColorR));

                            pb.SeparatorText("Cross-Section View");
                            var toIssue = pb.CheckBox("Enable Cross-Section", ref enableCrossSection);
                            if (enableCrossSection)
                            {
                                // todo: 
                                pb.Label("todo...");
                            }

                            // if (toIssue) new SetAppearance() { useCrossSection = enableCrossSection, clippingDirection = -Vector3.UnitY }.Issue();
                        }
                    }

                    pb.CollapsingHeaderEnd();
                }

                // 3D Model
                {
                    pb.CollapsingHeaderStart("3D Model");

                    var model3dPosChanged = pb.DragFloat("X", ref model3dX, 0.01f, -3, 3);
                    model3dPosChanged |= pb.DragFloat("Y", ref model3dY, 0.01f, -3, 3);

                    if (!model3dLoaded && pb.Button($"Load Stork and Horse"))
                    {
                        if (File.Exists($"Parrot.glb"))
                        {
                            Workspace.Prop(new LoadModel()
                            {
                                detail = new Workspace.ModelDetail(File.ReadAllBytes($"Parrot.glb"))
                                {
                                    Center = new Vector3(model3dX, model3dY, 0),
                                    Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                                    Scale = 0.01f
                                },
                                name = "stork"
                            });


                            model3dLoaded = true;
                        }
                        else UITools.Alert($"Stork.glb not exist!", t: pb.Panel.Terminal);

                        if (File.Exists("Horse.glb"))
                        {
                            Workspace.Prop(new LoadModel()
                            {
                                detail = new Workspace.ModelDetail(File.ReadAllBytes($"Horse.glb"))
                                {
                                    Center = new Vector3(model3dX, model3dY, 0),
                                    Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                                    Scale = 0.01f
                                },
                                name = "horse"
                            });
                        }
                        else UITools.Alert($"Horse.glb not exist!", t: pb.Panel.Terminal);
                    }

                    if (model3dLoaded && !obj_placed && pb.Button("Place Object"))
                    {
                        Workspace.Prop(putModelObject = new PutModelObject()
                        {
                            clsName = "horse",
                            name = "m_horse",
                            newPosition = new Vector3(model3dX, model3dY, 0)
                        });

                        Workspace.Prop(putModelObject = new PutModelObject()
                        {
                            clsName = "stork",
                            name = "m_stork_T",
                            newPosition = new Vector3(model3dX, model3dY, 2)
                        });
                        new SetObjectApperance() { namePattern = "m_stork_T", transparency = 0.5f }.IssueToDefault();
                        obj_placed = true;
                    }

                    if (obj_placed)
                    {
                        if (pb.Button("Fit to horse"))
                        {
                            new FrameToFit() { name = "m_horse" }.IssueToDefault();
                        }
                        if (pb.Button("Fit to stork"))
                        {
                            new FrameToFit() { name = "m_stork_T" }.IssueToDefault();
                        }
                        if (pb.Button("Horse bring to front"))
                        {
                            new SetObjectApperance(){namePattern = "m_horse", bring_to_front = true}.IssueToDefault();
                        }
                        if (pb.DragFloat("transparency", ref t2trans, 0.01f, 0, 1f))
                        {
                            new SetObjectApperance() { namePattern = "m_stork_T", transparency = t2trans }.IssueToDefault();
                        }
                        if (pb.Button($"Remove all objects"))
                        {
                            WorkspaceProp.RemoveNamePattern($"m_*");
                            obj_placed = false;
                        }
                    }

                    if (obj_placed)
                    {
                        if (pb.Button("Swap class"))
                        {
                            Workspace.Prop(putModelObject = new PutModelObject()
                            {
                                clsName = "stork",
                                name = "m_horse",
                                newPosition = new Vector3(model3dX, model3dY, 0)
                            });

                            Workspace.Prop(putModelObject = new PutModelObject()
                            {
                                clsName = "horse",
                                name = "m_stork_T",
                                newPosition = new Vector3(model3dX, model3dY, 2)
                            });
                        }

                        if (model3dPosChanged)
                            Workspace.Prop(putModelObject = new PutModelObject()
                            {
                                clsName = "model_glb",
                                name = "m_horse",
                                newPosition = new Vector3(model3dX, model3dY, 0)
                            });

                        if (pb.Button("GetPosition tranform"))
                        {
                            Workspace.Prop(new SetObjectMoonTo() { earth = "me::mouse", name = "m_horse" });
                            new GetPosition()
                            {
                                feedback = (ret, _) =>
                                {
                                    Console.WriteLine($"clicked pos = {ret.mouse_pos.X}, {ret.mouse_pos.Y}, obj={ret.snapping_object}");
                                    Workspace.Prop(new TransformObject() { name = "m_horse", coord = TransformObject.Coord.Relative });
                                }
                            }.Start();
                            // UITools.Alert("this is a test alert diaglog. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.", t:pb.Panel.Terminal);
                        }
                        pb.Button("Note:We can also use SetObjectMoonTo to transform me::camera");

                        if (pb.Button("Drag to translate"))
                        {
                            new FollowMouse()
                            {
                                method= FollowMouse.FollowingMethod.LineOnGrid,
                                follower_objects = ["m_horse"],
                                finished = () => { Console.WriteLine("Dragged"); },
                                terminated = () => { Console.WriteLine("Terminated"); },
                                feedback = (feedback, _) =>
                                {
                                    Console.WriteLine($"mouse from:{feedback.mouse_start_XYZ} -> {feedback.mouse_end_XYZ}");
                                }
                            }.Start();
                        }

                        pb.CheckBox("Selectable", ref model3dSelectable);
                        if (model3dSelectable)
                        {
                            pb.Label("Now you can left click on Workspace to select/unselect the model.");
                            pb.Label(model3dSelectInfo);

                            if (model3dSelectAction == null)
                            {
                                model3dSelectAction = new SelectObject()
                                {
                                    terminal = pb.Panel.Terminal,
                                    feedback = (tuples, _) =>
                                    {
                                        model3dSelectInfo =
                                            tuples.Length == 0 ? "Not selected yet." : $"\"m_horse\" selected.";

                                        new GuizmoAction()
                                        {
                                            type = GuizmoAction.GuizmoType.MoveXYZ,
                                            finished = () =>
                                            {
                                                Console.WriteLine("OKOK...");
                                                model3dSelectAction?.SetSelection([]);
                                            },
                                            terminated = () =>
                                            {
                                                Console.WriteLine("Forget it...");
                                                model3dSelectAction?.SetSelection([]);
                                            },
                                            //snaps = ["pc"]
                                        }.Start();
                                    },
                                };
                                model3dSelectAction.Start();

                                // Set the initial selection mode
                                model3dSelectAction.SetSelectionMode(
                                    (SelectObject.SelectionMode)currentSelectionMode,
                                    paintRadius);

                                model3dSelectAction.SetObjectSelectable("m_horse");
                            }

                            // Add selection mode combo box
                            if (pb.DropdownBox("Selection Mode", selectionModes, ref currentSelectionMode))
                            {
                                // Update selection mode when changed
                                model3dSelectAction.SetSelectionMode(
                                    (SelectObject.SelectionMode)currentSelectionMode,
                                    paintRadius);
                            }

                            // Only show paint radius slider when Paint mode is selected
                            if (currentSelectionMode == 2) // Paint mode
                            {
                                if (pb.DragFloat("Paint Radius", ref paintRadius, 5f, 50f))
                                {
                                    model3dSelectAction.SetSelectionMode(
                                        SelectObject.SelectionMode.Paint,
                                        paintRadius);
                                }
                            }

                            // Add help text for each selection mode
                            pb.SeparatorText("Selection Mode Help");
                            switch (currentSelectionMode)
                            {
                                case 0:
                                    pb.Label("Click Mode: Simply click on objects to select them.");
                                    break;
                                case 1:
                                    pb.Label("Rectangle Mode: Click and drag to create a selection rectangle.");
                                    break;
                                case 2:
                                    pb.Label("Paint Mode: Click or drag to paint over objects to select them.");
                                    pb.Label($"Using paint radius: {paintRadius:F1} pixels");
                                    break;
                            }
                        }
                    }

                    if (!model3dLoaded || !model3dSelectable)
                    {
                        model3dSelectAction?.End();
                        model3dSelectAction = null;
                        model3dSelectInfo = "Not selected yet.";
                    }

                    pb.CollapsingHeaderEnd();
                }

                // Grid Appearance and Operational Grid Demo
                {
                    pb.CollapsingHeaderStart("Grid Appearance & Operational Grid");

                    // Operational Grid Configuration
                    pb.SeparatorText("Operational Grid Configuration");

                    if (pb.Button("Apply Default Grid Settings"))
                    {
                        new SetOperatingGridAppearance()
                        {
                            pivot = Vector3.Zero,
                            unitX = Vector3.UnitX,
                            unitY = Vector3.UnitY
                        }.Issue();
                    }
                    if (pb.Button("Apply Custom Grid Settings"))
                    {
                        new SetOperatingGridAppearance()
                        {
                            pivot = Vector3.One,
                            unitX = Vector3.UnitX,
                            unitY = Vector3.UnitZ
                        }.Issue();
                    }

                    if (pb.CheckBox("Show operating grid", ref show_op_grid))
                    {
                        new SetOperatingGridAppearance()
                        {
                            show = show_op_grid
                        }.Issue();
                    }

                    pb.SeparatorText("Position Operations");

                    if (pb.Button("Get Position"))
                    {
                        new GetPosition()
                        {
                            method = GetPosition.PickMode.GridPlane,
                            feedback = (pos, _) =>
                            {
                                Console.WriteLine($"Position on Operational Grid: {pos.mouse_pos}");
                                if (!string.IsNullOrEmpty(pos.snapping_object))
                                    Console.WriteLine($"Snapped to: {pos.snapping_object} at {pos.object_pos}");
                            }
                        }.Start();
                    }

                    // Follow Mouse test controls
                    pb.SeparatorText("Follow Mouse");
                    {
                        // Persist selection across frames
                        const string followModeKey = "##follow_mouse_mode";
                        // Map enum to items
                        string[] modes = new[] { "LineOnGrid", "RectOnGrid", "Line3D", "Rect3D", "Box3D", "Sphere3D", "PointOnGrid" };
                        // store/read selection via a hidden dropdown to reuse state infra is not available; use RadioButtons directly
                        pb.RadioButtons("Follow Mode", modes, ref selectedMode);

                        if (pb.Button("Follow Mouse"))
                        {
                            var fm = FollowMouse.FollowingMethod.LineOnGrid;
                            try { fm = (FollowMouse.FollowingMethod)selectedMode; } catch {}
                            Console.WriteLine($"Use {fm} to follow");
                            new FollowMouse()
                            {
                                method = fm,
                                realtime = true,
                                feedback = (feedback, _) =>
                                {
                                    Console.WriteLine($"Mouse moved on operational grid from {feedback.mouse_start_XYZ} to {feedback.mouse_end_XYZ}");
                                },
                                finished = () => Console.WriteLine("Follow mouse operation completed"),
                                terminated = () => Console.WriteLine("Follow mouse operation cancelled")
                            }.Start();
                        }
                    }

                    pb.CollapsingHeaderEnd();
                }

                // Animation
                {
                    pb.CollapsingHeaderStart("3D Model Animation control");

                    if (!animation3d && pb.Button("Load Model"))
                    {
                        Workspace.Prop(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(
                                File.ReadAllBytes("RobotExpressive.glb"))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                                Scale = 0.3f
                            },
                            name = "robotexpressive"
                        });
                        Workspace.AddProp(new PutModelObject()
                        {
                            clsName = "robotexpressive",
                            name = "s1",
                            newPosition = new Vector3(1, 0, 0f),
                            newQuaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI)
                        });

                        animation3d = true;
                    }

                    pb.Toggle("Use as base animation", ref use_as_base);
                    pb.Toggle("Stop at end", ref stop_at_end);
                    pb.Toggle("Playback ASAP", ref asap);

                    if (pb.ButtonGroup("Trigger Animation", ["dance"
                            ,"death"
                            ,"idle"
                            ,"jump"
                            ,"no"
                            ,"punch"
                            ,"running"
                            ,"sitting"
                            ,"standing"
                            ,"thumb up"
                            ,"walking"
                            ,"walk-jump"
                            ,"wave"
                            ,"yes"], out var anim))
                    {
                        var set = new SetModelObjectProperty() { namePattern = "s1", };
                        if (use_as_base)
                        {
                            set.baseAnimId = anim;
                            set.base_stopatend = stop_at_end;
                        }
                        else
                        {
                            set.nextAnimId = anim;
                            set.next_stopatend = stop_at_end;
                            set.animate_asap = asap;
                        }
                        set.IssueToDefault();
                    }

                    pb.CollapsingHeaderEnd();
                }

                {

                    pb.CollapsingHeaderStart("World UI");
                    if (pb.Button("Show Handle") && hndSelect == null)
                    {
                        Workspace.AddProp(new PutHandleIcon()
                        {
                            position = Vector3.UnitY,
                            color = Color.White,
                            bgColor = Color.DarkRed,
                            icon = "懒", //ForkAwesome.Ambulance, 
                            name = $"XXX1TTT",
                        });

                        Workspace.AddProp(new PutHandleIcon()
                        {
                            position = -Vector3.UnitY,
                            color = Color.White,
                            bgColor = Color.Transparent,
                            icon = ForkAwesome.Ambulance,
                            name = $"handle-fa",
                        });


                        Workspace.AddProp(new PutHandleIcon()
                        {
                            position = -Vector3.UnitX,
                            color = Color.White,
                            bgColor = Color.Transparent,
                            icon = "懒",
                            name = $"handle3",
                        });

                        Workspace.AddProp(new PutHandleIcon()
                        {
                            position = Vector3.UnitX,
                            color = Color.Black,
                            bgColor = Color.White,
                            icon = "\ud83c\udfe0",
                            name = $"handle2",
                            size = 1.5f,
                        });

                        Workspace.AddProp(new PutTextAlongLine()
                        {
                            start = Vector3.Zero,
                            directionProp = "handle2",
                            color = Color.White,
                            text = $"懒书科技🚂123",
                            verticalOffset = -0.5f,
                            name = "handle_line"
                        });

                        hndSelect = new SelectObject()
                        {
                            terminal = pb.Panel.Terminal,
                            feedback = (tuples, _) =>
                            {
                                model3dSelectInfo =
                                    tuples.Length == 0 ? "Not selected yet." : $"{tuples[0].name} selected.";
                            },
                        };
                        hndSelect.Start();
                        hndSelect.SetSelectionMode(SelectObject.SelectionMode.Click);
                        hndSelect.SetObjectSelectable("handle*");
                    }


                    if (pb.Button("Remove Handle"))
                    {
                        WorkspaceProp.RemoveNamePattern("handle*");
                        hndSelect?.End();
                        hndSelect = null;
                    }

                    pb.Separator();

                    if (pb.Button("Measure distance"))
                    {
                        var firstTime = true;
                        new FollowMouse()
                        {
                            method = FollowMouse.FollowingMethod.LineOnGrid,
                            finished = () =>
                            {
                                Console.WriteLine("Dragged");
                                Workspace.Prop(new TransformObject() { name = "handle_ed", coord = TransformObject.Coord.Relative });
                            },
                            terminated = () => { Console.WriteLine("Terminated"); },
                            realtime = true,
                            feedback = (feedback, _) =>
                            {
                                if (firstTime)
                                {
                                    Workspace.AddProp(new PutHandleIcon()
                                    {
                                        position = feedback.mouse_start_XYZ,
                                        color = Color.White,
                                        bgColor = Color.DarkCyan,
                                        icon = ForkAwesome.Taxi,
                                        name = $"handle_st",
                                    });
                                    Workspace.AddProp(new PutHandleIcon()
                                    {
                                        position = feedback.mouse_end_XYZ,
                                        color = Color.White,
                                        bgColor = Color.DarkGoldenrod,
                                        icon = ForkAwesome.Trophy,
                                        name = $"handle_ed",
                                    });
                                    Workspace.Prop(new SetObjectMoonTo { earth = "me::mouse", name = "handle_ed" });
                                }

                                Workspace.AddProp(new PutTextAlongLine()
                                {
                                    start = feedback.mouse_start_XYZ,
                                    directionProp = "handle_ed",
                                    color = Color.White,
                                    text = $"dist=`{(feedback.mouse_end_XYZ - feedback.mouse_start_XYZ).Length()}'",
                                    verticalOffset = -0.5f,
                                });

                                Console.WriteLine($"mouse from:{feedback.mouse_start_XYZ} -> {feedback.mouse_end_XYZ}");
                            }
                        }.Start();
                    }
                    pb.CollapsingHeaderEnd();
                }


                // Viewport Manipulation
                {
                    pb.CollapsingHeaderStart("Viewport Manipulation");

                    if (pb.Button("Go full-screen"))
                    {
                        new SetFullScreen().IssueToDefault();
                    }
                    if (pb.Button("Go windowed"))
                    {
                        new SetFullScreen() { fullscreen = false }.IssueToDefault();
                    }

                    pb.DragFloat("Camera LookAt X", ref cameraLookAtX, 0.01f, -10, 10f);
                    pb.DragFloat("Camera LookAt Y", ref cameraLookAtY, 0.01f, -10, 10f);
                    // pb.DragFloat("Camera LookAt Z", ref cameraLookAtZ, 0.01f, -10, 10f);

                    pb.DragFloat("Camera Altitude", ref cameraAltitude, 0.001f, -(float)Math.PI / 2,
                        (float)Math.PI / 2);
                    pb.DragFloat("Camera Azimuth", ref cameraAzimuth, 0.001f, -(float)Math.PI / 2,
                        (float)Math.PI / 2);
                    pb.DragFloat("Camera Distance", ref cameraDistance, 0.01f, 0.1f, 100f);

                    pb.DragFloat("Camera FOV", ref cameraFov, 0.01f, 45f, 150f);

                    if (pb.Toggle("Toggle Projection Orthogonal", ref camModeOrth))
                    {
                        if (camModeOrth)
                        {
                            new SetCamera() { projectionMode = SetCamera.ProjectionMode.Orthographic }.Issue();
                        }
                        else
                        {
                            new SetCamera() { projectionMode = SetCamera.ProjectionMode.Perspective }.Issue();
                        }
                    }

                    if (pb.Button("Set camera"))
                    {
                        new SetCamera()
                        {
                            lookAt = new Vector3(cameraLookAtX, cameraLookAtY, 0f),
                            altitude = cameraAltitude,
                            azimuth = cameraAzimuth,
                            distance = cameraDistance,
                            fov = cameraFov,
                        }.Issue();
                    }
                    if (pb.Button("Set camera with restriction"))
                    {
                        new SetCamera()
                        {
                            azimuth_range = new Vector2((float)(-Math.PI / 4 * 3), (float)(-Math.PI / 4)),
                            altitude_range = new Vector2((float)(Math.PI / 4), (float)(Math.PI / 2)),
                            xyz_rangeD = new Vector3(1, 1, 1),
                        }.Issue();
                    }

                    // 1. SetAppearance Demo
                    pb.SeparatorText("Appearance Settings");
                    var appearanceChanged = false;
                    appearanceChanged |= pb.CheckBox("Use EyeDomeLighting", ref useEDL);
                    appearanceChanged |= pb.CheckBox("Use SSAO", ref useSSAO);
                    appearanceChanged |= pb.CheckBox("Use Ground", ref useGround);
                    appearanceChanged |= pb.CheckBox("Use Border", ref useBorder);
                    appearanceChanged |= pb.CheckBox("Use Bloom", ref useBloom);
                    appearanceChanged |= pb.CheckBox("Draw Grid", ref drawGrid);
                    appearanceChanged |= pb.CheckBox("Draw Guizmo", ref drawGuizmo);
                    appearanceChanged |= pb.CheckBox("Bring to Front on Hovering", ref btfOnHovering);
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
                            bring2front_onhovering = btfOnHovering,
                            sun_altitude = sun
                        }.Issue();
                    }

                    // 2. Workspace Position Demo
                    pb.SeparatorText("Position Query");
                    if (pb.Button("Get World Position"))
                    {
                        new GetPosition()
                        {
                            feedback = (pos, _) =>
                            {
                                if (pos.snapping_object != null)
                                {
                                    UITools.Alert($"Clicked on object: {pos.snapping_object}\n" +
                                                  $"Mouse Position:{pos.mouse_pos}" +
                                                  $"Object Position: ({pos.object_pos.X:F2}, {pos.object_pos.Y:F2}, {pos.object_pos.Z:F2})\n" +
                                                  $"Sub ID: {pos.sub_id}");
                                }
                                else
                                {
                                    UITools.Alert($"Clicked on empty space\n" +
                                                  $"Mouse position: ({pos.mouse_pos.X:F2}, {pos.mouse_pos.Y:F2})");
                                }
                            },
                            snaps = ["Stork", "demo_bezier", "demo_line"]
                        }.StartOnTerminal(pb.Panel.Terminal);
                    }

                    // 3. Query Viewport State Demo
                    pb.SeparatorText("Viewport State");
                    if (pb.Button("Query Viewport State"))
                    {
                        new QueryViewportState()
                        {
                            callback = state =>
                            {
                                UITools.Alert($"Camera Position: ({state.CameraPosition.X:F2}, {state.CameraPosition.Y:F2}, {state.CameraPosition.Z:F2})\n" +
                                            $"Look At: ({state.LookAt.X:F2}, {state.LookAt.Y:F2}, {state.LookAt.Z:F2})\n" +
                                            $"Up Vector: ({state.Up.X:F2}, {state.Up.Y:F2}, {state.Up.Z:F2})");
                            }
                        }.IssueToTerminal(pb.Panel.Terminal);
                    }

                    // 4. Capture Viewport Demo
                    pb.SeparatorText("Viewport Capture");
                    if (pb.Button("Capture Viewport"))
                    {
                        new CaptureRenderedViewport()
                        {
                            callback = img =>
                            {
                                GenJpg(img.bytes, img.width, img.height);

                                UITools.Alert("Viewport captured and saved as 'capture.jpg'");
                            }
                        }.Issue();
                    }

                    pb.CollapsingHeaderEnd();
                }

                // Bezier Curves and Lines Demo
                {
                    pb.CollapsingHeaderStart("Lines and Bezier Curves");

                    if (pb.CheckBox("Show Lines", ref showLines))
                    {
                        if (showLines)
                        {
                            stPnt = Workspace.AddProp(new PutPointCloud()
                            {
                                colors = [0xffff0000],
                                name = "l_st",
                                newPosition = lineStart,
                                xyzSzs = [new Vector4(0, 0, 0, 5)]
                            });
                            edPnt = Workspace.AddProp(new PutPointCloud()
                            {
                                colors = [0xff0000ff],
                                name = "l_ed",
                                newPosition = lineEnd,
                                xyzSzs = [new Vector4(0, 0, 0, 5)]
                            });
                            ctrlPnt1 = Workspace.AddProp(new PutPointCloud()
                            {
                                colors = [0xffffffff],
                                name = "l_ctrlPnt1",
                                newPosition = controlPoint1,
                                xyzSzs = [new Vector4(0, 0, 0, 5)]
                            });
                            ctrlPnt2 = Workspace.AddProp(new PutPointCloud()
                            {
                                colors = [0xffffffff],
                                name = "l_ctrlPnt2",
                                newPosition = controlPoint2,
                                xyzSzs = [new Vector4(0, 0, 0, 5)]
                            });
                        }
                        else
                        {
                            stPnt?.Remove();
                            edPnt?.Remove();
                            ctrlPnt1?.Remove();
                            ctrlPnt2?.Remove();
                            WorkspaceProp.RemoveNamePattern("demo_*");
                        }
                    }

                    void drawLine()
                    {
                        Workspace.Prop(new PutStraightLine
                        {
                            name = "demo_line",
                            start = lineStart,
                            end = lineEnd,
                            width = (int)lineWidth,
                            arrowType = showArrow ? Painter.ArrowType.End : Painter.ArrowType.None,
                            dashDensity = (int)dashDensity,
                            color = lineColor
                        });
                        Workspace.Prop(new PutBezierCurve
                        {
                            name = "demo_bezier",
                            start = lineStart,
                            end = lineEnd,
                            controlPnts = new Vector3[] { controlPoint1, controlPoint2 },
                            width = (int)lineWidth,
                            arrowType = showArrow ? Painter.ArrowType.End : Painter.ArrowType.None,
                            dashDensity = (int)dashDensity,
                            color = curveColor
                        });

                        Workspace.Prop(new PutVector()
                        {
                            name = "demo_vec",
                            start = lineStart,
                            propEnd = "me::mouse",
                            width = (int)lineWidth,
                            arrowType = showArrow ? Painter.ArrowType.End : Painter.ArrowType.None,
                            dashDensity = (int)dashDensity,
                            color = lineColor,
                            pixelLength = 30
                        });
                    }

                    if (showLines)
                    {
                        if (pntSelect == null)
                        {
                            pntSelect = new SelectObject()
                            {
                                terminal = pb.Panel.Terminal,
                                feedback = (tuples, _) =>
                                {
                                    model3dSelectInfo =
                                        tuples.Length == 0 ? "Not selected yet." : $"{tuples[0].name}.glb selected.";

                                    new GuizmoAction()
                                    {
                                        type = GuizmoAction.GuizmoType.MoveXYZ,
                                        finished = () =>
                                        {
                                            Console.WriteLine("finished moving...");
                                            pntSelect?.SetSelection([]);
                                        },
                                        terminated = () =>
                                        {
                                            Console.WriteLine("Forget it...");
                                            pntSelect?.SetSelection([]);
                                        },
                                        realtimeResult = true,
                                        feedback = (valueTuples, _) =>
                                        {
                                            if (tuples[0].name == "l_st")
                                                lineStart = valueTuples[0].pos;
                                            if (tuples[0].name == "l_ed")
                                                lineEnd = valueTuples[0].pos;
                                            if (tuples[0].name == "l_ctrlPnt1")
                                                controlPoint1 = valueTuples[0].pos;
                                            if (tuples[0].name == "l_ctrlPnt2")
                                                controlPoint2 = valueTuples[0].pos;
                                            drawLine();
                                        }
                                    }.Start();
                                },
                            };
                            pntSelect.Start();
                            pntSelect.SetSelectionMode(SelectObject.SelectionMode.Click);
                            pntSelect.SetObjectSelectable("l_*");
                        }

                        // Line controls
                        pb.SeparatorText("Line Controls");


                        // Line properties
                        pb.DragFloat("Line Width", ref lineWidth, 1, 1, 10);
                        pb.CheckBox("Show Arrow", ref showArrow);
                        pb.DragFloat("Dash Density", ref dashDensity, 1, 0, 10);

                        // Line color selector
                        pb.ColorEdit("Line Color", ref lineColor);
                        drawLine();
                    }
                    else
                    {
                        pntSelect?.End();
                        pntSelect = null;
                    }

                    pb.CollapsingHeaderEnd();
                }

                // Custom Background Shader Demo
                {
                    pb.CollapsingHeaderStart("OTHER Functions");
                    pb.SeparatorText("Use Custom Background HDRI");
                    if (pb.Button("Select Equirect Image"))
                        if (UITools.FileBrowser("Select equirect image", out var fn))
                        {
                            Bitmap bmp=new Bitmap(fn);
                            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
                            IntPtr ptr = bmpData.Scan0;
                            int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
                            byte[] bgrValues = new byte[bytes];
                            Marshal.Copy(ptr, bgrValues, 0, bytes);
                            bmp.UnlockBits(bmpData);

                            byte[] rgb = new byte[bytes];
                            for (int i = 0; i < bytes; i += 4)
                            {
                                // Keep red channel, zero out others
                                rgb[i] = bgrValues[i + 2]; // Red
                                rgb[i + 1] = bgrValues[i + 1]; // Green 
                                rgb[i + 2] = bgrValues[i + 0]; // Blue
                                rgb[i + 3] = bgrValues[i + 3]; // Alpha
                            }

                            Workspace.SetCustomBackgroundEnvmap(bmp.Width, bmp.Height, rgb);
                        }

                    pb.SeparatorText("Custom Background Shader");
                    if (pb.Button("Apply Checkerboard Background"))
                    {
                        string checkerboardShader = @"
// Checkerboard pattern with 1m intervals
void mainImage(out vec4 fragColor, in vec2 fragCoord) {
    // Convert screen coordinates to world coordinates using inverse matrices
    vec2 uv = fragCoord / iResolution.xy;
    uv = uv * 2.0 - 1.0; // Convert to NDC (-1 to 1)
    
    // Create a ray from camera
    vec4 ray_clip = vec4(uv, -1.0, 1.0);
    vec4 ray_eye = iInvPM * ray_clip;
    ray_eye = vec4(ray_eye.xy, -1.0, 0.0);
    vec4 ray_world = iInvVM * ray_eye;
    vec3 ray_dir = normalize(ray_world.xyz);
    
    // Ground plane intersection (z=0) - adjusted for your coordinate system
    float t = -iCameraPos.z / ray_dir.z;
    
    // Only draw checkerboard on ground plane
    if (t > 0.0 && ray_dir.z < 0.0) {
        // Compute world position of intersection
        vec3 pos = iCameraPos + t * ray_dir;
        
        // Create 1-meter checkerboard pattern
        float cellSize = 1.0; // 1 meter grid
        float x = floor(pos.x / cellSize);
        float y = floor(pos.y / cellSize);
        bool isEven = mod(x + y, 2.0) < 1.0;
        
        // Checkerboard colors
        vec3 color1 = vec3(0.9, 0.9, 0.9); // Light gray
        vec3 color2 = vec3(0.2, 0.2, 0.2); // Dark gray
        
        // Add grid lines
        float lineWidth = 0.02;
        float xFrac = abs(mod(pos.x, cellSize) / cellSize - 0.5) * 2.0;
        float yFrac = abs(mod(pos.y, cellSize) / cellSize - 0.5) * 2.0;
        bool isLine = xFrac > (1.0 - lineWidth) || yFrac > (1.0 - lineWidth);
        
        vec3 finalColor = isLine ? vec3(0.0, 0.7, 1.0) : (isEven ? color1 : color2);
        
        // Add distance fog
        float fogAmount = 1.0 - exp(-t * 0.03);
        vec3 fogColor = vec3(0.5, 0.6, 0.7);
        finalColor = mix(finalColor, fogColor, fogAmount);
        
        float alpha = 1.0 - smoothstep(-0.3, 0.0, ray_dir.z);
        fragColor = vec4(finalColor, alpha);
    } else {
        fragColor = vec4(0.0,0.0,0.0,0.0);
    }
}";
                        Workspace.SetCustomBackgroundShader(checkerboardShader);
                        UITools.Alert("Applied checkerboard background shader with 1m intervals");
                    }

                    if (pb.Button("Disable Custom Background"))
                    {
                        Workspace.DisableCustomBackgroundShader();
                        UITools.Alert("Custom background shader disabled");
                    }

                    if (pb.Button("Open SubViewport"))
                        aux_vp ??= GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("TEST aux Viewport"));

                    if (aux_vp != null)
                    {
                        if (pb.Button("Close SubViewport"))
                        {
                            aux_vp.Exit();
                            aux_vp = null;
                        }

                        if (pb.Button("Screenshot"))
                        {
                            new CaptureRenderedViewport()
                            {
                                callback = img =>
                                {
                                    GenJpg(img.bytes, img.width, img.height);

                                    UITools.Alert("Aux-Viewport captured and saved as 'capture.jpg'");
                                }
                            }.IssueToTerminal(aux_vp);
                        }

                        if (pb.Button("Hide"))
                            aux_vp.UseOffscreenRender(true);
                        pb.SameLine(10);
                        if (pb.Button("Show"))
                            aux_vp.UseOffscreenRender(false);
                        if (pb.Button("Follow"))
                        {
                            new SetCamera() { anchor_type = SetCamera.AnchorType.CopyCamera }.IssueToAllTerminals();
                            Workspace.Prop(new SetObjectMoonTo()
                                { name = "me::camera(TEST aux Viewport)", earth = "me::camera(main)" });
                        }
                    }

                    var (np, b) = pb.TextInput("name pattern", "", "name pattern");

                    if (b || pb.RadioButtons("Select Prop Display Mode:", ["All but specified", "None but specified"],
                            ref prop_display_type))
                    {
                        new SetWorkspacePropDisplayMode()
                        {
                            mode = prop_display_type == 0
                                ? SetWorkspacePropDisplayMode.PropDisplayMode.AllButSpecified
                                : SetWorkspacePropDisplayMode.PropDisplayMode.NoneButSpecified,
                            namePattern = np
                        }.IssueToDefault();
                    }

                    if (aux_vp != null && pb.Button("also apply to subvp"))
                    {
                        new SetWorkspacePropDisplayMode()
                        {
                            mode = prop_display_type == 0
                                ? SetWorkspacePropDisplayMode.PropDisplayMode.AllButSpecified
                                : SetWorkspacePropDisplayMode.PropDisplayMode.NoneButSpecified,
                            namePattern = np
                        }.IssueToTerminal(aux_vp);
                    }

                    pb.CollapsingHeaderEnd();
                }

                {
                    pb.CollapsingHeaderStart("Regions Demo");
                    static void PaintRegions()
                    {
                        var p = Painter.GetPainter("regions_demo");
                        // 10m radius, 5 turns spiral, point-based region voxels
                        float radius = 10.0f;
                        int turns = 5;
                        int samples = 1000;
                        var center = new Vector3(0, 0, 0);
                        for (int i = 0; i < samples; i++)
                        {
                            float t = (float)i / (samples - 1);
                            float ang = t * turns * 2.0f * (float)Math.PI;
                            float r = t * radius;
                            // spiral in XY, with slight Z ramp
                            var pos = new Vector3(
                                center.X + r * (float)Math.Cos(ang),
                                center.Y + r * (float)Math.Sin(ang),
                                center.Z + (t - 0.5f) * 2.0f
                            );
                            // p.DrawDot(Color.FromArgb(255, 255,0,0), pos);
                            p.DrawRegion3D(Color.FromArgb(150, 255, 128, 0), pos);
                        }
                    }
                    if (pb.Button("Draw Regions")) PaintRegions();
                    if (pb.Button("Clear Regions")) Painter.GetPainter("regions_demo").Clear();
                    pb.CollapsingHeaderEnd();
                }

                pb.Panel.Repaint();
            };
        }

        private static Viewport aux_vp;
    }
}
