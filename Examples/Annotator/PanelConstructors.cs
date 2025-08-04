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

namespace Annotator
{
    internal class PanelConstructors
    {
        public static PanelBuilder.CycleGUIHandler DefineTargetObjectPanel()
        {
            string[] RetrieveObjects(string path)
            {
                string[] objectFolders = Directory.Exists(path)
                    ? Directory.GetDirectories(path)
                        .Select(Path.GetFileName)
                        .Where(folderName => folderName != "__pycache__") // Exclude "__pycache__"
                        .ToArray()
                    : new[] { "No objects available" };

                return objectFolders;
            }

            var annotatorDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Objects"));
            var objectFolders = RetrieveObjects(annotatorDirectory);

            var colors = new List<Color>() { Color.CornflowerBlue, Color.FromArgb(255, 100, 149, 237), Color.FromArgb(255, 95, 158, 160), Color.FromArgb(255, 176, 196, 222), Color.FromArgb(255, 255, 182, 193), Color.FromArgb(255, 50, 205, 50), Color.FromArgb(255, 238, 130, 238), Color.FromArgb(255, 64, 224, 208), Color.DarkRed, Color.DarkGray };
            var cc = colors[0];
            BasicRender targetObject = new($"target_{objectFolders[0]}", cc.R, cc.G, cc.B);
            (targetObject.Mesh, _, _) = AnnotatorHelper.DemoObjectFromPython(objectFolders[0], 0);

            MeshUpdater.UpdateMesh(targetObject, true, -AnnotatorHelper.DemoObjectMinZ);
            targetObject.Shown = true;
            var initSelectable = false;

            float r = 100, g = 149, b = 237;

            return pb =>
            {
                pb.SeparatorText("Target Object");

                // var colorChanged = pb.DragFloat("R", ref r, 1, 0, 255);
                // colorChanged |= pb.DragFloat("G", ref g, 1, 0, 255);
                // colorChanged |= pb.DragFloat("B", ref b, 1, 0, 255);
                //
                // if (colorChanged)
                // {
                //     targetObject.ColorR = r;
                //     targetObject.ColorG = g;
                //     targetObject.ColorB = b;
                //     MeshUpdater.UpdateMesh(targetObject, false, -AnnotatorHelper.DemoObjectMinZ);
                // }

                pb.DropdownBox("Category", objectFolders, ref objectFolderSelectedIndex);
                objectFolderSelected = objectFolders[objectFolderSelectedIndex];

                var objectsDropDown = new[] { "0", "1", "2" };
                var objectDropdownItems = objectsDropDown
                    .Select((obj, index) => $"<I:{objectTypeSelectedIndex}{index}>{obj}").ToArray();
                pb.DropdownBox("Object ID", objectDropdownItems, ref objectTypeSelectedIndex);

                if (!initSelectable && Program.DefaultAction != null)
                {
                    Program.DefaultAction.SetObjectSelectable(targetObject.Name);
                    initSelectable = true;
                }

                pb.SeparatorText("Template Instances");

                pb.Table("template_objects_table", ["Name", "Actions"], TemplateObjects.Count, (row, i) =>
                {
                    row.Label(TemplateObjects[i].Name);
                    row.ButtonGroup([IconFonts.ForkAwesome.Pencil, IconFonts.ForkAwesome.Trash], ["Edit", "Delete"]);
                });

                pb.Panel.Repaint();
            };
        }

        private static dynamic conceptualAssembly = null;

        private const bool ProcessThumbNail = false;

        public static PanelBuilder.CycleGUIHandler DefineTemplateLibraryPanel()
        {
            #region read concept_examples

            var concepts = GetExample0ObjPaths("concept_examples");

            void GenJpg(byte[] rgb, int w, int h, string name)
            {
                using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                var rect = new Rectangle(0, 0, w, h);
                var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                IntPtr ptr = bmpData.Scan0;
                for (var i = 0; i < h; i++)
                    Marshal.Copy(rgb, w * i * 3, ptr + bmpData.Stride * i, w * 3);

                bitmap.UnlockBits(bmpData);

                bitmap.Save($"thumbnails\\{name}.jpg");
            }

            #endregion

            if (conceptualAssembly == null)
                conceptualAssembly =
                    AnnotatorHelper.InitializePythonAndAssembly(objectFolderSelected, objectTypeSelectedIndex);
            List<(string TemplateName, Dictionary<string, float[]> ParamList)> actualTemplates = GetTemplatesInfo(conceptualAssembly);

            List<(string TemplateName, Dictionary<string, float[]> ParamList)> virtualTemplates = concepts.Select(item =>
            {
                Dictionary<string, float[]> dict = null;
                if (actualTemplates.Any(tt => tt.TemplateName == item.TemplateName))
                {
                    var first = actualTemplates.First(tt => tt.TemplateName == item.TemplateName);
                    dict = first.ParamList;
                }
                return (item.TemplateName, dict);
            }).ToList();

            var displayItems = virtualTemplates.ToArray();

            foreach (var template in virtualTemplates)
            {
                if (File.Exists($"thumbnails\\{template.TemplateName}.jpg"))
                    LoadImage($"thumbnails\\{template.TemplateName}.jpg", $"rgb_{template.TemplateName}");
                else LoadImage("thumbnails\\av0.jpg", $"rgb_{template.TemplateName}");
            }

            var searchMode = 0;
            var searchIgnoreCase = true;

            var lastName = "";

            return pb =>
            {
                pb.Label($"{IconFonts.ForkAwesome.Search} Search Options");
                pb.SameLine(45);
                pb.RadioButtons("search_mode", ["Substring", "Regular Expression"], ref searchMode, true);
                pb.SameLine(45);
                pb.CheckBox("Ignore Case", ref searchIgnoreCase);

                var (searchStr, doneInput) =
                    pb.TextInput("template_search", hidePrompt: true, alwaysReturnString: true);
                if (searchStr != "")
                {
                    if (searchMode == 0)
                        displayItems = virtualTemplates.Where(info =>
                        {
                            var ttName = info.TemplateName;
                            if (searchIgnoreCase) ttName = ttName.ToLower();
                            return ttName.Contains(searchIgnoreCase ? searchStr.ToLower() : searchStr);
                        }).ToArray();
                    // else
                }
                else displayItems = virtualTemplates.ToArray();

                var clickIndex = pb.ImageList("Templates",
                    displayItems.Select(info => ($"rgb_{info.TemplateName}", info.TemplateName, info.TemplateName))
                        .ToArray(), hideSeparator: true);

                if (ProcessThumbNail)
                {
                    if (pb.Button("capture"))
                    {
                        new CaptureRenderedViewport()
                        {
                            callback = (img =>
                            {
                                GenJpg(img.bytes, img.width, img.height, lastName);
                            })
                        }.IssueToDefault();
                    }

                    if (pb.Button("reset cam"))
                    {
                        new SetCamera()
                        {
                            altitude = (float)(Math.PI / 6),
                            azimuth = (float)(-Math.PI / 3),
                            lookAt = Vector3.Zero,
                            distance = 6
                        }.IssueToAllTerminals();
                    }
                }

                if (clickIndex != -1)
                {
                    var tName = displayItems[clickIndex].TemplateName;
                    if (ProcessThumbNail)
                    {
                        lastName = displayItems[clickIndex].TemplateName;
                        var path = concepts.First(cc =>
                            cc.TemplateName == displayItems[clickIndex].TemplateName).FullPath;
                        Console.WriteLine(path);
                        Workspace.AddProp(new LoadModel()
                        {
                            detail = new Workspace.ModelDetail(File.ReadAllBytes(path))
                            {
                                Center = new Vector3(0, 0, 0),
                                Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                                Scale = 1f,
                            },
                            name = "stork"
                        });

                        Workspace.AddProp(new PutModelObject()
                        {
                            clsName = "stork",
                            name = "template_stork",
                            // newPosition = new Vector3(model3dX, model3dY, 2)
                        });
                        Program.DefaultAction.SetObjectSelectable("template_stork");
                        return;
                    }

                    Console.WriteLine($"adding: {tName}");
                    var (_, paramDict) =
                        virtualTemplates.FirstOrDefault(t => t.TemplateName == tName);

                    var ss = GetParamJsonObjects("concept_examples", tName)[0];
                    paramDict.Clear();
                    foreach (var kvp in ss.Parameters)
                    {
                        paramDict[kvp.Key] = kvp.Value.Select(t => (float)t).ToArray();
                    }

                    var c = Color.Orange;
                    var render = new GlobeConceptTemplates($"{tName}", $"template_{tName}",
                        paramDict, c.R, c.G, c.B);

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
                        render.Mesh = AnnotatorHelper.GetSingleComponentMesh(conceptualAssembly, tName);
                    }

                    MeshUpdater.UpdateMesh(render, true);
                    Program.DefaultAction.SetObjectSelectable(render.Name);

                    lock (TemplateObjects) TemplateObjects.Add(render);
                    lock (TemplateObjects) SelectedTemplate = render;

                    ToggleTransparency();
                }
            };
        }

        public static PanelBuilder.CycleGUIHandler DefineTemplateObjectEditor()
        {
            int dragStepId = 0;

            return pb =>
            {
                pb.CheckBox("Kiosk Mode", ref KioskMode);

                foreach (var render in TemplateObjects)
                {
                    if (!render.NeedsUpdateFlag) continue;
                    render.NeedsUpdateFlag = false;

                    var templateName = render.TemplateName;
                    templateName = templateName.Substring(templateName.IndexOf("_") + 1);

                    using (Py.GIL())
                    {
                        dynamic pyDict = new PyDict();
                        foreach (var param in render.Parameters)
                        {
                            if (param.Key.ToLower().Contains("rotation")) continue;
                            var arr = param.Value.Select(v => (PyObject)new PyFloat(v)).ToArray();
                            pyDict[param.Key] = new PyList(arr);
                        }
                    
                        conceptualAssembly.update_template_parameters(templateName, pyDict);
                    }
                    
                    render.Mesh = AnnotatorHelper.GetSingleComponentMesh(conceptualAssembly, templateName);
                    MeshUpdater.UpdateMesh(render, false, zBias: 0f);
                    render.Update();
                }

                GlobeConceptTemplates templateObject = null;

                lock (TemplateObjects)
                {
                    if (SelectedTemplate == null)
                    {
                        pb.Label("No template object selected yet.");
                        pb.Panel.Repaint();
                        return;
                    }
                    templateObject = SelectedTemplate;
                }

                pb.Label($"Selected: {templateObject.Name}");
                pb.Label("Drag Step");
                pb.SameLine(25);
                pb.RadioButtons("Drag step", ["0.01", "0.001"], ref dragStepId, true);
                float step = dragStepId == 0 ? 0.01f : 0.001f;
                if (pb.CheckBox("All transparent but editing", ref allTransparentButEditing))
                {
                    ToggleTransparency();
                }

                foreach (var kv in templateObject.Parameters)
                {
                    var key = kv.Key;
                    var arr = kv.Value;

                    // Skip keys that are related to position or rotation
                    if (key.Contains("position", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("rotation", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    for (int i = 0; i < arr.Length; i++)
                    {
                        float val = arr[i];

                        // Check if the key starts with "is_"
                        if (key.StartsWith("is_", StringComparison.OrdinalIgnoreCase))
                        {
                            // Treat the value as a boolean (0 or 1)
                            bool toggleValue = Math.Abs(val) > 0.5f; // Convert 0/1 to false/true

                            if (pb.Toggle($"{key}[{i}]", ref toggleValue))
                            {
                                // Console.WriteLine($"Updating parameter {Name} {key}[{i}] from {val} to {(toggleValue ? 1.0f : 0.0f)}");
                                // Update the array with the toggle value (convert back to float)
                                arr[i] = toggleValue ? 1.0f : 0.0f;
                            }
                        }
                        else
                        {
                            // Identify keys that require integer values
                            bool requiresInteger = key.Contains("num", StringComparison.OrdinalIgnoreCase);

                            // Determine the step size and minimum value
                            step = requiresInteger ? 1.0f : step;
                            float minValue = requiresInteger ? 0.0f : float.MinValue; // Minimum for integers is 0
                            float maxValue = float.MaxValue;
                            // Special constraints for "num_levels" and "number_of_legs"
                            if (key.Equals("num_levels", StringComparison.OrdinalIgnoreCase) ||
                                key.Equals("number_of_legs", StringComparison.OrdinalIgnoreCase))
                            {
                                minValue = 1.0f;
                                maxValue = 4.0f;
                            }
                            // Use DragFloat with the appropriate step size and value constraints
                            if (pb.DragFloat($"{key}[{i}]", ref val, step, minValue, maxValue))
                            {
                                // Ensure constraints are applied correctly
                                val = requiresInteger ? (float)Math.Round(val) : val;

                                // Apply clamping for strict value constraints
                                if (key.Equals("num_levels", StringComparison.OrdinalIgnoreCase) ||
                                    key.Equals("number_of_legs", StringComparison.OrdinalIgnoreCase))
                                {
                                    val = Math.Clamp(val, 1.0f, 4.0f);
                                }

                                // Console.WriteLine($"Updating parameter {Name} {key}[{i}] from {arr[i]} to {val}");
                                arr[i] = val;
                                templateObject.NeedsUpdateFlag = true;
                            }
                        }
                    }
                }

                pb.Panel.Repaint();
            };
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

        private static string objectFolderSelected = "";
        private static int objectFolderSelectedIndex = 0;
        private static int objectTypeSelectedIndex = 0;

        public static List<GlobeConceptTemplates> TemplateObjects = new ();

        public static GlobeConceptTemplates SelectedTemplate = null;

        private static List<(string templateName, Dictionary<string, float[]>)> GetTemplatesInfo(dynamic conceptualAssembly)
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

        private static void LoadImage(string name, string rgbname)
        {
            Bitmap bmp = new Bitmap(name);

            // 水平翻转 bmp
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipX); // todo: CycleGui bug

            // Convert to a standard 32-bit RGBA format to ensure consistent handling
            Bitmap standardBmp = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(standardBmp))
            {
                g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
            }
            bmp.Dispose();

            int width = standardBmp.Width;
            int height = standardBmp.Height;
            Rectangle rect = new Rectangle(0, 0, width, height);
            System.Drawing.Imaging.BitmapData bmpData =
                standardBmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, standardBmp.PixelFormat);
            nint ptr = bmpData.Scan0;

            // Calculate the actual bytes needed for RGBA (4 bytes per pixel)
            int pixelCount = width * height;
            int rgbaBytes = pixelCount * 4;
            byte[] bgrValues = new byte[Math.Abs(bmpData.Stride) * height];
            Marshal.Copy(ptr, bgrValues, 0, bgrValues.Length);
            int stride = Math.Abs(bmpData.Stride);
            standardBmp.UnlockBits(bmpData);
            standardBmp.Dispose();

            byte[] rgba = new byte[rgbaBytes];
            int bytesPerPixel = 4; // Format32bppArgb uses 4 bytes per pixel

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIndex = y * stride + x * bytesPerPixel;
                    int dstIndex = (y * width + x) * 4;

                    // Convert BGRA to RGBA
                    rgba[dstIndex] = bgrValues[srcIndex + 2];     // Red
                    rgba[dstIndex + 1] = bgrValues[srcIndex + 1]; // Green 
                    rgba[dstIndex + 2] = bgrValues[srcIndex + 0]; // Blue
                    rgba[dstIndex + 3] = bgrValues[srcIndex + 3]; // Alpha
                }
            }

            Workspace.AddProp(new PutARGB()
            {
                height = height,
                width = width,
                name = rgbname,
                requestRGBA = () => rgba
            });
        }

        private static List<(string CategoryName, string TemplateName, string FullPath)> GetExample0ObjPaths(string conceptExamplesRoot)
        {
            var result = new List<(string, string, string)>();

            if (!Directory.Exists(conceptExamplesRoot))
                throw new DirectoryNotFoundException($"Directory not found: {conceptExamplesRoot}");

            // 遍历物体文件夹
            foreach (var objectDir in Directory.GetDirectories(conceptExamplesRoot))
            {
                var objectName = Path.GetFileName(objectDir);

                // 遍历子物体文件夹
                foreach (var subObjectDir in Directory.GetDirectories(objectDir))
                {
                    var subObjectName = Path.GetFileName(subObjectDir);
                    var example0Dir = Path.Combine(subObjectDir, "example_0");

                    if (Directory.Exists(example0Dir))
                    {
                        var objFiles = Directory.GetFiles(example0Dir, "*.glb");
                        if (objFiles.Length > 0)
                        {
                            result.Add((objectName, $"{subObjectName}", objFiles[0]));
                        }
                    }
                }
            }

            return result;
        }

        public static List<(string CategoryName, string TemplateName, string FullPath, JObject Parameters)> GetParamJsonObjects(string conceptExamplesRoot)
        {
            var result = new List<(string, string, string, JObject)>();

            if (!Directory.Exists(conceptExamplesRoot))
                throw new DirectoryNotFoundException($"Directory not found: {conceptExamplesRoot}");

            // 遍历物体文件夹
            foreach (var objectDir in Directory.GetDirectories(conceptExamplesRoot))
            {
                var objectName = Path.GetFileName(objectDir);

                // 遍历子物体文件夹
                foreach (var subObjectDir in Directory.GetDirectories(objectDir))
                {
                    var subObjectName = Path.GetFileName(subObjectDir);
                    
                    // 只查找example_0文件夹中的param.json文件
                    var example0Dir = Path.Combine(subObjectDir, "example_0");
                    if (Directory.Exists(example0Dir))
                    {
                        var paramFile = Path.Combine(example0Dir, "param.json");
                        if (File.Exists(paramFile))
                        {
                            try
                            {
                                // 读取JSON文件
                                var jsonContent = File.ReadAllText(paramFile);
                                dynamic jsonObject = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonContent);
                                
                                // 检查是否存在template字段，且值等于子物体名
                                foreach (var concept in jsonObject.conceptualization)
                                {
                                    if (concept.template == subObjectName)
                                        result.Add((objectName, subObjectName, paramFile, concept.parameters));
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error reading param.json file {paramFile}: {ex.Message}");
                            }
                        }
                    }
                }
            }

            return result;
        }

        // 重载版本：只返回匹配特定template值的对象
        public static List<(string CategoryName, string TemplateName, string FullPath, JObject Parameters)> GetParamJsonObjects(string conceptExamplesRoot, string templateValue)
        {
            var allObjects = GetParamJsonObjects(conceptExamplesRoot);
            return allObjects.Where(obj => obj.TemplateName == templateValue).ToList();
        }
    }
}
