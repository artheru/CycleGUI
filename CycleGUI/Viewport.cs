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
        private int rcycle = 0, scycle = 0;
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

                        // Console.WriteLine($"get {len} for vp {ID} @ {rcycle++}");
                        // Console.WriteLine($"{DateTime.Now:ss.fff}> Send WS APIs to terminal {terminal.ID}, len={len}");
                        ws_send_bytes = changing.Take(len).ToArray();
                        Repaint();
                        Monitor.Wait(sync);
                    }
                    end:
                    Thread.Sleep(16); // release thread resources.
                }
            }) { Name = $"T{terminal1.ID}vp_daemon" }.Start();
        }

        private byte[] ws_send_bytes;

        public PanelBuilder.CycleGUIHandler GetViewportHandler(Action<Panel> panelProperty)
        {
            return pb =>
            {
                lock (sync)
                {
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
                        Workspace.ReceiveTerminalFeedback((byte[])recvWS, vterminal);
                    }

                    if (PopState(1000, out _))
                    {
                        ws_send_bytes = null;
                    }

                    if (ws_send_bytes != null)
                    {
                        scycle++;
                        // Console.WriteLine($"Send {ws_send_bytes.Length} to vp @ {scycle} ({pb.Panel.flipper})");
                        pb.commands.Add(new PanelBuilder.ByteCommand(new CB().Append(23).Append(scycle).Append(ws_send_bytes.Length)
                            .Append(ws_send_bytes).AsMemory()));
                    }
                    else
                    {
                        pb.commands.Add(new PanelBuilder.ByteCommand(new CB().Append(23).Append(scycle).Append(0).AsMemory()));
                    }

                    Monitor.PulseAll(sync);
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
