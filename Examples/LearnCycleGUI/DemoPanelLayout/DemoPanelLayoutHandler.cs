using CycleGUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LearnCycleGUI.DemoPanelLayout
{
    internal class DemoPanelLayoutHandler
    {
        public static PanelBuilder.CycleGUIHandler PreparePanel()
        {
            var initPosType = 0;
            var pinned = false;
            var leftFloat = 0f;
            var topFloat = 0f;
            var pivotX = 0f;
            var pivotY = 0f;
            var screenPivotX = 0f;
            var screenPivotY = 0f;
            var docking = 4;

            var dockEnums = new[]
            {
                Panel.Docking.Left,
                Panel.Docking.Top,
                Panel.Docking.Right,
                Panel.Docking.Bottom,
                Panel.Docking.None
            };

            return pb =>
            {
                if (pb.Closing())
                {
                    Program.CurrentPanels.Remove(pb.Panel);
                    pb.Panel.Exit();
                    return;
                }

                pb.SeparatorText("Initial Position");

                pb.RadioButtons("initPosType", ["Absolute", "Relative"], ref initPosType);

                if (initPosType == 0) pb.CheckBox("Pinned", ref pinned);

                pb.DragFloat("Left", ref leftFloat, 1f, 0, 200);
                pb.DragFloat("Top", ref topFloat, 1f, 0, 200);
                pb.DragFloat("Pivot X", ref pivotX, 0.01f, 0, 1);
                pb.DragFloat("Pivot Y", ref pivotY, 0.01f, 0, 1);
                pb.DragFloat("Screen Pivot X", ref screenPivotX, 0.01f, 0, 1);
                pb.DragFloat("Screen Pivot Y", ref screenPivotY, 0.01f, 0, 1);

                pb.SeparatorText("Initial Docking");

                pb.DropdownBox("Docking", ["Left", "Top", "Right", "Bottom", "None"], ref docking);

                if (_testPanel == null && pb.Button("Add panel"))
                {
                    _testPanel = GUI.DeclarePanel();
                    if (initPosType == 0)
                        _testPanel = _testPanel.InitPos(pinned, (int)leftFloat, (int)topFloat, pivotX, pivotY,
                            screenPivotX, screenPivotY);
                    // else _testPanel.InitPosRelative()

                    _testPanel.SetDefaultDocking(dockEnums[docking], false).Define(pb =>
                    {
                        if (pb.Closing())
                        {
                            pb.Panel.Exit();
                            _testPanel = null;
                        }
                    });
                }
            };
        }

        private static Panel _testPanel;
    }
}
