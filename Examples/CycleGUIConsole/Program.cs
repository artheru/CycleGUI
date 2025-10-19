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
            WebTerminal.RegisterRemotePanel(t =>
            {
                return pb =>
                {
                    if (pb.Button("Open SubViewport1"))
                        aux_vp1 ??= GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("TEST1"));
                    if (pb.Button("Open SubViewport2"))
                        aux_vp2 ??= GUI.PromptWorkspaceViewport(panel => panel.ShowTitle("TEST2"));
                    if (pb.Button("SET"))
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
                                drawGroundGrid = false,
                            }
                            .IssueToAllTerminals();

                        var setDrawGuizmo = new SetAppearance()
                        {
                            drawGuizmo = false,
                        };
                        setDrawGuizmo.IssueToTerminal(aux_vp1);
                        setDrawGuizmo.IssueToTerminal(aux_vp2);
                        new SetCamera() { anchor_type = SetCamera.AnchorType.CopyCamera }.IssueToTerminal(aux_vp1);
                        new SetCamera() { anchor_type = SetCamera.AnchorType.CopyCamera }.IssueToTerminal(aux_vp2);
                        Workspace.Prop(new SetObjectMoonTo()
                            { name = "me::camera(TEST1)", earth = "me::camera(main)" });
                        Workspace.Prop(new SetObjectMoonTo()
                            { name = "me::camera(TEST2)", earth = "me::camera(main)" });
                    }
                };
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
            WebTerminal.Use();
        }
    }
}