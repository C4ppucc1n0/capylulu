using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static CapyLulu.MatchGameOptions;

namespace CapyLulu;

// 方块长什么样只由这个文件决定。换成正式美术时改 Create/Apply 即可，
// 棋盘逻辑不认识颜色，窗口也只负责搬动元素。
internal static class MatchTileArt
{
    private const int DecodePixelSize = 96;

    // 颜色和形状同时区分种类，色觉障碍下也分得清。数量按 6 备好，
    // MatchGameOptions.TileKindCount 调到 6 时不用改这里。
    private static readonly SolidColorBrush[] Backgrounds =
    [
        Frozen(Color.FromRgb(242, 166, 90)),
        Frozen(Color.FromRgb(124, 186, 133)),
        Frozen(Color.FromRgb(110, 168, 220)),
        Frozen(Color.FromRgb(230, 124, 146)),
        Frozen(Color.FromRgb(183, 154, 214)),
        Frozen(Color.FromRgb(238, 205, 106))
    ];

    private static readonly SolidColorBrush GlyphBrush = Frozen(Color.FromArgb(238, 255, 255, 255));
    private static readonly SolidColorBrush EdgeBrush = Frozen(Color.FromArgb(48, 46, 34, 22));

    public static Border Create(int kind, GifAnimation? animation = null, double phaseOffsetSeconds = 0)
    {
        var index = NormalizeKind(kind);
        var tile = new Border
        {
            Width = TileSize,
            Height = TileSize,
            CornerRadius = new CornerRadius(TileCornerRadius),
            BorderBrush = EdgeBrush,
            BorderThickness = new Thickness(2),
            Background = Backgrounds[index],
            Padding = new Thickness(2),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1, 1), new TranslateTransform() }
            }
        };

        if (animation is null)
        {
            tile.Child = CreateGlyph(index);
            return tile;
        }

        var image = new Image
        {
            Source = animation.Frames[0],
            Stretch = Stretch.UniformToFill
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        tile.Child = image;
        tile.Tag = new AnimatedTile(image, animation, phaseOffsetSeconds);
        return tile;
    }

    // 就地改画另一种方块，让重力和洗牌能复用元素而不是整盘重建。
    public static void Apply(Border tile, int kind)
    {
        var index = NormalizeKind(kind);
        tile.Background = Backgrounds[index];
        tile.Child = CreateGlyph(index);
        tile.Tag = null;
    }

    public static GifAnimation?[] LoadRandomAnimations(Random random)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(BlockResourcePrefix, StringComparison.Ordinal))
            .ToArray();

        for (var index = resourceNames.Length - 1; index > 0; index--)
        {
            var swapWith = random.Next(index + 1);
            (resourceNames[index], resourceNames[swapWith]) = (resourceNames[swapWith], resourceNames[index]);
        }

        var animations = new List<GifAnimation>(TileKindCount);
        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is not null)
                {
                    animations.Add(GifAnimation.Load(stream, DecodePixelSize));
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException)
            {
                // 跳过坏素材，剩余位置会退回几何占位图，棋盘仍然可玩。
            }

            if (animations.Count == TileKindCount)
            {
                break;
            }
        }

        var result = new GifAnimation?[TileKindCount];
        for (var index = 0; index < animations.Count; index++)
        {
            result[index] = animations[index];
        }

        return result;
    }

    public static void ShowFrame(Border tile, double elapsedSeconds)
    {
        if (tile.Tag is not AnimatedTile state || state.Animation.DurationSeconds <= 0)
        {
            return;
        }

        var loopSeconds = ((elapsedSeconds * BlockPlaybackRate) + state.PhaseOffsetSeconds)
            % state.Animation.DurationSeconds;
        state.Image.Source = state.Animation.Frames[state.Animation.GetFrameIndex(loopSeconds)];
    }

    // 位移和缩放的挂点由这里创建，也由这里交出去，省得调用方去猜变换的层级。
    public static TranslateTransform OffsetOf(Border tile) =>
        (TranslateTransform)((TransformGroup)tile.RenderTransform).Children[1];

    public static ScaleTransform ScaleOf(Border tile) =>
        (ScaleTransform)((TransformGroup)tile.RenderTransform).Children[0];

    private static Shape CreateGlyph(int index)
    {
        const double size = MatchGameOptions.TileSize * 0.46;
        Shape glyph = index switch
        {
            0 => new Ellipse { Width = size, Height = size },
            1 => new Rectangle { Width = size, Height = size, RadiusX = 5, RadiusY = 5 },
            2 => Spikes(size, 4, 1),
            3 => Spikes(size, 3, 1),
            4 => Spikes(size, 5, 0.44),
            _ => Spikes(size, 6, 1)
        };

        glyph.Fill = GlyphBrush;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        return glyph;
    }

    // 以 size 为直径画正多边形；innerRatio 小于 1 时隔一个顶点收进来，就成了星形。
    private static Polygon Spikes(double size, int corners, double innerRatio)
    {
        var outer = size / 2;
        var vertices = innerRatio < 1 ? corners * 2 : corners;
        var points = new PointCollection();
        for (var i = 0; i < vertices; i++)
        {
            var radius = i % 2 == 0 || innerRatio >= 1 ? outer : outer * innerRatio;
            var angle = (-Math.PI / 2) + (i * 2 * Math.PI / vertices);
            points.Add(new Point(
                outer + (radius * Math.Cos(angle)),
                outer + (radius * Math.Sin(angle))));
        }

        return new Polygon { Points = points, Width = size, Height = size };
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static int NormalizeKind(int kind) =>
        ((kind % Backgrounds.Length) + Backgrounds.Length) % Backgrounds.Length;

    private sealed record AnimatedTile(
        Image Image,
        GifAnimation Animation,
        double PhaseOffsetSeconds);
}
