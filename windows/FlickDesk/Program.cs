using System;

namespace FlickDesk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App();
        app.Run();
    }
}
