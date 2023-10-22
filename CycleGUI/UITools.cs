using System;
using System.Collections.Generic;
using System.Text;

namespace CycleGUI
{
    public class UITools
    {
        public static void Alert(string prompt)
        {
            GUI.PromptAndWaitPanel(pb2 =>
            {
                pb2.Panel.TopMost(true).AutoSize(true).ShowTitle(prompt).Modal(true);
                if (pb2.ButtonGroups("", new[] { "OK" }, out var bid))
                    pb2.Panel.Exit();
            });
        }
        public static bool Input(string prompt, string defVal, out string val, string hint="", string title="Input Required")
        {
            var retTxt = "";
            var ret= GUI.WaitPanelResult<bool>(pb2 =>
            {
                pb2.Panel.TopMost(true).InitSize(320).ShowTitle(title).Modal(true);
                (retTxt, var done)= pb2.TextInput(prompt, defVal, hint);
                if (done)
                    pb2.Exit(true);

                if (pb2.ButtonGroups("", new[] { "OK", "Cancel" }, out var bid))
                {
                    if (bid == 0)
                        pb2.Exit(true);
                    else if (bid == 1)
                        pb2.Exit(false);
                }
            });
            val = retTxt;
            return ret;
        }
    }
}
