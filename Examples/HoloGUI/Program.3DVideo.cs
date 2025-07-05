using System.Collections.Concurrent;
using System.Numerics;
using CycleGUI;
using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using CycleGUI.API;
using Microsoft.VisualBasic.CompilerServices;

namespace HoloExample;

internal static partial class Program
{
    private static bool inited = false;
    private static Panel vpanel = null;
    private static AVHWDeviceType deviceType;
    const int INBUF_SIZE = 4096;
    static AVPixelFormat _hw_pix_fmt = AVPixelFormat.AV_PIX_FMT_NONE;
    private static bool loaded3dv = false, playback = false, repeat = false, seek = false;
    private static int total_frame, cur_frame;
    private static float frame_rate;
    private static Action<byte[]> updater;

    // FFmpeg context struct to manage all related pointers
    private struct FFmpegContext
    {
        public unsafe AVFormatContext* input_ctx;
        public unsafe AVCodecContext* decoder_ctx;
        public unsafe AVCodec* decoder;
        public unsafe AVBufferRef* hw_device_ctx;
        public int video_stream_index;
        public string current_filename;
        public int video_width;
        public int video_height;
    }

    private static FFmpegContext ffmpeg_ctx;
    
    // Frame cache and threading
    private static ConcurrentDictionary<int, byte[]> frame_cache = new ConcurrentDictionary<int, byte[]>();
    private static LinkedList<int> cache_lru = new LinkedList<int>(); // For LRU eviction
    private static readonly int MAX_CACHE_FRAMES = 200; // Limit cache size
    private static Thread playback_thread = null;
    private static bool stop_playback_thread = false;

    // Frame index and packet management
    private static int current_packet_frame = -1;
    private static bool decoder_flushed = false;

    private static void ConfigureHWDecoder(out AVHWDeviceType HWtype)
    {
        HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();

        Console.WriteLine("displaying hardware decoder:");
        var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        var number = 0;

        while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            Console.WriteLine($"{++number}. {type}");
            availableHWDecoders.Add(number, type);
        }

        if (availableHWDecoders.Count == 0)
        {
            Console.WriteLine("Your system have no hardware decoders.");
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            return;
        }

        var decoderNumber = availableHWDecoders
            .SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;
        if (decoderNumber == 0)
            decoderNumber = availableHWDecoders.First().Key;
        Console.WriteLine($"Selected [{decoderNumber}]");
        availableHWDecoders.TryGetValue(decoderNumber, out HWtype);
    }

    public static void Playback(PanelBuilder pb)
    {
        pb.CollapsingHeaderStart("3D Movies (LR)");
        if (pb.Button("Open Video Panel"))
        {
            new SetCamera() { azimuth = -(float)(Math.PI / 2), altitude = (float)(Math.PI/2), lookAt = new Vector3(-0.0720f, 0.1536f, -2.0000f), distance = 2.7000f, world2phy = 200f }.IssueToDefault();
            new SetAppearance() { useGround = false, drawGrid = false, drawGuizmo = false, sun_altitude = 0.00f }.IssueToDefault();
            if (!inited)
            {
                Console.WriteLine("Current directory: " + Environment.CurrentDirectory);
                Console.WriteLine("Running in {0}-bit mode.", Environment.Is64BitProcess ? "64" : "32");

                ffmpeg.RootPath = "D:\\ref\\ffmpeg\\autogen\\FFmpeg.AutoGen\\FFmpeg\\bin\\x64";
                DynamicallyLoadedBindings.Initialize();
                ConfigureHWDecoder(out deviceType);

                Console.WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");
                Console.WriteLine($"LIBAVFORMAT Version: {ffmpeg.LIBAVFORMAT_VERSION_MAJOR}.{ffmpeg.LIBAVFORMAT_VERSION_MINOR}");
                inited = true;
            }

            if (vpanel == null)
            {
                Workspace.AddProp(new PutImage()
                {
                    displayH = 0.9f,
                    displayW = 1.6f,
                    displayType = PutImage.DisplayType.World,
                    name = "Vali",
                    rgbaName = "stream",
                });

                float depth = 2.0f;
                vpanel = GUI.PromptPanel(pbv =>
                {
                    if (pbv.Closing())
                    {
                        vpanel = null;
                        StopPlaybackThread();
                        StopDecoderThread();
                        CleanupVideoDecoder();
                        pbv.Panel.Exit();
                    }

                    if (pbv.DragFloat("Depth", ref depth, 1f, 100f, 500f))
                    {
                        new SetCamera() { azimuth = -(float)(Math.PI / 2), altitude = (float)(Math.PI / 2), lookAt = new Vector3(-0.0720f, 0.1536f, -2.0000f), distance = 2.7000f, world2phy = depth }.IssueToDefault();
                    }
                    if (pbv.Button("Load Video"))
                        if (UITools.FileBrowser("Select video file", out var filename))
                        {
                            LoadVideo(filename);
                            loaded3dv = true;
                        }

                    if (loaded3dv)
                    {
                        pbv.SeparatorText($"Frames:{cur_frame}/{total_frame}, time:{cur_frame/frame_rate:0.0}s, fps:{frame_rate:0.0}");
                        var prog = (float)cur_frame;
                        if (pbv.DragFloat("Progress", ref prog, 1, 0, total_frame))
                        {
                            cur_frame = (int)prog;
                            seek = true;
                        }

                        pbv.CheckBox("Play", ref playback);
                        pbv.CheckBox("Repeat", ref repeat);

                    }
                });
            }
            else vpanel.BringToFront();
        }
        pb.CollapsingHeaderEnd();
    }

    static void LoadVideo(string filename)
    {
        StopPlaybackThread();
        StopDecoderThread();
        CleanupVideoDecoder();
        
        ffmpeg_ctx.current_filename = filename;
        ClearFrameCache();
        cur_frame = 0;
        
        if (InitializeVideoDecoder(filename))
        {
            Console.WriteLine($"Video loaded: {ffmpeg_ctx.video_width}x{ffmpeg_ctx.video_height}, {total_frame} frames, {frame_rate} fps");
            
            // Update PutARGB with correct video dimensions
            var streamer = Workspace.AddProp(new PutARGB()
            {
                height = ffmpeg_ctx.video_height,
                width = ffmpeg_ctx.video_width,
                name = "stream",
                displayType = PutARGB.DisplayType.Stereo3D_LR
            });
            updater = streamer.StartStreaming();

            // Display first frame
            StartDecoderThread();
            StartPlaybackThread();
            DisplayFrame(0);
        }
    }

    static unsafe bool InitializeVideoDecoder(string filename)
    {
        int ret = 0;
        ffmpeg_ctx.input_ctx = null;
        ffmpeg_ctx.decoder_ctx = null;
        ffmpeg_ctx.decoder = null;

        // Open input file
        AVFormatContext* input_ctx_temp = null;
        if (ffmpeg.avformat_open_input(&input_ctx_temp, filename, null, null) < 0)
        {
            Console.WriteLine($"Cannot open input file: {filename}");
            return false;
        }
        ffmpeg_ctx.input_ctx = input_ctx_temp;

        if (ffmpeg.avformat_find_stream_info(ffmpeg_ctx.input_ctx, null) < 0)
        {
            Console.WriteLine("Cannot find input stream information.");
            return false;
        }

        // Find video stream
        AVCodec* decoder_temp = null;
        ret = ffmpeg.av_find_best_stream(ffmpeg_ctx.input_ctx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder_temp, 0);
        if (ret < 0)
        {
            Console.WriteLine("Cannot find a video stream in the input file");
            return false;
        }
        ffmpeg_ctx.decoder = decoder_temp;

        ffmpeg_ctx.video_stream_index = ret;
        var video_stream = ffmpeg_ctx.input_ctx->streams[ffmpeg_ctx.video_stream_index];
        
        // Get video properties
        ffmpeg_ctx.video_width = video_stream->codecpar->width;
        ffmpeg_ctx.video_height = video_stream->codecpar->height;
        frame_rate = (float)video_stream->r_frame_rate.num / video_stream->r_frame_rate.den;
        total_frame = (int)video_stream->nb_frames;
        
        // If nb_frames is not available, estimate from duration and frame rate
        if (total_frame <= 0 && video_stream->duration > 0)
        {
            var duration_sec = (double)video_stream->duration * video_stream->time_base.num / video_stream->time_base.den;
            total_frame = (int)(duration_sec * frame_rate);
        }

        if (total_frame <= 0 && (*ffmpeg_ctx.input_ctx).duration > 0)
        {
            var duration_sec = (double)ffmpeg_ctx.input_ctx->duration / ffmpeg.AV_TIME_BASE;
            total_frame = (int)(duration_sec * frame_rate);
        }


        // Configure hardware decoder
        for (int i = 0; ; i++)
        {
            AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(ffmpeg_ctx.decoder, i);
            if (config == null)
            {
                string? decoderName = Marshal.PtrToStringUTF8(new IntPtr(ffmpeg_ctx.decoder->name));
                Console.WriteLine($"Decoder {decoderName} does not support device type {ffmpeg.av_hwdevice_get_type_name(deviceType)}");
                break;
            }

            if ((config->methods & 0x01) == 0x01 && config->device_type == deviceType)
            {
                _hw_pix_fmt = config->pix_fmt;
                break;
            }
        }

        // Initialize decoder context
        ffmpeg_ctx.decoder_ctx = ffmpeg.avcodec_alloc_context3(ffmpeg_ctx.decoder);
        if (ffmpeg_ctx.decoder_ctx == null)
        {
            Console.WriteLine("Could not allocate video codec context");
            return false;
        }

        if (ffmpeg.avcodec_parameters_to_context(ffmpeg_ctx.decoder_ctx, video_stream->codecpar) < 0)
        {
            return false;
        }

        ffmpeg_ctx.decoder_ctx->get_format = (AVCodecContext_get_format_func)get_hw_format;

        if (hw_decoder_init(ffmpeg_ctx.decoder_ctx, deviceType) < 0)
        {
            return false;
        }

        if ((ret = ffmpeg.avcodec_open2(ffmpeg_ctx.decoder_ctx, ffmpeg_ctx.decoder, null)) < 0)
        {
            Console.WriteLine($"Failed to open codec for stream {ffmpeg_ctx.video_stream_index}");
            return false;
        }

        return true;
    }

    static unsafe (byte[], string why) DecodeFrameAt(int target_frame)
    {
        if (target_frame < 0 || target_frame >= total_frame)
            return (null, "frame out of range");

        // Check if we can decode sequentially from current position
        bool can_decode_sequentially = current_packet_frame >= 0 && 
                                     target_frame >= current_packet_frame &&
                                     target_frame - current_packet_frame < 10;

        if (!can_decode_sequentially)
        {
            // Need to seek - calculate timestamp for target frame
            double frame_time = (double)target_frame / frame_rate;
            long seek_timestamp = (long)(frame_time * ffmpeg_ctx.input_ctx->streams[ffmpeg_ctx.video_stream_index]->time_base.den / 
                                        ffmpeg_ctx.input_ctx->streams[ffmpeg_ctx.video_stream_index]->time_base.num);
            
            // Seek to a bit before the target to ensure we don't overshoot
            int seek_frame = Math.Max(0, target_frame - 5);
            double seek_time = (double)seek_frame / frame_rate;
            seek_timestamp = (long)(seek_time * ffmpeg_ctx.input_ctx->streams[ffmpeg_ctx.video_stream_index]->time_base.den / 
                                   ffmpeg_ctx.input_ctx->streams[ffmpeg_ctx.video_stream_index]->time_base.num);

            if (ffmpeg.av_seek_frame(ffmpeg_ctx.input_ctx, ffmpeg_ctx.video_stream_index, seek_timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD) < 0)
            {
                return (null, $"Failed to seek to frame {target_frame}");
            }

            // Flush decoder after seeking
            ffmpeg.avcodec_flush_buffers(ffmpeg_ctx.decoder_ctx);
            decoder_flushed = true;
            // Set current position to where we seeked to (approximately)
            current_packet_frame = seek_frame - 1; // -1 because we'll increment when we decode
        }

        // Read and decode frames until we reach the target
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        byte[] result = null;
        int packets_read = 0;

        try
        {
            while (packets_read < 100) // Safety limit
            {
                if (ffmpeg.av_read_frame(ffmpeg_ctx.input_ctx, packet) < 0)
                {
                    break; // End of file
                }
                
                packets_read++;

                if (packet->stream_index != ffmpeg_ctx.video_stream_index)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                // Send packet to decoder
                if (ffmpeg.avcodec_send_packet(ffmpeg_ctx.decoder_ctx, packet) < 0)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                // Try to receive decoded frames
                while (ffmpeg.avcodec_receive_frame(ffmpeg_ctx.decoder_ctx, frame) == 0)
                {
                    // Increment frame position for each decoded frame
                    current_packet_frame++;
                    
                    if (current_packet_frame == target_frame)
                    {
                        result = ConvertFrameToRGBA(frame);
                        ffmpeg.av_packet_unref(packet);
                        return (result, null);
                    }
                    
                    // If we've gone past the target, we missed it
                    if (current_packet_frame > target_frame)
                    {
                        ffmpeg.av_packet_unref(packet);
                        return (null, $"Overshot target frame {target_frame}, got {current_packet_frame}");
                    }
                }

                ffmpeg.av_packet_unref(packet);
            }
            
            return (null, $"Could not decode frame {target_frame} after reading {packets_read} packets");
        }
        finally
        {
            var tmp_packet = packet;
            ffmpeg.av_packet_free(&tmp_packet);
            var tmp_frame = frame;
            ffmpeg.av_frame_free(&tmp_frame);
        }
    }

    static void StartPlaybackThread()
    {
        stop_playback_thread = false;
        
        playback_thread = new Thread(() =>
        {
            var playback_start_time = DateTime.Now.AddSeconds(1);
            bool prev_play = playback;
            var st_frame = 0;
            var last_frame = -1;
            while (!stop_playback_thread)
            {
                DateTime current_time = DateTime.Now;

                if (!prev_play && playback || seek)
                {
                    playback_start_time = DateTime.Now;
                    st_frame = cur_frame;
                }
                prev_play = playback;

                if (playback && !seek)
                {
                    // Calculate which frame should be displayed based on elapsed time
                    var elapsed_time = current_time - playback_start_time;
                    var elapsed_frames = (int)(elapsed_time.TotalSeconds * frame_rate);
                    cur_frame = st_frame + elapsed_frames;
                }
                    

                seek = false;

                // Check if we've reached the end
                if (cur_frame >= total_frame)
                {
                    if (repeat)
                    {
                        st_frame = cur_frame = 0;
                        playback_start_time = DateTime.Now;
                        continue;
                    }

                    cur_frame = total_frame - 1;
                    playback = false;
                }
                
                if (last_frame != cur_frame)
                {
                    last_frame = cur_frame;
                    DisplayFrame(cur_frame);
                }

                // Sleep for a short interval to check timing again
                // Use small intervals for better responsiveness
                Thread.Sleep(8); // ~120fps check rate, much smaller than typical video frame rates
            }
        })
        { IsBackground = true };
        
        playback_thread.Start();
    }

    static void StopPlaybackThread()
    {
        stop_playback_thread = true;
        if (playback_thread != null && playback_thread.IsAlive)
        {
            playback_thread.Join(1000); // Wait up to 1 second
        }
    }

    static void DisplayFrame(int frame_number)
    {
        try
        {
            vpanel?.Repaint();
            byte[] frame_data = GetFrame(frame_number);
            if (frame_data != null && updater != null)
            {
                updater(frame_data);
            }
            else
            {
                //Console.WriteLine($"DisplayFrame {frame_number}: frame_data is null or updater is null");
            }
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"Error displaying frame {frame_number}: {ex.Message}");
        }
    }

    private static int caching_frame = -1;
    private static int getting_frame = -1;
    
    static byte[] GetFrame(int frame_number)
    {
        getting_frame = frame_number;
        while (true)
        {
            if (!frame_cache.ContainsKey(frame_number))
            {
                caching_frame = frame_number;
                Thread.Sleep(0);
                continue;
            }
            return frame_cache[frame_number];
        }
    }

    // Background decoder thread
    private static Thread decoder_thread = null;
    private static bool stop_decoder_thread = false;
    private static int last_decoder_curframe = -1;
    private static readonly int DECODE_AHEAD_FRAMES = 30;

    static void StartDecoderThread()
    {
        stop_decoder_thread = false;

        decoder_thread = new Thread(() =>
        {
            while (!stop_decoder_thread)
            {
                bool decoded_something = false;

                int target_frame = getting_frame;
                // Decode up to DECODE_AHEAD_FRAMES frames ahead of current frame
                for (int i = 0; i <= DECODE_AHEAD_FRAMES; i++, target_frame++)
                {
                    // Check if curframe changed during this loop
                    if (caching_frame != -1)
                    {
                        //Console.WriteLine($"caching:{caching_frame}");
                        i = 0;
                        target_frame = caching_frame;
                        caching_frame = -1;
                    }

                    // Don't decode beyond total frames
                    if (target_frame >= total_frame)
                        break;

                    // Check if frame is already cached
                    if (frame_cache.ContainsKey(target_frame))
                        continue; // Skip already cached frames

                    // Decode the frame
                    var (frame_data, why) = DecodeFrameAt(target_frame);
                    if (frame_data == null)
                        Console.WriteLine($"Cached {target_frame} null?{why}");
                    AddToCache(target_frame, frame_data);
                    decoded_something = true;
                }

                Thread.Sleep(!decoded_something ? 100 : 1);
            }
        }) { Name = "Decoder" };

        decoder_thread.Start();
    }
    static void StopDecoderThread()
    {
        stop_decoder_thread = true;
        if (decoder_thread != null && decoder_thread.IsAlive)
        {
            decoder_thread.Join(1000); // Wait up to 1 second
        }
    }

    static unsafe byte[] ConvertFrameToRGBA(AVFrame* frame)
    {
        AVFrame* sw_frame = null;
        AVFrame* target_frame = frame;
        
        try
        {
            // If this is a hardware frame, transfer it to software
            if (frame->format == (int)_hw_pix_fmt)
            {
                sw_frame = ffmpeg.av_frame_alloc();
                if (sw_frame == null)
                {
                    Console.WriteLine("ConvertFrameToRGBA: Failed to allocate software frame");
                    return null;
                }
                
                int ret = ffmpeg.av_hwframe_transfer_data(sw_frame, frame, 0);
                if (ret < 0)
                {
                    Console.WriteLine($"ConvertFrameToRGBA: Failed to transfer hardware frame to software: {ret}");
                    return null;
                }
                
                target_frame = sw_frame;
                // Console.WriteLine($"ConvertFrameToRGBA: Transferred HW frame (format {frame->format}) to SW frame (format {target_frame->format})");
            }
            
            // Create RGBA conversion context
            var sws_ctx = ffmpeg.sws_getContext(
                target_frame->width, target_frame->height, (AVPixelFormat)target_frame->format,
                target_frame->width, target_frame->height, AVPixelFormat.AV_PIX_FMT_RGBA,
                ffmpeg.SWS_BILINEAR, null, null, null);

            if (sws_ctx == null)
            {
                Console.WriteLine("ConvertFrameToRGBA: Failed to create scaling context");
                return null;
            }

            try
            {
                // Allocate RGBA buffer
                int rgba_size = target_frame->width * target_frame->height * 4;
                byte[] rgba_data = new byte[rgba_size];
                
                // Create source arrays from frame data
                byte*[] src_data = new byte*[4];
                int[] src_linesize = new int[4];
                for (uint i = 0; i < 4; i++)
                {
                    src_data[i] = target_frame->data[i];
                    src_linesize[i] = target_frame->linesize[i];
                }
                
                // Create destination arrays
                byte*[] dst_data = new byte*[4];
                int[] dst_linesize = new int[4];
                
                fixed (byte* rgba_ptr = rgba_data)
                {
                    dst_data[0] = rgba_ptr;
                    dst_data[1] = null;
                    dst_data[2] = null;
                    dst_data[3] = null;
                    dst_linesize[0] = target_frame->width * 4;
                    dst_linesize[1] = 0;
                    dst_linesize[2] = 0;
                    dst_linesize[3] = 0;

                    int scale_result = ffmpeg.sws_scale(sws_ctx,
                        src_data, src_linesize, 0, target_frame->height,
                        dst_data, dst_linesize);
                    
                    if (scale_result != target_frame->height)
                    {
                        Console.WriteLine($"ConvertFrameToRGBA: sws_scale returned {scale_result}, expected {target_frame->height}");
                        return null;
                    }
                }

                //Console.WriteLine($"ConvertFrameToRGBA: Successfully converted frame {target_frame->width}x{target_frame->height} format {target_frame->format} to RGBA");
                return rgba_data;
            }
            finally
            {
                ffmpeg.sws_freeContext(sws_ctx);
            }
        }
        finally
        {
            if (sw_frame != null)
            {
                var tmp_sw_frame = sw_frame;
                ffmpeg.av_frame_free(&tmp_sw_frame);
            }
        }
    }

    static unsafe void CleanupVideoDecoder()
    {
        // Reset decoder state
        current_packet_frame = -1;
        decoder_flushed = false;
        
        // Clear frame cache
        frame_cache.Clear();
        cache_lru.Clear();
        
        if (ffmpeg_ctx.decoder_ctx != null)
        {
            AVCodecContext* ctx = ffmpeg_ctx.decoder_ctx;
            ffmpeg.avcodec_free_context(&ctx);
            ffmpeg_ctx.decoder_ctx = null;
        }

        if (ffmpeg_ctx.input_ctx != null)
        {
            AVFormatContext* ctx = ffmpeg_ctx.input_ctx;
            ffmpeg.avformat_close_input(&ctx);
            ffmpeg_ctx.input_ctx = null;
        }

        if (ffmpeg_ctx.hw_device_ctx != null)
        {
            AVBufferRef* hw_ctx = ffmpeg_ctx.hw_device_ctx;
            ffmpeg.av_buffer_unref(&hw_ctx);
            ffmpeg_ctx.hw_device_ctx = null;
        }
    }

    static unsafe int hw_decoder_init(AVCodecContext* ctx, AVHWDeviceType type)
    {
        int err = 0;

        AVBufferRef* hw_device_ctx_temp = null;
        if ((err = ffmpeg.av_hwdevice_ctx_create(&hw_device_ctx_temp, type, null, null, 0)) < 0)
            {
                Console.WriteLine("Failed to create specified HW device.");
                return err;
            }

        ffmpeg_ctx.hw_device_ctx = hw_device_ctx_temp;
        ctx->hw_device_ctx = ffmpeg.av_buffer_ref(ffmpeg_ctx.hw_device_ctx);

        return err;
    }

    static unsafe AVPixelFormat get_hw_format(AVCodecContext* ctx, AVPixelFormat* pix_fmts)
    {
        AVPixelFormat* p;

        for (p = pix_fmts; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
        {
            if (*p == _hw_pix_fmt)
            {
                return *p;
            }
        }

        Console.WriteLine("Failed to get HW surface format.");
        return AVPixelFormat.AV_PIX_FMT_NONE;
    }

    static void AddToCache(int frame_number, byte[] frame_data)
    {
        // Add to cache
        frame_cache[frame_number] = frame_data;
        cache_lru.AddFirst(frame_number);
        
        // Evict oldest frames if cache is too large
        while (frame_cache.Count > MAX_CACHE_FRAMES)
        {
            var oldest_frame = cache_lru.Last.Value;
            cache_lru.RemoveLast();
            frame_cache.TryRemove(oldest_frame, out _);
        }
    }

    static void ClearFrameCache()
    {
        lock (frame_cache)
        {
            frame_cache.Clear();
            cache_lru.Clear();
        }
    }
}