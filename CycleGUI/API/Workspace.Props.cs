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

        static internal List<WorkspaceAPI> Initializers = new();
        static internal Dictionary<string, WorkspaceAPI> Revokables = new();

        internal void SubmitReversible(string name)
        {
            lock (preliminarySync)
                Revokables[name] = this;
            foreach (var terminal in Terminal.terminals)
                lock (terminal)
                    terminal.PendingCmds.Add(this, name);
        }

        class RemoveObject : WorkspaceAPI
        {
            public string name;
            internal override void Submit()
            {
                foreach (var terminal in Terminal.terminals)
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
                foreach (var terminal in Terminal.terminals)
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

        internal void RemoveReversible(string wsname, string objname)
        {
            lock (preliminarySync)
                Revokables.Remove(wsname);
            new RemoveObject() { name = objname }.Submit();
        }

        internal void SubmitLoadings()
        {
            lock (preliminarySync)
                Initializers.Add(this);
            foreach (var terminal in Terminal.terminals)
                lock (terminal)
                    terminal.PendingCmds.Add(this);
        }

        public abstract void Remove();
    }


    public class LoadModel : WorkspaceProp
    {
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

        internal override void Submit()
        {
            SubmitLoadings();
        }

        public override void Remove()
        {
            throw new Exception("not allowed to remove model");
        }
    }


    public class TransformObject: WorkspaceProp
    {
        public enum Type
        {
            PosRot=0, Pos=1, Rot=2
        }
        public enum Coord
        {
            Absolute=0, Relative=1
        }

        public Type type = Type.PosRot;
        public Coord coord = Coord.Absolute;
        public Vector3 pos;
        public Quaternion quat;
        public int timeMs;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(5);
            cb.Append(name);
            cb.Append((byte)type);
            cb.Append((byte)coord);
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

        public override void Remove()
        {
            // not useful.
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

        public bool showWalkableRegion; // walkable region is always on z=0 plane.

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

            cb.Append(handleString);
        }

        public override void Remove()
        {
            RemoveReversible($"pointcloud#{name}", name);
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

        protected internal override void Serialize(CB cb)
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
            cb.Append(0);
        }

        public override void Remove()
        {
            RemoveReversible($"line#{name}", name);
        }
    }
    public class PutBezierCurve : PutStraightLine
    {
        public Vector3[] controlPnts;
    }
    public class PutCircleCurve : PutStraightLine
    {
        public float arcDegs;
    }

    public class ShapePath
    {
        public ShapePath MoveTo()
        {
            return null;
        }

        public ShapePath absarc()
        {
            return null;
        }
    }

    public class ShapeBuilder:ShapePath
    {
    }

    public class BuiltinShapes
    {
        public static ShapePath Circle()
        {
            return null;
        } 
    }

    // public class PutBoxGeometry : WorkspaceProp
    // {
    //     public string name;
    //     public Vector3 size;   
    // }

    public class PutShape : WorkspaceProp
    {
        public string name;
        public ShapePath shapePath;
        public bool closed;

        internal override void Submit()
        {
            throw new NotImplementedException();
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }

        public override void Remove()
        {
            throw new NotImplementedException();
        }
    }


    public class PutExtrudedGeometry : WorkspaceProp
    {
        public string name;
        public Vector2[] crossSection;
        public Vector3[] path;
        public bool closed;

        internal override void Submit()
        {
            throw new NotImplementedException();
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }

        public override void Remove()
        {
            throw new NotImplementedException();
        }
    }


    public class PutText : WorkspaceProp
    {
        public bool billboard;
        public bool perspective = true;
        public float size;
        public string text;

        internal override void Submit()
        {
            throw new NotImplementedException();
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }

        public override void Remove()
        {
            throw new NotImplementedException();
        }
    }

    public class SetPoseLocking : WorkspaceProp
    {
        public string earth, moon; // moon is locked to the earch.

        internal override void Submit()
        {
            throw new NotImplementedException();
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }

        public override void Remove()
        {
            throw new NotImplementedException();
        }
    }


    public class SetInteractionProxy : WorkspaceProp
    {
        public string proxy, who; // interaction on who is transfered to proxy, like moving/hovering... all apply on proxy.

        internal override void Submit()
        {
            throw new NotImplementedException();
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }

        public override void Remove()
        {
            throw new NotImplementedException();
        }
    }
    
    // basically static. if update, higher latency(must wait for sync)
    // todo: PutARGB is actually putting resources, not workspace item. use a dedicate class.
    public class PutARGB:WorkspaceProp
    {
        public const int PropActionID = 0;

        public int width, height;
        
        public byte[] rgba;
        public delegate byte[] OnRGBARequested();
        public OnRGBARequested requestRGBA; //if rgba set to null, image is shown on demand.
        
        private static ConcurrentDictionary<string, PutARGB> hooks = new();

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
            foreach (var terminal in Terminal.terminals)
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
        // must use the function object very very fast. this is quickly packed and sent to the display.
        public Action<byte[]> StartStreaming() // force update with lowest latency.
        {
            var ptr = LocalTerminal.RegisterStreamingBuffer(name, width * height * 4);

            var streaming = new RGBAStreaming() { name = name };
            lock (preliminarySync)
            {
                Initializers.Add(this);
                Initializers.Add(streaming);
            }

            foreach (var terminal in Terminal.terminals)
                lock (terminal)
                    terminal.PendingCmds.Add(streaming, $"streaming#{name}");
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

                    var jpeg = Utilities.GetJPG(rgbaCached, width, height, quality);
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
                        quality = Math.Max(quality - 3, 5);
                    else if (ewmaElapsed < 30)
                        quality = Math.Min(quality + 3, 50);
                    //Console.WriteLine($"ewma={ewmaElapsed}, sending quality as {quality}");
                }
            });
            return (bytes) =>
            {
                rgbaCached = bytes;
                lock (this)
                    Monitor.PulseAll(this);
                frameCnt += 1;
                Marshal.Copy(bytes, 0, ptr, width * height * 4);
            };
        }

        static PutARGB()
        {
            PropActions[PropActionID] = (t, br) =>
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
                        throw new Exception($"RequestRGBA of RGB:{sname}, return null byte array");
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

    public class PutWidgetImage : WorkspaceProp
    {
        // priority: aspect ratio, wh, argb aspect ratio.
        // all added together.
        public Vector2 UV_WidthHeight, UV_Pos, Pixel_Pos, Pixel_WidthHeight;
        public float RotationDegree, AspectRatio;
        public string argbName="";

        internal override void Submit()
        {
            SubmitReversible($"widget#{name}");
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(29);
            cb.Append(name);
            cb.Append(UV_WidthHeight);
            cb.Append(UV_Pos);
            cb.Append(Pixel_Pos);
            cb.Append(Pixel_WidthHeight);
            cb.Append(RotationDegree);
            cb.Append(argbName);
        }

        public override void Remove()
        {
            RemoveReversible($"widget#{name}", name);
        }
    }

    public class PutImage : WorkspaceProp
    {
        public enum DisplayType
        {
            World, World_Billboard
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
            RemoveReversible($"image#{name}", name);
        }
    }

    // define a static existing spot text.
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
            RemoveReversible($"spottext#{name}", name);
        }
    }

    public class PutModelObject : WorkspaceProp
    {
        public string clsName;
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

        public override void Remove()
        {
            RemoveReversible($"{clsName}#{name}", name);
        }
    }

    public struct PerNodeInfo
    {
        public Vector3 translation;
        public float flag;
        public Quaternion rotation;

        public void SetFlag(bool border=false, bool shine = false, bool front = false, bool selected = false)
        {

        }

        public void SetShineColor(Color color)
        {

        }
    }

    // public class SetModelObjectProperty : WorkspacePropAPI
    // {
    //     public bool border, shine, front, selected;
    //     public Color shineColor;
    //     public string nextAnimation;
    //     internal override void Submit()
    //     {
    //         throw new NotImplementedException();
    //     }
    //
    //     protected internal override void Serialize(CB cb)
    //     {
    //         throw new NotImplementedException();
    //     }
    // }

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
            RemoveReversible($"mesh#{clsname}", clsname);
        }
    }

}
