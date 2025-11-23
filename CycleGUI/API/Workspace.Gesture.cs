using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
// using FundamentalLib;

namespace CycleGUI.API
{
    public class UseGesture : WorkspaceUIOperation<float>
    {
        public enum GestureType
        {
            //0:only touch, 1: can move, 2: can move and bounce back.
            TouchOnly, Move, BounceBack
        }

        public abstract class Widget : WorkspaceUIState
        {
            public string name;
            public string keyboard="", joystick="";

            protected internal abstract void Deserialize(BinaryReader br);
        }

        public abstract class Widget<T> : Widget
        {
            public Action<T> OnValue;
            public T value;
        }

        public List<Widget> Widgets = new List<Widget>();
        public void AddWidget<T>(T widget) where T: Widget
        {
            ChangeState(widget);
            Widgets.Add(widget);
        }

        public class ButtonWidget : Widget
        {
            public string text;
            public string position; //css like string.
            public string size;
            public Action<bool> OnPressed;
            public Action OnPush;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(27);
                cb.Append(name);
                cb.Append(text);
                cb.Append(position);
                cb.Append(size);
                cb.Append(keyboard.ToLower());
                cb.Append(joystick.ToLower());
            }

            private bool previousPress = false;
            protected internal override void Deserialize(BinaryReader br)
            {
                var value = br.ReadBoolean();
                if (OnPressed != null)
                    OnPressed(value);
                if (!previousPress && value && OnPush != null)
                    OnPush();
            }
        }

        public class ToggleWidget : Widget<bool>
        {
            public string text;
            public string position; //css like string.
            public string size;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(29);
                cb.Append(name);
                cb.Append(text);
                cb.Append(position);
                cb.Append(size);
                cb.Append(keyboard.ToLower());
                cb.Append(joystick.ToLower());
            }

            protected internal override void Deserialize(BinaryReader br)
            {
                value = br.ReadBoolean();
                if (OnValue != null)
                    OnValue(value);
            }
        }

        public class StickWidget : Widget
        {
            public string text;
            public string position; //css like string.
            public string size;
            public bool onlyUseHandle = false;
            public bool bounceBack = true;
            public Vector2 initPos = Vector2.Zero;

            public delegate void StickHandler(Vector2 pos, bool manipulating);

            public StickHandler OnValue;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(31);
                cb.Append(name);
                cb.Append(text);
                cb.Append(position);
                cb.Append(size);
                cb.Append((byte)((onlyUseHandle ? 1 : 0) | (bounceBack ? 2 : 0)));
                cb.Append(initPos.X);
                cb.Append(initPos.Y);
                cb.Append(keyboard.ToLower());
                cb.Append(joystick.ToLower());
            }

            protected internal override void Deserialize(BinaryReader br)
            {
                var xx = br.ReadSingle();
                var yy = br.ReadSingle();
                var manipulating = br.ReadBoolean();
                if (OnValue != null)
                    OnValue(new Vector2(xx, yy), manipulating);
            }
        }

        public class ThrottleWidget : Widget
        {
            public string text;
            public string position; //css like string.
            public string size;
            public bool onlyUseHandle = false;
            public bool useTwoWay = false;
            public bool bounceBack = false;

            public delegate void ThrottleHandler(float pos, bool manipulating);

            public ThrottleHandler OnValue;

            protected internal override void Serialize(CB cb)
            {
                cb.Append(30);
                cb.Append(name);
                cb.Append(text);
                cb.Append(position);
                cb.Append(size);
                cb.Append((byte)((onlyUseHandle?1:0)|(useTwoWay?2:0)|(bounceBack?4:0)));
                cb.Append(keyboard.ToLower());
                cb.Append(joystick.ToLower());
            }

            protected internal override void Deserialize(BinaryReader br)
            {
                var val = br.ReadSingle();
                var manipulating = br.ReadBoolean();
                if (OnValue != null)
                    OnValue(val, manipulating);
            }
        }

        public override string Name { get; set; } = "GestureControl";

        protected internal override void Serialize(CB cb)
        {
            cb.Append(26);
            cb.Append(OpID);
            cb.Append(Name);
        }

        private long lastMs = G.watch.ElapsedTicks;
        protected override float Deserialize(BinaryReader binaryReader)
        {
            var length = binaryReader.ReadInt32();
            for(int i=0; i < length; ++i)
            {
                var name = ReadString(binaryReader);
                if (name != Widgets[i].name) 
                    throw new Exception("???");
                Widgets[i].Deserialize(binaryReader);
            }

            var tdicc = G.watch.ElapsedTicks - lastMs;
            lastMs=G.watch.ElapsedTicks;
            return (float)((double)tdicc / Stopwatch.Frequency * 1000);
        }
    }
}
