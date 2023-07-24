using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CycleGUI
{
    public class Workspace
    {
        public void ReloadWorkspace()
        {

        }

        public class WorkspaceQueueGenerator
        {
            public CB cb=new CB();
            
            public byte[] End()
            {
                cb.Append(-1); 
                return cb.ToArray();
            }

            public void AddVolatilePointCloud(string name, int capacity)
            {
                cb.Append(0);
                cb.Append(name);
                cb.Append(true);
                cb.Append(capacity);
                cb.Append(0); //initN, note xyzSz/color is ignored.
                cb.Append(0f); //position
                cb.Append(0f);
                cb.Append(0f);
                cb.Append(0f);
                cb.Append(0f);
                cb.Append(0f);
                cb.Append(1f);
            }
            public void AppendVolatilePoints(string name, int offset, List<(Vector4, int)> list)
            {
                cb.Append(1);
                cb.Append(name);
                cb.Append(list.Count - offset);
                for (var i = offset; i < list.Count; i++)
                {
                    var tuple = list[i];
                    cb.Append(tuple.Item1.X);
                    cb.Append(tuple.Item1.Y);
                    cb.Append(tuple.Item1.Z);
                    cb.Append(tuple.Item1.W);
                }

                for (var i = offset; i < list.Count; i++)
                {
                    var tuple = list[i];
                    cb.Append(tuple.Item2);
                }
            }

            public void ClearVolatilePoints(string name)
            {
                cb.Append(2);
                cb.Append(name);
            }
            
        }

        public static byte[] GetChanging()
        {
            WorkspaceQueueGenerator generator = new();
            foreach (var name in _allPainters.Keys.ToArray())
            {
                if (!_allPainters.TryGetValue(name, out var painter)) continue;
                if (!painter.added)
                {
                    painter.added = true;
                    generator.AddVolatilePointCloud($"{name}_pc", 1000000);
                }

                if (painter.needClear)
                {
                    painter.needClear = false;
                    painter.commitedN = 0;
                    generator.ClearVolatilePoints($"{name}_pc");
                }

                if (painter.commitedN < painter.dots.Count)
                {
                    generator.AppendVolatilePoints($"{name}_pc", painter.commitedN, painter.dots);
                    painter.commitedN = painter.dots.Count;
                }
            }

            return generator.End();
        }

        internal static ConcurrentDictionary<string, Painter> _allPainters = new();
        public class Painter
        {
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

        public static Painter GetPainter(string name)
        {
            if (_allPainters.TryGetValue(name, out var p)) return p;
            var painter = new Painter();
            _allPainters[name] = painter;
            return painter;
        }
    }
}
