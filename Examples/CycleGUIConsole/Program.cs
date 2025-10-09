using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using CycleGUI;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using CycleGUI.API;
using CycleGUI.Terminals;
using FundamentalLib;
using NativeFileDialogSharp;
using static System.Net.Mime.MediaTypeNames;
using Path = System.IO.Path;
using GitHub.secile.Video;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Drawing.Imaging;
using Newtonsoft.Json;

namespace VRenderConsole
{
    
    // to pack: dotnet publish -p:PublishSingleFile=true -r win-x64 -c Release --self-contained false
    internal static class Program
    {
        static unsafe void Main(string[] args)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();

            Viewport aux_vp1 = null, aux_vp2 = null;
            GUI.PromptPanel(pb =>
            {
                if (pb.Button("Open SubViewport1"))
                    aux_vp1 ??= GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("TEST1"));
                if (pb.Button("Open SubViewport2"))
                    aux_vp2 ??= GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("TEST2"));
            });

            Workspace.Prop(new PutStraightLine
            {
                color = Color.IndianRed,
                name = "demo_line",
                start = new Vector3(0,-9999,0),
                end = new Vector3(0,9999,0),
                width = 1,
                arrowType = Painter.ArrowType.End
            });
            Workspace.Prop(new PutStraightLine
            {
                color = Color.Green,
                name = "demo_line",
                start = new Vector3(-9, 0, 1),
                end = new Vector3(9, 0, 1),
                width = 1,
                arrowType = Painter.ArrowType.End
            });
        }
    }
}