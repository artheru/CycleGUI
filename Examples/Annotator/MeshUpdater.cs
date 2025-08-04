using System.Numerics;
using Annotator.RenderTypes;
using CycleGUI;
using CycleGUI.API;

namespace Annotator
{
    public static class MeshUpdater
    {
        public static void UpdateMesh(BasicRender options, bool firstPut, float zBias = 0.0f)
        {
            if (options.Mesh == null || options.Mesh.Length == 0)
            {
                Console.WriteLine($"Warning: Cannot update mesh for '{options.Name}' because mesh data is empty.");
                return;
            }

            Workspace.AddProp(new DefineMesh()
            {
                name = options.Name,
                clsname = $"{options.Name}-cls",
                positions = options.Mesh,
                color = ConcatHexABGR(options.ColorA, options.ColorB, options.ColorG, options.ColorR),
                smooth = false,
            });

            if (firstPut)
            {
                var biasedPosition = new Vector3(options.Pos.X, options.Pos.Y, options.Pos.Z + zBias);
                Workspace.AddProp(new PutModelObject()
                {
                    clsName = $"{options.Name}-cls",
                    name = options.Name,
                    newPosition = biasedPosition,
                    newQuaternion = options.Rot
                });
            }
        }

        public static uint ConcatHexABGR(float a, float b, float g, float r)
        {
            uint alpha = (uint)(Math.Clamp(a / 100f * 255, 0, 255));  // Normalize alpha to 0-255
            uint blue = (uint)Math.Clamp(b, 0, 255);
            uint green = (uint)Math.Clamp(g, 0, 255);
            uint red = (uint)Math.Clamp(r, 0, 255);

            return (alpha << 24) | (blue << 16) | (green << 8) | red;
        }
    }
}