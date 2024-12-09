using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;

namespace Annotator.RenderTypes
{
    internal class BasicRender
    {
        public BasicRender(string name, float r, float g, float b)
        {
            Name = name;
            ColorR = r;
            ColorG = g;
            ColorB = b;
        }

        public virtual bool NeedsUpdate()
        {
            return false;
        }

        public virtual void Update()
        {
            
        }

        public virtual void ParameterChangeAction(PanelBuilder pb)
        {

        }

        public string Name;
        public float[] Mesh;
        public bool Shown = false;
        public float ColorR;
        public float ColorG;
        public float ColorB;
        public float ColorA = 255f;
        public bool Dissected = false;
    }
}
