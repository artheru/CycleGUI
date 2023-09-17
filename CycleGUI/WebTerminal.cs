using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CycleGUI.PanelBuilder;

namespace CycleGUI;

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


    public static void Use()
    {
        if (remoteWelcomePanel == null) throw new WelcomePanelNotSetException();
        Console.WriteLine("use webterminal");
        PlankHttpServer2.AddServingResources("/terminal/", "res");
        PlankHttpServer2.AddSpecialTreat("/terminal/data", (headers, stream, socket) =>
        {
            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // Read the HTTP request headers
            string secWebSocketKey = null;

            // Parse the headers to find the Sec-WebSocket-Key
            var lines=headers.Split('\n');
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

            Console.WriteLine("WebSocket connection established with client.");


            void SendData(NetworkStream stream, byte[] send)
            {
                var length = send.Length;
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

                List<byte[]> messages = new List<byte[]>(); // The list of messages.

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
                        var buf=new byte[2];
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


            try
            {
                var terminal = new WebTerminal();
                terminal.remoteEndPoint = ((IPEndPoint)socket.RemoteEndPoint).ToString();
                terminal.SendDataDelegate = (byte[] bytes) => SendData(stream, bytes);

                var initPanel = new Panel(terminal);
                initPanel.Define(remoteWelcomePanel);

                while (true)
                    GUI.ReceiveTerminalFeedback(ReadData(stream), terminal);
            }
            catch
            {

            }
        });
    }

    private static Dictionary<string, WebTerminal> openedTerminals = new();

    private string remoteEndPoint="/";
    public override string description => remoteEndPoint;
    
    private Action<byte[]> SendDataDelegate;

    public override void SwapBuffer(int[] mentionedPid)
    {
        lock (this)
            SendDataDelegate(GenerateRemoteSwapCommands(mentionedPid));
    }
    
}