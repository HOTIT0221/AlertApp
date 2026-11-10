using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Tempo.Common;

/// <summary>Small helpers for native window operations not exposed directly by WinUI.</summary>
internal static class NativeWindow
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public static nint GetHwnd(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static bool IsForeground(Window window) => GetForegroundWindow() == GetHwnd(window);

    public static void BringToFront(Window window)
    {
        var hwnd = GetHwnd(window);
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    /// <summary>Centres a window on the display that currently contains the cursor / primary work area.</summary>
    public static void CenterOnScreen(Window window, int width, int height)
    {
        var appWindow = window.AppWindow;
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (area is null) return;

        var work = area.WorkArea;
        int x = work.X + (work.Width - width) / 2;
        int y = work.Y + (work.Height - height) / 2;
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
