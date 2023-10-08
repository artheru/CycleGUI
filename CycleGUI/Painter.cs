using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace CycleGUI;

public class Painter
{
    public class TerminalPainterStatus
    {
        internal int commitingFrame = 0;
        internal DateTime commitFrameTime;

        internal int commitedDots = 0, commitedText = 0;
        internal bool inited = false;
    }

    internal static ConcurrentDictionary<string, Painter> _allPainters = new();

    public static Painter GetPainter(string name)
    {
        if (_allPainters.TryGetValue(name, out var p)) return p;
        var painter = new Painter();
        _allPainters[name] = painter;
        return painter;
    }

    internal List<(Vector4, uint)> drawingDots = new(), cachedDots;

    internal int frameCnt = 0;
    internal DateTime prevFrameTime;


    internal List<(Vector3 pos, string text, uint)> drawingTexts = new(), cachedTexts;

    public void Clear()
    {
        lock (this)
        {
            frameCnt += 1;
            prevFrameTime = DateTime.Now;

            cachedDots = drawingDots;
            cachedTexts = drawingTexts;

            drawingDots = new();
            drawingTexts = new();
        }
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
            drawingDots.Add((new Vector4(xyz, size), color.RGBA8()));
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
            drawingDots.Add((new Vector4(xyz / 1000, size), color.RGBA8()));
    }

    public void DrawText(Color color, Vector3 tCenter, string s)
    {
        lock (this)
            drawingTexts.Add((tCenter / 1000, s, color.RGBA8()));
    }
}