using CycleGUI.API;
using CycleGUI;
using Python.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace Annotator
{
    public class AnnotatorHelper
    {
        public static float DemoObjectMinZ = float.MaxValue;

        public static (float[], string, string) DemoObjectFromPython(string objectName, int data_index)
        {
            using (Py.GIL())
            {
                //dynamic myModule = Py.Import("globe_main");
                dynamic manager = Py.Import("manager");
                dynamic objectModule = manager.load_object(objectName);
                return (ProcessPythonInstance(objectModule.DemoObject(data_index)), objectModule.DemoObject(data_index).data_id,
                    objectModule.DemoObject(data_index).data_type);
            }
        }

        public static dynamic InitializePythonAndAssembly(string objectName, int data_index)
        {
            using (Py.GIL())
            {
                //dynamic myModule = Py.Import("globe_main");
                dynamic manager = Py.Import("manager");
                dynamic objectModule = manager.load_object(objectName);
                return objectModule.ConceptualAssembly(data_index);
            }
        }

        public static float[] GetSingleComponentMesh(dynamic conceptualAssembly, string tname)
        {
            using (Py.GIL())
            {
                dynamic vertFaces = conceptualAssembly.get_single_component_mesh(tname);
                dynamic pyVertices = vertFaces[0];
                dynamic pyFaces = vertFaces[1];

                var vertices = ConvertToVertexList(pyVertices);
                var triangles = ConvertFacesToTriangles(vertices, pyFaces);
                return triangles;
            }
        }

        public static float CalculateAltitude(Vector3 cameraPos, Vector3 lookAt)
        {
            float dx = cameraPos.X - lookAt.X;
            float dy = cameraPos.Y - lookAt.Y;
            float dz = cameraPos.Z - lookAt.Z;

            float horizontalDistance = MathF.Sqrt(dx * dx + dy * dy);
            float altitude = MathF.Atan2(dz, horizontalDistance); // Angle in radians
            return altitude;
        }

        public static float CalculateAzimuth(Vector3 cameraPos, Vector3 lookAt)
        {
            return MathF.Atan2(cameraPos.Y - lookAt.Y, cameraPos.X - lookAt.X);
            //return MathHelper.ToDegrees(MathF.Atan2(cameraPos.Y - lookAt.Y, cameraPos.X - lookAt.X));
        }

        public static float CalculateDistance(Vector3 cameraPos, Vector3 lookAt)
        {
            return Vector3.Distance(cameraPos, lookAt);
        }

        private static List<Tuple<float, float, float>> ConvertToVertexList(dynamic pyVertices)
        {
            int len = pyVertices.shape[0].As<int>();
            var vertices = new List<Tuple<float, float, float>>(len);

            dynamic shape = pyVertices.shape;
            for (var i = 0; i < shape[0].As<int>(); ++i)
            {
                vertices.Add(Tuple.Create(
                    pyVertices.GetItem(i).GetItem(2).As<float>(),
                    pyVertices.GetItem(i).GetItem(0).As<float>(),
                    pyVertices.GetItem(i).GetItem(1).As<float>()));
            }
            return vertices;
        }

        private static float[] ConvertFacesToTriangles(List<Tuple<float, float, float>> vertices, dynamic pyFaces)
        {
            var toRender = new List<float>();

            var faces = new List<Tuple<int, int, int>>();
            dynamic faceShape = pyFaces.shape;

            for (var i = 0; i < faceShape[0].As<int>(); ++i)
            {
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
                DemoObjectMinZ = Math.Min(DemoObjectMinZ, vertices[index].Item3);
            }

            foreach (var face in faces)
            {
                AddVertex(face.Item1);
                AddVertex(face.Item2);
                AddVertex(face.Item3);
            }

            return toRender.ToArray();
        }
    }
}
