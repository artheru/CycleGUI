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

namespace CycleGUI
{
    public abstract partial class Terminal
    {
        // fields used by workspace:
        public static ConcurrentBag<Terminal> terminals = new();

        public Terminal()
        {
            lock (terminals)
                terminals.Add(this);
        }

        public List<Workspace.WorkspaceAPI> Initializers = new();
        public Dictionary<string, Workspace.WorkspaceAPI> Revokables = new();
        public List<Workspace.WorkspaceAPI> Temporary = new();

        public Dictionary<int, Action<BinaryReader>> registeredWorkspaceFeedbacks = new();
        internal Dictionary<int, List<Workspace.WorkspaceAPI>> pendingUIStates = new();

        internal void AddUIState(int id, Workspace.WorkspaceAPI what)
        {
            if (pendingUIStates.TryGetValue(id, out var ls))
                ls.Add(what);
            else pendingUIStates[id] = new List<Workspace.WorkspaceAPI>() { what };
        }

        internal void InvokeUIStates()
        {
            var id = opStack.Peek();
            foreach (var api in pendingUIStates[id])
                Temporary.Add(api);
        }

        public Stack<int> opStack = new();
    }

    public partial class Workspace
    {
        public static void Prop(WorkspacePropAPI api)
        {
            api.Submit();
        }

        internal static void ReceiveTerminalFeedback(byte[] feedBack, Terminal t)
        {
            // just routing.
            using var ms = new MemoryStream(feedBack);
            using var br = new BinaryReader(ms);

            var id = br.ReadInt32();
            lock (t)
                if (t.registeredWorkspaceFeedbacks.TryGetValue(id, out var handler))
                    handler(br);
        }

        internal class WorkspaceQueueGenerator
        {
            public CB cb = new CB();

            public byte[] End()
            {
                cb.Append(-1);
                return cb.ToArray();
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

            public void ClearVolatilePoints(string name)
            {
                cb.Append(2);
                cb.Append(name);
            }


        }

        public static byte[] GetChanging(Terminal terminal)
        {
            // API calling.

            // for painters, which is highly volatile, we don't distinguish terminal.
            WorkspaceQueueGenerator generator = new();
            foreach (var name in Painter._allPainters.Keys.ToArray())
            {
                if (!Painter._allPainters.TryGetValue(name, out var painter)) continue;
                if (!painter.added)
                {
                    painter.added = true;
                    generator.AddVolatilePointCloud($"{name}_pc", 10000000);
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

            List<WorkspaceAPI> nr;
            Dictionary<string, WorkspaceAPI>.ValueCollection nz;

            lock (terminal)
            {
                nr = terminal.Initializers.Concat(terminal.Temporary).ToList();
                nz = terminal.Revokables.Values;
                terminal.Initializers = new List<WorkspaceAPI>();
                terminal.Temporary = new();
                terminal.Revokables = new Dictionary<string, WorkspaceAPI>();
            }

            foreach (var workspaceApi in nr)
                workspaceApi.Serialize(generator.cb);

            foreach (var workspaceApi in nz)
                workspaceApi.Serialize(generator.cb);


            return generator.End();
        }

        public abstract class WorkspaceAPI
        {
            internal abstract void Submit();

            protected internal abstract void Serialize(CB cb);
        }
    }
}