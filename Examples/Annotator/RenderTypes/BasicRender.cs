using System.Numerics;
using CycleGUI;

namespace Annotator.RenderTypes
{
    public class BasicRender
    {
        public BasicRender(string name, float r, float g, float b)
        {
            Name = name;
            ColorR = r;
            ColorG = g;
            ColorB = b;

            Pos = Vector3.Zero;
            Rot = Quaternion.Identity;
        }

        public virtual bool NeedsUpdate()
        {
            return false;
        }

        public string Name;
        public float[] Mesh;
        public bool Shown = false;
        public float ColorR;
        public float ColorG;
        public float ColorB;
        public float ColorA = 255f;
        public bool Dissected = false;

        public Vector3 Pos;
        public Quaternion Rot;
    }
}
