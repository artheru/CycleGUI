using CycleGUI.Terminals;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CycleGUI.PanelBuilder;
using static System.Net.Mime.MediaTypeNames;

namespace CycleGUI
{
    public static partial class GUI
    {
        internal static ConcurrentBag<Panel> immediateRefreshingPanels = [];

        // static ConcurrentQueue<(Action act, DateTime dt, string doing)> UITasks = new();
        internal static void RunUITask(Action act, string doing)
        {
            Task.Run(act);
            // UITasks.Enqueue((act, DateTime.Now, doing));
            // lock (ui_sync)
            //     Monitor.PulseAll(ui_sync);
        }

        private static object ui_sync = new();

        static GUI()
        {
            // Console.WriteLine("This program is powered by CycleGUI v0.1d @20241129");

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("CycleGUI.res.generated_version_local.h");
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            var str = UTF8Encoding.UTF8.GetString(buffer).Trim();
            var vid = str.Substring(str.Length - 4, 4);
            Console.WriteLine($"This program is powered by CycleGUI Version = {vid}");
            // new Thread(() =>
            // {
            //     while (true)
            //     {
            //         while (UITasks.TryDequeue(out var result))
            //             result.act();
            //         lock (ui_sync)
            //             Monitor.Wait(ui_sync);
            //     }
            // }){Name="GUI_TaskRunner"}.Start();

            new Thread(() =>
            {
                while (true)
                { 
                    var st = DateTime.Now;
                    Dictionary<Terminal, HashSet<Panel>> affected = new();
                    while (immediateRefreshingPanels.TryTake(out var panel))
                    {
                        if (!panel.terminal.alive) continue;
                        if (!panel.terminal.registeredPanels.ContainsKey(panel.ID)) continue;
                        if (affected.TryGetValue(panel.terminal, out var ls))
                            ls.Add(panel);
                        else 
                            affected.Add(panel.terminal, [panel]);
                    }


                    foreach (var rp in affected)
                    {
                        foreach (var panel in rp.Value)
                            panel.Draw();

                        rp.Key.SwapBuffer(rp.Value.Select(p=>p.ID).ToArray());
                    }

                    // var tmp = UITasks.ToArray();
                    // foreach (var va in tmp)
                    // {
                    //     if (va.Key.IsCompleted) UITasks.TryRemove(va.Key, out _);
                    //     else if (UITasks.TryGetValue(va.Key, out var info) && info.dt.AddSeconds(1) < DateTime.Now)
                    //         Console.WriteLine($"UITask {info.doing} timeout, task status is {va.Key.Status}. Check program status.");
                    // }

                    var tts = Math.Max(0, (int)(st.AddMilliseconds(33) - DateTime.Now).TotalMilliseconds);
                    Thread.Sleep(tts);
                }
            }){Name = "GUI_Keeper"}.Start();
        }

        public static readonly LocalTerminal localTerminal = new();

        internal static Terminal userSetDefaultTerminal = null;
        internal static ThreadLocal<Terminal> lastUsedTerminal = new(() => localTerminal);
        public static Terminal defaultTerminal
        {
            get
            {
                var st = userSetDefaultTerminal ?? lastUsedTerminal.Value;
                if (!st.alive)
                {
                    userSetDefaultTerminal = null;
                    st = lastUsedTerminal.Value = localTerminal;
                }

                return st;
            }
            set =>
                // if null, use threadlocal terminal
                userSetDefaultTerminal = value;
        }

        internal static void ReceiveTerminalFeedback(byte[] feedBack, Terminal t)
        {
            // parse:
            using var ms = new MemoryStream(feedBack);
            using var br = new BinaryReader(ms);

            HashSet<int> refreshingPids = [];

            while (ms.Position < br.BaseStream.Length)
            {
                // state changed.
                var pid = br.ReadInt32();
                if (t is TCPTerminal) pid = -pid;
                var cid = br.ReadInt32();
                var typeId = br.ReadInt32();
                if (t.TryGetPanel(pid, out var panel))
                    refreshingPids.Add(pid);

                void Save(object val)
                {
                    if (panel != null)
                    {
                        var state = panel.PushState(cid, val);
                        if (state == 1) refreshingPids.Remove(pid);
                    }

                    // var str = val is byte[] bs ? string.Join(" ", bs.Select(p => $"{p:X2}")) : val.ToString();
                    // Console.WriteLine($"{t.description}:{(panel==null?$"*{pid}":panel.ID)}:{cid}:{str}");
                }

                // we only need one feedback for each control type.
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

            GUI.RunUITask(() =>
            {
                bool needSwap = false;
                foreach (var pid in refreshingPids)
                    needSwap |= t.Draw(pid);
                if (needSwap)
                    t.SwapBuffer(refreshingPids.ToArray());
            }, "ui action feedback");
        }

        // add some constraint to prevent multiple prompting.
        public static Panel PromptPanel(CycleGUIHandler panel, Terminal terminal=null)
        {
            var p = new Panel(terminal);
            p.Define(panel);
            return p;
        }

        public static Viewport PromptWorkspaceViewport(Action<Panel> panelProperty=null, Terminal terminal = null)
        {
            var p = new Viewport(terminal??GUI.defaultTerminal);
            p.Define(p.GetViewportHandler(panelProperty));
            return p;
        }

        public static void PromptAndWaitPanel(CycleGUIHandler panel, Terminal terminal =null)
        {
            var p = new Panel(terminal);
            p.Define(panel);
            lock (p)
                Monitor.Wait(p);
            if (!p.terminal.alive) throw new Exception($"dead terminal T{terminal.ID}");
        }

        public static T WaitPanelResult<T>(PanelBuilder<T>.CycleGUIHandler panel,Terminal terminal=null)
        {
            var p = new Panel<T>(terminal);
            p.Define2(panel);
            lock (p)
                Monitor.Wait(p);
            if (!p.terminal.alive) throw new Exception($"dead terminal T{terminal.ID}");
            return p.retVal;
        }

        public static Panel DeclarePanel(Terminal terminal = null)
        {
            var p = new Panel(terminal);
            return p;
        }
    }

    public abstract partial class Terminal
    {
        public bool alive = true;

        private static int allIds = 0;

        private int _id = allIds++;
        public int ID => _id;

        public virtual string description => "abstract_terminal";

        public static Queue<byte[]> command = new();
        internal abstract void SwapBuffer(int[] mentionedPid);
        
        public ConcurrentDictionary<int, Panel> registeredPanels = new();

        public void DeclarePanel(Panel panel)
        {
            Console.WriteLine($"Declare P{panel.ID} on T{ID}({description})");
            registeredPanels[panel.ID] = panel;
        }

        public void DestroyPanel(Panel panel)
        {
            registeredPanels.TryRemove(panel.ID, out _);
            SwapBuffer([panel.ID]);
        }

        internal void Close()
        {
            alive = false;
            foreach (var panel in registeredPanels.Values)
            {
                if (panel.OnQuit != null)
                    panel.OnQuit(); // broken issue.
                else
                {
                    // all actions hook
                    Console.WriteLine($"P{panel.ID} broken due to dead terminal T{panel.terminal.ID}, exit.");
                    // if there is closing in Panel's handler, call it.
                    panel.Draw();
                    // notify thread that is waiting for result(GUI.WaitForPanel)
                    lock (panel)
                        Monitor.PulseAll(panel);
                }
            }

            if (alive) return;

            if (GUI.userSetDefaultTerminal == this)
                GUI.userSetDefaultTerminal = null;
        }

        public class WelcomePanelNotSetException:Exception{}

        public delegate CycleGUIHandler CycleGUIRemoteWelcomeHandler(Terminal pb);
        internal static CycleGUIRemoteWelcomeHandler remoteWelcomePanel;

        public static void RegisterRemotePanel(CycleGUIRemoteWelcomeHandler panel)
        {
            remoteWelcomePanel = panel;
        }

        public Memory<byte> GenerateRemoteSwapCommands(int[] pids)
        {
            // using var ms = new MemoryStream();
            // using var bw = new BinaryWriter(ms);
            // bw.Write(pids.Length);
            var cb = new CB(4096);
            cb.Append(pids.Length);
            foreach (var pid in pids)
            {
                if (!registeredPanels.TryGetValue(pid, out var panel))
                {
                    cb.Append(pid);
                    cb.Append($"DEL{pid}");
                    cb.Append(0);
                    for (int i = 0; i < 4 * 10; ++i) cb.Append((byte)0);
                    continue;
                }

                cb.Append(panel.GetPanelProperties());

                if (!panel.alive) continue;

                cb.Append(panel.commands.Count() + (panel.pendingcmd == null ? 0 : 1));
                foreach (var panelCommand in panel.commands)
                {
                    if (panelCommand is ByteCommand bcmd)
                    {
                        cb.Append(0);
                        cb.Append(bcmd.bytes.Length);
                        cb.Append(bcmd.bytes);
                    }
                }

                if (panel.pendingcmd != null)
                {
                    cb.Append(0);
                    cb.Append(panel.pendingcmd.bytes.Length);
                    cb.Append(panel.pendingcmd.bytes);
                    panel.pendingcmd = null;
                }
            }

            return cb.AsMemory();
        }

        public virtual bool TryGetPanel(int pid, out Panel o)
        {
            return registeredPanels.TryGetValue(pid, out o);
        }

        public virtual bool Draw(int pid)
        {
            if (registeredPanels.TryGetValue(pid, out var panel))
                return panel.Draw();
            return true;
        }

    }

    public class Panel<T> : Panel
    {
        public T retVal = default;

        public Panel(Terminal terminal1) : base(terminal1)
        {
        }

        public void Exit(T val)
        {
            retVal = val;
            Exit();
        }

        internal override PanelBuilder GetBuilder() => new PanelBuilder<T>(this);

        public void Define2(PanelBuilder<T>.CycleGUIHandler panel)
        {
            this.handler = (pb) => panel(pb as PanelBuilder<T>);
            Draw();
            terminal.SwapBuffer([ID]);
        }
    }

    public class PanelBuilder<T> : PanelBuilder
    {
        public delegate void CycleGUIHandler(PanelBuilder<T> pb);
        public void Exit(T val)
        {
            ((Panel<T>)_panel).Exit(val);
        }

        public PanelBuilder(Panel panel) : base(panel)
        {
        }
    }

    // one time use.
}
