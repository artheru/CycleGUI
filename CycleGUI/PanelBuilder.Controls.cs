using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CycleGUI.Terminals;
using NativeFileDialogSharp;
using static System.Net.Mime.MediaTypeNames;

namespace CycleGUI;

public partial class PanelBuilder
{
    public void Label(string text)
    {
        commands.Add(new ByteCommand(new CB()
            .Append(1).Append(text).AsMemory()));
    }

    public void SeparatorText(string text)
    {
        commands.Add(new ByteCommand(new CB()
            .Append(11).Append(text).AsMemory()));
    }

    public void Separator()
    {
        commands.Add(new ByteCommand(new CB()
            .Append(18).AsMemory()));
    }

    public void BulletText(string text)
    {
    }

    public void Indent()
    {
    }

    public void UnIndent()
    {
    }

    public void SameLine(int spacing = 0)
    {
        commands.Add(new ByteCommand(new CB().Append(14).Append(spacing).AsMemory()));
    }

    public void QuestionMark(string text)
    {
    }

    public void Image(string prompt, string rgba)
    {
        commands.Add(new ByteCommand(new CB().Append(25).Append(prompt).Append(rgba).AsMemory()));
    }

    (CB cb, uint myid) start(string label, int typeid)
    {
        var myid = ImHashStr(label);
        return (new CB().Append(typeid).Append(myid).Append(label), myid);
    }
    // shortcut: [G:][Ctrl+][Alt+][Shift+]XXX
    public bool Button(string text, string shortcut="", string hint="")
    {
        uint myid = ImHashStr(text);
        commands.Add(new ByteCommand(new CB().Append(2).Append(myid).Append(text).Append(shortcut.ToLower()).Append(hint).AsMemory()));
        if (_panel.PopState(myid, out _))
            return true;
        return false;
    }

    private ByteCommand _collapsingHeaderCommand = null;
    private int _collapsingHeaderId = -1;

    public void CollapsingHeaderStart(string label)
    {
        if (_collapsingHeaderCommand != null) throw new Exception("Previous CollapsingHeader not ended!");
        uint myid = ImHashStr(label);
        _collapsingHeaderId = commands.Count;
        commands.Add(_collapsingHeaderCommand =
            new ByteCommand(new CB().Append(19).Append(myid).Append(label).Append(0).AsMemory()));
    }

    public void CollapsingHeaderEnd()
    {
        if (_collapsingHeaderCommand == null) throw new Exception("CollapsingHeader not started!");
        var bytesLen = _collapsingHeaderCommand.bytes.Length;
        var offset = commands.Where((_, i) => i > _collapsingHeaderId).Sum(c => ((ByteCommand)c).bytes.Length);
        MemoryMarshal.Write(_collapsingHeaderCommand.bytes.Slice(bytesLen - 4).Span, ref offset);
        _collapsingHeaderId = -1;
        _collapsingHeaderCommand = null;
    }

    public void DisplayFileLink(string localfilename, string displayName)
    {
        var fid = GUI.AddFileLink(Panel.ID, localfilename);
        var fname = Path.GetFileName(localfilename);
        commands.Add(new ByteCommand(new CB().Append(12).Append(displayName).Append(fid).Append(fname).AsMemory()));
    }

    public void ToolTip(string text)
    {
    }

    public (string ret, bool doneInput) TextInput(string prompt, string defaultText = "", string hintText = "", bool focusOnAppearing=false)
    {
        // todo: default to enter.
        var myid = ImHashStr(prompt);
        if (!_panel.PopState(myid, out var ret))
            ret = defaultText;
        commands.Add(new ByteCommand(new CB().Append(4).Append(myid).Append(prompt).Append(hintText).Append(defaultText).Append(focusOnAppearing).AsMemory()));
        return ((string)ret, _panel.PopState(myid + 1, out _));
    }

    //Selecting list box.
    public int ListBox(string prompt, string[] items, int height = 5, bool persistentSelecting=false)
    {
        var (cb, myid) = start(prompt, 5);
        var selecting = -1;
        if (persistentSelecting && _panel.TryStoreState(myid, out var ret) ||
            !persistentSelecting && _panel.PopState(myid, out ret))
            selecting = (int)ret;
        if (selecting >= items.Length)
            selecting = -1;
        cb.Append(height).Append(items.Length).Append(selecting);
        foreach (var item in items)
            cb.Append(item);
        commands.Add(new ByteCommand(cb.AsMemory()));
        return selecting;
    }

    public bool ButtonGroup(string prompt, string[] buttonText, out int selecting, bool sameLine = false)
    {
        int flag = (sameLine ? 1 : 0);
        var (cb, myid) = start(prompt, 6);
        cb.Append(flag).Append(buttonText.Length);
        foreach (var item in buttonText)
            cb.Append(item);
        commands.Add(new ByteCommand(cb.AsMemory()));

        selecting = -1;
        if (_panel.PopState(myid, out var ret))
            selecting = (int)ret;
        if (selecting >= buttonText.Length)
            selecting = -1;
        return selecting >= 0;
    }

    public class Row
    {
        internal CB cb;
        internal int action_col = -1;
        internal bool action_row=false;
        internal object action_obj = null;

        internal int i = 0;

        private bool action => i == action_col && action_row;

        public bool Label(string text, string hint="")
        {
            i += 1;
            if (string.IsNullOrWhiteSpace(hint))
                cb.Append(0).Append(text);
            else
                cb.Append(1).Append(text).Append(hint);
            return action;
        }

        public int ButtonGroup(string[] buttonText, string[] hints=null)
        {
            i += 1;
            cb.Append(hints == null ? 2 : 3).Append(buttonText.Length);
            for (int i = 0; i < buttonText.Length; i++)
            {
                cb.Append(buttonText[i]);
                if (hints != null) cb.Append(hints[i]);
            }

            return action ? (int)action_obj : -1;
        }

        public bool Checkbox(bool init, out bool afterval, string hint="")
        {
            i += 1;
            if (action) init = (bool)action_obj;
            afterval = init;
            if (string.IsNullOrWhiteSpace(hint))
                cb.Append(4).Append(init);
            else cb.Append(5).Append(init).Append(hint);
            return action;
        }

        // Set Color for this row.
        public void SetColor(Color color)
        {
            //i += 1;
            cb.Append(6).Append(color.RGBA8());
        }

        // pixh/w: width and height in pixel. <=0 means auto and preserve aspect ratio. both auto means use row height.
        public void Image(string rgba, int pixh=-1, int pixw=-1)
        {
            cb.Append(7).Append(rgba).Append(pixh).Append(pixw);
        }

        public float DragFloat(float value, float step)
        {
            i += 1;
            return 0;
        }

        public float InputFloat(float value, float step)
        {
            i += 1;
            return 0;
        }
    }

    // [Obsolete("Use Table() instead!")]
    // public unsafe void SearchableTable(string prompt, string[] header, int rows, Action<Row,int> content, int height=10)
    // {
    //     var (cb, myid) = start(prompt, 7);
    //     var cptr = cb.Sz;
    //     cb.Append(0);
    //     cb.Append(header.Length);
    //     foreach (var h in header)
    //         cb.Append(h);
    //     cb.Append(rows);
    //     int action_row = -1, action_col = -1;
    //     object action_obj = null;
    //     _panel.PopState(myid, out var ret);
    //     if (ret != null)
    //     {
    //         var bytes = ret as byte[];
    //         action_row = BitConverter.ToInt32(bytes, 1);
    //         action_col = BitConverter.ToInt32(bytes, 5)+1;
    //         if (bytes[0] == 0) //int
    //             action_obj = BitConverter.ToInt32(bytes, 9);
    //         else if (bytes[0] == 1) //bool
    //             action_obj = bytes[9] == 1;
    //         else if (bytes[0] == 2) //float
    //             action_obj = BitConverter.ToSingle(bytes, 9);
    //
    //     }
    //
    //     for (int i = 0; i < rows; i++)
    //     {
    //         var row = new Row() { cb = cb, action_obj = action_obj, action_row =action_row==i, action_col=action_col };
    //         content(row, i);
    //         if (row.i != header.Length) 
    //             throw new Exception("Header count != row's col count!");
    //     }
    //
    //     var cached = cb.AsSpan();
    //     fixed (byte* ptr = cached)
    //     {
    //         *(int*)(ptr + cptr) = cached.Length - cptr - 8; // default cache command size. todo: remove all cachecommand?
    //     }
    //     commands.Add(new ByteCommand(cb.AsMemory()));
    // }


    // height: <=0 means display all.
    public unsafe void Table(string strId, string[] header, int rows, Action<Row, int> content, int height = 0,
        bool enableSearch = false, string title = "", bool freezeFirstCol=false)
    {
        var (cb, myid) = start(strId, 20);
        var cptr = cb.Sz;
        cb.Append(0);
        cb.Append(title);
        cb.Append(rows);
        cb.Append(height);
        cb.Append(enableSearch);
        cb.Append(freezeFirstCol);
        cb.Append(header.Length); 

        var eptr = cb.Sz;
        // below could no show.
        foreach (var h in header)
            cb.Append(h);
        int action_row = -1, action_col = -1;
        object action_obj = null;
        _panel.PopState(myid, out var ret);
        if (ret != null)
        {
            var bytes = ret as byte[];
            action_row = BitConverter.ToInt32(bytes, 1);
            action_col = BitConverter.ToInt32(bytes, 5) + 1;
            if (bytes[0] == 0) //int
                action_obj = BitConverter.ToInt32(bytes, 9);
            else if (bytes[0] == 1) //bool
                action_obj = bytes[9] == 1;
            else if (bytes[0] == 2) //float
                action_obj = BitConverter.ToSingle(bytes, 9);
        }

        for (int i = 0; i < rows; i++)
        {
            var row = new Row() { cb = cb, action_obj = action_obj, action_row = action_row == i, action_col = action_col };
            content(row, i);
            if (row.i != header.Length)
                throw new Exception("Header count != row's col count!");
        }

        var cached = cb.AsSpan();
        fixed (byte* ptr = cached)
        {
            *(int*)(ptr + cptr) = cached.Length - eptr; // default cache command size. todo: remove all cachecommand?
        }
        commands.Add(new ByteCommand(cb.AsMemory()));
    }

    public class DuplicateIDException : Exception
    {
    }

    public bool ColorEdit(string label, ref Color color, bool alphaEnabled = true)
    {
        uint myid = ImHashStr(label);
        var ret = false;
        
        if (_panel.PopState(myid, out var val))
        {
            color = Color.FromArgb((int)val);
            ret = true;
        }
        
        var flags = (alphaEnabled ? 0 : 1);
        commands.Add(new ByteCommand(new CB().Append(26).Append(myid).Append(label).Append(color.ToArgb()).Append(flags).AsMemory()));
        return ret;
    }

    public bool CheckBox(string desc, ref bool chk)
    {
        uint myid = ImHashStr(desc);
        var ret = false;
        if (_panel.PopState(myid, out var val))
        {
            chk = (bool)val;
            ret = true;
        }

        commands.Add(new ByteCommand(new CB().Append(3).Append(myid).Append(desc).Append(chk).AsMemory()));
        return ret;
    }

    // magic string: <I/i:ganyu> means use ganyu rgba as thumbnail. I:always display, i:tooltip
    public bool DropdownBox(string prompt, string[] items, ref int selected)
    {
        var (cb, myId) = start(prompt, 21);
        bool trigger = false;
        if (trigger = _panel.PopState(myId, out var selectedIndex))
        {
            selected = (int)selectedIndex;
        }

        cb.Append(selected).Append(items.Length);

        foreach (var item in items)
            cb.Append(item);

        commands.Add(new ByteCommand(cb.AsMemory()));

        return trigger;
    }

    public int TabButtons(string prompt, string[] items)
    {
        if (items.Length == 0) return -1;

        var (cb, myId) = start(prompt, 27);
        var selected = 0;
        if ( _panel.TryStoreState(myId, out var selectedIndex))
            selected = (int)selectedIndex;

        if (selected < 0) selected = 0;
        if (selected >= items.Length) selected = items.Length - 1;
        cb.Append(selected).Append(items.Length);

        foreach (var item in items)
            cb.Append(item);

        commands.Add(new ByteCommand(cb.AsMemory()));

        return selected;
    }

    public bool RadioButtons(string prompt, string[] items, ref int selected, bool sameLine = false)
    {
        var (cb, myId) = start(prompt, 22);
        bool trigger = false;
        if (trigger = _panel.PopState(myId, out var selectedIndex))
        {
            selected = (int)selectedIndex;
        }

        cb.Append(selected).Append(items.Length).Append(sameLine);

        foreach (var item in items)
            cb.Append(item);

        commands.Add(new ByteCommand(cb.AsMemory()));

        return trigger;
    }

    public bool Closing()
    {
        _panel.user_closable = true;
        uint myid = ImHashStr($"##closing_{_panel.ID}");
        commands.Add(new ByteCommand(new CB().Append(8).Append(myid).AsMemory()));
        if (_panel.PopState(myid, out _) || !_panel.terminal.alive)
            return true;
        return false;
    }

    public bool Toggle(string desc, ref bool on)
    {
        uint myid = ImHashStr(desc);
        var ret = false;
        if (_panel.PopState(myid, out var val))
        {
            on = (bool)val;
            ret = true;
        }

        commands.Add(new ByteCommand(new CB().Append(13).Append(myid).Append(desc).Append(on).AsMemory()));
        return ret;
    }

    /// <summary>
    /// don't forget to call repaint.
    /// </summary>
    /// <param name="prompt"></param>
    /// <param name="val"></param>
    /// <param name="freeze"></param>
    /// <param name="optionalButtons"></param>
    public int RealtimePlot(string prompt, float val, bool freeze = false, string[] optionalButtons = null)
    {
        uint myid = ImHashStr(prompt);

        var ret = -1;
        if (_panel.PopState(myid, out var btnId))
        {
            ret = (int)btnId;
        }

        var cb = new CB().Append(9).Append(myid).Append(prompt).Append(val).Append(freeze)
            .Append(optionalButtons?.Length ?? 0);
        if (optionalButtons != null)
            foreach (var btn in optionalButtons)
                cb.Append(btn);

        commands.Add(new ByteCommand(cb.AsMemory()));
        return ret;
    }

    public void ImPlotDemo()
    {
        commands.Add(new ByteCommand(new CB().Append(17).AsMemory()));
    }

    public void Progress(float val, float max = 1)
    {
    }

    public void PopupMenu(CycleGUIHandler cguih)
    {
        cguih(this);
    }

    // public bool MenuItem(string desc) => default;
    // public bool MenuCheck(string desc, bool on) => default;

    public bool DragFloat(string prompt, ref float valf, float step, float min=Single.MinValue, float max=Single.MaxValue)
    {
        uint myid = ImHashStr(prompt);
        var ret = false;
        if (_panel.PopState(myid, out var val))
        {
            valf = (float)val;
            ret = true;
        }
        
        commands.Add(new ByteCommand(new CB().Append(10).Append(myid).Append(prompt).Append(valf).Append(step).Append(min).Append(max).AsMemory()));
        return ret;
    }

    public bool OpenFile(string prompt, string filters, out string dir, string defaultFn="")
    {
        if (Panel.terminal is LocalTerminal)
        {
            // todo: use imgui file browser
            // invoke platform specific panels.
            DialogResult result = null;
            Task.Run(() =>
            {
                result = Dialog.FileOpen(filters, defaultFn);
                lock (prompt)
                    Monitor.PulseAll(prompt);
            });

            lock (prompt)
                Monitor.Wait(prompt);

            if (result.IsOk)
            {
                dir = result.Path;
                return true;
            }
            dir = null;
            return false;
        }
        else
        {
            return UITools.FileBrowser(prompt, out dir, selectDir: false, t: Panel.terminal, actionName:"Open", defaultFileName:defaultFn);
        }
    }


    public bool SaveFile(string prompt, string filter, out string dir, string defaultFn = "")
    {
        if (Panel.terminal is LocalTerminal)
        {
            // todo: use imgui file browser
            // invoke platform specific panels.
            DialogResult result = null;
            Task.Run(() =>
            {
                result = Dialog.FileSave(filter,defaultFn);
                lock (prompt)
                    Monitor.PulseAll(prompt);
            });

            lock (prompt)
                Monitor.Wait(prompt);

            if (result.IsOk) 
            {
                dir = result.Path;
                return true;
            }
            dir = null;
            return false;
        }
        else
        {
            return UITools.FileBrowser(prompt, out dir, selectDir: false, t: Panel.terminal, actionName:"Save", defaultFileName:defaultFn);
        }
    }

    public bool SelectFolder(string prompt, out string dir)
    {
        if (Panel.terminal is LocalTerminal)
        {
            // todo: use imgui file browser
            // invoke platform specific panels.
            DialogResult result = null;
            Task.Run(() =>
            {
                result = Dialog.FolderPicker(null); 
                lock(prompt)
                    Monitor.PulseAll(prompt);
            });

            lock (prompt)
                Monitor.Wait(prompt);

            if (result.IsOk)
            {
                dir = result.Path;
                return true;
            }
            dir = null;
            return false;
        }
        else
        {
            return UITools.FileBrowser(prompt, out dir, selectDir: true, t: Panel.terminal, actionName:"Select");
        }
    }

    public (bool ret, string sending) ChatBox(string prompt, string[] appending, int lines = 18, bool input = true)
    {
        uint myid = ImHashStr(prompt);
        bool ret = false;
        string sending = "";
        if (_panel.PopState(myid, out var val))
        {
            sending = (string)val;
            ret = true;
        }

        var cb = new CB().Append(15).Append(myid).Append(prompt).Append(lines).Append(input)
            .Append((int)(DateTime.Now.Ticks)).Append(appending?.Length ?? 0);
        if (appending != null)
            foreach (var s in appending)
                cb.Append(s);
        commands.Add(new ByteCommand(cb.AsMemory()));

        return (ret, sending);
    }

    public void TextBox(string prompt, string content, bool copyButton = true)
    {
        uint myid = ImHashStr(content);
        var cb = new CB().Append(28).Append(myid).Append(prompt).Append(content).Append(copyButton);
        commands.Add(new ByteCommand(cb.AsMemory()));
    }

    public void MenuBar(List<MenuItem> menu)
    {
        Panel.enableMenuBar = true;

        var cb = new CB();
        var myId = ImHashStr($"{Panel.name}-MenuBar");

        _panel.PopState(myId, out var ret);
        List<MenuItem> current = menu;
        if (ret != null)
        {
            var bytes = ret as byte[];
            var len = BitConverter.ToInt32(bytes, 0);
            for (var i = 0; i < len; ++i)
            {
                if (current == null) break;
                var idx = BitConverter.ToInt32(bytes, (i + 1) * 4);
                var item = current[idx];
                if ((item.SubItems == null || item.SubItems.Count == 0) && item.OnClick != null)
                {
                    item.OnClick();
                    Panel.Repaint(); // to update selected
                    break;
                }
                current = item.SubItems;
            }
        }

        MenuItem.GenerateCB(cb, menu, true, 24, myId);
        commands.Add(new ByteCommand(cb.AsMemory()));
    }

    public void OpenWebview(string name, string url, string hint = "")
    {
        commands.Add(new ByteCommand(new CB().Append(7).Append(name).Append(url).Append(hint).AsMemory()));
    }
}

public class MenuItem
{
    public static unsafe void GenerateCB(CB cb, List<MenuItem> menu, bool show, int uiFuncId, uint imGuiHash)
    {
        cb.Append(uiFuncId).Append(imGuiHash).Append(show);

        var wholeStart = cb.Len;
        cb.Append(0);

        void ProcessItem(MenuItem item)
        {
            // 0 is item, 1 is menu
            var type = item.SubItems == null || item.SubItems.Count == 0 ? 0 : 1;
            var attr = 0;

            if (type == 0)
            {
                if (item.OnClick != null) attr |= 1;
                if (item.Shortcut != null) attr |= 1 << 1;
                if (item.Selected) attr |= 1 << 2;
            }
            if (item.Enabled) attr |= 1 << 3;

            cb.Append(type).Append(attr).Append(item.Label);
            if (type == 0 && item.Shortcut != null) cb.Append(item.Shortcut);

            if (type == 1)
            {
                var place = cb.Len;
                cb.Append(0);

                cb.Append(item.SubItems.Count);
                foreach (var subItem in item.SubItems)
                {
                    ProcessItem(subItem);
                }

                var cached = cb.AsSpan();
                fixed (byte* ptr = cached)
                {
                    *(int*)(ptr + place) = cb.Len - place - 4;
                }
            }
        }

        if (menu == null) cb.Append(0);
        else
        {
            cb.Append(menu.Count);
            foreach (var item in menu) ProcessItem(item);
        }

        var cached = cb.AsSpan();
        fixed (byte* ptr = cached)
        {
            *(int*)(ptr + wholeStart) = cb.Len - wholeStart - 4;
        }
    }

    public MenuItem(string label, Action onClick = null, string shortcut = null, bool selected = false,
        bool enabled = true, List<MenuItem> subItems = null)
    {
        Label = label;
        OnClick = onClick;
        Shortcut = shortcut;
        Selected = selected;
        Enabled = enabled;
        SubItems = subItems;
    }

    public string Label;
    public Action OnClick = null;
    public string Shortcut = null;
    public bool Selected = false;
    public bool Enabled = true;

    public List<MenuItem> SubItems = null; // this determines BeginMenu or MenuItem
}