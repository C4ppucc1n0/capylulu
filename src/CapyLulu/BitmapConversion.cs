using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CapyLulu;

internal static class BitmapConversion
{
    // 像素以 Bgra32 读出，与 WPF 的 PixelFormats.Bgra32 通道顺序一致，无需再交换 R/B。
    public static BitmapSource ToBitmapSource(int width, int height, byte[] bgraPixels)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            bgraPixels,
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
