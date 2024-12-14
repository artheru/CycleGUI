using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;

namespace Annotator.RenderTypes
{
    internal class SemiRingBracketRender : BasicRender
    {
        public SemiRingBracketRender(
            string name, float r, float g, float b,
            float pivot_size_radius = 0.025f, float pivot_size_height = 1.505f,
            float pivot_continuity_val = 1f,
            float pivot_seperation_val = 0f,
            float endpoint_radius_val = 0.06f,
            float bracket_size0 = 0.785f, float bracket_size1 = 0.675f, float bracket_size2 = 0.06f,
            float bracket_offset_val = -0.02f,
            float bracket_rotation_val = 168f,
            float bracket_exist_angle_val = 223f,
            float posX = 0.016f, float posY = 0.147f, float posZ = 0.036f,
            float rotX = 0, float rotY = 0, float rotZ = 0,
            float has_top_endpoint_val = 1f, float has_bottom_endpoint_val = 0f
        ) : base(name, r, g, b)
        {
            Pivot_size_radius = _lastPivot_size_radius = pivot_size_radius;
            Pivot_size_height = _lastPivot_size_height = pivot_size_height;
            Pivot_continuity_val = _lastPivot_continuity_val = pivot_continuity_val;
            Pivot_seperation_val = _lastPivot_seperation_val = pivot_seperation_val;
            Endpoint_radius_val = _lastEndpoint_radius_val = endpoint_radius_val;

            Bracket_size0 = _lastBracket_size0 = bracket_size0;
            Bracket_size1 = _lastBracket_size1 = bracket_size1;
            Bracket_size2 = _lastBracket_size2 = bracket_size2;

            Bracket_offset_val = _lastBracket_offset_val = bracket_offset_val;
            Bracket_rotation_val = _lastBracket_rotation_val = bracket_rotation_val;
            Bracket_exist_angle_val = _lastBracket_exist_angle_val = bracket_exist_angle_val;

            Has_top_endpoint_val = _lastHas_top_endpoint_val = has_top_endpoint_val;
            Has_bottom_endpoint_val = _lastHas_bottom_endpoint_val = has_bottom_endpoint_val;

            PosX = _lastPosX = posX;
            PosY = _lastPosY = posY;
            PosZ = _lastPosZ = posZ;

            RotX = _lastRotX = rotX;
            RotY = _lastRotY = rotY;
            RotZ = _lastRotZ = rotZ;
        }

        public override void ParameterChangeAction(PanelBuilder pb)
        {
            pb.SeparatorText($"{Name} - Pivot");
            pb.DragFloat($"Pivot Radius {Name}", ref Pivot_size_radius, 0.001f, 0.001f, 100f);
            pb.DragFloat($"Pivot Height {Name}", ref Pivot_size_height, 0.001f, 0.01f, 100f);

            pb.DragFloat($"Pivot Continuity {Name}", ref Pivot_continuity_val, 1f, 0f, 1f);
            pb.DragFloat($"Pivot Separation {Name}", ref Pivot_seperation_val, 0.001f, 0f, 10f);

            var hasTop = Has_top_endpoint_val > 0;
            var hasBottom = Has_bottom_endpoint_val > 0;
            pb.Toggle($"Has Top Endpoint {Name}", ref hasTop);
            pb.Toggle($"Has Bottom Endpoint {Name}", ref hasBottom);
            Has_top_endpoint_val = hasTop ? 1 : 0;
            Has_bottom_endpoint_val = hasBottom ? 1 : 0;
            if (hasTop || hasBottom)
                pb.DragFloat($"Endpoint Radius {Name}", ref Endpoint_radius_val, 0.01f, 0.001f, 100f);

            pb.SeparatorText($"{Name} - Bracket");
            pb.DragFloat($"Bracket Size0 {Name}", ref Bracket_size0, 0.001f, 0.01f, 100f);
            pb.DragFloat($"Bracket Size1 {Name}", ref Bracket_size1, 0.001f, 0.01f, 100f);
            pb.DragFloat($"Bracket Size2 {Name}", ref Bracket_size2, 0.001f, 0.01f, 100f);
            pb.DragFloat($"Bracket Offset {Name}", ref Bracket_offset_val, 0.001f, -10f, 10f);
            pb.DragFloat($"Bracket Rotation {Name} (deg)", ref Bracket_rotation_val, 0.1f, -180f, 180f);
            pb.DragFloat($"Bracket Exist Angle {Name} (deg)", ref Bracket_exist_angle_val, 0.1f, -180f, 360f);

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
                Pivot_size_radius != _lastPivot_size_radius ||
                Pivot_size_height != _lastPivot_size_height ||
                Pivot_continuity_val != _lastPivot_continuity_val ||
                Pivot_seperation_val != _lastPivot_seperation_val ||
                Endpoint_radius_val != _lastEndpoint_radius_val ||
                Bracket_size0 != _lastBracket_size0 ||
                Bracket_size1 != _lastBracket_size1 ||
                Bracket_size2 != _lastBracket_size2 ||
                Bracket_offset_val != _lastBracket_offset_val ||
                Bracket_rotation_val != _lastBracket_rotation_val ||
                Bracket_exist_angle_val != _lastBracket_exist_angle_val ||
                Has_top_endpoint_val != _lastHas_top_endpoint_val ||
                Has_bottom_endpoint_val != _lastHas_bottom_endpoint_val ||
                PosX != _lastPosX || PosY != _lastPosY || PosZ != _lastPosZ ||
                RotX != _lastRotX || RotY != _lastRotY || RotZ != _lastRotZ;
        }

        public override void Update()
        {
            _lastPivot_size_radius = Pivot_size_radius;
            _lastPivot_size_height = Pivot_size_height;
            _lastPivot_continuity_val = Pivot_continuity_val;
            _lastPivot_seperation_val = Pivot_seperation_val;
            _lastEndpoint_radius_val = Endpoint_radius_val;

            _lastBracket_size0 = Bracket_size0;
            _lastBracket_size1 = Bracket_size1;
            _lastBracket_size2 = Bracket_size2;
            _lastBracket_offset_val = Bracket_offset_val;
            _lastBracket_rotation_val = Bracket_rotation_val;
            _lastBracket_exist_angle_val = Bracket_exist_angle_val;

            _lastHas_top_endpoint_val = Has_top_endpoint_val;
            _lastHas_bottom_endpoint_val = Has_bottom_endpoint_val;

            _lastPosX = PosX; _lastPosY = PosY; _lastPosZ = PosZ;
            _lastRotX = RotX; _lastRotY = RotY; _lastRotZ = RotZ;
        }

        // Parameters
        public float Pivot_size_radius;
        public float Pivot_size_height;
        public float Pivot_continuity_val;
        public float Pivot_seperation_val;
        public float Endpoint_radius_val;

        public float Bracket_size0;
        public float Bracket_size1;
        public float Bracket_size2;

        public float Bracket_offset_val;
        public float Bracket_rotation_val;
        public float Bracket_exist_angle_val;

        public float Has_top_endpoint_val;
        public float Has_bottom_endpoint_val;

        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ;

        // Last known values
        private float _lastPivot_size_radius;
        private float _lastPivot_size_height;
        private float _lastPivot_continuity_val;
        private float _lastPivot_seperation_val;
        private float _lastEndpoint_radius_val;

        private float _lastBracket_size0;
        private float _lastBracket_size1;
        private float _lastBracket_size2;

        private float _lastBracket_offset_val;
        private float _lastBracket_rotation_val;
        private float _lastBracket_exist_angle_val;

        private float _lastHas_top_endpoint_val;
        private float _lastHas_bottom_endpoint_val;

        private float _lastPosX, _lastPosY, _lastPosZ;
        private float _lastRotX, _lastRotY, _lastRotZ;
    }
}