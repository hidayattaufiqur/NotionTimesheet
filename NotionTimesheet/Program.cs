using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace NotionTimesheet;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            ComServer server = new();

            var extensionDisposedEvent = new ManualResetEvent(false);
            var extensionInstance = new NotionTimesheetExtension(extensionDisposedEvent);

            server.RegisterClass<NotionTimesheetExtension, IExtension>(() => extensionInstance);
            server.Start();

            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as a Command Palette extension... exiting.");
        }
    }
}

