using System.Runtime.InteropServices;
using ExtendScreenServer.Models;

namespace ExtendScreenServer.Services;

/// <summary>
/// Use Windows SendInput API to inject touch events into the system
/// </summary>
public static class InputInjector
{
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static int _screenWidth = 1920;
    private static int _screenHeight = 1080;
    private static int _offsetX = 0; // X offset of virtual display in virtual desktop
    private static int _offsetY = 0; // Y offset of virtual display in virtual desktop

    public static void SetScreenBounds(int width, int height)
    {
        _screenWidth = width;
        _screenHeight = height;
    }

    /// <summary>
    /// Set the offset of the virtual display (for extend screen mode)
    /// </summary>
    public static void SetScreenOffset(int offsetX, int offsetY)
    {
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    public static void Inject(InputEvent e)
    {
        // Normalized coordinates -> virtual display coordinates
        var localX = (int)(e.X * _screenWidth);
        var localY = (int)(e.Y * _screenHeight);

        // Add the virtual display offset in the virtual desktop
        var screenX = localX + _offsetX;
        var screenY = localY + _offsetY;

        switch (e.Type)
        {
            case InputEventType.Touch:
                HandleTouch(e, screenX, screenY);
                break;
            case InputEventType.Scroll:
                HandleScroll(e.DeltaX, e.DeltaY);
                break;
            case InputEventType.Zoom:
                HandleZoom(e.Scale);
                break;
        }
    }

    private static void HandleTouch(InputEvent e, int x, int y)
    {
        SetCursorPos(x, y);

        var mouseEvent = e.Action switch
        {
            0 => MOUSEEVENTF.LEFTDOWN,
            1 => MOUSEEVENTF.MOVE,
            2 => MOUSEEVENTF.LEFTUP,
            _ => MOUSEEVENTF.MOVE
        };

        // MOUSEEVENTF.ABSOLUTE coordinates need to be mapped to the entire virtual desktop
        // Get total virtual desktop size
        var virtualDesktopWidth = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
        var virtualDesktopHeight = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
        var virtualDesktopX = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
        var virtualDesktopY = GetSystemMetrics(77); // SM_YVIRTUALSCREEN

        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = mouseEvent | MOUSEEVENTF.ABSOLUTE;
        // Convert screen coordinates to 0-65535 absolute coordinates
        inputs[0].u.mi.dx = (x - virtualDesktopX) * 65535 / virtualDesktopWidth;
        inputs[0].u.mi.dy = (y - virtualDesktopY) * 65535 / virtualDesktopHeight;

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void HandleScroll(float deltaX, float deltaY)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF.WHEEL;
        inputs[0].u.mi.mouseData = (uint)((int)(deltaY * 120));
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void HandleZoom(float scale)
    {
        var inputs = new INPUT[2];

        // Ctrl down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0x11; // VK_CONTROL

        // Scroll
        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF.WHEEL;
        inputs[1].u.mi.mouseData = scale < 1.0f ? unchecked((uint)-120) : 120u;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());

        // Ctrl up
        inputs[0].u.ki.dwFlags = KEYEVENTF.KEYUP;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    [Flags]
    private enum MOUSEEVENTF : uint
    {
        MOVE = 0x0001,
        LEFTDOWN = 0x0002,
        LEFTUP = 0x0004,
        RIGHTDOWN = 0x0008,
        RIGHTUP = 0x0010,
        WHEEL = 0x0800,
        ABSOLUTE = 0x8000
    }

    [Flags]
    private enum KEYEVENTF : uint
    {
        KEYDOWN = 0x0000,
        KEYUP = 0x0002
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEKEYBDHARDWARE u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct MOUSEKEYBDHARDWARE
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public MOUSEEVENTF dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public KEYEVENTF dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}