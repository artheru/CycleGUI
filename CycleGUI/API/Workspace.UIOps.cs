using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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
        protected Terminal terminal = GUI.defaultTerminal;
        internal override void Submit()
        {
            // todo: maybe throw exception?
            lock (terminal)
                terminal.PendingCmds.Add(this);
        }

        [Obsolete]
        public void Issue() => Submit();


        public void IssueToDefault() => Submit();

        public void IssueToTerminal(Terminal terminal)
        {
            this.terminal = terminal;
            lock (terminal)
                terminal.PendingCmds.Add(this);
        }

        public void IssueToAllTerminals()
        {
            terminal = null;
            foreach (var terminal in Terminal.terminals.Keys)
                lock (terminal)
                    terminal.PendingCmds.Add(this);
        }
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
            public string name;
            public int id;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(9);
                cb.Append(name);
                cb.Append(id);
            }
        }

        public void Start()
        {
            Submit();
        }

        public void StartOnTerminal(Terminal terminal)
        {
            this.terminal = terminal;
            Submit();
        }


        private bool ended = false;
        public void End()
        {
            ChangeState(new EndOperation() { name = Name, id = OpID }); // first pop ui_state.
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

        internal bool started = false;
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
                        try
                        {
                            if (feedback != null)
                                feedback(pck, this);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Feedback workspace state error, ex={ex.MyFormat()}");
                        }

                        if (wfin)
                        {
                            finished?.Invoke();
                            CleanUp();
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

            started = true;
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

    public class SetObjectApperance : CommonWorkspaceState
    {
        private bool bring_to_front_set, shine_color_set, border_color_set, transparency_set;
        private string _namePattern;
        private bool _bring_to_front;
        private uint _shine_color = 0x99990099;    // Default value matching SetAppearance
        private bool _use_border = false;    // Default value matching SetAppearance
        private float _transparency = 0.0f;    // Default value for transparency

        public string namePattern { get => _namePattern; set { _namePattern = value; } }
        public bool bring_to_front { get => _bring_to_front; set { _bring_to_front = value; bring_to_front_set = true; } }
        public uint shine_color { get => _shine_color; set { _shine_color = value; shine_color_set = true; } }
        public bool use_border { get => _use_border; set { _use_border = value; border_color_set = true; } }
        public float transparency { get => _transparency; set { _transparency = value; transparency_set = true; } }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(41);
            cb.Append(_namePattern);

            cb.Append(bring_to_front_set);
            if (bring_to_front_set) cb.Append(_bring_to_front);

            cb.Append(shine_color_set);
            if (shine_color_set) cb.Append(_shine_color);

            cb.Append(border_color_set);
            if (border_color_set) cb.Append(_use_border);
            
            cb.Append(transparency_set);
            if (transparency_set) cb.Append(_transparency);
        }
    }
    //todo: setsubobjectapperance.

    public class SetModelObjectProperty : CommonWorkspaceState
    {
        private string _namePattern;
        private bool baseAnimId_set, nextAnimId_set, material_variant_set, team_color_set;
        private bool base_stopatend_set, next_stopatend_set, animate_asap_set;
        private int _baseAnimId, _nextAnimId, _material_variant;
        private uint _team_color;
        private bool _base_stopatend, _next_stopatend, _animate_asap;
        
        public string namePattern { get => _namePattern; set { _namePattern = value; } }
        public int baseAnimId { get => _baseAnimId; set { _baseAnimId = value; baseAnimId_set = true; } }
        public int nextAnimId { get => _nextAnimId; set { _nextAnimId = value; nextAnimId_set = true; } }
        public int material_variant { get => _material_variant; set { _material_variant = value; material_variant_set = true; } }
        public uint team_color { get => _team_color; set { _team_color = value; team_color_set = true; } }
        public bool base_stopatend { get => _base_stopatend; set { _base_stopatend = value; base_stopatend_set = true; } }
        public bool next_stopatend { get => _next_stopatend; set { _next_stopatend = value; next_stopatend_set = true; } }
        public bool animate_asap { get => _animate_asap; set { _animate_asap = value; animate_asap_set = true; } }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(50);
            cb.Append(_namePattern);
            
            cb.Append(baseAnimId_set);
            if (baseAnimId_set) cb.Append(_baseAnimId);
            
            cb.Append(nextAnimId_set);
            if (nextAnimId_set) cb.Append(_nextAnimId);
            
            cb.Append(material_variant_set);
            if (material_variant_set) cb.Append(_material_variant);
            
            cb.Append(team_color_set);
            if (team_color_set) cb.Append(_team_color);
            
            cb.Append(base_stopatend_set);
            if (base_stopatend_set) cb.Append(_base_stopatend);
            
            cb.Append(next_stopatend_set);
            if (next_stopatend_set) cb.Append(_next_stopatend);
            
            cb.Append(animate_asap_set);
            if (animate_asap_set) cb.Append(_animate_asap);
        }
    }

    public class SetWorkspacePropDisplayMode : CommonWorkspaceState
    {
        public enum PropDisplayMode
        {
            AllButSpecified, // Default to display all props except the ones specified
            NoneButSpecified // Only display the props matching the pattern
        }
        
        private PropDisplayMode _mode = PropDisplayMode.AllButSpecified;
        private string _namePattern = "";
        
        public PropDisplayMode mode { get => _mode; set { _mode = value; } }
        public string namePattern { get => _namePattern; set { _namePattern = value; } }
        
        protected internal override void Serialize(CB cb)
        {
            cb.Append(51); // Command ID for SetWorkspacePropDisplayMode
            cb.Append((int)_mode);
            cb.Append(_namePattern);
        }
    }

    public class SetHoloViewEyePosition : CommonWorkspaceState
    {
        // position from center: X:from screen left to right, Y:from screen top to bottom
        public Vector3 leftEyePos;
        public Vector3 rightEyePos;
        protected internal override void Serialize(CB cb)
        {
            cb.Append(38);
            cb.Append(leftEyePos.X);
            cb.Append(leftEyePos.Y); 
            cb.Append(leftEyePos.Z);
            cb.Append(rightEyePos.X);
            cb.Append(rightEyePos.Y);
            cb.Append(rightEyePos.Z);
        }
    }

    public class SetFullScreen : CommonWorkspaceState
    {
        public bool fullscreen = true;
        protected internal override void Serialize(CB cb)
        {
            cb.Append(35);
            cb.Append(fullscreen);
        }
    }

    public class QueryViewportState : CommonWorkspaceState
    {
        public class ViewportState
        {
            public Vector3 CameraPosition;
            public Vector3 LookAt;
            public Vector3 Up;
        }

        private static ConcurrentDictionary<Terminal, Action<ViewportState>> cbDispatching = new();
        static QueryViewportState()
        {
            Workspace.PropActions[1] = (t, br) =>
            {
                var campos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                var lookat = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                var up = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                if (!cbDispatching.TryRemove(t, out var act) || act == null) return;
                act(new ViewportState() { CameraPosition = campos, LookAt = lookat, Up = up });
            };
        }
        public Action<ViewportState> callback;
        protected internal override void Serialize(CB cb)
        {
            if (callback == null) throw new Exception("Query Viewport State must assign a callback to receive state!");
            cb.Append(39);
            cbDispatching[terminal] = callback;
        }
    }

    public class CaptureRenderedViewport : CommonWorkspaceState
    {
        public class ImageBytes
        {
            public byte[] bytes;
            public int width, height;
        }
        private static ConcurrentDictionary<Terminal, Action<ImageBytes>> cbDispatching = new();
        static CaptureRenderedViewport()
        {
            Workspace.PropActions[2] = (t, br) =>
            {
                var w = br.ReadInt32();
                var h = br.ReadInt32();
                var bytes = br.ReadBytes(w * h * 3);
                if (!cbDispatching.TryRemove(t, out var act) || act == null) return;
                act(new ImageBytes() { bytes = bytes, height = h, width = w });
            };
        }
        public Action<ImageBytes> callback;
        protected internal override void Serialize(CB cb)
        {
            if (callback == null) throw new Exception("Must assign a callback to receive rendered viewport image!");
            cb.Append(40);
            cbDispatching[terminal] = callback;
        }
    }

    public class SetCamera : CommonWorkspaceState
    {
        public enum DisplayMode
        {
            Normal,
            VR,
            EyeTrackedHolography // this mode should use DXGI swapchain.
        }
        public enum ProjectionMode
        {
            Perspective = 0,
            Orthographic = 1
        }
        
        private bool lookAt_set, azimuth_set, altitude_set, distance_set, fov_set, displayMode_set;
        private bool projectionMode_set;
        private bool world2phy_set, azimuth_range_set, altitude_range_set, xyz_range_set, mmb_freelook_set, pan_range_set;
        private Vector3 _lookAt;

        // default value: azimuth -pi/2, altitude pi/2
        private float _azimuth, _altitude, _distance, _fov;
        private float _world2phy;
        private Vector2 _azimuth_range, _altitude_range;
        private Vector3 _pan_range;
        private Vector3 _xyzRangeD;
        private bool _mmb_freelook;
        private DisplayMode _displayMode;
        private ProjectionMode _projectionMode;

        public Vector3 lookAt { get => _lookAt; set { _lookAt = value; lookAt_set = true; } }
        // azi/alt relative to lookat pos.
        public float azimuth { get => _azimuth; set { _azimuth = value; azimuth_set = true; } }
        public float altitude { get => _altitude; set { _altitude = value; altitude_set = true; } }
        public float distance { get => _distance; set { _distance = value; distance_set = true; } }
        public float fov { get => _fov; set { _fov = value; fov_set = true; } }
        public DisplayMode displayMode { get => _displayMode; set { _displayMode = value; displayMode_set = true; } }
        public ProjectionMode projectionMode { get => _projectionMode; set { _projectionMode = value; projectionMode_set = true; } }
        
        public float world2phy { get => _world2phy; set { _world2phy = value; world2phy_set = true; } }
        public Vector2 azimuth_range { get => _azimuth_range; set { _azimuth_range = value; azimuth_range_set = true; } }
        public Vector2 altitude_range { get => _altitude_range; set { _altitude_range = value; altitude_range_set = true; } }
        public Vector3 pan_range { get => _pan_range; set { _pan_range = value; pan_range_set = true; } }
        // camera position range delta 
        public Vector3 xyz_rangeD { get => _xyzRangeD; set { _xyzRangeD = value; xyz_range_set = true; } }
        public bool mmb_freelook { get => _mmb_freelook; set { _mmb_freelook = value; mmb_freelook_set = true; } }

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
            cb.Append(projectionMode_set);
            if (projectionMode_set) cb.Append((int)_projectionMode);
            cb.Append(displayMode_set);
            if (displayMode_set) cb.Append((int)_displayMode);
            
            cb.Append(world2phy_set);
            if (world2phy_set) cb.Append(_world2phy);
            
            cb.Append(azimuth_range_set);
            if (azimuth_range_set)
            {
                cb.Append(_azimuth_range.X);
                cb.Append(_azimuth_range.Y);
            }
            
            cb.Append(altitude_range_set);
            if (altitude_range_set)
            {
                cb.Append(_altitude_range.X);
                cb.Append(_altitude_range.Y);
            }
            
            cb.Append(xyz_range_set);
            if (xyz_range_set)
            {
                cb.Append(_xyzRangeD.X);
                cb.Append(_xyzRangeD.Y);
                cb.Append(_xyzRangeD.Z);
            }
            
            cb.Append(mmb_freelook_set);
            if (mmb_freelook_set) cb.Append(_mmb_freelook);
            
            cb.Append(pan_range_set);
            if (pan_range_set)
            {
                cb.Append(_pan_range.X);
                cb.Append(_pan_range.Y);
                cb.Append(_pan_range.Z);
            }
        }
    }

    public class SetAppearance : CommonWorkspaceState
    {
        // color scheme is RGBA
        private bool useEDL_set, useSSAO_set, useGround_set, useBorder_set, useBloom_set, drawGrid_set, drawGuizmo_set;
        private bool hover_shine_set, selected_shine_set, hover_border_color_set, selected_border_color_set, world_border_color_set;
        private bool clippingPlanes_set;
        private bool btf_on_hovering_set;
        private bool sun_altitude_set;
        private float _sun_altitude = 0f; // Default value, darkness.

        private bool _useEDL = true, _useSSAO = true, _useGround = true, _useBorder = true, _useBloom = true, _drawGrid = true, _drawGuizmo = true;
        private uint _hover_shine = 0x99990099, _selected_shine = 0xff0000ff, _hover_border_color = 0xffff00ff, _selected_border_color = 0xff0000ff, _world_border_color = 0xffffffff;
        private (Vector3 center, Vector3 direction)[] _clippingPlanes = new (Vector3, Vector3)[4];
        private int _activePlanes = 0;
        private bool _btf_on_hovering = true;
        // region voxel appearance
        private bool voxel_quantize_set, voxel_opacity_set;
        private float _voxel_quantize = 0.3f, _voxel_opacity = 0.5f;

        public bool useEDL { get => _useEDL; set { _useEDL = value; useEDL_set = true; } }
        public bool useSSAO { get => _useSSAO; set { _useSSAO = value; useSSAO_set = true; } }
        public bool useGround { get => _useGround; set { _useGround = value; useGround_set = true; } }
        public bool useBorder { get => _useBorder; set { _useBorder = value; useBorder_set = true; } }
        public bool useBloom { get => _useBloom; set { _useBloom = value; useBloom_set = true; } }

        [Obsolete]
        public bool drawGrid
        {
            get => drawGroundGrid;
            set => drawGroundGrid = value;
        }

        public bool drawGroundGrid { get => _drawGrid; set { _drawGrid = value; drawGrid_set = true; } }

        public bool drawGuizmo { get => _drawGuizmo; set { _drawGuizmo = value; drawGuizmo_set = true; } }
        
        public uint hover_shine { get => _hover_shine; set { _hover_shine = value; hover_shine_set = true; } }
        public uint selected_shine { get => _selected_shine; set { _selected_shine = value; selected_shine_set = true; } }
        public uint hover_border_color { get => _hover_border_color; set { _hover_border_color = value; hover_border_color_set = true; } }
        public uint selected_border_color { get => _selected_border_color; set { _selected_border_color = value; selected_border_color_set = true; } }
        public uint world_border_color { get => _world_border_color; set { _world_border_color = value; world_border_color_set = true; } }
        
        public (Vector3 center, Vector3 direction)[] clippingPlanes { 
            get => _clippingPlanes;
            set { 
                if (value == null || value.Length > 4)
                    throw new ArgumentException("Must provide 0-4 clipping planes");
                Array.Clear(_clippingPlanes, 0, 4);
                Array.Copy(value, _clippingPlanes, value.Length);
                _activePlanes = value.Length;
                clippingPlanes_set = true;
            }
        }

        public bool bring2front_onhovering { get => _btf_on_hovering; set { _btf_on_hovering = value; btf_on_hovering_set = true; } }

        public float sun_altitude { get => _sun_altitude; set { _sun_altitude = value; sun_altitude_set = true; } }
        public float voxel_quantize { get => _voxel_quantize; set { _voxel_quantize = value; voxel_quantize_set = true; } }
        public float voxel_opacity { get => _voxel_opacity; set { _voxel_opacity = value; voxel_opacity_set = true; } }

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
            
            cb.Append(clippingPlanes_set);
            if (clippingPlanes_set)
            {
                cb.Append(_activePlanes);
                for (int i = 0; i < _activePlanes; i++)
                {
                    cb.Append(_clippingPlanes[i].center.X);
                    cb.Append(_clippingPlanes[i].center.Y);
                    cb.Append(_clippingPlanes[i].center.Z);
                    cb.Append(_clippingPlanes[i].direction.X);
                    cb.Append(_clippingPlanes[i].direction.Y);
                    cb.Append(_clippingPlanes[i].direction.Z);
                }
            }
            
            cb.Append(btf_on_hovering_set);
            if (btf_on_hovering_set) cb.Append(_btf_on_hovering);

            cb.Append(sun_altitude_set);
            if (sun_altitude_set) cb.Append(_sun_altitude);

            // voxel params
            cb.Append(voxel_quantize_set);
            if (voxel_quantize_set) cb.Append(_voxel_quantize);
            cb.Append(voxel_opacity_set);
            if (voxel_opacity_set) cb.Append(_voxel_opacity);
        }
    }

    public class SetOperatingGridAppearance : CommonWorkspaceState
    {
        private bool pivot_set, unitX_set, unitY_set, show_set;

        private Vector3 _pivot = Vector3.Zero;
        private Vector3 _unitX = new Vector3(1, 0, 0);
        private Vector3 _unitY = new Vector3(0, 1, 0);
        private bool _show = false;

        public Vector3 pivot { get => _pivot; set { _pivot = value; pivot_set = true; } }
        public Vector3 unitX { get => _unitX; set { _unitX = value; unitX_set = true; } }
        public Vector3 unitY { get => _unitY; set { _unitY = value; unitY_set = true; } }
        public bool show { get => _show; set { _show = value; show_set = true; } }
        public bool useViewPlane = false;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(56); // API ID for SetGridAppearance

            cb.Append(show_set);
            if (show_set)
                cb.Append(_show);
            // Send all the grid settings flags first
            cb.Append(pivot_set);
            // Send the actual values for set parameters
            if (pivot_set)
            {
                cb.Append(_pivot.X);
                cb.Append(_pivot.Y);
                cb.Append(_pivot.Z);
            }

            cb.Append(useViewPlane);
            if (!useViewPlane)
            {
                cb.Append(unitX_set);
                if (unitX_set)
                {
                    cb.Append(_unitX.X);
                    cb.Append(_unitX.Y);
                    cb.Append(_unitX.Z);
                }

                cb.Append(unitY_set);
                if (unitY_set)
                {
                    cb.Append(_unitY.X);
                    cb.Append(_unitY.Y);
                    cb.Append(_unitY.Z);
                }
            }
        }
    }

    [Obsolete]
    public class SetGridAppearance : CommonWorkspaceState
    {
        private bool pivot_set, unitX_set, unitY_set, show_set;
        
        private Vector3 _pivot = Vector3.Zero;
        private Vector3 _unitX = new Vector3(1, 0, 0);
        private Vector3 _unitY = new Vector3(0, 1, 0);
        private bool _show = false;

        public Vector3 pivot { get => _pivot; set { _pivot = value; pivot_set = true; } }
        public Vector3 unitX { get => _unitX; set { _unitX = value; unitX_set = true; } }
        public Vector3 unitY { get => _unitY; set { _unitY = value; unitY_set = true; } }
        public bool show { get => _show; set { _show = value; show_set = true; } }
        public bool useViewPlane = false;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(56); // API ID for SetGridAppearance

            cb.Append(show_set);
            if (show_set)
                cb.Append(_show);
            // Send all the grid settings flags first
            cb.Append(pivot_set);
            // Send the actual values for set parameters
            if (pivot_set)
            {
                cb.Append(_pivot.X);
                cb.Append(_pivot.Y);
                cb.Append(_pivot.Z);
            }

            cb.Append(useViewPlane);
            if (!useViewPlane)
            {
                cb.Append(unitX_set);
                if (unitX_set)
                {
                    cb.Append(_unitX.X);
                    cb.Append(_unitX.Y);
                    cb.Append(_unitX.Z);
                }

                cb.Append(unitY_set);
                if (unitY_set)
                {
                    cb.Append(_unitY.X);
                    cb.Append(_unitY.Y);
                    cb.Append(_unitY.Z);
                }
            }
        }
    }

    public class SetImGUIStyle : CommonWorkspaceState
    {
        // Color flags - track which colors have been set
        private bool text_set, textDisabled_set, windowBg_set, childBg_set, popupBg_set;
        private bool border_set, borderShadow_set, frameBg_set, frameBgHovered_set, frameBgActive_set;
        private bool titleBg_set, titleBgActive_set, titleBgCollapsed_set, menuBarBg_set;
        private bool scrollbarBg_set, scrollbarGrab_set, scrollbarGrabHovered_set, scrollbarGrabActive_set;
        private bool checkMark_set, sliderGrab_set, sliderGrabActive_set;
        private bool button_set, buttonHovered_set, buttonActive_set;
        private bool header_set, headerHovered_set, headerActive_set;
        private bool separator_set, separatorHovered_set, separatorActive_set;

        // Style property flags
        private bool windowPadding_set, framePadding_set, itemSpacing_set, itemInnerSpacing_set;
        private bool windowRounding_set, frameRounding_set, windowBorderSize_set, frameBorderSize_set;
        private bool indentSpacing_set, scrollbarSize_set, grabMinSize_set;

        // Color values (RGBA as uint32)
        private uint _text = 0xFFFFFFFF, _textDisabled = 0xFF808080, _windowBg = 0xFF171717, _childBg = 0x00000000, _popupBg = 0xFF120D12;
        private uint _border = 0x80555555, _borderShadow = 0x00000000, _frameBg = 0xFF000000, _frameBgHovered = 0xFF1A260F, _frameBgActive = 0xFF0D5557;
        private uint _titleBg = 0xFF0A0A0A, _titleBgActive = 0xFF751F8C, _titleBgCollapsed = 0x78000000, _menuBarBg = 0xFF1F1C4F;
        private uint _scrollbarBg = 0x00050505, _scrollbarGrab = 0xFF4F4F4F, _scrollbarGrabHovered = 0xFF696969, _scrollbarGrabActive = 0xFF828282;
        private uint _checkMark = 0xFF1AFA44, _sliderGrab = 0xFF694F4F, _sliderGrabActive = 0xFFA4182E;
        private uint _button = 0xFF3D3835, _buttonHovered = 0xFF8F3D9A, _buttonActive = 0xFF610AF8;
        private uint _header = 0xFF590A42, _headerHovered = 0xFF451CA1, _headerActive = 0xFF7142F8;
        private uint _separator = 0x806E6E80, _separatorHovered = 0xC7A11AC0, _separatorActive = 0xFF8A11C0;

        // Style values
        private Vector2 _windowPadding = new Vector2(8, 8), _framePadding = new Vector2(8, 3), _itemSpacing = new Vector2(12, 5), _itemInnerSpacing = new Vector2(8, 6);
        private float _windowRounding = 6.0f, _frameRounding = 6.0f, _windowBorderSize = 1.0f, _frameBorderSize = 1.0f;
        private float _indentSpacing = 25.0f, _scrollbarSize = 15.0f, _grabMinSize = 19.0f;

        // Color properties
        public uint text { get => _text; set { _text = value; text_set = true; } }
        public uint textDisabled { get => _textDisabled; set { _textDisabled = value; textDisabled_set = true; } }
        public uint windowBg { get => _windowBg; set { _windowBg = value; windowBg_set = true; } }
        public uint childBg { get => _childBg; set { _childBg = value; childBg_set = true; } }
        public uint popupBg { get => _popupBg; set { _popupBg = value; popupBg_set = true; } }
        public uint border { get => _border; set { _border = value; border_set = true; } }
        public uint borderShadow { get => _borderShadow; set { _borderShadow = value; borderShadow_set = true; } }
        public uint frameBg { get => _frameBg; set { _frameBg = value; frameBg_set = true; } }
        public uint frameBgHovered { get => _frameBgHovered; set { _frameBgHovered = value; frameBgHovered_set = true; } }
        public uint frameBgActive { get => _frameBgActive; set { _frameBgActive = value; frameBgActive_set = true; } }
        public uint titleBg { get => _titleBg; set { _titleBg = value; titleBg_set = true; } }
        public uint titleBgActive { get => _titleBgActive; set { _titleBgActive = value; titleBgActive_set = true; } }
        public uint titleBgCollapsed { get => _titleBgCollapsed; set { _titleBgCollapsed = value; titleBgCollapsed_set = true; } }
        public uint menuBarBg { get => _menuBarBg; set { _menuBarBg = value; menuBarBg_set = true; } }
        public uint scrollbarBg { get => _scrollbarBg; set { _scrollbarBg = value; scrollbarBg_set = true; } }
        public uint scrollbarGrab { get => _scrollbarGrab; set { _scrollbarGrab = value; scrollbarGrab_set = true; } }
        public uint scrollbarGrabHovered { get => _scrollbarGrabHovered; set { _scrollbarGrabHovered = value; scrollbarGrabHovered_set = true; } }
        public uint scrollbarGrabActive { get => _scrollbarGrabActive; set { _scrollbarGrabActive = value; scrollbarGrabActive_set = true; } }
        public uint checkMark { get => _checkMark; set { _checkMark = value; checkMark_set = true; } }
        public uint sliderGrab { get => _sliderGrab; set { _sliderGrab = value; sliderGrab_set = true; } }
        public uint sliderGrabActive { get => _sliderGrabActive; set { _sliderGrabActive = value; sliderGrabActive_set = true; } }
        public uint button { get => _button; set { _button = value; button_set = true; } }
        public uint buttonHovered { get => _buttonHovered; set { _buttonHovered = value; buttonHovered_set = true; } }
        public uint buttonActive { get => _buttonActive; set { _buttonActive = value; buttonActive_set = true; } }
        public uint header { get => _header; set { _header = value; header_set = true; } }
        public uint headerHovered { get => _headerHovered; set { _headerHovered = value; headerHovered_set = true; } }
        public uint headerActive { get => _headerActive; set { _headerActive = value; headerActive_set = true; } }
        public uint separator { get => _separator; set { _separator = value; separator_set = true; } }
        public uint separatorHovered { get => _separatorHovered; set { _separatorHovered = value; separatorHovered_set = true; } }
        public uint separatorActive { get => _separatorActive; set { _separatorActive = value; separatorActive_set = true; } }

        // Style properties
        public Vector2 windowPadding { get => _windowPadding; set { _windowPadding = value; windowPadding_set = true; } }
        public Vector2 framePadding { get => _framePadding; set { _framePadding = value; framePadding_set = true; } }
        public Vector2 itemSpacing { get => _itemSpacing; set { _itemSpacing = value; itemSpacing_set = true; } }
        public Vector2 itemInnerSpacing { get => _itemInnerSpacing; set { _itemInnerSpacing = value; itemInnerSpacing_set = true; } }
        public float windowRounding { get => _windowRounding; set { _windowRounding = value; windowRounding_set = true; } }
        public float frameRounding { get => _frameRounding; set { _frameRounding = value; frameRounding_set = true; } }
        public float windowBorderSize { get => _windowBorderSize; set { _windowBorderSize = value; windowBorderSize_set = true; } }
        public float frameBorderSize { get => _frameBorderSize; set { _frameBorderSize = value; frameBorderSize_set = true; } }
        public float indentSpacing { get => _indentSpacing; set { _indentSpacing = value; indentSpacing_set = true; } }
        public float scrollbarSize { get => _scrollbarSize; set { _scrollbarSize = value; scrollbarSize_set = true; } }
        public float grabMinSize { get => _grabMinSize; set { _grabMinSize = value; grabMinSize_set = true; } }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(59); // Command ID for SetImGUIStyle

            // Serialize color flags and values
            cb.Append(text_set);
            if (text_set) cb.Append(_text);
            
            cb.Append(textDisabled_set);
            if (textDisabled_set) cb.Append(_textDisabled);
            
            cb.Append(windowBg_set);
            if (windowBg_set) cb.Append(_windowBg);
            
            cb.Append(childBg_set);
            if (childBg_set) cb.Append(_childBg);
            
            cb.Append(popupBg_set);
            if (popupBg_set) cb.Append(_popupBg);
            
            cb.Append(border_set);
            if (border_set) cb.Append(_border);
            
            cb.Append(borderShadow_set);
            if (borderShadow_set) cb.Append(_borderShadow);
            
            cb.Append(frameBg_set);
            if (frameBg_set) cb.Append(_frameBg);
            
            cb.Append(frameBgHovered_set);
            if (frameBgHovered_set) cb.Append(_frameBgHovered);
            
            cb.Append(frameBgActive_set);
            if (frameBgActive_set) cb.Append(_frameBgActive);
            
            cb.Append(titleBg_set);
            if (titleBg_set) cb.Append(_titleBg);
            
            cb.Append(titleBgActive_set);
            if (titleBgActive_set) cb.Append(_titleBgActive);
            
            cb.Append(titleBgCollapsed_set);
            if (titleBgCollapsed_set) cb.Append(_titleBgCollapsed);
            
            cb.Append(menuBarBg_set);
            if (menuBarBg_set) cb.Append(_menuBarBg);
            
            cb.Append(scrollbarBg_set);
            if (scrollbarBg_set) cb.Append(_scrollbarBg);
            
            cb.Append(scrollbarGrab_set);
            if (scrollbarGrab_set) cb.Append(_scrollbarGrab);
            
            cb.Append(scrollbarGrabHovered_set);
            if (scrollbarGrabHovered_set) cb.Append(_scrollbarGrabHovered);
            
            cb.Append(scrollbarGrabActive_set);
            if (scrollbarGrabActive_set) cb.Append(_scrollbarGrabActive);
            
            cb.Append(checkMark_set);
            if (checkMark_set) cb.Append(_checkMark);
            
            cb.Append(sliderGrab_set);
            if (sliderGrab_set) cb.Append(_sliderGrab);
            
            cb.Append(sliderGrabActive_set);
            if (sliderGrabActive_set) cb.Append(_sliderGrabActive);
            
            cb.Append(button_set);
            if (button_set) cb.Append(_button);
            
            cb.Append(buttonHovered_set);
            if (buttonHovered_set) cb.Append(_buttonHovered);
            
            cb.Append(buttonActive_set);
            if (buttonActive_set) cb.Append(_buttonActive);
            
            cb.Append(header_set);
            if (header_set) cb.Append(_header);
            
            cb.Append(headerHovered_set);
            if (headerHovered_set) cb.Append(_headerHovered);
            
            cb.Append(headerActive_set);
            if (headerActive_set) cb.Append(_headerActive);
            
            cb.Append(separator_set);
            if (separator_set) cb.Append(_separator);
            
            cb.Append(separatorHovered_set);
            if (separatorHovered_set) cb.Append(_separatorHovered);
            
            cb.Append(separatorActive_set);
            if (separatorActive_set) cb.Append(_separatorActive);

            // Serialize style property flags and values
            cb.Append(windowPadding_set);
            if (windowPadding_set) {
                cb.Append(_windowPadding.X);
                cb.Append(_windowPadding.Y);
            }
            
            cb.Append(framePadding_set);
            if (framePadding_set) {
                cb.Append(_framePadding.X);
                cb.Append(_framePadding.Y);
            }
            
            cb.Append(itemSpacing_set);
            if (itemSpacing_set) {
                cb.Append(_itemSpacing.X);
                cb.Append(_itemSpacing.Y);
            }
            
            cb.Append(itemInnerSpacing_set);
            if (itemInnerSpacing_set) {
                cb.Append(_itemInnerSpacing.X);
                cb.Append(_itemInnerSpacing.Y);
            }
            
            cb.Append(windowRounding_set);
            if (windowRounding_set) cb.Append(_windowRounding);
            
            cb.Append(frameRounding_set);
            if (frameRounding_set) cb.Append(_frameRounding);
            
            cb.Append(windowBorderSize_set);
            if (windowBorderSize_set) cb.Append(_windowBorderSize);
            
            cb.Append(frameBorderSize_set);
            if (frameBorderSize_set) cb.Append(_frameBorderSize);
            
            cb.Append(indentSpacing_set);
            if (indentSpacing_set) cb.Append(_indentSpacing);
            
            cb.Append(scrollbarSize_set);
            if (scrollbarSize_set) cb.Append(_scrollbarSize);
            
            cb.Append(grabMinSize_set);
            if (grabMinSize_set) cb.Append(_grabMinSize);
        }
    }

    public class WorldPosition
    {
        public Vector3 mouse_pos;
        public string snapping_object;
        public Vector3 object_pos;
        public int sub_id;

    }

    public class GetPosition : WorkspaceUIOperation<WorldPosition>
    {
        // todo: if any snapping object, make it yellow. enable object snapping.
        public override string Name { get; set; } = "Get Position";
        public string[] snaps = [];

        public enum PickMode
        {
            GridPlane,
            Holo3D
        };

        public PickMode method = PickMode.GridPlane;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(36);
            cb.Append(OpID);
            cb.Append(Name);
            cb.Append((int)method); // Send plane mode
            cb.Append(snaps.Length);
            for (int i = 0; i < snaps.Length; i++)
            {
                cb.Append(snaps[i]);
            }
        }

        protected override WorldPosition Deserialize(BinaryReader binaryReader)
        {
            var ret = new WorldPosition();
            ret.mouse_pos.X = binaryReader.ReadSingle();
            ret.mouse_pos.Y = binaryReader.ReadSingle();
            ret.mouse_pos.Z = binaryReader.ReadSingle();
            if (binaryReader.ReadBoolean())
            {
                ret.snapping_object = ReadString(binaryReader);
                ret.object_pos.X = binaryReader.ReadSingle();
                ret.object_pos.Y = binaryReader.ReadSingle();
                ret.object_pos.Z = binaryReader.ReadSingle();
                ret.sub_id = binaryReader.ReadInt32();
            }

            return ret;
        }

        //todo...
        // public void SetObjectSnappable(string name) =>
        //     ChangeState(new SetObjectSelectableOrNot() { name = name });
        //
        // public void SetObjectUnsnappable(string name) =>
        //     ChangeState(new SetObjectSelectableOrNot() { name = name, selectable = false });
        //
        // public void SetObjectSubSnappable(string name) =>
        //     ChangeState(new SetObjectSubSelectableOrNot() { name = name });
        //
        // public void SetObjectSubUnsnappable(string name) =>
        //     ChangeState(new SetObjectSubSelectableOrNot() { name = name, subselectable = false });
    }


    public class FollowFeedback
    {
        public Vector3 mouse_start_XYZ, mouse_end_XYZ;
        public string start_snapping_object, end_snapping_object;
        public Dictionary<string, Vector3> follower_objects_position;
    }

    // drag mouse on the world, a line is shown in yellow with an arrow.
    // if any follower objects, move them by the same amount.
    public class FollowMouse : WorkspaceUIOperation<FollowFeedback>
    {
        // todo: show a yellow line arrow, or show a yellow rect.
        public override string Name { get; set; } = "Follow Mouse";
        public string[] follower_objects = []; 
        public string[] start_snapping_objects = [], end_snapping_objects = [];

        public enum FollowingMethod
        {
            LineOnGrid, RectOnGrid, // normal screen
            Line3D, Rect3D, Box3D, Sphere3D,// holography screen
            PointOnGrid, CircleOnGrid
        }

        public FollowingMethod method = FollowingMethod.LineOnGrid;

        public bool realtime = false;
        public bool allow_same_place = false; //todo.

        protected internal override void Serialize(CB cb)
        {
            cb.Append(47); // API ID for FollowMouse (follow_mouse_operation in C++)
            cb.Append(OpID);
            cb.Append(Name);
            cb.Append((int)method); // Send plane mode first
            cb.Append(realtime);
            cb.Append(allow_same_place);

            cb.Append(follower_objects.Length);
            for (int i = 0; i < follower_objects.Length; i++)
            {
                cb.Append(follower_objects[i]);
            }

            cb.Append(start_snapping_objects.Length);
            for (int i = 0; i < start_snapping_objects.Length; i++)
            {
                cb.Append(start_snapping_objects[i]);
            }

            cb.Append(end_snapping_objects.Length);
            for (int i = 0; i < end_snapping_objects.Length; i++)
            {
                cb.Append(end_snapping_objects[i]);
            }
        }   

        protected override FollowFeedback Deserialize(BinaryReader binaryReader)
        {
            var ret = new FollowFeedback();
            
            // Read start mouse position
            ret.mouse_start_XYZ = new Vector3();
            ret.mouse_start_XYZ.X = binaryReader.ReadSingle();
            ret.mouse_start_XYZ.Y = binaryReader.ReadSingle();
            ret.mouse_start_XYZ.Z = binaryReader.ReadSingle();

            // Read end mouse position
            ret.mouse_end_XYZ = new Vector3();
            ret.mouse_end_XYZ.X = binaryReader.ReadSingle();
            ret.mouse_end_XYZ.Y = binaryReader.ReadSingle();
            ret.mouse_end_XYZ.Z = binaryReader.ReadSingle();

            // Read start snapping object info
            if (binaryReader.ReadBoolean())
            {
                ret.start_snapping_object = ReadString(binaryReader);
            }
            
            // Read end snapping object info
            if (binaryReader.ReadBoolean())
            {
                ret.end_snapping_object = ReadString(binaryReader);
            }
            
            // Read follower objects positions
            int follower_count = binaryReader.ReadInt32();
            ret.follower_objects_position = new Dictionary<string, Vector3>();
            
            for (int i = 0; i < follower_count; i++)
            {
                string object_name = ReadString(binaryReader);
                Vector3 pos = new Vector3();
                pos.X = binaryReader.ReadSingle();
                pos.Y = binaryReader.ReadSingle();
                pos.Z = binaryReader.ReadSingle();
                
                ret.follower_objects_position[object_name] = pos;
            }
            
            return ret;
        }

        // Allows the user to set objects that can be snapped to at the start of the operation
        public void SetStartSnappingObjects(string[] objects)
        {
            start_snapping_objects = objects;
        }
        
        // Allows the user to set objects that can be snapped to at the end of the operation
        public void SetEndSnappingObjects(string[] objects)
        {
            end_snapping_objects = objects;
        }
        
        // Set the objects that should follow the mouse movement
        public void SetFollowerObjects(string[] objects)
        {
            follower_objects = objects;
        }
    }

    public class SetMainMenuBar : CommonWorkspaceState
    {
        public List<MenuItem> menu
        {
            set
            {
                if (value != null) StaticMenu = value;
            }
        }

        public bool show
        {
            set => StaticShow = value;
        }

        private static List<MenuItem> StaticMenu;
        private static bool StaticShow;

        static SetMainMenuBar()
        {
            Workspace.PropActions[3] = (t, br) =>
            {
                List<MenuItem> current = StaticMenu;
                var len = br.ReadInt32() / 4;
                for (var i = 0; i < len; ++i)
                {
                    if (current == null) break;
                    var idx = br.ReadInt32();
                    var item = current[idx];
                    if ((item.SubItems == null || item.SubItems.Count == 0) && item.OnClick != null)
                    {
                        Task.Run(item.OnClick);
                        // Panel.Repaint(); // to update selected
                        break;
                    }
                    current = item.SubItems;
                }
            };
        }

        protected internal override void Serialize(CB cb)
        {
            MenuItem.GenerateCB(cb, StaticMenu, StaticShow, 37,
                PanelBuilder.ImHashStrWithPanelId($"MainMenuBar", -1));
        }
    }

    public class SelectObject : WorkspaceUIOperation<(string name, BitArray selector, string firstSub)[]>
    {
        public override string Name { get; set; } = "Select Object";

        public enum SelectionMode
        {
            Click,
            Rectangle,
            Paint
        }

        class SetSelectionModeCmd : WorkspaceUIState
        {
            public int mode;
            public float paintRadius = 10; // only used for paint mode

            protected internal override void Serialize(CB cb)
            {
                // 43: SetSelectionMode
                cb.Append(43);
                cb.Append(mode);
                cb.Append(paintRadius);
            }
        }

        public void SetSelectionMode(SelectionMode mode, float paintRadius = 10f)
        {
            ChangeState(new SetSelectionModeCmd() 
            { 
                mode = (int)mode, 
                paintRadius = paintRadius 
            });
        }

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
        // public string[] snaps = [];

        public bool realtimeResult = false;
        protected internal override void Serialize(CB cb)
        {
            cb.Append(10);
            cb.Append(OpID);
            cb.Append(Name);
            cb.Append(realtimeResult);
            cb.Append((int)type);
            // cb.Append(snaps.Length);
            // for (int i = 0; i < snaps.Length; i++)
            // {
            //     cb.Append(snaps[i]);
            // }
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