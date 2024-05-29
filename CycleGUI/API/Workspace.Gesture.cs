using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace CycleGUI.API
{
    public class UseGesture : WorkspaceUIOperation<(string name, Vector3 pos)[]>
    {
        public enum GestureType
        {
            //0:only touch, 1: can move, 2: can move and bounce back.
            TouchOnly, Move, BounceBack
        }

        public class SetPropGestureBehaviour : WorkspaceUIState
        {
            public string name;

            public void JustTouch(string shortcuts="")
            {
                cber = cb =>
                {
                    cb.Append(0);
                };
            }

            private Action<CB> cber;
            public void MoveInScreenRect(Vector2 st, Vector2 ed, string shortcuts="")
            {
                cber = (cb =>
                {
                    cb.Append(1); //rect move.
                    cb.Append(st.X);
                    cb.Append(st.Y);
                    cb.Append(ed.X);
                    cb.Append(ed.Y);
                });
            }
            
            protected internal override void Serialize(CB cb)
            {
                cb.Append(27);
                cb.Append(name);
                cber(cb);
            }
        }

        public UseGesture Use(SetPropGestureBehaviour behaviour)
        {
            this.ChangeState(behaviour);
            return this;
        }

        public override string Name { get; set; } = "GestureControl";

        protected internal override void Serialize(CB cb)
        {
            cb.Append(26);
            cb.Append(OpID);
            cb.Append(Name);
        }

        protected override (string name, Vector3 pos)[] Deserialize(BinaryReader binaryReader)
        {
            List<(string name, Vector3 pos)> list = new();
            while (true)
            {
                var type = binaryReader.ReadInt32();
                if (type == -1) break;
                var name = ReadString(binaryReader);
                var x=binaryReader.ReadSingle();
                var y = binaryReader.ReadSingle();
                var z = binaryReader.ReadSingle();
                list.Add((name, new Vector3(x, y, z)));
            }

            return list.ToArray();
        }
    }
}
