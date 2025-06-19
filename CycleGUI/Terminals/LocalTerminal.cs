using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using CycleGUI.API;
using CycleGUI.PlatformSpecific.Windows;
using static CycleGUI.PanelBuilder;

namespace CycleGUI.Terminals;

public class LocalTerminal : Terminal
{
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern int MainLoop(bool hideAfterInit);

    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ShowMainWindow();
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern int HideMainWindow();
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetWndIcon(byte[] bytes, int length);

    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetAppIcon(byte[] rgba, int size);

    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void SetUIStack(byte* bytes, int length); // aggregate all panels.

    public unsafe delegate void NotifyStateChangedDelegate(byte* changedStates, int length);
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterStateChangedCallback(NotifyStateChangedDelegate callback);

    public unsafe delegate void NotifyWorkspaceDelegate(byte* changedStates, int length);
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterWorkspaceCallback(NotifyWorkspaceDelegate callback);

    public unsafe delegate void NotifyDisplayDelegate(string fid, int pid);
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterExternDisplayCB(NotifyDisplayDelegate callback);

    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr RegisterStreamingBuffer(string name, int length);

    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetRendererVersion();

    private static unsafe NotifyStateChangedDelegate DNotifyStateChanged = StateChanged;
    private static unsafe NotifyWorkspaceDelegate DNotifyWorkspace = WorkspaceCB;
    private static unsafe NotifyDisplayDelegate DNotifyDisplay = DisplayFileCB;

    private static unsafe void DisplayFileCB(string fid, int pid)
    {
        if (GUI.idPidNameMap.TryGetValue(fid, out var pck))
        {
            if (pck.pid == pid)
            {
                // show in explorer
                Process.Start("explorer.exe", $"/select,\"{pck.fn}\"");
            }
        }
    }

    public unsafe delegate void BeforeDrawDelegate();
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterBeforeDrawCallback(BeforeDrawDelegate callback);
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UploadWorkspace(IntPtr bytes);

    private static BeforeDrawDelegate DBeforeDraw = BeforeDraw;

    private static WindowsTray windowsTray;

    public static void Start(bool hideAfterInit = false)
    {
        if (!File.Exists("libVRender.dll") && !File.Exists("libVRender.so"))
            return;

        Headless = false;
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("CycleGUI.res.generated_version_local.h");
        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        var str = UTF8Encoding.UTF8.GetString(buffer).Trim();
        var vid = str.Substring(str.Length - 4, 4);
        Console.WriteLine($"CycleGUI C# Library Renderer Version = {vid}");
        if (GetRendererVersion() != Convert.ToInt32(vid, 16))
        {
            Console.WriteLine($"Renderer version mismatch, expected={vid}, libVRender report={GetRendererVersion():X2}, may not compatible.");
        }

        var t = new Thread(() =>
            {
                RegisterBeforeDrawCallback(DBeforeDraw);
                RegisterStateChangedCallback(DNotifyStateChanged);
                RegisterWorkspaceCallback(DNotifyWorkspace);
                RegisterExternDisplayCB(DNotifyDisplay);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    windowsTray = new();
                    windowsTray.OnDblClick += () => { ShowMainWindow(); };
                }

                MainLoop(hideAfterInit);
            })
            { Name = "LocalTerminal" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void InvokeOnMainThread(Action what)
    {
        mainThreadActions.Enqueue(what);
    }

    public static void SetIcon(byte[] iconBytes, string tip)
    {
        var (bytes, width, _) =Utilities.ConvertIcoBytesToRgba(iconBytes);
        SetAppIcon(bytes, width);
        mainThreadActions.Enqueue(() =>
        {
            SetWndIcon(iconBytes, iconBytes.Length);
            windowsTray.SetIcon(iconBytes, tip);
        });
    }
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetWndTitle(string title);

    public static void SetTitle(string title)
    {
        mainThreadActions.Enqueue(() =>
        {
            SetWndTitle(title);
        });
    }

    public static void Terminate()
    {
        windowsTray.Discard();
        Process.GetCurrentProcess().Kill();
    }

    public static void AddMenuItem(string name, Action action)
    {
        mainThreadActions.Enqueue(() => windowsTray.AddMenuItem(name, action));
    }

    public delegate void DImGUIHandler();

    public static DImGUIHandler ImGuiHandler;

    public static ConcurrentQueue<Action> invocations = new();
    public static void Invoke(Action handler)
    {
        lock (invocations)
            invocations.Enqueue(handler);
        lock (handler)
            Monitor.Wait(handler);
    }

    private static bool invalidate = false;

    private static MemoryHandle handle;
    private static ConcurrentQueue<Action> mainThreadActions = new();
    private static unsafe void BeforeDraw()
    {
        ImGuiHandler?.Invoke();
        while (invocations.TryDequeue(out var handler))
        {
            handler.Invoke();
            lock (handler)
                Monitor.PulseAll(handler);
        }

        while (mainThreadActions.TryDequeue(out var mta))
            mta.Invoke();

        if (invalidate)
        {
            invalidate = false;
            if (handle.Pointer!=(void*)0) handle.Dispose();
            var pending = GUI.localTerminal.GenerateUIStack();
            handle = pending.Pin();

            SetUIStack((byte*)handle.Pointer, pending.Length);
        }

        var (wsChanges, _) = Workspace.GetWorkspaceCommandForTerminal(GUI.localTerminal);
        fixed (byte* ws = wsChanges)
            UploadWorkspace((IntPtr)ws); //generate workspace stuff.

        if (remoteWSBytes != null)
        {
            fixed (byte* ws = remoteWSBytes)
                UploadWorkspace((IntPtr)ws); //generate workspace stuff.
            remoteWSBytes = null;
            Task.Run(apiNotice);
        }
    }

    private static byte[] remoteWSBytes;

    private static unsafe void WorkspaceCB(byte* changedstates, int length)
    {
        byte[] byteArray = new byte[length];
        Marshal.Copy((IntPtr)changedstates, byteArray, 0, length);
        GUI.RunUITask(() => Workspace.ReceiveTerminalFeedback(byteArray, GUI.localTerminal), "workspaceCB");
        if (remotedisplayer_writer != null)
            Task.Run(() =>
            {
                remotedisplayer_writer.Write(1); //WS change.
                remotedisplayer_writer.Write(byteArray.Length);
                remotedisplayer_writer.Write(byteArray);
            });
    }

    private static unsafe void StateChanged(byte* changedstates, int length)
    {
        byte[] byteArray = new byte[length];
        Marshal.Copy((IntPtr)changedstates, byteArray, 0, length);
        GUI.ReceiveTerminalFeedback(byteArray, GUI.localTerminal);
        if (remotedisplayer_writer != null)
            Task.Run(() =>
            {
                remotedisplayer_writer.Write(0); //UI change.
                remotedisplayer_writer.Write(byteArray.Length);
                remotedisplayer_writer.Write(byteArray);
            });
    }

    public static bool Headless = true;
    public static Terminal redirected;
    public override string description => redirected == null ? "local" : $"redirect:{redirected.description}";

    public Memory<byte> GenerateUIStack()
    {
        var cb = new CB();
        var count = registeredPanels.Count + remoteMap.Count;
        cb.Append(count);
        foreach (var panel in registeredPanels.Values)
        {
            cb.Append(panel.GetPanelProperties());
            foreach (var panelCommand in panel.commands)
            {
                if (panelCommand is ByteCommand bcmd)
                    cb.Append(bcmd.bytes);
            }

            if (panel.pendingcmd != null)
            {
                cb.Append(panel.pendingcmd.bytes);
                panel.pendingcmd = null;
            }

            cb.Append(0x04030201); // end of commands (01 02 03 04)
        }

        foreach (var pair in remoteMap)
        {
            cb.Append(-pair.Key); // remote re_map, so skip first 4 bytes.s
            cb.Append(pair.Value);
        }

        return cb.AsMemory();
    }

    internal override void SwapBuffer(int[] mentionedPid)
    {
        lock (this)
        {
            //Console.WriteLine("SWAP...");

            // simply refresh all ui stack.
            invalidate = true;
        }
    }


    public void RedirectToRemoteTerminal(TCPTerminal terminal)
    {
        redirected = terminal;
    }



    static Dictionary<int, byte[]> remoteMap = new Dictionary<int, byte[]>();
    static void GenerateStackFromPanelCommands(byte[] buffer)
    {
        using (MemoryStream memoryStream = new MemoryStream(buffer))
        using (BinaryReader reader = new BinaryReader(memoryStream))
        {
            int plen = reader.ReadInt32();

            for (int i = 0; i < plen; ++i)
            {
                int pid = reader.ReadInt32();

                // header:
                long stPosition = memoryStream.Position;
                int nameLen = reader.ReadInt32();
                byte[] name = reader.ReadBytes(nameLen);
                int flag = reader.ReadInt32();
                memoryStream.Seek(4 * 9, SeekOrigin.Current);
                if ((flag & 2) == 0)
                {
                    //dead
                    remoteMap.Remove(pid);
                }
                else
                {
                    List<byte> bytes = new List<byte>();
                    bytes.AddRange(buffer.Skip((int)stPosition).Take((int)(memoryStream.Position - stPosition)));

                    int commandLength = reader.ReadInt32();
                    for (int j = 0; j < commandLength; ++j)
                    {
                        int type = reader.ReadInt32();
                        if (type == 0) //type 0: byte command.
                        {
                            int len = reader.ReadInt32();
                            bytes.AddRange(reader.ReadBytes(len));
                        }
                        else if (type == 1) //type 1: cache.
                        {
                            int len = reader.ReadInt32();
                            int initLen = reader.ReadInt32();
                            var init = new byte[len];
                            reader.Read(init, 0, initLen);
                            bytes.AddRange(init);
                        }
                    }

                    bytes.AddRange(new byte[4] { 1, 2, 3, 4 });
                    remoteMap[pid] = bytes.ToArray();
                }
            }
        }
    }

    private static BinaryWriter remotedisplayer_writer;
    private static Action apiNotice;
    public static void DisplayRemote(string endpoint)
    {
        try
        {
            var (ip, portStr, _) = endpoint.Split(':');
            var client = new TcpClient(ip, int.Parse(portStr));
            using var stream = client.GetStream();
            remotedisplayer_writer = new BinaryWriter(stream);
            apiNotice = () =>
            {
                remotedisplayer_writer.Write(2); // ws api invoked
            };
            var reader = new BinaryReader(stream);

            while (true)
            {
                var type = reader.ReadInt32();
                // Console.WriteLine($"{ip}: command type={type}");
                if (type == 0) // UI stack command
                {
                    var len = reader.ReadInt32();
                    var bytes = reader.ReadBytes(len);
                    GenerateStackFromPanelCommands(bytes);
                    GUI.localTerminal.SwapBuffer(null);
                }
                else if (type == 1) //workspace command.
                {
                    var len = reader.ReadInt32();
                    var bytes = reader.ReadBytes(len);
                    if (remoteWSBytes == null)
                    {
                        remoteWSBytes = bytes;
                    }
                    else
                        throw new Exception("should throttle remote WS bytes on server side!");
                }
            }
        }
        catch (Exception ex)
        {
            // destroy console.
            remoteMap.Clear();
            GUI.localTerminal.SwapBuffer(null);
            Console.WriteLine("Display Remote failed");
            Console.WriteLine(ex.MyFormat());
        }
    }

}