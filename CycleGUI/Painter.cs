using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace CycleGUI;

public class Painter
{
    internal static ConcurrentDictionary<string, Painter> _allPainters = new();

    public static Painter GetPainter(string name)
    {
        if (_allPainters.TryGetValue(name, out var p)) return p;
        var painter = new Painter();
        _allPainters[name] = painter;
        return painter;
    }
    internal bool cleared = false;

    internal List<(Vector4, uint)> dots = new();
    internal int commitedDots = 0;
    internal bool inited = false;

    internal List<(Vector3 pos, string text, uint)> text = new();
    internal int commitedText = 0;

    public void Clear()
    {
        cleared = true;
        dots.Clear();
    }

    /// <summary>
    /// draw dot in meter.
    /// </summary>
    /// <param name="color"></param>
    /// <param name="xyz"></param>
    /// <param name="size"></param>
    public void DrawDotM(Color color, Vector3 xyz, float size)
    {
        lock (this)
            dots.Add((new Vector4(xyz, size), color.RGBA8()));
    }

    /// <summary>
    /// default in millimeter.
    /// </summary>
    /// <param name="color"></param>
    /// <param name="xyz"></param>
    /// <param name="size"></param>
    public void DrawDot(Color color, Vector3 xyz, float size)
    {
        lock (this)
            dots.Add((new Vector4(xyz / 1000, size), color.RGBA8()));
    }

    public void DrawText(Color color, Vector3 tCenter, string s)
    {
        lock (this)
            text.Add((tCenter, s, color.RGBA8()));
    }
}