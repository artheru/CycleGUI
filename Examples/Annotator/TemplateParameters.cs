using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Annotator
{
    internal class TemplateParameters
    {
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Rotation { get; set; } = Vector3.Zero;
        public int NumLevels { get; set; } = 2;
        public Vector3 Level1Size { get; set; } = new Vector3(0.21f, 0.86f, 0.21f);
        public Vector2 Level2Size { get; set; } = new Vector2(0.045f, 0.52f);
        public Vector2 Level3Size { get; set; } = new Vector2(0.04f, 0.21f);
        public Vector2 Level4Size { get; set; } = new Vector2(0.5f, 0.3f);
        public Vector3 OuterSize { get; set; } = new Vector3(0.04f, 0.04f, 0.22f);
        public Vector3 InnerSize { get; set; } = new Vector3(0.035f, 0.035f, 0.21f);
    }

}
