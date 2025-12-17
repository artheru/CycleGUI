using System;
using System.Collections;
using System.Collections.Concurrent;
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
using System.Web;

namespace CycleGUI.Terminals;

public class WebTerminal : Terminal
{
    // Reconnect behavior:
    // - Browser requests /terminal/session to obtain a reconnect id.
    // - WebSocket connects to /terminal/data?reconnect=<id>
    // - On unexpected disconnect, server keeps the Terminal alive for GracePeriodMs to allow resume.
    // - On explicit client close (page unload), server closes immediately.
    private const int GracePeriodMs = 10_000;

    private class Session
    {
        public string Id;
        public WebTerminal Terminal;
        public volatile bool Connected;
        public int Closing; // 1 = closing (terminal being closed)
        public int ConnectionGen;

        public object ConnectionLock = new object();
        public DateTime LastDisconnectUtc = DateTime.MinValue;

        public void ArmCloseIfStillDisconnected(DateTime disconnectStampUtc)
        {
            Task.Run(async () =>
            {
                await Task.Delay(GracePeriodMs).ConfigureAwait(false);
                if (Volatile.Read(ref Closing) == 1) return;
                if (Connected) return;
                if (LastDisconnectUtc != disconnectStampUtc) return;
                try
                {
                    Terminal?.Close();
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    Volatile.Write(ref Closing, 1);
                    sessions.TryRemove(Id, out _);
                }
                Console.WriteLine($"Close Web session sid={Id}");
            });
        }
    }

    private static readonly ConcurrentDictionary<string, Session> sessions = new();

    private static string NewSessionId()
    {
        // Compatible with older target frameworks (avoid Convert.ToHexString / RandomNumberGenerator.GetBytes(int)).
        var bytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static bool TryParseReconnectIdFromHeaders(string headers, out string reconnectId)
    {
        reconnectId = null;
        if (string.IsNullOrWhiteSpace(headers)) return false;
        var firstLine = headers.Split('\n').FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine)) return false;
        // Example: "GET /terminal/data?reconnect=abc HTTP/1.1"
        var parts = firstLine.Split(' ');
        if (parts.Length < 2) return false;
        var pathAndQuery = parts[1];
        var qIdx = pathAndQuery.IndexOf('?');
        if (qIdx < 0 || qIdx == pathAndQuery.Length - 1) return false;
        var query = pathAndQuery.Substring(qIdx + 1);
        try
        {
            var dict = HttpUtility.ParseQueryString(query);
            var val = dict.Get("reconnect");
            if (string.IsNullOrWhiteSpace(val)) return false;
            reconnectId = val.Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

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

        // WebTerminal reconnect session bootstrap.
        // Browser calls this to obtain a random session id before opening the websocket.
        LeastServer.AddGetHandler("/terminal/session", () =>
        {
            var id = NewSessionId();
            var t = new WebTerminal();
            var s = new Session { Id = id, Terminal = t, Connected = false, Closing = 0 };
            sessions[id] = s;
            Console.WriteLine($"WebTerminal session created sid={id}, T{t.ID}");
            return id;
        });

        // Browser calls this while reconnecting to see if the server still has the session.
        LeastServer.AddGetHandler("/terminal/check", new { reconnect = "" }, Q =>
        {
            if (string.IsNullOrWhiteSpace(Q.reconnect) || !sessions.ContainsKey(Q.reconnect))
                return "NO";
            return "OK";
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

            // Reconnect session id from query string
            string reconnectId = null;
            TryParseReconnectIdFromHeaders(headers, out reconnectId);
            Session session = null;
            if (!string.IsNullOrWhiteSpace(reconnectId))
                sessions.TryGetValue(reconnectId, out session);

            // If reconnect parameter was provided but the server doesn't have it, don't upgrade.
            if (!string.IsNullOrWhiteSpace(reconnectId) && session == null)
            {
                var resp = "HTTP/1.1 404 Not Found\r\n" +
                           "Content-Type: text/plain\r\n" +
                           "Connection: close\r\n" +
                           "\r\n" +
                           "Terminal on server has died\r\n";
                writer.Write(resp);
                writer.Flush();
                try { socket.Close(); } catch { }
                return;
            }

            // If no reconnect id was supplied, fall back to a non-resumable session.
            if (session == null)
            {
                reconnectId = NewSessionId();
                var t = new WebTerminal();
                session = new Session { Id = reconnectId, Terminal = t, Connected = false, Closing = 0 };
                sessions[reconnectId] = session;
                Console.WriteLine($"WebTerminal session auto-created sid={reconnectId}, T{t.ID}");
            }

            // Generate the response headers
            string responseHeaders = "HTTP/1.1 101 Switching Protocols\r\n" +
                                     "Upgrade: websocket\r\n" +
                                     "Connection: Upgrade\r\n" +
                                     "Sec-WebSocket-Accept: " + ComputeWebSocketAcceptKey(secWebSocketKey) + "\r\n" +
                                     "X-CycleGUI-Reconnect: " + reconnectId + "\r\n" +
                                     "\r\n";

            // Send the response headers
            writer.Write(responseHeaders);
            writer.Flush();



            byte[] BuildFrameHeader(int payloadLength)
            {
                if (payloadLength <= 125)
                    return new byte[] { 0x82, (byte)payloadLength };

                if (payloadLength <= 65535)
                {
                    var header = new byte[4];
                    header[0] = 0x82;
                    header[1] = 126;
                    var lenBytes = BitConverter.GetBytes((ushort)payloadLength);
                    header[2] = lenBytes[1];
                    header[3] = lenBytes[0];
                    return header;
                }

                {
                    var header = new byte[10];
                    header[0] = 0x82;
                    header[1] = 127;
                    var lenBytes = BitConverter.GetBytes((ulong)payloadLength);
                    for (int i = 0; i < 8; ++i)
                        header[2 + i] = lenBytes[7 - i];
                    return header;
                }
            }

            void WriteFrameF(NetworkStream targetStream, byte[] buffer, int offset, int count)
            {
                var header = BuildFrameHeader(count);
                targetStream.Write(header, 0, header.Length);
                targetStream.Write(buffer, offset, count);
            }

            void WriteFrame(NetworkStream targetStream, byte[] buffer)
                => WriteFrameF(targetStream, buffer, 0, buffer.Length);

            void SendData(NetworkStream stream, IEnumerable<byte> send)
            {
                const int ChunkSize = 64 * 1024; // 64KB
                var payload = send as byte[] ?? send.ToArray();
                var length = payload.Length;

                if (length <= ChunkSize)
                {
                    WriteFrame(stream, payload);
                    return;
                }

                var totalChunks = (length + ChunkSize - 1) / ChunkSize;
                if (totalChunks > 0xFFFF)
                    throw new InvalidOperationException($"Payload too large to split into 16-bit chunk count: {length} bytes");

                var chunkHeader = new byte[4];
                chunkHeader[0] = 0xAB;
                chunkHeader[1] = 0xCD;
                chunkHeader[2] = (byte)((totalChunks >> 8) & 0xFF);
                chunkHeader[3] = (byte)(totalChunks & 0xFF);
                WriteFrame(stream, chunkHeader);

                for (int chunkIndex = 0; chunkIndex < totalChunks; ++chunkIndex)
                {
                    var offset = chunkIndex * ChunkSize;
                    var chunkLength = Math.Min(ChunkSize, length - offset);
                    WriteFrameF(stream, payload, offset, chunkLength);
                }
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
                        {
                            var r = stream.Read(buf, alread_read, n - alread_read);
                            if (r <= 0) throw new IOException("WebSocket stream closed");
                            alread_read += r;
                        }
                    }

                    // Read the first 2 bytes of the frame header.
                    byte[] buffer = new byte[2]; // The buffer for reading a frame.

                    // stream.Read(buffer, 0, 2);
                    Read(buffer, 2);
                    opcode = buffer[0] & 0x0F;
                    length = buffer[1] & 0x7F;

                    if (opcode == 8)
                        throw new OperationCanceledException("WebSocket close frame received");

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

             
            var terminal = session.Terminal;
            var sync = new object();
            try
            {
                lock (session.ConnectionLock)
                {
                    if (Volatile.Read(ref session.Closing) == 1)
                        throw new Exception("Session is closing");

                    terminal.remoteEndPoint = ((IPEndPoint)socket.RemoteEndPoint).ToString();
                    terminal.SendDataDelegate = (bytes) => SendData(stream, bytes);
                    session.Connected = true;
                    Interlocked.Increment(ref session.ConnectionGen);
                }

                Console.WriteLine($"WebTerminal serve {terminal.remoteEndPoint} as ID={terminal.ID}, sid={session.Id}");

                // Only create welcome panel the first time; reconnect should keep existing panels.
                if (terminal.registeredPanels.Count == 0)
                {
                    var initPanel = new Panel(terminal);
                    initPanel.Define(remoteWelcomePanel(terminal));
                }
                else
                {
                    // Reconnected: force a full redraw to resync client state.
                    try { terminal.SwapBuffer(terminal.registeredPanels.Keys.ToArray()); } catch { }
                }

                bool allowWsAPI = true;
                var myGen = Volatile.Read(ref session.ConnectionGen);
                Task.Run(() =>
                {
                    while (terminal.alive)
                    {
                        if (!session.Connected || Volatile.Read(ref session.ConnectionGen) != myGen)
                            break;
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
                    if (!session.Connected || Volatile.Read(ref session.ConnectionGen) != myGen)
                        break;
                    var first = ReadData(stream);
                    if (first == null || first.Length < 4) continue;
                    int type = BitConverter.ToInt32(first, 0);

                    //Console.WriteLine($"tcp server recv type {type} command");
                    void WSWork(byte[] data)
                    {
                        GUI.RunUITask(() =>
                        {
                            try
                            {
                                Workspace.ReceiveTerminalFeedback(data, terminal);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.MyFormat());
                                UITools.Alert($"Workspace process error:{ex.MyFormat()}", "server-fault", terminal);
                            }
                        }, "WS");
                    }

                    switch (type)
                    {
                        //type0=ui stack feedback.
                        case 0:
                        {
                            // allow combining type+payload in one websocket message (optional protocol improvement)
                            var data = first.Length > 4 ? first.Skip(4).ToArray() : ReadData(stream);
                            GUI.ReceiveTerminalFeedback(data, terminal);
                            break;
                        }
                        case 1:
                            WSWork(first.Length > 4 ? first.Skip(4).ToArray() : ReadData(stream));
                            break;
                        case 2:
                        {
                            // ws api notice.
                            allowWsAPI = true;
                            lock (sync)
                                Monitor.PulseAll(sync);
                            break;
                        }
                        case 3:
                        {
                            WSWork(first.Length > 4 ? first.Skip(4).ToArray() : ReadData(stream));
                            // feedback interval.
                            lock (terminal.syncSend)
                                terminal.SendDataDelegate([2, 0, 0, 0]);
                            break;
                        }
                        case 4:
                        {
                            lock (terminal.syncSend)
                                terminal.SendDataDelegate([2, 0, 0, 0]);
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Explicit close from client: close immediately, no reconnect grace.
                Console.WriteLine($"WebTerminal closed by client, sid={session.Id}, T{terminal.ID}");
                session.Connected = false;
                Volatile.Write(ref session.Closing, 1);
                terminal.Close();
                sessions.TryRemove(session.Id, out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Terminal exception:{ex.MyFormat()}");
                // Unexpected disconnect: keep terminal alive briefly for reconnect.
                session.Connected = false;
                session.LastDisconnectUtc = DateTime.UtcNow;
                session.ArmCloseIfStillDisconnected(session.LastDisconnectUtc);
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
        // Console.WriteLine($"Terminal Swapbuffer T{ID} for [{string.Join(",",mentionedPid)}]");
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