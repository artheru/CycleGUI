using CycleGUI.Terminals;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
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
        internal static ConcurrentBag<Panel> immediateRefreshingPanels = new();

        static GUI()
        {
            new Thread(() =>
            {
                while (true)
                {
                    var st = DateTime.Now;
                    Dictionary<Terminal, HashSet<Panel>> affected = new();
                    while (immediateRefreshingPanels.TryTake(out var panel))
                    {
                        if (!panel.terminal.registeredPanels.ContainsKey(panel.ID)) continue;
                        if (affected.TryGetValue(panel.terminal, out var ls))
                            ls.Add(panel);
                        else 
                            affected.Add(panel.terminal, new HashSet<Panel>(){panel});
                    }


                    foreach (var rp in affected)
                    {
                        foreach (var panel in rp.Value)
                            panel.Draw();

                        rp.Key.SwapBuffer(rp.Value.Select(p=>p.ID).ToArray());
                    }

                    var tts = Math.Max(0,(int)(st.AddMilliseconds(33) - DateTime.Now).TotalMilliseconds);
                    Thread.Sleep(tts);
                }
            }){Name = "GUI_Keeper"}.Start();
        }

        public static readonly LocalTerminal localTerminal = new();
        public static Terminal defaultTerminal = localTerminal;

        internal static void ReceiveTerminalFeedback(byte[] feedBack, Terminal t)
        {
            // parse:
            using var ms = new MemoryStream(feedBack);
            using var br = new BinaryReader(ms);

            HashSet<int> refreshingPids = new();

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

            Task.Run(() =>
            {
                bool needSwap = false;
                foreach (var pid in refreshingPids)
                    needSwap |= t.Draw(pid);
                if (needSwap)
                    t.SwapBuffer(refreshingPids.ToArray());
            });
        }

        // add some constraint to prevent multiple prompting.
        public static Panel PromptPanel(CycleGUIHandler panel, Terminal terminal=null)
        {
            var p = new Panel(terminal);
            p.Define(panel);
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
        public abstract void SwapBuffer(int[] mentionedPid);
        
        public Dictionary<int, Panel> registeredPanels = new();

        public void DeclarePanel(Panel panel)
        {
            Console.WriteLine($"Declare P{panel.ID} on T{ID}({description})");
            registeredPanels[panel.ID] = panel;
        }

        public void DestroyPanel(Panel panel)
        {
            registeredPanels.Remove(panel.ID);
            SwapBuffer(new[] { panel.ID });
        }
        
        protected void Close()
        {
            alive = false;
            foreach (var panel in registeredPanels.Values)
            {
                if (panel.OnQuit != null)
                    panel.OnQuit();
                if (!panel.terminal.alive)
                {
                    // all actions hook
                    Console.WriteLine($"P{ID} broken due to dead terminal T{panel.terminal.ID}");
                    // notify thread that is waiting for result(GUI.WaitForPanel)
                    lock (panel)
                        Monitor.PulseAll(panel);
                }
            }

            if (!alive)
                if (GUI.defaultTerminal == this)
                    GUI.defaultTerminal = GUI.localTerminal;
        }

        public class WelcomePanelNotSetException:Exception{}
        internal static CycleGUIHandler remoteWelcomePanel;

        public static void RegisterRemotePanel(CycleGUIHandler panel)
        {
            remoteWelcomePanel = panel;
        }

        public byte[] GenerateRemoteSwapCommands(int[] pids)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(pids.Length);
            foreach (var pid in pids)
            {
                if (!registeredPanels.TryGetValue(pid, out var panel))
                {
                    bw.Write(pid);
                    var nameb = Encoding.UTF8.GetBytes($"DEL{pid}");
                    bw.Write(nameb.Length);
                    bw.Write(nameb);
                    bw.Write(0);
                    bw.Write(Enumerable.Repeat((byte)0, 4 * 9).ToArray());
                    continue;
                }

                bw.Write(panel.GetPanelProperties());

                if (!panel.alive) continue;

                bw.Write(panel.commands.Count());
                foreach (var panelCommand in panel.commands)
                {
                    if (panelCommand is ByteCommand bcmd)
                    {
                        bw.Write(0);
                        bw.Write(bcmd.bytes.Length);
                        bw.Write(bcmd.bytes);
                    }
                    // else if (panelCommand is CacheCommand ccmd)
                    // {
                    //     bw.Write(1);
                    //     bw.Write(ccmd.size);
                    //     bw.Write(ccmd.init.Length);
                    //     bw.Write(ccmd.init);
                    // }
                }
            }

            return ms.ToArray();
        }

        public virtual bool TryGetPanel(int pid, out Panel o)
        {
            return registeredPanels.TryGetValue(pid, out o);
        }

        public virtual bool Draw(int pid)
        {
            return registeredPanels[pid].Draw();
        }

    }

    public class Panel<T> : Panel
    {
        public T retVal;

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
            terminal.SwapBuffer(new[] { ID });
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
