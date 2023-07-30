using FundamentalLib.VDraw;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static CycleGUI.PanelBuilder;
using static System.Net.Mime.MediaTypeNames;

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
    private bool topMost = false, interacting = false; //interacting: popup around the mouse.
    private int panelTop, panelLeft, panelWidth=320, panelHeight=240;
    private float relPivotX, relPivotY, myPivotX, myPivotY;
    private Panel relPanel;
        
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

    public Panel Pin(bool set, int left, int top, float myPivotX = 0f, float myPivotY = 0f)
    {
        movable = !set;
        panelTop = top;
        panelLeft = left;
        this.myPivotX= myPivotX;
        this.myPivotY= myPivotY;
        return this;
    }

    public Panel PinRelative(Panel panel, int left, int top, 
        float relPivotX=0f, float relPivotY = 0f, float myPivotX = 0f, float myPivotY = 0f)
    {
        movable = false;
        relPanel = panel;
        panelTop = top;
        panelLeft = left;
        this.relPivotX= relPivotX;
        this.relPivotY= relPivotY;
        this.myPivotX= myPivotX;
        this.myPivotY= myPivotY;
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

    public Panel InitSize(int w, int h)
    {
        sizing = Sizing.Default;
        panelWidth = w; panelHeight = h;
        return this;
    }

    //title==null to disable title
    public Panel ShowTitle(string title)
    {
        if (title == null)
            showTitle = false;
        else name = title;
        return this;
    }

    public Panel Interacting(bool set)
    {
        interacting = set;
        return this;
    }

    public byte[] GetPanelProperties()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(ID);
        var nameb = Encoding.UTF8.GetBytes(name);
        bw.Write(nameb.Length);
        bw.Write(nameb);

        // flags:
        int flag = (freeze ? 1 : 0) | (alive ? 2 : 0) | (showTitle ? 4 : 0) | (sizing == Sizing.Default ? 0 : 8) |
                   (sizing == Sizing.AutoSizing ? 16 : 0) | (movable ? 32 : 0) | (topMost ? 64 : 0) |
                   (interacting ? 128 : 0) | (user_closable ? 256 : 0);
        bw.Write(flag);

        bw.Write(relPanel?.ID ?? -1);
        bw.Write(relPivotX);
        bw.Write(relPivotY);

        bw.Write(myPivotX);
        bw.Write(myPivotY);
        bw.Write(panelWidth);
        bw.Write(panelHeight);
        bw.Write(panelLeft);
        bw.Write(panelTop);

        return ms.ToArray();
    }

    internal Terminal terminal;

    internal Panel(Terminal terminal=null)
    {
        this.terminal = terminal ?? GUI.localTerminal;
        IDSeq++;
        this.terminal.DeclarePanel(this);
    }

    public void Define(PanelBuilder.CycleGUIHandler handler)
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
        //Console.WriteLine($"Draw {ID} ({did})");
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
        //Console.WriteLine($"Draw {ID} ({did}) done");
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


    public List<PanelBuilder.Command> commands = new();
    internal bool freeze = false, alive = true;
        
    private Dictionary<uint, object> ControlChangedStates = new();
    protected PanelBuilder.CycleGUIHandler handler;
    public bool Touched;
    public bool user_closable;

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