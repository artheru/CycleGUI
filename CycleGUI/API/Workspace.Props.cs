using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CycleGUI
{
    public partial class Workspace
    {
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

    }
}

namespace CycleGUI.API
{
    public abstract class WorkspacePropAPI : Workspace.WorkspaceAPI
    {
        static internal List<Workspace.WorkspaceAPI> Initializers = new();
        static internal Dictionary<string, Workspace.WorkspaceAPI> Revokables = new();

        public void SubmitReversible(string name)
        {
            lock (Workspace.preliminarySync)
            {
                Revokables[name] = this;
                foreach (var terminal in Terminal.terminals)
                {
                    lock (terminal)
                    {
                        Revokables[name] = this;
                        terminal.PendingCmds.Add(this, name);
                    }
                }

            }
        }

        public void SubmitLoadings()
        {
            lock (Workspace.preliminarySync)
            {
                Initializers.Add(this);
                foreach (var terminal in Terminal.terminals)
                {
                    lock (terminal)
                    {
                        Initializers.Add(this);
                        terminal.PendingCmds.Add(this);
                    }
                }
            }
        }
    }

    public class LoadModel : WorkspacePropAPI
    {
        public string name;
        public Workspace.ModelDetail detail;

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

        internal override void Submit()
        {
            SubmitLoadings();
        }
    }


    public class TransformObject: WorkspacePropAPI
    {
        public string name;
        public Vector3 pos;
        public Quaternion quat;
        public int timeMs;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(5);
            cb.Append(name);
            cb.Append(pos.X);
            cb.Append(pos.Y);
            cb.Append(pos.Z);
            cb.Append(quat.X);
            cb.Append(quat.Y);
            cb.Append(quat.Z);
            cb.Append(quat.W);
            cb.Append(timeMs);
        }

        internal override void Submit()
        {
            SubmitReversible($"transform#{name}");
        }
    }

    public class PutPointCloud : WorkspacePropAPI
    {
        public string name;
        public Vector3 newPosition;
        public Quaternion newQuaternion = Quaternion.Identity;
        public Vector4[] xyzSzs;
        public uint[] colors;
        // todo:

        internal override void Submit()
        {
            SubmitReversible($"pointcloud#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(0);
            cb.Append(name);
            cb.Append(false);
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

    public class PointCloudUseHandleOrNot : WorkspacePropAPI
    {
        public bool useHandle;
        internal override void Submit()
        {
            throw new NotImplementedException();
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }
    }

    public class PutModelObject : WorkspacePropAPI
    {
        public string clsName, name;
        public Vector3 newPosition;
        public Quaternion newQuaternion = Quaternion.Identity;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(4); // the index
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

        internal override void Submit()
        {
            SubmitReversible($"{clsName}#{name}");
        }
    }
    
}
