using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;

namespace Annotator.RenderTypes
{
    internal class CylindricalBaseRender : BasicRender
    {
        public CylindricalBaseRender(
            string name, float r, float g, float b,
            float bottom_topRadius = 0.38f, float bottom_bottomRadius = 0.42f, float bottom_height = 0.08f,
            float top_topRadius = 0.3f, float top_bottomRadius = 0.36f, float top_height = 0.075f,
            float posX = 0.016f, float posY = -0.6055f, float posZ = 0.026f,
            float rotX = 0, float rotY = 0, float rotZ = 0
        ) : base(name, r, g, b)
        {
            Bottom_topRadius = _lastBottom_topRadius = bottom_topRadius;
            Bottom_bottomRadius = _lastBottom_bottomRadius = bottom_bottomRadius;
            Bottom_height = _lastBottom_height = bottom_height;

            Top_topRadius = _lastTop_topRadius = top_topRadius;
            Top_bottomRadius = _lastTop_bottomRadius = top_bottomRadius;
            Top_height = _lastTop_height = top_height;

            PosX = _lastPosX = posX;
            PosY = _lastPosY = posY;
            PosZ = _lastPosZ = posZ;

            RotX = _lastRotX = rotX;
            RotY = _lastRotY = rotY;
            RotZ = _lastRotZ = rotZ;
        }

        public override void ParameterChangeAction(PanelBuilder pb)
        {
            pb.SeparatorText($"{Name} - Bottom Size (top radius, bottom radius, height)");
            pb.DragFloat($"Bottom Top Radius {Name}", ref Bottom_topRadius, 0.001f, 0.01f, 100f);
            pb.DragFloat($"Bottom Bottom Radius {Name}", ref Bottom_bottomRadius, 0.001f, 0.01f, 100f);
            pb.DragFloat($"Bottom Height {Name}", ref Bottom_height, 0.001f, 0.01f, 100f);

            pb.SeparatorText($"{Name} - Top Size (top radius, bottom radius, height)");
            pb.DragFloat($"Top Top Radius {Name}", ref Top_topRadius, 0.001f, 0.01f, 100f);
            pb.DragFloat($"Top Bottom Radius {Name}", ref Top_bottomRadius, 0.001f, 0.01f, 100f);
            pb.DragFloat($"Top Height {Name}", ref Top_height, 0.001f, 0.01f, 100f);

            // pb.SeparatorText($"{Name} - Position and Rotation");
            // pb.DragFloat($"Pos X {Name}", ref PosX, 0.01f, -100f, 100f);
            // pb.DragFloat($"Pos Y {Name}", ref PosY, 0.01f, -100f, 100f);
            // pb.DragFloat($"Pos Z {Name}", ref PosZ, 0.01f, -100f, 100f);
            //
            // pb.DragFloat($"Rot X {Name} (deg)", ref RotX, 1f, -180f, 180f);
            // pb.DragFloat($"Rot Y {Name} (deg)", ref RotY, 1f, -180f, 180f);
            // pb.DragFloat($"Rot Z {Name} (deg)", ref RotZ, 1f, -180f, 180f);
        }

        public override bool NeedsUpdate()
        {
            return
                Bottom_topRadius != _lastBottom_topRadius ||
                Bottom_bottomRadius != _lastBottom_bottomRadius ||
                Bottom_height != _lastBottom_height ||
                Top_topRadius != _lastTop_topRadius ||
                Top_bottomRadius != _lastTop_bottomRadius ||
                Top_height != _lastTop_height ||
                PosX != _lastPosX || PosY != _lastPosY || PosZ != _lastPosZ ||
                RotX != _lastRotX || RotY != _lastRotY || RotZ != _lastRotZ;
        }

        public override void Update()
        {
            _lastBottom_topRadius = Bottom_topRadius;
            _lastBottom_bottomRadius = Bottom_bottomRadius;
            _lastBottom_height = Bottom_height;

            _lastTop_topRadius = Top_topRadius;
            _lastTop_bottomRadius = Top_bottomRadius;
            _lastTop_height = Top_height;

            _lastPosX = PosX; _lastPosY = PosY; _lastPosZ = PosZ;
            _lastRotX = RotX; _lastRotY = RotY; _lastRotZ = RotZ;
        }

        public float Bottom_topRadius;
        public float Bottom_bottomRadius;
        public float Bottom_height;

        public float Top_topRadius;
        public float Top_bottomRadius;
        public float Top_height;

        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ;

        private float _lastBottom_topRadius;
        private float _lastBottom_bottomRadius;
        private float _lastBottom_height;

        private float _lastTop_topRadius;
        private float _lastTop_bottomRadius;
        private float _lastTop_height;

        private float _lastPosX, _lastPosY, _lastPosZ;
        private float _lastRotX, _lastRotY, _lastRotZ;
    }
}
