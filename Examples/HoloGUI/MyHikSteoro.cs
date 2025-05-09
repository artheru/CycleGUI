using CycleGUI;
using CycleGUI.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using static HoloExample.AngstrongHp60c;
using static System.Formats.Asn1.AsnWriter;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace HoloExample
{
    internal class MyHikSteoro
    {
        private Panel wp;
        private Vector3 left, right;
        private ret cur;
        private float rotateX, rotateY, rotateZ, translateX, translateY, translateZ, scale = 1;


        // Structure to hold the eye position data
        [StructLayout(LayoutKind.Sequential)]
        public struct EyePositionData
        {
            // Left eye position
            public float LeftX;
            public float LeftY;
            public float LeftZ;

            // Right eye position  
            public float RightX;
            public float RightY;
            public float RightZ;
        }

        public static T FromBytes<T>(byte[] data) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                handle.Free();
            }
        }

        // Name of the memory-mapped file (must match Python code)
        private const string SHARED_MEMORY_NAME = "hik_stereo_eye_tracking";

        // Size of the memory-mapped file (6 floats * 4 bytes)
        private const int SHARED_MEMORY_SIZE = 24;
        private FileSystemWatcher fsw;

        public void Polling()
        {
            // var path = "D:\\work\\test_pupil\\test_openseeface\\OpenSeeFace\\";
            // var fn = "hik_stereo_eye_tracking";
            var path = "D:\\work\\test_pupil\\tfjs";
            var fn = "mediapipe_stereo_eye_tracking";

            fsw = new(path);
            fsw.Filter = fn;

            fsw.NotifyFilter = NotifyFilters.LastWrite;

            DateTime lasTime=DateTime.Now;
            int frames = 0;
            fsw.Changed += (sender, args) =>
            {
                try
                {
                    var bytes = File.ReadAllBytes(Path.Combine(path, fn));
                    var eyeData = FromBytes<EyePositionData>(bytes);

                    left = new Vector3(-eyeData.LeftX, eyeData.LeftY, eyeData.LeftZ);
                    right = new Vector3(-eyeData.RightX, eyeData.RightY, eyeData.RightZ);


                    // for each eye, rotate and translate and scale.
                    // Create rotation matrix from Euler angles
                    var rotMatrix = Matrix4x4.CreateRotationX(rotateX) *
                                    Matrix4x4.CreateRotationY(rotateY) *
                                    Matrix4x4.CreateRotationZ(rotateZ);

                    // Transform left eye position
                    var transformedLeft = Vector3.Transform(left * scale, rotMatrix);
                    transformedLeft += new Vector3(translateX, translateY, translateZ);

                    // Transform right eye position  
                    var transformedRight = Vector3.Transform(right * scale, rotMatrix);
                    transformedRight += new Vector3(translateX, translateY, translateZ);

                    left = transformedLeft;
                    right = transformedRight;

                    new SetHoloViewEyePosition
                    {
                        leftEyePos = left,
                        rightEyePos = right
                    }.IssueToDefault();

                    wp.Repaint();
                    frames += 1;
                    if (lasTime.AddSeconds(1) < DateTime.Now)
                    {
                        Console.WriteLine($"Eye FPS={frames/((DateTime.Now-lasTime).TotalSeconds):0.00}");
                        lasTime=DateTime.Now;
                        frames = 0;
                    }
                }
                catch
                {
                }
            };
            fsw.EnableRaisingEvents = true;
        }

        public MyHikSteoro()
        {

            // Load calibration if exists
            if (File.Exists("hik_stereo_eyetracker_calibration.json"))
            {
                try
                {
                    string json = File.ReadAllText("hik_stereo_eyetracker_calibration.json");
                    var calibration = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
                    rotateX = calibration.GetProperty("rotateX").GetSingle();
                    rotateY = calibration.GetProperty("rotateY").GetSingle();
                    rotateZ = calibration.GetProperty("rotateZ").GetSingle();
                    translateX = calibration.GetProperty("translateX").GetSingle();
                    translateY = calibration.GetProperty("translateY").GetSingle();
                    translateZ = calibration.GetProperty("translateZ").GetSingle();
                    scale = calibration.GetProperty("scale").GetSingle();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading calibration: {ex.Message}");
                }
            }

            wp = GUI.PromptPanel(pb =>
            {
                pb.Separator();
                // rotate and translate and scale params:
                pb.DragFloat("rotate X", ref rotateX, 0.001f);
                pb.DragFloat("rotate Y", ref rotateY, 0.001f);
                pb.DragFloat("rotate Z", ref rotateZ, 0.001f);
                pb.DragFloat("translate X", ref translateX, 0.03f);
                pb.DragFloat("translate Y", ref translateY, 0.03f);
                pb.DragFloat("translate Z", ref translateZ, 0.03f);
                pb.DragFloat("scale", ref scale, 0.0001f);

                // save to file.
                if (pb.Button("Save Calibration"))
                {
                    var calibration = new
                    {
                        rotateX,
                        rotateY,
                        rotateZ,
                        translateX,
                        translateY,
                        translateZ,
                        scale
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(calibration, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText("hik_stereo_eyetracker_calibration.json", json);
                }

                pb.Separator();
                pb.Label($"left v3:{left.X}, {left.Y}, {left.Z}");
                pb.Label($"right v3:{right.X}, {right.Y}, {right.Z}");
            });

            new Thread(Polling){Name = "polling"}.Start();
        }
    }
}
