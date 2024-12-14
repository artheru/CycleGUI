using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CycleGUI;
using System;
using System.Numerics;

namespace Annotator.RenderTypes
{
    internal class StandardSphereRender : BasicRender
    {
        public StandardSphereRender(string name, float r, float g, float b,
            float radius = 0.59f,
            float posX = 0.016f, float posY = 0.122f, float posZ = 0.036f,
            float rotX = 0, float rotY = 0, float rotZ = 0
        ) : base(name, r, g, b)
        {
            Radius = _lastRadius = radius;

            PosX = _lastPosX = posX;
            PosY = _lastPosY = posY;
            PosZ = _lastPosZ = posZ;

            RotX = _lastRotX = rotX;
            RotY = _lastRotY = rotY;
            RotZ = _lastRotZ = rotZ;
        }

        public override void ParameterChangeAction(PanelBuilder pb)
        {
            pb.DragFloat($"Radius {Name}", ref Radius, 0.001f, 0.01f, 100f);

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
                Radius != _lastRadius ||
                PosX != _lastPosX || PosY != _lastPosY || PosZ != _lastPosZ ||
                RotX != _lastRotX || RotY != _lastRotY || RotZ != _lastRotZ;
        }

        public override void Update()
        {
            _lastRadius = Radius;

            _lastPosX = PosX; _lastPosY = PosY; _lastPosZ = PosZ;
            _lastRotX = RotX; _lastRotY = RotY; _lastRotZ = RotZ;
        }

        public float Radius;
        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ;

        private float _lastRadius;
        private float _lastPosX, _lastPosY, _lastPosZ;
        private float _lastRotX, _lastRotY, _lastRotZ;
    }
}

