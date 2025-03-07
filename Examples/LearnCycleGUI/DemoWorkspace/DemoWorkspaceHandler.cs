using CycleGUI;
using CycleGUI.API;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using LearnCycleGUI.Utilities;

namespace LearnCycleGUI.DemoWorkspace
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
            PutARGB streamer = null;
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
            var model3dX = 0f;
            var model3dY = 0f;
            var model3dSelectable = false;
            var model3dSelectInfo = "Not selected yet.";
            var selectionModes = new string[] { "Click", "Rectangle", "Paint" };
            var currentSelectionMode = 0; // Default to Click mode
            var paintRadius = 10f;
            SelectObject model3dSelectAction = null;

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
            var useSSAO = true;
            var useGround = true;
            var useBorder = true;
            var useBloom = true;
            var drawGrid = true;
            var drawGuizmo = true;
            var btfOnHovering = true;

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

            return pb =>
            {
                if (pb.Closing())
                {
                    Program.CurrentPanels.Remove(pb.Panel);
                    pb.Panel.Exit();
                    return;
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
                    pb.CollapsingHeaderStart("USB Camera");

                    if (!usbCameraOn && pb.Button("Start Capturing"))
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

                            streamer = Workspace.AddProp(new PutARGB()
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

                    var modelName = "Stork";
                    if (!model3dLoaded && pb.Button($"Load {modelName}.glb"))
                    {
                        if (File.Exists($"{modelName}.glb"))
                        {
                            Workspace.Prop(new LoadModel()
                            {
                                detail = new Workspace.ModelDetail(File.ReadAllBytes($"{modelName}.glb"))
                                {
                                    Center = new Vector3(model3dX, model3dY, 0),
                                    Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                                    Scale = 0.01f
                                },
                                name = "model_glb"
                            });

                            Workspace.Prop(putModelObject = new PutModelObject()
                            {
                                clsName = "model_glb",
                                name = modelName,
                                newPosition = new Vector3(model3dX, model3dY, 0)
                            });

                            model3dLoaded = true;
                        }
                        else UITools.Alert($"{modelName}.glb not exist!", t: pb.Panel.Terminal);
                    }

                    if (model3dLoaded && pb.Button($"Remove {modelName}.glb"))
                    {
                        putModelObject?.Remove();
                        model3dLoaded = false;
                    }

                    if (model3dLoaded)
                    {
                        if (model3dPosChanged)
                            Workspace.Prop(putModelObject = new PutModelObject()
                            {
                                clsName = "model_glb",
                                name = modelName,
                                newPosition = new Vector3(model3dX, model3dY, 0)
                            });

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
                                            tuples.Length == 0 ? "Not selected yet." : $"{modelName}.glb selected.";

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

                                model3dSelectAction.SetObjectSelectable(modelName);
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

                // Viewport Manipulation
                {
                    pb.CollapsingHeaderStart("Viewport Manipulation");

                    pb.DragFloat("Camera LookAt X", ref cameraLookAtX, 0.01f, -10, 10f);
                    pb.DragFloat("Camera LookAt Y", ref cameraLookAtY, 0.01f, -10, 10f);
                    // pb.DragFloat("Camera LookAt Z", ref cameraLookAtZ, 0.01f, -10, 10f);

                    pb.DragFloat("Camera Altitude", ref cameraAltitude, 0.001f, -(float)Math.PI / 2,
                        (float)Math.PI / 2);
                    pb.DragFloat("Camera Azimuth", ref cameraAzimuth, 0.001f, -(float)Math.PI / 2,
                        (float)Math.PI / 2);
                    pb.DragFloat("Camera Distance", ref cameraDistance, 0.01f, 0.1f, 100f);

                    pb.DragFloat("Camera FOV", ref cameraFov, 0.01f, 45f, 150f);

                    if (pb.Button("Set camera"))
                    {
                        new SetCamera()
                            {
                                lookAt = new Vector3(cameraLookAtX, cameraLookAtY, 0f),
                                altitude = cameraAltitude,
                                azimuth = cameraAzimuth,
                                distance = cameraDistance,
                                fov = cameraFov,
                            }
                            .Issue();
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
                                                  $"Position: ({pos.object_pos.X:F2}, {pos.object_pos.Y:F2}, {pos.object_pos.Z:F2})\n" +
                                                  $"Sub ID: {pos.sub_id}");
                                }
                                else
                                {
                                    UITools.Alert($"Clicked on empty space\n" +
                                                  $"Mouse position: ({pos.mouse_pos.X:F2}, {pos.mouse_pos.Y:F2})");
                                }
                            },
                            snaps = ["Stork"]
                        }.StartOnTermianl(pb.Panel.Terminal);
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

                                UITools.Alert("Viewport captured and saved as 'capture.jpg'");
                            }
                        }.Issue();
                    }

                    pb.CollapsingHeaderEnd();
                }


                pb.Panel.Repaint();
            };
        }
    }
}
