using System.Runtime.InteropServices;

namespace CapyLulu;

internal static class GlobalHotkey
{
    public const int ToggleId = 1;
    public const int HotkeyMessage = 0x0312;
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;

    public static bool Register(IntPtr windowHandle, uint modifiers, uint virtualKey) =>
        RegisterHotKey(windowHandle, ToggleId, modifiers, virtualKey);

    public static void Unregister(IntPtr windowHandle) =>
        UnregisterHotKey(windowHandle, ToggleId);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
