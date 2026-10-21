using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlickDesk;

internal static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_LAYERED = 0x00080000;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int value);

    private static long GetExStyle(IntPtr h) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64() : GetWindowLong32(h, GWL_EXSTYLE);

    private static void SetExStyle(IntPtr h, long style)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr(style));
        else SetWindowLong32(h, GWL_EXSTYLE, unchecked((int)style));
    }

    /// <summary>Mouse clicks pass straight through; never steals focus; hidden from Alt+Tab.</summary>
    public static void MakeClickThrough(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        SetExStyle(h, GetExStyle(h) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar on Windows 10 20H1+ / 11, so it matches the dark window content.</summary>
    public static void UseDarkTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            int on = 1;
            DwmSetWindowAttribute(h, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }
        catch (Exception) { /* older Windows: keep the default title bar */ }
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// <summary>True while a full-screen game/video or presentation is running (Windows' own "busy" signal).</summary>
    public static bool IsBusyFullScreen()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) != 0) return false;
            return state is 2 /* BUSY */ or 3 /* D3D full screen */ or 4 /* presentation mode */;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Switches a layered window between click-through and hit-testable without recreating it.</summary>
    public static void SetClickThrough(Window w, bool clickThrough)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        long style = GetExStyle(h);
        long next = clickThrough ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (next != style) SetExStyle(h, next);
    }

    /// <summary>Receives mouse / drag-drop but never steals focus; hidden from Alt+Tab.</summary>
    public static void MakeNoActivate(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        SetExStyle(h, GetExStyle(h) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }
}
