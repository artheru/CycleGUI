using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;
using CycleGUI.API;
using Python.Runtime;
using static Annotator.PanelConstructors;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Numerics;

namespace Annotator.RenderTypes
{
    internal class ConceptTemplate
    {
        public List<(string ParamName, PanelConstructors.ParameterInfo ParamInfo, List<double> ParamVal)> Parameters;
        public string CategoryName;
        public string TemplateName;
        public string PyFileFullName;
        public readonly Guid Guid = Guid.NewGuid();

        public string ObjectId = "";

        public string Name;
        public float[] Mesh;
        public float ColorR;
        public float ColorG;
        public float ColorB;
        public float ColorA = 255f;

        public Vector3 Pos;
        public Quaternion Rot;

        public ConceptTemplate(string categoryName, string templateName, Color color, bool offScreen, string objectId)
        {
            CategoryName = categoryName;
            TemplateName = templateName; // Use the template name directly

            PyFileFullName = Path.GetFullPath($"Objects\\{categoryName}\\concept_template.py");

            Name = $"{(offScreen ? "offscreen:" : "template:")}{categoryName}-{templateName}-{Guid.ToString()}";
            ColorR = color.R;
            ColorG = color.G;
            ColorB = color.B;
            
            Pos = Vector3.Zero;
            Rot = Quaternion.Identity;

            ObjectId = objectId;

            try
            {
                Parameters = InspectPythonClassFromFile();
                Mesh = InstantiateTemplateAndGetTriangles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{CategoryName}]({templateName}) init failed: {ex.MyFormat()}");
            }
        }

        public void UpdateParamsAndMesh(List<(string ParamName, PanelConstructors.ParameterInfo ParamInfo, List<double> ParamVal)> parameters)
        {
            Parameters = parameters;
            Mesh = InstantiateTemplateAndGetTriangles();
        }

        // Instantiate a Python class from file and return the instance as dynamic
        private List<(string ParamName, PanelConstructors.ParameterInfo ParamInfo, List<double> ParamVal)> InspectPythonClassFromFile()
        {
            if (string.IsNullOrWhiteSpace(PyFileFullName)) throw new ArgumentException("pyFile is null or empty", nameof(PyFileFullName));
            if (string.IsNullOrWhiteSpace(TemplateName)) throw new ArgumentException("className is null or empty", nameof(TemplateName));
            if (!File.Exists(PyFileFullName)) throw new FileNotFoundException($"Python file not found: {PyFileFullName}", PyFileFullName);

            var moduleDir = Path.GetFullPath(Path.GetDirectoryName(PyFileFullName));
            var moduleName = Path.GetFileNameWithoutExtension(PyFileFullName);

            var ret = new List<(string ParamName, PanelConstructors.ParameterInfo ParamInfo, List<double> ParamVal)>();

            // Use merged parameters loaded in PanelConstructors instead of template.json

            using (Py.GIL())
            {
                try
                {
                    dynamic sys = Py.Import("sys");
                    dynamic importlib = Py.Import("importlib");
                    dynamic inspect = Py.Import("inspect");

                    // Check if moduleDir is already in sys.path using traditional loop
                    bool pathExists = false;
                    foreach (dynamic pathItem in sys.path)
                    {
                        if (pathItem.ToString() == moduleDir)
                        {
                            pathExists = true;
                            break;
                        }
                    }

                    if (!pathExists)
                    {
                        sys.path.insert(0, moduleDir);
                    }

                    dynamic module = importlib.import_module(moduleName);
                    // Reload to reflect latest edits if any
                    module = importlib.reload(module);

                    // Debug: Print available attributes in the module
                    // Console.WriteLine($"Available attributes in module {moduleName}:");
                    try
                    {
                        dynamic moduleDict = module.__dict__;
                        foreach (dynamic key in moduleDict.keys())
                        {
                            // Console.WriteLine($"  - {key}");
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Could not list module attributes: {ex.MyFormat()}");
                    }

                    // Check if class exists by trying to access it directly
                    bool classExists = false;
                    try
                    {
                        dynamic testAttr = module.__dict__[TemplateName];
                        classExists = true;
                    }
                    catch
                    {
                        classExists = false;
                    }

                    if (!classExists)
                    {
                        throw new InvalidOperationException($"Class '{TemplateName}' not found in module '{moduleName}'. Available classes: {string.Join(", ", GetAvailableClasses(module))}");
                    }

                    // Get the class directly from the dynamic module
                    dynamic cls = module.__dict__[TemplateName];

                    // Find merged parameter spec for this category and concept
                    Dictionary<string, PanelConstructors.ParameterInfo> mergedSpec = null;
                    try
                    {
                        if (PanelConstructors.MergedParameter != null &&
                            PanelConstructors.MergedParameter.TryGetValue(CategoryName, out var conceptDict) &&
                            conceptDict != null &&
                            conceptDict.TryGetValue(TemplateName, out var paramDict))
                        {
                            mergedSpec = paramDict;
                        }
                    }
                    catch { }

                    // Inspect constructor parameters and their default values
                    try
                    {
                        dynamic signature = inspect.signature(cls);
                        dynamic parameters = signature.parameters;

                        // Console.WriteLine($"\n=== Constructor parameters for {TemplateName} ===");
                        // Console.WriteLine($"File: {PyFileFullName}");

                        foreach (dynamic paramName in parameters.keys())
                        {
                            string paramNameStr = paramName.ToString();
                            var paramVals = new List<double>();
                            PanelConstructors.ParameterInfo boundInfo = null;
                            
                            // Populate paramVals from predicted_conceptualization.pkl (parsed JSON)
                            try
                            {
                                var pklPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                    "Objects", CategoryName, "predicted_pkl", "predicted_conceptualization.pkl");
                                var jsonStr = PanelConstructors.ParsePklAsJson(pklPath);
                                if (!string.IsNullOrWhiteSpace(jsonStr))
                                {
                                    var root = JToken.Parse(jsonStr);
                                    JToken selected = null;
                                    if (root is JArray arrRoot)
                                    {
                                        if (!string.IsNullOrWhiteSpace(ObjectId))
                                        {
                                            foreach (var e in arrRoot)
                                            {
                                                if ($"{e["id"]}.obj" == ObjectId) { selected = e; break; }
                                            }
                                        }
                                        if (selected == null)
                                        {
                                            foreach (var e in arrRoot)
                                            {
                                                var cz = e?["conceptualization"] as JArray;
                                                if (cz == null) continue;
                                                foreach (var item in cz)
                                                {
                                                    if (item?["template"]?.ToString() == TemplateName)
                                                    {
                                                        selected = e; break;
                                                    }
                                                }
                                                if (selected != null) break;
                                            }
                                        }
                                        if (selected == null && arrRoot.Count() > 0) selected = arrRoot.First();
                                    }
                                    else
                                    {
                                        selected = root;
                                    }

                                    var conceptualization = selected?["conceptualization"] as JArray;
                                    if (conceptualization != null)
                                    {
                                        JToken matched = null;
                                        foreach (var item in conceptualization)
                                        {
                                            if (item?["template"]?.ToString() == TemplateName)
                                            {
                                                matched = item; break;
                                            }
                                        }
                                        var parametersNode = matched?["parameters"] as JObject;
                                        if (parametersNode != null)
                                        {
                                            var valueToken = parametersNode[paramNameStr];
                                            if (valueToken != null)
                                            {
                                                if (valueToken is JArray ja)
                                                {
                                                    foreach (var v in ja)
                                                    {
                                                        if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
                                                            paramVals.Add(v.Value<double>());
                                                    }
                                                }
                                                else if (valueToken.Type == JTokenType.Integer || valueToken.Type == JTokenType.Float)
                                                {
                                                    paramVals.Add(valueToken.Value<double>());
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to read predicted_conceptualization for {TemplateName}.{paramNameStr}: {ex.Message}");
                            }

                            // Bind ParamInfo from merged parameter spec
                            if (mergedSpec != null && mergedSpec.TryGetValue(paramNameStr, out var pinfo))
                            {
                                boundInfo = pinfo;
                            }
                            else
                            {
                                // create a fallback ParamInfo
                                boundInfo = new PanelConstructors.ParameterInfo
                                {
                                    IsFloat = true,
                                    Min = -1,
                                    Max = -1,
                                    Len = paramVals.Count > 0 ? paramVals.Count : -1
                                };
                            }

                            if (paramNameStr == "position")
                            {
                                Pos = new Vector3((float)paramVals[2], (float)paramVals[0], (float)paramVals[1]);
                            }
                            else if (paramNameStr == "rotation")
                            {
                                var rx = (float)((float)paramVals[2] / 180f * Math.PI);
                                var ry = (float)((float)paramVals[0] / 180f * Math.PI);
                                var rz = (float)((float)paramVals[1] / 180f * Math.PI);
                                var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, rz);
                                var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rx);
                                var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, ry);
                                Rot = qz * qy * qx;
                            }
                            // append to result
                            else ret.Add((paramNameStr, boundInfo, paramVals));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not inspect constructor parameters for {TemplateName}: {ex.Message}");
                    }
                }
                catch (PythonException pex)
                {
                    throw pex;
                }
            }

            return ret;
        }

        // Build a Python class instance from C# lists and return triangles as float[]
        private float[] InstantiateTemplateAndGetTriangles()
        {
            if (string.IsNullOrWhiteSpace(PyFileFullName)) throw new ArgumentException("pyFile is null or empty", nameof(PyFileFullName));
            if (string.IsNullOrWhiteSpace(TemplateName)) throw new ArgumentException("className is null or empty", nameof(TemplateName));
            if (!File.Exists(PyFileFullName)) throw new FileNotFoundException($"Python file not found: {PyFileFullName}", PyFileFullName);
            // if (ctorArgs == null) throw new ArgumentNullException(nameof(ctorArgs));

            var moduleDir = Path.GetFullPath(Path.GetDirectoryName(PyFileFullName));
            var moduleName = Path.GetFileNameWithoutExtension(PyFileFullName);

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                dynamic importlib = Py.Import("importlib");

                bool pathExists = false;
                foreach (dynamic pathItem in sys.path)
                {
                    if (pathItem.ToString() == moduleDir)
                    {
                        pathExists = true;
                        break;
                    }
                }
                if (!pathExists) sys.path.insert(0, moduleDir);

                dynamic module = importlib.import_module(moduleName);
                module = importlib.reload(module);
                dynamic cls = module.__dict__[TemplateName];

                // Build kwargs from ctorArgs
                using var kwargs = new PyDict();
                foreach (var kv in Parameters)
                {
                    if (kv.ParamName == "rotation" || kv.ParamName == "position") continue;

                    var name = kv.ParamName;
                    var list = kv.ParamVal;

                    var pyItems = kv.ParamInfo.IsFloat
                        ? list.Select(v => (PyObject)new PyFloat(v)).ToArray()
                        : list.Select(v => (PyObject)new PyInt((int)v)).ToArray();
                    using var pyList = new PyList(pyItems);
                    kwargs[name] = pyList; // note: copies reference; pyList disposed at end of using scope
                }

                // Instantiate using kwargs
                PyObject instance;
                try
                {
                    using var emptyArgs = new PyTuple(new PyObject[] { });
                    instance = ((PyObject)cls).Invoke(emptyArgs, kwargs);
                }
                catch (PythonException pex)
                {
                    Console.WriteLine($"Python instantiation failed: {pex.MyFormat()}");
                    throw pex;
                }

                // Extract vertices and faces and convert to triangle float[] (zxy -> xyz mapping)
                try
                {
                    dynamic pyVertices = instance.GetAttr("vertices");
                    dynamic pyFaces = instance.GetAttr("faces");

                    var vertices = ConvertToVertexListLocal(pyVertices);
                    var triangles = ConvertFacesToTrianglesLocal(vertices, pyFaces);
                    return triangles;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse vertices/faces: {ex.Message}");
                    throw;
                }
            }
        }

        // Helper method to get available classes from a module
        private static List<string> GetAvailableClasses(dynamic module)
        {
            var classes = new List<string>();
            try
            {
                dynamic moduleDict = module.__dict__;
                foreach (dynamic key in moduleDict.keys())
                {
                    string keyStr = key.ToString();
                    try
                    {
                        dynamic value = moduleDict[key];
                        // Check if it's a class by checking if it has __bases__ attribute or is callable
                        bool isClass = false;
                        try
                        {
                            if (value.__bases__ != null)
                            {
                                isClass = true;
                            }
                        }
                        catch
                        {
                            // Try alternative method: check if it's callable and has __name__
                            try
                            {
                                if (value.__name__ != null && value.__call__ != null)
                                {
                                    isClass = true;
                                }
                            }
                            catch
                            {
                                // Skip if we can't determine
                            }
                        }

                        if (isClass)
                        {
                            classes.Add(keyStr);
                        }
                    }
                    catch
                    {
                        // Skip if we can't check the value
                    }
                }
            }
            catch
            {
                // Return empty list if we can't inspect the module
            }
            return classes;
        }

        private static List<Tuple<float, float, float>> ConvertToVertexListLocal(dynamic pyVertices)
        {
            int len = pyVertices.shape[0].As<int>();
            var vertices = new List<Tuple<float, float, float>>(len);
            dynamic shape = pyVertices.shape;
            for (var i = 0; i < shape[0].As<int>(); ++i)
            {
                // zxy -> xyz
                vertices.Add(Tuple.Create(
                    pyVertices.GetItem(i).GetItem(2).As<float>(),
                    pyVertices.GetItem(i).GetItem(0).As<float>(),
                    pyVertices.GetItem(i).GetItem(1).As<float>()));
            }
            return vertices;
        }

        private static float[] ConvertFacesToTrianglesLocal(List<Tuple<float, float, float>> vertices, dynamic pyFaces)
        {
            var toRender = new List<float>();
            var faces = new List<Tuple<int, int, int>>();
            dynamic faceShape = pyFaces.shape;
            for (var i = 0; i < faceShape[0].As<int>(); ++i)
            {
                // zxy -> xyz index order
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
    }
}
