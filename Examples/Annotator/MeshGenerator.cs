using Python.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Annotator
{
    internal class MeshGenerator
    {
        private static float[] ProcessPythonInstance(dynamic instance)
        {
            var toRender = new List<float>();
            var vertices = new List<Tuple<float, float, float>>();
            var faces = new List<Tuple<int, int, int>>();

            dynamic pyVertices = instance.vertices;
            dynamic shape = pyVertices.shape;
            for (var i = 0; i < shape[0].As<int>(); ++i)
            {
                // zxy => xyz
                vertices.Add(Tuple.Create(
                    pyVertices.GetItem(i).GetItem(2).As<float>(),
                    pyVertices.GetItem(i).GetItem(0).As<float>(),
                    pyVertices.GetItem(i).GetItem(1).As<float>()));
            }

            dynamic pyFaces = instance.faces;
            dynamic faceShape = pyFaces.shape;

            for (var i = 0; i < faceShape[0].As<int>(); ++i)
            {
                // zxy => xyz
                var b = (int)pyFaces.GetItem(i).GetItem(0);
                faces.Add(Tuple.Create(
                    (int)pyFaces.GetItem(i).GetItem(2),
                    (int)pyFaces.GetItem(i).GetItem(0),
                    (int)pyFaces.GetItem(i).GetItem(1)));
            }

            void AddVertex(int index)
            {
                toRender.Add(vertices[index].Item1);
                toRender.Add(vertices[index].Item2);
                toRender.Add(vertices[index].Item3);
            }

            foreach (var face in faces)
            {
                AddVertex(face.Item1);
                AddVertex(face.Item2);
                AddVertex(face.Item3);
            }

            return toRender.ToArray();
        }

        public static float[] DemoObjectFromPython()
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("geometry_template");
                dynamic instance = myModule.DemoObject();

                var pts = (float[])ProcessPythonInstance(instance);
                // just for demo
                var minZ = pts.Where((_, i) => i % 3 == 2).Min();
                for (var i = 0; i < pts.Length; i++)
                {
                    if (i % 3 != 2) continue;
                    pts[i] -= minZ;
                }
                return pts;
            }
        }

        public static float[] CylinderFromPython(float height, float topRadius, float bottomRadius)
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("geometry_template");
                dynamic myClass = myModule.GetAttr("Cylinder");
                dynamic instance = myClass(height, topRadius, bottomRadius);

                return ProcessPythonInstance(instance);
            }
        }

        // ADDED FOR CONCEPTUAL TEMPLATES
        public static float[] MultilevelBodyFromPython(int numLevels, float[] level1Size, float[] level2Size, float[] level3Size, float[] level4Size)
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("concept_template");
                // Pass numLevels as an array instead of a single integer
                dynamic instance = myModule.Multilevel_Body(
                    new[] { numLevels },  // Now num_levels in Python is an array, e.g. [2]
                    level1Size,
                    level2Size,
                    level3Size,
                    level4Size
                );

                return ProcessPythonInstance(instance);
            }
        }


        // ADDED FOR CONCEPTUAL TEMPLATES
        public static float[] CylindricalLidFromPython(float[] outerSize, float[] innerSize)
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("concept_template");
                dynamic instance = myModule.Cylindrical_Lid(
                    outerSize,
                    innerSize
                );
                return ProcessPythonInstance(instance);
            }
        }

        public static float[] StandardSphereFromPython(float radius, Vector3 position, Vector3 rotation)
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("concept_template");
                dynamic instance = myModule.Standard_Sphere(
                    new[] { radius },
                    new[] { position.X, position.Y, position.Z },
                    new[] { rotation.X, rotation.Y, rotation.Z }
                );
                return ProcessPythonInstance(instance);
            }
        }

        public static float[] SemiRingBracketFromPython(float[] pivot_size, float[] pivot_continuity, float[] pivot_seperation,
            float[] endpoint_radius, float[] bracket_size, float[] bracket_offset, float[] bracket_rotation, float[] bracket_exist_angle,
            float[] position, float[] rotation, float[] has_top_endpoint, float[] has_bottom_endpoint)
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("concept_template");
                dynamic instance = myModule.Semi_Ring_Bracket(
                    pivot_size,
                    pivot_continuity,
                    pivot_seperation,
                    has_top_endpoint,
                    has_bottom_endpoint,
                    endpoint_radius,
                    bracket_size,
                    bracket_exist_angle,
                    bracket_offset,
                    bracket_rotation,
                    position,
                    rotation
                );
                return ProcessPythonInstance(instance);
            }
        }

        public static float[] CylindricalBaseFromPython(float[] bottom_size, float[] top_size, Vector3 position, Vector3 rotation)
        {
            using (Py.GIL())
            {
                dynamic myModule = Py.Import("concept_template");
                dynamic instance = myModule.Cylindrical_Base(
                    bottom_size,
                    top_size,
                    new[] { position.X, position.Y, position.Z },
                    new[] { rotation.X, rotation.Y, rotation.Z }
                );
                return ProcessPythonInstance(instance);
            }
        }

        public static float[] CreatePlaneMesh()
        {
            var mesh = new List<float>()
            {
                -3f, -3f, 0f, // Bottom-left
                3f, -3f, 0f, // Bottom-right 
                3f, 3f, 0f, // Top-right
                -3f, -3f, 0f, // Bottom-left
                3f, 3f, 0f, // Top-right
                -3f, 3f, 0f // Top-left
            }.ToArray();
            for (int i = 0; i < mesh.Length; i++)
            {
                mesh[i] *= 0.5f;
            }
            return mesh;
        }

        public static float[] CreateSphereMesh(int resolution)
        {
            var vertices = new List<float>();

            // Generate triangles directly
            for (int lat = 0; lat < resolution; lat++)
            {
                float theta1 = lat * MathF.PI / resolution;
                float theta2 = (lat + 1) * MathF.PI / resolution;

                for (int lon = 0; lon < resolution; lon++)
                {
                    float phi1 = lon * 2 * MathF.PI / resolution;
                    float phi2 = (lon + 1) * 2 * MathF.PI / resolution;

                    // Calculate vertices for both triangles
                    float x1 = MathF.Sin(theta1) * MathF.Cos(phi1);
                    float y1 = MathF.Cos(theta1);
                    float z1 = MathF.Sin(theta1) * MathF.Sin(phi1);

                    float x2 = MathF.Sin(theta2) * MathF.Cos(phi1);
                    float y2 = MathF.Cos(theta2);
                    float z2 = MathF.Sin(theta2) * MathF.Sin(phi1);

                    float x3 = MathF.Sin(theta1) * MathF.Cos(phi2);
                    float y3 = MathF.Cos(theta1);
                    float z3 = MathF.Sin(theta1) * MathF.Sin(phi2);

                    float x4 = MathF.Sin(theta2) * MathF.Cos(phi2);
                    float y4 = MathF.Cos(theta2);
                    float z4 = MathF.Sin(theta2) * MathF.Sin(phi2);

                    // First triangle
                    vertices.AddRange(new[] { x1, y1, z1 });
                    vertices.AddRange(new[] { x3, y3, z3 });
                    vertices.AddRange(new[] { x2, y2, z2 });

                    // Second triangle
                    vertices.AddRange(new[] { x2, y2, z2 });
                    vertices.AddRange(new[] { x3, y3, z3 });
                    vertices.AddRange(new[] { x4, y4, z4 });
                }
            }

            return vertices.ToArray();
        }

        public static float[] CreateCubeMesh()
        {
            // Define the vertices of the cube
            float[] vertices = {
                // Front face
                -1.0f, -1.0f,  1.0f,  // Bottom-left
                1.0f, -1.0f,  1.0f,  // Bottom-right
                1.0f,  1.0f,  1.0f,  // Top-right
                -1.0f,  1.0f,  1.0f,  // Top-left

                // Back face
                -1.0f, -1.0f, -1.0f,  // Bottom-left
                1.0f, -1.0f, -1.0f,  // Bottom-right
                1.0f,  1.0f, -1.0f,  // Top-right
                -1.0f,  1.0f, -1.0f,  // Top-left
            };

            // Define the indices for the triangles
            int[] indices = {
                // Front face
                0, 1, 2,  0, 2, 3,
                // Back face
                4, 6, 5,  4, 7, 6,
                // Left face
                4, 5, 1,  4, 1, 0,
                // Right face
                3, 2, 6,  3, 6, 7,
                // Top face
                1, 5, 6,  1, 6, 2,
                // Bottom face
                4, 0, 3,  4, 3, 7
            };

            // Create a list to hold the cube's vertices as triangles
            List<float> cubeMesh = new List<float>();

            // Add the vertices to the list using the indices
            foreach (int index in indices)
            {
                cubeMesh.Add(vertices[index * 3]);
                cubeMesh.Add(vertices[index * 3 + 1]);
                cubeMesh.Add(vertices[index * 3 + 2]);
            }

            return cubeMesh.ToArray();
        }
    }
}
