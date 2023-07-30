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

    internal List<(Vector4, int)> dots = new();
    internal int commitedN = 0;
    internal bool needClear = false;
    internal bool added = false;


    public void Clear()
    {
        needClear = true;
        dots.Clear();
    }

    public void DrawDot(Color color, Vector3 xyz, float size)
    {
        dots.Add((new Vector4(xyz,size), color.ToArgb()));
    }
}