using CycleGUI;

namespace CycleGUIClient
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            VDraw.PromptPanel((pb =>
            {
                var endpoint=pb.TextInput("cgui-server", "127.0.0.1:5432", "ip:port");
                if (pb.Button("connect"))
                {
                    LocalTerminal.DisplayRemote(endpoint);
                }
            }));

            while (true)
            {
                Thread.Sleep(500);
            }
        }
    }
}