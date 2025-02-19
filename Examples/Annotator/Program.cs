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
        Dictionary<string, int> templateNameCounts = new Dictionary<string, int>();
        var dropdownMenu = objectFolders.Length > 0 ? objectFolders : new[] { "No objects available" };
        int dropdownIndex = 0; // Default to "Bottle" if it exists
        var dropDownTemplates = new List<string>();
        int templateIndex = 0;
        var objectsDropDown = new[] { "0", "1", "2" };
        int objectDropDownIndex = Array.IndexOf(objectsDropDown, "0");
        var selectedObject = dropdownMenu[0];
        var selectedTemplates = "";
        var lastSelectedTemplate = "";
        var dataIndex = 0;
        var templateObjects = new List<GlobeConceptTemplates>();
        var objectCnt = 0;
        var templateNameCnt = 1;
        float desiredZplane = 0.0f;
        float globalMinZ = 0;
        var colors = new List<Color>() { Color.DarkGoldenrod, Color.CornflowerBlue, Color.DarkRed, Color.DarkGray };
        var startOptions = LoadStartOptions();
        var pythonDllPath = startOptions.PythonPath;
        string objectDataID = "";
        string objectDataType = "";
        bool isDataReady = true;

        CopyFileFromEmbeddedResources("libVRender.dll");
        byte[] icoBytes = LoadIcon();

        PythonInterface.Initialize(pythonDllPath);
        PythonEngine.BeginAllowThreads();
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(annotatorDirectory);
        }

        dynamic conceptualAssembly = InitializePythonAndAssembly(selectedObject, dataIndex);
        List<(string templateName, Dictionary<string, float[]>)> templatesInfo = GetTemplatesInfo(conceptualAssembly);
        using(Py.GIL())
        {
            dynamic templateNames = conceptualAssembly.get_template_names();
            foreach (var templateName in templateNames) { 
                var name = templateName.ToString();
                dropDownTemplates.Add(name); 
            }
        }
        // Retrieves all the templates info for each object
        selectedTemplates = templatesInfo[templateIndex].templateName; // Update the selected template name

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

        void UpdateMesh(BasicRender options, bool firstPut, bool template=false, float zBias = 0.0f)
        {
            if (options.Mesh == null || options.Mesh.Length == 0)
            {
                Console.WriteLine($"Warning: Cannot update mesh for '{options.Name}' because mesh data is empty.");
                // Optionally, you could switch to a dynamic buffer here if updates are expected.
                return;
            }

            Workspace.AddProp(new DefineMesh()
            {
                clsname = $"{options.Name}-cls",
                positions = options.Mesh,
                color = ConcatHexABGR(options.ColorA, options.ColorB, options.ColorG, options.ColorR),
                smooth = false,
            });

            if (firstPut)
            {
                var biasedPosition = new Vector3(options.Pos.X, options.Pos.Y, options.Pos.Z + zBias);
                Workspace.AddProp(new PutModelObject()
                {
                    clsName = $"{options.Name}-cls",
                    name = options.Name,
                    newPosition = biasedPosition,
                    newQuaternion = options.Rot
                });
                if (template) SetViewportVisibility(objectViewport, options.Name, false);

            }
        }
        void BuildMesh(PanelBuilder pb, BasicRender options, bool template, float zBias = 0)
        {
            var objName = options.Name;
            if (objName.IndexOf('-') >= 0)
            {
                objName = objName.Substring(0, objName.IndexOf("-"));
            }

            if(!isDataReady)
            {
                pb.Label("Loading data... Please wait");
            }
            else
            {
                if (!options.Shown && pb.Button($"Render {objName}"))
                {
                    UpdateMesh(options, true, template, zBias);
                    options.Shown = true;
                    SetViewportVisibility(objectViewport, options.Name, true);
                    SetViewportVisibility(templateViewport, options.Name, false);
                }
            }
            //if (!options.Shown && pb.Button($"Render {objName}"))
            //{
            //    UpdateMesh(options, true, template, zBias);
            //    options.Shown = true;
            //    SetViewportVisibility(objectViewport, options.Name, true);
            //    SetViewportVisibility(templateViewport, options.Name, false);
            //}

            //if (!options.Shown && pb.Button($"Render {objName}"))
            //{
            //    Task.Run(async () =>
            //    {
            //        int retries = 0;
            //        while (!isDataReady && retries < 50) // Wait up to 5 seconds
            //        {
            //            Thread.Sleep(100);
            //            retries++;
            //        }

            //        if (!isDataReady)
            //        {
            //            Console.WriteLine("Error: Data still not ready after waiting.");
            //            return;
            //        }

            //        lock (templateObjects)
            //        {
            //            UpdateMesh(options, true, template, zBias);
            //            options.Shown = true;
            //        }

            //        SetViewportVisibility(objectViewport, options.Name, true);
            //        SetViewportVisibility(templateViewport, options.Name, false);
            //    });
            //}

            if (options.Shown)
            {
                if (pb.Button($"Stop Rendering {objName}"))
                {
                    WorkspaceProp.RemoveNamePattern(options.Name);
                    options.Shown = false;
                }
                if (template)
                    options.ParameterChangeAction(pb);
            }
        }
        var cc = NextColor(objectCnt++);
        BasicRender demo = new(selectedObject, cc.R, cc.G, cc.B);
        (demo.Mesh, objectDataID, objectDataType) = DemoObjectFromPython(selectedObject, dataIndex);

        // DEBUGGING FOR THE PROBLEM ON CUSTOMER SIDE
        if(demo.Mesh == null || demo.Mesh.Length == 0)
        {
            Console.WriteLine($"ERROR: Mesh data for object {selectedObject} is empty.  Please ensure the object asset is correctly loaded");
        }
        globalMinZ = CalculateGlobalMinZ(demo, templateObjects);

        new Thread(() =>
        {
            string lastObj = selectedObject;
            int lastIndex = dataIndex;
            while (true)
            {
                Thread.Sleep(100);

                if (selectedObject != lastObj || dataIndex != lastIndex)
                {
                    isDataReady = false;
                    lock (templateObjects)
                    {
                        foreach (var render in templateObjects)
                        {
                            WorkspaceProp.RemoveNamePattern(render.Name);
                        }
                        templateObjects.Clear();
                    }
                    templatesInfo.Clear();
                    templateNameCounts.Clear();
                    selectedTemplates = "";
                    templateIndex = 0;
                    _templatePanels.Clear();
                    WorkspaceProp.RemoveNamePattern(demo.Name);

                    demo = new(selectedObject, cc.R, cc.G, cc.B);
                    (demo.Mesh, objectDataID, objectDataType) = DemoObjectFromPython(selectedObject, dataIndex);
                    if (demo.Mesh == null || demo.Mesh.Length == 0)
                    {
                        Console.WriteLine($"[225]ERROR: Mesh data for object {selectedObject} is empty.  Please ensure the object asset is correctly loaded");
                    }
                    globalMinZ = CalculateGlobalMinZ(demo, templateObjects);

                    conceptualAssembly = InitializePythonAndAssembly(selectedObject, dataIndex);
                    templatesInfo = GetTemplatesInfo(conceptualAssembly);
                    selectedTemplates = templatesInfo[templateIndex].templateName; // Update the selected template name
                    isDataReady = true;
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
                lock(templateObjects)
                {
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
                                //foreach (var kv in render.Parameters)
                                //{
                                //    var arr = kv.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
                                //    pyDict[kv.Key] = new PyList(arr);
                                //}
                                foreach (var param in render.Parameters)
                                {
                                    if (param.Key.ToLower().Contains("rotation")) // Exclude rotation parameters
                                    {
                                        continue;
                                    }

                                    var arr = param.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
                                    pyDict[param.Key] = new PyList(arr);
                                }

                                // Update the template in Python
                                conceptualAssembly.update_template_parameters(templateName, pyDict);
                            }

                            render.Mesh = GetSingleComponentMesh(conceptualAssembly, templateName);
                            if (render.Mesh == null || render.Mesh.Length == 0)
                            {
                                Console.WriteLine($" [297] Warning: Template '{templateName}' returned empty mesh data.");
                            }

                            UpdateMesh(render, false, true);
                            render.Update();
                            break;
                        }
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
        List<int> indicesToDelete = new List<int>();

        var uniqueName = "";
        CycleGUIHandler DefineMainPanel()
        {
            return pb =>
            {
                var issue = false;
                pb.Panel.ShowTitle("Manipulation");

                if (demo.Mesh != null)
                {
                    var objectCenter = CalculateBoundingBoxCenter(demo.Mesh);

                    if (pb.Button("Focus Camera on Object"))
                    {
                        FocusCameraOnObject(objectCenter);
                    }
                }
                else
                {
                    if (pb.Button("Focus Camera on Object"))
                    {
                        FocusCameraOnObject(new Vector3(0.3f, 0.3f, 0f));
                    }
                }

                pb.CollapsingHeaderStart("Target Object");
                pb.Label("Select Object:");
                // Dropdown menu for object selection
                if (pb.DropdownBox("Object Names", dropdownMenu.ToArray(), ref dropdownIndex))
                {
                    string selectedFolder = dropdownMenu[dropdownIndex];
                    selectedObject = selectedFolder;
                    
                }

                pb.Label($"Select type of {selectedObject}:");
                if (pb.DropdownBox("", objectsDropDown, ref objectDropDownIndex))
                {
                    var selectIndex = int.Parse(objectsDropDown[objectDropDownIndex]);
                    dataIndex = selectIndex;
                    
                }
                // Section for adding new objects
                //pb.SeparatorText("Target Object");
                
                BuildMesh(pb, demo, false, desiredZplane - globalMinZ);
                pb.CollapsingHeaderEnd();
                pb.Separator();

                if (!init)
                {
                    DefineSelectAction();

                    new SetAppearance() { bring2front_onhovering = false, useGround = false }
                        .Issue();
                    init = true;
                }

                if(demo.Shown) defaultAction.SetObjectSelectable(demo.Name);

                //lock(templateObjects)
                //{
                //    foreach (var geo in templateObjects)
                //    {
                //        if (geo.Shown) defaultAction.SetObjectSelectable(geo.Name);
                //    }
                //}

                lock (templateObjects)
                {
                    foreach (var geo in templateObjects)
                    {
                        if (geo.Shown) defaultAction.SetObjectSelectable(geo.Name);
                    }
                }
                pb.CollapsingHeaderStart("Conceptual Templates");

                // Dropdown for selecting a template
                pb.Label("Select Template");
                if (pb.DropdownBox("Template Names", templatesInfo.Select(t => t.templateName).ToArray(), ref templateIndex))
                {
                    selectedTemplates = templatesInfo[templateIndex].templateName; // Update the selected template name
                        
                }
                //if (lastSelectedTemplate != selectedTemplates) templateNameCnt = 1;

                if(pb.Button($"Add {selectedTemplates}"))
                {
                    lastSelectedTemplate = selectedTemplates;
                    // Render or Stop Rendering the selected template
                    if (!string.IsNullOrEmpty(selectedTemplates))
                    {
                        if (!templateNameCounts.ContainsKey(selectedTemplates))
                        {
                            templateNameCounts[selectedTemplates] = 1;
                        }

                        var selectedTemplate = templatesInfo.FirstOrDefault(t => t.templateName == selectedTemplates);

                        if (selectedTemplate != default)
                        {

                            var (tName, paramDict) = selectedTemplate;
                            var color = NextColor(objectCnt);
                            var uniqueName = $"{tName}-{templateNameCounts[selectedTemplates]++}";

                            var render = new GlobeConceptTemplates(uniqueName, paramDict, color.R, color.G, color.B);

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
                            OpenOrBringToFrontTemplatePanel(_mainPanel, render, _templatePanels);
                            render.PanelShown = true;
                            lock(templateObjects)
                            {
                                templateObjects.Add(render);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Template '{selectedTemplates}' not found.");
                        }
                    }

                    lock(templateObjects)
                    {
                        foreach (var template in templateObjects)
                        {
                            if (template.Resume)
                            {
                                OpenOrBringToFrontTemplatePanel(_mainPanel, template, _templatePanels);
                                template.PanelShown = true;
                                template.Resume = false;
                            }
                        }
                    }

                }

                lock(templateObjects)
                {
                    // Render the table with the dynamically added templates
                    pb.Table("TemplateTable", ["Template Name", "Show/Hide", "Delete", "Parameter Panel"], templateObjects.Count, (row, rowIndex) =>
                    {

                        if (rowIndex < 0 || rowIndex >= templateObjects.Count)
                            return;

                        var template = templateObjects[rowIndex];
                        var templateMinZ = CalculateGlobalMinZ(template, templateObjects);
                        if (!template.Shown)
                        {

                            UpdateMesh(template, true, true);
                            template.Shown = true;
                        }

                        // Column 1
                        row.Label(template.Name);

                        var defaultShowHideButtonLabel = template.ShowInDefaultViewport ? "M-Hide" : "M-Show";
                        var showHideButtonLabel = template.ShowInTempViewport ? "T-Hide" : "T-Show";
                        var defaultShowHideAction = row.ButtonGroup([defaultShowHideButtonLabel, showHideButtonLabel]);

                        if (defaultShowHideAction == 0)
                        {
                            SetViewportVisibility(objectViewport, template.Name, false);
                            template.ShowInDefaultViewport = !template.ShowInDefaultViewport;
                            SetViewportVisibilityDefault(template.Name, template.ShowInDefaultViewport);

                        }
                        if (defaultShowHideAction == 1)
                        {
                            SetViewportVisibility(objectViewport, template.Name, false);
                            template.ShowInTempViewport = !template.ShowInTempViewport;
                            SetViewportVisibility(templateViewport, template.Name, template.ShowInTempViewport);

                        }

                        var deleteAction = row.ButtonGroup(["Delete"]);
                        if (deleteAction == 0)
                        {
                            indicesToDelete.Add(rowIndex);

                            var templatePrefix = template.Name.Split('-')[0];

                            //templateObjects.RemoveAt(rowIndex);
                            WorkspaceProp.RemoveNamePattern(template.Name);
                            template.Shown = false;
                            // Adjust the count in the templateNameCounts dictionary
                            if (templateNameCounts.ContainsKey(templatePrefix))
                            {
                                templateNameCounts[templatePrefix]--;
                                if (templateNameCounts[templatePrefix] == 0)
                                {
                                    // Remove the entry if no templates of this type exist
                                    templateNameCounts.Remove(templatePrefix);
                                }
                            }
                            CloseTemplatePanelIfOpen(template.Name, _templatePanels);
                            template.PanelShown = false;
                        }
                        var panelShownButton = template.PanelShown ? "Hide" : "Show";
                        var panelShowHideAction = row.ButtonGroup([panelShownButton]);
                        if (panelShowHideAction == 0)
                        {
                            if (template.PanelShown)
                            {
                                CloseTemplatePanelIfOpen(template.Name, _templatePanels);
                                template.PanelShown = false;
                            }
                            else if (!template.PanelShown)
                            {
                                OpenOrBringToFrontTemplatePanel(_mainPanel, template, _templatePanels);
                                template.PanelShown = true;
                            }
                        }
                    });
                }

                if (indicesToDelete.Any())
                {
                    lock (templateObjects)
                    {
                        foreach (int index in indicesToDelete.OrderByDescending(i => i))
                        {
                            templateObjects.RemoveAt(index);
                        }
                    }
                    indicesToDelete.Clear();
                }


                pb.CollapsingHeaderEnd();
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
                var (str, done) = pb.TextInput("Press Enter to confirm the file name.", "enter file name to save");
                if (pb.Button("Export"))
                {

                    if (pb.SelectFolder("Choose file to save.", out var folderName))
                    {
                        lock(templateObjects)
                        {
                            SaveAllTemplatesForObject(selectedObject, $"{folderName}/{str}.json", templateObjects, objectDataID, objectDataType);
                        }
                    }
                }
                pb.SameLine(10);

                if (pb.Button("Import"))
                {
                    if (pb.OpenFile("Choose File.", "", out var fileName))
                    {
                        if (!File.Exists(fileName))
                        {
                            Console.WriteLine($"File not found: {fileName}");
                            throw new Exception();
                        }
                        // Read and deserialize the JSON
                        var jsonStr = File.ReadAllText(fileName);
                        var objectData = JsonConvert.DeserializeObject<ObjectSaveData>(jsonStr);
                        selectedObject = objectData.category;
                        dropdownIndex = Array.IndexOf(dropdownMenu, selectedObject);
                        conceptualAssembly = InitializePythonAndAssembly(selectedObject, 0);

                        var loadedTemplates = LoadAllTemplatesForObjectWithAdjustment(objectData, conceptualAssembly);
                        //selectedObject = result.Item1;
                        if (loadedTemplates != null)
                        {
                            lock(templateObjects)
                            {
                                foreach (var render in templateObjects)
                                {
                                    // Remove meshes associated with the previous object
                                    WorkspaceProp.RemoveNamePattern(render.Name);
                                }
                                templateObjects.Clear();
                            }

                            lock(templateObjects)
                            {
                                foreach (var template in loadedTemplates)
                                {
                                    //UpdateMesh(template, true, true, desiredZplane - globalMinZ);
                                    templateObjects.Add(template);
                                }
                            }
                        }
                    }
                }
                pb.CollapsingHeaderEnd();
                pb.Panel.Repaint();
                
            };
        }

        if (startOptions.DisplayOnWeb)
        {
            //Terminal.RegisterRemotePanel(Terminal => pb =>
            //{
            //    pb.Panel.ShowTitle("Annotator");
            //    pb.Label("Welcome to Annotator on web!");
            //    if (pb.Button("Start Labeling"))
            //    {
            //        GUI.defaultTerminal = pb.Panel.Terminal;
            //        _mainPanel = GUI.DeclarePanel(pb.Panel.Terminal);
            //        _mainPanel.Define(DefineMainPanel());
            //        pb.Panel.Exit();
            //    }
            //});
            //WebTerminal.Use(startOptions.Port, ico: icoBytes);
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