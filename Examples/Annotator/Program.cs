using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using Annotator.RenderTypes;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;
using Newtonsoft.Json;
using Python.Runtime;
using static CycleGUI.PanelBuilder;

namespace Annotator;

internal static class Program
{
    static unsafe void Main(string[] args)
    {
        if (!File.Exists("start_options.json"))
        {
            File.WriteAllText("start_options.json", JsonConvert.SerializeObject(new StartOptions()
            {
                DisplayOnWeb = false,
                PythonPath = @"C:\Path_to_Your_Python\python310.dll",
                Port = 8081,
            }, Formatting.Indented));
            Console.WriteLine("\"start_options.json\" does not exist!\n" +
                              "Already created one for you.\n" +
                              "Please replace \"Path_to_Your_Python\" with actual path and restart.\n" +
                              "Python version 3.7 to 3.10 supported.\n" +
                              "You can change DisplayOnWen to True, so that web version is enabled.\n" +
                              "By default, the service listens port 8081.\n" +
                              "Press any key to exit.");
            Console.ReadKey();
            return;
        }

        var startOptions = JsonConvert.DeserializeObject<StartOptions>(File.ReadAllText("start_options.json"));
        var pythonDllPath = startOptions.PythonPath;

        CopyFileFromEmbeddedResources("libVRender.dll");

        // First read the .ico file from assembly, and then extract it as byte array.
        // Note: the .ico file must be 32x32.
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .First(p => p.Contains(".ico")));
        var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);

        PythonInterface.Initialize(pythonDllPath);
        PythonEngine.BeginAllowThreads();
        string scriptDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string annotatorDirectory = "D:\\src\\CycleGUI\\Examples\\Annotator\\";//Path.GetFullPath(Path.Combine(exeDirectory, "..", "..", ".."));

        string pythonDirectory = Path.Combine(annotatorDirectory, "Globe");
        
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(pythonDirectory);
        }

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

        var objectViewport = GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("Target Object"));
        var templateViewport = GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("Template Rendering"));
        var init = false;

        #region Just for demo. Because multi-viewport feature has bug.

        var objectLookAt = new Vector2(100, 100);
        var templateLookAt = new Vector2(200, 200);
        new SetCamera() { lookAt = new Vector3(objectLookAt, 0) }.IssueToTerminal(objectViewport);
        new SetCamera() { lookAt = new Vector3(templateLookAt, 0) }.IssueToTerminal(templateViewport);

        #endregion

        void UpdateMesh(BasicRender options, bool firstPut, bool template)
        {
            Workspace.Prop(new DefineMesh()
            {
                clsname = $"{options.Name}-cls",
                positions = options.Mesh,
                color = ConcatHexABGR(options.ColorA, options.ColorB, options.ColorG, options.ColorR),
                smooth = false,
            });

            if (firstPut)
            {
                Workspace.Prop(new PutModelObject()
                {
                    clsName = $"{options.Name}-cls",
                    name = options.Name,
                    newPosition = options.Pos,
                    newQuaternion = options.Rot
                });

                Workspace.Prop(new PutModelObject()
                {
                    clsName = $"{options.Name}-cls",
                    name = $"{options.Name}-clone",
                    newPosition = options.Pos + new Vector3(template ? templateLookAt : objectLookAt, 0),
                    newQuaternion = options.Rot
                });
            }
        }

        void BuildMesh(PanelBuilder pb, BasicRender options, bool template) // template: just for demo
        {
            // selectAction.SetObjectSelectable(options.Name);

            if (!options.Shown && pb.Button($"Render {options.Name}"))
            {
                UpdateMesh(options, true, template);
                options.Shown = true;
                // DefineGuizmoAction(options.Name, false);
            }

            if (options.Shown)
            {
                if (pb.Button($"Stop Rendering {options.Name}"))
                {
                    WorkspaceProp.RemoveNamePattern(options.Name);
                    options.Shown = false;
                }

                // BuildPalette(pb, options.Name, ref options.ColorA, ref options.ColorB, ref options.ColorG,
                //     ref options.ColorR);

                options.ParameterChangeAction(pb);
            }
        }

        var templateObjects = new List<BasicRender>();
        var objectCnt = 0;
        var colors = new List<Color>() { Color.DarkGoldenrod, Color.CornflowerBlue, Color.DarkRed, Color.DarkGray };

        var cc = NextColor(objectCnt++);
        BasicRender demoGlobe = new ("Globe", cc.R, cc.G, cc.B);
        demoGlobe.Mesh = MeshGenerator.DemoObjectFromPython();

        Color NextColor(int index)
        {
            return colors[index == 0 ? 0 : 1];
            // return colors[index % colors.Count];
        }

        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(40);

                foreach (var options in templateObjects)
                {
                    if (options.NeedsUpdate())
                    {
                        if (options is CylinderRender cylinder)
                            cylinder.Mesh = MeshGenerator.CylinderFromPython(cylinder.Height, cylinder.TopRadius,
                                cylinder.BottomRadius);
                        // ADDED FOR CONCEPTUAL TEMPLATES
                        else if (options is MultilevelBodyRender multilevelBody)
                        {
                            multilevelBody.Mesh = MeshGenerator.MultilevelBodyFromPython(
                                multilevelBody.NumLevels,
                                new float[] { multilevelBody.Level1TopRadius, multilevelBody.Level1Height, multilevelBody.Level1BottomRadius },
                                new float[] { multilevelBody.Level2TopRadius, multilevelBody.Level2Height, multilevelBody.Level2BottomRadius },
                                new float[] { multilevelBody.Level3TopRadius, multilevelBody.Level3Height, multilevelBody.Level3BottomRadius },
                                new float[] { multilevelBody.Level4TopRadius, multilevelBody.Level4Height, multilevelBody.Level4BottomRadius }

                            );
                        }
                        else if (options is CylindricalLidRender cylindricalLid)
                        {
                            cylindricalLid.Mesh = MeshGenerator.CylindricalLidFromPython(
                                new float[] { cylindricalLid.OuterTopRadius, cylindricalLid.OuterHeight, cylindricalLid.OuterBottomRadius },
                                new float[] { cylindricalLid.InnerTopRadius, cylindricalLid.InnerHeight, cylindricalLid.InnerBottomRadius }
                            );
                        }

                        else if (options is StandardSphereRender sphere)
                        {
                            sphere.Mesh = MeshGenerator.StandardSphereFromPython(
                                sphere.Radius,
                                new Vector3(sphere.PosX, sphere.PosY, sphere.PosZ),
                                new Vector3(sphere.RotX, sphere.RotY, sphere.RotZ)
                            );
                            //UpdateMesh(sphere);
                            //sphere.Update();
                            //break;
                        }
                        else if (options is SemiRingBracketRender bracket)
                        {
                            bracket.Mesh = MeshGenerator.SemiRingBracketFromPython(
                                new float[] { bracket.Pivot_size_radius, bracket.Pivot_size_height },
                                new float[] { bracket.Pivot_continuity_val },
                                new float[] { bracket.Pivot_seperation_val },
                                new float[] { bracket.Endpoint_radius_val },
                                new float[] { bracket.Bracket_size0, bracket.Bracket_size1, bracket.Bracket_size2 },
                                new float[] { bracket.Bracket_offset_val },
                                new float[] { bracket.Bracket_rotation_val },
                                new float[] { bracket.Bracket_exist_angle_val },
                                new float[] { bracket.PosX, bracket.PosY, bracket.PosZ },
                                new float[] { bracket.RotX, bracket.RotY, bracket.RotZ },
                                new float[] { bracket.Has_top_endpoint_val },
                                new float[] { bracket.Has_bottom_endpoint_val }
                            );
                            //UpdateMesh(bracket);
                            //bracket.Update();
                            //break;
                        }
                        else if (options is CylindricalBaseRender baseObj)
                        {
                            baseObj.Mesh = MeshGenerator.CylindricalBaseFromPython(
                                new float[] { baseObj.Bottom_topRadius, baseObj.Bottom_bottomRadius, baseObj.Bottom_height },
                                new float[] { baseObj.Top_topRadius, baseObj.Top_bottomRadius, baseObj.Top_height },
                                new Vector3(baseObj.PosX, baseObj.PosY, baseObj.PosZ),
                                new Vector3(baseObj.RotX, baseObj.RotY, baseObj.RotZ)
                            );
                            //UpdateMesh(baseObj);
                            //baseObj.Update();
                            //break;
                        }

                        UpdateMesh(options, false, options.Name != "Globe");
                        options.Update();
                        break;
                    }
                }
            }
        })
        { Name = "MeshUpdater" }.Start();

        SelectObject defaultAction = null;

        var planeVerticalAxis = 0;
        var planeAxisDir = 0;
        var planePosition = 0f;

        void IssueCrossSection()
        {
            var planeDir =
                (planeVerticalAxis == 0 ? Vector3.UnitX : planeVerticalAxis == 1 ? Vector3.UnitY : Vector3.UnitZ) *
                (planeAxisDir == 1 ? 1 : -1);
            var planePos =
                (planeVerticalAxis == 0 ? Vector3.UnitX : planeVerticalAxis == 1 ? Vector3.UnitY : Vector3.UnitZ) *
                planePosition;

            var clippingPlanes = new List<(Vector3, Vector3)>();
            if (demoGlobe.Dissected || templateObjects.Any(geo => geo.Dissected))
            {
                clippingPlanes.Add((planePos, planeDir));
            }
            new SetAppearance() { clippingPlanes = clippingPlanes.ToArray() }.Issue();

            new SetPropApplyCrossSection() { namePattern = demoGlobe.Name, apply = demoGlobe.Dissected }.Issue();

            foreach (var render in templateObjects)
            {
                new SetPropApplyCrossSection() { namePattern = render.Name, apply = render.Dissected }.Issue();
            }

            new SetPropApplyCrossSection() { namePattern = "Plane", apply = false }.Issue();
        }

        void DefineSelectAction()
        {
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
                            realtimeResult = true,//realtime,
                            finished = () =>
                            {
                                Console.WriteLine("OKOK...");
                                defaultAction.SetSelection([]);
                                // IssueCrossSection();
                            },
                            terminated = () =>
                            {
                                Console.WriteLine("Forget it...");
                                defaultAction.SetSelection([]);
                                // IssueCrossSection();
                            }
                        };
                        action.feedback = (valueTuples, _) =>
                        {
                            var name = valueTuples[0].name;
                            if (name == demoGlobe.Name)
                            {
                                demoGlobe.Pos = valueTuples[0].pos;
                                demoGlobe.Rot = valueTuples[0].rot;
                            }
                            else
                            {
                                var render = templateObjects.First(geo => geo.Name == name);
                                render.Pos = valueTuples[0].pos;
                                render.Rot = valueTuples[0].rot;

                                Workspace.Prop(new PutModelObject()
                                {
                                    clsName = $"{render.Name}-cls",
                                    name = $"{render.Name}-clone",
                                    newPosition = render.Pos + new Vector3(templateLookAt, 0),
                                    newQuaternion = render.Rot
                                });
                            }
                        };
                        action.Start();
                    }
                },
            };
            defaultAction.Start();
        }

        PutStraightLine line = null;

        CycleGUIHandler DefineMainPanel()
        {
            return pb =>
            {
                if (!init)
                {
                    DefineSelectAction();

                    new SetAppearance() { bring2front_onhovering = false, useGround = false }
                        .Issue();
                    init = true;
                }

                // defaultAction.SetObjectSelectable(demoGlobe.Name);
                foreach (var geo in templateObjects)
                {
                    defaultAction.SetObjectSelectable(geo.Name);
                }

                pb.Panel.ShowTitle("Manipulation");

                pb.SeparatorText("Target Object");

                BuildMesh(pb, demoGlobe, false);

                //pb.SeparatorText("Geometric Templates");

                var issue = false;

                // ADED For Conceptual Templates
                pb.SeparatorText("Conceptual Templates");

                if (pb.Button("Add Standard Sphere"))
                {
                    var ccc = NextColor(objectCnt);
                    var sphere = new StandardSphereRender($"StdSphere{objectCnt++}", ccc.R, ccc.G, ccc.B);
                    sphere.Mesh = MeshGenerator.StandardSphereFromPython(
                        sphere.Radius,
                        new Vector3(sphere.PosX, sphere.PosY, sphere.PosZ),
                        new Vector3(sphere.RotX, sphere.RotY, sphere.RotZ)
                    );
                    templateObjects.Add(sphere);
                }

                // Add Semi Ring Bracket
                if (pb.Button("Add Semi Ring Bracket"))
                {
                    var ccc = NextColor(objectCnt);
                    var bracket = new SemiRingBracketRender($"SRBracket{objectCnt++}", ccc.R, ccc.G, ccc.B);
                    bracket.Mesh = MeshGenerator.SemiRingBracketFromPython(
                        new float[] { bracket.Pivot_size_radius, bracket.Pivot_size_height },
                        new float[] { bracket.Pivot_continuity_val },
                        new float[] { bracket.Pivot_seperation_val },
                        new float[] { bracket.Endpoint_radius_val },
                        new float[] { bracket.Bracket_size0, bracket.Bracket_size1, bracket.Bracket_size2 },
                        new float[] { bracket.Bracket_offset_val },
                        new float[] { bracket.Bracket_rotation_val },
                        new float[] { bracket.Bracket_exist_angle_val },
                        new float[] { bracket.PosX, bracket.PosY, bracket.PosZ },
                        new float[] { bracket.RotX, bracket.RotY, bracket.RotZ },
                        new float[] { bracket.Has_top_endpoint_val },
                        new float[] { bracket.Has_bottom_endpoint_val }
                    );
                    templateObjects.Add(bracket);
                }

                // Add Cylindrical Base
                if (pb.Button("Add Cylindrical Base"))
                {
                    var ccc = NextColor(objectCnt);
                    var baseObj = new CylindricalBaseRender($"CylBase{objectCnt++}", ccc.R, ccc.G, ccc.B);
                    baseObj.Mesh = MeshGenerator.CylindricalBaseFromPython(
                        new float[] { baseObj.Bottom_topRadius, baseObj.Bottom_bottomRadius, baseObj.Bottom_height },
                        new float[] { baseObj.Top_topRadius, baseObj.Top_bottomRadius, baseObj.Top_height },
                        new Vector3(baseObj.PosX, baseObj.PosY, baseObj.PosZ),
                        new Vector3(baseObj.RotX, baseObj.RotY, baseObj.RotZ)
                    );
                    templateObjects.Add(baseObj);
                }

                foreach (var render in templateObjects.OfType<StandardSphereRender>())
                {
                    pb.Separator();
                    BuildMesh(pb, render, true);
                }

                foreach (var render in templateObjects.OfType<SemiRingBracketRender>())
                {
                    pb.Separator();
                    BuildMesh(pb, render, true);
                }

                foreach (var render in templateObjects.OfType<CylindricalBaseRender>())
                {
                    pb.Separator();
                    BuildMesh(pb, render, true);
                }

                pb.SeparatorText("Dissect");

                pb.Label("Vertical to axis:");
                issue |= pb.RadioButtons("axisChoice", ["X", "Y", "Z"], ref planeVerticalAxis, true);
                pb.Label("Remain part direction:");
                issue |= pb.RadioButtons("axisDirection", ["Positive", "Negative"], ref planeAxisDir, true);
                issue |= pb.DragFloat("Position", ref planePosition, 0.001f, -10f, 10f);

                issue |= pb.CheckBox($"Dissect {demoGlobe.Name}", ref demoGlobe.Dissected);
                if (demoGlobe.Dissected)
                {
                    Workspace.Prop(line = new PutStraightLine()
                    {
                        name = "tl",
                        start = new Vector3(100, 0, 0),
                        end = Vector3.Zero,
                        width = 2,
                        arrowType = Painter.ArrowType.None,
                        color = Color.Red
                    });
                }
                else
                {
                    line?.Remove();
                    WorkspaceProp.RemoveNamePattern("tl");
                }

                foreach (var render in templateObjects)
                {
                    issue |= pb.CheckBox($"Dissect {render.Name}", ref render.Dissected);
                }

                if (issue) IssueCrossSection();

                pb.Panel.Repaint();
            };
        }

        if (startOptions.DisplayOnWeb)
        {
            Terminal.RegisterRemotePanel(pb =>
            {
                pb.Panel.ShowTitle("Annotator");
                pb.Label("Welcome to Annotator on web!");
                if (pb.Button("Start Labeling"))
                {
                    GUI.defaultTerminal = pb.Panel.Terminal;
                    _mainPanel = GUI.DeclarePanel(pb.Panel.Terminal);
                    _mainPanel.Define(DefineMainPanel());
                    pb.Panel.Exit();
                }
            });
            WebTerminal.Use(startOptions.Port, ico: icoBytes);
        }
        else
        {
            // Set an icon for the app.
            LocalTerminal.SetIcon(icoBytes, "Annotator");

            // Set app window title.
            LocalTerminal.SetTitle("Annotator");

            // Add an "Exit" button when right-click the tray icon.
            // Note: you can add multiple MenuItems like this.
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);

            // Actually start the app window.
            LocalTerminal.Start();

            _mainPanel = GUI.PromptPanel(DefineMainPanel());
        }
    }

    internal class StartOptions
    {
        public bool DisplayOnWeb;
        public string PythonPath;
        public int Port;
    }

    private static Panel _mainPanel;

    private const string ResourceNamespace = "Annotator.Resources";

    private static void CopyFileFromEmbeddedResources(string fileName, string relativePath = "")
    {
        return;
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