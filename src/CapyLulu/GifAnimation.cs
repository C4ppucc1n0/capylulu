using System.IO;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

    public static GifAnimation Load(Stream stream, int? maxPixelDimension = null)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Bgra32>(stream);
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
            var delayHundredths = frameImage.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay;
            elapsedSeconds += Math.Max(2, delayHundredths) / 100.0;
            frameEndSeconds.Add(elapsedSeconds);

            if (maxPixelDimension is > 0
                && Math.Max(frameImage.Width, frameImage.Height) > maxPixelDimension.Value)
            {
                var scale = maxPixelDimension.Value / (double)Math.Max(frameImage.Width, frameImage.Height);
                frameImage.Mutate(context => context.Resize(
                    Math.Max(1, (int)Math.Round(frameImage.Width * scale)),
                    Math.Max(1, (int)Math.Round(frameImage.Height * scale))));
            }

            frames.Add(ToBitmapSource(frameImage));
        }

        return new GifAnimation(
            frames,
            frameEndSeconds,
            frames[0].PixelWidth,
            frames[0].PixelHeight);
    }

    private static BitmapSource ToBitmapSource(Image<Bgra32> image)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return BitmapConversion.ToBitmapSource(image.Width, image.Height, pixels);
    }
}
