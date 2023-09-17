using FundamentalLib.Utilities;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CycleGUI.API;
using Mono.CompilerServices.SymbolWriter;

namespace CycleGUI;

public class TCPTerminal : Terminal
{
    public static void Serve(int port=5432)
    {
        if (remoteWelcomePanel == null) throw new WelcomePanelNotSetException();
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        Console.WriteLine($"Start TCP Remote terminal on {port}");

        while (true)
        {
            var client = listener.AcceptTcpClient();
            Console.WriteLine($"{(IPEndPoint)client.Client.RemoteEndPoint} has connected");
            Task.Run(() => { HandleClient(client); });
        }
    }

    static void HandleClient(TcpClient client)
    {
        Console.WriteLine("Handle client...");
        using var stream = client.GetStream();
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        
        // try
        // {
            var terminal = new TCPTerminal();
            terminal.client = client;
            terminal.writer = writer;
            Console.WriteLine("creating panel...");
            var initPanel = new Panel(terminal);
            initPanel.Define(remoteWelcomePanel);
            Console.WriteLine("initialize workspace...");

            bool allowWsAPI = true;
            Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(100);
                    if (allowWsAPI)
                    {
                        var changing = Workspace.GetRemote(terminal);
                        lock (terminal)
                        {
                            writer.Write(1);
                            writer.Write(changing.Length);
                            writer.Write(changing);
                        }

                        allowWsAPI = false;
                    }
                }
            });

            while (true)
            {
                var type = reader.ReadInt32();
                Console.WriteLine($"tcp server recv type {type} command");
                if (type == 0) //type0=ui stack feedback.
                {
                    var len = reader.ReadInt32();
                    GUI.ReceiveTerminalFeedback(reader.ReadBytes(len), terminal);
                }
                else if (type == 1)
                {
                    var len = reader.ReadInt32();
                    Workspace.ReceiveTerminalFeedback(reader.ReadBytes(len), terminal);
                }
                else if (type == 2)
                {
                    // ws api notice.
                    allowWsAPI = true;
                }
            }
            // }
        // catch
        // {
        //
        // }
    }
    

    public TcpClient client;
    private BinaryWriter writer;

    public override string description => $"remote {((IPEndPoint)client.Client.RemoteEndPoint)}";

    public override void SwapBuffer(int[] mentionedPid)
    {
        lock (this)
        {
            Console.WriteLine("tcp terminal swap...");
            var swaps = GenerateRemoteSwapCommands(mentionedPid);
            Console.WriteLine("gen commands...");
            writer.Write(0); // ui stack type.
            writer.Write(swaps.Length);
            writer.Write(swaps);
        }
    }
}