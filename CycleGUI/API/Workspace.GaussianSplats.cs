using System;
using System.Numerics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CycleGUI.API
{
    /// <summary>
    /// 3D Gaussian Splat - represents a single 3D Gaussian
    /// </summary>
    public struct GaussianSplat
    {
        // Position (x, y, z)
        public Vector3 position;
        
        // Rotation as quaternion (w, x, y, z)
        public Quaternion rotation;
        
        // Scale (sx, sy, sz) - determines ellipsoid shape
        public Vector3 scale;
        
        // Opacity (alpha)
        public float opacity;
        
        // Color - RGB or SH coefficients
        // DC component (base color)
        public Vector3 color_dc;  // RGB for SH degree 0
        
        // Optional: Higher order Spherical Harmonics coefficients
        public float[] sh_coefficients; // Can be null for simple colored splats
    }

    /// <summary>
    /// 4D Gaussian Splat - adds temporal dimension
    /// </summary>
    public struct GaussianSplat4D
    {
        public GaussianSplat baseGaussian;
        
        // Temporal properties
        public float time;           // Time stamp
        public Vector3 velocity;     // Motion vector
        public Vector3 acceleration; // Optional acceleration
    }

    /// <summary>
    /// Load and display 3D Gaussian Splats
    /// </summary>
    public class PutGaussianSplats : WorkspaceProp
    {
        public GaussianSplat[] splats;
        
        // Optional: LOD (Level of Detail) - downsample for performance
        public int maxSplats = -1; // -1 = no limit
        
        // Rendering options
        public float globalOpacityScale = 1.0f;
        public float globalSizeScale = 1.0f;
        
        // Bounding box for culling
        public Vector3 boundingBoxMin = new Vector3(-1000, -1000, -1000);
        public Vector3 boundingBoxMax = new Vector3(1000, 1000, 1000);

        /// <summary>
        /// Load from PLY file (standard 3DGS format)
        /// </summary>
        public static PutGaussianSplats FromPLY(string plyFilePath, string name)
        {
            var result = new PutGaussianSplats { name = name };
            
            // Parse PLY file
            var splatsList = new List<GaussianSplat>();
            
            using (var reader = new StreamReader(plyFilePath))
            {
                string line;
                bool inHeader = true;
                int vertexCount = 0;
                var properties = new List<string>();
                
                // Parse header
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("element vertex"))
                    {
                        vertexCount = int.Parse(line.Split(' ')[2]);
                    }
                    else if (line.StartsWith("property"))
                    {
                        var parts = line.Split(' ');
                        properties.Add(parts[2]); // property name
                    }
                    else if (line == "end_header")
                    {
                        inHeader = false;
                        break;
                    }
                }
                
                // Parse data (binary or ASCII)
                // For simplicity, assume ASCII format
                for (int i = 0; i < vertexCount; i++)
                {
                    line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) continue;
                    
                    var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(float.Parse).ToArray();
                    
                    var splat = ParsePLYVertex(values, properties);
                    splatsList.Add(splat);
                }
            }
            
            result.splats = splatsList.ToArray();
            return result;
        }
        
        private static GaussianSplat ParsePLYVertex(float[] values, List<string> properties)
        {
            var splat = new GaussianSplat
            {
                position = Vector3.Zero,
                rotation = Quaternion.Identity,
                scale = Vector3.One,
                opacity = 1.0f,
                color_dc = Vector3.One
            };
            
            for (int i = 0; i < Math.Min(values.Length, properties.Count); i++)
            {
                var prop = properties[i];
                var val = values[i];
                
                // Position
                if (prop == "x") splat.position.X = val;
                else if (prop == "y") splat.position.Y = val;
                else if (prop == "z") splat.position.Z = val;
                
                // Color (DC component of SH)
                else if (prop == "f_dc_0") splat.color_dc.X = val;
                else if (prop == "f_dc_1") splat.color_dc.Y = val;
                else if (prop == "f_dc_2") splat.color_dc.Z = val;
                
                // Opacity
                // else if (prop == "opacity") splat.opacity = 1.0f / (1.0f + MathF.Exp(-val)); // sigmoid
                //
                // // Scale
                // else if (prop == "scale_0") splat.scale.X = MathF.Exp(val);
                // else if (prop == "scale_1") splat.scale.Y = MathF.Exp(val);
                // else if (prop == "scale_2") splat.scale.Z = MathF.Exp(val);
                
                // Rotation (quaternion)
                else if (prop == "rot_0") splat.rotation.X = val;
                else if (prop == "rot_1") splat.rotation.Y = val;
                else if (prop == "rot_2") splat.rotation.Z = val;
                else if (prop == "rot_3") splat.rotation.W = val;
            }
            
            // Normalize quaternion
            splat.rotation = Quaternion.Normalize(splat.rotation);
            
            return splat;
        }
        
        /// <summary>
        /// Create from simple point cloud (auto-generate Gaussians)
        /// </summary>
        public static PutGaussianSplats FromPointCloud(Vector3[] positions, Vector3[] colors, float defaultSize = 0.01f, string name = "gaussian_splats")
        {
            var splats = new GaussianSplat[positions.Length];
            
            for (int i = 0; i < positions.Length; i++)
            {
                splats[i] = new GaussianSplat
                {
                    position = positions[i],
                    rotation = Quaternion.Identity,
                    scale = new Vector3(defaultSize),
                    opacity = 1.0f,
                    color_dc = colors != null && i < colors.Length ? colors[i] : Vector3.One
                };
            }
            
            return new PutGaussianSplats
            {
                name = name,
                splats = splats
            };
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(67); // Command ID for 3D Gaussian Splats
            cb.Append(name);
            
            // Options
            cb.Append(globalOpacityScale);
            cb.Append(globalSizeScale);
            
            // Splat count
            int count = maxSplats > 0 ? Math.Min(splats.Length, maxSplats) : splats.Length;
            cb.Append(count);
            
            // Serialize each splat
            for (int i = 0; i < count; i++)
            {
                var s = splats[i];
                
                // Position
                cb.Append(s.position.X);
                cb.Append(s.position.Y);
                cb.Append(s.position.Z);
                
                // Rotation (quaternion)
                cb.Append(s.rotation.X);
                cb.Append(s.rotation.Y);
                cb.Append(s.rotation.Z);
                cb.Append(s.rotation.W);
                
                // Scale
                cb.Append(s.scale.X);
                cb.Append(s.scale.Y);
                cb.Append(s.scale.Z);
                
                // Opacity
                cb.Append(s.opacity);
                
                // Color (DC)
                cb.Append(s.color_dc.X);
                cb.Append(s.color_dc.Y);
                cb.Append(s.color_dc.Z);
                
                // SH coefficients count (0 for now, can extend later)
                cb.Append(0);
            }
        }

        internal override void Submit()
        {
            SubmitReversible($"gaussians#{name}");
        }

        public override void Remove()
        {
            RemoveProp($"gaussians#{name}", name);
        }
    }

    /// <summary>
    /// Load and display 4D Gaussian Splats (with temporal dimension)
    /// </summary>
    public class PutGaussianSplats4D : WorkspaceProp
    {
        public GaussianSplat4D[] splats4d;
        
        // Time control
        public float currentTime = 0.0f;
        public float timeScale = 1.0f;
        public bool loop = true;
        
        // Rendering options
        public float globalOpacityScale = 1.0f;
        public float globalSizeScale = 1.0f;
        
        /// <summary>
        /// Create from animated point cloud
        /// </summary>
        public static PutGaussianSplats4D FromAnimatedPointCloud(
            Vector3[] positions, 
            Vector3[] colors, 
            Vector3[] velocities,
            float[] timestamps,
            float defaultSize = 0.01f,
            string name = "gaussian_splats_4d")
        {
            var splats = new GaussianSplat4D[positions.Length];
            
            for (int i = 0; i < positions.Length; i++)
            {
                splats[i] = new GaussianSplat4D
                {
                    baseGaussian = new GaussianSplat
                    {
                        position = positions[i],
                        rotation = Quaternion.Identity,
                        scale = new Vector3(defaultSize),
                        opacity = 1.0f,
                        color_dc = colors != null && i < colors.Length ? colors[i] : Vector3.One
                    },
                    time = timestamps != null && i < timestamps.Length ? timestamps[i] : 0,
                    velocity = velocities != null && i < velocities.Length ? velocities[i] : Vector3.Zero
                };
            }
            
            return new PutGaussianSplats4D
            {
                name = name,
                splats4d = splats
            };
        }

        protected internal override void Serialize(CB cb)
        {
            cb.Append(68); // Command ID for 4D Gaussian Splats
            cb.Append(name);
            
            // Time control
            cb.Append(currentTime);
            cb.Append(timeScale);
            cb.Append(loop);
            
            // Options
            cb.Append(globalOpacityScale);
            cb.Append(globalSizeScale);
            
            // Splat count
            cb.Append(splats4d.Length);
            
            // Serialize each 4D splat
            for (int i = 0; i < splats4d.Length; i++)
            {
                var s = splats4d[i];
                var g = s.baseGaussian;
                
                // 3D Gaussian part (same as 3D)
                cb.Append(g.position.X);
                cb.Append(g.position.Y);
                cb.Append(g.position.Z);
                
                cb.Append(g.rotation.X);
                cb.Append(g.rotation.Y);
                cb.Append(g.rotation.Z);
                cb.Append(g.rotation.W);
                
                cb.Append(g.scale.X);
                cb.Append(g.scale.Y);
                cb.Append(g.scale.Z);
                
                cb.Append(g.opacity);
                
                cb.Append(g.color_dc.X);
                cb.Append(g.color_dc.Y);
                cb.Append(g.color_dc.Z);
                
                // Temporal part
                cb.Append(s.time);
                cb.Append(s.velocity.X);
                cb.Append(s.velocity.Y);
                cb.Append(s.velocity.Z);
            }
        }

        internal override void Submit()
        {
            SubmitReversible($"gaussians4d#{name}");
        }

        public override void Remove()
        {
            RemoveProp($"gaussians4d#{name}", name);
        }
    }
}
