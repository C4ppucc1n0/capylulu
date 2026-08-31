using System.Runtime.InteropServices;
using System.Windows;

namespace CapyLulu;

internal static class NativeCursor
{
    public static Point GetScreenPosition()
    {
        return GetCursorPos(out var point)
            ? new Point(point.X, point.Y)
            : default;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
