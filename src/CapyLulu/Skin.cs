using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CapyLulu;

// 全项目唯一的视觉词汇表：颜色、边框画法、按钮、窗口开场设置都在这里。
// 界面代码不该再出现颜色字面量，想整体换风格只改这个文件。
//
// 画法是星露谷那一套语汇的原创实现——羊皮纸底、硬描边、左上高光右下阴影的斜面、
// 直角、不抗锯齿。不含任何游戏素材。
internal static class Skin
{
    public const string FontName = "Microsoft YaHei UI";
    public static readonly FontFamily Font = new(FontName);

    // 描边与文字
    public static readonly SolidColorBrush Outline = Hex(0x4A2C18);
    public static readonly SolidColorBrush Ink = Hex(0x3A2317);
    public static readonly SolidColorBrush Muted = Hex(0x8A6A4A);

    // 面板底
    public static readonly SolidColorBrush Parchment = Hex(0xFFF3D6);
    public static readonly SolidColorBrush ParchmentDim = Hex(0xF3DEB3);

    // 木框
    public static readonly SolidColorBrush WoodMid = Hex(0xB87A45);
    public static readonly SolidColorBrush WoodDark = Hex(0x8B5A2B);

    // 棋盘那块内凹的田。特意用深绿而不是深棕：噜噜自己就是棕色的，
    // 底板跟它撞色会让按颜色量 GIF 边界的验证失真。
    public static readonly SolidColorBrush Field = Hex(0x3E5B33);

    // 斜面。亮边在左上、暗边在右下 = 凸起；对调 = 内凹。Raised/Sunken 就差这一下。
    // 高光必须比 ParchmentDim 明显亮：截图确认过，它要是只比底色亮一点，
    // 按钮的凸起感就整个看不出来，只剩右下那道阴影。
    public static readonly SolidColorBrush Highlight = Hex(0xFFF8E4);
    public static readonly SolidColorBrush Shadow = Hex(0x7A4A22);

    // 强调
    public static readonly SolidColorBrush Accent = Hex(0x5D9C48);
    public static readonly SolidColorBrush Gold = Hex(0xF0B830);
    public static readonly SolidColorBrush Crimson = Hex(0xC7452C);

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

    private const double BevelThickness = 2;

    // 凸起面板：面板、卡片、按钮的脸都用这个。
    public static Border Raised(
        UIElement? child = null,
        double padding = 0,
        SolidColorBrush? body = null,
        double frame = 0) =>
        Bevel(Highlight, Shadow, child, padding, body ?? Parchment, frame);

    // 内凹槽位：棋盘底板、进度槽这类"陷进去"的地方。
    public static Border Sunken(
        UIElement? child = null,
        double padding = 0,
        SolidColorBrush? body = null,
        double frame = 0) =>
        Bevel(Shadow, Highlight, child, padding, body ?? ParchmentDim, frame);

    // 窗口外壳：木框一圈 + 羊皮纸底。两个窗口原本各抄一份配方，现在共用这一处。
    public static Border Shell(UIElement content) => Raised(content, frame: 6);

    // WPF 的 Border 一条边只能一种颜色，所以斜面得套三层：
    // 最外一圈深棕硬描边（+ 可选的木框带），里面左上一道、右下一道，最里面才是底色。
    private static Border Bevel(
        Brush topLeft,
        Brush bottomRight,
        UIElement? child,
        double padding,
        Brush body,
        double frame)
    {
        var inner = new Border
        {
            BorderBrush = bottomRight,
            BorderThickness = new Thickness(0, 0, BevelThickness, BevelThickness),
            Background = body,
            Padding = new Thickness(padding),
            Child = child
        };

        var lit = new Border
        {
            BorderBrush = topLeft,
            BorderThickness = new Thickness(BevelThickness, BevelThickness, 0, 0),
            Child = inner
        };

        return new Border
        {
            BorderBrush = Outline,
            BorderThickness = new Thickness(BevelThickness),
            Background = frame > 0 ? WoodMid : null,
            Padding = new Thickness(frame),
            Child = lit
        };
    }

    // 按下时把斜面反过来、内容下移 1px——星露谷最标志性的手感。
    // 层级由 Bevel 固定，所以照着拆就能拿到那两条边，不用额外存状态。
    // 只给 Raised 出来的按钮用，所以两个状态都写死，重复调用结果一致。
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
            // 居中的内容加 2 上边距，视觉上正好下移 1px。
            content.Margin = new Thickness(0, pressed ? 2 : 0, 0, 0);
        }
    }

    // 唯一的按钮做法。模板只是个裸 ContentPresenter，斜面是真实元素，
    // 所以按下反色直接改那两条边就行，不用在模板里绕触发器。
    public static Button CreateButton(
        string text,
        double width,
        double height,
        Action onClick,
        double fontSize = 14,
        SolidColorBrush? body = null,
        SolidColorBrush? foreground = null,
        string? fontFamily = null)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground ?? Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (fontFamily is not null)
        {
            label.FontFamily = new FontFamily(fontFamily);
        }

        // 按钮脸用次级底色而不是 Parchment：面板本身就是 Parchment，
        // 同色的话按钮只剩一圈描边，看不出是"浮"在面板上的。
        var face = Raised(label, body: body ?? ParchmentDim);
        var button = new Button
        {
            Width = width,
            Height = height,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Content = face,
            Template = BareTemplate()
        };

        button.PreviewMouseLeftButtonDown += (_, _) => SetPressed(face, true);
        button.PreviewMouseLeftButtonUp += (_, _) => SetPressed(face, false);
        button.MouseLeave += (_, _) => SetPressed(face, false);
        button.Click += (_, _) => onClick();
        return button;
    }

    // 按钮的字被斜面包在里面，要改字或改色的调用方从这儿取，不用自己拆三层。
    public static TextBlock LabelOf(Button button) =>
        (TextBlock)((Border)((Border)((Border)button.Content).Child!).Child!).Child!;

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

    private static SolidColorBrush Hex(uint rgb) => Frozen(Color.FromRgb(
        (byte)(rgb >> 16),
        (byte)(rgb >> 8),
        (byte)rgb));
}
