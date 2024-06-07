using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FundamentalLib;

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
            protected internal abstract void Deserialize(BinaryReader br);
        }

        public List<Widget> Widgets = new List<Widget>();
        public void AddWidget<T>(T widget) where T: Widget
        {
            ChangeState(widget);
            Widgets.Add(widget);
        }

        public class ThrottleWidget : Widget
        {
            public string text;
            public string position; //css like string.
            public string size;
            public bool onlyUseHandle = false;
            public bool useTwoWay = false;
            public bool bounceBack = false;

            public Action<float> onValueChanged;
            public float value;
            protected internal override void Serialize(CB cb)
            {
                cb.Append(30);
                cb.Append(name);
                cb.Append(text);
                cb.Append(position);
                cb.Append(size);
                cb.Append((byte)((onlyUseHandle?1:0)|(useTwoWay?2:0)|(bounceBack?4:0)));
            }

            protected internal override void Deserialize(BinaryReader br)
            {
                value = br.ReadSingle();
                onValueChanged(value);
            }
        }

        public override string Name { get; set; } = "GestureControl";

        protected internal override void Serialize(CB cb)
        {
            cb.Append(26);
            cb.Append(OpID);
            cb.Append(Name);
        }

        private long lastMs = X.watch.ElapsedTicks;
        protected override float Deserialize(BinaryReader binaryReader)
        {
            var length = binaryReader.ReadInt32();
            for(int i=0; i < length; ++i)
            {
                var name = ReadString(binaryReader);
                if (name != Widgets[i].name) throw new Exception("???");
                Widgets[i].Deserialize(binaryReader);
            }

            var tdicc = X.watch.ElapsedTicks - lastMs;
            lastMs=X.watch.ElapsedTicks;
            return (float)((double)tdicc / Stopwatch.Frequency * 1000);
        }
    }
}
