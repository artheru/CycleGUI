using System;
using System.Numerics;
using System.Reflection;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;
using Newtonsoft.Json;
using Python.Runtime;

namespace Annotator;

internal static class Program
{
    internal static unsafe void Main(string[] args)
    {
        if (!File.Exists("start_options.json"))
        {
            var defaultOptions = new StartOptions
            {
                DisplayOnWeb = false,
                PythonPath = @"C:\Path_to_Your_Python\python310.dll",
                Port = 8081,
            };
            File.WriteAllText("start_options.json", JsonConvert.SerializeObject(defaultOptions, Formatting.Indented));

            Console.WriteLine("Start options file created. Please update the Python path and restart." +
                              "\nPress Enter to exit.");
            Console.ReadLine();
            Environment.Exit(0);
        }

        var startOptions = JsonConvert.DeserializeObject<StartOptions>(File.ReadAllText("start_options.json"));

        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));
        var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);

        PythonInterface.Initialize(startOptions.PythonPath);
        PythonEngine.BeginAllowThreads();
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Objects")));
        }

        if (startOptions.DisplayOnWeb)
        {

        }
        else
        {
            LocalTerminal.SetIcon(icoBytes, "Annotator");
            // Set app window title.
            LocalTerminal.SetTitle("Annotator");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.Start();

            _targetObjectViewport = GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("Target Object"));
            _mixedViewport = GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("Mixed View"));

            _objectList = GUI.DeclarePanel()
                .ShowTitle("Object Info")
                .SetDefaultDocking(Panel.Docking.Left);
            _objectList.Define(PanelConstructors.DefineTargetObjectPanel());

            _templateObjectLibrary = GUI.DeclarePanel()
                .ShowTitle("Conceptual Templates Library")
                .SetDefaultDocking(Panel.Docking.Left);
            _templateObjectLibrary.Define(PanelConstructors.DefineTemplateLibraryPanel());

            _templateObjectEditor = GUI.DeclarePanel()
                .ShowTitle("Parameter Editor")
                .SetDefaultDocking(Panel.Docking.Left);
            _templateObjectEditor.Define(PanelConstructors.DefineTemplateObjectEditor());

            new SetWorkspacePropDisplayMode()
            {
                mode = SetWorkspacePropDisplayMode.PropDisplayMode.NoneButSpecified,
                namePattern = "target_*"
            }.IssueToTerminal(_targetObjectViewport);

            new SetWorkspacePropDisplayMode()
            {
                mode = SetWorkspacePropDisplayMode.PropDisplayMode.AllButSpecified,
                namePattern = "target_*"
            }.IssueToDefault();
        }

        SetCustomGround();

        DefaultAction = new SelectObject()
        {
            feedback = (tuples, _) =>
            {
                if (tuples.Length == 0)
                    Console.WriteLine($"no selection");
                else
                {
                    Console.WriteLine($"selected {tuples[0].name}");
                    PanelConstructors.SelectedTemplate =
                        PanelConstructors.TemplateObjects.First(t => t.Name == tuples[0].name);
                    PanelConstructors.ToggleTransparency();
                    var action = new GuizmoAction()
                    {
                        realtimeResult = true,//realtime,
                        finished = () =>
                        {
                            Console.WriteLine("OKOK...");
                            DefaultAction.SetSelection([]);
                            // IssueCrossSection();
                        },
                        terminated = () =>
                        {
                            Console.WriteLine("Forget it...");
                            DefaultAction.SetSelection([]);
                            // IssueCrossSection();
                        }
                    };
                    action.feedback = (valueTuples, _) =>
                    {
                        var name = valueTuples[0].name;
                        // if (name == demo.Name)
                        // {
                        //     demo.Pos = valueTuples[0].pos;
                        //     demo.Rot = valueTuples[0].rot;
                        // }
                        // else
                        // {
                        //     var render = TemplateObjects.First(geo => geo.Name == name);
                        //     render.Pos = valueTuples[0].pos;
                        //     render.Rot = valueTuples[0].rot;
                        //     //CheckPlaneAlignment(render);
                        // }
                    };
                    action.Start();
                }
            },
        };
        DefaultAction.StartOnTerminal(GUI.defaultTerminal);
    }

    public static SelectObject DefaultAction = null;

    private static void SetCustomGround()
    {
        string checkerboardShader = @"
        // Checkerboard pattern with 1m intervals
        void mainImage(out vec4 fragColor, in vec2 fragCoord) {
            // 屏幕坐标转NDC
            vec2 uv = fragCoord / iResolution.xy;
            uv = uv * 2.0 - 1.0;

            // 生成射线
            vec4 ray_clip = vec4(uv, -1.0, 1.0);
            vec4 ray_eye = iInvPM * ray_clip;
            ray_eye = vec4(ray_eye.xy, -1.0, 0.0);
            vec4 ray_world = iInvVM * ray_eye;
            vec3 ray_dir = normalize(ray_world.xyz);

            // 与地面z=0的交点
            float t = -iCameraPos.z / ray_dir.z;

            if (t > 0.0 && ray_dir.z < 0.0) {
                // 交点世界坐标
                vec3 pos = iCameraPos + t * ray_dir;

                // 计算到(0,0,0)的距离
                float dist = length(pos.xy);

                // 距离归一化（可调节渐变范围），指数<1让过渡更缓和
                float fade = clamp(pow(dist / 20.0, 0.5), 0.0, 1.0);

                // 颜色插值：中心为更暗的灰色
                vec3 centerColor = vec3(0.6, 0.6, 0.6); // 更暗的灰
                vec3 edgeColor   = vec3(0.3, 0.3, 0.3); // 深灰
                vec3 finalColor = mix(centerColor, edgeColor, fade);

                fragColor = vec4(finalColor, 0.8);
            } else {
                // 天空渐变
                float y = uv.y * 0.5 + 0.5;
                vec3 skyColor = mix(
                    vec3(0.5, 0.7, 1.0),
                    vec3(0.2, 0.4, 0.8),
                    y
                );
                fragColor = vec4(skyColor, 0.8);
            }
        }";

        Workspace.SetCustomBackgroundShader(checkerboardShader);
        new SetAppearance()
            {
                bring2front_onhovering = false, 
                drawGrid = false,
            }
            .IssueToAllTerminals();

        var setDrawGuizmo = new SetAppearance()
        {
            drawGuizmo = false,
        };
        setDrawGuizmo.IssueToTerminal(_targetObjectViewport);
        setDrawGuizmo.IssueToTerminal(_mixedViewport);

        new SetCamera()
        {
            altitude = (float)(Math.PI / 6),
            azimuth = (float)(-Math.PI / 3),
            lookAt = Vector3.Zero,
            distance = 5
        }.IssueToAllTerminals();

        bool kiosk = PanelConstructors.KioskMode;
        Vector3 camPos = new Vector3(), lookAt = new Vector3();
        var kioskTime = DateTime.Now;

        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(10);

                if (PanelConstructors.KioskMode)
                {
                    // if (!kiosk)
                    // {
                    //     kiosk = true;
                    //     new QueryViewportState()
                    //     {
                    //         callback = vs =>
                    //         {
                    //             camPos = vs.CameraPosition;
                    //             lookAt = vs.LookAt;
                    //         }
                    //     }.IssueToDefault();
                    //     kioskTime = DateTime.Now;
                    // }
                    
                    new SetCamera()
                    {
                        altitude = (float)(Math.PI / 6),
                        azimuth = (float)(-Math.PI / 3) + (float)(DateTime.Now - kioskTime).TotalSeconds * 2,
                        lookAt = Vector3.Zero,
                        distance = 5
                        // lookAt = lookAt,
                        // altitude = AnnotatorHelper.CalculateAltitude(camPos, lookAt),
                        // azimuth = AnnotatorHelper.CalculateAzimuth(camPos, lookAt) + (float)(DateTime.Now - kioskTime).TotalSeconds * 10,
                        // distance = AnnotatorHelper.CalculateDistance(camPos, lookAt)
                    }.IssueToAllTerminals();
                }
                else
                {
                    kiosk = false;

                    new QueryViewportState()
                    {
                        callback = vs =>
                        {
                            // Console.WriteLine($"cam:{vs.CameraPosition} lookAt:{vs.LookAt}");
                            var sc = new SetCamera()
                            {
                                lookAt = vs.LookAt,
                                altitude = AnnotatorHelper.CalculateAltitude(vs.CameraPosition, vs.LookAt),
                                azimuth = AnnotatorHelper.CalculateAzimuth(vs.CameraPosition, vs.LookAt),
                                distance = AnnotatorHelper.CalculateDistance(vs.CameraPosition, vs.LookAt)
                            };
                            sc.IssueToTerminal(_targetObjectViewport);
                            sc.IssueToTerminal(_mixedViewport);
                        }
                    }.IssueToDefault();
                }
            }
        }).Start();
    }

    private static Panel _objectList;
    private static Panel _templateObjectLibrary;
    private static Panel _templateObjectEditor;

    private static Viewport _targetObjectViewport = null;
    private static Viewport _mixedViewport = null;

    internal class StartOptions
    {
        public bool DisplayOnWeb { get; set; }
        public string PythonPath { get; set; }
        public int Port { get; set; }
    }
}