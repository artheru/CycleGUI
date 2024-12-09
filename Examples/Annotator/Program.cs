using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using Annotator.RenderTypes;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;
using Python.Runtime;

namespace Annotator;

internal static class Program
{
    static unsafe void Main(string[] args)
    {
        string pythonDllPath = @"C:\Python311\python311.dll"; // Update with your Python version

        // CopyFileFromEmbeddedResources("libVRender.dll");

        // Set app window title.
        LocalTerminal.SetTitle("Annotator");

        // Set an icon for the app.
        // First read the .ico file from assembly, and then extract it as byte array.
        // Note: the .ico file must be 32x32.
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .First(p => p.Contains(".ico")));
        var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
        LocalTerminal.SetIcon(icoBytes, "Annotator");

        // Add an "Exit" button when right-click the tray icon.
        // Note: you can add multiple MenuItems like this.
        LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);

        // Actually start the app window.
        LocalTerminal.Start();

        PythonInterface.Initialize(pythonDllPath);
        PythonEngine.BeginAllowThreads();
        string scriptDirectory = AppDomain.CurrentDomain.BaseDirectory;
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(scriptDirectory);
        }

        float[] ProcessPythonInstance(dynamic instance)
        {
            var toRender = new List<float>();
            var vertices = new List<Tuple<float, float, float>>();
            var faces = new List<Tuple<int, int, int>>();

            dynamic pyVertices = instance.vertices;
            dynamic shape = pyVertices.shape;
            for (var i = 0; i < shape[0].As<int>(); ++i)
            {
                // zxy => xyz
                vertices.Add(Tuple.Create(
                    pyVertices.GetItem(i).GetItem(2).As<float>(),
                    pyVertices.GetItem(i).GetItem(0).As<float>(),
                    pyVertices.GetItem(i).GetItem(1).As<float>()));
            }

            dynamic pyFaces = instance.faces;
            dynamic faceShape = pyFaces.shape;

            for (var i = 0; i < faceShape[0].As<int>(); ++i)
            {
                // zxy => xyz
                var b = (int)pyFaces.GetItem(i).GetItem(0);
                faces.Add(Tuple.Create(
                    (int)pyFaces.GetItem(i).GetItem(2),
                    (int)pyFaces.GetItem(i).GetItem(0),
                    (int)pyFaces.GetItem(i).GetItem(1)));
            }

            void AddVertex(int index)
            {
                toRender.Add(vertices[index].Item1);
                toRender.Add(vertices[index].Item2);
                toRender.Add(vertices[index].Item3);
            }

            foreach (var face in faces)
            {
                AddVertex(face.Item1);
                AddVertex(face.Item2);
                AddVertex(face.Item3);
            }

            return toRender.ToArray();
        }

        float[] DemoObjectFromPython()
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("geometry_template");
                dynamic instance = myModule.DemoObject();

                return ProcessPythonInstance(instance);
            }
        }

        float[] CylinderFromPython(float height, float topRadius, float bottomRadius)
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("geometry_template");
                dynamic instance = myModule.Cylinder(height, topRadius, bottomRadius);

                return ProcessPythonInstance(instance);
            }
        }

        var showDissectPlane = false;
        var planePos = Vector3.Zero;
        var planeDir = Quaternion.Identity;

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

        float[] CreatePlaneMesh()
        {
            return new List<float>()
                {
                    -3f, -3f, 0f, // Bottom-left
                    3f, -3f, 0f, // Bottom-right 
                    3f, 3f, 0f, // Top-right
                    -3f, -3f, 0f, // Bottom-left
                    3f, 3f, 0f, // Top-right
                    -3f, 3f, 0f // Top-left
                }.ToArray();
        }

        float[] CreateSphereMesh(int resolution)
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

            return vertices.ToArray();
        }

        float[] CreateCubeMesh()
        {
            // Define the vertices of the cube
            float[] vertices = {
                // Front face
                -1.0f, -1.0f,  1.0f,  // Bottom-left
                1.0f, -1.0f,  1.0f,  // Bottom-right
                1.0f,  1.0f,  1.0f,  // Top-right
                -1.0f,  1.0f,  1.0f,  // Top-left

                // Back face
                -1.0f, -1.0f, -1.0f,  // Bottom-left
                1.0f, -1.0f, -1.0f,  // Bottom-right
                1.0f,  1.0f, -1.0f,  // Top-right
                -1.0f,  1.0f, -1.0f,  // Top-left
            };

            // Define the indices for the triangles
            int[] indices = {
                // Front face
                0, 1, 2,  0, 2, 3,
                // Back face
                4, 6, 5,  4, 7, 6,
                // Left face
                4, 5, 1,  4, 1, 0,
                // Right face
                3, 2, 6,  3, 6, 7,
                // Top face
                1, 5, 6,  1, 6, 2,
                // Bottom face
                4, 0, 3,  4, 3, 7
            };

            // Create a list to hold the cube's vertices as triangles
            List<float> cubeMesh = new List<float>();

            // Add the vertices to the list using the indices
            foreach (int index in indices)
            {
                cubeMesh.Add(vertices[index * 3]);
                cubeMesh.Add(vertices[index * 3 + 1]);
                cubeMesh.Add(vertices[index * 3 + 2]);
            }

            return cubeMesh.ToArray();
        }

        void DefineGuizmoAction(string name, bool realtime, Action<Vector3, Quaternion> onMoving = null)
        {
            void Dummy()
            {
                SelectObject defaultAction = null;
                defaultAction = new SelectObject()
                {
                    feedback = (tuples, _) =>
                    {
                        if (tuples.Length == 0)
                            Console.WriteLine($"no selection");
                        else
                        {
                            Console.WriteLine($"selected {tuples[0].name}");
                            var action = new GuizmoAction()
                            {
                                realtimeResult = realtime,
                                finished = () =>
                                {
                                    Console.WriteLine("OKOK...");
                                    defaultAction.SetSelection([]);
                                },
                                terminated = () =>
                                {
                                    Console.WriteLine("Forget it...");
                                    defaultAction.SetSelection([]);
                                }
                            };
                            action.feedback = (valueTuples, _) =>
                            {
                                onMoving?.Invoke(valueTuples[0].pos, valueTuples[0].rot);
                            };
                            action.Start();
                        }
                    },
                };
                defaultAction.Start();
                defaultAction.SetObjectSelectable(name);
            }


            // Dummy();
            Dummy();
        }

        void UpdateMesh(BasicRender options, bool firstPut = false)
        {
            Workspace.Prop(new DefineMesh()
            {
                clsname = $"{options.Name}-cls",
                positions = options.Mesh,
                color = ConcatHexABGR(options.ColorA, options.ColorB, options.ColorG, options.ColorR),
                smooth = false,
            });

            if (firstPut)
                Workspace.Prop(new PutModelObject()
                {
                    clsName = $"{options.Name}-cls",
                    name = options.Name,
                    newPosition = new Vector3(0, 0, 0)
                });
        }

        void BuildMesh(PanelBuilder pb, BasicRender options)
        {
            if (!options.Shown && pb.Button($"Render {options.Name}"))
            {
                UpdateMesh(options, true);
                options.Shown = true;

                DefineGuizmoAction(options.Name, false);
            }

            if (options.Shown)
            {
                if (pb.Button($"Stop Rendering {options.Name}"))
                {
                    WorkspaceProp.RemoveNamePattern(options.Name);
                    options.Shown = false;
                }

                BuildPalette(pb, options.Name, ref options.ColorA, ref options.ColorB, ref options.ColorG,
                    ref options.ColorR);

                options.ParameterChangeAction(pb);
            }
        }

        var geoTemplates = new List<BasicRender>();
        var objectCnt = 0;
        var colors = new List<Color>() { Color.DarkGoldenrod, Color.CornflowerBlue, Color.DarkRed, Color.DarkGray };

        // var cc = NextColor(objectCnt++);
        // BasicRender demoBottle = new ("Demo Bottle", cc.R, cc.G, cc.B);
        // demoBottle.Mesh = DemoObjectFromPython();

        Color NextColor(int index)
        {
            return colors[index % colors.Count];
        }

        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(40);

                foreach (var options in geoTemplates)
                {
                    if (options.NeedsUpdate())
                    {
                        if (options is CylinderRender cylinder)
                            cylinder.Mesh = CylinderFromPython(cylinder.Height, cylinder.TopRadius,
                                cylinder.BottomRadius);

                        UpdateMesh(options);
                        options.Update();
                        break;
                    }
                }
            }
        }) { Name = "MeshUpdater" }.Start();

        var init = false;
        
        GUI.PromptPanel(pb =>
        {
            if (!init)
            {
                new SetAppearance() { bring2front_onhovering = false }.Issue();
                init = true;
            }
            
            pb.Panel.ShowTitle("Manipulation");

            // pb.SeparatorText("Source Object");
            //
            // BuildMesh(pb, demoBottle);
            //
            // pb.SeparatorText("Geometric Templates");
            //
            // if (pb.Button("Add Cylinder"))
            // {
            //     var ccc = NextColor(objectCnt);
            //     var cylinder = new CylinderRender($"Cylinder{objectCnt++}", ccc.R, ccc.G, ccc.B, 0.5f, 0.1f, 0.1f);
            //     cylinder.Mesh = CylinderFromPython(cylinder.Height, cylinder.TopRadius, cylinder.BottomRadius);
            //     geoTemplates.Add(cylinder);
            // }
            //
            // foreach (var render in geoTemplates)
            // {
            //     pb.Separator();
            //     if (render is CylinderRender cylinder) BuildMesh(pb, cylinder);
            // }

            pb.SeparatorText("Dissect");
            var issue = pb.CheckBox("Show Plane", ref showDissectPlane);
            if (issue)
            {
                if (showDissectPlane)
                {
                    Workspace.Prop(new DefineMesh()
                    {
                        clsname = $"plane-cls",
                        positions = CreatePlaneMesh(),
                        color = 0xff555555,
                        smooth = false,
                    });

                    Workspace.Prop(new PutModelObject()
                    {
                        clsName = $"plane-cls",
                        name = $"Plane",
                        newPosition = planePos,
                        newQuaternion = planeDir
                    });

                    DefineGuizmoAction("Plane", true, (pos, rot) =>
                    {
                        planePos = pos;
                        planeDir = rot;
                        new SetAppearance()
                        {
                            useCrossSection = true,
                            crossSectionPlanePos = planePos,
                            clippingDirection = Vector3.Transform(-Vector3.UnitZ, planeDir)
                        }.Issue();
                    });
                }
                else WorkspaceProp.RemoveNamePattern($"Plane");
            }

            // issue |= pb.CheckBox("Dissect Demo Bottle", ref demoBottle.Dissected);
            // foreach (var render in geoTemplates)
            // {
            //     issue |= pb.CheckBox($"Dissect {render.Name}", ref render.Dissected);
            // }
            //
            if (issue)
            {
                // new SetAppearance()
                // {
                //     useCrossSection = demoBottle.Dissected || geoTemplates.Any(geo => geo.Dissected),
                //     crossSectionPlanePos = planePos,
                //     clippingDirection = Vector3.Transform(-Vector3.UnitZ, planeDir)
                // }.Issue();
                //
                // new SetPropApplyCrossSection() { namePattern = demoBottle.Name, apply = demoBottle.Dissected }.Issue();
                //
                // foreach (var render in geoTemplates)
                // {
                //     new SetPropApplyCrossSection() { namePattern = render.Name, apply = render.Dissected }.Issue();
                // }
                
                new SetPropApplyCrossSection() { namePattern = "Plane", apply = false }.Issue(); //??
            }

            pb.Panel.Repaint();
        });
    }

    private const string ResourceNamespace = "Annotator.Resources";

    private static void CopyFileFromEmbeddedResources(string fileName, string relativePath = "")
    {
        try
        {
            var resourceName = $"{ResourceNamespace}.{fileName}";
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new Exception($"Embedded resource {resourceName} not found");

            using var output = new FileStream(
                Path.Combine(Directory.GetCurrentDirectory(), relativePath, fileName), FileMode.Create);
            stream.CopyTo(output);
            Console.WriteLine($"Successfully copied {fileName}");
        }
        catch (Exception e)
        {
            // force rewrite.
            Console.WriteLine($"Failed to copy {fileName}");
            // Console.WriteLine(e.FormatEx());
        }
    }
}