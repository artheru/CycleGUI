using CycleGUI;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Annotator.RenderTypes;
using CycleGUI.API;
using Python.Runtime;
using System.IO;
using System.Text.RegularExpressions;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Annotator
{
    internal partial class PanelConstructors
    {
        public static PanelBuilder.CycleGUIHandler DefineTemplateLibraryPanel()
        {
            // Scan Objects/*/concept_template.py and collect all class names
            var objectsRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Objects"));
            var templateClassesByCategory = new Dictionary<string, List<string>>();
            if (Directory.Exists(objectsRoot))
            {
                foreach (var categoryDir in Directory.GetDirectories(objectsRoot))
                {
                    var categoryName = Path.GetFileName(categoryDir);
                    var templatePy = Path.Combine(categoryDir, "concept_template.py");
                    var classNames = new List<string>();
                    if (File.Exists(templatePy))
                    {
                        try
                        {
                            var content = File.ReadAllText(templatePy);
                            var matches = Regex.Matches(content, @"^\s*class\s+([A-Za-z_]\w*)", RegexOptions.Multiline);
                            foreach (Match m in matches)
                                classNames.Add(m.Groups[1].Value);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading {templatePy}: {ex.Message}");
                        }
                    }
                    templateClassesByCategory[categoryName] = classNames;
                }
            }

            void GenJpg(byte[] rgb, int w, int h, string fn)
            {
                using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                var rect = new Rectangle(0, 0, w, h);
                var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                IntPtr ptr = bmpData.Scan0;
                for (var i = 0; i < h; i++)
                    Marshal.Copy(rgb, w * i * 3, ptr + bmpData.Stride * i, w * 3);

                bitmap.UnlockBits(bmpData);
                bitmap.Save(fn);
            }

            var allTemplateItems = new List<TemplateInfo>();

            var offScreenViewport = GUI.PromptWorkspaceViewport(panel =>
            {
                panel.ShowTitle("OffScreen Rendering");
                panel.FixSize(250, 160);
            });

            new SetAppearance()
            {
                drawGuizmo = false
            }.IssueToTerminal(offScreenViewport);

            new SetCamera()
            {
                altitude = (float)(Math.PI / 6),
                azimuth = (float)(-Math.PI / 3),
                lookAt = Vector3.Zero,
                distance = 3
            }.IssueToTerminal(offScreenViewport);

            new SetWorkspacePropDisplayMode()
            {
                mode = SetWorkspacePropDisplayMode.PropDisplayMode.NoneButSpecified,
                namePattern = "offscreen:*"
            }.IssueToTerminal(offScreenViewport);

            offScreenViewport.UseOffscreenRender(true);

            new Thread(async () =>
            {
                // placeholder picture
                foreach (var kvp in templateClassesByCategory)
                {
                    foreach (var templateClassName in kvp.Value)
                    {
                        var item = new TemplateInfo(kvp.Key, templateClassName);
                        allTemplateItems.Add(item);
                        var fn = $"thumbnails\\{item.RgbName}.jpg";
                        LoadImage(File.Exists(fn) ? fn : $"placeholder.jpg", item.RgbName);
                    }
                }

                Thread.Sleep(2000);

                // off screen rendering
                foreach (var item in allTemplateItems)
                {
                    var fn = $"thumbnails\\{item.RgbName}.jpg";
                    if (!File.Exists(fn))
                    {
                        WorkspaceProp.RemoveNamePattern("offscreen:*");

                        var templateObj =
                            new ConceptTemplate(item.Category, item.ClassName, Color.Orange, true, "");
                        MeshUpdater.UpdateMesh(templateObj, true);

                        var p = new TaskCompletionSource<int>();

                        new CaptureRenderedViewport()
                        {
                            callback = img =>
                            {
                                GenJpg(img.bytes, img.width, img.height, fn);
                                LoadImage(fn, item.RgbName);
                                p.SetResult(1);
                            }
                        }.IssueToTerminal(offScreenViewport);

                        await p.Task;
                    }
                    // else LoadImage(fn, item.RgbName);
                }
            }).Start();

            var searchMode = 0;
            var searchIgnoreCase = true;

            var lastName = "";

            return pb =>
            {
                pb.Label($"{IconFonts.ForkAwesome.Search} Conceptual Templates Search Options");
                pb.SameLine(45);
                pb.RadioButtons("search_mode", ["Substring", "Regular Expression"], ref searchMode, true);
                pb.SameLine(45);
                pb.CheckBox("Ignore Case", ref searchIgnoreCase);

                var displayItems = allTemplateItems.Where(info => info.Category == objectFolderSelected).ToList();

                var (searchStr, _) =
                    pb.TextInput("template_search", hidePrompt: true, alwaysReturnString: true);
                if (searchStr != "")
                {
                    if (searchMode == 0)
                        displayItems = allTemplateItems.Where(info =>
                        {
                            var ttName = info.ClassName;
                            if (searchIgnoreCase) ttName = ttName.ToLower();
                            return ttName.Contains(searchIgnoreCase ? searchStr.ToLower() : searchStr);
                        }).ToList();
                }

                var clickIndex = pb.ImageList("Templates",
                    displayItems.Select(info => (info.RgbName, info.ClassName, info.ClassName)).ToArray(),
                    hideSeparator: true);

                if (clickIndex != -1)
                {
                    try
                    {
                        var templateInfo = displayItems[clickIndex];

                        var templateObj =
                            new ConceptTemplate(templateInfo.Category, templateInfo.ClassName, Color.Orange, false, ObjectIdStr);
                        MeshUpdater.UpdateMesh(templateObj, true);

                        lock (TemplateObjects)
                        {
                            SelectedTemplate = templateObj;
                            TemplateObjects.Add(templateObj);
                        }

                        Program.DefaultAction.SetObjectSelectable(templateObj.Name);
                        ToggleTransparency();
                    }
                    catch (Exception ex)
                    {
                        UITools.RichText("Error", ex.MyFormat());
                    }
                }

                pb.Panel.Repaint();
            };
        }

        private static void LoadImage(string name, string rgbname)
        {
            Bitmap bmp = new Bitmap(name);

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

        public class TemplateInfo
        {
            public TemplateInfo(string category, string className)
            {
                Category = category;
                ClassName = className;
                RgbName = $"{category}-{className}";
            }

            public string Category; // e.g. Bottle
            public string ClassName; // e.g. Multilevel_Body
            public string RgbName; // e.g. Bottle-Multilevel_Body
        }

        // Data structures for merged_parameters.json
        public class ParameterInfo
        {
            public bool IsFloat; // regression -> true, classification -> false
            public int Min; // when IsFloat==true, set to -1
            public int Max; // when IsFloat==true, set to -1
            public int Len; // parameter length defined in merged_parameters.json, -1 if unknown
        }
    }
}
