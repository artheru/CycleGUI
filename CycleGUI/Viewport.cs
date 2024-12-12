using System;
using System.Collections.Generic;
using System.Text;

namespace CycleGUI
{
    public class Viewport :Panel
    {
        private ViewportSubTerminal vterminal;
        public override void Exit()
        {
            // destroy terminal.
            vterminal.Close();
            base.Exit();
        }

        public Viewport(Terminal terminal1): base(terminal1) {  }

        public PanelBuilder.CycleGUIHandler GetViewportHandler(Action<Panel> panelProperty)
        {
            return pb =>
            {
                panelProperty?.Invoke(pb.Panel);
                pb.commands.Add(new PanelBuilder.ByteCommand(new CB().Append(23).AsMemory()));
            };
        }

        public class ViewportSubTerminal : Terminal
        {
            internal override void SwapBuffer(int[] mentionedPid)
            {
                // not allowed to swap.
                throw new Exception("not allowed");
            }
        }
    }
}
