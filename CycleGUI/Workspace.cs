using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using CycleGUI.API;
using static CycleGUI.Painter;

namespace CycleGUI
{
    public abstract partial class Terminal
    {
        // fields used by workspace:
        public static ConcurrentDictionary<Terminal, int> terminals = [];

        internal Dictionary<Painter, Painter.TerminalPainterStatus> painterMap = new();

        public Terminal()
        {
            Workspace.InitTerminal(this);
            lock (terminals)
                terminals[this] = 0;
        }
        protected Terminal(bool dummy){} //do nothing!

        // public List<Workspace.WorkspaceAPI> Initializers = new();
        // public Dictionary<string, Workspace.WorkspaceAPI> Revokables = new();

        // todo: make cmdseq sync free.
        internal class CmdSeq
        {
            private List<Workspace.WorkspaceAPI> backing = [];
            private Dictionary<string, int> indexing = new();
            public int Length { get=>backing.Count; }

            public void Add(Workspace.WorkspaceAPI api)
            {
                backing.Add(api);
            }
            public void Add(Workspace.WorkspaceAPI api, string index)
            {
                if (indexing.TryGetValue(index, out var value))
                {
                    backing[value] = api;
                }
                else
                {
                    indexing[index] = backing.Count;
                    backing.Add(api);
                }
            }

            public void Clear()
            {
                backing.Clear();
                indexing.Clear();
            }

            public Workspace.WorkspaceAPI[] ToArray()
            {
                return backing.ToArray();
            }

            public void AddRange(IEnumerable<Workspace.WorkspaceAPI> initializers)
            {
                backing.AddRange(initializers);
            }
        }
        internal CmdSeq PendingCmds = new();

        public Dictionary<int, Action<BinaryReader>> registeredWorkspaceFeedbacks = new();
        internal Dictionary<int, List<Workspace.WorkspaceAPI>> pendingUIStates = new();

        internal void QueueUIStateChange(int id, Workspace.WorkspaceAPI what)
        {
            if (pendingUIStates.TryGetValue(id, out var ls))
                ls.Add(what);
            else pendingUIStates[id] = [what];
        }

        internal void ApplyQueuedUIStateChanges()
        {
            if (opStack.Count == 0) return;
            var id = opStack.Peek();
            if (pendingUIStates.TryGetValue(id, out var ls))
                foreach (var api in ls)
                    PendingCmds.Add(api);
        }

        public Stack<int> opStack = new();
    }


    public partial class Workspace
    {
        [Obsolete]
        public static void Prop(WorkspaceProp api)
        {
            api.Submit();
        }

        public static T AddProp<T>(T api) where T: WorkspaceProp
        {
            api.Submit();
            return api;
        }

        public static Dictionary<int, Action<Terminal, BinaryReader>> PropActions = new();
        // RGBA related: 0.
        // Viewport query: 1.
        // Capture Viewport: 2.

        internal static void ReceiveTerminalFeedback(byte[] feedBack, Terminal t)
        {
            // just routing.
            using var ms = new MemoryStream(feedBack);
            using var br = new BinaryReader(ms);

            var id = br.ReadInt32();
            if (id >= 0)
            { 
                // ui operation

                Action<BinaryReader> handler;
                lock (t)
                    if (!t.registeredWorkspaceFeedbacks.TryGetValue(id, out handler))
                        return;
                handler(br);
            }
            else
            { 
                // workspace prop comm
                // tackle prop feedbacks.
                int pid = br.ReadInt32();
                PropActions[pid](t, br);
            }
        }

        internal class WorkspaceQueueGenerator
        {
            public CB cb = new CB();

            public (byte[] buffer, int len) End()
            {
                cb.Append(-1);
                return (cb.Buffer, cb.Len);
                // return cb.AsSpan();
            }

            // used in workspace painter.
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
                cb.Append("");
            }
            // used in workspace painter.
            public void AddVolatileLines(string name, int capacity)
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
                cb.Append("");
            }

            public void AppendVolatilePoints(string name, int offset, List<(Vector4, uint)> list)
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

            public void AppendSpotText(string name, int offset, List<(Vector3 pos, string text, uint)> list)
            {
                cb.Append(12);
                cb.Append(name);
                cb.Append(list.Count - offset);
                for (var i = offset; i < list.Count; i++)
                {
                    var tuple = list[i];
                    cb.Append((byte)1);
                    cb.Append(tuple.Item1.X);
                    cb.Append(tuple.Item1.Y);
                    cb.Append(tuple.Item1.Z);
                    cb.Append(tuple.Item3);
                    cb.Append(tuple.text);
                }
            }

            public void AppendToLineBunch(string name, int offset, List<(uint color, Vector3 start, Vector3 end, float width, ArrowType arrow, int dashScale)> list)
            {
                cb.Append(17);
                cb.Append(name);
                cb.Append(list.Count - offset);
                for (var i = offset; i < list.Count; i++)
                {
                    var tuple = list[i];
                    // this is actually raw 'gpu_line_info' in messyengine.
                    cb.Append(tuple.start.X);
                    cb.Append(tuple.start.Y);
                    cb.Append(tuple.start.Z);
                    cb.Append(tuple.end.X);
                    cb.Append(tuple.end.Y);
                    cb.Append(tuple.end.Z);
                    uint metaint = (uint)(((int)tuple.arrow) | (tuple.dashScale << 8) | (Math.Min(255, (int)tuple.width) << 16));
                    cb.Append(metaint); // convert to float to fit opengl vertex attribute requirement.
                    cb.Append(tuple.color);
                    cb.Append((float)i); //line-id
                }
            }

            public void ClearVolatilePoints(string name)
            {
                cb.Append(2);
                cb.Append(name);
            }

            public void ClearSpotText(string name)
            {
                cb.Append(13);
                cb.Append(name);
            }

            public void ClearTempLine(string name)
            {
                cb.Append(19);
                cb.Append(name);
            }
        }
        
        public static (byte[] buffer, int len) GetWorkspaceCommandForTerminal(Terminal terminal)
        {
            // API calling.

            // for painters, which is highly volatile, we don't distinguish terminal.
            WorkspaceQueueGenerator generator = new();
            foreach (var name in Painter._allPainters.Keys.ToArray())
            {
                if (!Painter._allPainters.TryGetValue(name, out var painter)) continue;
                if (painter.terminal != null && painter.terminal != terminal) continue;
                lock (painter)
                {
                    if (!terminal.painterMap.TryGetValue(painter, out var stat))
                        terminal.painterMap[painter] = stat = new Painter.TerminalPainterStatus();

                    if (!stat.inited)
                    {
                        stat.inited = true;
                        generator.AddVolatilePointCloud($"{name}_pc", 1000);
                    }

                    // logic: prefer to draw cached, if cached is outdated use currently drawing content.
                    if (painter.cachedDots != null && painter.prevFrameTime.AddMilliseconds(300) > DateTime.Now)
                    {
                        if (stat.commitingFrame == painter.frameCnt - 1) // just been validated.
                        {
                            if (stat.commitedDots < painter.cachedDots.Count)
                                generator.AppendVolatilePoints($"{name}_pc", stat.commitedDots, painter.cachedDots);
                            if (stat.commitedText < painter.cachedTexts.Count)
                                generator.AppendSpotText($"{name}_stext", stat.commitedText, painter.cachedTexts);
                            if (stat.commitedLines < painter.cachedLines.Count)
                                generator.AppendToLineBunch($"{name}_lines", stat.commitedLines, painter.cachedLines);

                        }
                        else
                        {
                            // point cloud.
                            generator.ClearVolatilePoints($"{name}_pc");
                            generator.AppendVolatilePoints($"{name}_pc", 0, painter.cachedDots);

                            // text.
                            generator.ClearSpotText($"{name}_stext");
                            generator.AppendSpotText($"{name}_stext", 0, painter.drawingTexts);

                            // lines
                            generator.ClearTempLine($"{name}_lines");
                            generator.AppendToLineBunch($"{name}_lines", 0, painter.drawingLines);
                        }
                        stat.commitedDots = painter.cachedDots.Count;
                        stat.commitedText = painter.cachedTexts.Count;
                        stat.commitedLines = painter.cachedLines.Count;
                    }
                    else
                    {
                        // we should draw newest frame, even though it's not finished.
                        if (stat.commitingFrame != painter.frameCnt)
                        {
                            stat.commitingFrame = painter.frameCnt;
                            generator.ClearVolatilePoints($"{name}_pc");
                            generator.ClearSpotText($"{name}_stext");
                            generator.ClearTempLine($"{name}_lines");
                            stat.commitedDots = stat.commitedText = stat.commitedLines = 0;
                        }

                        if (stat.commitedDots < painter.drawingDots.Count)
                        {
                            generator.AppendVolatilePoints($"{name}_pc", stat.commitedDots, painter.drawingDots);
                            stat.commitedDots = painter.drawingDots.Count;
                        }

                        if (stat.commitedText < painter.drawingTexts.Count)
                        {
                            generator.AppendSpotText($"{name}_stext", stat.commitedText, painter.drawingTexts);
                            stat.commitedText = painter.drawingTexts.Count;
                        }

                        if (stat.commitedLines < painter.drawingLines.Count)
                        {
                            generator.AppendToLineBunch($"{name}_lines", stat.commitedLines, painter.drawingLines);
                            stat.commitedLines = painter.drawingLines.Count;
                        }
                    }

                }
            }

            WorkspaceAPI[] nr;
            
            lock (terminal)
            {
                nr = terminal.PendingCmds.ToArray();
                terminal.PendingCmds.Clear();
            }

            foreach (var workspaceApi in nr)
                workspaceApi.Serialize(generator.cb);

            var ret = generator.End();
            // if (ret.Length>0)
            //     Console.WriteLine($"workspace={ret.Length}bytes, {nr.Length} apis");
            return ret;
        }

        public static void InitTerminal(Terminal terminal)
        {
            lock (preliminarySync)
            lock (terminal)
            {
                terminal.PendingCmds.AddRange(WorkspaceProp.Initializers);
                terminal.PendingCmds.AddRange(WorkspaceProp.Revokables.Values);
            }

            Console.WriteLine($"Initialize terminal {terminal.ID} with {terminal.PendingCmds.Length} cmds");
        }

        public abstract class WorkspaceAPI
        {
            internal abstract void Submit();

            protected internal abstract void Serialize(CB cb);
        }


        public static CustomBackgroundShader currentShader = null;
        /// <summary>
        /// Sets a custom background shader similar to ShaderToy
        /// </summary>
        /// <param name="shaderCode">GLSL shader code (fragment shader)</param>
        /// <returns>The created shader object that can be used to remove the shader later</returns>
        public static CustomBackgroundShader SetCustomBackgroundShader(string shaderCode)
        {
            var shader = currentShader= new CustomBackgroundShader
            {
                shaderCode = shaderCode
            };

            shader.Submit();
            return shader;
        }

        /// <summary>
        /// Disables any active custom background shader
        /// </summary>
        public static void DisableCustomBackgroundShader()
        {
            currentShader?.Remove();
        }
    }
}