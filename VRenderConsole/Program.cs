using CycleGUI;
using FundamentalLib.Utilities;

namespace VRenderConsole
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 0)
            {
                VDraw.PromptPanel((pb =>
                {
                    var endpoint = pb.TextInput("cgui-server", "127.0.0.1:5432", "ip:port");
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

            Terminal.RegisterRemotePanel(pb =>
            {
                pb.Text("Welcome!");
                if (pb.Button("Click me"))
                {
                    Console.WriteLine("Clicked！");
                    pb.Text("You clicked, and i show");
                }

                var txt = pb.TextInput("Some text");
                if (pb.Button("Submit"))
                    Console.WriteLine(txt);
            });

            Task.Run(()=>
            {
                WebTerminal.Use();
                PlankHttpServer2.AddServingFiles("/files", "D:\\src\\vrender\\libVRender\\Emscripten\\Release");
                PlankHttpServer2.Listener(8081);
            });

            Task.Run(() =>
            {
                TCPTerminal.Serve();
            });

            bool test = true;
            int loops = 0;

            object sync = new object();

            var loopNotifier = new PanelBuilder.GUINotify<int>();
            VDraw.PromptPanel(pb =>
            {
                pb.Text("Hello world");
                if (pb.CheckBox("Test A", ref test))
                {
                    pb.Text($"Changed Checkbox, val={test}");
                }

                if (pb.Button("Close"))
                    pb.Exit();

                var txt=pb.TextInput("Some text");
                if (pb.Button("Submit"))
                    Console.WriteLine(txt);

                if (pb.Button("Long procedure"))
                {
                    pb.Freeze();
                    var result=VDraw.WaitPanelResult<int>(pb2 =>
                    {
                        var text = pb2.TextInput("enter some text", "default", "now");
                        if (pb2.Button("OK"))
                            pb2.Exit(int.Parse(text));
                    });
                    Console.WriteLine($"Result={result}");
                    var p = VDraw.DeclarePanel();
                    for (int i = 0; i < 10; ++i)
                    {
                        Thread.Sleep(1000);
                        p.Define(pb2 =>
                        {
                            pb2.Text($"{i+1}/10");
                            pb2.Progress(i, 10);
                        });
                    }
                    Console.WriteLine("done");
                    Thread.Sleep(1000);
                    p.Exit();
                    pb.UnFreeze();
                }

                var eventObj = Statics<PanelBuilder.GUINotify>.Declare(() => new());
                if (pb.Button("async procedure"))
                {
                    eventObj.Clear();
                    pb.Text("Notify external");
                    new Thread(() =>
                    {
                        Console.WriteLine("perform time consuming operation");
                        Thread.Sleep(1000);
                        eventObj.Set();
                        Console.WriteLine("perform time complete");
                    }).Start();
                }
                
                
                if (pb.Notified(eventObj))
                {
                    pb.Text("Async procedure done");
                }
                else
                {
                    pb.Text("pending");
                }

                if (pb.Notified(loopNotifier, out var ln))
                    pb.Text($"loop n={ln}");
                else pb.Text("not yet notified");
            });

            while (true)
            {
                Thread.Sleep(500);
                loops += 1;
                loopNotifier.Set(loops * 2);
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            // ApplicationConfiguration.Initialize();
            // Application.Run(new Form1());
        }
    }
}