using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CycleGUI.Terminals;
using static CycleGUI.API.PutImage;
using static CycleGUI.Viewport;
using static CycleGUI.Workspace;

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
            public Vector3 ColorBias = Vector3.Zero; // Add color bias (0-1 range)
            public float ColorScale = 1;
            public float Brightness = 1;
            public bool ForceDblFace = false;
            public float NormalShading = 0;

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
    public abstract class WorkspaceProp : WorkspaceAPI
    {
        public string name;

        static internal Dictionary<string, int> Initializers = new();

        static internal void AddInitializer(string name, WorkspaceAPI api)
        {
            lock (preliminarySync)
            {
                var id = InitializerList.Count;
                InitializerList.Add(api);
                Initializers[name] = id;
            }
        }
        static internal List<WorkspaceAPI> InitializerList = new();

        static internal Dictionary<string, WorkspaceAPI> Revokables = new();

        internal void SubmitOnce()
        {
            foreach (var terminal in Terminal.terminals.Keys)
                if (!(terminal is ViewportSubTerminal))
                    lock (terminal)
                        terminal.PendingCmds.Add(this);
        }

        internal void SubmitReversible(string name)
        {
            lock (preliminarySync)
                Revokables[name] = this;
            foreach (var terminal in Terminal.terminals.Keys)
                if (!(terminal is ViewportSubTerminal))
                    lock (terminal)
                        terminal.PendingCmds.Add(this, name);
        }

        class RemoveObject : WorkspaceAPI
        {
            public string name;
            internal override void Submit()
            {
                foreach (var terminal in Terminal.terminals.Keys)
                    if (!(terminal is ViewportSubTerminal))
                        lock (terminal)
                            terminal.PendingCmds.Add(this, name);
            }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(28);
                cb.Append(name);
            }
        }

        class RemoveNamePatternAPI : WorkspaceAPI
        {
            public string name;
            internal override void Submit()
            {
                foreach (var terminal in Terminal.terminals.Keys)
                    if (!(terminal is ViewportSubTerminal))
                        lock (terminal)
                            terminal.PendingCmds.Add(this, name);
            }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(18); // 18 is the ID for RemoveNamePattern
                cb.Append(name);
            }
        }

        public static void RemoveNamePattern(string namePattern)
        {
            bool WildcardMatch(string text, string pattern)
            {
                int n = text.Length;
                int m = pattern.Length;
                int i = 0, j = 0, startIndex = -1, match = 0;

                while (i < n)
                {
                    if (j < m && (pattern[j] == '?' || pattern[j] == text[i]))
                    {
                        i++;
                        j++;
                    }
                    else if (j < m && pattern[j] == '*')
                    {
                        startIndex = j;
                        match = i;
                        j++;
                    }
                    else if (startIndex != -1)
                    {
                        j = startIndex + 1;
                        match++;
                        i = match;
                    }
                    else
                    {
                        return false;
                    }
                }

                while (j < m && pattern[j] == '*')
                {
                    j++;
                }

                return j == m;
            }
            lock (preliminarySync){
                foreach (var key in Revokables.Keys.ToArray())
                    if (WildcardMatch(key, namePattern)) Revokables.Remove(key);
            }
            // use wildcard match to perform name match, in revokables. remove and issue a RemoveNamePatternAPI
            new RemoveNamePatternAPI(){name = namePattern}.Submit();
        }


        internal void RemoveReversible(string wsname)
        {
            lock (preliminarySync)
                Revokables.Remove(wsname);
        }

        internal void RemoveProp(string wsname, string objname)
        {
            lock (preliminarySync)
                Revokables.Remove(wsname);
            new RemoveObject() { name = objname }.Submit();
        }

        internal void SubmitLoadings(string name)
        {
            AddInitializer(name, this);
            foreach (var terminal in Terminal.terminals.Keys)
                if (!(terminal is ViewportSubTerminal))
                    lock (terminal)
                        terminal.PendingCmds.Add(this);
        }

        public abstract void Remove();

        public void Transform(TransformObject to)
        {
            to.name = name;
            to.preciseName = true;
            to.Submit();
        }
    }


    public class LoadModel : WorkspaceProp
    {
        public ModelDetail detail;

        protected bool UpdateExisting = false;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(3);
            cb.Append(name);
            if (UpdateExisting)
                cb.Append(0);
            else
            {
                cb.Append(detail.GLTF.Length);
                cb.Append(detail.GLTF);
            }

            cb.Append(detail.Center.X);
            cb.Append(detail.Center.Y);
            cb.Append(detail.Center.Z);
            cb.Append(detail.Rotate.X);
            cb.Append(detail.Rotate.Y); 
            cb.Append(detail.Rotate.Z);
            cb.Append(detail.Rotate.W);
            cb.Append(detail.Scale);
            cb.Append(detail.ColorBias.X);
            cb.Append(detail.ColorBias.Y);
            cb.Append(detail.ColorBias.Z);
            cb.Append(detail.ColorScale);
            cb.Append(detail.Brightness);
            cb.Append(detail.ForceDblFace);
            cb.Append(detail.NormalShading);
        }

        internal override void Submit()
        {
            SubmitLoadings($"gltf_{name}");
        }

        public override void Remove()
        {
            throw new Exception("not allowed to remove model");
        }
    }

    public class ReloadModel : LoadModel
    {
        public ReloadModel()
        {
            UpdateExisting = true;
        }

        internal override void Submit()
        {
            if (Initializers.TryGetValue($"gltf_{name}", out var i) && InitializerList[i] is LoadModel lm)
            {
                var gltf = lm.detail.GLTF;
                lm.detail = detail;
                lm.detail.GLTF = gltf;
                base.Submit();
                return;
            }

            throw new Exception($"Model {name} is not loaded yet");
        }

        public override void Remove()
        {
            throw new Exception("not allowed to remove model");
        }
    }

    public class SetObjectMoonTo : WorkspaceProp
    {
        public Vector3 pos;
        public Quaternion quat;
        public string earth = "";

        internal override void Submit()
        {
            SubmitReversible($"anchor#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(42);
            cb.Append(earth);
            cb.Append(name);
            cb.Append(pos.X);
            cb.Append(pos.Y);
            cb.Append(pos.Z);
            cb.Append(quat.X);
            cb.Append(quat.Y);
            cb.Append(quat.Z);
            cb.Append(quat.W);
        }

        public override void Remove()
        {
            // not useful.
        }
    }

    public class TransformObject : WorkspaceProp
    {
        public enum Type
        {
            PosRot = 0, Pos = 1, Rot = 2
        }
        public enum Coord
        {
            Absolute = 0, Relative = 1
        }

        // Private backing fields
        private Vector3 _pos;
        private Quaternion _quat = Quaternion.Identity;
        private bool _posSet = false;
        private bool _quatSet = false;

        // Public properties with flags
        public Coord coord = Coord.Absolute;
        public Vector3 pos
        {
            get => _pos;
            set
            {
                _pos = value;
                _posSet = true;
            }
        }

        public Quaternion quat
        {
            get => _quat;
            set
            {
                _quat = value;
                _quatSet = true;
            }
        }

        public int timeMs;
        public Terminal terminal = null; //if not null, perform a temporary transform on the specified terminal.

        // Get effective type based on which properties were set
        private Type GetEffectiveType()
        {
            if (_posSet && _quatSet) return Type.PosRot;
            if (_posSet) return Type.Pos;
            if (_quatSet) return Type.Rot;
            return Type.PosRot; // Default if nothing set
        }

        internal bool preciseName = false;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(5);
            cb.Append(preciseName);
            cb.Append(name);
            cb.Append((byte)GetEffectiveType());
            cb.Append((byte)coord);
            cb.Append(_pos.X);
            cb.Append(_pos.Y);
            cb.Append(_pos.Z);
            cb.Append(_quat.X);
            cb.Append(_quat.Y);
            cb.Append(_quat.Z);
            cb.Append(_quat.W);
            cb.Append(timeMs);
        }

        internal override void Submit()
        {
            SubmitReversible($"transform#{name}");
        }

        public override void Remove()
        {
            // not useful.
        }
    }

    public class TransformSubObject : WorkspaceProp
    {
        public enum Type
        {
            PosRot = 0, Pos = 1, Rot = 2
        }

        public enum SelectionMode
        {
            ByName = 0, ById = 1
        }

        public enum ActionMode
        {
            Set = 0, Revert = 1
        }

        // Private backing fields
        private string _objectNamePattern;
        private string _subObjectName = "";
        private int _subObjectId = -1;
        private Vector3 _translation = Vector3.Zero;
        private Quaternion _rotation = Quaternion.Identity;
        private int _timeMs;
        private bool _revert = false;

        // Flags to track which properties have been explicitly set
        private bool _translationSet = false;
        private bool _rotationSet = false;
        private bool _subObjectNameSet = false;
        private bool _subObjectIdSet = false;

        // Public properties with setters that update flags
        public string objectNamePattern { get => _objectNamePattern; set { _objectNamePattern = value; } }

        public string subObjectName
        {
            get => _subObjectName;
            set
            {
                _subObjectName = value;
                _subObjectNameSet = true;
            }
        }

        public int subObjectId
        {
            get => _subObjectId;
            set
            {
                _subObjectId = value;
                _subObjectIdSet = true;
            }
        }

        public Vector3 translation
        {
            get => _translation;
            set
            {
                _translation = value;
                _translationSet = true;
            }
        }

        public Quaternion rotation
        {
            get => _rotation;
            set
            {
                _rotation = value;
                _rotationSet = true;
            }
        }

        public int timeMs { get => _timeMs; set { _timeMs = value; } }

        public bool Revert
        {
            get => _revert;
            set { _revert = value; }
        }

        // Determine type based on which properties were explicitly set
        private Type GetEffectiveType()
        {
            if (_translationSet && _rotationSet) return Type.PosRot;
            if (_translationSet) return Type.Pos;
            if (_rotationSet) return Type.Rot;
            return Type.PosRot; // Default
        }

        // Determine selection mode based on which properties were explicitly set
        private SelectionMode GetEffectiveSelectionMode()
        {
            if (_subObjectNameSet) return SelectionMode.ByName;
            if (_subObjectIdSet) return SelectionMode.ById;
            return SelectionMode.ByName; // Default
        }

        // Determine action mode based on Revert flag
        private ActionMode GetEffectiveActionMode()
        {
            return _revert ? ActionMode.Revert : ActionMode.Set;
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(44);
            cb.Append(_objectNamePattern);
            cb.Append((byte)GetEffectiveSelectionMode());
            cb.Append(_subObjectName);
            cb.Append(_subObjectId);
            cb.Append((byte)GetEffectiveActionMode());
            cb.Append((byte)GetEffectiveType());
            cb.Append(_translation.X);
            cb.Append(_translation.Y);
            cb.Append(_translation.Z);
            cb.Append(_rotation.X);
            cb.Append(_rotation.Y);
            cb.Append(_rotation.Z);
            cb.Append(_rotation.W);
            cb.Append(_timeMs);
        }

        internal override void Submit()
        {
            SelectionMode selMode = GetEffectiveSelectionMode();
            string subObjIdentifier = selMode == SelectionMode.ByName ? _subObjectName : _subObjectId.ToString();

            if (GetEffectiveActionMode() == ActionMode.Revert)
            {
                RemoveReversible($"transform_sub#{_objectNamePattern}#{subObjIdentifier}");
                SubmitOnce();
            }
            else
                SubmitReversible($"transform_sub#{_objectNamePattern}#{subObjIdentifier}");
        }

        public override void Remove()
        {
            // Create a new instance with same targeting but revert mode
            var revert = new TransformSubObject
            {
                objectNamePattern = this._objectNamePattern,
                subObjectName = this._subObjectName,
                subObjectId = this._subObjectId,
                Revert = true
            };
            revert.Submit();
        }
    }

    public class PutPointCloud : WorkspaceProp
    {
        public Vector3 newPosition;
        public Quaternion newQuaternion = Quaternion.Identity;
        public Vector4[] xyzSzs;
        public uint[] colors;
        // todo:
        
        public string handleString; //empty:nohandle, single char: character handle, multiple char: png.

        public int type; // 0: normal point clout,
                         // 1: local_map of a slam_map( will show walkable region ).

        internal override void Submit()
        {
            SubmitReversible($"pointcloud#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(0);
            cb.Append(name);
            bool isVolatile = xyzSzs.Length == 0;
            int capacity = isVolatile ? Math.Max(1, 1) : xyzSzs.Length;
            cb.Append(isVolatile);
            cb.Append(capacity);
            cb.Append(xyzSzs.Length); // initN

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

            cb.Append(handleString);

            // new fields
            cb.Append(type);
        }

        public override void Remove()
        {
            RemoveProp($"pointcloud#{name}", name);
        }
    }

    public class PutStraightLine : WorkspaceProp
    {
        public string propStart="", propEnd=""; //if empty, use position.
        public Vector3 start, end;
        public Painter.ArrowType arrowType = Painter.ArrowType.None;
        public int width = 1; // in pixel
        public Color color;
        public int dashDensity = 0; //0: no dash

        internal override void Submit()
        {
            SubmitReversible($"line#{name}");
        }

        protected internal void LineMeta(CB cb)
        {
            cb.Append(21);
            cb.Append(name);
            cb.Append(propStart);
            cb.Append(propEnd);
            cb.Append(start.X);
            cb.Append(start.Y);
            cb.Append(start.Z);
            cb.Append(end.X);
            cb.Append(end.Y);
            cb.Append(end.Z);
            uint metaint = (uint)(((int)arrowType) | (dashDensity << 8) | (Math.Min(255, (int)width) << 16));
            cb.Append(metaint); // convert to float to fit opengl vertex attribute requirement.
            cb.Append(color.RGBA8());
        }
        protected internal override void Serialize(CB cb)
        {
            LineMeta(cb);
            cb.Append(0);
        }

        public override void Remove()
        {
            RemoveProp($"line#{name}", name);
        }
    }
    public class PutBezierCurve : PutStraightLine
    {
        public Vector3[] controlPnts;

        protected internal override void Serialize(CB cb)
        {
            // linemeta add line common start and end info, so we just append line type in the following.
            LineMeta(cb);
            cb.Append(1); // Line type 1 for Bezier curve
            cb.Append(controlPnts.Length);
            foreach (var point in controlPnts)
            {
                cb.Append(point.X);
                cb.Append(point.Y);
                cb.Append(point.Z);
            }
        }
    }

    
    public class PutVector : PutStraightLine
    {
        public int pixelLength = 10;
        protected internal override void Serialize(CB cb)
        {
            LineMeta(cb);
            cb.Append(2); // Line type 2 for vector.
            cb.Append(pixelLength);
        }
    }
    
    // basically static. if update, higher latency(must wait for sync)
    // todo: PutRGBA is actually putting resources, not workspace item. use a dedicate class.
    public class PutRGBA:WorkspaceProp
    {
        public int width, height;

        public enum DisplayType
        {
            Normal,
            Stereo3D_LR,
        }

        public DisplayType displayType;
        
        public byte[] rgba;
        public delegate byte[] OnRGBARequested();
        public OnRGBARequested requestRGBA; //if rgba set to null, image is shown on demand.
        
        private static ConcurrentDictionary<string, PutRGBA> hooks = new();

        class RGBAUpdater : WorkspaceAPI
        {
            public string name;
            public byte[] rgba;

            internal override void Submit() { }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(23);
                cb.Append(name);
                cb.Append(rgba.Length);
                cb.Append(rgba);
            }
        }

        class RGBAInvalidate : WorkspaceAPI
        {
            public string name;
            internal override void Submit() { }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(25);
                cb.Append(name);
            }
        }
        class RGBAStreaming : WorkspaceAPI
        {
            public string name;
            internal override void Submit() { }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(24);
                cb.Append(name);
            }
        }

        public void Invalidate()
        {
            var invalidate = new RGBAInvalidate() { name = name };
            foreach (var terminal in Terminal.terminals.Keys)
                if (!(terminal is ViewportSubTerminal))
                    lock (terminal)
                        terminal.PendingCmds.Add(invalidate, $"invalidate#{name}");
        }
        
        public void UpdateRGBA(byte[] bytes) // might not actually updates(depends on whether the rgba is used)
        {
            Invalidate();
            if (bytes.Length != width * height *4)
                throw new Exception(
                    $"Size not match, requires {width}x{height}x4={width * height * 4}B, {bytes.Length}given");
            rgba = bytes;
            requestRGBA = () => rgba;
            hooks[name] = this;
        }

        static Stopwatch sw=Stopwatch.StartNew();

        // call the returned function object with minimal latency. this is quickly packed and sent to the display.
        public Action<byte[]> StartStreaming() // force update with lowest latency.
        {
            Console.WriteLine($"Start streaming for `{name}`");

            IntPtr ptr = IntPtr.Zero;
            if (!LocalTerminal.Headless)
                ptr = LocalTerminal.RegisterStreamingBuffer(name, width * height * 4);

            var streaming = new RGBAStreaming() { name = name };
            var api_name = $"streaming#{name}";

            AddInitializer(api_name, streaming);
            foreach (var terminal in Terminal.terminals.Keys)
                if (!(terminal is ViewportSubTerminal))
                    lock (terminal)
                        terminal.PendingCmds.Add(streaming, api_name);

            byte[] rgbaCached = null;
            int frameCnt = 0;
            // variable quality MJPEG for streaming.
            LeastServer.AddSpecialTreat($"/stream/{name}", (_, networkStream, socket) =>
            {
                Console.WriteLine($"Serve RGBA stream {name} for {socket.RemoteEndPoint}.");

                var boundary = "frame";
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: multipart/x-mixed-replace; boundary={boundary}\r\n\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(header);

                networkStream.Write(headerBytes, 0, headerBytes.Length);
                int quality = 50;
                double smoothingFactor = 0.1; // Smoothing factor for EWMA
                double ewmaElapsed = 0;
                while (socket.Connected)
                {
                    var tic = sw.ElapsedTicks;
                    var prevFrameCnt = frameCnt;
                    lock (this)
                        Monitor.Wait(this);
                    var lag = frameCnt - prevFrameCnt - 1; //>0 means frame been skipped.

                    // if quality <50, also resize using scale = (2*quality)%.
                    var srcBmp = new SoftwareBitmap(width, height, rgbaCached);
                    SoftwareBitmap encBmp = srcBmp;
                    if (quality < 50)
                    {
                        float scale = Math.Max(0.01f, (2f * quality) / 100f);
                        int newW = Math.Max(1, (int)Math.Round(width * scale));
                        int newH = Math.Max(1, (int)Math.Round(height * scale));
                        var resized = new SoftwareBitmap(newW, newH);
                        resized.DrawImage(srcBmp, 0, 0, scale);
                        encBmp = resized;
                    }
                    var jpeg = ImageCodec.SaveJpegToBytes(encBmp, quality);
                    var frameHeader = $"Content-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n";
                    var frameHeaderBytes = Encoding.ASCII.GetBytes(frameHeader);
                    var boundaryBytes = Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\n");

                    var tic2 = sw.ElapsedTicks;
                    networkStream.Write(frameHeaderBytes, 0, frameHeaderBytes.Length);
                    networkStream.Write(jpeg, 0, jpeg.Length);
                    networkStream.Write(boundaryBytes, 0, boundaryBytes.Length);
                    var toc = sw.ElapsedTicks;

                    // control jpg quality to meet current network status.
                    var elapsed = (toc - tic2) / (double)TimeSpan.TicksPerMillisecond;
                    ewmaElapsed = (smoothingFactor * elapsed) + ((1 - smoothingFactor) * ewmaElapsed);

                    // Adjust JPEG quality based on EWMA of elapsed time
                    if (ewmaElapsed > 50)
                        quality = Math.Max(quality - 3, 15);
                    else if (ewmaElapsed < 30)
                        quality = Math.Min(quality + 3, 70);
                    //Console.WriteLine($"ewma={ewmaElapsed}, sending quality as {quality}");
                }
            });
            return (bytes) =>
            {
                rgbaCached = bytes;
                lock (this)
                    Monitor.PulseAll(this);
                frameCnt += 1;
                if (ptr != IntPtr.Zero)
                    Marshal.Copy(bytes, 0, ptr, width * height * 4);
            };
        }

        static PutRGBA()
        {
            PropActions[0] = (t, br) =>
            {
                int num = br.ReadInt32();
                for (int i = 0; i < num; ++i)
                {
                    var byteLength = br.ReadInt32();
                    var byteArray = br.ReadBytes(byteLength);
                    string sname = Encoding.UTF8.GetString(byteArray);
                    if (!hooks.ContainsKey(sname)) continue;
                    var rgba = hooks[sname];
                    var bytes = rgba.requestRGBA();
                    if (bytes == null) continue;
                    if (bytes.Length != rgba.width * rgba.height *4)
                        Console.WriteLine($"RequestRGBA of RGB:{sname}, return len={bytes.Length}!={rgba.width*rgba.height*4}.");
                    lock (t)
                        t.PendingCmds.Add(new RGBAUpdater() { name = sname, rgba = bytes }, $"rgbaDt#{sname}");
                }

            };
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(22);
            cb.Append(name);
            cb.Append(width);
            cb.Append(height);
            cb.Append((int)displayType);
        }

        internal override void Submit()
        {
            if (rgba != null && requestRGBA != null)
                throw new Exception("plz provide either rgba or requestRGB but not both: RGBA data load now or just provide a fetcher.");
            if (rgba != null)
            {
                if (rgba.Length != width * height * 4)
                    throw new Exception(
                        $"Size not match, requires {width}x{height}x4={width * height * 4}B, {rgba.Length}given");
                requestRGBA = () => rgba;
            }

            if (requestRGBA != null)
                hooks[name] = this;
            SubmitReversible($"rgba#{name}");
        }

        public override void Remove()
        {
            throw new Exception("rgba removal not supported");
        }
    }
    
    // if attenuated, H/W is world, if unattenuated, H/W is pixel.
    public class PutImage : WorkspaceProp
    {
        public enum DisplayType
        {
            World, //00
            Billboard_Attenuated, //01
            World_Unattenuated, //10
            Billboard,  //11
        }

        public DisplayType displayType;

        public float displayH, displayW; //any value<=0 means auto fit.
        public Vector3 newPosition;
        public Quaternion newQuaternion = Quaternion.Identity;

        public string rgbaName; 
        //maybe opacity?

        internal override void Submit()
        {
            SubmitReversible($"image#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(20);
            cb.Append(name);
            cb.Append((byte)(displayType));
            cb.Append(displayH);
            cb.Append(displayW);
            cb.Append(newPosition.X);
            cb.Append(newPosition.Y);
            cb.Append(newPosition.Z);
            cb.Append(newQuaternion.X);
            cb.Append(newQuaternion.Y);
            cb.Append(newQuaternion.Z);
            cb.Append(newQuaternion.W);
            cb.Append(rgbaName);
        }

        public override void Remove()
        {
            RemoveProp($"image#{name}", name);
        }
    }

    public class DeclareSVG : WorkspaceProp
    {
        public string svgContent;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(53);
            cb.Append(name);
            cb.Append(svgContent);
        }

        internal override void Submit()
        {
            SubmitReversible($"svg#{name}");
        }

        public override void Remove()
        {
            throw new Exception("SVG removal not supported");
        }
    }

    // define a static existing spot text.
    // todo: not implemented yet. weird.
    public class SpotText : WorkspaceProp
    {
        private byte header = 0;
        private Vector3 worldpos;
        private Vector2 ndc_offset, pixel_offset, pivot;
        private string relative;

        private
            List<(byte header, Color color, string text, Vector3 worldpos, Vector2 ndc, Vector2 px, Vector2 pivot,
                string relative)> list =
                new();
        public SpotText WorldPos(Vector3 pos)
        {
            header |= 1 << 0;
            worldpos = pos;
            return this;
        }
        public SpotText NDCOffset(Vector2 offset)
        {
            header |= 1 << 1;
            ndc_offset = offset;
            return this;
        }
        public SpotText PixelOffset(Vector2 offset)
        {
            header |= 1 <<2;
            pixel_offset = offset;
            return this;
        }
        public SpotText Pivot(Vector2 p)
        {
            header |= 1 << 3;
            pivot = p;
            return this;
        }
        public SpotText RelativeTo(string what)
        {
            header |= 1 << 4;
            relative=what;
            return this;
        }

        public SpotText DrawText(string text, Color color)
        {
            if (header == 0) throw new Exception("Must give a pos");
            list.Add((header, color, text, worldpos, ndc_offset, pixel_offset, pivot, relative));
            header = 0;
            return this;
        }

        internal override void Submit()
        {
            SubmitReversible($"spottext#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            // clear spot text...
            cb.Append(13);
            cb.Append(name);

            cb.Append(12);
            cb.Append(name);
            cb.Append(list.Count);
            foreach (var ss in list)
            {
                cb.Append(ss.header);
                if ((ss.header & (1 << 0))!=0)
                {
                    cb.Append(ss.worldpos);
                }
                if ((ss.header & (1 << 1))!= 0)
                {
                    cb.Append(ss.ndc);
                }
                if ((ss.header & (1 << 2))!= 0)
                {
                    cb.Append(ss.px);
                }
                if ((ss.header & (1 << 3))!= 0)
                {
                    cb.Append(ss.pivot);
                }
                if ((ss.header & (1 << 4)) != 0)
                {
                    cb.Append(ss.relative);
                }

                cb.Append(ss.color.RGBA8());
                cb.Append(ss.text);
            }
        }

        public override void Remove()
        {
            RemoveProp($"spottext#{name}", name);
        }
    }

    public class PutModelObject : WorkspaceProp
    {
        public string clsName;
        public Vector3 newPosition;
        public Quaternion newQuaternion = Quaternion.Identity;
        public uint unitColor; //todo. what?

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

        public override void Remove()
        {
            RemoveProp($"{clsName}#{name}", name);
        }
    }
    
    public class DefineMesh : WorkspaceProp 
    {
        public string clsname;
        public float[] positions;  // Required xyz per vertex
        public uint color;         // Single color for all vertices
        public bool smooth;        // true: smooth normals, false: flat normals
        
        protected internal override void Serialize(CB cb)
        {
            if (positions.Length % 9 != 0)
                throw new Exception(
                    $"Defined mesh should be a list of triangle list, actually position length {positions.Length}%9!=0");
            cb.Append(34); // API ID for DefineMesh
            cb.Append(clsname);
            
            // Write vertex count and positions 
            cb.Append(positions.Length / 3); // vertex_count
            cb.Append(MemoryMarshal.Cast<float, byte>(positions.AsSpan()));
            
            // Write color and smoothing flag
            cb.Append((int)color);
            cb.Append(smooth);
        }

        internal override void Submit()
        {
            SubmitReversible($"mesh#{clsname}");
        }

        public override void Remove()
        {
            // non removable.
        }
    }

    public class CustomBackgroundShader : WorkspaceProp
    {
        public string shaderCode;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(45); // New command ID for custom background shader
            cb.Append(shaderCode);
        }

        internal override void Submit()
        {
            SubmitReversible($"custom_bg_shader");
        }

        public override void Remove()
        {
            RemoveReversible($"custom_bg_shader");
            new DisableCustomBackgroundShader().Submit();
        }
        
        class DisableCustomBackgroundShader : WorkspaceAPI
        {
            internal override void Submit()
            {
                foreach (var terminal in Terminal.terminals.Keys)
                    if (!(terminal is ViewportSubTerminal))
                        lock (terminal)
                            terminal.PendingCmds.Add(this);
            }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(46); // Command ID for disabling custom background shader
            }
        }

        static internal Action<string> OnEx;
        static CustomBackgroundShader()
        {
            PropActions[4] = (t, br) =>
            {
                var len = br.ReadInt32();
                byte[] bytes = br.ReadBytes(len);
                var str = Encoding.UTF8.GetString(bytes);
                OnEx?.Invoke(str);
            };
        }
    }

    public class SkyboxImage : WorkspaceProp
    {
        public byte[] imageData;
        public int width;
        public int height;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(57); // New API ID for SkyboxImage
            cb.Append(width);
            cb.Append(height);
            cb.Append(imageData.Length);
            cb.Append(imageData);
        }

        internal override void Submit()
        {
            SubmitReversible("custom_envmap");
        }

        public override void Remove()
        {
            RemoveReversible($"custom_envmap");
            new RemoveSkyboxImage().Submit();
        }

        class RemoveSkyboxImage : WorkspaceAPI
        {
            internal override void Submit()
            {
                foreach (var terminal in Terminal.terminals.Keys)
                    if (!(terminal is ViewportSubTerminal))
                        lock (terminal)
                            terminal.PendingCmds.Add(this);
            }

            protected internal override void Serialize(CB cb)
            {
                cb.Append(58); // New API ID for RemoveSkyboxImage
            }
        }

    }

    public class PutHandleIcon : WorkspaceProp
    {
        public Vector3 position; // Position if not pinned to an object
        public Quaternion quat = Quaternion.Identity; // add a default quat.
        public float size = 1.0f;
        public string icon; // Single character to show in the handle
        public Color color = Color.White; // Color of the handle
        public Color bgColor = Color.OrangeRed; // Background color of the handle

        internal override void Submit()
        {
            SubmitReversible($"handle#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(49); // ID for handle icon
            cb.Append(name);
            cb.Append(position.X);
            cb.Append(position.Y);
            cb.Append(position.Z);
            cb.Append(quat.X);
            cb.Append(quat.Y);
            cb.Append(quat.Z);
            cb.Append(quat.W);
            cb.Append(size);
            cb.Append(icon);
            cb.Append(color.RGBA8());
            cb.Append(bgColor.RGBA8());
        }

        public override void Remove()
        {
            RemoveProp($"handle#{name}", name);
        }
    }

    public enum TextVerticalAlignment
    {
        Up = 0,
        Middle = 1,
        Down = 2
    }

    // Text is always facing to screen.
    public class PutTextAlongLine : WorkspaceProp
    {
        public Vector3 start; // Start position if not pinned
        public Vector3 direction; // Direction vector for the text
        public string text; // Text to display
        public float size=1, verticalOffset;
        public bool billboard = false; //if billboard, direction is screenspace direction.
        public Color color = Color.White;

        public string directionProp = "";

        internal override void Submit()
        {
            SubmitReversible($"textline#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(55); // ID for text along line
            cb.Append(name);
            cb.Append(start.X);
            cb.Append(start.Y);
            cb.Append(start.Z);
            cb.Append(directionProp);
            cb.Append(direction.X);
            cb.Append(direction.Y);
            cb.Append(direction.Z);
            cb.Append(text);
            cb.Append(size);
            cb.Append(billboard);
            cb.Append(verticalOffset);
            cb.Append(color.RGBA8());
        }

        public override void Remove()
        {
            RemoveProp($"textline#{name}", name);
        }
    }

}
