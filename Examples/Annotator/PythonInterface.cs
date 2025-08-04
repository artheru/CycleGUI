using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Python.Runtime;

namespace Annotator
{
    public class PythonInterface
    {
        private static bool inited = false;

        public static void Initialize(string pythonDllPath)
        {
            if (inited) return;
            inited = true;
            Runtime.PythonDLL = pythonDllPath;

            string pythonHome = Path.GetDirectoryName(pythonDllPath);
            string pythonLib = Path.Combine(pythonHome, "Lib");
            string pythonSitePackages = Path.Combine(pythonLib, "site-packages");
            string pythonDLLs = Path.Combine(pythonHome, "DLLs");

            // Set PYTHONHOME and PYTHONPATH for the global Python installation
            Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("PYTHONPATH", $"{pythonLib};{pythonSitePackages};{pythonDLLs}", EnvironmentVariableTarget.Process);

            
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string newPath = $"{pythonHome};{pythonDLLs};{currentPath}";
            Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Process);

            // Initialize the Python engine
            PythonEngine.PythonHome = pythonHome;
            PythonEngine.PythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
            PythonEngine.Initialize();
        }

        public static void Run(string script, string function, params object[] args)
        {
            if (!inited)
                throw new InvalidOperationException("Python engine not initialized. Call Initialize() first.");

            // Add the script's directory to sys.path
            string scriptDirectory = Path.GetDirectoryName(script);
            if (!string.IsNullOrEmpty(scriptDirectory))
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    sys.path.append(scriptDirectory);
                }
            }

            using (Py.GIL())
            {
                using var ps = Py.CreateScope("tmp");
                var moduleName = Path.GetFileNameWithoutExtension(script); // Get the script name without .py
                var module = ps.Import(moduleName);
                module.GetAttr(function).Invoke(args.Select(PyObject.FromManagedObject).ToArray());
            }
        }

        public static T Run<T>(string script, string function, params object[] args)
        {
            if (!inited)
                throw new InvalidOperationException("Python engine not initialized. Call Initialize() first.");

            // Add the script's directory to sys.path
            string scriptDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(scriptDirectory))
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    sys.path.append(scriptDirectory);
                }
            }

            using (Py.GIL())
            {
                using var ps = Py.CreateScope("me");
                var moduleName = Path.GetFileNameWithoutExtension(script); // Get the script name without .py
                var module = ps.Import(moduleName);
                return module.GetAttr(function).Invoke(args.Select(PyObject.FromManagedObject).ToArray()).As<T>();
            }
        }

        public static T UpdateMesh<T>(string script, string function, Dictionary<string, object> parameters)
        {
            if (!inited)
                throw new InvalidOperationException("Python engine not initialized. Call Initialize() first.");

            // Add the script's directory to sys.path
            Console.WriteLine(">>>>>>> DEBUG Multi Level Body 4");

            string scriptDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(scriptDirectory))
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    sys.path.append(scriptDirectory);
                }
            }
            Console.WriteLine(">>>>>>> DEBUG Multi Level Body 5");
            using (Py.GIL())
            {
                using var ps = Py.CreateScope("me");
                var moduleName = Path.GetFileNameWithoutExtension(script); // Get the script name without .py
                var module = ps.Import(moduleName);

                // Convert parameters to JSON for Python compatibility
                string jsonParameters = JsonSerializer.Serialize(parameters);
                return module.GetAttr(function).Invoke(new PyObject[] { PyObject.FromManagedObject(jsonParameters) }).As<T>();
            }
        }

        public static void LogSysPath()
        {
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                Console.WriteLine("Python sys.path:");
                foreach (var path in sys.path)
                {
                    Console.WriteLine($"------------------------- {path.ToString()}");
                }
            }
        }
    }
}
