using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CapyLulu;

// 全项目唯一的视觉词汇表：颜色、边框画法、图标、按钮、窗口开场设置都在这里。
// 界面代码不该再出现颜色字面量，想整体换风格只改这个文件。
//
// 画法是星露谷那一套语汇的原创实现——木框一圈圈套、羊皮纸底、硬描边、直角、
// 不抗锯齿。框的结构照着它那套 9-slice 的做法：四角厚实、四边同色往里收。
// 不含任何游戏素材。
internal static class Skin
{
    // 一个"美术像素"占几个 DIP。星露谷是 1x 画完整屏放大 4 倍，所有边框都是这个
    // 单位的整数倍——线细了就变成网页描边，正是之前那版看着不对的根源。
    public const double U = 4;

    public const string FontName = "Microsoft YaHei UI";
    public static readonly FontFamily Font = new(FontName);

    // 描边与文字
    public static readonly SolidColorBrush Outline = Hex(0x3D2415);
    public static readonly SolidColorBrush Ink = Hex(0x4B2E1A);
    public static readonly SolidColorBrush Muted = Hex(0x9A7550);

    // 面板底
    public static readonly SolidColorBrush Parchment = Hex(0xFFF4DC);
    public static readonly SolidColorBrush ParchmentDim = Hex(0xF0D9A8);

    // 木框。由外向内 亮 -> 中 -> 暗，堆出圆木被削平的那种厚度。
    public static readonly SolidColorBrush WoodLight = Hex(0xD9A567);
    public static readonly SolidColorBrush WoodMid = Hex(0xB4763C);
    public static readonly SolidColorBrush WoodDark = Hex(0x7E4A22);

    // 棋盘那块内凹的田。特意用深绿而不是深棕：噜噜自己就是棕色的，
    // 底板跟它撞色会让按颜色量 GIF 边界的验证失真。
    public static readonly SolidColorBrush Field = Hex(0x3E5B33);

    // 斜面。亮边在左上、暗边在右下 = 凸起；对调 = 内凹。Raised/Sunken 就差这一下。
    public static readonly SolidColorBrush Highlight = Hex(0xFFF8E4);
    public static readonly SolidColorBrush Shadow = Hex(0x8A5227);

    // 强调
    public static readonly SolidColorBrush Accent = Hex(0x5D9C48);
    public static readonly SolidColorBrush Gold = Hex(0xF0B830);
    public static readonly SolidColorBrush Crimson = Hex(0xC7452C);

    // 木纹：每格底部一道暗线，横向平铺。平色的木框看着像塑料，
    // 有这道线才像一条条木板拼起来的。
    public static readonly DrawingBrush WoodGrain = Grain();

    // 纸纹：几点比底色深一档的细斑。大片平色的奶油底看着像塑料板，有这点噪点才像纸。
    // 压得很淡（只差 4 个色阶、半个美术像素大），任何靠取色判读界面的验证都感觉不到。
    public static readonly DrawingBrush Paper = PaperTexture();

    // 彩纸沿用方块那一组颜色。方块素材待换，所以这里先各留一份，
    // 等 MatchTileArt 换正式素材时两边一起收敛。
    public static readonly SolidColorBrush[] Confetti =
    [
        Hex(0xF2A65A),
        Hex(0x7CBA85),
        Hex(0x6EA8DC),
        Hex(0xE67C92),
        Hex(0xEECD6A)
    ];

    // 凸起面板：面板、卡片、按钮的脸都用这个。三层的结构是固定的，
    // SetPressed 靠它往里数两层找到那两条斜面边。
    public static Border Raised(UIElement? child = null, double padding = 0, SolidColorBrush? body = null) =>
        Frame(child, body ?? Parchment, padding,
            (new Thickness(U / 2), Outline),
            (new Thickness(U / 2, U / 2, 0, 0), Highlight),
            (new Thickness(0, 0, U / 2, U / 2), Shadow));

    // 内凹槽位：棋盘底板、进度槽这类"陷进去"的地方。高光与阴影对调即内凹。
    public static Border Sunken(UIElement? child = null, double padding = 0, SolidColorBrush? body = null) =>
        Frame(child, body ?? ParchmentDim, padding,
            (new Thickness(U / 2), Outline),
            (new Thickness(U / 2, U / 2, 0, 0), Shadow),
            (new Thickness(0, 0, U / 2, U / 2), Highlight));

    // 棋盘那种"陷进地里"的大槽：外描边 + 木框 + 内凹斜面，一共 3U。
    // 和 Sunken 分开是因为进度槽只有 16 DIP 高，套 12 DIP 的框会把它整个糊住。
    // 3U 这个厚度是挑出来的：Field 区正好还是 468 DIP，靠取色认棋盘的验证不用改。
    public static Border Plot(UIElement? child, double padding, SolidColorBrush body) =>
        Frame(child, body, padding,
            (new Thickness(U), Outline),
            (new Thickness(U), WoodGrain),
            (new Thickness(U, U, 0, 0), WoodDark),
            (new Thickness(0, 0, U, U), WoodLight));

    // 窗口外壳：一圈厚木框 + 四角铆钉。这是整套皮里最显眼的一件，
    // 六条band 从外到内是 描边/亮木/木纹/暗木/描边，合起来 6U。
    public static UIElement Shell(UIElement content)
    {
        var frame = Frame(content, Paper, 0,
            (new Thickness(U), Outline),
            (new Thickness(U), WoodLight),
            (new Thickness(U * 2), WoodGrain),
            (new Thickness(U), WoodDark),
            (new Thickness(U), Outline));

        var shell = new Grid();
        shell.Children.Add(frame);
        foreach (var (h, v) in new[]
        {
            (HorizontalAlignment.Left, VerticalAlignment.Top),
            (HorizontalAlignment.Right, VerticalAlignment.Top),
            (HorizontalAlignment.Left, VerticalAlignment.Bottom),
            (HorizontalAlignment.Right, VerticalAlignment.Bottom)
        })
        {
            shell.Children.Add(new Border
            {
                Width = U * 2,
                Height = U * 2,
                Margin = new Thickness(U * 2),
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Background = WoodLight,
                BorderBrush = Outline,
                BorderThickness = new Thickness(U / 2)
            });
        }

        return shell;
    }

    // 一圈一圈往里套，最里面才是内容。WPF 的 Border 一条边只能一种颜色，
    // 想要分层的框就只能这么堆——但堆的规律是一样的，所以只写这一处。
    private static Border Frame(
        UIElement? child,
        Brush body,
        double padding,
        params (Thickness Band, Brush Brush)[] bands)
    {
        // 底色挂在最内那条 band 上，不另起一层：SetPressed 和 LabelOf 都是照层数
        // 往里找的，多一层它们就会摸到背景板而不是内容。
        var current = new Border
        {
            BorderBrush = bands[^1].Brush,
            BorderThickness = bands[^1].Band,
            Background = body,
            Padding = new Thickness(padding),
            Child = child
        };

        for (var index = bands.Length - 2; index >= 0; index--)
        {
            current = new Border
            {
                BorderBrush = bands[index].Brush,
                BorderThickness = bands[index].Band,
                Child = current
            };
        }

        return current;
    }

    // 按下时把斜面反过来、内容下移 1px——星露谷最标志性的手感。
    // 层级由 Raised 固定，所以照着拆就能拿到那两条边，不用额外存状态。
    public static void SetPressed(Border bevel, bool pressed)
    {
        if (bevel.Child is not Border lit || lit.Child is not Border inner)
        {
            return;
        }

        (lit.BorderBrush, inner.BorderBrush) = pressed
            ? (Shadow, Highlight)
            : (Highlight, Shadow);

        if (inner.Child is FrameworkElement content)
        {
            content.Margin = new Thickness(0, pressed ? 2 : 0, 0, 0);
        }
    }

    // 唯一的按钮做法。模板只是个裸 ContentPresenter，斜面是真实元素，
    // 所以按下反色直接改那两条边就行，不用在模板里绕触发器。
    public static Button CreateButton(
        UIElement face,
        double width,
        double height,
        Action onClick,
        SolidColorBrush? body = null)
    {
        // 按钮脸用次级底色而不是 Parchment：面板本身就是 Parchment，
        // 同色的话按钮只剩一圈描边，看不出是"浮"在面板上的。
        var bevel = Raised(face, body: body ?? ParchmentDim);
        var button = new Button
        {
            Width = width,
            Height = height,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Content = bevel,
            Template = BareTemplate()
        };

        button.PreviewMouseLeftButtonDown += (_, _) => SetPressed(bevel, true);
        button.PreviewMouseLeftButtonUp += (_, _) => SetPressed(bevel, false);
        button.MouseLeave += (_, _) => SetPressed(bevel, false);
        button.Click += (_, _) => onClick();
        return button;
    }

    // 文字按钮：脸就是一个居中的 TextBlock。
    public static Button CreateButton(
        string text,
        double width,
        double height,
        Action onClick,
        double fontSize = 14,
        SolidColorBrush? body = null,
        SolidColorBrush? foreground = null) =>
        CreateButton(Label(text, fontSize, foreground), width, height, onClick, body);

    public static TextBlock Label(string text, double fontSize = 14, SolidColorBrush? foreground = null) => new()
    {
        Text = text,
        FontSize = fontSize,
        FontWeight = FontWeights.SemiBold,
        Foreground = foreground ?? Ink,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    // 按钮的字被斜面包在里面，要改字或改色的调用方从这儿取，不用自己拆三层。
    public static TextBlock LabelOf(Button button) =>
        (TextBlock)((Border)((Border)((Border)button.Content).Child!).Child!).Child!;

    // 像素图标：一行一个字符串，'.' 是透明，其余字符按 palette 下标取色。
    // 先造 1:1 的位图再按整数倍最近邻放大，边缘才是硬的——用 Path 画会被抗锯齿糊掉，
    // 那正是原来那批 Segoe UI Symbol 字形看着不像素的原因。
    public static Image Icon(string[] rows, double scale, params SolidColorBrush[] palette)
    {
        var image = new Image
        {
            Source = IconSource(rows, palette),
            Width = rows[0].Length * scale,
            Height = rows.Length * scale,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    // 想换图标的调用方换 Image.Source 就行，尺寸不用重算——点阵一样大的两张图
    // （播放/暂停、亮/暗的循环键）就是靠这个原地对调的。
    public static BitmapSource IconSource(string[] rows, params SolidColorBrush[] palette)
    {
        var width = rows[0].Length;
        var height = rows.Length;
        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var glyph = rows[y][x];
                if (glyph == '.')
                {
                    continue;
                }

                var color = palette[glyph - '0'].Color;
                pixels[(y * width) + x] = 0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    // 图标点阵。都画在奇数宽高的方格里，居中时不会半像素偏。
    public static class Art
    {
        public static readonly string[] Close =
        [
            "0.......0",
            ".0.....0.",
            "..0...0..",
            "...0.0...",
            "....0....",
            "...0.0...",
            "..0...0..",
            ".0.....0.",
            "0.......0"
        ];

        public static readonly string[] Minimize =
        [
            ".........",
            ".........",
            ".........",
            ".........",
            ".........",
            ".........",
            ".0000000.",
            ".0000000.",
            "........."
        ];

        public static readonly string[] Heart =
        [
            ".00...00.",
            "0000.0000",
            "000000000",
            "000000000",
            ".0000000.",
            "..00000..",
            "...000...",
            "....0...."
        ];

        public static readonly string[] Play =
        [
            "0........",
            "000......",
            "00000....",
            "0000000..",
            "000000000",
            "0000000..",
            "00000....",
            "000......",
            "0........"
        ];

        public static readonly string[] Pause =
        [
            "000...000",
            "000...000",
            "000...000",
            "000...000",
            "000...000",
            "000...000",
            "000...000",
            "000...000",
            "000...000"
        ];

        public static readonly string[] Previous =
        [
            "00......0",
            "00.....00",
            "00....000",
            "00...0000",
            "00..00000",
            "00...0000",
            "00....000",
            "00.....00",
            "00......0"
        ];

        public static readonly string[] Next =
        [
            "0......00",
            "00.....00",
            "000....00",
            "0000...00",
            "00000..00",
            "0000...00",
            "000....00",
            "00.....00",
            "0......00"
        ];

        // 环上开一道口、口边带箭头，才读得出是"转回去"而不是一个停止键。
        public static readonly string[] Loop =
        [
            "...00000...",
            "..00...00..",
            ".00.....000",
            "00.......00",
            "0.........0",
            "0..........",
            "0..........",
            "00.........",
            ".00.....00.",
            "..00...00..",
            "...00000..."
        ];

        // 奖励进度用一排星星代替"0/10"：满一颗是一轮，比读数字直观。
        public static readonly string[] Star =
        [
            "...0...",
            "..000..",
            "0000000",
            ".00000.",
            "..000..",
            ".00.00.",
            "0.....0"
        ];

        public static readonly string[] Queue =
        [
            "000000000",
            "000000000",
            ".........",
            "000000000",
            "000000000",
            ".........",
            "000000000",
            "000000000"
        ];

        // 麦穗，给标题栏当徽标。星露谷满屏都是这种小作物图标，
        // 比原来那个纯色小方块像回事。
        public static readonly string[] Wheat =
        [
            "....0....",
            "..00.00..",
            "...000...",
            "..00.00..",
            "...000...",
            "..00.00..",
            "....0....",
            "....0....",
            "....0...."
        ];
    }

    // 每个窗口的开场都一样：像素味的字、整数对齐、不抗锯齿。
    public static void ApplyChrome(Window window)
    {
        window.FontFamily = Font;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Aliased);
    }

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // 按钮自己的背景和描边都由 Content 里的斜面负责，模板只管把内容铺满可点区域。
    private static ControlTemplate BareTemplate()
    {
        // ContentPresenter 默认就是拉伸，所以不用再设对齐。
        return new ControlTemplate(typeof(Button))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
        };
    }

    // 斑点位置是错开的，不然平铺出来是一眼看得见的方格阵。
    private static DrawingBrush PaperTexture()
    {
        var speck = Hex(0xFBEFD0);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            Parchment, null, new RectangleGeometry(new Rect(0, 0, U * 8, U * 8))));
        foreach (var (x, y) in new[] { (0.0, 1.0), (3.0, 4.0), (5.0, 0.0), (6.0, 6.0), (2.0, 7.0) })
        {
            group.Children.Add(new GeometryDrawing(
                speck, null, new RectangleGeometry(new Rect(x * U, y * U, U / 2, U / 2))));
        }

        var paper = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, U * 8, U * 8),
            ViewportUnits = BrushMappingMode.Absolute
        };
        paper.Freeze();
        return paper;
    }

    private static DrawingBrush Grain()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(WoodMid, null, new RectangleGeometry(new Rect(0, 0, U * 8, U * 8))));
        group.Children.Add(new GeometryDrawing(WoodDark, null, new RectangleGeometry(new Rect(0, U * 7.5, U * 8, U / 2))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, U * 8, U * 8),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Hex(uint rgb) => Frozen(Color.FromRgb(
        (byte)(rgb >> 16),
        (byte)(rgb >> 8),
        (byte)rgb));
}
