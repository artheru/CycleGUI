using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;
using CycleGUI.API;
using System.Net;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HoloExample
{
    internal static partial class Program
    {
        private static Panel stereoPanel = null;

        private static Action<byte[]> stereoUpdater;
        private static float lx = -117, ly = -47, lrot = -0.01f;
        private static float rx = 0, ry = 0, rrot = 0;
        private const float PI = (float)Math.PI;
        private static int targetWGlobal = 0, targetHGlobal = 0;
        private static bool streaming = false;
        private static bool streamerReady = false;
        private static bool unifiedStereo = false; // unified SBS source
        private static int sbsFormatIndex = 0; // 0: LR, 1: RL
        private static bool undistortLeft = false, undistortRight = false; // enable camera undistortion per eye
        private static float lk1 = 0.0f, lk2 = 0.0f; // left radial
        private static float rk1 = 0.0f, rk2 = 0.0f; // right radial
        private static float lCenterOffsetX = 0.0f, lCenterOffsetY = 0.0f; // left center offsets
        private static float rCenterOffsetX = 0.0f, rCenterOffsetY = 0.0f; // right center offsets
        private static float lZoom = 1.0f, rZoom = 1.0f; // per-eye zoom
        private static string currentIp = "192.168.0.192";
        private static bool settingsLoaded = false;

        class StereoSettings
        {
            public string ip { get; set; }
            public float lx { get; set; }
            public float ly { get; set; }
            public float lrot { get; set; }
            public float rx { get; set; }
            public float ry { get; set; }
            public float rrot { get; set; }
            public bool undistortLeft { get; set; }
            public bool undistortRight { get; set; }
            public float lk1 { get; set; }
            public float lk2 { get; set; }
            public float rk1 { get; set; }
            public float rk2 { get; set; }
            public float lCenterOffsetX { get; set; }
            public float lCenterOffsetY { get; set; }
            public float rCenterOffsetX { get; set; }
            public float rCenterOffsetY { get; set; }
            public float lZoom { get; set; }
            public float rZoom { get; set; }
            public int sbsFormatIndex { get; set; }
        }

        static string SettingsPath()
        {
            var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "stereocam.json");
        }

        static void SaveSettings()
        {
            try
            {
                var s = new StereoSettings
                {
                    ip = currentIp,
                    lx = lx, ly = ly, lrot = lrot,
                    rx = rx, ry = ry, rrot = rrot,
                    undistortLeft = undistortLeft, undistortRight = undistortRight,
                    lk1 = lk1, lk2 = lk2, rk1 = rk1, rk2 = rk2,
                    lCenterOffsetX = lCenterOffsetX, lCenterOffsetY = lCenterOffsetY,
                    rCenterOffsetX = rCenterOffsetX, rCenterOffsetY = rCenterOffsetY,
                    lZoom = lZoom, rZoom = rZoom,
                    sbsFormatIndex = sbsFormatIndex,
                };
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath(), json);
            }
            catch { }
        }

        static void TryLoadSettings()
        {
            try
            {
                var path = SettingsPath();
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<StereoSettings>(json);
                if (s == null) return;
                currentIp = string.IsNullOrWhiteSpace(s.ip) ? currentIp : s.ip;
                lx = s.lx; ly = s.ly; lrot = s.lrot;
                rx = s.rx; ry = s.ry; rrot = s.rrot;
                undistortLeft = s.undistortLeft; undistortRight = s.undistortRight;
                lk1 = s.lk1; lk2 = s.lk2; rk1 = s.rk1; rk2 = s.rk2;
                lCenterOffsetX = s.lCenterOffsetX; lCenterOffsetY = s.lCenterOffsetY;
                rCenterOffsetX = s.rCenterOffsetX; rCenterOffsetY = s.rCenterOffsetY;
                lZoom = s.lZoom; rZoom = s.rZoom;
                sbsFormatIndex = s.sbsFormatIndex;
            }
            catch { }
        }

        static void ShowRemote(PanelBuilder pb)
        {
            pb.CollapsingHeaderStart("Live Stereo Camera");
            if (pb.Button("Open StereoCamera"))
            {
                new SetCamera() { azimuth = -(float)(Math.PI / 2), altitude = (float)(Math.PI / 2), lookAt = new Vector3(-0.0720f, 0.1536f, -2.0000f), distance = 2.7000f, world2phy = 200f }.IssueToDefault();
                new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }.IssueToDefault();

                if (stereoPanel == null)
                {
                    Workspace.AddProp(new PutImage()
                    {
                        displayH = 0.9f,
                        displayW = 1.6f,
                        displayType = PutImage.DisplayType.World,
                        name = "Vali",
                        rgbaName = "stream",
                    });

                    stereoPanel = GUI.PromptPanel(pbv =>
                    {
                        pbv.Panel.ShowTitle("Stereo camera...");
                        pbv.Label($"Streaming={streaming}, read={streamerReady}");
                        if (pbv.Closing())
                        {
                            stereoPanel = null;
                            WorkspaceProp.RemoveNamePattern("Vali");
                            pbv.Panel.Exit();
                        }

                        if (streaming && !streamerReady)
                        {
                            pbv.Label("Starting...");
                            pbv.Panel.Repaint();
                        }

                        if (!settingsLoaded)
                        {
                            TryLoadSettings();
                            settingsLoaded = true;
                        }
                        var (ret, edit) = pbv.TextInput("IP", currentIp, "Medulla IP");
                        currentIp = ret;
                        if (!streaming && pbv.Button("Start Stream Seperate Left Right"))
                        {
                            streaming = true;
                            unifiedStereo = false;
                            streamerReady = false;
                            new Thread(() =>
                            {
                                // multipart/x-mixed-replace:
                                // IP:8081/stream/rgb_left and IP:8081/stream/rgb_right 
                                // fetch both, merge side-by-side, and stream RGBA frames

                                string leftUrl = $"http://{ret}:8081/stream/rgb_left";
                                string rightUrl = $"http://{ret}:8081/stream/rgb_right";

                                var cts = new CancellationTokenSource();

                                Bitmap latestLeft = null;
                                Bitmap latestRight = null;
                                int targetW = 0, targetH = 0; // single-eye size
                                Bitmap composite = null; // w*2 x h
                                Graphics compositeG = null;
                                byte[] composedRgba = null; // preallocated RGBA buffer

                                void EnsureStreamer(int w, int h)
                                {
                                    if (streamerReady) return;
                                var streamer = Workspace.AddProp(new PutARGB()
                                    {
                                        height = h,
                                        width = w * 2,
                                        name = "stream",
                                        displayType = PutARGB.DisplayType.Stereo3D_LR
                                    });
                                    stereoUpdater = streamer.StartStreaming();
                                    targetW = w; targetH = h;
                                    targetWGlobal = w; targetHGlobal = h;
                                    Console.WriteLine($"Use {w}*{h} LR");
                                    streamerReady = true;

                                    // prepare composite surface and buffer
                                    composite = new Bitmap(w * 2, h, PixelFormat.Format32bppArgb);
                                    compositeG = Graphics.FromImage(composite);
                                    compositeG.CompositingMode = CompositingMode.SourceCopy;
                                    compositeG.CompositingQuality = CompositingQuality.HighSpeed;
                                    compositeG.InterpolationMode = InterpolationMode.NearestNeighbor;
                                    compositeG.SmoothingMode = SmoothingMode.None;
                                    compositeG.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                                    composedRgba = new byte[w * 2 * h * 4];
                                }

                                static Bitmap DecodeJpegToBitmap(byte[] jpeg)
                                {
                                    using (var ms = new MemoryStream(jpeg))
                                    using (var bmp = new Bitmap(ms))
                                    {
                                        return (Bitmap)bmp.Clone();
                                    }
                                }

                                static void ReadMjpeg(string url, Action<byte[]> onFrame, CancellationToken token)
                                {
                                    while (!token.IsCancellationRequested)
                                    {
                                        try
                                        {
                                            var req = (HttpWebRequest)WebRequest.Create(url);
                                            req.AllowReadStreamBuffering = false;
                                            req.Timeout = 5000;
                                            using var resp = (HttpWebResponse)req.GetResponse();
                                            using var stream = resp.GetResponseStream();
                                            if (stream == null) return;

                                            var buffer = new MemoryStream(64 * 1024);
                                            int headerEnd = -1;
                                            int contentLength = -1;
                                            var temp = new byte[16 * 1024];

                                            while (!token.IsCancellationRequested)
                                            {
                                                int n = stream.Read(temp, 0, temp.Length);
                                                if (n <= 0) break;
                                                buffer.Write(temp, 0, n);

                                                while (true)
                                                {
                                                    if (headerEnd < 0)
                                                    {
                                                        var arr = buffer.GetBuffer();
                                                        int len = (int)buffer.Length;
                                                        for (int i = 3; i < len; i++)
                                                        {
                                                            if (arr[i - 3] == 13 && arr[i - 2] == 10 && arr[i - 1] == 13 && arr[i] == 10)
                                                            {
                                                                headerEnd = i + 1;
                                                                var headers = Encoding.ASCII.GetString(arr, 0, headerEnd);
                                                                var idx = headers.IndexOf("Content-Length:", StringComparison.OrdinalIgnoreCase);
                                                                if (idx >= 0)
                                                                {
                                                                    int start = idx + "Content-Length:".Length;
                                                                    while (start < headers.Length && char.IsWhiteSpace(headers[start])) start++;
                                                                    int end = start;
                                                                    while (end < headers.Length && char.IsDigit(headers[end])) end++;
                                                                    contentLength = int.Parse(headers.Substring(start, end - start));
                                                                }
                                                                break;
                                                            }
                                                        }
                                                        if (headerEnd < 0) break;
                                                    }

                                                    if (headerEnd >= 0 && contentLength > 0)
                                                    {
                                                        int available = (int)buffer.Length - headerEnd;
                                                        if (available >= contentLength)
                                                        {
                                                            var arr = buffer.GetBuffer();
                                                            var jpeg = new byte[contentLength];
                                                            Buffer.BlockCopy(arr, headerEnd, jpeg, 0, contentLength);
                                                            onFrame(jpeg);
                                                            // move remaining to new buffer
                                                            int remain = (int)buffer.Length - (headerEnd + contentLength);
                                                            var newBuf = new MemoryStream(Math.Max(64 * 1024, remain + 4096));
                                                            if (remain > 0)
                                                                newBuf.Write(arr, headerEnd + contentLength, remain);
                                                            buffer.Dispose();
                                                            buffer = newBuf;
                                                            headerEnd = -1;
                                                            contentLength = -1;
                                                            continue; // try parse next frame from residual data
                                                        }
                                                    }
                                                    break; // need more bytes
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            // backoff then retry
                                            if (token.IsCancellationRequested) break;
                                            Thread.Sleep(300);
                                        }
                                    }
                                }

                                void TryComposeAndSend()
                                {
                                    if (!streamerReady) return;
                                    if (latestLeft == null || latestRight == null) return;

                                    int w = targetW, h = targetH;
                                    var g = compositeG;
                                    g.ResetTransform();
                                    g.Clear(Color.Black);

                                    // Left eye with pan/rotation, scaled to w x h
                                    float deg = lrot * 180f / (float)Math.PI;
                                    var state = g.Save();
                                    g.TranslateTransform(w / 2f + lx, h / 2f + ly);
                                    g.RotateTransform(deg);
                                    g.TranslateTransform(-w / 2f, -h / 2f);
                                    g.DrawImage(latestLeft, new Rectangle(0, 0, w, h));
                                    g.Restore(state);

                                    // Right eye baseline, scaled
                                    g.DrawImage(latestRight, new Rectangle(w, 0, w, h));

                                    // marshal to preallocated RGBA buffer
                                    var rect = new Rectangle(0, 0, composite.Width, composite.Height);
                                    var data = composite.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                                    try
                                    {
                                        int stride = data.Stride;
                                        int tw = composite.Width;
                                        int th = composite.Height;
                                        unsafe
                                        {
                                            byte* src = (byte*)data.Scan0;
                                            int di = 0;
                                            for (int y = 0; y < th; y++)
                                            {
                                                byte* row = src + y * stride;
                                                for (int x = 0; x < tw; x++)
                                                {
                                                    byte b = row[x * 4 + 0];
                                                    byte gch = row[x * 4 + 1];
                                                    byte r = row[x * 4 + 2];
                                                    byte a = row[x * 4 + 3];
                                                    composedRgba[di++] = r;
                                                    composedRgba[di++] = gch;
                                                    composedRgba[di++] = b;
                                                    composedRgba[di++] = a;
                                                }
                                            }
                                        }
                                        stereoUpdater(composedRgba);
                                    }
                                    finally
                                    {
                                        composite.UnlockBits(data);
                                    }
                                }

                                void OnLeft(byte[] jpeg)
                                {
                                    var sb = DecodeJpegToBitmap(jpeg);
                                    latestLeft = sb;
                                    if (!streamerReady)
                                        EnsureStreamer(sb.Width, sb.Height);
                                    TryComposeAndSend();
                                }
                                void OnRight(byte[] jpeg)
                                {
                                    var sb = DecodeJpegToBitmap(jpeg);
                                    latestRight = sb;
                                }

                                var tLeft = new Thread(() => ReadMjpeg(leftUrl, OnLeft, cts.Token)) { IsBackground = true };
                                var tRight = new Thread(() => ReadMjpeg(rightUrl, OnRight, cts.Token)) { IsBackground = true };
                                tLeft.Start();
                                tRight.Start();

                                while (stereoPanel != null)
                                {
                                    Thread.Sleep(50);
                                }

                                cts.Cancel();
                                try { compositeG?.Dispose(); } catch { }
                                try { composite?.Dispose(); } catch { }
                                compositeG = null; composite = null; composedRgba = null;
                            }).Start();
                        }

                        if (!streaming && pbv.Button("Start Stream Unified Stereo"))
                        {
                            streaming = true;
                            unifiedStereo = true;
                            streamerReady = false;
                            new Thread(() =>
                            {
                                string sbsUrl = $"http://{ret}:8081/stream/rgb_stereocam";
                                var cts = new CancellationTokenSource();

                                Bitmap latestUnified = null;
                                int targetW = 0, targetH = 0; // single-eye size
                                Bitmap composite = null; // w*2 x h
                                Graphics compositeG = null;
                                byte[] composedRgba = null; // preallocated

                                // undistort resources
                                Bitmap srcUnified32 = null; // 32bpp staging
                                Bitmap leftUnd = null, rightUnd = null;
                                int[] mapX = null, mapY = null; // left-eye map
                                int mapW = 0, mapH = 0;
                                float prevLk1 = float.NaN, prevLk2 = float.NaN, prevLCx = float.NaN, prevLCy = float.NaN, prevLZoom = float.NaN;

                                void EnsureStreamer(int singleW, int singleH)
                                {
                                    if (streamerReady) return;
                                    var streamer = Workspace.AddProp(new PutARGB()
                                    {
                                        height = singleH,
                                        width = singleW * 2,
                                    name = "stream",
                                    displayType = PutARGB.DisplayType.Stereo3D_LR
                                });
                                stereoUpdater = streamer.StartStreaming();
                                    targetW = singleW; targetH = singleH;
                                    targetWGlobal = singleW; targetHGlobal = singleH;
                                    Console.WriteLine($"Use {singleW}*{singleH} LR (Unified)");
                                    streamerReady = true;

                                    composite = new Bitmap(singleW * 2, singleH, PixelFormat.Format32bppArgb);
                                    compositeG = Graphics.FromImage(composite);
                                    compositeG.CompositingMode = CompositingMode.SourceCopy;
                                    compositeG.CompositingQuality = CompositingQuality.HighSpeed;
                                    compositeG.InterpolationMode = InterpolationMode.NearestNeighbor;
                                    compositeG.SmoothingMode = SmoothingMode.None;
                                    compositeG.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                                    composedRgba = new byte[singleW * 2 * singleH * 4];

                                    srcUnified32 = new Bitmap(singleW * 2, singleH, PixelFormat.Format32bppArgb);
                                    leftUnd = new Bitmap(singleW, singleH, PixelFormat.Format32bppArgb);
                                    rightUnd = new Bitmap(singleW, singleH, PixelFormat.Format32bppArgb);
                                }

                                static Bitmap DecodeJpegToBitmap(byte[] jpeg)
                                {
                                    using (var ms = new MemoryStream(jpeg))
                                    using (var bmp = new Bitmap(ms))
                                    {
                                        return (Bitmap)bmp.Clone();
                                    }
                                }

                                static void ReadMjpeg(string url, Action<byte[]> onFrame, CancellationToken token)
                                {
                                    while (!token.IsCancellationRequested)
                                    {
                                        try
                                        {
                                            var req = (HttpWebRequest)WebRequest.Create(url);
                                            req.AllowReadStreamBuffering = false;
                                            req.Timeout = 5000;
                                            using var resp = (HttpWebResponse)req.GetResponse();
                                            using var stream = resp.GetResponseStream();
                                            if (stream == null) return;

                                            var buffer = new MemoryStream(64 * 1024);
                                            int headerEnd = -1;
                                            int contentLength = -1;
                                            var temp = new byte[16 * 1024];

                                            while (!token.IsCancellationRequested)
                                            {
                                                int n = stream.Read(temp, 0, temp.Length);
                                                if (n <= 0) break;
                                                buffer.Write(temp, 0, n);

                                                while (true)
                                                {
                                                    if (headerEnd < 0)
                                                    {
                                                        var arr = buffer.GetBuffer();
                                                        int len = (int)buffer.Length;
                                                        for (int i = 3; i < len; i++)
                                                        {
                                                            if (arr[i - 3] == 13 && arr[i - 2] == 10 && arr[i - 1] == 13 && arr[i] == 10)
                                                            {
                                                                headerEnd = i + 1;
                                                                var headers = Encoding.ASCII.GetString(arr, 0, headerEnd);
                                                                var idx = headers.IndexOf("Content-Length:", StringComparison.OrdinalIgnoreCase);
                                                                if (idx >= 0)
                                                                {
                                                                    int start = idx + "Content-Length:".Length;
                                                                    while (start < headers.Length && char.IsWhiteSpace(headers[start])) start++;
                                                                    int end = start;
                                                                    while (end < headers.Length && char.IsDigit(headers[end])) end++;
                                                                    contentLength = int.Parse(headers.Substring(start, end - start));
                                                                }
                                                                break;
                                                            }
                                                        }
                                                        if (headerEnd < 0) break;
                                                    }

                                                    if (headerEnd >= 0 && contentLength > 0)
                                                    {
                                                        int available = (int)buffer.Length - headerEnd;
                                                        if (available >= contentLength)
                                                        {
                                                            var arr = buffer.GetBuffer();
                                                            var jpeg = new byte[contentLength];
                                                            Buffer.BlockCopy(arr, headerEnd, jpeg, 0, contentLength);
                                                            onFrame(jpeg);
                                                            int remain = (int)buffer.Length - (headerEnd + contentLength);
                                                            var newBuf = new MemoryStream(Math.Max(64 * 1024, remain + 4096));
                                                            if (remain > 0)
                                                                newBuf.Write(arr, headerEnd + contentLength, remain);
                                                            buffer.Dispose();
                                                            buffer = newBuf;
                                                            headerEnd = -1;
                                                            contentLength = -1;
                                                            continue;
                                                        }
                                                    }
                                                    break;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            if (token.IsCancellationRequested) break;
                                            Thread.Sleep(300);
                                        }
                                    }
                                }

                                void BuildUndistortMap(int w, int h)
                                {
                                    mapW = w; mapH = h;
                                    // snapshot left params to detect changes precisely
                                    prevLk1 = lk1; prevLk2 = lk2; prevLCx = lCenterOffsetX; prevLCy = lCenterOffsetY; prevLZoom = lZoom;

                                    float lcx = w * 0.5f + lCenterOffsetX;
                                    float lcy = h * 0.5f + lCenterOffsetY;
                                    float lfx = (w * 0.5f) / Math.Max(0.001f, lZoom);
                                    float lfy = lfx;

                                    if (mapX == null || mapX.Length != w * h) mapX = new int[w * h];
                                    if (mapY == null || mapY.Length != w * h) mapY = new int[w * h];

                                    for (int y = 0; y < h; y++)
                                    {
                                        float v = (y - lcy) / lfy;
                                        for (int x = 0; x < w; x++)
                                        {
                                            float u = (x - lcx) / lfx;
                                            float r2 = u * u + v * v;
                                            float s = 1.0f + lk1 * r2 + lk2 * r2 * r2;
                                            int xs = (int)Math.Round((u * s) * lfx + lcx);
                                            int ys = (int)Math.Round((v * s) * lfy + lcy);
                                            if (xs < 0) xs = 0; if (xs >= w) xs = w - 1;
                                            if (ys < 0) ys = 0; if (ys >= h) ys = h - 1;
                                            int idx = y * w + x;
                                            mapX[idx] = xs;
                                            mapY[idx] = ys;
                                        }
                                    }
                                }

                                void TryComposeAndSend()
                                {
                                    if (!streamerReady) return;
                                    if (latestUnified == null) return;

                                    int fullW = latestUnified.Width; int h = latestUnified.Height; int w = fullW / 2;
                                    EnsureStreamer(w, h);

                                    var g = compositeG;
                                    g.ResetTransform();
                                    g.Clear(Color.Black);

                                    Rectangle srcLeftRect = sbsFormatIndex == 0 ? new Rectangle(0, 0, w, h) : new Rectangle(w, 0, w, h);
                                    Rectangle srcRightRect = sbsFormatIndex == 0 ? new Rectangle(w, 0, w, h) : new Rectangle(0, 0, w, h);

                                    if ((undistortLeft || undistortRight) && (mapW != w || mapH != h || prevLk1 != lk1 || prevLk2 != lk2 || prevLCx != lCenterOffsetX || prevLCy != lCenterOffsetY || prevLZoom != lZoom))
                                        BuildUndistortMap(w, h);

                                    if (undistortLeft || undistortRight)
                                    {
                                        // stage source to 32bpp
                                        using (var sg = Graphics.FromImage(srcUnified32))
                                        {
                                            sg.CompositingMode = CompositingMode.SourceCopy;
                                            sg.InterpolationMode = InterpolationMode.NearestNeighbor;
                                            sg.SmoothingMode = SmoothingMode.None;
                                            sg.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                                            sg.DrawImage(latestUnified, new Rectangle(0, 0, 2 * w, h));
                                        }

                                        var rectAll = new Rectangle(0, 0, 2 * w, h);
                                        var rectHalf = new Rectangle(0, 0, w, h);
                                        var srcData = srcUnified32.LockBits(rectAll, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                                        var leftData = leftUnd.LockBits(rectHalf, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                                        var rightData = rightUnd.LockBits(rectHalf, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                                        try
                                        {
                                            unsafe
                                            {
                                                byte* src = (byte*)srcData.Scan0;
                                                byte* dl = (byte*)leftData.Scan0;
                                                byte* dr = (byte*)rightData.Scan0;
                                                int sStride = srcData.Stride;
                                                int dlStride = leftData.Stride;
                                                int drStride = rightData.Stride;
                                                for (int y = 0; y < h; y++)
                                                {
                                                    byte* dlRow = dl + y * dlStride;
                                                    byte* drRow = dr + y * drStride;
                                                    for (int x = 0; x < w; x++)
                                                    {
                                                int idx = y * w + x;
                                                int lxs = undistortLeft ? mapX[idx] : x;
                                                int lys = undistortLeft ? mapY[idx] : y;
                                                // recompute right map on the fly using right params if undistortRight
                                                int rxs, rys;
                                                if (undistortRight)
                                                {
                                                    float rcx = w * 0.5f + rCenterOffsetX;
                                                    float rcy = h * 0.5f + rCenterOffsetY;
                                                    float rfx = (w * 0.5f) / Math.Max(0.001f, rZoom);
                                                    float rfy = rfx;
                                                    float uR = (x - rcx) / rfx;
                                                    float vR = (y - rcy) / rfy;
                                                    float r2R = uR * uR + vR * vR;
                                                    float sR = 1.0f + rk1 * r2R + rk2 * r2R * r2R;
                                                    float xdR = uR * sR;
                                                    float ydR = vR * sR;
                                                    rxs = (int)Math.Round(xdR * rfx + rcx);
                                                    rys = (int)Math.Round(ydR * rfy + rcy);
                                                    if (rxs < 0) rxs = 0; if (rxs >= w) rxs = w - 1;
                                                    if (rys < 0) rys = 0; if (rys >= h) rys = h - 1;
                                                }
                                                else { rxs = x; rys = y; }
                                                // left/right sample
                                                int srcLX = srcLeftRect.X + lxs;
                                                int srcRX = srcRightRect.X + rxs;
                                                byte* spL = src + lys * sStride + (srcLX * 4);
                                                byte* spR = src + rys * sStride + (srcRX * 4);
                                                        int di = x * 4;
                                                        // copy BGRA 4 bytes
                                                        dlRow[di + 0] = spL[0];
                                                        dlRow[di + 1] = spL[1];
                                                        dlRow[di + 2] = spL[2];
                                                        dlRow[di + 3] = spL[3];
                                                        drRow[di + 0] = spR[0];
                                                        drRow[di + 1] = spR[1];
                                                        drRow[di + 2] = spR[2];
                                                        drRow[di + 3] = spR[3];
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            srcUnified32.UnlockBits(srcData);
                                            leftUnd.UnlockBits(leftData);
                                            rightUnd.UnlockBits(rightData);
                                        }

                                        // Left eye with pan/rot
                                        float deg = lrot * 180f / (float)Math.PI;
                                        var state = g.Save();
                                        g.TranslateTransform(w / 2f + lx, h / 2f + ly);
                                        g.RotateTransform(deg);
                                        g.TranslateTransform(-w / 2f, -h / 2f);
                                        g.DrawImage(leftUnd, new Rectangle(0, 0, w, h));
                                        g.Restore(state);

                                        // Right baseline
                                        g.DrawImage(rightUnd, new Rectangle(w, 0, w, h));
                                    }
                                    else
                                    {
                                        // Left with pan/rot from unified source half
                                        float deg = lrot * 180f / (float)Math.PI;
                                        var state = g.Save();
                                        g.TranslateTransform(w / 2f + lx, h / 2f + ly);
                                        g.RotateTransform(deg);
                                        g.TranslateTransform(-w / 2f, -h / 2f);
                                        g.DrawImage(latestUnified, new Rectangle(0, 0, w, h), srcLeftRect, GraphicsUnit.Pixel);
                                        g.Restore(state);

                                        // Right baseline
                                        g.DrawImage(latestUnified, new Rectangle(w, 0, w, h), srcRightRect, GraphicsUnit.Pixel);
                                    }

                                    // marshal to RGBA
                                    var rect = new Rectangle(0, 0, composite.Width, composite.Height);
                                    var data = composite.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                                    try
                                    {
                                        int stride = data.Stride;
                                        int tw = composite.Width;
                                        int th = composite.Height;
                                        unsafe
                                        {
                                            byte* src = (byte*)data.Scan0;
                                            int di = 0;
                                            for (int y = 0; y < th; y++)
                                            {
                                                byte* row = src + y * stride;
                                                for (int x = 0; x < tw; x++)
                                                {
                                                    byte b = row[x * 4 + 0];
                                                    byte gch = row[x * 4 + 1];
                                                    byte r = row[x * 4 + 2];
                                                    byte a = row[x * 4 + 3];
                                                    composedRgba[di++] = r;
                                                    composedRgba[di++] = gch;
                                                    composedRgba[di++] = b;
                                                    composedRgba[di++] = a;
                                                }
                                            }
                                        }
                                        stereoUpdater(composedRgba);
                                    }
                                    finally
                                    {
                                        composite.UnlockBits(data);
                                    }
                                }

                                void OnUnified(byte[] jpeg)
                                {
                                    var sbmp = DecodeJpegToBitmap(jpeg);
                                    latestUnified = sbmp;
                                    if (!streamerReady)
                                    {
                                        int sw = Math.Max(1, sbmp.Width / 2);
                                        int sh = sbmp.Height;
                                        EnsureStreamer(sw, sh);
                                    }
                                    TryComposeAndSend();
                                }

                                var tUnified = new Thread(() => ReadMjpeg(sbsUrl, OnUnified, cts.Token)) { IsBackground = true };
                                tUnified.Start();

                                while (stereoPanel != null)
                                {
                                    Thread.Sleep(50);
                                }

                                cts.Cancel();
                                try { compositeG?.Dispose(); } catch { }
                                try { composite?.Dispose(); } catch { }
                                try { srcUnified32?.Dispose(); } catch { }
                                try { leftUnd?.Dispose(); } catch { }
                                try { rightUnd?.Dispose(); } catch { }
                                compositeG = null; composite = null; composedRgba = null; srcUnified32 = null; leftUnd = null; rightUnd = null;
                            }).Start();
                        }

                        // format selection for unified stereo
                        if (unifiedStereo)
                        {
                            pbv.RadioButtons("Stereo Format", new[] { "SBS-LR", "SBS-RL" }, ref sbsFormatIndex);
                        }

                        // adjust displaying...
                        pbv.SeparatorText("Left Eye");
                        pbv.DragFloat("adjust left eye x", ref lx, 1, -targetWGlobal, targetWGlobal);
                        pbv.DragFloat("adjust left eye y", ref ly, 1, -targetHGlobal, targetHGlobal);
                        pbv.DragFloat("adjust left eye rotation", ref lrot, 0.002f, -2 * PI, 2 * PI);
                        pbv.Separator();
                        pbv.SeparatorText("Left Camera Calibration");
                        pbv.CheckBox("Undistort Left", ref undistortLeft);
                        pbv.DragFloat("L k1", ref lk1, 0.001f, -0.5f, 0.5f);
                        pbv.DragFloat("L k2", ref lk2, 0.001f, -0.5f, 0.5f);
                        pbv.DragFloat("L center offset X (px)", ref lCenterOffsetX, 1f, -targetWGlobal, targetWGlobal);
                        pbv.DragFloat("L center offset Y (px)", ref lCenterOffsetY, 1f, -targetHGlobal, targetHGlobal);
                        pbv.DragFloat("L zoom", ref lZoom, 0.01f, 0.5f, 2.5f);
                        pbv.SeparatorText("Right Eye");
                        pbv.DragFloat("adjust right eye x", ref rx, 1, -targetWGlobal, targetWGlobal);
                        pbv.DragFloat("adjust right eye y", ref ry, 1, -targetHGlobal, targetHGlobal);
                        pbv.DragFloat("adjust right eye rotation", ref rrot, 0.002f, -2 * PI, 2 * PI);
                        pbv.SeparatorText("Right Camera Calibration");
                        pbv.CheckBox("Undistort Right", ref undistortRight);
                        pbv.DragFloat("R k1", ref rk1, 0.001f, -0.5f, 0.5f);
                        pbv.DragFloat("R k2", ref rk2, 0.001f, -0.5f, 0.5f);
                        pbv.DragFloat("R center offset X (px)", ref rCenterOffsetX, 1f, -targetWGlobal, targetWGlobal);
                        pbv.DragFloat("R center offset Y (px)", ref rCenterOffsetY, 1f, -targetHGlobal, targetHGlobal);
                        pbv.DragFloat("R zoom", ref rZoom, 0.01f, 0.5f, 2.5f);
                        pbv.Separator();
                        if (pbv.Button("Save Params")) SaveSettings();
                    });



                }
                else stereoPanel.BringToFront();
            }
            pb.CollapsingHeaderEnd();
        }
    }
}
