using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;

namespace Annotator;

internal static class Program
{
    static unsafe void Main(string[] args)
    {
        CopyFileFromEmbeddedResources("libVRender.dll");

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

        var sphere = new RenderOptions("Sphere", 0, 117, 155, () => CreateSphereMesh(32));
        var cube = new RenderOptions("Cube", 155, 117, 0, CreateCubeMesh);

        var showDissectPlane = false;
        var sphereDissected = false;
        var cubeDissected = false;
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

        void BuildMesh(PanelBuilder pb, RenderOptions options)
        {
            void UpdateMesh(uint color)
            {
                Workspace.Prop(new DefineMesh()
                {
                    clsname = $"{options.Name}-cls",
                    positions = options.CreateMesh(),
                    color = color,
                    smooth = false,
                });

                Workspace.Prop(new PutModelObject()
                {
                    clsName = $"{options.Name}-cls",
                    name = options.Name,
                    newPosition = new Vector3(0, 0, 0)
                });
            }

            if (!options.Shown && pb.Button($"Render {options.Name}"))
            {
                UpdateMesh(ConcatHexABGR(options.ColorA, options.ColorB, options.ColorG, options.ColorR));
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

                if (BuildPalette(pb, options.Name, ref options.ColorA, ref options.ColorB, ref options.ColorG, ref options.ColorR))
                    UpdateMesh(ConcatHexABGR(options.ColorA, options.ColorB, options.ColorG, options.ColorR));
            }
        }

        var init = false;

        GUI.PromptPanel(pb =>
        {
            if (!init)
            {
                new SetAppearance() { bring2front_onhovering = false }.Issue();
                init = true;
            }
            
            pb.Panel.ShowTitle("Manipulation");

            pb.SeparatorText("Sphere");
            BuildMesh(pb, sphere);

            pb.SeparatorText("Cube");
            BuildMesh(pb, cube);

            pb.SeparatorText("Dissect");
            var issue = pb.CheckBox("Show Plane", ref showDissectPlane);
            issue |= pb.CheckBox("Dissect Sphere", ref sphereDissected);
            issue |= pb.CheckBox("Dissect Cube", ref cubeDissected);
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
                        newPosition = new Vector3(0, 0, 0)
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

                new SetAppearance()
                {
                    useCrossSection = sphereDissected || cubeDissected,
                    crossSectionPlanePos = planePos,
                    clippingDirection = Vector3.Transform(-Vector3.UnitZ, planeDir)
                }.Issue();
                new SetPropApplyCrossSection() { namePattern = "Sphere", apply = sphereDissected }.Issue();
                new SetPropApplyCrossSection() { namePattern = "Cube", apply = cubeDissected }.Issue();
                new SetPropApplyCrossSection() { namePattern = "Plane", apply = false }.Issue();
            }

            pb.Panel.Repaint();
        });
    }

    internal class RenderOptions
    {
        public RenderOptions(string name, float r, float g, float b, Func<float[]> createMesh)
        {
            Name = name;
            CreateMesh = createMesh;
            ColorR = r;
            ColorG = g;
            ColorB = b;
        }

        public string Name;
        public Vector3 Pos = Vector3.Zero;
        public Func<float[]> CreateMesh;
        public bool Shown = false;
        public float ColorR;
        public float ColorG;
        public float ColorB;
        public float ColorA = 255f;
    }
     
    private const string ResourceNamespace = "Annotator.Resources";

    private static void CopyFileFromEmbeddedResources(string fileName, string relativePath = "")
    {
#if !CGUIDebug
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
#endif
    }
}