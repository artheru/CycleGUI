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
        internal override void Submit()
        {
            // empty submit.
        }
    }

    public abstract class CommonWorkspaceState : WorkspaceUIState
    {
        public Terminal terminal = GUI.defaultTerminal;
        internal override void Submit()
        {
            terminal.PendingCmds.Add(this);
        }

        public void Issue() => Submit();
    }

    public abstract class CWorkspaceUIOperation : Workspace.WorkspaceAPI
    {
        private static int OpIdN = 0;
        protected int OpID = OpIdN++;
    }
    public abstract class WorkspaceUIOperation<T> : CWorkspaceUIOperation
    {

        public Action<T, WorkspaceUIOperation<T>> feedback;
        public virtual string Name { get; set; }

        public Action terminated, finished;
        public Terminal terminal = GUI.defaultTerminal;

        class EndOperation : WorkspaceUIState
        { 
            protected internal override void Serialize(CB cb)
            {
                cb.Append(9);
            }
        }

        public void Start()
        {
            Submit();
        }


        private bool ended = false;
        public void End()
        {
            ChangeState(new EndOperation()); // first pop ui_state.
            CleanUp();
        }

        void CleanUp()
        {
            if (ended) return;
            ended = true;
            lock (terminal)
            {
                var poped = terminal.opStack.Pop();
                terminal.pendingUIStates.Remove(poped);
                terminal.registeredWorkspaceFeedbacks.Remove(OpID);
                terminal.ApplyQueuedUIStateChanges(); // then apply ui state changes.
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
                    { 
                        var wfin = br.ReadBoolean();
                        var pck = Deserialize(br);
                        if (feedback != null)
                            feedback(pck, this);
                        if (wfin)
                        {
                            CleanUp();
                            finished?.Invoke();
                        }
                    }
                    else
                    {
                        CleanUp();
                        if (terminated != null)
                            terminated();
                    }
                };
                terminal.PendingCmds.Add(this);
                terminal.ApplyQueuedUIStateChanges();
            }
        }

        protected string ReadString(BinaryReader br)
        {
            var len = br.ReadInt32();
            byte[] bytes = br.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }
        protected abstract T Deserialize(BinaryReader binaryReader);


        internal void ChangeState(WorkspaceUIState state)
        {
            // 
            lock (terminal)
            {
                if (terminal.opStack.Count>0 && terminal.opStack.Peek() == OpID)
                    terminal.PendingCmds.Add(state); //after taking effect, the wsop state is stored.
                else
                    terminal.QueueUIStateChange(OpID, state);
            }
        }

    }

    public class SetPropShowHide : CommonWorkspaceState
    {
        public string namePattern;
        public bool show;
        protected internal override void Serialize(CB cb)
        {
            cb.Append(32);
            cb.Append(namePattern);
            cb.Append(show);
        }
    }

    public class SetCameraPosition : CommonWorkspaceState
    {
        public Vector3 lookAt;

        /// <summary>
        /// in Rad, meter.
        /// </summary>
        public float Azimuth, Altitude, distance;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(14);
            cb.Append(lookAt.X);
            cb.Append(lookAt.Y);
            cb.Append(lookAt.Z);
            cb.Append(Azimuth);
            cb.Append(Altitude);
            cb.Append(distance);
        }
    }

    public class SetCameraType : CommonWorkspaceState
    {
        public float fov;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(15);
            cb.Append(fov);
        }
    }

    public class SetAppearance : CommonWorkspaceState
    {
        // color scheme is RGBA
        public bool useEDL = true, useSSAO = true, useGround = true, useBorder = true, useBloom = true, drawGrid = true, drawGuizmo=true;
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
            cb.Append(drawGuizmo);
            cb.Append(hover_shine);
            cb.Append(selected_shine);
            cb.Append(hover_border_color);
            cb.Append(selected_border_color);
            cb.Append(world_border_color);
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

    public class SelectObject : WorkspaceUIOperation<(string name, BitArray selector, string firstSub)[]>
    {
        public override string Name { get; set; } = "Select Object";

        protected internal override void Serialize(CB cb)
        {
            cb.Append(8);
            cb.Append(OpID);
            cb.Append(Name);
        }

        class SetSelectionCmd : WorkspaceUIState
            {
                public string[] selection=new string[0];

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

        public void SetSelection(string[] selection)
        {
            ChangeState(new SetSelectionCmd() { selection = selection });
        }


        class SetObjectSelectableOrNot : WorkspaceUIState
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

        class SetObjectSubSelectableOrNot : WorkspaceUIState
        {
            public string name;
            public bool subselectable = true;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(16);
                cb.Append(name);
                cb.Append(subselectable);
            }
        }

        public void SetObjectSelectable(string name) =>
            ChangeState(new SetObjectSelectableOrNot() { name = name });

        public void SetObjectUnselectable(string name) =>
            ChangeState(new SetObjectSelectableOrNot() { name = name, selectable = false});

        public void SetObjectSubSelectable(string name) =>
            ChangeState(new SetObjectSubSelectableOrNot() { name = name });

        public void SetObjectSubUnselectable(string name) =>
            ChangeState(new SetObjectSubSelectableOrNot() { name = name, subselectable = false });

        protected override (string name, BitArray selector, string firstSub)[] Deserialize(BinaryReader binaryReader)
        {
            List<(string name, BitArray selector, string firstSub)> list = [];
            while (true)
            {
                var type = binaryReader.ReadInt32();
                if (type == -1) break;

                if (type == 0)
                {
                    // selected as whole
                    list.Add((ReadString(binaryReader), null, null));
                }
                else if (type == 1)
                {
                    // point cloud sub selected
                    var name = ReadString(binaryReader);
                    var bitlen=binaryReader.ReadInt32();
                    BitArray bitArr = new(binaryReader.ReadBytes(bitlen));
                    list.Add((name, bitArr, null));
                }
                else if (type == 2)
                {
                    // object sub selected
                    var name = ReadString(binaryReader);
                    var sub = ReadString(binaryReader);
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
            cb.Append(10);
            cb.Append(OpID);
            cb.Append(Name);
            cb.Append(realtimeResult);
            cb.Append((int)type);
        }

        protected override (string name, Vector3 pos, Quaternion rot)[] Deserialize(BinaryReader binaryReader)
        {
            var len = binaryReader.ReadInt32();
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

            return ret;
        }

    }
}