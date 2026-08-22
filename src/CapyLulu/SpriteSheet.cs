using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CapyLulu;

internal sealed class SpriteSheet
{
    // 当前动作资源使用这两种单帧规格；行列数量由图片实际尺寸自动推断。
    private static readonly FrameSize[] KnownFrameSizes =
    {
        new(288, 312),
        new(192, 208)
    };

    private readonly BitmapSource[][] _frames;

    private SpriteSheet(
        string sourcePath,
        BitmapSource[][] frames,
        int frameWidth,
        int frameHeight,
        int columns,
        PetActionManifest actions)
    {
        SourcePath = sourcePath;
        _frames = frames;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        Columns = columns;
        Actions = actions;
    }

    public string SourcePath { get; }

    public int FrameWidth { get; }

    public int FrameHeight { get; }

    public int Rows => _frames.Length;

    public PetActionManifest Actions { get; }

    // 保留图集的原始列数作为一段动作的总时长；每行实际帧数可以更少。
    public int Columns { get; }

    public BitmapSource this[int row, int column] => _frames[row][column % _frames[row].Length];

    public int GetFrameCount(int row) => row >= 0 && row < _frames.Length ? _frames[row].Length : 0;

    public int GetPlaybackFrameCount(int row) => Actions.SpriteVersionNumber >= 2
        ? GetFrameCount(row)
        : Columns;

    public BitmapSource? GetLookFrame(int directionIndex)
    {
        if (!Actions.HasLookDirections || directionIndex is < 0 or >= 16)
        {
            return null;
        }

        var row = directionIndex < 8 ? Actions.LookRows[0] : Actions.LookRows[1];
        var column = directionIndex % 8;
        return row >= 0 && row < _frames.Length && column < _frames[row].Length
            ? _frames[row][column]
            : null;
    }

    public static SpriteSheet Load(Stream stream, string sourceName, PetActionManifest? actions = null)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
        var layout = DetectLayout(image.Width, image.Height);
        var sourceFrames = new Image<Rgba32>[layout.Rows][];
        var anchors = new int[layout.Rows, layout.Columns];
        var hasVisibleContent = new bool[layout.Rows, layout.Columns];
        var allAnchors = new List<int>(layout.Rows * layout.Columns);

        try
        {
            for (var row = 0; row < layout.Rows; row++)
            {
                sourceFrames[row] = new Image<Rgba32>[layout.Columns];
                for (var column = 0; column < layout.Columns; column++)
                {
                    var frame = image.Clone(context => context.Crop(
                        new SixLabors.ImageSharp.Rectangle(
                            column * layout.FrameWidth,
                            row * layout.FrameHeight,
                            layout.FrameWidth,
                            layout.FrameHeight)));
                    sourceFrames[row][column] = frame;
                    hasVisibleContent[row, column] = HasVisibleContent(frame);
                    if (!hasVisibleContent[row, column])
                    {
                        continue;
                    }

                    anchors[row, column] = FindHorizontalAnchor(frame);
                    allAnchors.Add(anchors[row, column]);
                }
            }

            if (allAnchors.Count == 0)
            {
                throw new InvalidDataException("动作图中没有可播放的角色帧。");
            }

            allAnchors.Sort();
            var targetAnchor = allAnchors[allAnchors.Count / 2];
            var frames = new List<BitmapSource[]>();
            for (var row = 0; row < layout.Rows; row++)
            {
                var rowFrames = new List<BitmapSource>();
                for (var column = 0; column < layout.Columns; column++)
                {
                    if (!hasVisibleContent[row, column])
                    {
                        continue;
                    }

                    var maximumOffset = Math.Max(24, layout.FrameWidth / 3);
                    var horizontalOffset = Math.Clamp(
                        targetAnchor - anchors[row, column],
                        -maximumOffset,
                        maximumOffset);
                    rowFrames.Add(ToBitmapSource(sourceFrames[row][column], horizontalOffset));
                }

                // 整行为空时不视为动作，避免被排进互动轮换。
                if (rowFrames.Count > 0)
                {
                    frames.Add(rowFrames.ToArray());
                }
            }

            actions ??= layout.FrameWidth == 192 && layout.FrameHeight == 208 && frames.Count >= 11
                ? PetActionManifest.CreateV2Default()
                : new PetActionManifest();

            return new SpriteSheet(
                sourceName,
                frames.ToArray(),
                layout.FrameWidth,
                layout.FrameHeight,
                layout.Columns,
                actions);
        }
        finally
        {
            foreach (var row in sourceFrames)
            {
                if (row is null)
                {
                    continue;
                }

                foreach (var frame in row)
                {
                    frame?.Dispose();
                }
            }
        }
    }

    private static GridLayout DetectLayout(int sheetWidth, int sheetHeight)
    {
        var candidates = KnownFrameSizes
            .Select(size => GridLayout.TryCreate(sheetWidth, sheetHeight, size))
            .Where(layout => layout is not null)
            .Select(layout => layout!.Value)
            .OrderBy(layout => layout.TrailingArea)
            .ThenByDescending(layout => layout.FrameWidth * layout.FrameHeight)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidDataException(
                $"无法识别动作图布局：{sheetWidth} × {sheetHeight}。\n" +
                "当前支持 288 × 312 或 192 × 208 的单帧图，并可自动识别实际行列数。");
        }

        return candidates[0];
    }

    private static int FindHorizontalAnchor(Image<Rgba32> image)
    {
        const byte visibleAlpha = 40;
        var minY = image.Height;
        var maxY = -1;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A < visibleAlpha)
                    {
                        continue;
                    }

                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        });

        if (maxY < minY)
        {
            return image.Width / 2;
        }

        // 手臂和头部会随动作变化，角色下方约 42% 通常是更稳定的躯干或脚部。
        var stableAreaTop = minY + (int)((maxY - minY + 1) * 0.58);
        var columnWeights = new long[image.Width];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = stableAreaTop; y <= maxY; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A >= visibleAlpha)
                    {
                        columnWeights[x] += row[x].A;
                    }
                }
            }
        });

        var totalWeight = columnWeights.Sum();
        if (totalWeight == 0)
        {
            return image.Width / 2;
        }

        var halfway = totalWeight / 2;
        long accumulated = 0;
        for (var x = 0; x < columnWeights.Length; x++)
        {
            accumulated += columnWeights[x];
            if (accumulated >= halfway)
            {
                return x;
            }
        }

        return image.Width / 2;
    }

    private static bool HasVisibleContent(Image<Rgba32> image)
    {
        const byte visibleAlpha = 40;
        var hasVisiblePixel = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height && !hasVisiblePixel; y++)
            {
                foreach (var pixel in accessor.GetRowSpan(y))
                {
                    if (pixel.A >= visibleAlpha)
                    {
                        hasVisiblePixel = true;
                        break;
                    }
                }
            }
        });

        return hasVisiblePixel;
    }

    private static BitmapSource ToBitmapSource(Image<Rgba32> image, int horizontalOffset)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        FeatherHorizontalCutEdges(pixels, image.Width, image.Height);

        if (horizontalOffset != 0)
        {
            var translated = new byte[pixels.Length];
            var sourceX = Math.Max(0, -horizontalOffset);
            var destinationX = Math.Max(0, horizontalOffset);
            var copyWidth = image.Width - Math.Abs(horizontalOffset);
            if (copyWidth > 0)
            {
                for (var y = 0; y < image.Height; y++)
                {
                    Buffer.BlockCopy(
                        pixels,
                        ((y * image.Width) + sourceX) * 4,
                        translated,
                        ((y * image.Width) + destinationX) * 4,
                        copyWidth * 4);
                }
            }

            pixels = translated;
        }

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

    private static void FeatherHorizontalCutEdges(byte[] pixels, int width, int height)
    {
        var featherWidth = Math.Min(12, Math.Max(4, width / 16));
        const byte minimumUsefulAlpha = 12;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double opacityFactor;
                if (x < featherWidth)
                {
                    opacityFactor = x / (double)featherWidth;
                }
                else if (x >= width - featherWidth)
                {
                    opacityFactor = (width - 1 - x) / (double)featherWidth;
                }
                else
                {
                    continue;
                }

                var alphaIndex = ((y * width) + x) * 4 + 3;
                var adjustedAlpha = (byte)Math.Round(pixels[alphaIndex] * opacityFactor);
                if (adjustedAlpha < minimumUsefulAlpha)
                {
                    var colorIndex = alphaIndex - 3;
                    pixels[colorIndex] = 0;
                    pixels[colorIndex + 1] = 0;
                    pixels[colorIndex + 2] = 0;
                    adjustedAlpha = 0;
                }

                pixels[alphaIndex] = adjustedAlpha;
            }
        }
    }

    private readonly record struct FrameSize(int Width, int Height);

    private readonly record struct GridLayout(
        int FrameWidth,
        int FrameHeight,
        int Columns,
        int Rows,
        long TrailingArea)
    {
        public static GridLayout? TryCreate(int sheetWidth, int sheetHeight, FrameSize frameSize)
        {
            var columns = sheetWidth / frameSize.Width;
            var rows = sheetHeight / frameSize.Height;
            var trailingWidth = sheetWidth % frameSize.Width;
            var trailingHeight = sheetHeight % frameSize.Height;

            // 允许生成工具在右侧或底部保留少量透明补边，但不把它当成一格动画。
            if (columns < 1 || rows < 1
                || trailingWidth > frameSize.Width / 4
                || trailingHeight > frameSize.Height / 4)
            {
                return null;
            }

            var trailingArea = ((long)trailingWidth * sheetHeight)
                + ((long)trailingHeight * (sheetWidth - trailingWidth));
            return new GridLayout(
                frameSize.Width,
                frameSize.Height,
                columns,
                rows,
                trailingArea);
        }
    }
}
