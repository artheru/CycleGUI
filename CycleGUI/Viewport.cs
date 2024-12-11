using System;
using System.Collections.Generic;
using System.Text;

namespace CycleGUI
{
    public class Viewport :Panel
    {
        public Viewport(Terminal terminal1): base(terminal1) { }

        public PanelBuilder.CycleGUIHandler GetViewportHandler(Action<Panel> panelProperty)
        {
            return pb =>
            {
                panelProperty?.Invoke(pb.Panel);
                pb.commands.Add(new PanelBuilder.ByteCommand(new CB().Append(23).AsMemory()));
            };
        }
    }
}
