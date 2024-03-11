using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
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

    public class PutImage : WorkspacePropAPI
    {
        public bool billboard;
        public bool perspective = true;
        public float displayH, displayW; //any value<=0 means auto fit.
        public byte[] rgba; //if update, simply invoke putimage again.

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
