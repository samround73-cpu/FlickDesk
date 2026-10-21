using System;
using System.Runtime.InteropServices;

namespace FlickDesk;

/// <summary>
/// Tells us when the left mouse button goes down/up anywhere (low-level mouse hook).
/// Used to "arm" the drop rail's invisible edge sensor only while something could be dragged,
/// so the rest of the time the screen edge is completely click-through.
/// The callback runs on the UI thread (the hook's thread pumps messages) and must stay tiny.
/// </summary>
internal sealed class MouseButtonWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    private readonly LowLevelMouseProc _proc; // kept alive while hooked
    private IntPtr _hook;

    /// <summary>true = left button pressed, false = released.</summary>
    public event Action<bool>? LeftButtonChanged;

    public MouseButtonWatcher()
    {
        _proc = OnMouse;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public bool Active => _hook != IntPtr.Zero;

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN) LeftButtonChanged?.Invoke(true);
            else if (msg == WM_LBUTTONUP) LeftButtonChanged?.Invoke(false);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
