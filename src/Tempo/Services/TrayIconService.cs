using Tempo.Interop;
using static Tempo.Interop.NativeMethods;

namespace Tempo.Services;

/// <summary>
/// A dependency-free system tray icon built on Shell_NotifyIcon with a hidden message window.
/// Exposes Open / Exit actions via a native context menu.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const string ClassName = "TempoTrayMessageWindow";
    private const uint IconId = 1;
    private const int CmdOpen = 1;
    private const int CmdExit = 2;

    private WndProcDelegate? _wndProc; // kept alive to prevent GC of the callback
    private nint _hwnd;
    private nint _hicon;
    private bool _added;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public void Initialize(string tooltip, string? iconPath)
    {
        _wndProc = WndProc;
        var hInstance = GetModuleHandle(null);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref wc); // ignore failure if already registered

        _hwnd = CreateWindowEx(0, ClassName, "Tempo", 0, 0, 0, 0, 0, nint.Zero, nint.Zero, hInstance, nint.Zero);
        if (_hwnd == nint.Zero)
            return;

        _hicon = LoadTrayIcon(iconPath);

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hicon,
            szTip = tooltip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
        _added = Shell_NotifyIcon(NIM_ADD, ref data);

        // Request v4 behaviour for reliable tooltips/messages.
        data.uTimeoutOrVersion = 4;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private nint LoadTrayIcon(string? iconPath)
    {
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            var h = LoadImage(nint.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (h != nint.Zero) return h;
        }
        return LoadIcon(nint.Zero, IDI_APPLICATION);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_TRAYICON:
                int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
                if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
                {
                    OpenRequested?.Invoke();
                }
                else if (mouseMsg is WM_RBUTTONUP or WM_CONTEXTMENU)
                {
                    ShowContextMenu();
                }
                return nint.Zero;

            case WM_DESTROY:
                return nint.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero) return;
        try
        {
            AppendMenu(menu, MF_STRING, CmdOpen, "Open Tempo");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdExit, "Exit");

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd); // required so the menu dismisses correctly
            uint cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, nint.Zero);

            if (cmd == CmdOpen) OpenRequested?.Invoke();
            else if (cmd == CmdExit) ExitRequested?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added && _hwnd != nint.Zero)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = IconId,
            };
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_hwnd != nint.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
    }
}
