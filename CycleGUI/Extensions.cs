using System;
using System.Collections.Generic;
using System.Text;

namespace CycleGUI
{
    public static class Extensions
    {
        public static uint RGBA8(this System.Drawing.Color color)
        {
            uint abgr = ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | (uint)color.R;
            return abgr;
        }
    }


}
