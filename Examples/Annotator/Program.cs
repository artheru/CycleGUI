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
using static Annotator.AnnotatorHelpers;
using System.Security.AccessControl;


namespace Annotator;

internal static class Program
{
    static unsafe void Main(string[] args)
    {

        string annotatorDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Objects"));

        var objectFolders = RetrieveObjects(annotatorDirectory);
        
        var dropdownMenu = objectFolders.Length > 0 ? objectFolders : new[] { "No objects available" };
        int dropdownIndex = 0; // Default to "Bottle" if it exists

        var objectsDropDown = new[] { "0", "1", "2" };
        int objectDropDownIndex = Array.IndexOf(objectsDropDown, "0");
        var selectedObject = dropdownMenu[0];
        var dataIndex = 0;
        var templateObjects = new List<GlobeConceptTemplates>();
        var objectCnt = 0;
        var colors = new List<Color>() { Color.DarkGoldenrod, Color.CornflowerBlue, Color.DarkRed, Color.DarkGray };

        var startOptions = LoadStartOptions();
        var pythonDllPath = startOptions.PythonPath;

        CopyFileFromEmbeddedResources("libVRender.dll");
        byte[] icoBytes = LoadIcon();

        PythonInterface.Initialize(pythonDllPath);
        PythonEngine.BeginAllowThreads();
        //string annotatorDirectory = AppDomain.CurrentDomain.BaseDirectory;


        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(annotatorDirectory);
        }

        dynamic conceptualAssembly = InitializePythonAndAssembly(selectedObject, dataIndex);
        List<(string templateName, Dictionary<string, float[]>)> templatesInfo = GetTemplatesInfo(conceptualAssembly);

        // Retrieves all the templates info for each object

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

        void UpdateMesh(BasicRender options, bool firstPut, bool template)
        {
            float zBias = 0.8f; // The desired Z-plane position
            Workspace.AddProp(new DefineMesh()
            {
                clsname = $"{options.Name}-cls",
                positions = options.Mesh,
                color = ConcatHexABGR(options.ColorA, options.ColorB, options.ColorG, options.ColorR),
                smooth = false,
            });

            if (firstPut)
            {
                Console.WriteLine($"---------------NAME in the Update: {options.Name}");

                var biasedPosition = new Vector3(options.Pos.X, options.Pos.Y, options.Pos.Z + zBias);

                Workspace.AddProp(new PutModelObject()
                {
                    clsName = $"{options.Name}-cls",
                    name = options.Name,
                    newPosition = biasedPosition,
                    newQuaternion = options.Rot
                });
            }

            SetViewportVisibility(objectViewport, $"{options.Name}", !template); // Show target object in objectViewport
            SetViewportVisibility(templateViewport, $"{options.Name}", template); // Show template object in templateViewport
        }

        void BuildMesh(PanelBuilder pb, BasicRender options, bool template) // template: just for demo
        {
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
                if(template)
                    options.ParameterChangeAction(pb);
            }
        }
        var cc = NextColor(objectCnt++);
        BasicRender demo = new (selectedObject, cc.R, cc.G, cc.B);
        demo.Mesh = DemoObjectFromPython(selectedObject, dataIndex);
        lock(templateObjects)
        {
            foreach (var (tName, paramDict) in templatesInfo)
            {
                var color = NextColor(objectCnt);
                var render = new GlobeConceptTemplates($"{tName}", paramDict, color.R, color.G, color.B);
                // Create initial mesh using the default parameters
                using (Py.GIL())
                {
                    dynamic pyDict = new PyDict();
                    foreach (var kv in paramDict)
                    {
                        var arr = kv.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
                        pyDict[kv.Key] = new PyList(arr);
                    }

                    conceptualAssembly.update_template_parameters(tName, pyDict);

                    // Retrieve the mesh for this template
                    render.Mesh = GetSingleComponentMesh(conceptualAssembly, tName);
                }
                templateObjects.Add(render);
            }
        }

        new Thread(() =>
        {
            string lastObj = selectedObject;
            int lastIndex = dataIndex;
            while (true)
            {
                Thread.Sleep(100);

                if (selectedObject != lastObj || dataIndex != lastIndex)
                {
                    lock (templateObjects)
                    {
                        foreach (var render in templateObjects)
                        {
                            // Remove meshes associated with the previous object
                            WorkspaceProp.RemoveNamePattern(render.Name);
                        }
                        templateObjects.Clear();
                    }
                    templatesInfo.Clear();
                    demo = new(selectedObject, cc.R, cc.G, cc.B);
                    demo.Mesh = DemoObjectFromPython(selectedObject, dataIndex);
                    
                    conceptualAssembly = InitializePythonAndAssembly(selectedObject, dataIndex);
                    templatesInfo = GetTemplatesInfo(conceptualAssembly);

                    lock(templateObjects)
                    {
                        foreach (var (tName, paramDict) in templatesInfo)
                        {
                            var color = NextColor(objectCnt);
                            var render = new GlobeConceptTemplates($"{tName}", paramDict, color.R, color.G, color.B);
                            // Create initial mesh using the default parameters
                            using (Py.GIL())
                            {
                                dynamic pyDict = new PyDict();
                                foreach (var kv in paramDict)
                                {
                                    var arr = kv.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
                                    pyDict[kv.Key] = new PyList(arr);
                                }

                                conceptualAssembly.update_template_parameters(tName, pyDict);

                                // Retrieve the mesh for this template
                                render.Mesh = GetSingleComponentMesh(conceptualAssembly, tName);
                            }
                            templateObjects.Add(render);
                        }
                    }
                }
                _mainPanel?.Repaint();
                lastObj = selectedObject;
                lastIndex = dataIndex;
            }
        }).Start();

        Color NextColor(int index)
        {
            return colors[index == 0 ? 0 : 1];
        }

        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(100);

                bool updated = false;
                foreach (var render in templateObjects)
                {
                    if (render.NeedsUpdate())
                    {
                        var templateName = render.TemplateName;
                        if (templateName.IndexOf('-') >= 0)
                        {
                            templateName = templateName.Substring(0, templateName.IndexOf("-"));
                        }

                        using (Py.GIL())
                        {
                            dynamic pyDict = new PyDict();
                            foreach (var kv in render.Parameters)
                            {
                                var arr = kv.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
                                pyDict[kv.Key] = new PyList(arr);
                            }

                            // Update the template in Python
                            conceptualAssembly.update_template_parameters(templateName, pyDict);
                        }

                        // Update the full mesh in C#
                        render.Mesh = GetSingleComponentMesh(conceptualAssembly, templateName);

                        UpdateMesh(render, false, true);
                        render.Update();
                        break;
                    }
                }
            }
        }).Start();

        SelectObject defaultAction = null;

        var planeVerticalAxis = 0;
        var planeAxisDir = 0;
        var planePosition = 0f;

        bool dissectX = false;
        bool dissectY = false;
        bool dissectZ = false;

        // The direction: 0 => Negative, 1 => Positive
        int xAxisDir = 0;
        int yAxisDir = 0;
        int zAxisDir = 0;

        // The position for each axis
        float xPlanePos = 0f;
        float yPlanePos = 0f;
        float zPlanePos = 0f;

        void IssueCrossSection()
        {
            var clippingPlanes = new List<(Vector3 pos, Vector3 normal)>();

            // We only add a plane to the list if that axis is toggled on
            if (dissectX)
            {
                var dirX = (xAxisDir == 1) ? +1 : -1;  // 1 => +1, 0 => -1
                var planeDir = Vector3.UnitX * dirX;
                var planePos = Vector3.UnitX * xPlanePos;
                clippingPlanes.Add((planePos, planeDir));
            }

            if (dissectY)
            {
                var dirY = (yAxisDir == 1) ? +1 : -1;
                var planeDir = Vector3.UnitY * dirY;
                var planePos = Vector3.UnitY * yPlanePos;
                clippingPlanes.Add((planePos, planeDir));
            }

            if (dissectZ)
            {
                var dirZ = (zAxisDir == 1) ? +1 : -1;
                var planeDir = Vector3.UnitZ * dirZ;
                var planePos = Vector3.UnitZ * zPlanePos;
                clippingPlanes.Add((planePos, planeDir));
            }

            bool anyDissection = demo.Dissected || templateObjects.Any(geo => geo.Dissected);

            new SetAppearance()
            {
                clippingPlanes = anyDissection ? clippingPlanes.ToArray() : Array.Empty<(Vector3, Vector3)>()
            }.Issue();

            // Apply cross-sections to each relevant object
            new SetPropApplyCrossSection() { namePattern = demo.Name, apply = demo.Dissected }.Issue();
            foreach (var render in templateObjects)
            {
                new SetPropApplyCrossSection() { namePattern = render.Name, apply = render.Dissected }.Issue();
            }

            // Possibly also ignore the plane object
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
                            if (name == demo.Name)
                            {
                                demo.Pos = valueTuples[0].pos;
                                demo.Rot = valueTuples[0].rot;
                            }
                            else
                            {
                                var render = templateObjects.First(geo => geo.Name == name);
                                render.Pos = valueTuples[0].pos;
                                render.Rot = valueTuples[0].rot;
                            }
                        };
                        action.Start();
                    }
                },
            };
            defaultAction.Start();
        }

        PutStraightLine line = null;
        BasicRender loadedObj = null;
        List<GlobeConceptTemplates> localCopy;
        //objectNames = LoadObjectNames(objectNames, ObjectListFile);
        CycleGUIHandler DefineMainPanel()
        {
            return pb =>
            {
                pb.CollapsingHeaderStart("Target Object");
                pb.Label("Select Object:");
                // Dropdown menu for object selection
                if (pb.DropdownBox("Object Names", dropdownMenu.ToArray(), ref dropdownIndex))
                {
                    string selectedFolder = dropdownMenu[dropdownIndex];
                    selectedObject = selectedFolder;
                    Console.WriteLine($"Selected object: {selectedFolder}");
                }

                pb.Label("Select Object data index:");
                if (pb.DropdownBox("# of objects", objectsDropDown, ref objectDropDownIndex))
                {
                    var selectIndex = int.Parse(objectsDropDown[objectDropDownIndex]);
                    dataIndex = selectIndex;
                }
                // Section for adding new objects
                //pb.SeparatorText("Target Object");

                BuildMesh(pb, demo, false);
                pb.CollapsingHeaderEnd();
                pb.Separator();

                if (!init)
                {
                    DefineSelectAction();

                    new SetAppearance() { bring2front_onhovering = false, useGround = false }
                        .Issue();
                    init = true;
                }

                // defaultAction.SetObjectSelectable(demo.Name);
                // foreach (var geo in templateObjects)
                // {
                //     defaultAction.SetObjectSelectable(geo.Name);
                // }

                pb.Panel.ShowTitle("Manipulation");

                var issue = false;

                //pb.SeparatorText("Conceptual Templates");
                lock(templateObjects)
                {
                    localCopy = templateObjects.ToList();
                }

                lock (templateObjects)
                {
                    pb.CollapsingHeaderStart("Conceptual Templates");
                    pb.Table("TemplateTable", ["Template Name", "Action"], localCopy.Count, (row, rowIndex) =>
                    {
                        var template = localCopy[rowIndex];
                        // Column 0
                        row.Label(template.Name);
                        // Column 1
                        //row.Label(template.Shown ? "Rendering" : "Not Rendering");
                        // Column 2
                        var buttonLabel = template.Shown ? "Stop" : "Render";
                        var actions = row.ButtonGroup([buttonLabel]);
                        if(actions == 0)
                        {
                            if (!template.Shown)
                            {

                                    UpdateMesh(template, true, true);
                                    template.Shown = true;

                                    OpenOrBringToFrontTemplatePanel(template, _templatePanels);
                            }
                            else
                            {

                                    WorkspaceProp.RemoveNamePattern(template.Name);
                                    template.Shown = false;

                                    CloseTemplatePanelIfOpen(template.Name, _templatePanels);
                            }
                        }
                        
                    });
                    pb.CollapsingHeaderEnd();
                }
                pb.Separator();

                pb.CollapsingHeaderStart("Select Objects to Dissect");
                {
                    {
                        issue |= pb.CheckBox($"Dissect {demo.Name}", ref demo.Dissected);
                        lock (templateObjects)
                        {
                            foreach (var render in templateObjects)
                            {
                                issue |= pb.CheckBox($"Dissect {render.Name}", ref render.Dissected);
                            }
                        }
                    }

                    // -- X-axis Dissection --
                    issue |= pb.CheckBox("Enable X-axis plane", ref dissectX);
                    if (dissectX)
                    {
                        pb.Label("X-axis direction:");
                        issue |= pb.RadioButtons("xAxisDir", new[] { "Negative", "Positive" }, ref xAxisDir, true);

                        issue |= pb.DragFloat("X-plane position", ref xPlanePos, 0.01f, -10f, 10f);
                    }
                    // -- Y-axis Dissection --
                    issue |= pb.CheckBox("Enable Y-axis plane", ref dissectY);
                    if (dissectY)
                    {
                        pb.Label("Y-axis direction:");
                        issue |= pb.RadioButtons("yAxisDir", new[] { "Negative", "Positive" }, ref yAxisDir, true);

                        issue |= pb.DragFloat("Y-plane position", ref yPlanePos, 0.01f, -10f, 10f);
                    }
     
                    // -- Z-axis Dissection --
                    
                    issue |= pb.CheckBox("Enable Z-axis plane", ref dissectZ);
                    if (dissectZ)
                    {
                        pb.Label("Z-axis direction:");
                        issue |= pb.RadioButtons("zAxisDir", new[] { "Negative", "Positive" }, ref zAxisDir, true);

                        issue |= pb.DragFloat("Z-plane position", ref zPlanePos, 0.01f, -10f, 10f);
                    }
                }
                pb.CollapsingHeaderEnd();

                if (issue) IssueCrossSection();

                pb.CollapsingHeaderStart("Import/Export Mesh");
                if(pb.Button("Export")) SaveAllTemplatesForObject(selectedObject, templateObjects);
                pb.SameLine(10);
                if (pb.Button("Import"))
                {

                    if (pb.OpenFile("Choose File.", "", out var fileName))
                    {
                        loadedObj = LoadAllTemplatesForObject(fileName);

                    }
                    UpdateMesh(loadedObj, true, false);
                    loadedObj.Shown = true;
                }
                pb.SameLine(10);
                if (pb.Button("Clear"))
                {
                    WorkspaceProp.RemoveNamePattern(loadedObj.Name);
                    loadedObj.Shown = false;
                }

                pb.CollapsingHeaderEnd();
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


    private static Panel _mainPanel;

    private const string ResourceNamespace = "Annotator.Resources";
    private static Dictionary<string, Panel> _templatePanels = new();

}