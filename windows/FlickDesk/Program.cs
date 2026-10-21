using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FlickDesk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception); e.SetObserved(); };
        try
        {
            var app = new App();
            app.DispatcherUnhandledException += (_, e) => { Crash(e.Exception); e.Handled = true; };
            app.Run();
        }
        catch (Exception ex)
        {
            Crash(ex);
        }
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlickDesk", "crash.log");

    private static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:u}] {ex}\r\n\r\n");
        }
        catch { /* nothing else we can do */ }
    }

    /// <summary>Logs the error and tells the user, using a raw Win32 box in case WPF itself is what failed.</summary>
    private static void Crash(Exception? ex)
    {
        Log(ex);
        MessageBoxW(IntPtr.Zero,
            $"FlickDesk hit an error and has to close.\n\n{ex?.GetType().Name}: {ex?.Message}\n\nDetails were saved to:\n{LogPath}",
            "FlickDesk", 0x10 /* MB_ICONERROR */);
        Environment.Exit(1);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
