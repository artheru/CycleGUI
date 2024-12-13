using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CycleGUI.PanelBuilder;

namespace CycleGUI.Terminals;

public class WebTerminal : Terminal
{
    private static string ComputeWebSocketAcceptKey(string secWebSocketKey)
    {
        const string guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        string concatenated = secWebSocketKey + guid;
        byte[] concatenatedBytes = Encoding.UTF8.GetBytes(concatenated);
        byte[] sha1HashBytes;

        using (SHA1 sha1 = SHA1.Create())
        {
            sha1HashBytes = sha1.ComputeHash(concatenatedBytes);
        }

        string base64String = Convert.ToBase64String(sha1HashBytes);
        return base64String;
    }

    private static bool used = false;
    public static void Use(int port = 8081, string name = null, string title = null, byte[] ico=null)
    {
        if (used)
        {
            Console.WriteLine("Already in use");
            return;
        }

        used = true;
        if (remoteWelcomePanel == null) throw new WelcomePanelNotSetException();
        Console.WriteLine($"Serve WebSocket Terminal at {port}");
        using var sr = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("CycleGUI.res.webVRender.html"));
        var html = sr.ReadToEnd();
        html = html.Replace("placeholder1", name ?? Assembly.GetCallingAssembly().GetName().Name);
        var bytes = Encoding.UTF8.GetBytes(html);
        LeastServer.AddGetHandler("/", () => new LeastServer.BCType()
        {
            bytes = bytes,
            contentType = "text/html"
        });
        LeastServer.AddServingResources("/", "res");
        LeastServer.AddGetHandler("/favicon.ico", ()=> new LeastServer.BCType()
        {
            bytes = ico,
            contentType = "image/x-icon"
        });
        LeastServer.AddGetHandler("/filelink", new{pid=0, fid=""}, Q =>
        {
            if (GUI.idPidNameMap.TryGetValue(Q.fid, out var pck))
            {
                if (pck.pid == Q.pid)
                {
                    // output bytes.
                    return new LeastServer.BCType
                    {
                        bytes = File.ReadAllBytes(pck.fn),
                        contentType = "application/octet-stream"
                    };
                }
            }

            throw new Exception("filelink not exist");
        });
        // to server webVRender.html, port in the html should be modified.
        LeastServer.AddSpecialTreat("/terminal/data", (headers, stream, socket) =>
        {
            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // Read the HTTP request headers
            string secWebSocketKey = null;

            // Parse the headers to find the Sec-WebSocket-Key
            var lines = headers.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string[] header = lines[i].Split(':');
                if (header[0].Trim() == "Sec-WebSocket-Key")
                {
                    secWebSocketKey = header[1].Trim();
                    break;
                }
            }

            // Generate the response headers
            string responseHeaders = "HTTP/1.1 101 Switching Protocols\r\n" +
                                     "Upgrade: websocket\r\n" +
                                     "Connection: Upgrade\r\n" +
                                     "Sec-WebSocket-Accept: " + ComputeWebSocketAcceptKey(secWebSocketKey) + "\r\n" +
                                     "\r\n";

            // Send the response headers
            writer.Write(responseHeaders);



            void SendData(NetworkStream stream, IEnumerable<byte> send)
            {
                var length = send.Count();
                var frame = new List<byte>();
                frame.Add(0x82); // this means the frame is binary and final

                if (length <= 125)
                {
                    frame.Add((byte)length);
                }
                else if (length >= 126 && length <= 65535)
                {
                    frame.Add(126);
                    frame.AddRange(BitConverter.GetBytes((ushort)length).Reverse());
                }
                else
                {
                    frame.Add(127);
                    frame.AddRange(BitConverter.GetBytes((ulong)length).Reverse());
                }

                frame.AddRange(send);
                stream.Write(frame.ToArray(), 0, frame.Count);

            }


            byte[] ReadData(NetworkStream stream)
            {
                int opcode; // The opcode in the current frame.
                long length; // The length of the payload data in the current frame.
                byte[] mask = new byte[4]; // The masking key in the current frame.

                List<byte[]> messages = []; // The list of messages.

                while (true)
                {
                    // Read the first 2 bytes of the frame header.
                    byte[] buffer = new byte[2]; // The buffer for reading a frame.

                    stream.Read(buffer, 0, 2);
                    opcode = buffer[0] & 0x0F;
                    length = buffer[1] & 0x7F;


                    if (length == 126)
                    {
                        // The length is a 16-bit unsigned integer in the next 2 bytes.
                        var buf = new byte[2];
                        stream.Read(buf, 0, 2);
                        length = BitConverter.ToUInt16(buf.Reverse().ToArray(), 0);
                    }
                    else if (length == 127)
                    {
                        // The length is a 64-bit unsigned integer in the next 8 bytes.
                        var buf = new byte[8];
                        stream.Read(buf, 0, 8);
                        length = (long)BitConverter.ToUInt64(buf.Reverse().ToArray(), 0);
                    }

                    // Read the masking key.
                    stream.Read(mask, 0, 4);

                    // Read the payload data.
                    var payload = new byte[length];
                    stream.Read(payload, 0, (int)length);

                    // Unmask the payload data.
                    for (int i = 0; i < length; i++)
                    {
                        payload[i] ^= mask[i % 4];
                    }

                    if (opcode == 2 || opcode == 0)
                    {
                        messages.Add(payload);
                    }
                    // Add the message to the list.

                    // If the final bit is set, this is the last frame of the message.
                    if ((buffer[0] & 0x80) != 0)
                    {
                        break;
                    }
                }

                // Concatenate all the messages.
                var total = messages.SelectMany(msg => msg).ToArray();
                return total;
            }

             
            var terminal = new WebTerminal();
            var sync = new object();
            try
            {
                terminal.remoteEndPoint = ((IPEndPoint)socket.RemoteEndPoint).ToString();
                terminal.SendDataDelegate = (bytes) => SendData(stream, bytes);

                Console.WriteLine($"WebTerminal serve {terminal.remoteEndPoint} as ID={terminal.ID}");

                var initPanel = new Panel(terminal);
                initPanel.Define(remoteWelcomePanel);

                bool allowWsAPI = true;
                Task.Run(() =>
                {
                    while (terminal.alive)
                    {
                        if (allowWsAPI)
                        {
                            var (changing, len) = Workspace.GetWorkspaceCommandForTerminal(terminal);
                            if (len == 4)
                            {
                                // only -1, means no workspace changing.
                                Thread.Sleep(0); // release thread resources.
                                continue;
                            }
                            // Console.WriteLine($"{DateTime.Now:ss.fff}> Send WS APIs to terminal {terminal.ID}, len={changing.Length}");

                            lock (terminal.syncSend)
                            {
                                terminal.SendDataDelegate([1, 0, 0, 0]);
                                terminal.SendDataDelegate(changing.Take(len));
                            }

                            // Console.WriteLine($"{DateTime.Now:ss.fff}> Sent");

                            allowWsAPI = false;
                        }else
                            lock (sync)
                                Monitor.Wait(sync, 1000);
                    }
                });

                while (terminal.alive)
                {
                    int type = BitConverter.ToInt32(ReadData(stream), 0);

                    //Console.WriteLine($"tcp server recv type {type} command");
                    if (type == 0) //type0=ui stack feedback.
                    {
                        GUI.ReceiveTerminalFeedback(ReadData(stream), terminal);
                    }
                    else if (type == 1)
                    {
                        Workspace.ReceiveTerminalFeedback(ReadData(stream), terminal);
                        // Console.WriteLine($"{DateTime.Now:ss.fff}>Feedback...");
                    }
                    else if (type == 2)
                    {
                        // ws api notice.
                        // Console.WriteLine($"{DateTime.Now:ss.fff}>allow next...");
                        allowWsAPI = true;
                        lock (sync)
                            Monitor.PulseAll(sync);
                    }else if (type == 3)
                    {
                        // real time UI operation.
                        Workspace.ReceiveTerminalFeedback(ReadData(stream), terminal);
                        // also feed back interval.
                        lock (terminal.syncSend)
                            terminal.SendDataDelegate([2, 0, 0, 0]);
                    }
                }
            }
            catch
            {
                terminal.Close();
            }
        });
        new Thread(()=>LeastServer.Listener(port)){Name = "CycleGUI-LEAST"}.Start();
    }

    private static Dictionary<string, WebTerminal> openedTerminals = new();

    private string remoteEndPoint = "/";
    public override string description => remoteEndPoint;

    private Action<IEnumerable<byte>> SendDataDelegate;

    object syncSend = new object();

    internal override void SwapBuffer(int[] mentionedPid)
    {
        // Console.WriteLine($"Swapbuffer for T{ID} for {string.Join(",",mentionedPid)}");
        try
        {
            lock (syncSend)
            {
                SendDataDelegate([0, 0, 0, 0]);
                SendDataDelegate(GenerateRemoteSwapCommands(mentionedPid).ToArray());
            }
        }
        catch (Exception)
        {

        }
    }
}