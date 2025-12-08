using System.Numerics;
using System.Reflection;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;

namespace GltfViewer
{
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

            var model3dX = 0f;
            var model3dY = 0f;
            var model3dZ = 0f;
            var scale = 0.1f;
            var colorScale = 1.0f;
            var brightness = 1.0f;
            var normal = 0f;
            var dblFace = false;

            var fn = "/";

            var lm = new LoadModel()
            {
                detail = default,
                name = "m_glb"
            };
            var rot = 1;
            GUI.PromptPanel(pb =>
            {
                var model3dPosChanged = pb.DragFloat("center X", ref model3dX, 0.01f, -30, 30);
                model3dPosChanged |= pb.DragFloat("center Y", ref model3dY, 0.01f, -30, 30);
                model3dPosChanged |= pb.DragFloat("center Z", ref model3dZ, 0.01f, -30, 30);
                model3dPosChanged |= pb.DragFloat("scale", ref scale, 0.0001f, 0, 1000);
                model3dPosChanged |= pb.DragFloat("color scale", ref colorScale, 0.0003f, 0.1f, 10);
                model3dPosChanged |= pb.DragFloat("brightness", ref brightness, 0.0003f, 0.1f, 10);
                model3dPosChanged |= pb.DragFloat("normal shading", ref normal, 0.0001f, 0f, 1f);

                model3dPosChanged |= pb.RadioButtons("Rotation", ["NoRotation", "X", "Y", "2X"], ref rot, true);

                model3dPosChanged |= pb.CheckBox("Force Double sided", ref dblFace);
                var quats = new[]
                {
                    Quaternion.Identity,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI / 2),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI),
                };

                if (model3dPosChanged)
                {
                    Workspace.AddProp(new ReloadModel()
                    {
                        detail = new Workspace.ModelDetail([])
                        {
                            Center = new Vector3(model3dX, model3dY, model3dZ),
                            Rotate = quats[rot],
                            Scale = scale,
                            Brightness = brightness,
                            ColorScale = colorScale,
                            NormalShading = normal,
                            ForceDblFace = dblFace
                        },
                        name = "m_glb"
                    });
                }

                pb.Label($"gltf fn={fn}");
                if (pb.Button("Load and show glb"))
                    if (pb.OpenFile("select file", "glb", out fn))
                    {
                        lm.detail = new Workspace.ModelDetail(File.ReadAllBytes(fn))
                        {
                            Center = new Vector3(model3dX, model3dY, model3dZ),
                            Rotate = quats[rot],
                            Scale = scale
                        };
                        Workspace.Prop(lm);
                        Workspace.Prop(new PutModelObject()
                        {
                            clsName = "m_glb", name = "glb1", newPosition = Vector3.Zero,
                            newQuaternion = Quaternion.Identity
                        });
                        ;
                        new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();
                    }

            });
        }
    }
}