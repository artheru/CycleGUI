using FundamentalLib.VDraw;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static CycleGUI.PanelBuilder;
using static System.Net.Mime.MediaTypeNames;

namespace CycleGUI
{
    public static class VDraw
    {
        //
        // static VDraw()
        // {
        //     new Thread(() =>
        //     {
        //         while (true)
        //         {
        //             Thread.Sleep(70);
        //             // foreach (var rp in RegisteredPanels.Values)
        //             // {
        //             //     if (rp.PendingStates>0)
        //             //         rp.Draw();
        //             // }
        //         }
        //     }){Name = "VDraw_Keeper"}.Start();
        // }

        public static readonly LocalTerminal localTerminal = new();

        public delegate void SendCommandsDelegate(byte[] command);
        public static SendCommandsDelegate SendCommands;
        

        public static void ReceiveTerminalFeedback(byte[] feedBack, Terminal t)
        {
            // parse:
            using var ms = new MemoryStream(feedBack);
            using var br = new BinaryReader(ms);

            HashSet<int> mentionedPid = new();
            
            while (ms.Position < br.BaseStream.Length)
            {
                // state changed.
                var pid = br.ReadInt32();
                if (t is TCPTerminal) pid = -pid;
                var cid = br.ReadInt32();
                var typeId = br.ReadInt32();
                if (t.TryGetPanel(pid, out var panel))
                    mentionedPid.Add(pid);

                void Save(object val)
                {
                    panel?.PushState((uint)cid, val);
                    Console.WriteLine($"{t.description}:{(panel==null?$"*{pid}":panel.ID)}:{cid}:{val}");
                }

                switch (typeId)
                {
                    case 1:
                        int intValue = br.ReadInt32();
                        Save(intValue);
                        break;
                    case 2:
                        float floatValue = br.ReadSingle();
                        Save(floatValue);
                        break;
                    case 3:
                        double doubleValue = br.ReadDouble();
                        Save(doubleValue);
                        break;
                    case 4:
                        int byteLength = br.ReadInt32();
                        byte[] byteArray = br.ReadBytes(byteLength);
                        Save(byteArray);
                        break;
                    case 5:
                        byteLength = br.ReadInt32();
                        byteArray = br.ReadBytes(byteLength);
                        string stringValue = Encoding.UTF8.GetString(byteArray);
                        Save(stringValue);
                        break;
                    case 6:
                        bool boolValue = br.ReadBoolean();
                        Save(boolValue);
                        break;
                    default:
                        // Handle unknown type ID
                        break;
                }
            }
            
            foreach (var pid in mentionedPid)
                t.Draw(pid);
            Console.WriteLine("After draws, call swap.");
            t.SwapBuffer(mentionedPid.ToArray());
        }
        
        // add some constraint to prevent multiple prompting.
        public static Panel PromptPanel(CycleGUIHandler panel)
        {
            var p = new Panel();
            p.Define(panel);
            return p;
        }

        public static void PromptAndWaitPanel(CycleGUIHandler panel)
        {
            var p = new Panel();
            p.Define(panel);
            lock (p)
                Monitor.Wait(p);
        }

        public static T WaitPanelResult<T>(PanelBuilder<T>.CycleGUIHandler panel)
        {
            var p = new Panel<T>();
            p.Define2(panel);
            lock (p)
                Monitor.Wait(p);
            return p.retVal;
        }

        public static Panel DeclarePanel()
        {
            var p = new Panel();
            return p;
        }

        public static CycleGUIHandler DefaultRemotePanel;

        public static void ServeRemote(CycleGUIHandler panel, int port = 9336)
        {
            new Thread(() =>
            {
                // todo:
            }){Name = "RemoteCGUI"}.Start();
        }
    }

    public abstract class Terminal
    {
        public bool alive = true;

        private static int allIds = 0;

        private int _id = allIds++;
        public int ID => _id;

        public virtual string description => "abstract_terminal";

        public static Queue<byte[]> command = new();
        public abstract void SwapBuffer(int[] mentionedPid);

        public HashSet<int> previousPanels = new();
        public Dictionary<int, Panel> registeredPanels = new();

        public void DeclarePanel(Panel panel)
        {
            Console.WriteLine($"Declare panel on {description}");
            registeredPanels[panel.ID] = panel;
        }

        public void DestroyPanel(Panel panel)
        {
            registeredPanels.Remove(panel.ID);
            SwapBuffer(new[] { ID });
        }

        // todo:
        public byte[] GenerateDataBuffer() // for fast refreshing.
        {
            throw new NotImplementedException();
        }

        public class WelcomePanelNotSetException:Exception{}
        internal static CycleGUIHandler remoteWelcomePanel;

        public static void RegisterRemotePanel(CycleGUIHandler panel)
        {
            remoteWelcomePanel = panel;
        }

        public byte[] GenerateSwapCommands(int[] pids)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(pids.Length);
            foreach (var panel in pids.Select(p => registeredPanels[p]))
            {
                bw.Write(panel.ID);
                var name = Encoding.UTF8.GetBytes(panel.name);
                bw.Write(name.Length);
                bw.Write(name);

                // flags: 2: destroy.
                int flag = (panel.freeze ? 1 : 0) | (panel.alive ? 0 : 1);
                bw.Write(flag);
                bw.Write(panel.commands.Count());
                foreach (var panelCommand in panel.commands)
                {
                    if (panelCommand is ByteCommand bcmd)
                    {
                        bw.Write(0);
                        bw.Write(bcmd.bytes.Length);
                        bw.Write(bcmd.bytes);
                    }
                    else if (panelCommand is CacheCommand ccmd)
                    {
                        bw.Write(1);
                        bw.Write(ccmd.size);
                        bw.Write(ccmd.init.Length);
                        bw.Write(ccmd.init);
                    }
                }
            }

            return ms.ToArray();
        }

        public virtual bool TryGetPanel(int pid, out Panel o)
        {
            return registeredPanels.TryGetValue(pid, out o);
        }

        public virtual void Draw(int pid)
        {
            registeredPanels[pid].Draw();
        }
    }

    public class Panel<T> : Panel
    {
        public T retVal;
        public void Exit(T val)
        {
            retVal = val;
            Exit();
        }

        public override PanelBuilder GetBuilder() => new PanelBuilder<T>(this);

        public void Define2(PanelBuilder<T>.CycleGUIHandler panel)
        {
            this.handler = (pb) => panel(pb as PanelBuilder<T>);
            Draw();
            terminal.SwapBuffer(new[] { ID });
        }
    }

    public class PanelBuilder<T> : PanelBuilder
    {
        public delegate void CycleGUIHandler(PanelBuilder<T> pb);
        public void Exit(T val)
        {
            ((Panel<T>)panel).Exit(val);
        }

        public PanelBuilder(Panel panel) : base(panel)
        {
        }
    }

    public class Panel
    {
        private static int IDSeq = 0;

        public string name => $"Panel{ID}";

        public int _id = IDSeq++;
        public int ID => _id;

        public Terminal terminal;

        public Panel(Terminal terminal=null)
        {
            this.terminal = terminal ?? VDraw.localTerminal;
            IDSeq++;
            this.terminal.DeclarePanel(this);
        }

        public int PendingStates => ControlChangedStates.Count();

        public void Define(CycleGUIHandler handler)
        {
            this.handler=handler;
            Draw();
            terminal.SwapBuffer(new []{ID});
        }

        private object testDraw = new object();
        bool drawing = false;

        private int did = 0;
        public void Draw()
        {
            Console.WriteLine($"Draw {ID} ({did})");
            lock (testDraw)
            {
                if (drawing)
                    return; // skip current drawing sequence.
                drawing = true;
            }

            lock (this)
            {
                freeze = false;
                var pb = GetBuilder();
                handler?.Invoke(pb);
                ClearState();
                commands = pb.commands;
            }

            lock (testDraw)
                drawing = false;

            Touched = true;
            Console.WriteLine($"Draw {ID} ({did}) done");
            did += 1;
        }

        public void Exit()
        {
            alive = false;
            Console.WriteLine($"Exit {ID}");
            terminal.DestroyPanel(this);
            lock (this)
                Monitor.PulseAll(this);
        }


        public List<Command> commands = new();
        internal bool freeze = false, alive = true;
        
        private Dictionary<uint, object> ControlChangedStates = new();
        protected CycleGUIHandler handler;
        public bool Touched;

        public bool PopState(uint id, out object state)
        {
            if (!ControlChangedStates.TryGetValue(id, out state)) return false;
            ControlChangedStates.Remove(id);
            return true;

        }

        public virtual PanelBuilder GetBuilder() => new(this);

        public void PushState(uint id, object val)
        {
            ControlChangedStates[id] = val;
        }

        public void ClearState() => ControlChangedStates.Clear();

        public void Freeze()
        {
            freeze = true;
        }

        public void UnFreeze()
        {
            freeze = false;
        }
    }
    
    // one time use.
    public class PanelBuilder
    {
        public abstract class Command{}
        public class CacheCommand:Command
        {
            public int size = 256;
            public byte[] init;
        }
        public class ByteCommand:Command
        {
            public byte[] bytes;

            public ByteCommand(byte[] toArray)
            {
                bytes = toArray;
            }
        }

        public HashSet<uint> ids = new();
        public List<Command> commands = new();

        // use XXX##ID to choose id.
        protected Panel panel;

        // copied from imgui.
        public uint ImHashStr(string data, uint seed)
        {
            seed = ~seed;
            uint crc = seed;
            byte[] dataBytes = Encoding.ASCII.GetBytes(data);
            var dataSize = dataBytes.Length;

            uint[] crc32LookupTable =
            {
                0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA, 0x076DC419, 0x706AF48F, 0xE963A535, 0x9E6495A3,
                0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988, 0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91,
                0x1DB71064, 0x6AB020F2, 0xF3B97148, 0x84BE41DE, 0x1ADAD47D, 0x6DDDE4EB, 0xF4D4B551, 0x83D385C7,
                0x136C9856, 0x646BA8C0, 0xFD62F97A, 0x8A65C9EC, 0x14015C4F, 0x63066CD9, 0xFA0F3D63, 0x8D080DF5,
                0x3B6E20C8, 0x4C69105E, 0xD56041E4, 0xA2677172, 0x3C03E4D1, 0x4B04D447, 0xD20D85FD, 0xA50AB56B,
                0x35B5A8FA, 0x42B2986C, 0xDBBBC9D6, 0xACBCF940, 0x32D86CE3, 0x45DF5C75, 0xDCD60DCF, 0xABD13D59,
                0x26D930AC, 0x51DE003A, 0xC8D75180, 0xBFD06116, 0x21B4F4B5, 0x56B3C423, 0xCFBA9599, 0xB8BDA50F,
                0x2802B89E, 0x5F058808, 0xC60CD9B2, 0xB10BE924, 0x2F6F7C87, 0x58684C11, 0xC1611DAB, 0xB6662D3D,
                0x76DC4190, 0x01DB7106, 0x98D220BC, 0xEFD5102A, 0x71B18589, 0x06B6B51F, 0x9FBFE4A5, 0xE8B8D433,
                0x7807C9A2, 0x0F00F934, 0x9609A88E, 0xE10E9818, 0x7F6A0DBB, 0x086D3D2D, 0x91646C97, 0xE6635C01,
                0x6B6B51F4, 0x1C6C6162, 0x856530D8, 0xF262004E, 0x6C0695ED, 0x1B01A57B, 0x8208F4C1, 0xF50FC457,
                0x65B0D9C6, 0x12B7E950, 0x8BBEB8EA, 0xFCB9887C, 0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0xFBD44C65,
                0x4DB26158, 0x3AB551CE, 0xA3BC0074, 0xD4BB30E2, 0x4ADFA541, 0x3DD895D7, 0xA4D1C46D, 0xD3D6F4FB,
                0x4369E96A, 0x346ED9FC, 0xAD678846, 0xDA60B8D0, 0x44042D73, 0x33031DE5, 0xAA0A4C5F, 0xDD0D7CC9,
                0x5005713C, 0x270241AA, 0xBE0B1010, 0xC90C2086, 0x5768B525, 0x206F85B3, 0xB966D409, 0xCE61E49F,
                0x5EDEF90E, 0x29D9C998, 0xB0D09822, 0xC7D7A8B4, 0x59B33D17, 0x2EB40D81, 0xB7BD5C3B, 0xC0BA6CAD,
                0xEDB88320, 0x9ABFB3B6, 0x03B6E20C, 0x74B1D29A, 0xEAD54739, 0x9DD277AF, 0x04DB2615, 0x73DC1683,
                0xE3630B12, 0x94643B84, 0x0D6D6A3E, 0x7A6A5AA8, 0xE40ECF0B, 0x9309FF9D, 0x0A00AE27, 0x7D079EB1,
                0xF00F9344, 0x8708A3D2, 0x1E01F268, 0x6906C2FE, 0xF762575D, 0x806567CB, 0x196C3671, 0x6E6B06E7,
                0xFED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC, 0xF9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5,
                0xD6D6A3E8, 0xA1D1937E, 0x38D8C2C4, 0x4FDFF252, 0xD1BB67F1, 0xA6BC5767, 0x3FB506DD, 0x48B2364B,
                0xD80D2BDA, 0xAF0A1B4C, 0x36034AF6, 0x41047A60, 0xDF60EFC3, 0xA867DF55, 0x316E8EEF, 0x4669BE79,
                0xCB61B38C, 0xBC66831A, 0x256FD2A0, 0x5268E236, 0xCC0C7795, 0xBB0B4703, 0x220216B9, 0x5505262F,
                0xC5BA3BBE, 0xB2BD0B28, 0x2BB45A92, 0x5CB36A04, 0xC2D7FFA7, 0xB5D0CF31, 0x2CD99E8B, 0x5BDEAE1D,
                0x9B64C2B0, 0xEC63F226, 0x756AA39C, 0x026D930A, 0x9C0906A9, 0xEB0E363F, 0x72076785, 0x05005713,
                0x95BF4A82, 0xE2B87A14, 0x7BB12BAE, 0x0CB61B38, 0x92D28E9B, 0xE5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21,
                0x86D3D2D4, 0xF1D4E242, 0x68DDB3F8, 0x1FDA836E, 0x81BE16CD, 0xF6B9265B, 0x6FB077E1, 0x18B74777,
                0x88085AE6, 0xFF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF, 0xF862AE69, 0x616BFFD3, 0x166CCF45,
                0xA00AE278, 0xD70DD2EE, 0x4E048354, 0x3903B3C2, 0xA7672661, 0xD06016F7, 0x4969474D, 0x3E6E77DB,
                0xAED16A4A, 0xD9D65ADC, 0x40DF0B66, 0x37D83BF0, 0xA9BCAE53, 0xDEBB9EC5, 0x47B2CF7F, 0x30B5FFE9,
                0xBDBDF21C, 0xCABAC28A, 0x53B39330, 0x24B4A3A6, 0xBAD03605, 0xCDD70693, 0x54DE5729, 0x23D967BF,
                0xB3667A2E, 0xC4614AB8, 0x5D681B02, 0x2A6F2B94, 0xB40BBE37, 0xC30C8EA1, 0x5A05DF1B, 0x2D02EF8D,
            };

            if (dataSize != 0)
            {
                for (int i = 0; i < dataSize; i++)
                {
                    byte c = dataBytes[i];
                    if (c == '#' && dataSize >= 2 && dataBytes[i + 1] == '#' && dataBytes[i + 2] == '#')
                        crc = seed;
                    crc = (crc >> 8) ^ crc32LookupTable[(crc & 0xFF) ^ c];
                }
            }
            else
            {
                for (int i = 0; i < dataBytes.Length; i++)
                {
                    byte c = dataBytes[i];
                    if (c == '#' && dataBytes[i + 1] == '#' && dataBytes[i + 2] == '#')
                        crc = seed;
                    crc = (crc >> 8) ^ crc32LookupTable[(crc & 0xFF) ^ c];
                }
            }

            var z = ~crc;
            if (ids.Contains(z)) throw new DuplicateIDException();
            ids.Add(z);
            return z;
        }

        public PanelBuilder(Panel panel)
        {
            this.panel = panel;
        }

        public delegate void CycleGUIHandler(PanelBuilder pb);

        public void Text(string text)
        {
            var textBytes = Encoding.UTF8.GetBytes(text);
            commands.Add(new ByteCommand(new CB()
                .Append(1)
                .Append(textBytes.Length)
                .Append(textBytes).ToArray()));
        }
        public void SeperatorText(string text) { }
        public void Seperator(){}
        public void BulletText(string text) { }

        public void Indent()
        {

        }

        public void UnIndent()
        {
        }
        public void SameLine(){}

        public void QuestionMark(string text){} //sameline+question


        public void Image(byte[] img) { }

        public bool Button(string text, int style = 0)
        {
            uint myid = ImHashStr(text, 0);
            commands.Add(new ByteCommand(new CB().Append(2).Append(myid).Append(text).ToArray()));
            if (panel.PopState(myid, out _))
                return true;
            return false;
        }

        public void ToopTip(string text){}

        public string TextInput(string prompt, string defaultText = "", string hintText = "")
        {
            var myid = ImHashStr(prompt, 0);
            if (!panel.PopState(myid, out var ret))
                ret = defaultText;
            commands.Add(new ByteCommand(new CB().Append(4).Append(myid).Append(prompt).Append(hintText).ToArray()));
            commands.Add(new CacheCommand(){init=Encoding.UTF8.GetBytes((string)ret)});
            return (string)ret;
        }

        public int ListOptions(string prompt, string[] items, int height) => default;
        public int Combo(string[] options, int defaultChoice) => default;

        public void IntInput(string prompt, int min, int max, ref int val) { }
        public void IntDrag(string prompt, int min, int max, ref int val) { }
        public void IntSlider(string prompt, int min, int max, ref int val) { }

        public string FloatInput(string prompt, float min, float max, ref float val) => default;
        public string FloatDrag(string prompt, float min, float max, ref float val) => default;
        public string FloatSlider(string prompt, float min, float max, ref float val) => default;

        public void ColorPick(string prompt, ref Color defaultColor){}

        public class DuplicateIDException : Exception
        {

        }

        public bool CheckBox(string desc, ref bool chk)
        {
            uint myid = ImHashStr(desc,0);
            var ret = false;
            if (panel.PopState(myid, out var val))
            {
                chk = (bool)val;
                ret = true;
            }
            commands.Add(new ByteCommand(new CB().Append(3).Append(myid).Append(desc).Append(chk).ToArray()));
            return ret;
        }

        public void Toggle(string desc, ref bool on)
        {
        }

        public void Radio(string[] choices, ref int choice) { }
        
        public void Plot<T>(Func<T> value) where T: struct, IComparable, IFormattable, IConvertible, IComparable<T>, IEquatable<T> { }
        public void Plot<T>(Func<T> value, T min, T max) where T : struct, IComparable, IFormattable, IConvertible, IComparable<T>, IEquatable<T> { }

        public void Progress(float val, float max=1) { }

        public void PopupMenu(CycleGUIHandler cguih)
        {
            cguih(this);
        }

        public bool MenuItem(string desc) => default;
        public bool MenuCheck(string desc, bool on) => default;
        

        public class GUINotify
        {
            public bool notify = false;
            public void Set()
            {
                notify = true;
            }

            public void Clear()
            {
                notify = false;
            }
        }
        public bool Notified(GUINotify func)
        {
            // todo: tell UI to refresh more frequently or wait the set event to recover quickly.
            if (func.notify) return true;
            return false;
        }


        public class GUINotify<T>
        {
            private T _saved;
            public void Set(T value)
            {
                _saved = value;
            }

            public T Get() => _saved;
            public void Clear()
            {

            }
        }
        public bool Notified<T>(GUINotify<T> func, out T val)
        {
            val = func.Get();
            return false;
        }

        public void Freeze()
        {
            panel.Freeze();
        }

        public void UnFreeze()
        {
            panel.UnFreeze();
        }
        public void Exit()
        {
            panel.Exit();
        }
    }
}
