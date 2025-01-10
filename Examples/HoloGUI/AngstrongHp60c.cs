using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CycleGUI;
using CycleGUI.API;
using Newtonsoft.Json;

namespace HoloExample
{
    public class AngstrongHp60c
    {


        enum AS_FRAME_Type
        {
            DEPTH = 0,
            RGB,
            IR,
            POINTCLOUD,
            YUYV,
            PEAK,
            BUTT
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AS_POINTCLOUD
        {
            public double Angle;
            public double Distance;
            public double Quality;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CAM_DATA
        {
            public CAM_FRAME rawImg;
            public CAM_FRAME depthImg;
            public CAM_FRAME rgbImg;
            public CAM_FRAME irImg;
            public CAM_FRAME tofImg;
            public CAM_FRAME peakImg;
            public CAM_FRAME noiseImg;
            public CAM_FRAME multishotImg;
            public CAM_FRAME pointCloud;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CAM_DATA_NEW
        {
            public CAM_FRAME depthImg;
            public CAM_FRAME rgbImg;
            public CAM_FRAME irImg;
            public CAM_FRAME pointCloud;
            public CAM_FRAME yuyvImg;
            public CAM_FRAME peakImg;
            // public List<AS_POINTCLOUD> PointCloud2;

        }
        [StructLayout(LayoutKind.Sequential)]
        unsafe struct CAM_FRAME
        {
            AS_FRAME_Type type;
            public uint width;
            public uint height;
            public void* data;
            public uint size;
            public uint bufferSize;
            public uint frameId;
            public ulong ts;
        }

        public delegate void FPtr(IntPtr pCamera, IntPtr pstData, IntPtr privateData);

        public float[] cache;
        public FPtr fptr;
        //
        private const float fx = 519.010498f;
        private const float fy = 518.566040f;
        private const float cx = 316.864807f;
        private const float cy = 241.339005f;
        // private const float fx = 425f;
        // private const float fy = 425f;
        // private const float cx = 320;
        // private const float cy = 270f;

        public uint lastId = 0;

        int width, height;

        public struct CameraPoint3D
        {
            public float depth, X, Y, Z;
        }

        private HttpClient hc = new();
        private int frame = 0;
        private DateTime lastFpsUpdate = DateTime.Now;
        private float currentFps = 0;

        public class pix_pos
        {
            public float x, y;
        }

        public class ret
        {
            public pix_pos righteye, lefteye;
        }
        
        public unsafe void GetData(IntPtr pCamera, IntPtr pstData, IntPtr privateData)
        {
            frame++;
            
            // Update FPS every second
            var now = DateTime.Now;
            var timeDiff = (now - lastFpsUpdate).TotalSeconds;
            if (timeDiff >= 1.0)
            {
                currentFps = frame / (float)timeDiff;
                frame = 0;
                lastFpsUpdate = now;
                Console.WriteLine($"FPS: {currentFps:F1}");
            }
            
            var camData = (CAM_DATA_NEW)Marshal.PtrToStructure(pstData, typeof(CAM_DATA_NEW));
            width = (int)camData.depthImg.width;
            height = (int)camData.depthImg.height;
            if (cache == null) cache = new float[width * height];
            // Console.WriteLine($"{width}{height}  {camData.rgbImg.frameId}");
            //if (camData.depthImg.frameId == lastId) return;
            lastId = camData.depthImg.frameId;

            var cachedPoints = new Vector3[width * height];
            var rgb = new byte[width * height * 3];
            for (var y = 0; y < height; ++y)
            {
                for (var x = 0; x < width; ++x)
                {
                    var k = y * width + x;

                    rgb[k * 3 + 0] = ((byte*)camData.rgbImg.data)[y * width * 3 + x * 3 + 0];
                    rgb[k * 3 + 1] = ((byte*)camData.rgbImg.data)[y * width * 3 + x * 3 + 1]; 
                    rgb[k * 3 + 2] = ((byte*)camData.rgbImg.data)[y * width * 3 + x * 3 + 2];

                    var depth = ((ushort*)camData.depthImg.data)[k];
                    var xyzX = depth;
                    var xyzY = -((float)x - cx) * xyzX / fx;
                    var xyzZ = -((float)y - cy) * xyzX / fy;

                    // fix dir.
                    cachedPoints[k].X = xyzY;
                    cachedPoints[k].Y = -xyzZ;
                    cachedPoints[k].Z = xyzX;//xyzZ;
                }
            }

            try
            {
                var str = hc.PostAsync($"http://127.0.0.1:9999/perform?w={width}&h={height}", new ByteArrayContent(rgb))
                    .Result
                    .Content.ReadAsStringAsync().Result;
                var ret = JsonConvert.DeserializeObject<ret>(str);
                
                if (ret.lefteye != null)
                {
                    cur = ret;
                    // Apply 3x3 median filter for left eye, component by component
                    var leftXs = new List<float>();
                    var leftYs = new List<float>();
                    var leftZs = new List<float>();
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            int x = (int)cur.lefteye.x + dx;
                            int y = (int)cur.lefteye.y + dy;
                            if (x >= 0 && x < width && y >= 0 && y < height) {
                                var point = cachedPoints[y * width + x];
                                leftXs.Add(point.X);
                                leftYs.Add(point.Y);
                                leftZs.Add(point.Z);
                            }
                        }
                    }
                    left = new Vector3(
                        leftXs.OrderBy(x => x).Skip(leftXs.Count / 2).First(),
                        leftYs.OrderBy(y => y).Skip(leftYs.Count / 2).First(),
                        leftZs.OrderBy(z => z).Skip(leftZs.Count / 2).First()
                    );

                    // Apply 3x3 median filter for right eye, component by component
                    var rightXs = new List<float>();
                    var rightYs = new List<float>();
                    var rightZs = new List<float>();
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            int x = (int)cur.righteye.x + dx;
                            int y = (int)cur.righteye.y + dy;
                            if (x >= 0 && x < width && y >= 0 && y < height) {
                                var point = cachedPoints[y * width + x];
                                rightXs.Add(point.X);
                                rightYs.Add(point.Y);
                                rightZs.Add(point.Z);
                            }
                        }
                    }
                    right = new Vector3(
                        rightXs.OrderBy(x => x).Skip(rightXs.Count / 2).First(),
                        rightYs.OrderBy(y => y).Skip(rightYs.Count / 2).First(),
                        rightZs.OrderBy(z => z).Skip(rightZs.Count / 2).First()
                    );
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

                    // Update eye positions in workspace
                    new SetHoloViewEyePosition {
                        leftEyePos = left,
                        rightEyePos = right
                    }.IssueToDefault();

                    wp.Repaint();
                }
            }
            catch
            {
                Console.WriteLine($"{frame} offline...");
            }
        }

        [DllImport("AngStrongAPI.dll")]
        public static extern int StartStream(FPtr func);

        private Panel wp;
        private Vector3 left, right;
        private ret cur;
        private float rotateX, rotateY, rotateZ, translateX, translateY, translateZ, scale=1;
        public AngstrongHp60c()
        {
            // Load calibration if exists
            if (File.Exists("eyetracker_calibration.json"))
            {
                try
                {
                    string json = File.ReadAllText("eyetracker_calibration.json");
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
                pb.Label($"left:{cur?.lefteye.x}, {cur?.lefteye.y}");
                pb.Label($"right:{cur?.righteye.x}, {cur?.righteye.y}");
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
                    File.WriteAllText("eyetracker_calibration.json", json);
                }

                pb.Separator();
                pb.Label($"left v3:{left.X}, {left.Y}, {left.Z}");
                pb.Label($"right v3:{right.X}, {right.Y}, {right.Z}");
            });
            StartStream(fptr = GetData);
            //Thread t = new Thread(() => { StartStream(fptr = GetData); });
        }
    }
}
