using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Annotator.RenderTypes;
using CycleGUI;
using CycleGUI.API;
using Newtonsoft.Json.Linq;
using Python.Runtime;
using System.Diagnostics;

namespace Annotator
{
    internal partial class PanelConstructors
    {
        static PanelConstructors()
        {
            MergedParameter = LoadMergedParameters("merged_parameters.json");

            try
            {
                var script = Path.GetFullPath("copy_predicted_pkl.py");
                if (File.Exists(script))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{script}\"",
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout);
                        if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine(stderr);
                    }
                }
                else
                {
                    Console.WriteLine($"copy_predicted_pkl.py not found at: {script}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to run copy_predicted_pkl.py: {ex.Message}");
            }
        }

        private static string objectFolderSelected;

        public static string ObjectIdStr = "";

        public static readonly Dictionary<string, Dictionary<string, Dictionary<string, ParameterInfo>>> MergedParameter;

        // Load merged_parameters.json and convert to Dictionary<object_name, Dictionary<concept_name, Dictionary<param_name, ParameterInfo>>>>
        private static Dictionary<string, Dictionary<string, Dictionary<string, ParameterInfo>>>
            LoadMergedParameters(string mergedJsonPath)
        {
            if (string.IsNullOrWhiteSpace(mergedJsonPath)) throw new ArgumentException("mergedJsonPath is null or empty", nameof(mergedJsonPath));
            if (!File.Exists(mergedJsonPath)) throw new FileNotFoundException($"merged_parameters.json not found: {mergedJsonPath}", mergedJsonPath);
            var result = new Dictionary<string, Dictionary<string, Dictionary<string, ParameterInfo>>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var text = File.ReadAllText(mergedJsonPath, Encoding.UTF8);
                var root = JToken.Parse(text) as JArray;
                if (root == null) return result;

                foreach (var categoryNode in root)
                {
                    var objectName = categoryNode["object_name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(objectName)) continue;
                    var conceptsArr = categoryNode["concepts"] as JArray;
                    if (conceptsArr == null) continue;

                    var conceptMap = new Dictionary<string, Dictionary<string, ParameterInfo>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var conceptNode in conceptsArr)
                    {
                        var conceptName = conceptNode["concept_name"]?.ToString() ?? string.Empty;
                        var def = new Dictionary<string, ParameterInfo>(StringComparer.OrdinalIgnoreCase);

                        // classification parameters
                        var classificationObj = conceptNode["classification"] as JObject;
                        var classificationInfo = conceptNode["classification_info"] as JObject;
                        if (classificationObj != null)
                        {
                            foreach (var prop in classificationObj.Properties())
                            {
                                var p = new ParameterInfo
                                {
                                    IsFloat = false,
                                    Len = -1
                                };
                                // range from classification_info if provided
                                var infoForParam = classificationInfo?[prop.Name] as JObject;
                                if (infoForParam != null)
                                {
                                    if (infoForParam["min_value"] != null) p.Min = infoForParam["min_value"].Value<int>();
                                    if (infoForParam["max_value"] != null) p.Max = infoForParam["max_value"].Value<int>();
                                }
                                else { p.Min = -1; p.Max = -1; }
                                // set Len from value of classification map (e.g., "num_levels": 1)
                                try { if (prop.Value != null) p.Len = prop.Value.Value<int>(); } catch { p.Len = -1; }
                                def[prop.Name] = p;
                            }
                        }

                        // regression parameters
                        var regressionObj = conceptNode["regression"] as JObject;
                        if (regressionObj != null)
                        {
                            foreach (var prop in regressionObj.Properties())
                            {
                                var p = new ParameterInfo
                                {
                                    IsFloat = true,
                                    Min = -1,
                                    Max = -1,
                                    Len = -1
                                };
                                // set Len from value (e.g., "outer_size": 3)
                                try { if (prop.Value != null) p.Len = prop.Value.Value<int>(); } catch { p.Len = -1; }
                                def[prop.Name] = p;
                            }
                        }

                        conceptMap[conceptName] = def;
                    }

                    result[objectName] = conceptMap;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse merged_parameters.json: {ex.Message}");
                throw;
            }

            return result;
        }

        public static bool KioskMode = false;

        private static bool allTransparentButEditing = true;

        public static void ToggleTransparency()
        {
            if (allTransparentButEditing)
            {
                new SetObjectApperance()
                {
                    namePattern = "*",
                    transparency = 0.7f
                }.IssueToDefault();
                new SetObjectApperance()
                {
                    namePattern = $"*{SelectedTemplate.Name}*",
                    transparency = 0
                }.IssueToDefault();
            }
            else
            {
                new SetObjectApperance()
                {
                    namePattern = "*",
                    transparency = 0.0f
                }.IssueToDefault();
            }
        }

        public static List<ConceptTemplate> TemplateObjects = new ();

        public static ConceptTemplate SelectedTemplate = null;

        // Read a .pkl file via Python and print its JSON (indented) to backend console
        public static string ParsePklAsJson(string pklPath)
        {
            if (string.IsNullOrWhiteSpace(pklPath)) throw new ArgumentException("pklPath is null or empty", nameof(pklPath));
            var fullPath = Path.GetFullPath(pklPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException($".pkl file not found: {fullPath}", fullPath);

            using (Py.GIL())
            {
                // Define a robust converter in __main__ to handle numpy types and common containers
                PythonEngine.RunSimpleString(@"
import numpy as np

def to_builtin(o):
    import numpy as np
    if isinstance(o, (str, int, float, bool)) or o is None:
        return o
    if isinstance(o, np.integer):
        return int(o)
    if isinstance(o, np.floating):
        return float(o)
    if isinstance(o, np.ndarray):
        return o.tolist()
    if isinstance(o, (list, tuple, set)):
        return [to_builtin(x) for x in o]
    if isinstance(o, dict):
        return {str(to_builtin(k)): to_builtin(v) for k, v in o.items()}
    try:
        return o.__dict__
    except Exception:
        return str(o)

");

                dynamic builtins = Py.Import("builtins");
                dynamic pickle = Py.Import("pickle");
                dynamic json = Py.Import("json");
                dynamic main = Py.Import("__main__");

                using var pyPath = new PyString(fullPath);
                dynamic f = builtins.open(pyPath, "rb");
                dynamic obj = pickle.load(f);
                f.close();

                dynamic to_builtin = main.GetAttr("to_builtin");
                dynamic conv_obj = to_builtin(obj);

                using var kwargs = new PyDict();
                kwargs["indent"] = new PyInt(2);
                // use 0/1 for boolean flags to avoid PyBool dependency
                kwargs["ensure_ascii"] = new PyInt(0);
                PyObject jsonStrObj = ((PyObject)json.GetAttr("dumps")).Invoke(new PyObject[] { (PyObject)conv_obj }, kwargs);
                var jsonStr = jsonStrObj.As<string>();

                // File.WriteAllText("test_parse_pkl.json", jsonStr);
                return jsonStr;
            }
        }
    }
}
