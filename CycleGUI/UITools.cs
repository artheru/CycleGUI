using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IconFonts;

namespace CycleGUI
{
    public static class UITools
    {
        public static void Alert(string prompt, string title="Alert", Terminal t=null)
        {
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
                (retTxt, var done)= pb2.TextInput(prompt, defVal, hint);
                if (done)
                    pb2.Exit(true);

                if (pb2.ButtonGroups("", new[] { "OK", "Cancel" }, out var bid))
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
        public static bool FileBrowser(string title, out string filename, bool selectDir=false, Terminal t=null)
        {
            string currentPath = Directory.GetCurrentDirectory();
            string inputPath = currentPath;
            List<string> directoryItems = new List<string>();
            int selectedIndex = -1;
            string fileInput = "";
            string lastGoodPath = currentPath;
            filename = GUI.WaitPanelResult<string>(pb =>
            {
                pb.Panel.TopMost(true).InitSize(480).AutoSize(true).ShowTitle(title).Modal(true);

                // Check the operating system and display available drives if on Windows
                if (!currentPath.StartsWith("/"))
                {
                    string[] drives = Directory.GetLogicalDrives();
                    if (pb.ButtonGroups("Drives:", drives, out int driveSelected))
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
                selectedIndex = pb.Listbox("Contents:", directoryItems.ToArray(), 10);

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
                (fileInput, var doneFileInput) = pb.TextInput("File Name:", fileInput, "Enter file name here...");

                string tmpFn = currentPath;
                if (!selectDir)
                    tmpFn = Path.Combine(currentPath, fileInput);

                // OK and Cancel button group
                if (pb.ButtonGroups("", new[] { "OK", "Cancel" }, out var buttonID))
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
    }
}
