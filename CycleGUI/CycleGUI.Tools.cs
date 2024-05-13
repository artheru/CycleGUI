using System;
using System.Collections.Generic;

namespace CycleGUI;

public static partial class GUI
{
    internal static Dictionary<string, (int pid, string fn)> idPidNameMap=new();
    internal static string AddFileLink(int pid, string fn)
    {
        var id = Guid.NewGuid().ToString();
        idPidNameMap[id] = (pid, fn);
        return id;
    }
}