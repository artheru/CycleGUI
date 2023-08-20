using FundamentalLib.VDraw;
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
using static CycleGUI.PanelBuilder;
using static System.Net.Mime.MediaTypeNames;

namespace CycleGUI
{
    public static class GUI
    {
        internal static ConcurrentBag<Panel> immediateRefreshingPanels = new();
        //
        static GUI()
        {
            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(33);
                    Dictionary<Terminal, List<Panel>> affected = new();
                    while (immediateRefreshingPanels.TryTake(out var panel))
                    {
                        if (!panel.terminal.registeredPanels.ContainsKey(panel.ID)) continue;
                        if (affected.TryGetValue(panel.terminal, out var ls))
                            ls.Add(panel);
                        else 
                            affected.Add(panel.terminal, new List<Panel>(){panel});
                    }


                    foreach (var rp in affected)
                    {
                        foreach (var panel in rp.Value)
                            panel.Draw();

                        rp.Key.SwapBuffer(rp.Value.Select(p=>p.ID).ToArray());
                    }
                }
            }){Name = "GUI_Keeper"}.Start();
        }

        public static readonly LocalTerminal localTerminal = new();

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
                    panel?.PushState((uint)cid, val);
                    var str = val is byte[] bs ? string.Join(" ", bs.Select(p => $"{p:X2}")) : val.ToString();
                    Console.WriteLine($"{t.description}:{(panel==null?$"*{pid}":panel.ID)}:{cid}:{str}");
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
            
            foreach (var pid in refreshingPids)
                t.Draw(pid);
            Console.WriteLine("After draws, call swap.");
            t.SwapBuffer(refreshingPids.ToArray());
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
            Console.WriteLine($"Declare panel on {description}");
            registeredPanels[panel.ID] = panel;
        }

        public void DestroyPanel(Panel panel)
        {
            registeredPanels.Remove(panel.ID);
            SwapBuffer(new[] { ID });
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
                bw.Write(panel.GetPanelProperties());

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
            ((Panel<T>)panel).Exit(val);
        }

        public PanelBuilder(Panel panel) : base(panel)
        {
        }
    }

    // one time use.
}
