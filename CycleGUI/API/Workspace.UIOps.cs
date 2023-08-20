using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace CycleGUI.API
{
    public abstract class WorkspaceUIState : Workspace.WorkspaceAPI
    {
        public Terminal terminal = GUI.localTerminal;
        internal override void Submit()
        {
            // empty submit.
        }
    }

    public abstract class WorkspaceUIOperation<T> : Workspace.WorkspaceAPI
    {
        private static int OpIdN = 0;
        internal int OpID = OpIdN++;

        public virtual string Name { get; set; }

        public Action<T, WorkspaceUIOperation<T>> action;
        public Action except;
        public Terminal terminal = GUI.localTerminal;

        class EndOperation : WorkspaceUIState
        {
            protected internal override void Serialize(CB cb)
            {
                throw new NotImplementedException();
            }
        }

        public void Start()
        {
            Submit();
        }

        private bool ended = false;
        public void End()
        {
            if (ended) return;
            lock (terminal)
            {
                var poped = terminal.opStack.Pop();
                terminal.pendingUIStates.Remove(poped);
                terminal.registeredWorkspaceFeedbacks.Remove(OpID);
                ChangeState(new EndOperation()); // first pop ui_state.
                terminal.InvokeUIStates(); // then apply ui state changes.
            }
        }

        internal override void Submit()
        {
            lock (terminal)
            {
                terminal.opStack.Push(OpID);
                terminal.registeredWorkspaceFeedbacks[OpID] = (br) =>
                {
                    if (br.ReadBoolean())
                        action(Deserialize(br), this);
                    else
                        except?.Invoke();
                };
                terminal.Temporary.Add(this);
            }
        }
        
        protected abstract T Deserialize(BinaryReader binaryReader);

        public void ChangeState(WorkspaceUIState state)
        {
            // 
            lock (terminal)
            {
                state.terminal = terminal;
                if (terminal.opStack.Peek() == OpID)
                    terminal.Temporary.Add(state);
                else
                    terminal.AddUIState(OpID, state);
            }
        }
    }

    public class SetObjectSelectableOrNot : WorkspaceUIState
    {
        public string name;
        public bool selectable = true;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(6);
            cb.Append(name);
            cb.Append(selectable);
        }
    }

    public class SetSelection : WorkspaceUIState
    {
        public string[] selection;

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

    public class SetWorkspaceDisplay : Workspace.WorkspaceAPI
    {
        public bool DrawPointCloud = true, DrawModels = true, DrawPainters = true, SSAO = true, Reflections = true;

        internal override void Submit()
        {
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }
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

    public class RotateXYZ : WorkspaceUIOperation<Quaternion[]>
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