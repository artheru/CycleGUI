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

        public static float CalculateGlobalMinZ(BasicRender demoObject, List<GlobeConceptTemplates> templateObjects)
        {
            float globalMinZ = float.MaxValue;

            //if (demoObject == null && templateObjects == null) Console.WriteLine("Temporary Solution");
            //else
            //{
                // Check demo object
                for (int i = 2; i < demoObject.Mesh.Length; i += 3)
                {
                    if (demoObject.Mesh[i] < globalMinZ)
                        globalMinZ = demoObject.Mesh[i];
                }

                // Check all templates
                foreach (var template in templateObjects)
                {
                    for (int i = 2; i < template.Mesh.Length; i += 3)
                    {
                        if (template.Mesh[i] < globalMinZ)
                            globalMinZ = template.Mesh[i];
                    }
                }
            //}
            
            return globalMinZ;
        }

        public static string TemporaryCleanName(string name)
        {
            var templateName = name;
            if (templateName.IndexOf('-') >= 0)
            {
                templateName = templateName.Substring(0, templateName.IndexOf("-"));
            }
            return templateName;
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
            Console.WriteLine($"Tthe object {namePattern} is being visualized on {viewport.ID} and show is {show}");
            new SetPropShowHide
            {
                namePattern = namePattern,
                show = show
            }.IssueToTerminal(viewport);
        }

        public static void SetViewportVisibilityDefault(string namePattern, bool show)
        {
            new SetPropShowHide
            {
                namePattern = namePattern,
                show = show
            }.IssueToDefault();
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
            try
            {
                var resourceName = $"{ResourceNamespace}.{fileName}";
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName);
                if (stream == null) throw new Exception($"Resource {resourceName} not found.");

                using var output = new FileStream(Path.Combine(Directory.GetCurrentDirectory(), fileName), FileMode.Create);
                stream.CopyTo(output);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to copy {fileName}: {e.Message}");
            }
        }


        public static (float[], string, string) DemoObjectFromPython(string objectName, int data_index)
        {
            using (Py.GIL())
            {
                //dynamic myModule = Py.Import("globe_main");
                dynamic manager = Py.Import("manager");
                dynamic objectModule = manager.load_object(objectName);
                return (ProcessPythonInstance(objectModule.DemoObject(data_index)), objectModule.DemoObject(data_index).data_id,
                    objectModule.DemoObject(data_index).data_type);

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
                    //PythonPath = @"C:\Path_to_Your_Python\python310.dll",
                    Port = 8081,
                };
                File.WriteAllText("start_options.json", JsonConvert.SerializeObject(defaultOptions, Formatting.Indented));

                Console.WriteLine("Start options file created. Please update the Python path and restart.");
                Console.ReadKey();
                Environment.Exit(0);
            }

            return JsonConvert.DeserializeObject<StartOptions>(File.ReadAllText("start_options.json"));
        }

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

                    foreach (dynamic key in pyParams.keys())
                    {
                        dynamic value = pyParams[key];

                        if (value is Python.Runtime.PyObject)
                        {
                            string pythonType = value.GetPythonType().ToString();

                            if (pythonType == "<class 'list'>")
                            {
                                // Handle lists
                                var paramVals = new List<float>();
                                foreach (dynamic v in value)
                                {
                                    paramVals.Add(Convert.ToSingle(v.AsManagedObject(typeof(float))));
                                }
                                paramDict[key.ToString()] = paramVals.ToArray();
                            }
                            else
                            {
                                // Handle scalars
                                paramDict[key.ToString()] = new float[] { Convert.ToSingle(value.AsManagedObject(typeof(float))) };
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Unexpected Python object type.");
                        }
                    }
                    result.Add((templateName, paramDict));
                }
            }
            return result;
        }

        public static float[] GetSingleComponentMesh(dynamic conceptualAssembly, string tname)
        {
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

        public static void OpenOrBringToFrontTemplatePanel(Panel _mainPanel, GlobeConceptTemplates template, Dictionary<string, Panel> _templatePanels)
        {

            // Check if this panel is already open
            if (!_templatePanels.ContainsKey(template.Name))
            {
                var newPanel = GUI.DeclarePanel()
                    .InitPosRelative(_mainPanel, 0, 16, 0, 1)
                    .SetDefaultDocking(Panel.Docking.Right);
                newPanel.ShowTitle($"{template.Name}");

                // Define the panel: show any template-specific parameters
                newPanel.Define(pb =>
                {
                    if(pb.Closing())
                    {
                        _templatePanels.Remove(template.Name);
                        newPanel.Exit();
                        template.PanelShown = false;
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

        public static void SaveAllTemplatesForObject(string objectName, string fileName, List<GlobeConceptTemplates> templateObjects,
            string objectId, string objectType)
        {
            try
            {

                var objectData = new ObjectSaveData
                {
                    category = objectName,
                    id = objectId,
                    type = objectType,
                };

                foreach(var templateObject in templateObjects)
                {
                    var parametersCopy = CloneParameters(templateObject.Parameters);

                    Vector3 eulerRotation = ToEulerAngles(templateObject.Rot);
                    eulerRotation = new Vector3(
                        eulerRotation.X * (180 / MathF.PI),
                        eulerRotation.Y * (180 / MathF.PI),
                        eulerRotation.Z * (180 / MathF.PI)
                    );

                    Console.WriteLine($"Quaternion: X: {templateObject.Rot.X} - Y: {templateObject.Rot.Y} - Z: {templateObject.Rot.Z}");
                    
                    parametersCopy["position"][0] = templateObject.Pos.Y / 1000;
                    parametersCopy["position"][1] = templateObject.Pos.Z / 1000;
                    parametersCopy["position"][2] = templateObject.Pos.X / 1000;

                    Console.WriteLine($"Euler in Degree: X: {eulerRotation.X} - Y: {eulerRotation.Y} - Z: {eulerRotation.Z}");
                    parametersCopy["rotation"][0] = eulerRotation.Y;
                    parametersCopy["rotation"][1] = eulerRotation.Z;
                    parametersCopy["rotation"][2] = eulerRotation.X;
                    
                    //templateObject.Parameters["rotation"] = new float[] {eulerRotation.X, eulerRotation.Y, eulerRotation.Z};
                    //templateObject.Parameters["position"] = new float[] { templateObject.Pos.X, templateObject.Pos.Y, templateObject.Pos.Z };
                    var templateData = new TemplateSaveData
                    {
                        template = templateObject.Name,
                        Parameters = parametersCopy
                    };

                    objectData.conceptualization.Add(templateData);

                }

                // Serialize and save to JSON
                var jsonStr = JsonConvert.SerializeObject(objectData, Formatting.Indented);
                var savePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                File.WriteAllText(savePath, jsonStr);

                Console.WriteLine($"Saved parameters for '{objectName}' to {savePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving parameters: {ex.Message}");
            }
        }

        private static Dictionary<string, float[]> CloneParameters(Dictionary<string, float[]> original)
        {
            var copy = new Dictionary<string, float[]>();
            foreach (var kv in original)
            {
                copy[kv.Key] = (float[])kv.Value.Clone(); // Deep copy each array
            }
            return copy;
        }

        public static Vector3 CalculateBoundingBoxCenter(float[] vertices)
        {
            if (vertices == null || vertices.Length % 3 != 0)
            {
                throw new ArgumentException("Vertices array must be non-null and a multiple of 3.");
            }

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < vertices.Length; i += 3)
            {
                if (i + 2 >= vertices.Length)
                {
                    throw new IndexOutOfRangeException($"Index {i + 2} is out of range for vertices array of length {vertices.Length}.");
                }

                float x = vertices[i];
                float y = vertices[i + 1];
                float z = vertices[i + 2];

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                minZ = Math.Min(minZ, z);

                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                maxZ = Math.Max(maxZ, z);
            }

            float centerX = (minX + maxX) / 2;
            float centerY = (minY + maxY) / 2;
            float centerZ = (minZ + maxZ) / 2;

            return new Vector3(centerX, centerY, 0f);
        }

        public static void FocusCameraOnObject(Vector3 center)
        {
            // Adjust the camera's LookAt position to the object's center
            var cameraLookAtX = center.X;
            var cameraLookAtY = center.Y;
            var cameraLookAtZ = center.Z;

            // Optionally adjust other camera parameters
            var cameraDistance = 5f;
            var cameraAltitude = 0f;
            var cameraAzimuth = 0f;
            var cameraFov = 45f;

            // Apply the camera settings
            new SetCamera()
            {
                lookAt = new Vector3(cameraLookAtX, cameraLookAtY, cameraLookAtZ),
                altitude = cameraAltitude,
                azimuth = cameraAzimuth,
                distance = cameraDistance,
                fov = cameraFov,
            }.IssueToAllTerminals();
        }

        public static List<GlobeConceptTemplates> LoadAllTemplatesForObjectWithAdjustment(ObjectSaveData objectData, dynamic conceptualAssembly)
        {
            try
            {
                if (objectData == null || objectData.conceptualization.Count == 0)
                {
                    Console.WriteLine("No templates found in the loaded file.");
                    throw new Exception();
                }

                // List to store all loaded templates
                var loadedTemplates = new List<GlobeConceptTemplates>();

                foreach (var templateData in objectData.conceptualization)
                {
                    using (Py.GIL())
                    {
                        // Prepare Python parameters
                        dynamic pyDict = new PyDict();

                        foreach (var param in templateData.Parameters)
                        {
                            if (param.Key.ToLower().Contains("rotation")) // Exclude rotation parameters
                            {
                                continue;
                            }

                            var arr = param.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
                            pyDict[param.Key] = new PyList(arr);
                        }



                        // Update Python template parameters
                        conceptualAssembly.update_template_parameters(TemporaryCleanName(templateData.template), pyDict);

                        // Retrieve the mesh for the template
                        var generatedMesh = GetSingleComponentMesh(conceptualAssembly, TemporaryCleanName(templateData.template));
                        var savedPos = Vector3.Zero;
                        var savedRot = Quaternion.Identity;

                        if (templateData.Parameters.ContainsKey("position"))
                        {
                            var posArray = templateData.Parameters["position"];
                            if (posArray.Length >= 3)
                            {
                                savedPos = new Vector3(posArray[2] * 1000, posArray[0] * 1000, posArray[1] * 1000);
                            }
                        }

                        if (templateData.Parameters.ContainsKey("rotation"))
                        {
                            var rotArray = templateData.Parameters["rotation"];
                            if (rotArray.Length >= 3)
                            {
                                // Convert Euler angles (in degrees) to Quaternion
                                Vector3 eulerRotation = new Vector3(
                                    rotArray[2] * (MathF.PI / 180),
                                    rotArray[0] * (MathF.PI / 180),
                                    rotArray[1] * (MathF.PI / 180)
                                );
                                savedRot = ToQuaternion(eulerRotation);
                                //savedRot = new Quaternion(rotArray[2], rotArray[0], rotArray[1], 1);
                            }
                        }

                        // Create a new template object in C#
                        var render = new GlobeConceptTemplates($"{templateData.template}", templateData.Parameters, 128, 128, 128)
                        {
                            Mesh = generatedMesh,
                            Pos = savedPos,
                            Rot = savedRot,
                            Shown = false,
                            Resume = true,
                            minZ = true,
                        };
                        loadedTemplates.Add(render);

                        Console.WriteLine($"Loaded Position and Rotation: Pos: {render.Pos} -- ROT: {render.Rot}");
                    }
                }

                Console.WriteLine($"Loaded and reinitialized {loadedTemplates.Count} templates for '{objectData.category}'.");
                return loadedTemplates;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading parameters for adjustment: {ex.Message}");
                throw new Exception();
            }
        }

        public static Vector3 ToEulerAngles(Quaternion q)
        {
            Vector3 angles = new();

            // roll / x
            double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            angles.X = (float)Math.Atan2(sinr_cosp, cosr_cosp);

            // pitch / y
            double sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (Math.Abs(sinp) >= 1)
            {
                angles.Y = (float)Math.CopySign(Math.PI / 2, sinp);
            }
            else
            {
                angles.Y = (float)Math.Asin(sinp);
            }

            // yaw / z
            double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            angles.Z = (float)Math.Atan2(siny_cosp, cosy_cosp);

            return angles;
        }

        public static Quaternion ToQuaternion(Vector3 v)
        {

            float cy = (float)Math.Cos(v.Z * 0.5);
            float sy = (float)Math.Sin(v.Z * 0.5);
            float cp = (float)Math.Cos(v.Y * 0.5);
            float sp = (float)Math.Sin(v.Y * 0.5);
            float cr = (float)Math.Cos(v.X * 0.5);
            float sr = (float)Math.Sin(v.X * 0.5);

            return new Quaternion
            {
                W = (cr * cp * cy + sr * sp * sy),
                X = (sr * cp * cy - cr * sp * sy),
                Y = (cr * sp * cy + sr * cp * sy),
                Z = (cr * cp * sy - sr * sp * cy)
            };

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
        public string category { get; set; } = "";
        public string id { get; set; } = "";
        public string type { get; set; } = "";  
        public List<TemplateSaveData> conceptualization { get; set; } = new();
    }


    public class TemplateSaveData
    {
        public string template { get; set; } = "";
        public Dictionary<string, float[]> Parameters { get; set; } = new();
        //public Vector3 Pos { get; set; } = new Vector3();
        //public Quaternion Rot { get; set; } = new Quaternion();
    }

}
