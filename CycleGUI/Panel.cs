using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CycleGUI;

public class Panel
{
    private static int IDSeq = 0;

    internal string name = $"Panel_{IDSeq}";
    private int _id = IDSeq++;

    internal int ID => _id;

    private bool showTitle = true;
    private bool movable = true;
    private enum Sizing
    {
        Default,
        None,
        AutoSizing
    };

    private Sizing sizing = Sizing.Default;
    private bool topMost = false, modal = false; //modal: popup around the mouse.
    private int panelTop, panelLeft, panelWidth=320, panelHeight=240;
    private float relPivotX, relPivotY, myPivotX, myPivotY;
    private Panel relPanel;
    private Docking mydocking = Docking.Left;
    private bool dockSplitting = false;
    
    internal bool enableMenuBar = false;

    public Panel TopMost(bool set)
    {
        topMost = set;
        return this;
    }

    public Panel AutoSize(bool set)
    {
        sizing = Sizing.AutoSizing;
        return this;
    }

    public Panel InitPos(bool pin, int left, int top, float myPivotX = 0f, float myPivotY = 0f, float screenPivotX=0, float screenPivotYs=0)
    {
        movable = !pin;
        panelTop = top;
        panelLeft = left;
        this.myPivotX= myPivotX;
        this.myPivotY= myPivotY;
        this.relPivotX = screenPivotX;
        this.relPivotY = screenPivotYs;
        return this;
    }

    public Panel InitPosRelative(Panel panel, int left, int top,
        float relPivotX = 0f, float relPivotY = 0f, float myPivotX = 0f, float myPivotY = 0f)
    {
        relPanel = panel;
        panelTop = top;
        panelLeft = left;
        this.relPivotX = relPivotX;
        this.relPivotY = relPivotY;
        this.myPivotX = myPivotX;
        this.myPivotY = myPivotY;
        return this;
    }

    public Panel FixSize(int w, int h)
    {
        sizing = Sizing.None;
        panelWidth=w; panelHeight=h;
        return this;
    }

    public Panel InitSize(int w=320, int h=240)
    {
        sizing = Sizing.Default;
        panelWidth = w; panelHeight = h;
        return this;
    }

    /// <summary>
    /// title==null to disable title
    /// </summary>
    /// <param name="title"></param>
    /// <returns></returns>
    public Panel ShowTitle(string title)
    {
        if (title == null)
            showTitle = false;
        else name = title;
        return this;
    }

    public Panel Modal(bool set)
    {
        modal = set;
        return this;
    }

    // private Panel EnableMenuBar(bool set)
    // {
    //     enableMenuBar = set;
    //     return this;
    // }

    public enum Docking
    {
        Left=0b110, Top=0b101, Right=0b100, Bottom=0b111, None=0b000, Full = 0b001
    }

    /// <summary>
    /// auxiliary: further split the panel in half and use the vice-panel.
    /// </summary>
    /// <param name="docking"></param>
    /// <param name="auxiliary"></param>
    /// <returns></returns>
    public Panel SetDefaultDocking(Docking docking, bool auxiliary=false)
    {
        dockSplitting = auxiliary;
        mydocking = docking;
        return this;
    }

    internal int flipper = 0;
    internal Span<byte> GetPanelProperties()
    {
        var cb = new CB(1024); // Assuming an initial expected size

        cb.Append(ID);
        cb.Append(name); // 

        // flags:
        int flag = (freeze ? 1 : 0) | (alive ? 2 : 0) | (showTitle ? 4 : 0) | (sizing == Sizing.Default ? 0 : 8) |
                   (sizing == Sizing.AutoSizing ? 16 : 0) | (movable ? 32 : 0) | (topMost ? 64 : 0) |
                   (modal ? 128 : 0) | (user_closable ? 256 : 0);
        flag |= ((int)mydocking << 9);
        flag |= dockSplitting ? (1 << 12) : 0;
        //flag |= flipper << 13; // already moved out of flag.
        flag |= enableMenuBar ? (1 << 14) : 0;
        // Console.WriteLine($"generate panel {name}:flipper{flipper}...");

        cb.Append(flag);

        // auxiliary from here. affect panel commands (GenerateStackFromPanelCommands).
        cb.Append(relPanel?.ID ?? -1);
        cb.Append(relPivotX);
        cb.Append(relPivotY);
        cb.Append(myPivotX);
        cb.Append(myPivotY);
        cb.Append(panelWidth);
        cb.Append(panelHeight);
        cb.Append(panelLeft);
        cb.Append(panelTop);
        cb.Append(flipper);

        if (exception == null)
        {
            cb.Append(0);
        }
        else
        {
            cb.Append(exception);
        }

        return cb.AsSpan();
    }

    internal Terminal terminal;
    public Terminal Terminal => terminal;

    internal Panel(Terminal terminal=null)
    {
        //if invoked by panel draw, use panel draw's terminal as default.
        this.terminal = terminal ?? GUI.lastUsedTerminal.Value;
        // OnTerminalQuit = () => SwitchTerminal(GUI.localTerminal);
        this.terminal.DeclarePanel(this);
    }

    public void Repaint(bool dropCurrent=false)
    {
        if (!terminal.alive || !alive) return;
        if (dropCurrent)
            redraw = true;
        else
            GUI.immediateRefreshingPanels[this] = 0; //or = other?
    }

    public void Define(PanelBuilder.CycleGUIHandler handler)
    {
        this.handler=handler;
        Draw();
        terminal.SwapBuffer([ID]);
    }

    private object testDraw = new object();
    bool drawing = false;
    private string exception = null;

    private int did = 0;
    private bool redraw = false;

    internal bool Draw(bool no_drop=false)
    {
        lock (testDraw)
        {
            if (drawing)
            {
                if (!Monitor.Wait(testDraw, 100))
                    return false; // currently busy drawing or blocked, skip this drawing sequence.
            }
            drawing = true;
        }

        try
        {
            lock (this)
            {
                exception = null;
                freeze = false;
                GUI.lastUsedTerminal.Value = Terminal;

                PanelBuilder pb;

                var drawnTime = 0;
                do
                {
                    redraw = false;
                    pb = GetBuilder();
                    handler?.Invoke(pb);
                    if (drawnTime++ > 10)
                        throw new Exception("Excessive redraw called!");
                } while (redraw);

                if (!no_drop)
                    ClearState();
                commands = pb.commands;
            }
        }
        catch (Exception e)
        {
            exception = e.MyFormat();
            // Console.WriteLine($"Draw {name} UI exception: {exception = e.MyFormat()}");
        }
        finally
        {
            flipper += 1;
            lock (testDraw)
            {
                drawing = false;
                Monitor.PulseAll(testDraw);
            }

            Touched = true;
            // Console.WriteLine($"Draw {ID} ({did}) done");
            did += 1;
        }



        return true;
    }

    public virtual void Exit()
    {
        alive = false;
        Console.WriteLine($"P{ID}(on T{terminal.ID}) decide to close");
        terminal.DestroyPanel(this);

        // notify thread that is waiting for result(GUI.WaitForPanel)
        lock (this)
            Monitor.PulseAll(this);
    }

    private static int btfId = 0;
    internal PanelBuilder.OneOffCommand pendingcmd = null;
    public void BringToFront()
    {
        btfId++;
        var mem = new Memory<byte>([16, 0, 0, 0, 0, 0, 0, 0]);
        MemoryMarshal.Write(mem.Span.Slice(4), ref btfId);
        pendingcmd = new PanelBuilder.OneOffCommand(mem);
        Console.WriteLine($"write of cmd {btfId} for {name}");
        terminal.SwapBuffer([ID]);
    }

    public void SwitchTerminal(Terminal newTerminal)
    {
        if (newTerminal == terminal) return; // if already in this terminal, skip.
        alive = false;
        Console.WriteLine($"Make P{ID} to exit T{terminal.ID} and enter {newTerminal.ID}");
        terminal.DestroyPanel(this);
        alive = true;
        terminal = newTerminal;
        terminal.DeclarePanel(this);
        terminal.SwapBuffer([ID]);
    }
    
    internal Action OnTerminalQuit;
    public void IfTerminalQuit(Action action)
    {
        OnTerminalQuit = action;
    }


    internal List<PanelBuilder.Command> commands = [];
    internal bool freeze = false, alive = true;
        
    private Dictionary<uint, object> ControlChangedStates = new();
    protected PanelBuilder.CycleGUIHandler handler;
    internal bool Touched;
    internal bool user_closable;

    internal bool PopState(uint id, out object state)
    {
        if (!ControlChangedStates.TryGetValue(id, out state)) return false;
        ControlChangedStates.Remove(id);
        return true;
    }

    private Dictionary<uint, object> PersistentState = new();
    internal bool TryStoreState(uint id, out object state)
    {
        if (!PopState(id, out state)) 
            return PersistentState.TryGetValue(id, out state);
        PersistentState[id] = state;
        return true;
    }

    internal virtual PanelBuilder GetBuilder() => new(this);

    internal int PushState(int id, object val)
    {
        if (id == -2)
        {
            // exception handler.
            var v = (int)val;
            if (v == 0)
            {
                //retry; ignore
            }else if (v == 1)
            {
                // close panel.
                Exit();
                return 1; //pid discarded.
            }
        }
        ControlChangedStates[(uint)id] = val;
        return 0;
    }

    internal void ClearState() => ControlChangedStates.Clear();

    public void Freeze()
    {
        freeze = true;
    }

    public void UnFreeze()
    {
        freeze = false;
    }
}