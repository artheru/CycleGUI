using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CycleGUI
{
    public class Viewport :Panel
    {
        private ViewportSubTerminal vterminal;
        public override void Exit()
        {
            // destroy terminal.
            vterminal.Close();
            base.Exit();
        }

        private Func<bool> allowUserExit = null;

        public void UseOffscreenRender(bool offscreen)
        {
            hiddenSubViewport = offscreen;
        }

        private object sync = new();
        // closeEvent: return true to allow closing.
        private int rcycle = 0, scycle = -1;
        private bool pb_started = false;
        public Viewport(Terminal terminal1, Func<bool> closeEvent=null) : base(terminal1)
        {
            this.allowUserExit = closeEvent;
            vterminal = new ViewportSubTerminal();
            new Thread(() =>
            {
                while (alive)
                {
                    lock (sync)
                    {
                        if (ws_send_bytes != null) 
                            goto end;
                        var (changing, len) = Workspace.GetWorkspaceCommandForTerminal(vterminal);
                        if (len == 4)
                        {
                            // only -1, means no workspace changing.
                            goto end;
                        }

                        // Console.WriteLine($"prepare {len}B for T{terminal.ID}.vp{ID} @ {rcycle}");
                        rcycle++;

                        // Console.WriteLine($"{DateTime.Now:ss.fff}> Send WS APIs to terminal {terminal.ID}, len={len}");
                        ws_send_bytes = changing.Take(len).ToArray();
                        if (pb_started)
                            Repaint();
                        Monitor.Wait(sync);
                    }
                    end:
                    Thread.Sleep(16); // release thread resources.
                }
                Console.WriteLine($"Viewport {name} on {terminal.GetType().Name}({terminal.ID}) exit.");
            }) { Name = $"T{terminal1.ID}vp_daemon" }.Start();
        }

        internal byte[] ws_send_bytes = null;

        public PanelBuilder.CycleGUIHandler GetViewportHandler(Action<Panel> panelProperty)
        {
            return pb =>
            {
                lock (sync)
                {
                    pb_started = true;
                    panelProperty?.Invoke(pb.Panel);
                    if (allowUserExit != null)
                        if (pb.Closing())
                        {
                            if (allowUserExit())
                            {
                                Exit();
                                return; 
                            }
                        }

                    ;
                    if (PopState(999, out var recvWS))
                    {
                        // Console.WriteLine($"recv T{terminal.ID}.vp{ID} feedback");
                        Workspace.ReceiveTerminalFeedback((byte[])recvWS, vterminal);
                    }

                    if (PopState(1000, out var obj) && obj is int tscycle) 
                    {
                        if (scycle != tscycle) 
                            throw new Exception("WTFs?");
                        // Console.WriteLine($"T{terminal.ID}.vp{ID} finish processing ws api #{tscycle}. clear cache.");
                        ws_send_bytes = null;
                        Monitor.PulseAll(sync);
                    }

                    if (ws_send_bytes != null)
                    {
                        if (scycle == rcycle - 1)
                        {
                            // spurious repaint. let's ignore.
                            pb.commands.Add(
                                new PanelBuilder.ByteCommand(new CB().Append(23).Append(scycle).Append(0).AsMemory()));
                            Console.WriteLine($"Spurious repaint @{scycle}");
                            return;
                        }

                        scycle++;
                        if (scycle != rcycle - 1)
                            throw new Exception("WTF?");
                        // Console.WriteLine($"Send {ws_send_bytes.Length} to T{terminal.ID}.vp{ID} #{scycle} ({pb.Panel.flipper})");
                        pb.commands.Add(new PanelBuilder.ByteCommand(new CB().Append(23).Append(scycle).Append(ws_send_bytes.Length)
                            .Append(ws_send_bytes).AsMemory()));
                    }
                    else
                    {
                        pb.commands.Add(new PanelBuilder.ByteCommand(new CB().Append(23).Append(scycle).Append(0).AsMemory()));
                        // Console.WriteLine($"Send nothing @ {scycle}");
                    }
                }
            };
        }
        public static implicit operator Terminal(Viewport viewport)
        {
            return viewport.vterminal;
        }

        public class ViewportSubTerminal : Terminal
        {
            public ViewportSubTerminal():base(false)
            {
                // ignore terminal initialization.
                lock (terminals)
                    terminals[this] = 0;
            }
            internal override void SwapBuffer(int[] mentionedPid)
            {
                // not allowed to swap.
                throw new Exception("not allowed");
            }
        }
    }
}
