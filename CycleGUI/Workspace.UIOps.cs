using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

namespace CycleGUI;

public partial class Workspace
{
    public abstract class WorkspaceUIOperation<T> : WorkspaceAPI
    {
        private static int OpIdN = 1;
        internal int OpID = OpIdN++;

        public virtual string Name { get; set; }

        public Action<T> action;
        public Terminal terminal = GUI.localTerminal;

        public override void Submit()
        {
            terminal.registeredWorkspaceFeedbacks[OpIdN] = (br) =>
            {
                terminal.registeredWorkspaceFeedbacks.Remove(OpIdN);
                action(Deserialize(br));
            };
        }
        
        protected abstract T Deserialize(BinaryReader binaryReader);
    }

    public class SelectObject : WorkspaceUIOperation<(string name, BitArray selector, int firstSub)[]>
    {
        public override string Name { get; set; } = "Select Object";

        protected internal override void Serialize(CB cb)
        {
            cb.Append(8);
            cb.Append(OpID);
            cb.Append(Name);
        }

        protected override (string name, BitArray selector, int firstSub)[] Deserialize(BinaryReader binaryReader)
        {
            List<(string name, BitArray selector, int firstSub)> list = new();
            while (true)
            {
                var type = binaryReader.ReadInt32();
                if (type == -1) break;

                if (type == 0)
                {
                    // selected as whole
                    list.Add((binaryReader.ReadString(), null,-1));
                }
                else if (type == 1)
                {
                    // sub selected
                    var name = binaryReader.ReadString();
                    var bitlen=binaryReader.ReadInt32();
                    BitArray bitArr = new(binaryReader.ReadBytes(bitlen));
                    list.Add((name, bitArr, -1));
                }
                else if (type == 2)
                {
                    var name = binaryReader.ReadString();
                    var sub = binaryReader.ReadInt32();
                    list.Add((name, null, sub));
                }
            }

            return list.ToArray();
        }
    }

    public class AfterMoveXYZ : WorkspaceUIOperation<Vector3[]>
    {
        public bool realtimeResult = false;
        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }

        protected override Vector3[] Deserialize(BinaryReader binaryReader)
        {
            throw new NotImplementedException();
        }
    }

    public class AfterRotateXYZ : WorkspaceUIOperation<Quaternion[]>
    {
        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }

        protected override Quaternion[] Deserialize(BinaryReader binaryReader)
        {
            throw new NotImplementedException();
        }
    }
}