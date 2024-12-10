using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;

namespace Annotator.RenderTypes
{
    internal class CylinderRender : BasicRender
    {
        public CylinderRender(string name, float r, float g, float b, float height, float topRadius, float bottomRadius) : base(name, r, g, b)
        {
            Height = _lastHeight = height;
            TopRadius = _lastTopRadius = topRadius;
            BottomRadius = _lastBottomRadius = bottomRadius;
        }

        public override void ParameterChangeAction(PanelBuilder pb)
        {
            pb.DragFloat($"height {Name}", ref Height, 0.001f, 0.1f, 100f);
            pb.DragFloat($"top radius {Name}", ref TopRadius, 0.001f, 0.1f, 100f);
            pb.CheckBox($"same bottom & top radius {Name}", ref TopBottomSame);
            if (!TopBottomSame) pb.DragFloat($"bottom radius {Name}", ref BottomRadius, 0.001f, 0.1f, 100f);
            else BottomRadius = TopRadius;
        }

        public override bool NeedsUpdate()
        {
            return Height != _lastHeight || TopRadius != _lastTopRadius || BottomRadius != _lastBottomRadius;
        }

        public override void Update()
        {
            _lastHeight = Height;
            _lastTopRadius = TopRadius;
            _lastBottomRadius = BottomRadius;
        }

        public float Height;
        public float TopRadius;
        public float BottomRadius;
        public bool TopBottomSame = true;

        private float _lastHeight;
        private float _lastTopRadius;
        private float _lastBottomRadius;
    }
}
