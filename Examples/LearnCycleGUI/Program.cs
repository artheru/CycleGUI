using System.Numerics;
using System.Reflection;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;
using LearnCycleGUI.DemoControls;
using LearnCycleGUI.DemoPanelLayout;
using LearnCycleGUI.DemoPlot;
using LearnCycleGUI.DemoWorkspace;

namespace LearnCycleGUI;

internal static class Program
{
    static unsafe void Main(string[] args)
    {
        // First read the .ico file from assembly, and then extract it as byte array.
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .First(p => p.Contains(".ico")));
        var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);

        // Set app window title.
        LocalTerminal.SetTitle("LearnCycleGUI");

        // Set an icon for the app.
        LocalTerminal.SetIcon(icoBytes, "LearnCycleGUI");

        // Add an "Exit" button when right-click the tray icon.
        // Note: you can add multiple MenuItems like this.
        LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);

        // Actually start the app window.
        LocalTerminal.Start();

        Task.Run(() => { WebTerminal.Use(ico: icoBytes); });

        // Create the main panel, in which there are buttons for 4 tutorial chapters.
        PanelBuilder.CycleGUIHandler CreateDemoPanel(Terminal t)
        {
            Panel _mainPanel = null;

            Panel _demoControls = null;

            Panel _demoPanelLayout = null;

            Panel _demoPlot = null;

            Panel _demoWorkspace = null;
            return pb =>
            {
                // Name the panel title as "Welcome". If title not set, it will be "Panel_0" by default.
                pb.Panel.ShowTitle("Welcome");

                pb.Label($"{IconFonts.ForkAwesome.SmileO} Greetings from CycleGUI!");
                pb.Label("Please left-click the buttons to enter tutorial chapters in the left panel.");
                pb.Label($"In the right-side workspace view, press and hold your middle mouse button (mouse wheel) " +
                         "and move your mouse to change observation angle; press and hold your right mouse button " +
                         "and move you mouse to drag the scene; and use mouse wheel to zoom in/out.");

                pb.Separator();

                if (pb.Button("Chapter 1 - Panel Controls"))
                {
                    if (!CurrentPanels.Contains(_demoControls))
                    {
                        _demoControls = GUI.DeclarePanel()
                            .ShowTitle("Controls")
                            .InitPosRelative(_mainPanel, 0, 16, 0, 1)
                            .SetDefaultDocking(Panel.Docking.Left);
                        _demoControls.Define(DemoControlsHandler.PreparePanel());
                        CurrentPanels.Add(_demoControls);
                    }
                    else
                        _demoControls.BringToFront();
                }

                pb.Label("In this chapter, you will go through all control types with examples.");
                pb.Label("These examples will show you how to use the control APIs to utilize " +
                         "different types of widget.");

                pb.Separator();

                if (pb.Button("Chapter 2 - Panel Layout"))
                {
                    // UITools.Alert("Coming soon~", "Information", pb.Panel.Terminal);
                    if (!CurrentPanels.Contains(_demoPanelLayout))
                    {
                        _demoPanelLayout = GUI.DeclarePanel()
                            .ShowTitle("Panel Layout")
                            .InitPosRelative(_mainPanel, 0, 16, 0, 1)
                            // .InitPos(true, 0, 0)
                            .SetDefaultDocking(Panel.Docking.Left);
                        _demoPanelLayout.Define(DemoPanelLayoutHandler.PreparePanel());
                        CurrentPanels.Add(_demoPanelLayout);
                    }
                    else
                        _demoPanelLayout.BringToFront();
                }

                pb.Label("In this chapter, you will learn how to deal with the panel functionalities.");
                pb.Label(
                    "Specifically, we will show you how to create, dock, minimize panels with different user restrictions.");

                pb.Separator();

                if (pb.Button("Chapter 3 - Plot"))
                {
                    if (!CurrentPanels.Contains(_demoPlot))
                    {
                        _demoPlot = GUI.DeclarePanel()
                            .ShowTitle("Plot")
                            .InitPosRelative(_mainPanel, 0, 16, 0, 1)
                            .SetDefaultDocking(Panel.Docking.Left);
                        _demoPlot.Define(DemoPlotHandler.PreparePanel());
                        CurrentPanels.Add(_demoPlot);
                    }
                    else
                        _demoPlot.BringToFront();
                }

                pb.Label("In this chapter, you can see how to invoke plotting APIs.");
                pb.Label(
                    "You can learn how to display various types of data using line charts, pie charts, bar charts, etc.");

                pb.Separator();

                if (pb.Button("Chapter 4 - Workspace"))
                {
                    if (!CurrentPanels.Contains(_demoWorkspace))
                    {
                        _demoWorkspace = GUI.DeclarePanel()
                            .ShowTitle("Workspace")
                            .InitPosRelative(_mainPanel, 0, 16, 0, 1)
                            .SetDefaultDocking(Panel.Docking.Left);
                        _demoWorkspace.Define(DemoWorkspaceHandler.PreparePanel());
                        CurrentPanels.Add(_demoWorkspace);
                    }
                    else
                        _demoWorkspace.BringToFront();
                }

                pb.Label("Workspace is the UI area where 3D object displaying and 3D interactions are executed.");
                pb.Label("In this chapter, you get to know how to fully utilize our 3D APIs.");
            };
        }

        Workspace.AddProp(new PutPointCloud()
            { colors = [0xffffffff], name = "pc", newPosition = Vector3.UnitZ, xyzSzs = [new Vector4(0, 0, 0, 5)] });

        Terminal.RegisterRemotePanel(CreateDemoPanel);
        GUI.PromptPanel(CreateDemoPanel(GUI.defaultTerminal));
    }

    public static List<Panel> CurrentPanels = new();

    private const string ResourceNamespace = "LearnCycleGUI.Resources";

    public static void CopyFileFromEmbeddedResources(string fileName, string relativePath = "")
    {
        try
        {
            var resourceName = $"{ResourceNamespace}.{fileName}";
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new Exception($"Embedded resource {resourceName} not found");

            using var output = new FileStream(
                Path.Combine(Directory.GetCurrentDirectory(), relativePath, fileName), FileMode.Create);
            stream.CopyTo(output);
            Console.WriteLine($"Successfully copied {fileName}");
        }
        catch (Exception e)
        {
            // force rewrite.
            Console.WriteLine($"Failed to copy {fileName}");
            // Console.WriteLine(e.FormatEx());
        }
    }
}