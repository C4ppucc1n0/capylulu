using System.Diagnostics;
using System.IO;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CapyLulu;

/// <summary>
/// 在整个虚拟桌面上播放一次不抢焦点、可点击穿透的庆祝动画。
/// 窗口和动画帧都只在播放期间存在，结束后不会留下常驻计时器。
/// </summary>
internal static class ScreenCelebration
{
    internal const int DurationMs = 2800;
    internal const int ConfettiCount = 104;
    internal const int EmojiCount = 64;
    internal const int CooldownMs = 1500;

    private static CelebrationWindow? _activeWindow;
    private static long _lastStartedAt = long.MinValue;

    public static bool Fire()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return false;
        }

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Fire);
            return true;
        }

        // 对应 Windows“动画效果”辅助功能设置。用户关闭动画时，不创建覆盖窗口。
        if (!SystemParameters.ClientAreaAnimation || _activeWindow is not null)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (_lastStartedAt != long.MinValue && now - _lastStartedAt < CooldownMs)
        {
            return false;
        }

        CelebrationWindow? window = null;
        try
        {
            window = new CelebrationWindow();
            _activeWindow = window;
            _lastStartedAt = now;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_activeWindow, window))
                {
                    _activeWindow = null;
                }
            };
            window.Show();
            window.Start();
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to play screen celebration: {exception}");
            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
            }

            window?.Close();
            return false;
        }
    }

    private sealed class CelebrationWindow : Window
    {
        private const int ExtendedWindowStyleIndex = -20;
        private const long TransparentStyle = 0x00000020L;
        private const long ToolWindowStyle = 0x00000080L;
        private const long NoActivateStyle = 0x08000000L;
        private const int NonClientHitTestMessage = 0x0084;
        private static readonly nint TransparentHitTest = new(-1);

        private readonly CelebrationSurface _surface;
        private HwndSource? _source;

        public CelebrationWindow()
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = Math.Max(1, SystemParameters.VirtualScreenWidth);
            Height = Math.Max(1, SystemParameters.VirtualScreenHeight);
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            Focusable = false;
            IsHitTestVisible = false;

            _surface = new CelebrationSurface();
            _surface.Completed += OnAnimationCompleted;
            Content = _surface;
            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
        }

        public void Start() => _surface.Start(
            Math.Max(1, ActualWidth),
            Math.Max(1, ActualHeight));

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var styles = GetWindowLongPtr(handle, ExtendedWindowStyleIndex).ToInt64();
            styles |= TransparentStyle | ToolWindowStyle | NoActivateStyle;
            SetWindowLongPtr(handle, ExtendedWindowStyleIndex, new nint(styles));

            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WindowMessageHook);
        }

        private static nint WindowMessageHook(
            nint hwnd,
            int message,
            nint wParam,
            nint lParam,
            ref bool handled)
        {
            if (message != NonClientHitTestMessage)
            {
                return nint.Zero;
            }

            handled = true;
            return TransparentHitTest;
        }

        private void OnAnimationCompleted(object? sender, EventArgs e) => Close();

        private void OnClosed(object? sender, EventArgs e)
        {
            _surface.Stop();
            _surface.Completed -= OnAnimationCompleted;
            if (_source is not null)
            {
                _source.RemoveHook(WindowMessageHook);
                _source = null;
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint window, int index, nint newValue);
    }

    private sealed class CelebrationSurface : FrameworkElement
    {
        private const double Gravity = 720;
        private const double FadeMs = 420;

        private static readonly Brush[] Palette = CreatePalette();
        private static readonly Geometry Star = CreateStar();
        private static readonly Brush SparkBrush = CreateFrozenBrush(Color.FromRgb(255, 184, 70));
        private static readonly Pen SparkOutline = CreateSparkOutline();
        private static readonly Brush SparkGlint = CreateFrozenBrush(Color.FromRgb(255, 242, 177));
        private static readonly ImageSource PartyFace = LoadSymbol("party-face.png");
        private static readonly ImageSource PartyPopper = LoadSymbol("party-popper.png");

        private readonly Stopwatch _clock = new();
        private readonly List<Particle> _confetti = [];
        private readonly List<EmojiParticle> _emojiParticles = [];
        private double _lastElapsedSeconds;
        private bool _isRunning;

        public CelebrationSurface()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        public event EventHandler? Completed;

        public void Start(double width, double height)
        {
            if (_isRunning)
            {
                return;
            }

            BuildParticles(width, height);
            _lastElapsedSeconds = 0;
            _clock.Restart();
            _isRunning = true;
            CompositionTarget.Rendering += OnRendering;
            InvalidateVisual();
        }

        public void Stop()
        {
            if (_isRunning)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isRunning = false;
            }

            _clock.Stop();
            _confetti.Clear();
            _emojiParticles.Clear();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            foreach (var particle in _confetti)
            {
                if (particle.DelaySeconds > 0 || particle.Opacity <= 0)
                {
                    continue;
                }

                drawingContext.PushOpacity(particle.Opacity);
                drawingContext.PushTransform(new TranslateTransform(particle.X, particle.Y));
                drawingContext.PushTransform(new RotateTransform(particle.Rotation));

                var flip = Math.Max(0.18, Math.Abs(Math.Cos(particle.Flip)));
                switch (particle.Shape)
                {
                    case 0:
                        drawingContext.DrawRoundedRectangle(
                            particle.Brush,
                            null,
                            new Rect(
                                -particle.Width * flip / 2,
                                -particle.Height / 2,
                                particle.Width * flip,
                                particle.Height),
                            1.5,
                            1.5);
                        break;
                    case 1:
                        drawingContext.DrawEllipse(
                            particle.Brush,
                            null,
                            new Point(0, 0),
                            particle.Width * flip / 2,
                            particle.Height / 2);
                        break;
                    default:
                        drawingContext.PushTransform(
                            new ScaleTransform(particle.Width * flip / 2, particle.Height / 2));
                        drawingContext.DrawGeometry(particle.Brush, null, Star);
                        drawingContext.Pop();
                        break;
                }

                drawingContext.Pop();
                drawingContext.Pop();
                drawingContext.Pop();
            }

            foreach (var particle in _emojiParticles)
            {
                if (particle.DelaySeconds > 0 || particle.Opacity <= 0)
                {
                    continue;
                }

                drawingContext.PushOpacity(particle.Opacity);
                drawingContext.PushTransform(new TranslateTransform(particle.X, particle.Y));
                drawingContext.PushTransform(new RotateTransform(particle.Rotation));
                drawingContext.PushTransform(new ScaleTransform(particle.Scale, particle.Scale));
                if (particle.IsSpark)
                {
                    drawingContext.PushTransform(
                        new ScaleTransform(particle.SymbolSize / 2, particle.SymbolSize / 2));
                    drawingContext.DrawGeometry(SparkBrush, SparkOutline, Star);
                    drawingContext.PushTransform(new TranslateTransform(-0.28, -0.3));
                    drawingContext.PushTransform(new ScaleTransform(0.2, 0.2));
                    drawingContext.DrawGeometry(SparkGlint, null, Star);
                    drawingContext.Pop();
                    drawingContext.Pop();
                    drawingContext.Pop();
                }
                else if (particle.Symbol is not null)
                {
                    drawingContext.DrawImage(
                        particle.Symbol,
                        new Rect(
                            -particle.SymbolSize / 2,
                            -particle.SymbolSize / 2,
                            particle.SymbolSize,
                            particle.SymbolSize));
                }

                drawingContext.Pop();
                drawingContext.Pop();
                drawingContext.Pop();
                drawingContext.Pop();
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var elapsedSeconds = _clock.Elapsed.TotalSeconds;
            if (elapsedSeconds * 1000 >= DurationMs)
            {
                Stop();
                Completed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var deltaSeconds = Math.Clamp(elapsedSeconds - _lastElapsedSeconds, 0, 0.05);
            _lastElapsedSeconds = elapsedSeconds;
            foreach (var particle in _confetti)
            {
                UpdateParticle(particle, deltaSeconds, elapsedSeconds);
                particle.Flip += particle.FlipSpeed * deltaSeconds;
            }

            foreach (var particle in _emojiParticles)
            {
                UpdateParticle(particle, deltaSeconds, elapsedSeconds);
                var ageSeconds = Math.Max(0, elapsedSeconds - particle.DelaySeed);
                var entrance = Math.Clamp(ageSeconds / 0.12, 0, 1);
                particle.Scale = particle.BaseScale
                    * (0.68 + (0.32 * entrance))
                    * (0.98 + (0.025 * Math.Sin((elapsedSeconds + particle.DelaySeed) * 7)));
            }

            InvalidateVisual();
        }

        private static void UpdateParticle(Particle particle, double deltaSeconds, double elapsedSeconds)
        {
            particle.DelaySeconds -= deltaSeconds;
            if (particle.DelaySeconds > 0)
            {
                return;
            }

            particle.VelocityY += Gravity * deltaSeconds;
            particle.VelocityX *= Math.Pow(0.58, deltaSeconds);
            particle.X += particle.VelocityX * deltaSeconds;
            particle.Y += particle.VelocityY * deltaSeconds;
            particle.Rotation += particle.RotationSpeed * deltaSeconds;

            var ageMs = (elapsedSeconds - particle.DelaySeed) * 1000;
            particle.Opacity = ageMs <= particle.LifeMs - FadeMs
                ? 1
                : Math.Clamp((particle.LifeMs - ageMs) / FadeMs, 0, 1);
        }

        private void BuildParticles(double width, double height)
        {
            _confetti.Clear();
            _emojiParticles.Clear();

            var random = new Random();
            for (var index = 0; index < ConfettiCount; index++)
            {
                var fromLeft = index % 2 == 0;
                var delay = 0.04 + (random.NextDouble() * 0.16);
                var emitterY = height * ((index % 4 < 2 ? 0.32 : 0.68)
                    + ((random.NextDouble() - 0.5) * 0.14));
                var horizontalSpeed = Math.Clamp(
                    width * (0.62 + (random.NextDouble() * 0.72)),
                    620,
                    2700);
                var fan = -0.9 + (random.NextDouble() * 1.8);
                var isDot = index % 4 != 0;
                var widthDip = isDot ? 4 + (random.NextDouble() * 3) : 3 + (random.NextDouble() * 3);
                _confetti.Add(new Particle
                {
                    X = fromLeft ? -10 - (random.NextDouble() * 28) : width + 10 + (random.NextDouble() * 28),
                    Y = emitterY,
                    VelocityX = (fromLeft ? 1 : -1)
                        * horizontalSpeed
                        * Math.Sqrt(1 - (0.45 * fan * fan)),
                    VelocityY = height * ((fan * 0.62) - 0.2),
                    Rotation = random.NextDouble() * 360,
                    RotationSpeed = RandomSignedSpeed(random, 360, 820),
                    Flip = random.NextDouble() * Math.PI,
                    FlipSpeed = 5 + (random.NextDouble() * 9),
                    Width = widthDip,
                    Height = isDot ? widthDip : 7 + (random.NextDouble() * 6),
                    Brush = Palette[index % Palette.Length],
                    Shape = isDot ? 1 : 0,
                    DelaySeconds = delay,
                    DelaySeed = delay,
                    LifeMs = 1500 + (random.NextDouble() * 920)
                });
            }

            for (var index = 0; index < EmojiCount; index++)
            {
                var fromLeft = index % 2 == 0;
                var delay = 0.04 + (random.NextDouble() * 0.2);
                var isSpark = index % 3 == 0;
                var emitterY = height * ((index % 4 < 2 ? 0.32 : 0.68)
                    + ((random.NextDouble() - 0.5) * 0.16));
                var horizontalSpeed = Math.Clamp(
                    width * (0.52 + (random.NextDouble() * 0.65)),
                    580,
                    2400);
                var fan = -0.88 + (random.NextDouble() * 1.76);
                _emojiParticles.Add(new EmojiParticle
                {
                    X = fromLeft ? -28 - (random.NextDouble() * 45) : width + 28 + (random.NextDouble() * 45),
                    Y = emitterY,
                    VelocityX = (fromLeft ? 1 : -1)
                        * horizontalSpeed
                        * Math.Sqrt(1 - (0.42 * fan * fan)),
                    VelocityY = height * ((fan * 0.58) - 0.22),
                    Rotation = -20 + (random.NextDouble() * 40),
                    // 实录中的大图形在扩散时持续明显自转，不能随机到近乎静止。
                    RotationSpeed = RandomSignedSpeed(random, 150, 360),
                    DelaySeconds = delay,
                    DelaySeed = delay,
                    LifeMs = 1680 + (random.NextDouble() * 860),
                    IsSpark = isSpark,
                    SymbolSize = (isSpark ? 38 : 48) + (random.NextDouble() * (isSpark ? 31 : 27)),
                    BaseScale = 0.86 + (random.NextDouble() * 0.34),
                    Symbol = isSpark ? null : index % 2 == 0 ? PartyFace : PartyPopper
                });
            }
        }

        private static Brush[] CreatePalette()
        {
            var brushes = new[]
            {
                new SolidColorBrush(Color.FromRgb(255, 77, 109)),
                new SolidColorBrush(Color.FromRgb(255, 196, 61)),
                new SolidColorBrush(Color.FromRgb(49, 208, 170)),
                new SolidColorBrush(Color.FromRgb(64, 156, 255)),
                new SolidColorBrush(Color.FromRgb(159, 92, 255)),
                new SolidColorBrush(Color.FromRgb(255, 126, 59)),
                new SolidColorBrush(Color.FromRgb(255, 98, 188))
            };
            foreach (var brush in brushes)
            {
                brush.Freeze();
            }

            return brushes;
        }

        private static Geometry CreateStar()
        {
            var geometry = Geometry.Parse(
                "M 0,-1 L 0.25,-0.25 L 1,0 L 0.25,0.25 L 0,1 L -0.25,0.25 L -1,0 L -0.25,-0.25 Z");
            geometry.Freeze();
            return geometry;
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen CreateSparkOutline()
        {
            var pen = new Pen(CreateFrozenBrush(Color.FromRgb(255, 150, 50)), 0.08)
            {
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }

        private static double RandomSignedSpeed(Random random, double minimum, double maximum) =>
            (random.Next(2) == 0 ? -1 : 1)
            * (minimum + (random.NextDouble() * (maximum - minimum)));

        private static ImageSource LoadSymbol(string fileName)
        {
            var assembly = typeof(ScreenCelebration).Assembly;
            using var bundle = assembly.GetManifestResourceStream("CapyLulu.g.resources")
                ?? throw new InvalidDataException("EXE 内未找到庆祝图形资源包。");
            using var resources = new ResourceReader(bundle);
            var expectedName = $"resources/celebration/{fileName}";
            var entries = resources.GetEnumerator();
            while (entries.MoveNext())
            {
                if (entries.Key is not string name
                    || !string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)
                    || entries.Value is not Stream stream)
                {
                    continue;
                }

                using (stream)
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }

            throw new InvalidDataException($"EXE 内未找到庆祝图形：{fileName}");
        }

        private class Particle
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double VelocityX { get; set; }
            public double VelocityY { get; set; }
            public double Rotation { get; set; }
            public double RotationSpeed { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Flip { get; set; }
            public double FlipSpeed { get; set; }
            public double DelaySeconds { get; set; }
            public double DelaySeed { get; set; }
            public double LifeMs { get; set; }
            public double Opacity { get; set; } = 1;
            public int Shape { get; set; }
            public Brush Brush { get; set; } = Brushes.Transparent;
        }

        private sealed class EmojiParticle : Particle
        {
            public ImageSource? Symbol { get; set; }
            public bool IsSpark { get; set; }
            public double SymbolSize { get; set; }
            public double BaseScale { get; set; } = 1;
            public double Scale { get; set; } = 1;
        }
    }
}
