using System.Numerics;
using CycleGUI;

namespace Annotator.RenderTypes
{
    internal class MultilevelBodyRender : BasicRender
    {
        public MultilevelBodyRender(
            string name, float r, float g, float b,
            int numLevels,
            float level1TopRadius, float level1Height, float level1BottomRadius,
            float level2TopRadius, float level2Height, float level2BottomRadius,
            float level3TopRadius, float level3Height, float level3BottomRadius,
            float level4TopRadius, float level4Height, float level4BottomRadius
        ) : base(name, r, g, b)
        {
            NumLevels = _lastNumLevels = numLevels;

            Level1TopRadius = _lastLevel1TopRadius = level1TopRadius;
            Level1Height = _lastLevel1Height = level1Height;
            Level1BottomRadius = _lastLevel1BottomRadius = level1BottomRadius;

            Level2TopRadius = _lastLevel2TopRadius = level2TopRadius;
            Level2Height = _lastLevel2Height = level2Height;
            Level2BottomRadius = _lastLevel2BottomRadius = level2BottomRadius;

            Level3TopRadius = _lastLevel3TopRadius = level3TopRadius;
            Level3Height = _lastLevel3Height = level3Height;
            Level3BottomRadius = _lastLevel3BottomRadius = level3BottomRadius;

            Level4TopRadius = _lastLevel4TopRadius = level4TopRadius;
            Level4Height = _lastLevel4Height = level4Height;
            Level4BottomRadius = _lastLevel4BottomRadius = level4BottomRadius;
        }

        public override void ParameterChangeAction(PanelBuilder pb)
        {
            // We only have DragFloat, so let's create a temporary float.
            float tmpNumLevels = NumLevels; // copy current int value to float
            pb.DragFloat($"Num Levels {Name}", ref tmpNumLevels, 1f, 1f, 4f);
            // Convert the float back to int after user interaction.
            NumLevels = (int)Math.Round(tmpNumLevels);

            pb.SeparatorText($"{Name} - Level 1 Size (top radius, height, bottom radius)");
            pb.DragFloat($"L1 Top Radius {Name}", ref Level1TopRadius, 0.01f, 0.1f, 100f);
            pb.DragFloat($"L1 Height {Name}", ref Level1Height, 0.01f, 0.1f, 100f);
            pb.DragFloat($"L1 Bottom Radius {Name}", ref Level1BottomRadius, 0.01f, 0.1f, 100f);

            if (NumLevels >= 2)
            {
                pb.SeparatorText($"{Name} - Level 2 Size");
                pb.DragFloat($"L2 Top Radius {Name}", ref Level2TopRadius, 0.01f, 0.1f, 100f);
                pb.DragFloat($"L2 Height {Name}", ref Level2Height, 0.01f, 0.1f, 100f);
                pb.DragFloat($"L2 Bottom Radius {Name}", ref Level2BottomRadius, 0.01f, 0.1f, 100f);
            }

            if (NumLevels >= 3)
            {
                pb.SeparatorText($"{Name} - Level 3 Size");
                pb.DragFloat($"L3 Top Radius {Name}", ref Level3TopRadius, 0.01f, 0.1f, 100f);
                pb.DragFloat($"L3 Height {Name}", ref Level3Height, 0.01f, 0.1f, 100f);
                pb.DragFloat($"L3 Bottom Radius {Name}", ref Level3BottomRadius, 0.01f, 0.1f, 100f);
            }

            if (NumLevels == 4)
            {
                pb.SeparatorText($"{Name} - Level 4 Size");
                pb.DragFloat($"L4 Top Radius {Name}", ref Level4TopRadius, 0.01f, 0.1f, 100f);
                pb.DragFloat($"L4 Height {Name}", ref Level4Height, 0.01f, 0.1f, 100f);
                pb.DragFloat($"L4 Bottom Radius {Name}", ref Level4BottomRadius, 0.01f, 0.1f, 100f);
            }
        }


        public override bool NeedsUpdate()
        {
            return
                NumLevels != _lastNumLevels ||

                Level1TopRadius != _lastLevel1TopRadius ||
                Level1Height != _lastLevel1Height ||
                Level1BottomRadius != _lastLevel1BottomRadius ||

                Level2TopRadius != _lastLevel2TopRadius ||
                Level2Height != _lastLevel2Height ||
                Level2BottomRadius != _lastLevel2BottomRadius ||

                Level3TopRadius != _lastLevel3TopRadius ||
                Level3Height != _lastLevel3Height ||
                Level3BottomRadius != _lastLevel3BottomRadius ||

                Level4TopRadius != _lastLevel4TopRadius ||
                Level4Height != _lastLevel4Height ||
                Level4BottomRadius != _lastLevel4BottomRadius;
        }

        public override void Update()
        {
            _lastNumLevels = NumLevels;

            _lastLevel1TopRadius = Level1TopRadius;
            _lastLevel1Height = Level1Height;
            _lastLevel1BottomRadius = Level1BottomRadius;

            _lastLevel2TopRadius = Level2TopRadius;
            _lastLevel2Height = Level2Height;
            _lastLevel2BottomRadius = Level2BottomRadius;

            _lastLevel3TopRadius = Level3TopRadius;
            _lastLevel3Height = Level3Height;
            _lastLevel3BottomRadius = Level3BottomRadius;

            _lastLevel4TopRadius = Level4TopRadius;
            _lastLevel4Height = Level4Height;
            _lastLevel4BottomRadius = Level4BottomRadius;

        }

        public int NumLevels;
        public float Level1TopRadius;
        public float Level1Height;
        public float Level1BottomRadius;

        public float Level2TopRadius;
        public float Level2Height;
        public float Level2BottomRadius;

        public float Level3TopRadius;
        public float Level3Height;
        public float Level3BottomRadius;

        public float Level4TopRadius;
        public float Level4Height;
        public float Level4BottomRadius;

        private int _lastNumLevels;

        private float _lastLevel1TopRadius;
        private float _lastLevel1Height;
        private float _lastLevel1BottomRadius;

        private float _lastLevel2TopRadius;
        private float _lastLevel2Height;
        private float _lastLevel2BottomRadius;

        private float _lastLevel3TopRadius;
        private float _lastLevel3Height;
        private float _lastLevel3BottomRadius;

        private float _lastLevel4TopRadius;
        private float _lastLevel4Height;
        private float _lastLevel4BottomRadius;
    }
}
