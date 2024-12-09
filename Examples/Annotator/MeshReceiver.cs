using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Python.Runtime;

namespace Annotator
{
    public class MeshReceiver
    {
        // private readonly string _pythonDllPath;
        //
        // public MeshReceiver(string pythonDllPath)
        // {
        //     _pythonDllPath = pythonDllPath;
        //     PythonInterface.Initialize(pythonDllPath);
        // }
        //
        // public (float[] realMesh, float[] conceptMesh) GetMeshes(string script, string function, string pklFilePath)
        // {
        //
        //         // Run the Python function and get the JSON string
        //         string jsonString = PythonInterface.Run<string>(script, function, pklFilePath);
        //
        //         // Parse the JSON into a C# object
        //         var meshData = JsonSerializer.Deserialize<Dictionary<string, List<float>>>(jsonString);
        //
        //         // Extract real and conceptual meshes
        //         float[] realMesh = meshData["real_mesh"].ToArray();
        //         float[] conceptMesh = meshData["concept_mesh"].ToArray();
        //
        //         return (realMesh, conceptMesh);
        // }
        //
        // public float[] UpdateMesh(string templateName, Dictionary<string, object> parameters)
        // {
        //    
        //         Console.WriteLine(">>>>>>> DEBUG Multi Level Body 3");
        //         // Call the Python `update_mesh` function in the `mesh_generator` script
        //         string jsonString = PythonInterface.UpdateMesh<string>("mesh_generator", "update_mesh", parameters);
        //
        //         // Parse the JSON into a C# object
        //         var meshData = JsonSerializer.Deserialize<Dictionary<string, List<float>>>(jsonString);
        //         Console.WriteLine(">>>>>>> DEBUG Multi Level Body 1");
        //         // Extract the flattened mesh
        //         return meshData["flattened_mesh"].ToArray();
        //     
        // }
    }

}
