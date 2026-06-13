using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class Program
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_HOTKEY = 0x0312;
// dotnet publish -c Release -r win-x64 --self-contained true   /p:PublishSingleFile=true   /p:PublishTrimmed=false
    private const int HOTKEY_TOGGLE_ID = 1;
    private const int HOTKEY_QUIT_ID = 2;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    private const uint VK_Y = 0x59;
    private const uint VK_Q = 0x51;

    private const uint INPUT_MOUSE = 0;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const uint LLMHF_INJECTED = 0x00000001;
    private const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

    private const int HC_ACTION = 0;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const int MAX_REASONABLE_DELTA = 500;

    private static readonly nint OwnExtraInfo = 0x494D5959;
    private static readonly LowLevelMouseProc MouseProc = MouseHookCallback;

    private static nint _mouseHook;
    private static bool _enabled;
    private static POINT? _lastPoint;

    private static int Main()
    {
        using var mutex = new Mutex(true, "Global\\InvertMouseY_3F6F5282", out var createdNew);

        if (!createdNew)
            return 0;

        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, MouseProc, GetModuleHandle(null), 0);

        if (_mouseHook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install mouse hook.");

        if (!RegisterHotKey(0, HOTKEY_TOGGLE_ID, MOD_CONTROL | MOD_ALT, VK_Y))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register Ctrl+Alt+Y hotkey.");

        if (!RegisterHotKey(0, HOTKEY_QUIT_ID, MOD_CONTROL | MOD_ALT, VK_Q))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register Ctrl+Alt+Q hotkey.");

        try
        {
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY)
                {
                    if (msg.wParam == HOTKEY_TOGGLE_ID)
                    {
                        _enabled = !_enabled;
                        _lastPoint = null;
                        Beep(_enabled ? 880u : 440u, 80u);
                        continue;
                    }

                    if (msg.wParam == HOTKEY_QUIT_ID)
                    {
                        Beep(330u, 80u);
                        PostQuitMessage(0);
                        continue;
                    }
                }

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            UnregisterHotKey(0, HOTKEY_TOGGLE_ID);
            UnregisterHotKey(0, HOTKEY_QUIT_ID);

            if (_mouseHook != 0)
                UnhookWindowsHookEx(_mouseHook);
        }

        return 0;
    }

    private static nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        if (code != HC_ACTION || wParam != WM_MOUSEMOVE)
            return CallNextHookEx(_mouseHook, code, wParam, lParam);

        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        var injected = (info.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;
        var ownInjected = injected && info.dwExtraInfo == OwnExtraInfo;

        if (ownInjected)
        {
            _lastPoint = info.pt;
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        if (!_enabled)
        {
            _lastPoint = info.pt;
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        if (_lastPoint is not { } last)
        {
            _lastPoint = info.pt;
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        var dx = info.pt.x - last.x;
        var dy = info.pt.y - last.y;

        if (Math.Abs(dx) > MAX_REASONABLE_DELTA || Math.Abs(dy) > MAX_REASONABLE_DELTA)
        {
            _lastPoint = info.pt;
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        if (dy == 0)
        {
            _lastPoint = info.pt;
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        var target = new POINT
        {
            x = info.pt.x,
            y = last.y - dy
        };

        ClampToVirtualDesktop(ref target);

        if (target.x == info.pt.x && target.y == info.pt.y)
        {
            _lastPoint = info.pt;
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        if (SendAbsoluteMouseMove(target))
            _lastPoint = target;
        else
            _lastPoint = info.pt;

        return 1;
    }

    private static void ClampToVirtualDesktop(ref POINT point)
    {
        var minX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var minY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var maxX = minX + GetSystemMetrics(SM_CXVIRTUALSCREEN) - 1;
        var maxY = minY + GetSystemMetrics(SM_CYVIRTUALSCREEN) - 1;

        point.x = Math.Clamp(point.x, minX, maxX);
        point.y = Math.Clamp(point.y, minY, maxY);
    }

    private static bool SendAbsoluteMouseMove(POINT point)
    {
        var minX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var minY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        var absoluteX = NormalizeAbsolute(point.x, minX, width);
        var absoluteY = NormalizeAbsolute(point.y, minY, height);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = absoluteX,
                dy = absoluteY,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE_NOCOALESCE,
                time = 0,
                dwExtraInfo = OwnExtraInfo
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
    }

    private static int NormalizeAbsolute(int value, int origin, int size)
    {
        var denominator = Math.Max(1, size - 1);
        var normalized = (int)Math.Round((value - origin) * 65535.0 / denominator);

        return Math.Clamp(normalized, 0, 65535);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Beep(uint dwFreq, uint dwDuration);

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}