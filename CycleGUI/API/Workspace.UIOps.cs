using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

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

    public abstract class CWorkspaceUIOperation : Workspace.WorkspaceAPI
    {
        private static int OpIdN = 0;
        protected int OpID = OpIdN++;
    }
    public abstract class WorkspaceUIOperation<T> : CWorkspaceUIOperation
    {
        public virtual string Name { get; set; }

        public Action<T, WorkspaceUIOperation<T>> feedback;
        public Action terminated, finished;
        public Terminal terminal = GUI.localTerminal;

        class EndOperation : WorkspaceUIState
        {
            protected internal override void Serialize(CB cb)
            {
                throw new NotImplementedException();
            }
        }
        protected void BasicSerialize(int id, CB cb)
        {

            cb.Append(10);
            cb.Append(OpID);
            cb.Append(Name);
        }

        public void Start()
        {
            Submit();
        }


        private bool ended = false;
        public void End()
        {
            if (ended) return;
            ended = true;
            lock (terminal)
            {
                var poped = terminal.opStack.Pop();
                terminal.pendingUIStates.Remove(poped);
                terminal.registeredWorkspaceFeedbacks.Remove(OpID);
                ChangeState(new EndOperation()); // first pop ui_state.
                terminal.InvokeUIStates(); // then apply ui state changes.
            }
            terminated?.Invoke(); // external terminate.
        }

        public void NotifyEnded()
        {
            if (ended) return;
            ended = true;
            lock (terminal)
            {
                var poped = terminal.opStack.Pop();
                terminal.pendingUIStates.Remove(poped);
                terminal.registeredWorkspaceFeedbacks.Remove(OpID);
                terminal.InvokeUIStates(); // then apply ui state changes.
            }
            finished?.Invoke();
        }

        internal override void Submit()
        {
            lock (terminal)
            {
                terminal.opStack.Push(OpID);
                terminal.registeredWorkspaceFeedbacks[OpID] = (br) =>
                {
                    if (br.ReadBoolean())
                        feedback(Deserialize(br), this);
                    else
                    {
                        if (ended) return;
                        ended = true;
                        lock (terminal)
                        {
                            var poped = terminal.opStack.Pop();
                            terminal.pendingUIStates.Remove(poped);
                            terminal.registeredWorkspaceFeedbacks.Remove(OpID);
                            terminal.InvokeUIStates(); // then apply ui state changes.
                        }
                        terminated?.Invoke();
                    }
                };
                terminal.Temporary.Add(this);
            }
        }

        protected string ReadString(BinaryReader br)
        {
            var len = br.ReadInt32();
            byte[] bytes = br.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
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

    public class SetAppearance : WorkspaceUIState
    {
        // color scheme is RGBA
        public bool useEDL = true, useSSAO = true, useGround = true, useBorder = true, useBloom = true, drawGrid = true;
        public uint hover_shine = 0x99990099, selected_shine = 0xff0000ff, hover_border_color = 0xffff00ff, selected_border_color =0xff0000ff, world_border_color = 0xffffffff;
        
        protected internal override void Serialize(CB cb)
        {
            cb.Append(11);
            cb.Append(useEDL);
            cb.Append(useSSAO);
            cb.Append(useGround);
            cb.Append(useBorder);
            cb.Append(useBloom);
            cb.Append(drawGrid);
            cb.Append(hover_shine);
            cb.Append(selected_shine);
            cb.Append(hover_border_color);
            cb.Append(selected_border_color);
            cb.Append(world_border_color);
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
                    list.Add((ReadString(binaryReader), null,-1));
                }
                else if (type == 1)
                {
                    // sub selected
                    var name = ReadString(binaryReader);
                    var bitlen=binaryReader.ReadInt32();
                    BitArray bitArr = new(binaryReader.ReadBytes(bitlen));
                    list.Add((name, bitArr, -1));
                }
                else if (type == 2)
                {
                    var name = ReadString(binaryReader);
                    var sub = binaryReader.ReadInt32();
                    list.Add((name, null, sub));
                }
            }

            return list.ToArray();
        }
    }
    
    public class GuizmoAction : WorkspaceUIOperation<(String name, Vector3 pos, Quaternion rot)[]>
    {
        public override string Name { get; set; } = "Guizmo";

        public enum GuizmoType
        {
            MoveXYZ, RotateXYZ
        }

        public GuizmoType type = GuizmoType.MoveXYZ;

        public bool realtimeResult = false;
        protected internal override void Serialize(CB cb)
        {
            BasicSerialize(10,cb);

            cb.Append(realtimeResult);
            cb.Append((int)type);
        }

        protected override (string name, Vector3 pos, Quaternion rot)[] Deserialize(BinaryReader binaryReader)
        {
            var len = binaryReader.ReadInt32();
            var finished = binaryReader.ReadBoolean();
            var ret = new (string name, Vector3 pos, Quaternion rot)[len];
            for (int i = 0; i < len; i++)
            {
                var name = ReadString(binaryReader);
                var x = binaryReader.ReadSingle();
                var y = binaryReader.ReadSingle();
                var z = binaryReader.ReadSingle();
                var pos = new Vector3(x, y, z);
                var a = binaryReader.ReadSingle();
                var b = binaryReader.ReadSingle();
                var c = binaryReader.ReadSingle();
                var d = binaryReader.ReadSingle();
                var quat = new Quaternion(a, b, c, d);
                ret[i] = (name, pos, quat);
            }

            if (finished)
                NotifyEnded();

            return ret;
        }

    }
}