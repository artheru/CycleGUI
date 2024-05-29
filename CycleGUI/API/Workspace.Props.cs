using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CycleGUI.Terminals;
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
    public abstract class WorkspacePropAPI : Workspace.WorkspaceAPI
    {
        static internal List<Workspace.WorkspaceAPI> Initializers = new();
        static internal Dictionary<string, Workspace.WorkspaceAPI> Revokables = new();

        public void SubmitReversible(string name)
        {
            lock (Workspace.preliminarySync)
                Revokables[name] = this;
            foreach (var terminal in Terminal.terminals)
                lock (terminal)
                    terminal.PendingCmds.Add(this, name);
        }

        public void SubmitLoadings()
        {
            lock (Workspace.preliminarySync)
                Initializers.Add(this);
            foreach (var terminal in Terminal.terminals)
                lock (terminal)
                    terminal.PendingCmds.Add(this);
        }
    }


    public class SetCameraPosition : WorkspacePropAPI
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

        internal override void Submit()
        {
            SubmitReversible($"camera_pos");
        }
    }

    public class SetCameraType : WorkspacePropAPI
    {
        public float fov;

        protected internal override void Serialize(CB cb)
        {
            cb.Append(15);
            cb.Append(fov);
        }

        internal override void Submit()
        {
            SubmitReversible($"camera_type");
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
    }

    public class PutPointCloud : WorkspacePropAPI
    {
        public string name;
        public Vector3 newPosition;
        public Quaternion newQuaternion = Quaternion.Identity;
        public Vector4[] xyzSzs;
        public uint[] colors;
        // todo:
        
        public string handleString; //empty:nohandle, single char: character handle, multiple char: png.

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
    }

    public class PutStraightLine : WorkspacePropAPI
    {
        public string name;
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

    public class PutShape : WorkspacePropAPI
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
    }


    public class PutExtrudedGeometry : WorkspacePropAPI
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
    }


    public class PutText : WorkspacePropAPI
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
    }

    public class SetPoseLocking : WorkspacePropAPI
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
    }


    public class SetInteractionProxy : WorkspacePropAPI
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
    }
    
    // basically static. if update, higher latency(must wait for sync)
    public class PutARGB:WorkspacePropAPI
    {
        public const int PropActionID = 0;

        public string name;
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
            lock (Workspace.preliminarySync)
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
            Workspace.PropActions[PropActionID] = (t, br) =>
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

        private static string ffmpegpath = "ffmpeg.exe";
        public static void SetFFMpegPath(string path)
        {
            ffmpegpath=path;
        }
    }

    public enum LengthMetric
    {
        Meter, Pixel, ScreenRatio
    }

    public class PutImage : WorkspacePropAPI
    {
        public string name;
        public bool billboard;
        public LengthMetric metric;
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
            cb.Append(billboard);
            cb.Append((byte)metric);
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
    }

    // define a static existing spot text.
    public class SpotText : WorkspacePropAPI
    {
        public string name;
        private List<Action<CB>> builder = new();

        public SpotText DrawAtWorld(Color color, Vector3 position, string text, string relative=null)
        {
            builder.Add(cb =>
            {
                cb.Append((byte)LengthMetric.Meter);
                cb.Append(relative);
                cb.Append(position.X);
                cb.Append(position.Y);
                cb.Append(position.Z);
                cb.Append(color.RGBA8());
                cb.Append(text);
            });
            return this;
        }

        public SpotText DrawAtScreenPixel(Color color, Vector2 position, string text, string relative = null)
        {
            builder.Add(cb =>
            {
                cb.Append((byte)LengthMetric.Pixel);
                cb.Append(relative);
                cb.Append(position.X);
                cb.Append(position.Y);
                cb.Append(0f);
                cb.Append(color.RGBA8());
                cb.Append(text);
            });
            return this;
        }

        public SpotText DrawAtScreenRatio(Color color, Vector2 position, string text, string relative = null)
        {
            builder.Add(cb =>
            {
                cb.Append((byte)LengthMetric.ScreenRatio);
                cb.Append(relative);
                cb.Append(position.X);
                cb.Append(position.Y);
                cb.Append(0f);
                cb.Append(color.RGBA8());
                cb.Append(text);
            });
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
            cb.Append(builder.Count);
            for (var i = 0; i < builder.Count; i++)
                builder[i](cb);
        }
    }

    public class PutModelObject : WorkspacePropAPI
    {
        public string clsName, name;
        public Vector3 newPosition;
        public Quaternion newQuaternion = Quaternion.Identity;
        public string defaultAnimation;

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

    public class SetModelObjectProperty : WorkspacePropAPI
    {
        public bool border, shine, front, selected;
        public Color shineColor;
        public string nextAnimation;
        internal override void Submit()
        {
            throw new NotImplementedException();
        }

        protected internal override void Serialize(CB cb)
        {
            throw new NotImplementedException();
        }
    }
}
