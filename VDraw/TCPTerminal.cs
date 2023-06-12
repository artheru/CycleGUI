using FundamentalLib.Utilities;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            ThreadPool.QueueUserWorkItem((_) => { HandleClient(client); });
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

            while (true)
            {
                var len=reader.ReadInt32();
                VDraw.ReceiveTerminalFeedback(reader.ReadBytes(len), terminal);
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
        Console.WriteLine("tcp terminal swap...");
        var swaps = GenerateSwapCommands(mentionedPid);
        Console.WriteLine("gen commands...");
        writer.Write(swaps.Length);
        writer.Write(swaps);
    }
}