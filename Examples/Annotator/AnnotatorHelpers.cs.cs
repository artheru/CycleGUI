using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;
using Python.Runtime;
using Annotator.RenderTypes;
using System.Drawing;
using System.Security.AccessControl;
using CycleGUI.API;
using CycleGUI;

namespace Annotator
{
    internal static class AnnotatorHelpers
    {
        private const string ResourceNamespace = "Annotator.Resources";

        public static string[] RetrieveObjects(string path)
        {
            string[] objectFolders = Directory.Exists(path)
                    ? Directory.GetDirectories(path)
                     .Select(Path.GetFileName)
                        .Where(folderName => folderName != "__pycache__") // Exclude "__pycache__"
         .              ToArray()
                        : Array.Empty<string>();

            return objectFolders;
        }

        public static void UpdatePythonDirectory(string folderName)
        {
            string annotatorDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string pythonDirectory = Path.Combine(annotatorDirectory, folderName);

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                sys.path.append(pythonDirectory);
            }

            Console.WriteLine($"Python directory updated to: {pythonDirectory}");
        }

        //public static float CalculatePositiveZ(BasicRender object)
        //{
        //    float minZ = float.MaxValue;
        //    for(int i =2)
        //}
        public static dynamic InitializePythonAndAssembly(string objectName, int data_index)
        {
            using (Py.GIL())
            {
                //dynamic myModule = Py.Import("globe_main");
                dynamic manager = Py.Import("manager");
                dynamic objectModule = manager.load_object(objectName);
                return objectModule.ConceptualAssembly(data_index);
            }
        }


        public static void SetViewportVisibility(Terminal viewport, string namePattern, bool show)
        {
            Console.WriteLine($"Set viewport visibility for {namePattern} on T{viewport.ID}");
            new SetPropShowHide
            {
                namePattern = namePattern,
                show = show
            }.IssueToTerminal(viewport);
        }

        public static byte[] LoadIcon()
        {
            var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(Assembly.GetExecutingAssembly()
                    .GetManifestResourceNames().First(p => p.Contains(".ico")));
            return new BinaryReader(stream).ReadBytes((int)stream.Length);
        }

        public static void CopyFileFromEmbeddedResources(string fileName)
        {
            // try
            // {
            //     var resourceName = $"{ResourceNamespace}.{fileName}";
            //     using var stream = Assembly.GetExecutingAssembly()
            //         .GetManifestResourceStream(resourceName);
            //     if (stream == null) throw new Exception($"Resource {resourceName} not found.");
            //
            //     using var output = new FileStream(Path.Combine(Directory.GetCurrentDirectory(), fileName), FileMode.Create);
            //     stream.CopyTo(output);
            // }
            // catch (Exception e)
            // {
            //     Console.WriteLine($"Failed to copy {fileName}: {e.Message}");
            // }
        }


        public static float[] DemoObjectFromPython(string objectName, int data_index)
        {
            using (Py.GIL())
            {
                //dynamic myModule = Py.Import("globe_main");
                dynamic manager = Py.Import("manager");
                dynamic objectModule = manager.load_object(objectName);
                return ProcessPythonInstance(objectModule.DemoObject(data_index));

            }
        }

        static float[] ProcessPythonInstance(dynamic instance)
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

        public static StartOptions LoadStartOptions()
        {
            if (!File.Exists("start_options.json"))
            {
                var defaultOptions = new StartOptions
                {
                    DisplayOnWeb = false,
                    PythonPath = @"C:\Path_to_Your_Python\python310.dll",
                    Port = 8081,
                };
                File.WriteAllText("start_options.json", JsonConvert.SerializeObject(defaultOptions, Formatting.Indented));

                Console.WriteLine("Start options file created. Please update the Python path and restart.");
                Console.ReadKey();
                Environment.Exit(0);
            }

            return JsonConvert.DeserializeObject<StartOptions>(File.ReadAllText("start_options.json"));
        }

        //public static List<GlobeConceptTemplates> InitializeTemplateRenders(
        //    List<(string templateName, Dictionary<string, float[]>)> templatesInfo,
        //    List<Color> colors,
        //    dynamic conceptualAssembly)
        //{
        //    int colorIndex = 0;
        //    var geoTemplates = new List<GlobeConceptTemplates>();

        //    foreach (var (tName, paramDict) in templatesInfo)
        //    {
        //        var render = new GlobeConceptTemplates(tName, paramDict, colors[colorIndex % colors.Count].R, colors[colorIndex % colors.Count].G, colors[colorIndex % colors.Count].B);
        //        colorIndex++;

        //        using (Py.GIL())
        //        {
        //            dynamic pyDict = new PyDict();
        //            foreach (var kv in paramDict)
        //            {
        //                var arr = kv.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
        //                pyDict[kv.Key] = new PyList(arr);
        //            }
        //            conceptualAssembly.update_template_parameters(tName, pyDict);

        //            render.Mesh = GetSingleComponentMesh(conceptualAssembly, tName);
        //        }
        //        geoTemplates.Add(render);
        //    }

        //    return geoTemplates;
        //}

        public static List<(string templateName, Dictionary<string, float[]>)> GetTemplatesInfo(dynamic conceptualAssembly)
        {
            var result = new List<(string, Dictionary<string, float[]>)>();
            using (Py.GIL())
            {
                dynamic templatesInfo = conceptualAssembly.get_templates_info();

                foreach (var item in templatesInfo)
                {
                    string templateName = item[0].ToString();
                    dynamic pyParams = item[1];
                    var paramDict = new Dictionary<string, float[]>();

                    foreach (var key in pyParams.keys())
                    {
                        var arr = pyParams[key];
                        var paramVals = new List<float>();
                        foreach (var v in arr)
                        {
                            paramVals.Add((float)v);
                        }
                        paramDict[key.ToString()] = paramVals.ToArray();
                    }
                    result.Add((templateName, paramDict));
                }
            }
            return result;
        }
        public static BasicRender InitializeFullRender(string name, float[] mesh, Color color)
        {
            return new BasicRender(name, color.R, color.G, color.B) { Mesh = mesh };
        }

        public static float[] GetSingleComponentMesh(dynamic conceptualAssembly, string tname)
        {
            Console.WriteLine($">>> DEBUG template class name: {tname}");
            using (Py.GIL())
            {
                dynamic vertFaces = conceptualAssembly.get_single_component_mesh(tname);
                dynamic pyVertices = vertFaces[0];
                dynamic pyFaces = vertFaces[1];

                var vertices = ConvertToVertexList(pyVertices);
                var triangles = ConvertFacesToTriangles(vertices, pyFaces);
                return triangles;
            }
        }

        public static float[] GetFullMeshFromAssembly(dynamic conceptualAssembly)
        {
            using (Py.GIL())
            {
                dynamic vertFaces = conceptualAssembly.get_full_mesh();
                dynamic pyVertices = vertFaces[0];
                dynamic pyFaces = vertFaces[1];

                var vertices = ConvertToVertexList(pyVertices);
                var triangles = ConvertFacesToTriangles(vertices, pyFaces);
                return triangles;
            }
        }

        private static List<Tuple<float, float, float>> ConvertToVertexList(dynamic pyVertices)
        {
            int len = pyVertices.shape[0].As<int>();
            var vertices = new List<Tuple<float, float, float>>(len);

            dynamic shape = pyVertices.shape;
            for (var i = 0; i < shape[0].As<int>(); ++i)
            {
                vertices.Add(Tuple.Create(
                    pyVertices.GetItem(i).GetItem(2).As<float>(),
                    pyVertices.GetItem(i).GetItem(0).As<float>(),
                    pyVertices.GetItem(i).GetItem(1).As<float>()));
            }
            return vertices;
        }

        private static float[] ConvertFacesToTriangles(List<Tuple<float, float, float>> vertices, dynamic pyFaces)
        {
            var toRender = new List<float>();

            var faces = new List<Tuple<int, int, int>>();
            dynamic faceShape = pyFaces.shape;

            for (var i = 0; i < faceShape[0].As<int>(); ++i)
            {
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

        public static void OpenOrBringToFrontTemplatePanel(GlobeConceptTemplates template, Dictionary<string, Panel> _templatePanels)
        {

            // Check if this panel is already open
            if (!_templatePanels.ContainsKey(template.Name))
            {
                var newPanel = GUI.DeclarePanel();
                newPanel.ShowTitle($"{template.Name}");

                // Define the panel: show any template-specific parameters
                newPanel.Define(pb =>
                {
                    if(pb.Closing())
                    {
                        newPanel.Exit();
                        return;
                    }
                    pb.Label($"Params for {template.Name}");
                    // The same logic you'd have used inline:
                    template.ParameterChangeAction(pb);
                });

                _templatePanels[template.Name] = newPanel;
            }
            else
            {
                // Already open, bring it to front
                _templatePanels[template.Name].BringToFront();
            }

        }

        public static void CloseTemplatePanelIfOpen(string templateName, Dictionary<string, Panel> _templatePanels)
        {
            if (_templatePanels.TryGetValue(templateName, out var panel))
            {
     
                panel.Exit();
                _templatePanels.Remove(templateName);
            }
        }

        public static void SaveAllTemplatesForObject(string objectName, List<GlobeConceptTemplates> templateObjects)
        {
            try
            {
                var objectData = new ObjectSaveData
                {
                    ObjectName = objectName
                };


                foreach (var template in templateObjects)
                {
                    var templateData = new TemplateSaveData
                    {
                        Positions = template.Mesh,
                        Pos = template.Pos,
                        Rot = template.Rot
                    };

                    objectData.Templates.Add(templateData);
                }


                var jsonStr = JsonConvert.SerializeObject(objectData, Formatting.Indented);

                var savePath = Path.Combine(Directory.GetCurrentDirectory(), $"{objectName}_allTemplates.json");
                File.WriteAllText(savePath, jsonStr);

                Console.WriteLine($"Saved all templates for '{objectName}' to {savePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving multiple templates: " + ex.Message);
            }
        }

        public static List<string> LoadObjectNames(List<string> objectNames, string ObjectListFile)
        {
            if (File.Exists(ObjectListFile))
            {
                var jsonStr = File.ReadAllText(ObjectListFile);
                objectNames = JsonConvert.DeserializeObject<List<string>>(jsonStr) ?? new List<string>();
            }
            else
            {
                // Initialize with default objects if the file doesn't exist
                objectNames = new List<string> { "Globe", "Chair", "Bottle", "KitchenPot" };
                SaveObjectNames(objectNames, ObjectListFile);
            }

            return objectNames;
        }

        public static void SaveObjectNames(List<string> objectNames, string ObjectListFile)
        {
            var jsonStr = JsonConvert.SerializeObject(objectNames, Formatting.Indented);
            File.WriteAllText(ObjectListFile, jsonStr);
        }

        public static BasicRender LoadAllTemplatesForObject(string objectName)
        {
            BasicRender mergedObject = null; // In case we can't load or something fails

            try
            {
                var loadPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"{objectName}"
                );

                if (!File.Exists(loadPath))
                {
                    Console.WriteLine($"File not found: {loadPath}");
                }

                // 1) Read & Deserialize
                var jsonStr = File.ReadAllText(loadPath);
                var objectData = JsonConvert.DeserializeObject<ObjectSaveData>(jsonStr);
                if (objectData == null)
                {
                    Console.WriteLine("Error: failed to deserialize ObjectSaveData");
                }

                // 2) Prepare a list for combined geometry
                var mergedPositions = new List<float>();

                // 3) Merge all template geometry
                foreach (var tData in objectData.Templates)
                {
                    if (tData.Positions == null || tData.Positions.Length % 3 != 0)
                    {
                        continue;
                    }

                    // Process each set of 3 floats as a Vector3
                    for (int i = 0; i < tData.Positions.Length; i += 3)
                    {
                        var localVertex = new Vector3(
                            tData.Positions[i],
                            tData.Positions[i + 1],
                            tData.Positions[i + 2]
                        );

                        // Ensure quaternion is normalized
                        if (Math.Abs(1.0 - tData.Rot.Length()) > 1e-6)
                        {
                            tData.Rot = Quaternion.Normalize(tData.Rot);
                        }

                        // Apply rotation and translation
                        var rotated = Vector3.Transform(localVertex, tData.Rot);
                        var worldPos = rotated + tData.Pos;

                        // Add to the merged positions list (flattened as floats)
                        mergedPositions.Add(worldPos.X);
                        mergedPositions.Add(worldPos.Y);
                        mergedPositions.Add(worldPos.Z);
                    }
                }

                // 4) Create one combined mesh (BasicRender)
                mergedObject = new BasicRender(
                    objectName + "_Combined",
                    128, 128, 128 // Placeholder color
                )
                {
                    // Flattened float[] for all vertices
                    Mesh = mergedPositions.ToArray(),

                    // Since we already placed vertices in "world" coords,
                    // typically set base transform to zero
                    Pos = Vector3.Zero,
                    Rot = Quaternion.Identity
                };
                Console.WriteLine($"Merged {objectData.Templates.Count} templates from '{objectName}' into a single mesh.");

                return mergedObject;

            }
            catch (Exception ex)
            {
                throw;
                Console.WriteLine("Error loading & merging templates: " + ex.Message);
            }
        }
    }

    internal class StartOptions
    {
        public bool DisplayOnWeb { get; set; }
        public string PythonPath { get; set; }
        public int Port { get; set; }
    }

    public class ObjectSaveData
    {
        public string ObjectName { get; set; } = "";
        public List<TemplateSaveData> Templates { get; set; } = new();
    }

    public class TemplateSaveData
    {
        // raw geometry (if you want to store actual vertices; optional)
        public float[] Positions { get; set; } = Array.Empty<float>();

        // the transform (position + rotation) of this template's part
        public Vector3 Pos { get; set; } = new Vector3();
        public Quaternion Rot { get; set; } = new Quaternion();
    }


}
