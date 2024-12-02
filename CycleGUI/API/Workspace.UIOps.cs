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


        public void ChangeState(WorkspaceUIState state)
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

    public class SetPropApplyCrossSection : CommonWorkspaceState
    {
        public string namePattern;
        public bool apply;
        protected internal override void Serialize(CB cb)
        {
            cb.Append(15);
            cb.Append(namePattern);
            cb.Append(apply);
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

    public class SetCamera : CommonWorkspaceState
    {
        private bool lookAt_set, azimuth_set, altitude_set, distance_set, fov_set;
        private Vector3 _lookAt;
        private float _azimuth, _altitude, _distance, _fov;

        public Vector3 lookAt { get => _lookAt; set { _lookAt = value; lookAt_set = true; } }
        public float azimuth { get => _azimuth; set { _azimuth = value; azimuth_set = true; } }
        public float altitude { get => _altitude; set { _altitude = value; altitude_set = true; } }
        public float distance { get => _distance; set { _distance = value; distance_set = true; } }
        public float fov { get => _fov; set { _fov = value; fov_set = true; } }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(14);
            cb.Append(lookAt_set);
            if (lookAt_set)
            {
                cb.Append(_lookAt.X);
                cb.Append(_lookAt.Y);
                cb.Append(_lookAt.Z);
            }
            cb.Append(azimuth_set);
            if (azimuth_set) cb.Append(_azimuth);
            cb.Append(altitude_set);
            if (altitude_set) cb.Append(_altitude);
            cb.Append(distance_set);
            if (distance_set) cb.Append(_distance);
            cb.Append(fov_set);
            if (fov_set) cb.Append(_fov);
        }
    }

    public class SetAppearance : CommonWorkspaceState
    {
        // color scheme is RGBA
        private bool useEDL_set, useSSAO_set, useGround_set, useBorder_set, useBloom_set, drawGrid_set, drawGuizmo_set;
        private bool hover_shine_set, selected_shine_set, hover_border_color_set, selected_border_color_set, world_border_color_set;
        private bool useCrossSection_set, crossSectionPlanePos_set, clippingDirection_set;

        private bool _useEDL = true, _useSSAO = true, _useGround = true, _useBorder = true, _useBloom = true, _drawGrid = true, _drawGuizmo = true;
        private uint _hover_shine = 0x99990099, _selected_shine = 0xff0000ff, _hover_border_color = 0xffff00ff, _selected_border_color = 0xff0000ff, _world_border_color = 0xffffffff;
        private bool _useCrossSection = false;
        private Vector3 _crossSectionPlanePos, _clippingDirection;

        public bool useEDL { get => _useEDL; set { _useEDL = value; useEDL_set = true; } }
        public bool useSSAO { get => _useSSAO; set { _useSSAO = value; useSSAO_set = true; } }
        public bool useGround { get => _useGround; set { _useGround = value; useGround_set = true; } }
        public bool useBorder { get => _useBorder; set { _useBorder = value; useBorder_set = true; } }
        public bool useBloom { get => _useBloom; set { _useBloom = value; useBloom_set = true; } }
        public bool drawGrid { get => _drawGrid; set { _drawGrid = value; drawGrid_set = true; } }
        public bool drawGuizmo { get => _drawGuizmo; set { _drawGuizmo = value; drawGuizmo_set = true; } }
        
        public uint hover_shine { get => _hover_shine; set { _hover_shine = value; hover_shine_set = true; } }
        public uint selected_shine { get => _selected_shine; set { _selected_shine = value; selected_shine_set = true; } }
        public uint hover_border_color { get => _hover_border_color; set { _hover_border_color = value; hover_border_color_set = true; } }
        public uint selected_border_color { get => _selected_border_color; set { _selected_border_color = value; selected_border_color_set = true; } }
        public uint world_border_color { get => _world_border_color; set { _world_border_color = value; world_border_color_set = true; } }
        
        public bool useCrossSection { get => _useCrossSection; set { _useCrossSection = value; useCrossSection_set = true; } }
        public Vector3 crossSectionPlanePos { get => _crossSectionPlanePos; set { _crossSectionPlanePos = value; crossSectionPlanePos_set = true; } }
        public Vector3 clippingDirection { get => _clippingDirection; set { _clippingDirection = value; clippingDirection_set = true; } }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(11);
            
            // Only append values that have been explicitly set
            cb.Append(useEDL_set);
            if (useEDL_set) cb.Append(_useEDL);
            
            cb.Append(useSSAO_set);
            if (useSSAO_set) cb.Append(_useSSAO);
            
            cb.Append(useGround_set);
            if (useGround_set) cb.Append(_useGround);
            
            cb.Append(useBorder_set);
            if (useBorder_set) cb.Append(_useBorder);
            
            cb.Append(useBloom_set);
            if (useBloom_set) cb.Append(_useBloom);
            
            cb.Append(drawGrid_set);
            if (drawGrid_set) cb.Append(_drawGrid);
            
            cb.Append(drawGuizmo_set);
            if (drawGuizmo_set) cb.Append(_drawGuizmo);
            
            cb.Append(hover_shine_set);
            if (hover_shine_set) cb.Append(_hover_shine);
            
            cb.Append(selected_shine_set);
            if (selected_shine_set) cb.Append(_selected_shine);
            
            cb.Append(hover_border_color_set);
            if (hover_border_color_set) cb.Append(_hover_border_color);
            
            cb.Append(selected_border_color_set);
            if (selected_border_color_set) cb.Append(_selected_border_color);
            
            cb.Append(world_border_color_set);
            if (world_border_color_set) cb.Append(_world_border_color);
            
            cb.Append(useCrossSection_set);
            if (useCrossSection_set) cb.Append(_useCrossSection);
            
            cb.Append(crossSectionPlanePos_set);
            if (crossSectionPlanePos_set)
            {
                cb.Append(_crossSectionPlanePos.X);
                cb.Append(_crossSectionPlanePos.Y);
                cb.Append(_crossSectionPlanePos.Z);
            }
            
            cb.Append(clippingDirection_set);
            if (clippingDirection_set)
            {
                cb.Append(_clippingDirection.X);
                cb.Append(_clippingDirection.Y);
                cb.Append(_clippingDirection.Z);
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