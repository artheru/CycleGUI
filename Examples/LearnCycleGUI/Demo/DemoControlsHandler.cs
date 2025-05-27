using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;

namespace LearnCycleGUI.Demo
{
    internal class DemoControlsHandler
    {
        public static PanelBuilder.CycleGUIHandler PreparePanel()
        {
            var count = 0;
            var checkbox = false;
            var radioButtonsIndex = 0;
            var radioButtonsSameLine = false;
            var radioButtonsEditTime = DateTime.MaxValue;
            var strList = new List<string>() { "This is a line.", "Another Line" };
            string[] chatBoxAppending = null;
            var buttonGroupsClicked = "Not clicked yet.";
            var buttonGroupsSameLine = false;
            var dragFloatVal = 0f;
            var dragFloatVal2 = 0f;
            var dragFloatEditTime = DateTime.MaxValue;
            var dropdownIndex = 0;
            var dropDownEditTime = DateTime.MaxValue;
            var tableUseHeight = true;
            var tableEnableTitle = true;
            var tableEnableSearch = true;
            var tableRowInterest = new[] { false, false, false, false, false, false, false, false, false, false, false, false };
            var toggleFlag = false;
            var openFileFilter = "";
            var openFileName = "";
            var selectFolderName = "";
            var selectedColor = Color.FromArgb(255, 100, 150, 200); // Initial RGBA color

            return pb =>
            {
                if (pb.Closing())
                {
                    Program.CurrentPanels.Remove(pb.Panel);
                    pb.Panel.Exit();
                    return;
                }

                // Label
                pb.CollapsingHeaderStart("Label");
                pb.Label("This is a label.");
                pb.CollapsingHeaderEnd();

                // Separator
                pb.CollapsingHeaderStart("Separator");
                pb.Label("You can use separators to keep your UI content apart.");
                pb.Separator();
                pb.Label("Or you can use a separator with text as title or something else.");
                pb.SeparatorText("I am a title.");
                pb.Label("Just like this.");
                pb.CollapsingHeaderEnd();

                // Button
                pb.CollapsingHeaderStart("Button");
                if (pb.Button("Count")) // Enters this if when button is clicked.
                    count++;
                pb.Label($"Button pressed {count} time{(count > 1 ? "s" : "")}.");
                pb.CollapsingHeaderEnd();

                // SameLine
                pb.CollapsingHeaderStart("SameLine");
                pb.Label("You can put controls in the same line with different spacing between them.");
                pb.Button("Button1");
                pb.SameLine(10);
                pb.Button("Button2");
                pb.SameLine(20);
                pb.Button("Button3");
                pb.CollapsingHeaderEnd();

                // CheckBox
                pb.CollapsingHeaderStart("CheckBox");
                if (pb.CheckBox("Binary select.", ref checkbox)) // Enters this if when checkbox state is changed.
                    count++;
                if (checkbox) pb.Label("You see me if the checkbox is checked!");
                pb.CollapsingHeaderEnd();

                // RadioButtons
                pb.CollapsingHeaderStart("RadioButtons");
                pb.CheckBox("Same line.", ref radioButtonsSameLine);

                var radioList = new[] { "radio a", "radio b", "radio c" };
                if (pb.RadioButtons("TestRadio", radioList, ref radioButtonsIndex, radioButtonsSameLine))
                    radioButtonsEditTime = DateTime.Now;

                var radioEdit = radioButtonsEditTime > DateTime.Now
                    ? "Value not changed yet."
                    : $"Item changed {(DateTime.Now - radioButtonsEditTime).TotalSeconds:0.0} sec before.";
                pb.Label(radioEdit);
                pb.Label(radioButtonsIndex >= 0 && radioButtonsIndex < radioList.Length
                    ? $"Selected item: {radioList[radioButtonsIndex]}"
                    : "Invalid index.");
                pb.CollapsingHeaderEnd();

                // Toggle
                pb.CollapsingHeaderStart("Toggle");
                pb.Toggle("Toggle.", ref toggleFlag);
                pb.Label($"Toggle {(toggleFlag ? "on" : "off")}.");
                pb.CollapsingHeaderEnd();

                // DragFloat
                pb.CollapsingHeaderStart("DragFloat");

                pb.Label("You can drag the box to change its value, " +
                         "or double click the box to input value directly.");
                if (pb.DragFloat("-10 ~ +10", ref dragFloatVal, 0.1f, -10, 10))
                    dragFloatEditTime = DateTime.Now;
                if (pb.DragFloat("-1 ~ +1", ref dragFloatVal2, 1e-6f, -1e-3f, 1e-3f))
                    dragFloatEditTime = DateTime.Now;

                string dragChangeHint;
                if (dragFloatEditTime > DateTime.Now) dragChangeHint = "Value not changed yet.";
                else if ((DateTime.Now - dragFloatEditTime).TotalSeconds < 0.1)
                    dragChangeHint = "Value changed just now.";
                else dragChangeHint = $"Value changed {(DateTime.Now - dragFloatEditTime).TotalSeconds:0.0} sec before.";
                pb.Label(dragChangeHint);
                pb.CollapsingHeaderEnd();

                // DropdownBox
                pb.CollapsingHeaderStart("DropdownBox");
                var dropdownMenu = new[] { "Apricot", "Broccoli", "Cucumber", "Durian", "Eggplant" };

                if (pb.DropdownBox("Menu", dropdownMenu, ref dropdownIndex))
                    dropDownEditTime = DateTime.Now;

                var dropdownEdit = dropDownEditTime > DateTime.Now
                    ? "Value not changed yet."
                    : $"Item changed {(DateTime.Now - dropDownEditTime).TotalSeconds:0.0} sec before.";
                pb.Label(dropdownEdit);
                pb.Label(dropdownIndex >= 0 && dropdownIndex < dropdownMenu.Length
                    ? $"Selected item: {dropdownMenu[dropdownIndex]}"
                    : "Invalid index.");
                pb.CollapsingHeaderEnd();

                // ButtonGroups
                pb.CollapsingHeaderStart("ButtonGroups");
                var zoo = new[] { "Alligator", "Buffalo", "Cheetah", "Dolphin", "Elephant", "Frog", "Giraffe" };
                pb.CheckBox("Same line forcibly.", ref buttonGroupsSameLine);

                pb.ButtonGroup("You can click the animals.", zoo, out var buttonIndex, buttonGroupsSameLine);

                if (buttonIndex >= 0) buttonGroupsClicked = zoo[buttonIndex];
                pb.Label(buttonGroupsClicked);
                pb.CollapsingHeaderEnd();

                // TextInput
                pb.CollapsingHeaderStart("TextInput");
                var (str, done) = pb.TextInput("Press Enter to confirm input.", "default text");
                if (done) strList.Add(str);
                pb.Label("You can unfold the \"ListBox\" header to see your input history.");
                pb.CollapsingHeaderEnd();

                // SelectableText
                string GetSystemInfo()
                {
                    var sb = new StringBuilder();

                    // OS Information
                    sb.AppendLine("Operating System:");
                    sb.AppendLine($"  OS: {Environment.OSVersion}");
                    sb.AppendLine($"  64-bit OS: {Environment.Is64BitOperatingSystem}");
                    sb.AppendLine($"  Platform: {Environment.OSVersion.Platform}");
                    sb.AppendLine();

                    // Machine Information
                    sb.AppendLine("Machine Information:");
                    sb.AppendLine($"  Machine Name: {Environment.MachineName}");
                    sb.AppendLine($"  Processor Count: {Environment.ProcessorCount}");
                    sb.AppendLine($"  64-bit Process: {Environment.Is64BitProcess}");
                    sb.AppendLine();

                    // Memory Information
                    sb.AppendLine("Memory Information:");
                    sb.AppendLine($"  Working Set: {Environment.WorkingSet / (1024 * 1024)} MB");
                    sb.AppendLine($"  System Page Size: {Environment.SystemPageSize / 1024} KB");
                    sb.AppendLine();

                    // .NET Information
                    sb.AppendLine(".NET Information:");
                    sb.AppendLine($"  Version: {Environment.Version}");
                    sb.AppendLine($"  Runtime: {RuntimeInformation.FrameworkDescription}");
                    sb.AppendLine();

                    // User Information
                    sb.AppendLine("User Information:");
                    sb.AppendLine($"  User Name: {Environment.UserName}");
                    sb.AppendLine($"  User Domain: {Environment.UserDomainName}");
                    sb.AppendLine();

                    // Directory Information
                    sb.AppendLine("Directory Information:");
                    sb.AppendLine($"  Current Directory: {Environment.CurrentDirectory}");
                    sb.AppendLine($"  System Directory: {Environment.SystemDirectory}");

                    return sb.ToString();
                }

                pb.CollapsingHeaderStart("SelectableText");
                pb.SelectableText("testTextBox", GetSystemInfo());
                pb.CollapsingHeaderEnd();

                // ListBox
                pb.CollapsingHeaderStart("ListBox");
                pb.ListBox("Lines of texts.", strList.ToArray(), persistentSelecting: true);
                pb.Label("You can unfold the \"TextInput\" header to add some lines into this box.");
                pb.CollapsingHeaderEnd();

                // ChatBox
                pb.CollapsingHeaderStart("ChatBox");
                var (messageEntered, sending) = pb.ChatBox("Dummy Console", chatBoxAppending);
                chatBoxAppending = null;
                if (messageEntered)
                    chatBoxAppending = [sending, $"> You said \"{sending}\"."];
                pb.Label("This is a dummy console implemented using ChatBox. Type something in the entering box, press Enter or click the paper-airplane-like send button, you will see a dummy reply in the console.");
                pb.CollapsingHeaderEnd();

                // Table
                pb.CollapsingHeaderStart("Table");
                var monthNameList = new[]
                {
                    "January", "February", "March", "April", "May", "June",
                    "July", "August", "September", "October", "November", "December"
                };
                var numberOfDays = new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

                pb.CheckBox("Enable title.", ref tableEnableTitle);

                pb.CheckBox("Enable searching.", ref tableEnableSearch);

                if (tableEnableSearch)
                    pb.Label($"If you type some key word in the search box, only rows containing the key word will be displayed. " +
                         $"The rows of interested months are highlighted in red.");

                pb.CheckBox("Declare table height", ref tableUseHeight);

                pb.Table("months", ["Name", "Number of days", "Interested"], 12, (row, index) =>
                    {
                        if (tableRowInterest[index]) row.SetColor(Color.Red);
                        // Column 0: month names.
                        row.Label(monthNameList[index]);
                        // Column 1: number of days in that month.
                        row.Label($"{numberOfDays[index]}",
                            $"{monthNameList[index]} has {numberOfDays[index]} days." +
                            (index == 1 ? " 29 days in a leap year." : ""));
                        // Column 2: put or remove the month from interest list.
                        // Note: hint is also available with row.ButtonGroup.
                        var rowButtonIndex = row.ButtonGroup(["Add", "Remove"]);
                        if (rowButtonIndex == 0) tableRowInterest[index] = true;
                        if (rowButtonIndex == 1) tableRowInterest[index] = false;
                    }, enableSearch: tableEnableSearch, title: tableEnableTitle ? "Months Table" : "",
                    height: tableUseHeight ? 5 : 0);

                if (tableRowInterest.Any(ii => ii))
                    pb.Label($"Interested in month: " +
                         $"{string.Join(", ", tableRowInterest.Select((bb, ii) => (bb, ii)).Where(month => month.bb).Select(month => monthNameList[month.ii]))}.");
                else pb.Label("No interested month.");
                pb.CollapsingHeaderEnd();

                // DisplayFileLink
                pb.CollapsingHeaderStart("DisplayFileLink");
                pb.Label("Click file link to open its containing folder.");
                pb.DisplayFileLink("libVRender.dll", "libVRender.dll");
                pb.CollapsingHeaderEnd();

                // OpenFile
                pb.CollapsingHeaderStart("OpenFile");
                var (filterStr, filterSet) = pb.TextInput("Enter filters here.", openFileFilter);
                if (filterSet) openFileFilter = filterStr;

                pb.Label("Filters examples:");
                var filterExplanation = new[]
                {
                    ("Empty string", "Any file."), ("*", "Any file with arbitrary extension."),
                    ("zip", "Files with \".zip\" extension."), ("zip,pptx", "Files with zip or pptx extension.")
                };
                pb.Table("file filter explanation", ["Filters", "Explanation"], 4, (row, index) =>
                {
                    row.Label(filterExplanation[index].Item1);
                    row.Label(filterExplanation[index].Item2);
                });
                pb.Label($"Current filters: {openFileFilter}");

                if (pb.Button("Open File Dialog"))
                    if (pb.OpenFile("Choose File.", openFileFilter, out var fileName))
                        openFileName = fileName;
                if (pb.Button("Save File Dialog"))
                    if (pb.SaveFile("Choose File.", openFileFilter, out var fileName))
                        openFileName = fileName;

                pb.Label(openFileName != "" ? $"File {openFileName} selected." : "No file selected yet.");

                pb.CollapsingHeaderEnd();

                // SelectFolder
                pb.CollapsingHeaderStart("SelectFolder");
                if (pb.Button("Select Directory"))
                    pb.SelectFolder("Choose Folder", out selectFolderName);
                pb.Label(selectFolderName != "" ? $"Folder {selectFolderName} selected." : "No folder selected yet.");
                pb.CollapsingHeaderEnd();

                // Alert
                pb.CollapsingHeaderStart("Alert");
                if (pb.Button($"Show Alert"))
                    UITools.Alert("This is an alert!", t: pb.Panel.Terminal);
                pb.CollapsingHeaderEnd();

                // Icons
                pb.CollapsingHeaderStart("Icons");
                pb.Label($"You can use IconFonts.ForkAwesome to make your UI more expressive. " +
                         $"For example you can easily let user know these buttons function as a media player.");
                pb.ButtonGroup("Player",
                [
                    IconFonts.ForkAwesome.Backward, IconFonts.ForkAwesome.Play, IconFonts.ForkAwesome.Pause,
                    IconFonts.ForkAwesome.Stop, IconFonts.ForkAwesome.Forward
                ], out _);
                pb.Separator();
                pb.Label($"Here is a table showing all icons.");
                Type type = typeof(IconFonts.ForkAwesome);
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                pb.Table("IconsTable", ["Name", "Icon"], fields.Length, (row, id) =>
                {
                    row.Label(fields[id].Name);
                    row.Label($"{fields[id].GetValue(null)}");
                }, enableSearch: true);
                pb.CollapsingHeaderEnd();


                // ColorEdit
                pb.CollapsingHeaderStart("Color Picker Control");

                pb.Label("This demonstrates the ColorEdit control for selecting colors with a visual color picker.");

                // Display the current color as text
                pb.Label($"Current Color: R:{selectedColor.R}, G:{selectedColor.G}, B:{selectedColor.B}, A:{selectedColor.A}");

                // The ColorEdit control
                if (pb.ColorEdit("Edit Color", ref selectedColor))
                {
                    Console.WriteLine("Edit color"); //todo
                }

                pb.CollapsingHeaderEnd();


                // Tabs
                pb.CollapsingHeaderStart("Tabs");
                var tabList = new[] { "Avocado", "Broccoli", "Cucumber" };
                var tabButtonIndex = pb.TabButtons("TestTabs", radioList);
                pb.Label($"Selected tab={tabList[tabButtonIndex]}");
                pb.CollapsingHeaderEnd();


                //pb.Panel.Repaint();
                pb.CollapsingHeaderStart("Integrated Web-browser");
                pb.OpenWebview("Open WebBrowser", "https://wiki.lessokaji.com", "Open web browser with CycleGUI");
                pb.CollapsingHeaderEnd();
            };
        }
    }
}
