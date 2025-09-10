using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace CycleGUI;

public class Painter
{
    public Terminal terminal = null;

    public class TerminalPainterStatus
    {
        internal int commitingFrame = 0;
        internal DateTime commitFrameTime;

        internal int commitedDots = 0, commitedText = 0, commitedLines = 0, commitedRegions;
        internal bool inited = false;
    }

    internal static ConcurrentDictionary<string, Painter> _allPainters = new();

    /// <summary>
    /// can use {name}_pc, {name}_stext, {name}_lines to transform the temporary draws.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static Painter GetPainter(string name)
    {
        if (_allPainters.TryGetValue(name, out var p)) return p;
        var painter = new Painter();
        _allPainters[name] = painter;
        return painter;
    }

    internal List<(Vector4, uint)> drawingDots = new(), cachedDots;
    internal List<(Vector3 pos, string text, uint)> drawingTexts = new(), cachedTexts;
    internal List<(uint color, Vector3 start, Vector3 end, float width, int arrow_or_vlength, int dashScale)> drawingLines = new(), cachedLines;
    // arrow_or_vlength: 0/1/2: arrow type, >=3: vector type, specifying length.

    // region storage for native fast-path
    internal struct Region3D { public Vector3 center; public uint color; }
    internal List<Region3D> drawingRegions3D = new(), cachedRegions3D;

    internal int frameCnt = 0;
    internal DateTime prevFrameTime;

    public static byte[] DumpPainters()
    {
        //

        throw new NotImplementedException();
    }


    public void Clear()
    {
        lock (this)
        {
            frameCnt += 1;
            prevFrameTime = DateTime.Now;

            cachedDots = drawingDots;
            cachedTexts = drawingTexts;
            cachedLines = drawingLines;
            
            cachedRegions3D = drawingRegions3D;

            drawingDots = new();
            drawingTexts = new();
            drawingLines = new();

            drawingRegions3D = new();
        }
    }

    /// <summary>
    /// draw dot in millimeter.
    /// </summary>
    /// <param name="color"></param>
    /// <param name="xyz"></param>
    /// <param name="size"></param>
    [Obsolete]
    public void DrawDotMM(Color color, Vector3 xyz, float size)
    {
        lock (this)
            drawingDots.Add((new Vector4(xyz / 1000, size), color.RGBA8()));
    }

    /// <summary>
    /// default in meter, draw to {name}_pc point cloud.
    /// </summary>
    public void DrawDot(Color color, Vector3 xyz, float size=1f)
    {
        lock (this)
            drawingDots.Add((new Vector4(xyz, size), color.RGBA8()));
    }

    /// <summary>
    /// draw to {name}_stext spot text.
    /// </summary>
    public void DrawText(Color color, Vector3 tCenter, string s)
    {
        lock (this)
            drawingTexts.Add((tCenter, s, color.RGBA8()));
    }

    [Obsolete]
    public void DrawTextM(Color color, Vector3 tCenter, string s)
    {
        lock (this)
            drawingTexts.Add((tCenter, s, color.RGBA8()));
    }

    [Flags]
    public enum ArrowType
    {
        None, Start, End, 
    }

    /// <summary>
    /// draw to {name}_lines
    /// </summary>
    public void DrawLine(Color color, Vector3 start, Vector3 end, float width=1.0f, ArrowType arrow = ArrowType.None, int dashScale=0)
    {
        lock (this)
            drawingLines.Add((color.RGBA8(), start, end, width, (int)arrow, dashScale));
    }

    /// <summary>
    /// Draw a vector field arrow. Length is in pixels.
    /// </summary>
    /// <param name="from">Start position in world coordinates</param>
    /// <param name="dir">Direction vector (normalized)</param>
    /// <param name="color">Arrow color</param>
    /// <param name="length">Length in pixels</param>
    public void DrawVector(Vector3 from, Vector3 dir, Color color, float width=1.0f, int pixels=10)
    {
        lock (this)
        {
            // Normalize direction vector
            dir = Vector3.Normalize(dir);
            
            // We'll calculate the end position in the shader based on pixel length
            // For now, store the direction in the end position and mark as vector
            drawingLines.Add((color.RGBA8(), from, from + dir, width, Math.Max(3, pixels), 0));
        }
    }

    /// <summary>
    /// Draw a 3D region voxel at position. Region is point-based; size comes from current quantize.
    /// </summary>
    public void DrawRegion3D(Color color, Vector3 center)
    {
        lock (this)
        {
            drawingRegions3D.Add(new Region3D { center = center, color = color.RGBA8()});
        }
    }
}