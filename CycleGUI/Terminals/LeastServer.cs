﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web;
using CycleGUI;

public class LeastServer
{
    // todo: add 304 not modified.
    public class HTTPCtx
    {
        public HTTPCtx(TcpClient client, string request, NetworkStream body)
        {
            Request = new HTTPRequest(request, body);
            Request.Address = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            Response = new HTTPResponse(body);
        }

        public HTTPRequest Request { get; }

        public HTTPResponse Response { get; }
    }


    public class HTTPRequest
    {
        public string Address;
        public readonly int Length;

        /// <summary>
        /// If client sends Header "If-Modified-Since: <date>", we store it here
        /// </summary>
        public DateTime? IfModifiedSince { get; private set; }

        public HTTPRequest(string request, Stream body)
        {
            // You can parse request string to fill properties like LocalPath, Query, etc.
            string[] lines = request.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string firstLine = lines[0];
            string[] firstLineParts = firstLine.Split(' ');

            // Assuming firstLineParts[1] is something like "/path?query"
            string[] pathAndQuery = firstLineParts[1].Split('?');
            LocalPath = pathAndQuery[0];
            Query = pathAndQuery.Length > 1 ? pathAndQuery[1] : "";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ');
                    if (parts.Length > 1)
                        Length = int.Parse(parts[1]);
                }
                else if (line.StartsWith("If-Modified-Since:", StringComparison.OrdinalIgnoreCase))
                {
                    // Attempt to parse the date
                    var datePart = line.Substring("If-Modified-Since:".Length).Trim();
                    if (DateTime.TryParse(datePart, out DateTime dt))
                    {
                        IfModifiedSince = dt.ToUniversalTime();
                    }
                }
            }

            if (Length > 0)
            {
                Body = new byte[Length];
                body.Read(Body, 0, Length);
            }
        }

        public byte[] Body { get; }

        public string LocalPath { get; }

        public string Query { get; }

        public Stream InputStream { get; }

        public Encoding ContentEncoding { get; private set; }
    }

    public class HTTPResponse
    {
        private NetworkStream _outputStream;

        public HTTPResponse(NetworkStream stream)
        {
            _outputStream = stream;
        }

        public string ContentType { get; set; }

        public Encoding ContentEncoding { get; set; }

        public int StatusCode { get; set; }

        /// <summary>
        /// If we have a known last-modified time, we store it here so we can write to the header.
        /// </summary>
        public DateTime? LastModified { get; set; }

        /// <summary>
        /// Optional Cache-Control header value.
        /// </summary>
        public string CacheControl { get; set; }

        public void Write(byte[] buffer, int offset, int count)
        {
            var statusLine = StatusCode switch
            {
                200 => "HTTP/1.1 200 OK",
                304 => "HTTP/1.1 304 Not Modified",
                404 => "HTTP/1.1 404 Not Found",
                500 => "HTTP/1.1 500 Internal Server Error",
                _ => "HTTP/1.1 200 OK"
            };

            // Build response header
            var builder = new StringBuilder();
            builder.AppendLine(statusLine);

            // If 304, we do not send a response body—just headers
            if (StatusCode != 304)
            {
                builder.AppendLine($"Content-Length: {count}");
            }
            if (!string.IsNullOrEmpty(ContentType))
            {
                builder.AppendLine($"Content-Type: {ContentType}");
            }
            if (!string.IsNullOrEmpty(CacheControl))
            {
                builder.AppendLine($"Cache-Control: {CacheControl}");
            }
            if (LastModified.HasValue)
            {
                builder.AppendLine($"Last-Modified: {LastModified.Value.ToString("R")}");
            }
            builder.AppendLine();

            var headerBytes = Encoding.UTF8.GetBytes(builder.ToString());
            _outputStream.Write(headerBytes, 0, headerBytes.Length);

            // If 304 => no body
            if (StatusCode != 304)
            {
                _outputStream.Write(buffer, offset, count);
            }

            _outputStream.Flush();
            _outputStream.Close();
        }
    }

    public static void Listener(int port = 8080)
    {

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        Console.WriteLine($"Start Least Http server CycleGUI WebUI on {port}");

        while (true)
        {
            var client = listener.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem((_) => {
                try
                {
                    HandleClient(client);
                }
                catch
                {

                }});
        }
    }

    static void HandleClient(TcpClient client)
    {
        using (var stream = client.GetStream())
        {
            StringBuilder requestString = new StringBuilder();

            string ReadLineFromStream(Stream stream)
            {
                StringBuilder stringBuilder = new StringBuilder();
                byte[] buffer = new byte[1]; // Read one byte at a time

                while (stream.Read(buffer, 0, 1) > 0)
                {
                    char c = (char)buffer[0];

                    // Break if end-of-line is reached
                    if (c == '\n') break;
                    if (c == '\r') // Ignore carriage return if followed by newline
                    {
                        if (stream.ReadByte() != '\n') stream.Seek(-1, SeekOrigin.Current); // Move back if next byte is not newline
                        break;
                    }

                    stringBuilder.Append(c);
                }

                if (stringBuilder.Length == 0) return null; // End of file

                return stringBuilder.ToString();
            }

            String line;
            while (!string.IsNullOrEmpty(line = ReadLineFromStream(stream)))
            {
                requestString.Append(line + "\n");
            }

            HTTPCtx context = new HTTPCtx(client, requestString.ToString(), stream);


            string localPath = "/", query = "/";
            try
            {
                HTTPRequest request = context.Request;

                localPath = request.LocalPath;
                if (special.TryGetValue(localPath, out var specialAction))
                {
                    specialAction.Invoke(requestString.ToString(), stream, client.Client);
                    return;
                }

                query = request.Query;
                // routing:

                var ps = posts.Where(p => p.Key == localPath).ToArray();
                if (ps.Length > 0)
                {
                    ps[0].Value(context);
                    return;
                }

                var qs = gets.Where(p => p.Key == localPath).ToArray();
                if (qs.Length > 0)
                {
                    qs[0].Value(context);
                    return;
                }

                var file = servingFiles.Where(p => p.Key == localPath || localPath.StartsWith(p.Key))
                    .ToArray();
                if (file.Length > 0 && ServeFile(context, file[0].Key, localPath)) return;

                var res = servingResources.Where(p => p.Key == localPath || localPath.StartsWith(p.Key))
                    .ToArray();
                if (res.Length > 0 && ServeResources(context, res[0].Key, localPath)) return;


                var response = context.Response;
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.ContentType = "text/plain";
                var body = Encoding.UTF8.GetBytes("Not found!");
                response.Write(body, 0, body.Length);
            }
            catch (Exception err)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"WebAPI {localPath}?{query} Error: ex=" + err.MyFormat());
                Console.ResetColor();
                try
                {
                    var response = context.Response;
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.ContentType = "text/plain";
                    var body = Encoding.UTF8.GetBytes($"Error:{err.Message}");
                    response.Write(body, 0, body.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("WebAPI: Not able to feedback error");
                }
            }
        }
    }


    // special queries: userHostAddress.
    private static T ParseAnonymousQuery<T>(string query, HTTPCtx ctx)
    {
        var dict = HttpUtility.HtmlDecode(query).Trim('?').Split('&').Select(p => p.Split('='))
            .ToDictionary(p => p[0].ToLower(), p => p.Length == 2 ? p[1] : p[0]);
        var ci = typeof(T).GetConstructors()[0];
        var paramls = new object[ci.GetParameters().Length];
        for (var i = 0; i < ci.GetParameters().Length; i++)
        {
            var pi = ci.GetParameters()[i];
            if (dict.ContainsKey(pi.Name.ToLower()))
                paramls[i] = TypeDescriptor.GetConverter(pi.ParameterType)
                    .ConvertFromString(dict[pi.Name.ToLower()]);
            if (pi.Name.ToLower() == "userhostaddress")
                paramls[i] = ctx.Request.Address;
        }

        T ret = (T)ci.Invoke(paramls);

        return ret;
    }


    public static void AddSpecialTreat(string path, Action<string, NetworkStream, Socket> action) => special[path] = action;

    public static void AddGetHandler<Tin, Tout>(string path, Tin type, Func<Tin, Tout> func) => gets.Add(path,
        ctx => processRetVal(func(ParseAnonymousQuery<Tin>(ctx.Request.Query, ctx)), ctx));

    public static void AddGetHandler<T>(string path, Func<T> func) =>
        gets.Add(path, ctx => processRetVal(func(), ctx));

    private static void addPost<TPost, TRet>(string path, Func<TPost, TRet> func,
        Func<HTTPCtx, TPost> parser) => posts.Add(path, (ctx) => processRetVal(func(parser(ctx)), ctx));

    private static void addPost<TQuery, TPost, TRet>(string path, TQuery type, Func<TQuery, TPost, TRet> func,
        Func<HTTPCtx, TPost> parser) => posts.Add(path,
        (ctx) => processRetVal(func(ParseAnonymousQuery<TQuery>(ctx.Request.Query, ctx), parser(ctx)), ctx));

    public static void AddPostTextHandler<TRet>(string path, Func<string, TRet> func) =>
        addPost(path, func, parseTxt);

    public static void AddPostByteHandler<TRet>(string path, Func<byte[], TRet> func) =>
        addPost(path, func, parseByte);

    public static void
        AddPostByteHandler<TQuery, TRet>(string path, TQuery query, Func<TQuery, byte[], TRet> func) =>
        addPost(path, query, func, parseByte);

    public static void
        AddPostTextHandler<TQuery, TRet>(string path, TQuery query, Func<TQuery, string, TRet> func) =>
        addPost(path, query, func, parseTxt);


    public class BCType
    {
        public byte[] bytes;
        public string contentType;
    }
    private static void processRetVal(object ret, HTTPCtx ctx)
    {
        if (ret is string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.Write(bytes, 0, bytes.Length);
        }
        else if (ret is byte[] bytes)
        {
            ctx.Response.Write(bytes, 0, bytes.Length);
        }
        else if (ret is BCType bytesWithCT)
        {
            ctx.Response.ContentType = bytesWithCT.contentType;
            ctx.Response.Write(bytesWithCT.bytes, 0, bytesWithCT.bytes.Length);
        }
    }


    private static string parseTxt(HTTPCtx ctx)
    {
        return UTF8Encoding.UTF8.GetString(ctx.Request.Body);
    }

    private static byte[] parseByte(HTTPCtx ctx)
    {
        return ctx.Request.Body;
    }

    public static void AddServingResources(string path, string resPath)
    {
        servingResources.Add(path, (Assembly.GetCallingAssembly(), resPath));
    }

    public static void AddServingFiles(string path, string resPath)
    {
        servingFiles.Add(path, resPath);
    }

    private static Dictionary<string, (Assembly asm, string resPath)> servingResources = new();
    private static Dictionary<string, string> servingFiles = new();
    private static Dictionary<string, Action<HTTPCtx>> gets = new();
    private static Dictionary<string, Action<HTTPCtx>> posts = new();
    private static Dictionary<string, Action<string, NetworkStream, Socket>> special = new();


    internal static string GetContentType(string ext)
    {
        string ContentType;
        switch (ext)
        {
            case "asf":
                ContentType = "video/x-ms-asf";
                break;
            case "avi":
                ContentType = "video/avi";
                break;
            case "doc":
                ContentType = "application/msword";
                break;
            case "zip":
                ContentType = "application/zip";
                break;
            case "pdf":
                ContentType = "application/pdf";
                break;
            case "xls":
                ContentType = "application/vndms-excel";
                break;
            case "gif":
                ContentType = "image/gif";
                break;
            case "jpg":
                ContentType = "image/jpeg";
                break;
            case "jpeg":
                ContentType = "image/jpeg";
                break;
            case "png":
                ContentType = "image/png";
                break;
            case "wav":
                ContentType = "audio/wav";
                break;
            case "mp3":
                ContentType = "audio/mpeg3";
                break;
            case "mpg":
                ContentType = "video/mpeg";
                break;
            case "mepg":
                ContentType = "video/mpeg";
                break;
            case "rtf":
                ContentType = "application/rtf";
                break;
            case "html":
                ContentType = "text/html";
                break;
            case "htm":
                ContentType = "text/html";
                break;
            case "js":
                ContentType = "text/javascript";
                break;
            case "txt":
                ContentType = "text/plain";
                break;
            case "json":
                ContentType = "text/json";
                break;
            default:
                ContentType = "application/octet-stream";
                break;
        }

        return ContentType;
    }

    private static bool ServeResources(HTTPCtx ctx, string path, string localPath)
    {
        var resPath = servingResources[path];
        if (localPath.EndsWith("/")) localPath = localPath + "index.html";
        var rest = localPath.Remove(0, path.Length).Trim('/').Replace('/', '.');
        var fullname = resPath.asm.GetName().Name + "." +
                       resPath.resPath.Replace("/", ".") + "." + rest;
        var stream = resPath.asm.GetManifestResourceStream(fullname);
        if (stream == null)
            return false;
        // perhaps we should serve as file?

        var response = ctx.Response; //响应

        response.StatusCode = 200;
        response.ContentType = GetContentType(Path.GetExtension(rest).Remove(0, 1));

        // We treat the assembly's file date/time as resource's last modified time
        var asmLocation = resPath.asm.Location;
        DateTime lastWriteTimeUtc;
        try
        {
            lastWriteTimeUtc = File.GetLastWriteTimeUtc(asmLocation);
        }
        catch
        {
            // Fallback if we can't get file info
            lastWriteTimeUtc = DateTime.UtcNow;
        }

        ctx.Response.CacheControl = "public, max-age=1";
        ctx.Response.LastModified = lastWriteTimeUtc; // so Last-Modified gets written out

        // Check If-Modified-Since
        if (ctx.Request.IfModifiedSince.HasValue &&
            ctx.Request.IfModifiedSince.Value.AddSeconds(1) >= lastWriteTimeUtc)
        {
            ctx.Response.StatusCode = 304;
            ctx.Response.ContentType = GetContentType(Path.GetExtension(rest).Trim('.'));
            ctx.Response.Write(Array.Empty<byte>(), 0, 0);
            return true;
        }

        //输出响应内容
        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        response.Write(buffer, 0, buffer.Length);
        return true;
    }

    private static bool ServeFile(HTTPCtx ctx, string path, string localPath)
    {
        var resPath = servingFiles[path];
        if (localPath.EndsWith("/")) localPath = localPath + "index.html";
        var rest = localPath.Remove(0, path.Length).Trim('/');
        var fullPath = Path.Combine(resPath, rest);

        //Console.WriteLine($"serve {fullPath}");
        if (!File.Exists(fullPath))
            return false;
        using var stream = File.OpenRead(fullPath);
        // perhaps we should serve as file?

        var response = ctx.Response; //响应

        response.StatusCode = 200;
        response.ContentType = GetContentType(Path.GetExtension(fullPath).Remove(0, 1));

        // Get the file's last write time for 304 check
        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);

        ctx.Response.CacheControl = "public, max-age=1";
        ctx.Response.LastModified = lastWriteTimeUtc; // so we output Last-Modified
        // Check If-Modified-Since
        if (ctx.Request.IfModifiedSince.HasValue &&
            ctx.Request.IfModifiedSince.Value.AddSeconds(1) >= lastWriteTimeUtc)
        {
            ctx.Response.StatusCode = 304;
            ctx.Response.ContentType = GetContentType(Path.GetExtension(fullPath).Trim('.'));
            ctx.Response.Write(Array.Empty<byte>(), 0, 0);
            return true;
        }

        //输出响应内容
        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        response.Write(buffer, 0, buffer.Length);
        return true;
    }
}