using System;
using System.Collections.Generic;
using System.Text;

namespace CycleGUI
{
    public class UITools
    {
        public static bool Input(string prompt, string defVal, out string val, string hint="")
        {
            var retTxt = "";
            var ret= GUI.WaitPanelResult<bool>(pb2 =>
            {
                pb2.Panel.TopMost(true).AutoSize(true).ShowTitle(prompt).Interacting(true);
                retTxt = pb2.TextInput("input", defVal, hint);

                if (pb2.ButtonGroups("> ", new[] { "OK", "Cancel" }, out var bid))
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
