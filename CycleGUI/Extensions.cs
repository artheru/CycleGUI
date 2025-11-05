using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace CycleGUI
{
    public static class Extensions
    {
        
        // RR GG BB AA.
        public static uint RGBA8(this System.Drawing.Color color)
        {
            uint abgr = ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | (uint)color.R;
            return abgr;
        }

        public static Vector4 Vector4(this System.Drawing.Color color)
        {
            return new Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
        }
    }


}
