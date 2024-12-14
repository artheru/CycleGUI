using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;

namespace Annotator.RenderTypes
{
    internal class CylindricalLidRender : BasicRender
    {
        public CylindricalLidRender(
            string name, float r, float g, float b,
            float outerTopRadius, float outerHeight, float outerBottomRadius,
            float innerTopRadius, float innerHeight, float innerBottomRadius,
            float posX = 0, float posY = 0, float posZ = 0,
            float rotX = 0, float rotY = 0, float rotZ = 0
        ) : base(name, r, g, b)
        {
            OuterTopRadius = _lastOuterTopRadius = outerTopRadius;
            OuterHeight = _lastOuterHeight = outerHeight;
            OuterBottomRadius = _lastOuterBottomRadius = outerBottomRadius;

            InnerTopRadius = _lastInnerTopRadius = innerTopRadius;
            InnerHeight = _lastInnerHeight = innerHeight;
            InnerBottomRadius = _lastInnerBottomRadius = innerBottomRadius;

        }

        public override void ParameterChangeAction(PanelBuilder pb)
        {
            pb.SeparatorText($"{Name} - Outer Size (top radius, height, bottom radius)");
            pb.DragFloat($"Outer Top Radius {Name}", ref OuterTopRadius, 0.01f, 0.1f, 100f);
            pb.DragFloat($"Outer Height {Name}", ref OuterHeight, 0.01f, 0.1f, 100f);
            pb.DragFloat($"Outer Bottom Radius {Name}", ref OuterBottomRadius, 0.01f, 0.1f, 100f);

            pb.SeparatorText($"{Name} - Inner Size (top radius, height, bottom radius)");
            pb.DragFloat($"Inner Top Radius {Name}", ref InnerTopRadius, 0.01f, 0.1f, 100f);
            pb.DragFloat($"Inner Height {Name}", ref InnerHeight, 0.01f, 0.1f, 100f);
            pb.DragFloat($"Inner Bottom Radius {Name}", ref InnerBottomRadius, 0.01f, 0.1f, 100f);
        }

        public override bool NeedsUpdate()
        {
            return
                OuterTopRadius != _lastOuterTopRadius ||
                OuterHeight != _lastOuterHeight ||
                OuterBottomRadius != _lastOuterBottomRadius ||

                InnerTopRadius != _lastInnerTopRadius ||
                InnerHeight != _lastInnerHeight ||
                InnerBottomRadius != _lastInnerBottomRadius;
        }

        public override void Update()
        {
            _lastOuterTopRadius = OuterTopRadius;
            _lastOuterHeight = OuterHeight;
            _lastOuterBottomRadius = OuterBottomRadius;

            _lastInnerTopRadius = InnerTopRadius;
            _lastInnerHeight = InnerHeight;
            _lastInnerBottomRadius = InnerBottomRadius;
        }

        public float OuterTopRadius;
        public float OuterHeight;
        public float OuterBottomRadius;

        public float InnerTopRadius;
        public float InnerHeight;
        public float InnerBottomRadius;

        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ;

        private float _lastOuterTopRadius;
        private float _lastOuterHeight;
        private float _lastOuterBottomRadius;

        private float _lastInnerTopRadius;
        private float _lastInnerHeight;
        private float _lastInnerBottomRadius;
    }
}
