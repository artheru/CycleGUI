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

        public static bool ViewAnnotatedResult;

        public static void SaveLabelResultToPkl()
        {
            if (string.IsNullOrWhiteSpace(objectFolderSelected) || string.IsNullOrWhiteSpace(ObjectIdStr))
            {
                Console.WriteLine("Error: objectFolderSelected or ObjectIdStr is empty");
                return;
            }

            try
            {
                // 去掉ObjectIdStr中的文件后缀名
                var cleanObjectId = Path.GetFileNameWithoutExtension(ObjectIdStr);
                
                // 1. 创建新的JSON内容
                var newEntry = CreateNewEntryJson(cleanObjectId);
                
                // 2. 检查目标pkl文件是否存在
                var targetDir = Path.Combine("Objects", objectFolderSelected, "annotated_pkl");
                Directory.CreateDirectory(targetDir);
                var pklPath = Path.Combine(targetDir, "annotated_conceptualization.pkl");
                
                List<object> allEntries;
                if (File.Exists(pklPath))
                {
                    // 3. 如果pkl存在，读取并转换为JSON
                    var existingJson = ParsePklAsJson(pklPath);
                    var existingData = JArray.Parse(existingJson);
                    allEntries = existingData.ToObject<List<object>>();
                    
                    // 检查是否已存在相同id的条目
                    var existingEntry = allEntries.FirstOrDefault(entry => 
                    {
                        var entryObj = JObject.FromObject(entry);
                        return entryObj["id"]?.ToString() == cleanObjectId;
                    });
                    
                    if (existingEntry != null)
                    {
                        // 4. 弹窗询问是否替换
                        var shouldReplace = UITools.Confirmation($"ID {cleanObjectId} already exists. Replace?", 
                            $"Entry with same ID");
                        
                        if (!shouldReplace)
                        {
                            Console.WriteLine("用户取消保存操作");
                            return;
                        }
                        
                        // 移除现有条目
                        allEntries.RemoveAll(entry => 
                        {
                            var entryObj = JObject.FromObject(entry);
                            return entryObj["id"]?.ToString() == cleanObjectId;
                        });
                    }
                }
                else
                {
                    allEntries = new List<object>();
                }
                
                // 添加新条目
                allEntries.Add(newEntry);
                
                // 5. 将整个JSON保存为pkl
                SaveJsonAsPkl(allEntries, pklPath);
                
                UITools.Alert($"Saved to: {pklPath}", "Notification");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存失败: {ex.Message}");
            }
        }

        public static bool LoadAnnotatedResult()
        {
            if (string.IsNullOrWhiteSpace(objectFolderSelected) || string.IsNullOrWhiteSpace(ObjectIdStr))
            {
                Console.WriteLine("Error: objectFolderSelected or ObjectIdStr is empty");
                return false;
            }

            try
            {
                // 去掉ObjectIdStr中的文件后缀名
                var cleanObjectId = Path.GetFileNameWithoutExtension(ObjectIdStr);
                
                // 检查目标pkl文件是否存在
                var pklPath = Path.Combine("Objects", objectFolderSelected, "annotated_pkl", "annotated_conceptualization.pkl");
                
                if (!File.Exists(pklPath))
                {
                    Console.WriteLine($"Annotated PKL file not found: {pklPath}");
                    return false;
                }

                // 将pkl转化为json
                var jsonStr = ParsePklAsJson(pklPath);
                if (string.IsNullOrWhiteSpace(jsonStr))
                {
                    Console.WriteLine("Failed to parse PKL file to JSON");
                    return false;
                }

                // 解析JSON并查找匹配的ID
                var root = JToken.Parse(jsonStr);
                JToken matchedEntry = null;
                
                if (root is JArray arrRoot)
                {
                    foreach (var entry in arrRoot)
                    {
                        var entryId = entry["id"]?.ToString();
                        if (entryId == cleanObjectId)
                        {
                            matchedEntry = entry;
                            break;
                        }
                    }
                }
                else if (root is JObject objRoot)
                {
                    var entryId = objRoot["id"]?.ToString();
                    if (entryId == cleanObjectId)
                    {
                        matchedEntry = objRoot;
                    }
                }

                if (matchedEntry == null)
                {
                    Console.WriteLine($"No entry found with ID: {cleanObjectId}");
                    return false;
                }

                // 清空现有的TemplateObjects
                TemplateObjects.Clear();

                // 获取conceptualization数组
                var conceptualization = matchedEntry["conceptualization"] as JArray;
                if (conceptualization == null)
                {
                    Console.WriteLine("No conceptualization data found in entry");
                    return false;
                }

                // 为每个conceptualization项创建ConceptTemplate
                foreach (var conceptItem in conceptualization)
                {
                    var templateName = conceptItem["template"]?.ToString();
                    if (string.IsNullOrWhiteSpace(templateName))
                    {
                        Console.WriteLine("Template name is missing in conceptualization item");
                        continue;
                    }

                    // 创建ConceptTemplate实例
                    var template = new ConceptTemplate(objectFolderSelected, templateName, Color.Orange, false, ObjectIdStr);
                    
                    // 从JSON中提取参数并更新template
                    var parameters = conceptItem["parameters"] as JObject;
                    if (parameters != null)
                    {
                        // 更新位置
                        var position = parameters["position"] as JArray;
                        if (position != null && position.Count >= 3)
                        {
                            // 注意：JSON中存储的是[Y, Z, X]顺序，需要转换为[X, Y, Z]
                            template.Pos = new Vector3(
                                position[2].Value<float>(), // X
                                position[0].Value<float>(), // Y  
                                position[1].Value<float>()  // Z
                            );
                        }

                        // 更新旋转
                        var rotation = parameters["rotation"] as JArray;
                        if (rotation != null && rotation.Count >= 3)
                        {
                            // JSON中存储的是[ry, rz, rx]顺序，需要转换为弧度并创建四元数
                            var rx = rotation[2].Value<float>() * (float)(Math.PI / 180.0); // 转换为弧度
                            var ry = rotation[0].Value<float>() * (float)(Math.PI / 180.0);
                            var rz = rotation[1].Value<float>() * (float)(Math.PI / 180.0);
                            
                            // 创建四元数 (注意轴序映射)
                            var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, rx);
                            var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ry);
                            var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rz);
                            template.Rot = qz * qy * qx;
                        }

                        // 更新其他参数
                        var updatedParams = new List<(string ParamName, PanelConstructors.ParameterInfo ParamInfo, List<double> ParamVal)>();
                        
                        foreach (var param in template.Parameters)
                        {
                            var paramName = param.ParamName;
                            var paramValue = parameters[paramName];
                            
                            if (paramValue != null)
                            {
                                var newParamVals = new List<double>();
                                
                                if (paramValue is JArray paramArray)
                                {
                                    foreach (var val in paramArray)
                                    {
                                        if (val.Type == JTokenType.Integer || val.Type == JTokenType.Float)
                                        {
                                            newParamVals.Add(val.Value<double>());
                                        }
                                    }
                                }
                                else if (paramValue.Type == JTokenType.Integer || paramValue.Type == JTokenType.Float)
                                {
                                    newParamVals.Add(paramValue.Value<double>());
                                }
                                
                                if (newParamVals.Count > 0)
                                {
                                    updatedParams.Add((paramName, param.ParamInfo, newParamVals));
                                }
                                else
                                {
                                    updatedParams.Add(param); // 保持原值
                                }
                            }
                            else
                            {
                                updatedParams.Add(param); // 保持原值
                            }
                        }
                        
                        // 更新模板参数
                        template.UpdateParamsAndMesh(updatedParams);
                    }
                     
                    // 添加到TemplateObjects
                    MeshUpdater.UpdateMesh(template, true);
                    TemplateObjects.Add(template);
                    Program.DefaultAction.SetObjectSelectable(template.Name);
                }

                Console.WriteLine($"Successfully loaded {TemplateObjects.Count} templates from annotated PKL");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load annotated result: {ex.Message}");
                return false;
            }
        }

        public static bool AnnotationExists = false;

        private static object CreateNewEntryJson(string objectId)
        {
            var conceptualization = new List<object>();
            
            foreach (var template in TemplateObjects)
            {
                var conceptEntry = new Dictionary<string, object>
                {
                    ["template"] = template.TemplateName,
                    ["parameters"] = new Dictionary<string, object>()
                };
                
                // 添加position和rotation
                var parameters = (Dictionary<string, object>)conceptEntry["parameters"];
                parameters["position"] = new[] { template.Pos.Y, template.Pos.Z, template.Pos.X };
                
                // 将Quaternion转换为Euler角度
                var (rx, ry, rz) = QuaternionToEulerXYZ(template.Rot);
                rx = (float)(rx / Math.PI * 180f);
                ry = (float)(ry / Math.PI * 180f);
                rz = (float)(rz / Math.PI * 180f);
                parameters["rotation"] = new[] { ry, rz, rx };
                
                // 添加其他参数
                foreach (var param in template.Parameters)
                {
                    parameters[param.ParamName] = param.ParamVal.ToArray();
                }
                
                conceptualization.Add(conceptEntry);
            }
            
            return new Dictionary<string, object>
            {
                ["category"] = objectFolderSelected,
                ["id"] = objectId,
                ["type"] = "mesh",
                ["conceptualization"] = conceptualization
            };
        }

        private static void SaveJsonAsPkl(List<object> data, string pklPath)
        {
            using (Py.GIL())
            {
                dynamic pickle = Py.Import("pickle");
                dynamic json = Py.Import("json");
                dynamic builtins = Py.Import("builtins");
                
                // 将C#对象转换为Python对象
                var jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                dynamic jsonData = json.loads(jsonStr);
                
                // 保存为pkl
                using var pyPath = new PyString(pklPath);
                dynamic f = builtins.open(pyPath, "wb");
                pickle.dump(jsonData, f);
                f.close();
            }
        }

        private static (float rx, float ry, float rz) QuaternionToEulerXYZ(Quaternion q)
        {
            // Normalize
            q = Quaternion.Normalize(q);
            // Based on standard quaternion to XYZ intrinsic euler conversion
            // roll (x-axis rotation)
            double sinr_cosp = 2.0 * (q.W * q.X + q.Y * q.Z);
            double cosr_cosp = 1.0 - 2.0 * (q.X * q.X + q.Y * q.Y);
            double rx = Math.Atan2(sinr_cosp, cosr_cosp);

            // pitch (y-axis rotation)
            double sinp = 2.0 * (q.W * q.Y - q.Z * q.X);
            double ry;
            if (Math.Abs(sinp) >= 1.0)
                ry = Math.CopySign(Math.PI / 2.0, sinp);
            else
                ry = Math.Asin(sinp);

            // yaw (z-axis rotation)
            double siny_cosp = 2.0 * (q.W * q.Z + q.X * q.Y);
            double cosy_cosp = 1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z);
            double rz = Math.Atan2(siny_cosp, cosy_cosp);

            return ((float)rx, (float)ry, (float)rz);
        }

        public static float TargetObjectHeightBias = 0;

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

        public static bool AllTransparentButEditing = true;

        public static void ToggleTransparency()
        {
            if (AllTransparentButEditing)
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

                File.WriteAllText("test_parse_pkl.json", jsonStr);
                return jsonStr;
            }
        }
    }
}
