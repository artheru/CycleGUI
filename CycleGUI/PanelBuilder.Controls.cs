using System;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CycleGUI.Terminals;
using NativeFileDialogSharp;

namespace CycleGUI;

public partial class PanelBuilder
{
    public void Label(string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        commands.Add(new ByteCommand(new CB()
            .Append(1)
            .Append(textBytes.Length)
            .Append(textBytes).ToArray()));
    }

    public void SeperatorText(string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        commands.Add(new ByteCommand(new CB()
            .Append(11)
            .Append(textBytes.Length)
            .Append(textBytes).ToArray()));
    }

    public void Seperator()
    {
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

    public void SameLine()
    {
    }

    public void QuestionMark(string text)
    {
    }

    public void Image(byte[] img)
    {
    }

    (CB cb, uint myid) start(string label, int typeid)
    {
        var myid = ImHashStr(label, 0);
        return (new CB().Append(typeid).Append(myid).Append(label), myid);
    }
    // shortcut: [G:][Ctrl+][Alt+][Shift+]XXX
    public bool Button(string text, int style = 0, string shortcut="", string hint="")
    {
        uint myid = ImHashStr(text, 0);
        commands.Add(new ByteCommand(new CB().Append(2).Append(myid).Append(text).Append(shortcut).Append(hint).ToArray()));
        if (_panel.PopState(myid, out _))
            return true;
        return false;
    }

    public void ToolTip(string text)
    {
    }

    public (string ret, bool doneInput) TextInput(string prompt, string defaultText = "", string hintText = "")
    {
        // todo: default to enter.
        var myid = ImHashStr(prompt, 0);
        if (!_panel.PopState(myid, out var ret))
            ret = defaultText;
        commands.Add(new ByteCommand(new CB().Append(4).Append(myid).Append(prompt).Append(hintText).Append(defaultText).ToArray()));
        //commands.Add(new CacheCommand() { init = Encoding.UTF8.GetBytes((string)ret) });
        return ((string)ret, _panel.PopState(myid + 1, out _));
    }

    public int Listbox(string prompt, string[] items, int height=5)
    {
        var (cb, myid) = start(prompt, 5);
        var selecting = -1;
        if (_panel.PopState(myid, out var ret))
            selecting = (int)ret;
        if (selecting >= items.Length)
            selecting = -1;
        cb.Append(height).Append(items.Length).Append(selecting);
        foreach (var item in items)
            cb.Append(item);
        commands.Add(new ByteCommand(cb.ToArray()));
        return selecting;
    }

    
    public bool ButtonGroups(string prompt, string[] buttonText, out int selecting, bool sameline=false)
    {
        int flag = (sameline ? 1 : 0);
        var (cb, myid) = start(prompt, 6);
        cb.Append(flag).Append(buttonText.Length);
        foreach (var item in buttonText)
            cb.Append(item);
        commands.Add(new ByteCommand(cb.ToArray()));

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

        public void SetColor(Color color)
        {
            //i += 1;
            cb.Append(6).Append(color.RGBA8());
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

    public unsafe void SearchableTable(string prompt, string[] header, int rows, Action<Row,int> content, int height=10)
    {
        var (cb, myid) = start(prompt, 7);
        var cptr = cb.Sz;
        cb.Append(0);
        cb.Append(header.Length);
        foreach (var h in header)
            cb.Append(h);
        cb.Append(rows);
        int action_row = -1, action_col = -1;
        object action_obj = null;
        _panel.PopState(myid, out var ret);
        if (ret != null)
        {
            var bytes = ret as byte[];
            action_row = BitConverter.ToInt32(bytes, 1);
            action_col = BitConverter.ToInt32(bytes, 5)+1;
            if (bytes[0] == 0) //int
                action_obj = BitConverter.ToInt32(bytes, 9);
            else if (bytes[0] == 1) //bool
                action_obj = bytes[9] == 1;
            else if (bytes[0] == 2) //float
                action_obj = BitConverter.ToSingle(bytes, 9);

        }

        for (int i = 0; i < rows; i++)
        {
            var row = new Row() { cb = cb, action_obj = action_obj, action_row =action_row==i, action_col=action_col };
            content(row, i);
            if (row.i != header.Length) 
                throw new Exception("Header count != row's col count!");
        }

        var cached = cb.ToArray();
        fixed (byte* ptr = cached)
        {
            *(int*)(ptr + cptr) = cached.Length - cptr - 8; // default cache command size. todo: remove all cachecommand?
        }
        commands.Add(new ByteCommand(cached));
        //commands.Add(new CacheCommand() { init = Encoding.UTF8.GetBytes("") });
    }

    public class DuplicateIDException : Exception
    {
    }

    public bool CheckBox(string desc, ref bool chk)
    {
        uint myid = ImHashStr(desc, 0);
        var ret = false;
        if (_panel.PopState(myid, out var val))
        {
            chk = (bool)val;
            ret = true;
        }

        commands.Add(new ByteCommand(new CB().Append(3).Append(myid).Append(desc).Append(chk).ToArray()));
        return ret;
    }

    public bool Closing()
    {
        _panel.user_closable = true;
        uint myid = ImHashStr($"##closing_{_panel.ID}", 0);
        commands.Add(new ByteCommand(new CB().Append(8).Append(myid).ToArray()));
        if (_panel.PopState(myid, out _))
            return true;
        return false;
    }

    public void Toggle(string desc, ref bool on)
    {
    }

    public void Radio(string[] choices, ref int choice)
    {
    }

    public void RealtimePlot(string prompt, float val)
    {
        commands.Add(new ByteCommand(new CB().Append(9).Append(prompt).Append(val).ToArray()));
    }

    public void Progress(float val, float max = 1)
    {
    }

    public void PopupMenu(CycleGUIHandler cguih)
    {
        cguih(this);
    }

    public bool MenuItem(string desc) => default;
    public bool MenuCheck(string desc, bool on) => default;

    public bool DragFloat(string prompt, ref float valf, float step, float min=Single.MinValue, float max=Single.MaxValue)
    {
        uint myid = ImHashStr(prompt, 0);
        var ret = false;
        if (_panel.PopState(myid, out var val))
        {
            valf = (float)val;
            ret = true;
        }
        
        commands.Add(new ByteCommand(new CB().Append(10).Append(myid).Append(prompt).Append(valf).Append(step).Append(min).Append(max).ToArray()));
        return ret;
    }

    public bool SelectFolder(string prompt, out string dir)
    {
        if (Panel.terminal is LocalTerminal)
        {
            // todo: use imgui file browser
            // invoke platform specific panels.
            DialogResult result = null;
            LocalTerminal.InvokeOnMainThread(() =>
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
            return UITools.Input(prompt, Directory.GetCurrentDirectory(), out dir, "remote path", "Input Folder Path");
        }
    }

}