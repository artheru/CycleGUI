using CycleGUI.API;
using CycleGUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace LearnCycleGUI.Demo
{
    internal class DemoUtilities
    {

        public struct Throttle // single directional throttle
        {
            public float val; //0~1
        }

        public struct DThrottle // dual directional throttle
        {
            public float dval; //-1~1
        }
        public struct Switch
        {
            public bool on;
        }
        public struct Button
        {
            public bool pressed;
        }
        public struct Stick
        {
            public float x, y; //-1~1
        }
        public struct Knob
        {
            public byte val;
            public int lastDir; // -1,0,+1
        }

        [Flags]
        public enum ControlItemLayoutPolicy
        {
            /// <summary>
            /// First search horizontally, from left to right. Then search vertically.
            /// </summary>
            Default = 0b00000000,

            /// <summary>
            /// First search vertically.
            /// </summary>
            VerticallySearchFirst = 0b00000001,

            /// <summary>
            /// Search from top to bottom.
            /// </summary>
            TopToBottom = 0b00000010,

            /// <summary>
            /// Search from right to left.
            /// </summary>
            RightToLeft = 0b00000100,
        }

        [AttributeUsage(AttributeTargets.Field)]
        public class AsControlItem : Attribute
        {
            public string name = "";
            public string keyboard = "";
            public string joystick = "";

            public int LayoutRow = -1;
            public int LayoutCol = -1;

            public int LayoutWidth = -1;
            public int LayoutHeight = -1;

            /// <summary>
            /// Policy is effective if Row and Col are non-negative.
            /// </summary>
            public ControlItemLayoutPolicy LayoutPolicy = ControlItemLayoutPolicy.Default;
        }

        public class TestRemote
        {
            //定义使用到的控制元器件
            [AsControlItem(name = "手柄", keyboard = "Up,Down,Left,Right", joystick = "Axis0,Axis1")] public Stick direction;
            [AsControlItem(name = "开关", keyboard = "Enter")] public Switch sw;
            [AsControlItem(name = "举升", keyboard = "Space")] public Button btn;
            [AsControlItem(name = "下降", keyboard = "T")] public Button btn2s;
            [AsControlItem(name = "最大速度", keyboard = "Q,W")] public Throttle speed;
        }

        public static unsafe PanelBuilder.CycleGUIHandler PreparePanel()
        {
            var manipulation = new UseGesture();
            bool _manualRemoteControlOn = false;
            DateTime lastDate = DateTime.Now;
            var manualObj = new TestRemote();

            void remote()
            {
                var fields = manualObj.GetType().GetFields();
                
                // from bottom to top arranging, left->right, screen is divided by 8x8 grid.
                const int gridNum = 8;
                const float gridPercentStep = 1f / gridNum;
                const int gridPaddingPx = 10;
                var occupancy = new bool[gridNum, gridNum];

                (float left, float top, int leftpx, int toppx) getPosition(int cols, int rows, bool autoLayout, int tRow,
                    int tCol, bool firstSearchRow, int horizontalDir, int verticalDir)
                {
                    bool testp(bool[,] arr, int nr, int nc)
                    {
                        if (nr + rows <= gridNum && nc + cols <= gridNum)
                        {
                            for (int m = 0; m < cols; ++m)
                                for (int n = 0; n < rows; ++n)
                                    if (arr[nc + m, nr + n])
                                        return false;

                            //ok, use.
                            for (int m = 0; m < cols; ++m)
                                for (int n = 0; n < rows; ++n)
                                    arr[nc + m, nr + n] = true;

                            return true;
                        }

                        return false;
                    }

                    (float ll, float tt, int llPx, int ttPx) GetReturnTuple(int ii, int jj)
                    {
                        return ((jj + cols / 2f) * gridPercentStep, 1.0f - (ii + rows / 2f) * gridPercentStep, 0, 0);
                    }

                    if (autoLayout)
                        for (var i = 0; i < gridNum; ++i)
                        {
                            for (var j = 0; j < gridNum / 2; ++j)
                            {
                                if (testp(occupancy, i, j))
                                    return GetReturnTuple(i, j);
                                if (testp(occupancy, i, gridNum - cols - j))
                                    return GetReturnTuple(i, gridNum - cols - j);
                            }
                        }
                    else
                    {
                        if (firstSearchRow)
                        {
                            for (var i = 0; i < gridNum; ++i)
                                for (var j = 0; j < gridNum; ++j)
                                    if (testp(occupancy, (i * verticalDir + tRow) % gridNum,
                                            (j * horizontalDir + tCol) % gridNum))
                                        return GetReturnTuple((i * verticalDir + tRow) % gridNum,
                                            (j * horizontalDir + tCol) % gridNum);
                        }
                        else
                        {
                            for (var j = 0; j < gridNum; ++j)
                                for (var i = 0; i < gridNum; ++i)
                                    if (testp(occupancy, (i * verticalDir + tRow) % gridNum,
                                            (j * horizontalDir + tCol) % gridNum))
                                        return GetReturnTuple((i * verticalDir + tRow) % gridNum,
                                            (j * horizontalDir + tCol) % gridNum);
                        }
                    }

                    return (0, 0, -1, -1);
                }

                string GetSizeString(int width, int height)
                {
                    return $"{12.5f * width}%-{gridPaddingPx}px, {12.5f * height}%-{gridPaddingPx}px";
                }

                foreach (var fi in fields)
                {
                    var aci = fi.GetCustomAttribute<AsControlItem>();
                    if (aci == null) continue;

                    var autoLayout = true;
                    var firstSearchRow = true;
                    var horizontalDir = 1;
                    var verticalDir = 1;

                    if (aci.LayoutCol >= 0 && aci.LayoutCol >= 0)
                    {
                        autoLayout = false;
                        if ((aci.LayoutPolicy & ControlItemLayoutPolicy.VerticallySearchFirst) != 0) firstSearchRow = false;
                        if ((aci.LayoutPolicy & ControlItemLayoutPolicy.TopToBottom) != 0) horizontalDir = -1;
                        if ((aci.LayoutPolicy & ControlItemLayoutPolicy.RightToLeft) != 0) verticalDir = -1;
                    }

                    if (fi.FieldType == typeof(Throttle))
                    {
                        int width = 4, height = 1;
                        if (aci.LayoutWidth >= 0) width = aci.LayoutWidth;
                        if (aci.LayoutHeight >= 0) height = aci.LayoutHeight;

                        var (l, t, lpx, tpx) =
                            getPosition(width, height, autoLayout, aci.LayoutRow, aci.LayoutCol, firstSearchRow, horizontalDir, verticalDir);
                        manipulation.AddWidget(new UseGesture.ThrottleWidget()
                        {
                            name = $"ctrl_{fi.Name}",
                            text = aci.name,
                            position = $"{l * 100}%+{lpx}px,{t * 100}%+{tpx}px",
                            size = GetSizeString(width, height),
                            keyboard = aci.keyboard,
                            joystick = aci.joystick,
                            // color? background? customizable?
                            OnValue = (f, _) => { fi.SetValue(manualObj, new Throttle() { val = f }); }
                        });
                        Console.WriteLine($"{aci.name}\t{fi.FieldType.Name}\t{l * 100}%+{lpx}px,{t * 100}%+{tpx}px");
                    }

                    if (fi.FieldType == typeof(DThrottle))
                    {
                        int width = 4, height = 1;
                        if (aci.LayoutWidth >= 0) width = aci.LayoutWidth;
                        if (aci.LayoutHeight >= 0) height = aci.LayoutHeight;

                        var (l, t, lpx, tpx) =
                            getPosition(width, height, autoLayout, aci.LayoutRow, aci.LayoutCol, firstSearchRow, horizontalDir, verticalDir);
                        manipulation.AddWidget(new UseGesture.ThrottleWidget()
                        {
                            name = $"ctrl_{fi.Name}",
                            text = aci.name,
                            position = $"{l * 100}%+{lpx}px,{t * 100}%+{tpx}px",
                            size = GetSizeString(width, height),
                            keyboard = aci.keyboard,
                            joystick = aci.joystick,
                            useTwoWay = true,
                            OnValue = (f, _) => { fi.SetValue(manualObj, new DThrottle() { dval = f }); }
                        });
                        Console.WriteLine($"{aci.name}\t{fi.FieldType.Name}\t{l * 100}%+{lpx}px,{t * 100}%+{tpx}px");
                    }

                    if (fi.FieldType == typeof(Switch))
                    {
                        int width = 2, height = 1;
                        if (aci.LayoutWidth >= 0) width = aci.LayoutWidth;
                        if (aci.LayoutHeight >= 0) height = aci.LayoutHeight;

                        var (l, t, lpx, tpx) =
                            getPosition(width, height, autoLayout, aci.LayoutRow, aci.LayoutCol, firstSearchRow, horizontalDir, verticalDir);
                        manipulation.AddWidget(new UseGesture.ToggleWidget()
                        {
                            name = $"ctrl_{fi.Name}",
                            text = aci.name,
                            position = $"{l * 100}%+{lpx}px,{t * 100}%+{tpx}px",
                            size = GetSizeString(width, height),
                            keyboard = aci.keyboard,
                            joystick = aci.joystick,
                            OnValue = (b) => { fi.SetValue(manualObj, new Switch() { on = b }); }
                        });
                        Console.WriteLine($"{aci.name}\t{fi.FieldType.Name}\t{l * 100}%+{lpx}px,{t * 100}%+{tpx}px");
                    }

                    if (fi.FieldType == typeof(Button))
                    {
                        int width = 2, height = 1;
                        if (aci.LayoutWidth >= 0) width = aci.LayoutWidth;
                        if (aci.LayoutHeight >= 0) height = aci.LayoutHeight;

                        var (l, t, lpx, tpx) =
                            getPosition(width, height, autoLayout, aci.LayoutRow, aci.LayoutCol, firstSearchRow, horizontalDir, verticalDir);
                        manipulation.AddWidget(new UseGesture.ButtonWidget()
                        {
                            name = $"ctrl_{fi.Name}",
                            text = aci.name,
                            position = $"{l * 100}%+{lpx}px,{t * 100}%+{tpx}px",
                            size = GetSizeString(width, height),
                            keyboard = aci.keyboard,
                            joystick = aci.joystick,
                            OnPressed = (b) => { fi.SetValue(manualObj, new Button() { pressed = b }); }
                        });
                        Console.WriteLine($"{aci.name}\t{fi.FieldType.Name}\t{l * 100}%+{lpx}px,{t * 100}%+{tpx}px");
                    }

                    if (fi.FieldType == typeof(Stick))
                    {
                        int width = 3, height = 3;
                        if (aci.LayoutWidth >= 0) width = aci.LayoutWidth;
                        if (aci.LayoutHeight >= 0) height = aci.LayoutHeight;

                        var (l, t, lpx, tpx) =
                            getPosition(width, height, autoLayout, aci.LayoutRow, aci.LayoutCol, firstSearchRow, horizontalDir, verticalDir);
                        manipulation.AddWidget(new UseGesture.StickWidget()
                        {
                            name = $"ctrl_{fi.Name}",
                            text = aci.name,
                            position = $"{l * 100}%+{lpx}px,{t * 100}%+{tpx}px",
                            size = GetSizeString(width, height),
                            keyboard = aci.keyboard,
                            joystick = aci.joystick,
                            OnValue = (pos, manipulating) => { fi.SetValue(manualObj, new Stick() { x = pos.X, y = pos.Y }); }
                        });
                        Console.WriteLine($"{aci.name}\t{fi.FieldType.Name}\t{l * 100}%+{lpx}px,{t * 100}%+{tpx}px");
                    }

                    if (fi.FieldType == typeof(Knob))
                    {
                        // not implemented.
                    }
                }

                manipulation.feedback = (values, _) =>
                {

                };
                manipulation.Start();
            }

            return pb =>
            {
                if (!_manualRemoteControlOn && pb.Button("Show Control Widgets"))
                {
                    remote();
                }

                if (_manualRemoteControlOn && pb.Button("Hide controls widgets"))
                {
                    _manualRemoteControlOn = false;
                    manipulation.End();
                }

                pb.Separator();
                {
                    pb.CollapsingHeaderStart("Native renderer configuration");
                    (string fields, string desc)[] fields = [("swapinterval", "glfwSwapInterval")];
                    pb.Table("configurations", ["field","description"], fields.Length, (row, i) =>
                    {
                        row.Label(fields[i].fields);
                        row.Label(fields[i].desc);
                    });
                    pb.CollapsingHeaderEnd();
                }

                if (pb.Button("Show 2dlm map")){
                    if (pb.OpenFile("Load 2dlm", "*.2dlm", out var fn))
                    {
                        //... todo.
                    }
                }

                if (pb.Button("Graphics"))
                {
                    var bmp = new CycleGUI.SoftwareBitmap(320, 200, CycleGUI.Colors.Black);
                    bmp.DrawLine(10, 10, 200, 100, CycleGUI.Colors.Red);
                    bmp.DrawRect(20, 20, 80, 60, CycleGUI.Colors.Green, filled: false);
                    bmp.DrawCircle(160, 100, 30, CycleGUI.Colors.Blue, filled: true);
                    bmp.DrawPolygon(new[] { (50, 150), (100, 180), (80, 195), (40, 190) }, CycleGUI.Colors.Yellow, filled: true);
                    bmp.DrawText(8, 8, "Hello, CycleGUI!", CycleGUI.Colors.White);
                    bmp.Save("out.jpg");
                    UITools.Alert("saved to out.jpg");
                }

                if (pb.Closing())
                    pb.Panel.Exit();
            };
        }


        public struct float2
        {
            public float x, y;
        }
        public class Frame
        {
            public int id;
            public float x, y, th;
            public long tick; // from sensor or counter.
            public long time; // arrive time or lastFrameTime+(tick-OldTick)/tickInterval*interval

        }
        public class Keyframe : Frame
        {
            public bool labeledXY, labeledTh;
            public float lx, ly, lth;

            public int deletionType = 0;
            public int l_step = 9999; // steps to labeled keyframe.
            public int type = 0; // 0: ok to refine 1:not to refine.
            public HashSet<int> connected = new HashSet<int>();
            public bool referenced = false;
        }
        public class LidarKeyframe : Keyframe
        {
            public float2[] pc; // virt cords.
            public float2[] keypoints; // virt cords.
            public float2[] reflexes;
            public float2 gcenter;



            public byte[] getBytes()
            {
                var ret = new byte[100000];
                var len = 0;
                using (Stream stream = new MemoryStream(ret))
                using (BinaryWriter bw = new BinaryWriter(stream))
                {
                    bw.Write(id);
                    bw.Write(x);
                    bw.Write(y);
                    bw.Write(th);
                    bw.Write(l_step);
                    bw.Write(labeledTh);
                    bw.Write(labeledXY);
                    bw.Write(lx);
                    bw.Write(ly);
                    bw.Write(lth);
                    bw.Write(pc.Length);
                    for (int i = 0; i < pc.Length; ++i)
                    {
                        bw.Write(pc[i].x);
                        bw.Write(pc[i].y);
                    }

                    bw.Write(keypoints.Length);
                    for (int i = 0; i < keypoints.Length; ++i)
                    {
                        bw.Write(keypoints[i].x);
                        bw.Write(keypoints[i].y);
                    }


                    len = (int)stream.Position;
                }

                return ret.Take(len).ToArray();
            }

            public static LidarKeyframe fromBytes(byte[] bytes)
            {
                using (Stream stream = new MemoryStream(bytes))
                using (BinaryReader br = new BinaryReader(stream))
                {
                    var ret = new LidarKeyframe();
                    ret.id = br.ReadInt32();
                    ret.x = br.ReadSingle();
                    ret.y = br.ReadSingle();
                    ret.th = br.ReadSingle();
                    ret.l_step = br.ReadInt32();
                    ret.labeledTh = br.ReadBoolean();
                    ret.labeledXY = br.ReadBoolean();
                    ret.lx = br.ReadSingle();
                    ret.ly = br.ReadSingle();
                    ret.lth = br.ReadSingle();
                    var len = br.ReadInt32();
                    ret.pc = new float2[len];
                    for (int i = 0; i < len; ++i)
                    {
                        ret.pc[i].x = br.ReadSingle();
                        ret.pc[i].y = br.ReadSingle();
                    }

                    len = br.ReadInt32();
                    ret.keypoints = new float2[len];
                    for (int i = 0; i < len; ++i)
                    {
                        ret.keypoints[i].x = br.ReadSingle();
                        ret.keypoints[i].y = br.ReadSingle();
                    }
                    return ret;
                }
            }
        }

        public class Map
        {

            public string filename;
            private ConcurrentDictionary<long, LidarKeyframe> frames = new ConcurrentDictionary<long, LidarKeyframe>();

            public void load(string fn)
            {
                frames.Clear();
                try
                {
                    using (Stream stream = new FileStream(fn, FileMode.Open))
                    using (BinaryReader br = new BinaryReader(stream))
                    {
                        var len = br.ReadInt32();
                        for (int i = 0; i < len; ++i)
                        {
                            var blen = br.ReadInt32();
                            var bytes = br.ReadBytes(blen);
                            var frame = LidarKeyframe.fromBytes(bytes);
                            frames[frame.id] = frame;
                        }
                    }

                }
                catch { }

                filename = fn;
            }
        }
    }
}
