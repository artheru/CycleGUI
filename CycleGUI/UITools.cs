using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CycleGUI.API;
using IconFonts;

namespace CycleGUI
{
    public static class UITools
    {
        // Track image display panels by name
        private static ConcurrentDictionary<string, ImageShowPanel> imagePanels = new();

        private class ImageShowPanel
        {
            public Panel Panel;
            public PutRGBA PutARGB;
            public int Width;
            public int Height;
            public string Name;
        }


        /// <summary>
        /// Display a monochrome image with colormap in a named panel
        /// </summary>
        /// <param name="name">Panel name (will be prefixed with "imshow-")</param>
        /// <param name="monoData">Single-channel array (byte: 0-255, float: auto-normalized from min-max)</param>
        /// <param name="width">Image width</param>
        /// <param name="height">Image height</param>
        /// <param name="colorMap">Colormap to apply (default: Jet)</param>
        /// <param name="terminal">Target terminal (null for default)</param>
        public static void ImageShowMono<T>(string name, T[] monoData, int width, int height, 
            SoftwareBitmap.ColorMapType colorMap = SoftwareBitmap.ColorMapType.Jet, Terminal terminal = null, PanelBuilder.CycleGUIHandler h_aux=null,
            bool bringToFront=true)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name cannot be null or empty");

            // Convert to byte array based on type
            byte[] byteData;
            
            if (typeof(T) == typeof(byte))
            {
                byteData = monoData as byte[];
            }
            else if (typeof(T) == typeof(float))
            {
                float[] floatData = monoData as float[];
                
                // Find min and max
                float min = float.MaxValue;
                float max = float.MinValue;
                
                foreach (float val in floatData)
                {
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
                
                // Normalize to 0-255
                byteData = new byte[floatData.Length];
                float range = max - min;
                
                if (range > 0)
                {
                    for (int i = 0; i < floatData.Length; i++)
                    {
                        byteData[i] = (byte)((floatData[i] - min) / range * 255);
                    }
                }
            }
            else
            {
                throw new ArgumentException($"Unsupported type {typeof(T)}. Only byte and float are supported.");
            }

            string panelName = $"imshow-{name}";
            terminal ??= GUI.defaultTerminal;

            // Check if panel already exists
            if (imagePanels.TryGetValue(panelName, out var existingPanel))
            {
                // Check if dimensions match
                if (existingPanel.Width != width || existingPanel.Height != height)
                {
                    // Dimensions changed, remove old panel and create new one
                    existingPanel.PutARGB= new PutRGBA()
                    {
                        name = panelName,
                        width = width,
                        height = height
                    };
                    Workspace.AddProp(existingPanel.PutARGB);
                }
                // update the image
                byte[] argbData = SoftwareBitmap.ColorMap(byteData, width, height, colorMap);
                existingPanel.PutARGB.UpdateRGBA(argbData);
                if (bringToFront)
                    existingPanel.Panel?.BringToFront();
                return;
            }

            // Create new panel
            byte[] argbDataNew = SoftwareBitmap.ColorMap(byteData, width, height, colorMap);
            
            var putARGB = new PutRGBA()
            {
                name = panelName,
                width = width,
                height = height
            };
            
            Workspace.AddProp(putARGB);
            putARGB.UpdateRGBA(argbDataNew);


            var panel = GUI.DeclarePanel(terminal);
            var panelInfo = new ImageShowPanel
            {
                Panel = panel,
                PutARGB = putARGB,
                Width = width,
                Height = height,
                Name = panelName
            };

            imagePanels[panelName] = panelInfo;

            panel.Define(pb =>
            {
                pb.Panel.ShowTitle(panelName)
                    .InitSize(Math.Min(800, width), Math.Min(600, height))
                    .TopMost(false);

                // Allow panel to be closed
                if (pb.Closing())
                {
                    imagePanels.TryRemove(panelName, out _);
                    pb.Panel.Exit();
                    return;
                }

                h_aux?.Invoke(pb);

                // Display the image
                pb.Image("", panelName, -1);
            });
            if (bringToFront)
                panel.BringToFront();

            panel.OnTerminalQuit = () =>
            {
                imagePanels.TryRemove(panelName, out _);
            };
        }

        public static void Alert(string prompt, string title="Alert", Terminal t=null)
        {
            Console.WriteLine("alert:" + prompt);
            GUI.PromptAndWaitPanel(pb2 =>
            {
                pb2.Panel.TopMost(true).InitSize(320,0).AutoSize(true).ShowTitle(title).Modal(true);
                pb2.Label(prompt);
                if (pb2.Button("OK"))
                    pb2.Panel.Exit();
            }, t);
        }

        public static bool Input(string prompt, string defVal, out string val, string hint="", string title="Input Required", Terminal t=null)
        {
            var retTxt = "";
            var ret= GUI.WaitPanelResult<bool>(pb2 =>
            {
                pb2.Panel.TopMost(true).InitSize(320).ShowTitle(title).Modal(true);
                (retTxt, var done)= pb2.TextInput(prompt, defVal, hint, true);
                if (done)
                    pb2.Exit(true);
                pb2.Separator();
                if (pb2.ButtonGroup("", ["OK", "Cancel"], out var bid))
                {
                    if (bid == 0)
                        pb2.Exit(true);
                    else if (bid == 1)
                        pb2.Exit(false);
                }
            }, t);
            val = retTxt;
            return ret;
        }

        public static bool Confirmation(string prompt, string title = "Confirmation", Terminal t = null)
        {
            return GUI.WaitPanelResult<bool>(pb2 =>
            {
                pb2.Panel.TopMost(true).InitSize(320, 0).AutoSize(true).ShowTitle(title).Modal(true);
                pb2.Label(prompt);
                if (pb2.ButtonGroup("", ["OK", "Cancel"], out var bid))
                {
                    if (bid == 0) pb2.Exit(true);
                    else if (bid == 1) pb2.Exit(false);
                }
            });
        }

        //????
        public static void RichText(string title, string content, Terminal t = null)
        {
            GUI.PromptAndWaitPanel(pb =>
            {
                pb.Panel.TopMost(true)
                   .InitSize(600, 400)
                   .ShowTitle(title)
                   .Modal(true);

                // Add a scrollable text area for the content
                pb.SelectableText($"RichText-{title}", content);

                // Add a close button
                if (pb.Button("Close"))
                    pb.Panel.Exit();
            }, t);
        }

        public static bool FileBrowser(string title, out string filename, string defaultFileName = "",
            bool selectDir = false, Terminal t = null, string actionName = null)
        {
            string currentPath = Directory.GetCurrentDirectory();
            string inputPath = currentPath;
            List<string> directoryItems = [];
            int selectedIndex = -1;
            string fileInput = defaultFileName;
            string lastGoodPath = currentPath;
            filename = GUI.WaitPanelResult<string>(pb =>
            {
                pb.Panel.TopMost(true).InitSize(480).AutoSize(true).ShowTitle(title).Modal(true);

                // Check the operating system and display available drives if on Windows
                if (!currentPath.StartsWith("/"))
                {
                    string[] drives = Directory.GetLogicalDrives();
                    if (pb.ButtonGroup("Drives:", drives, out int driveSelected))
                    {
                        currentPath = drives[driveSelected];
                    }
                }

                // Upper text input for entering directory paths directly
                (inputPath, var donePathInput) =
                    pb.TextInput($"Current Directory", currentPath, "Enter directory path here...");
                if (donePathInput)
                {
                    if (Directory.Exists(inputPath))
                    {
                        currentPath = inputPath;
                    }
                    else
                    {
                        Alert("Invalid directory path.");
                    }
                }

                // Refresh directory and file list
                directoryItems.Clear();
                directoryItems.Add("Folder: .."); // Parent directory
                try
                {
                    directoryItems.AddRange(Directory.GetDirectories(currentPath)
                        .Select(d => $"{ForkAwesome.Folder} {Path.GetFileName(d)}"));
                    // Adding file items with ForkAwesome icons
                    if (!selectDir)
                        foreach (var file in Directory.GetFiles(currentPath))
                        {
                            string extension = Path.GetExtension(file).ToLower();
                            string icon = GetFileIcon(extension);
                            directoryItems.Add($"{icon} {Path.GetFileName(file)}");
                        }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Alert("Unauthorized access exception.");
                    currentPath = lastGoodPath;
                    pb.Panel.Repaint(true);
                    return;
                }

                lastGoodPath = currentPath;
                // Listbox showing the current directory contents
                selectedIndex = pb.ListBox("Contents:", directoryItems.ToArray(), 10);

                // Navigate to the selected directory or select a file name
                if (selectedIndex == 0)
                {
                    currentPath = Directory.GetParent(currentPath)?.FullName ?? currentPath;
                    pb.Panel.Repaint(true);
                }
                else if (selectedIndex > 0 && selectedIndex <= Directory.GetDirectories(currentPath).Length)
                {
                    var selDir = Path.GetFileName(Directory.GetDirectories(currentPath)[selectedIndex - 1]);
                    currentPath = Path.Combine(currentPath, selDir);
                    pb.Panel.Repaint(true);
                }
                else if (selectedIndex > Directory.GetDirectories(currentPath).Length)
                {
                    fileInput = Path.GetFileName(
                        Directory.GetFiles(currentPath)[
                            selectedIndex - Directory.GetDirectories(currentPath).Length - 1]);
                }

                // Lower text input for the filename
                (fileInput, _) = pb.TextInput(!selectDir?"File Name:":"Directory Name:", fileInput, !selectDir?"Enter file name here...":"Enter directory name here...");

                string tmpFn = currentPath;
                if (!selectDir)
                    tmpFn = Path.Combine(currentPath, fileInput);

                // OK and Cancel button group
                if (pb.ButtonGroup("", [actionName??"OK", "Cancel"], out var buttonID))
                {
                    if (buttonID == 0)
                    {
                        pb.Exit(tmpFn);
                    }
                    else if (buttonID == 1)
                    {
                        pb.Exit(null);
                    }
                }
            }, t);
            return filename != null;
        }

        private static string GetFileIcon(string extension)
        {
            return extension switch
            {
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => $"{ForkAwesome.FileArchiveO}",
                ".mp3" or ".wav" or ".aiff" => $"{ForkAwesome.FileAudioO}",
                ".html" or ".css" or ".js" or ".cs" or ".py" or ".cpp" or ".c" or ".h" or ".hpp" => $"{ForkAwesome.FileCodeO}",
                ".xls" or ".xlsx" => $"{ForkAwesome.FileExcelO}",
                ".png" or ".jpg" or ".jpeg" or ".gif" => $"{ForkAwesome.FileImageO}",
                ".avi" or ".mp4" or ".mov" or ".wmv" => $"{ForkAwesome.FileVideoO}",
                ".pdf" => $"{ForkAwesome.FilePdfO}",
                ".ppt" or ".pptx" => $"{ForkAwesome.FilePowerpointO}",
                ".doc" or ".docx" => $"{ForkAwesome.FileWordO}",
                ".txt" or ".log" or ".json" => $"{ForkAwesome.FileTextO}",
                ".dll" or ".exe" => $"{ForkAwesome.Cogs}",
                ".glb" => $"{ForkAwesome.Cube}",
                _ => $"{ForkAwesome.FileO}"
            };
        }
    }
}
