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

        public List<Workspace.WorkspaceAPI> Initializers= new();
        public Dictionary<string, Workspace.WorkspaceAPI> Revokables=new();
        public List<Workspace.WorkspaceAPI> Temporary = new();

    }

    public partial class Workspace
    {

        internal static void ReceiveTerminalFeedback(byte[] feedBack, Terminal t)
        {
            // parse:
            using var ms = new MemoryStream(feedBack);
            using var br = new BinaryReader(ms);

            var id = br.ReadInt32();
            if (t.registeredWorkspaceFeedbacks.TryGetValue(id, out var handler))
                handler(br);
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
                terminal.Initializers=new List<WorkspaceAPI>();
                terminal.Temporary = new();
                terminal.Revokables = new Dictionary<string, WorkspaceAPI>();
            }

            foreach (var workspaceApi in nr)
                workspaceApi.Serialize(generator.cb);

            foreach (var workspaceApi in nz)
                workspaceApi.Serialize(generator.cb);
            

            return generator.End();
        }

        public static object preliminarySync = new();

        public static byte[] GetPreliminaries(Terminal terminal)
        {
            // Preliminary APIs, used for remote terminals.
            lock (preliminarySync)
            lock (terminal)
            {
                //...
            }

            return null;
        }


        // void LoadModel(std::string cls_name, unsigned char* bytes, int length, ModelDetail detail);
        // void PutModelObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion);
        // void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time);
        public struct ModelDetail
        {
            public Vector3 Center;
            public Quaternion Rotate = Quaternion.Identity;
            public float Scale = 1;
            public byte[] GLTF;

            // Constructor with default values
            public ModelDetail(byte[] gltfBytes)
            {
                GLTF = gltfBytes;
            }
        }

        private static Dictionary<string, ModelDetail> models=new();

        public abstract class WorkspaceAPI
        {
            public void SubmitReversible(string name)
            {
                lock (Terminal.terminals)
                {
                    foreach (var terminal in Terminal.terminals)
                    {
                        lock (terminal)
                        {
                            terminal.Revokables[name] = this;
                        }
                    }

                }
            }

            public void SubmitLoadings(Action action)
            {
                lock (preliminarySync)
                {
                    action();
                    foreach (var terminal in Terminal.terminals)
                    {
                        lock (terminal)
                        {
                            terminal.Initializers.Add(this);
                        }
                    }
                }
            }
            public abstract void Submit();
            protected internal abstract void Serialize(CB cb);
        }

        public class LoadModel : WorkspaceAPI
        {
            public string name;
            public ModelDetail detail;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(3);
                cb.Append(name);
                cb.Append(detail.GLTF.Length);
                cb.Append(detail.GLTF);
                cb.Append(detail.Center.X);
                cb.Append(detail.Center.Y);
                cb.Append(detail.Center.Z);
                cb.Append(detail.Rotate.X);
                cb.Append(detail.Rotate.Y);
                cb.Append(detail.Rotate.Z);
                cb.Append(detail.Rotate.W);
                cb.Append(detail.Scale);
            }

            public override void Submit()
            {
                SubmitLoadings(()=> {
                    models[name] = detail;
                });
            }
        }

        public class SetWorkspaceDrawings : WorkspaceAPI
        {
            public bool DrawPointCloud = true, DrawModels = true, DrawPainters = true, SSAO = true, Reflections = true;
            public override void Submit()
            {
            }

            protected internal override void Serialize(CB cb)
            {
                throw new NotImplementedException();
            }
        }

        public class PutPointCloud : WorkspaceAPI
        {
            public string name;
            public Vector3 newPosition;
            public Quaternion newQuaternion = Quaternion.Identity;
            public Vector4[] xyzSzs;
            public uint[] colors;

            public override void Submit()
            {
                SubmitReversible($"pointcloud#{name}");
            }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(0);
                cb.Append(name);
                cb.Append(true);
                cb.Append(xyzSzs.Length);
                cb.Append(xyzSzs.Length); //initN, note xyzSz/color is ignored.

                for (int i = 0; i < xyzSzs.Length; i++)
                {
                    cb.Append(xyzSzs[i].X);
                    cb.Append(xyzSzs[i].Y);
                    cb.Append(xyzSzs[i].Z);
                    cb.Append(xyzSzs[i].W);
                }
                for (int i = 0; i < xyzSzs.Length; i++)
                {
                    cb.Append(colors[i]);
                }

                cb.Append(newPosition.X); //position
                cb.Append(newPosition.Y);
                cb.Append(newPosition.Z);
                cb.Append(newQuaternion.X);
                cb.Append(newQuaternion.Y);
                cb.Append(newQuaternion.Z);
                cb.Append(newQuaternion.W);
            }
        }

        public class SetObjectSelectable:WorkspaceAPI
        {
            public string name;

            public override void Submit()
            {
                // just apply to specific terminal, todo: pending architecturing.
                
            }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(6);
                cb.Append(name);
            }
        }
        

        public class PutModelObject : WorkspaceAPI
        {
            public string clsName, name;
            public Vector3 newPosition;
            public Quaternion newQuaternion = Quaternion.Identity;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(4);  // the index
                cb.Append(clsName);
                cb.Append(name);
                cb.Append(newPosition.X);
                cb.Append(newPosition.Y);
                cb.Append(newPosition.Z);
                cb.Append(newQuaternion.X);
                cb.Append(newQuaternion.Y);
                cb.Append(newQuaternion.Z);
                cb.Append(newQuaternion.W);
            }

            public override void Submit()
            {
                lock (Terminal.terminals)
                {
                    foreach (var terminal in Terminal.terminals)
                    {
                        lock (terminal)
                        {
                            terminal.Revokables[clsName + "#" + name] = this;
                        }
                    }
                }
            }
        }

        public void MoveObject(string name, Vector3 newPosition, Quaternion newQuaternion, float time)
        {
            // Implementation of the function...
        }
        
        public class SetSelection:WorkspaceAPI
        {
            public string[] selection;
            public Terminal terminal= GUI.localTerminal;

            public override void Submit()
            {
                lock (terminal)
                    terminal.Temporary.Add(this);
                // ignore: ui state api.
            }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(7);
                cb.Append(selection.Length);
                foreach (var s in selection)
                {
                    cb.Append(s);
                }
            }
        }
    }
}
