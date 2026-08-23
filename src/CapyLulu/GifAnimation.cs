using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace CapyLulu;

internal sealed class GifAnimation
{
    private GifAnimation(
        IReadOnlyList<BitmapSource> frames,
        IReadOnlyList<double> frameEndSeconds,
        int pixelWidth,
        int pixelHeight)
    {
        Frames = frames;
        FrameEndSeconds = frameEndSeconds;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DurationSeconds = frameEndSeconds[^1];
    }

    public IReadOnlyList<BitmapSource> Frames { get; }

    public IReadOnlyList<double> FrameEndSeconds { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public double DurationSeconds { get; }

    public int GetFrameIndex(double loopSeconds)
    {
        for (var index = 0; index < FrameEndSeconds.Count; index++)
        {
            if (loopSeconds < FrameEndSeconds[index])
            {
                return index;
            }
        }

        return Frames.Count - 1;
    }

    public static GifAnimation Load(Stream stream)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
        if (image.Frames.Count == 0)
        {
            throw new InvalidDataException("唱歌 GIF 中没有可播放的帧。");
        }

        var frames = new List<BitmapSource>(image.Frames.Count);
        var frameEndSeconds = new List<double>(image.Frames.Count);
        var elapsedSeconds = 0.0;
        for (var index = 0; index < image.Frames.Count; index++)
        {
            using var frameImage = image.Frames.CloneFrame(index);
            frames.Add(ToBitmapSource(frameImage));

            var delayHundredths = frameImage.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay;
            elapsedSeconds += Math.Max(2, delayHundredths) / 100.0;
            frameEndSeconds.Add(elapsedSeconds);
        }

        return new GifAnimation(
            frames,
            frameEndSeconds,
            image.Width,
            image.Height);
    }

    private static BitmapSource ToBitmapSource(Image<Rgba32> image)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
        }

        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
