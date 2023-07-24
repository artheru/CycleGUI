using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FundamentalLib.MiscHelpers;
using FundamentalLib.VDraw;
using static CycleGUI.PanelBuilder;

namespace CycleGUI;

public class LocalTerminal:Terminal
{
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern int MainLoop();

    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ShowMainWindow();
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetWndIcon(byte[] bytes, int length);

    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void SetUIStack(byte* bytes, int length); // aggregate all panels.

    public unsafe delegate void NotifyStateChangedDelegate(byte* changedStates, int length);
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterStateChangedCallback(NotifyStateChangedDelegate callback);

    public unsafe delegate void NotifyWorkspaceDelegate(byte* changedStates, int length);
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterWorkspaceCallback(NotifyWorkspaceDelegate callback);

    private static unsafe NotifyStateChangedDelegate DNotifyStateChanged = StateChanged;
    private static unsafe NotifyStateChangedDelegate DNotifyWorkspace = WorkspaceCB;


    public unsafe delegate void BeforeDrawDelegate();
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterBeforeDrawCallback(BeforeDrawDelegate callback);
    [DllImport("libVRender", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UploadWorkspace(IntPtr bytes);

    private static BeforeDrawDelegate DBeforeDraw = BeforeDraw;

    private static WindowsTray windowsTray;
    static unsafe LocalTerminal()
    {
        if (!File.Exists("libVRender.dll"))
        {
            Headless = true;
        }
        else
        {
            new Thread(() =>
            {
                RegisterBeforeDrawCallback(DBeforeDraw);
                RegisterStateChangedCallback(DNotifyStateChanged);

                windowsTray = new();
                windowsTray.OnDblClick += () =>
                {
                    ShowMainWindow();
                };
                MainLoop();
            }){Name="LocalTerminal"}.Start();
        }
    }
    public static void SetIcon(byte[] iconBytes, string tip)
    {
        mainThreadActions.Enqueue(() =>
        {
            SetWndIcon(iconBytes, iconBytes.Length);
            windowsTray.SetIcon(iconBytes, tip);
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

    private static bool invalidate = false;
        
    private static IntPtr bytePtr = IntPtr.Zero;
    private static byte[] pending;
    private static ConcurrentQueue<Action> mainThreadActions = new();
    private static unsafe void BeforeDraw()
    {
        ImGuiHandler?.Invoke();

        if (!invalidate) return;

        while(mainThreadActions.TryDequeue(out var mta))
            mta.Invoke();

        if (bytePtr != IntPtr.Zero)
            Marshal.FreeHGlobal(bytePtr);
            
        bytePtr = Marshal.AllocHGlobal(pending.Length);
        Marshal.Copy(pending, 0, bytePtr, pending.Length);

        SetUIStack((byte*)bytePtr, pending.Length);
        invalidate = false;

        var wsChanges = Workspace.GetChanging();
        fixed (byte* ws=wsChanges)
            UploadWorkspace((IntPtr)ws); //generate workspace stuff.
    }

    private static unsafe void WorkspaceCB(byte* changedstates, int length)
    {
        byte[] byteArray = new byte[length];
        Marshal.Copy((IntPtr)changedstates, byteArray, 0, length);
        Task.Run(() => GUI.ReceiveTerminalFeedback(byteArray, GUI.localTerminal));
        if (writer != null)
            Task.Run(() =>
            {

                writer.Write(byteArray.Length);
                writer.Write(byteArray);
            });
    }

    private static unsafe void StateChanged(byte* changedstates, int length)
    {
        byte[] byteArray = new byte[length];
        Marshal.Copy((IntPtr)changedstates, byteArray, 0, length);
        Task.Run(() => GUI.ReceiveTerminalFeedback(byteArray, GUI.localTerminal));
        if (writer != null)
            Task.Run(() =>
            {

                writer.Write(byteArray.Length);
                writer.Write(byteArray);
            });
    }

    public static bool Headless = false;
    public static Terminal redirected;
    public override string description => redirected==null?"local":$"redirect:{redirected.description}";

    public byte[] GenerateUIStack()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var count = registeredPanels.Count + remoteMap.Count;
        bw.Write(count);
        foreach (var panel in registeredPanels.Values)
        {
            bw.Write(panel.ID);
            var name = Encoding.UTF8.GetBytes(panel.name);
            bw.Write(name.Length);
            bw.Write(name);
            // flags:
            int flag = (panel.freeze ? 1 : 0);
            bw.Write(flag);
            foreach (var panelCommand in panel.commands)
            {
                if (panelCommand is ByteCommand bcmd)
                    bw.Write(bcmd.bytes);
                else if (panelCommand is CacheCommand ccmd)
                {
                    int len = ccmd.size;
                    if (ccmd.init != null)
                    {
                        bw.Write(ccmd.init);
                        len -= ccmd.init.Length;
                    }

                    bw.Write(new byte[len]);
                }
            }

            bw.Write(0); // end of commands
        }

        foreach (var pair in remoteMap)
        {
            bw.Write(-pair.Key); // remote re_map, so skip first 4 bytes.s
            bw.Write(pair.Value);
        }

        return ms.ToArray();
    }

    public override void SwapBuffer(int[] mentionedPid)
    {
        Console.WriteLine("SWAP...");

        // simply refresh all ui stack.
        pending = GenerateUIStack();

        invalidate = true;
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

                long stPosition = memoryStream.Position;
                if (remoteMap.ContainsKey(pid))
                    remoteMap[pid] = new byte[0];
                int nameLen = reader.ReadInt32();
                byte[] name = reader.ReadBytes(nameLen);
                int flag = reader.ReadInt32();

                if ((flag & 2) == 2) //shutdown.
                {
                    remoteMap.Remove(pid);
                }
                else
                {
                    List<byte> bytes = new List<byte>();
                    bytes.AddRange(buffer.Skip((int)stPosition).Take((int)(memoryStream.Position - stPosition)));
                    // initialized;

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
                            var init=new byte[len];
                            reader.Read(init, 0, initLen);
                            bytes.AddRange(init);
                        }
                    }
                    bytes.AddRange(new byte[4]);
                    remoteMap[pid] = bytes.ToArray();
                }
            }
        }
    }

    private static BinaryWriter writer;
    public static void DisplayRemote(string endpoint)
    {
        try
        {
            var (ip, portStr, _) = endpoint.Split(':');
            var client = new TcpClient(ip, int.Parse(portStr));
            using var stream = client.GetStream();
            writer = new BinaryWriter(stream);
            var reader = new BinaryReader(stream);
            while (true)
            {
                var len = reader.ReadInt32();
                var bytes = reader.ReadBytes(len);
                GenerateStackFromPanelCommands(bytes);
                GUI.localTerminal.SwapBuffer(null);
            }
        }
        catch
        {
            // destroy console.
            remoteMap.Clear();
            GUI.localTerminal.SwapBuffer(null);
        }
    }
}