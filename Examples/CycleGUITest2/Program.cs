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
            var scale = 0.1f;
            var lm = new LoadModel()
            {
                detail = default,
                name = "m_glb"
            };
            var rot = 1;
            GUI.PromptPanel(pb =>
            {
                var model3dPosChanged = pb.DragFloat("center X", ref model3dX, 0.01f, -3, 3);
                model3dPosChanged |= pb.DragFloat("center Y", ref model3dY, 0.01f, -3, 3);
                model3dPosChanged |= pb.DragFloat("scale", ref scale, 0.0001f, 0, 1000);

                pb.RadioButtons("Rotation", ["NoRotation", "X", "Y"], ref rot, true);
                var quats = new Quaternion[]
                {
                    Quaternion.Identity,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI / 2)
                };
                if (pb.Button("Load and show glb"))
                if (pb.OpenFile("select file", "glb", out var fn))
                {
                    lm.detail = new Workspace.ModelDetail(File.ReadAllBytes(fn))
                    {
                        Center = new Vector3(model3dX, model3dY, 0),
                        Rotate = quats[rot],
                        Scale = scale
                    };
                    Workspace.Prop(lm);
                    Workspace.Prop(new PutModelObject()
                        { clsName = "m_glb", name = "glb1", newPosition = Vector3.Zero, newQuaternion = Quaternion.Identity }); ;
                    new SetModelObjectProperty() { namePattern = "glb1", baseAnimId = 0 }.IssueToDefault();
                }

            });
        }
    }
}