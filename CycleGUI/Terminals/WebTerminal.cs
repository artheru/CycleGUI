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
    public static void Use(int port = 8081, string name = null, string defaultImGUILayoutIni = null, byte[] ico=null)
    {
        if (used)
        {
            Console.WriteLine("Already in use");
            return;
        }

        string getVID(string terminal){
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream($"CycleGUI.res.generated_version_{terminal}.h");
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            var str = UTF8Encoding.UTF8.GetString(buffer).Trim();
            return str.Substring(str.Length - 4, 4);
        }

        if (getVID("local") != getVID("web"))
        {
            Console.WriteLine($"Renderer version mismatch, libVRender ver={getVID("local")}, webVRender ver={getVID("web")}, may not compatible.");
        }

        used = true;
        if (remoteWelcomePanel == null) throw new WelcomePanelNotSetException();
        Console.WriteLine($"Serve WebSocket Terminal at {port}");
        using var sr = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("CycleGUI.res.webVRender.html"));
        var html = sr.ReadToEnd();
        html = html.Replace("placeholder1", name ?? Assembly.GetCallingAssembly().GetName().Name);
        var placeholder2Value = defaultImGUILayoutIni == null
            ? "placeholder2"
            : defaultImGUILayoutIni.Replace(@"\", @"\\").Replace("\"", "\\\"").Replace("\n", @"\n").Replace("\r", @"\r");
        html = html.Replace("placeholder2", placeholder2Value);
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
            StreamReader reader = new StreamReader(stream);
            StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };

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
            writer.Flush();



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
                begin:
                int opcode; // The opcode in the current frame.
                long length; // The length of the payload data in the current frame.
                byte[] mask = new byte[4]; // The masking key in the current frame.

                List<byte[]> messages = []; // The list of messages.

                while (true)
                {
                    void Read(byte[] buf, int n)
                    {
                        int alread_read = 0;
                        while (alread_read < n)
                            alread_read += stream.Read(buf, alread_read, n - alread_read);
                    }

                    // Read the first 2 bytes of the frame header.
                    byte[] buffer = new byte[2]; // The buffer for reading a frame.

                    // stream.Read(buffer, 0, 2);
                    Read(buffer, 2);
                    opcode = buffer[0] & 0x0F;
                    length = buffer[1] & 0x7F;


                    if (length == 126)
                    {
                        // The length is a 16-bit unsigned integer in the next 2 bytes.
                        var buf = new byte[2];
                        // stream.Read(buf, 0, 2);
                        Read(buf, 2);
                        length = BitConverter.ToUInt16(buf.Reverse().ToArray(), 0);
                    }
                    else if (length == 127)
                    {
                        // The length is a 64-bit unsigned integer in the next 8 bytes.
                        var buf = new byte[8];
                        // stream.Read(buf, 0, 8);
                        Read(buf, 8);
                        length = (long)BitConverter.ToUInt64(buf.Reverse().ToArray(), 0);
                    }

                    // Read the masking key.
                    // stream.Read(mask, 0, 4);
                    Read(mask, 4);

                    // Read the payload data.
                    var payload = new byte[length];
                    // stream.Read(payload, 0, (int)length);
                    Read(payload, (int)length);

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
                if (total.Length == 0) goto begin;
                return total;
            }

             
            var terminal = new WebTerminal();
            // var sync = new object();
            try
            {
                terminal.remoteEndPoint = ((IPEndPoint)socket.RemoteEndPoint).ToString();
                terminal.SendDataDelegate = (bytes) => SendData(stream, bytes);

                Console.WriteLine($"WebTerminal serve {terminal.remoteEndPoint} as ID={terminal.ID}");

                var initPanel = new Panel(terminal);
                initPanel.Define(remoteWelcomePanel(terminal));

                Task.Run(() =>
                {
                    while (terminal.alive)
                    {
                        var (changing, len) = Workspace.GetWorkspaceCommandForTerminal(terminal);
                        if (len == 4)
                        {
                            // only -1, means no workspace changing.
                            Thread.Sleep(10); // release thread resources.
                            continue;
                        }
                        // Console.WriteLine($"{DateTime.Now:ss.fff}> Send WS APIs to terminal {terminal.ID}, len={changing.Length}");

                        lock (terminal.syncSend)
                        {
                            terminal.SendDataDelegate([1, 0, 0, 0]);
                            terminal.SendDataDelegate(changing.Take(len));
                        }
                    }
                });

                while (terminal.alive)
                {
                    int type = BitConverter.ToInt32(ReadData(stream), 0);

                    //Console.WriteLine($"tcp server recv type {type} command");
                    if (type == 0) //type0=ui stack feedback.
                    {
                        var data = ReadData(stream);
                        GUI.ReceiveTerminalFeedback(data, terminal);
                    }
                    else if (type == 1)
                    {
                        var data = ReadData(stream);
                        GUI.RunUITask(() => Workspace.ReceiveTerminalFeedback(data, terminal), "webWorkspaceCB");
                        // Console.WriteLine($"{DateTime.Now:ss.fff}>Feedback...");
                    }
                    else if (type == 2)
                    {
                        // ws api notice.
                        lock (terminal.syncSend)
                            terminal.SendDataDelegate([2, 0, 0, 0]);
                    }
                    else if (type == 3)
                    {
                        // real time UI operation.
                        var data = ReadData(stream);
                        GUI.RunUITask(() => Workspace.ReceiveTerminalFeedback(data, terminal), "webRTwscb");
                        // also feed back interval.
                        lock (terminal.syncSend)
                            terminal.SendDataDelegate([2, 0, 0, 0]);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Terminal exception:{ex.MyFormat()}");
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