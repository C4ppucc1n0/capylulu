using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CapyLulu;

// 界面外观全在 Skin 里，这个文件只管播放器自己的结构和状态。
internal sealed class MusicPlayerWindow : Window
{
    private const string CoverResourceName =
        "CapyLulu.GifResources.music-player-cover.png";
    private const double SongDurationSeconds = 200;

    private readonly Image _coverImage;
    private readonly RotateTransform _recordRotation = new();
    private readonly Button _playButton;
    private readonly Image _playIcon = Skin.Icon(Skin.Art.Pause, 3, Skin.Parchment);
    private readonly TextBlock _currentTimeText;
    // 进度用两根按比例分账的柱子表示，不存像素宽度，所以窗口拉伸时自己就对。
    private readonly ColumnDefinition _playedLane;
    private readonly ColumnDefinition _remainingLane;
    private readonly Rectangle[] _waveBars;
    private readonly DispatcherTimer _renderTimer;
    private readonly Stopwatch _tickClock = Stopwatch.StartNew();
    private double _elapsedSeconds = 86;
    private double _lastTickSeconds;
    private bool _isPlaying = true;
    private bool _isLooping = true;
    private bool _isFavorite = true;
    private bool _isSeeking;
    private int _paintedActiveBars;

    public MusicPlayerWindow()
    {
        Title = "CapyLulu 音乐播放器";
        // 尺寸按最宽的内容块定：走带那一排 360 + 左右各 40 + 外壳木框 24 两边。
        // 原来 740 宽而最宽的内容只有 360，唱片盒左右各空掉 190 DIP。
        Width = 496;
        Height = 612;
        MinWidth = 460;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = true;
        Skin.ApplyChrome(this);

        var root = new Grid { ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Content = Skin.Shell(root);

        root.Children.Add(BuildTitleBar());

        var content = new Grid { Margin = new Thickness(10, 2, 10, 6) };
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var stage = new Grid();
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(142) });
        content.Children.Add(stage);

        var trackHeader = Skin.Raised(BuildTrackHeader(), Skin.U * 1.5);
        trackHeader.HorizontalAlignment = HorizontalAlignment.Left;
        trackHeader.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(trackHeader, 0);
        stage.Children.Add(trackHeader);

        var record = BuildRecord(out _coverImage);
        record.RenderTransform = _recordRotation;

        // 唱片搁在一个浅木盘里。原来它孤零零悬在一大片奶油底中间，
        // 左右两侧那片空白是这个窗口最显空的地方。
        var tray = Skin.Plot(record, Skin.U * 3, Skin.WoodDark);
        tray.HorizontalAlignment = HorizontalAlignment.Center;
        tray.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(tray, 1);
        stage.Children.Add(tray);

        var player = BuildPlayer(
            out _playButton,
            out _currentTimeText,
            out _playedLane,
            out _remainingLane,
            out _waveBars);
        Grid.SetRow(player, 2);
        stage.Children.Add(player);

        var favoriteButton = BuildFavoriteButton();
        favoriteButton.HorizontalAlignment = HorizontalAlignment.Right;
        favoriteButton.VerticalAlignment = VerticalAlignment.Center;
        favoriteButton.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetRow(favoriteButton, 0);
        stage.Children.Add(favoriteButton);

        LoadCoverImage();
        UpdatePlaybackUi();

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _renderTimer.Tick += OnRenderTick;
        _lastTickSeconds = _tickClock.Elapsed.TotalSeconds;
        _renderTimer.Start();

        Closed += (_, _) => _renderTimer.Stop();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private Grid BuildTitleBar()
    {
        var bar = new Grid
        {
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 2, 2, 0)
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            DragMove();
        };

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var wheat = Skin.Icon(Skin.Art.Wheat, 2, Skin.Accent);
        wheat.Margin = new Thickness(0, 0, 8, 0);
        brand.Children.Add(wheat);
        brand.Children.Add(new TextBlock
        {
            Text = "CAPYLULU  ·  RADIO",
            Foreground = Skin.Ink,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var plaque = Skin.Raised(brand, Skin.U * 1.5);
        plaque.HorizontalAlignment = HorizontalAlignment.Left;
        plaque.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(plaque);

        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var minimize = Skin.CreateButton(
            Skin.Icon(Skin.Art.Minimize, 2, Skin.Ink), 34, 34, () => WindowState = WindowState.Minimized);
        minimize.ToolTip = "最小化";
        var close = Skin.CreateButton(Skin.Icon(Skin.Art.Close, 2, Skin.Ink), 34, 34, Close);
        close.ToolTip = "关闭";
        windowButtons.Children.Add(minimize);
        windowButtons.Children.Add(close);
        Grid.SetColumn(windowButtons, 1);
        bar.Children.Add(windowButtons);
        return bar;
    }

    private static StackPanel BuildTrackHeader()
    {
        var header = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0)
        };
        header.Children.Add(new TextBlock
        {
            Text = "把晴天装进口袋",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Skin.Ink,
            Margin = new Thickness(0, 0, 0, 3)
        });
        header.Children.Add(new TextBlock
        {
            Text = "CapyLulu  ·  花园散步电台",
            FontSize = 11,
            Foreground = Skin.Muted
        });
        return header;
    }

    // 整张唱片是一个会转的整体：盘面、纹路、封面都在这一个 Grid 里。
    // 原来封面单独挂在外层，只有盘面在转 —— 而盘面是一圈圈同心圆，
    // 转起来和不转一模一样，看上去就是"没在转"。
    private static Grid BuildRecord(out Image coverImage)
    {
        var record = new Grid
        {
            Width = 252,
            Height = 252,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -4, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };


        // 唱片压成两段平色 + 一圈硬描边。原来是三段径向渐变，
        // 那种写实打光是另一套语言，跟别处的平涂对不上。
        record.Children.Add(new Ellipse
        {
            Fill = Skin.Frozen(Color.FromRgb(42, 28, 18)),
            Stroke = Skin.Outline,
            StrokeThickness = Skin.U
        });
        record.Children.Add(new Ellipse
        {
            Margin = new Thickness(Skin.U * 4),
            Fill = Skin.Frozen(Color.FromRgb(62, 44, 30)),
            IsHitTestVisible = false
        });

        // 纹路留着，但改成实色细线，不再靠半透明叠出层次。
        for (var inset = 24; inset <= 52; inset += 10)
        {
            record.Children.Add(new Ellipse
            {
                Margin = new Thickness(inset),
                Stroke = Skin.Frozen(Color.FromRgb(94, 68, 46)),
                StrokeThickness = 2,
                IsHitTestVisible = false
            });
        }

        coverImage = new Image
        {
            Width = 166,
            Height = 166,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Clip = new EllipseGeometry(new Point(83, 83), 83, 83)
        };
        RenderOptions.SetBitmapScalingMode(coverImage, BitmapScalingMode.HighQuality);

        // 封面是这张唱片的标签面，压在纹路之上、且什么都不许盖在它上面。
        // 原来中心还画了个 16 DIP 的轴孔，正好戳在噜噜脸上。
        record.Children.Add(coverImage);
        return record;
    }

    private Grid BuildPlayer(
        out Button playButton,
        out TextBlock currentTime,
        out ColumnDefinition playedLane,
        out ColumnDefinition remainingLane,
        out Rectangle[] waveBars)
    {
        var player = new Grid
        {
            Width = 360,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        player.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        player.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        player.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });

        var waveform = new UniformGrid
        {
            Rows = 1,
            Columns = 76,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var bars = new List<Rectangle>();
        for (var index = 0; index < 76; index++)
        {
            var height = 3 + Math.Abs(Math.Sin(index * 0.58) * 12) + Math.Abs(Math.Cos(index * 0.17) * 5);
            var bar = new Rectangle
            {
                Width = 2,
                Height = height,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = Skin.Muted
            };
            bars.Add(bar);
            waveform.Children.Add(bar);
        }
        waveBars = bars.ToArray();
        player.Children.Add(waveform);

        var progressRow = new Grid();
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        Grid.SetRow(progressRow, 1);
        player.Children.Add(progressRow);

        currentTime = BuildTimeText("01:26", HorizontalAlignment.Left);
        progressRow.Children.Add(currentTime);
        var totalTime = BuildTimeText("03:20", HorizontalAlignment.Right);
        Grid.SetColumn(totalTime, 2);
        progressRow.Children.Add(totalTime);

        // 自己画一条内凹的槽。WPF 的 Slider 顶着系统主题长相，Background/Foreground
        // 根本管不到它的轨道，要改就得连 Track/Thumb 一起重写模板 —— 比这两根柱子长得多。
        playedLane = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        remainingLane = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var lanes = new Grid();
        lanes.ColumnDefinitions.Add(playedLane);
        lanes.ColumnDefinitions.Add(remainingLane);
        lanes.Children.Add(new Rectangle { Fill = Skin.Accent });

        var groove = Skin.Sunken(lanes);
        groove.Height = 20;
        groove.Cursor = Cursors.Hand;
        groove.VerticalAlignment = VerticalAlignment.Center;
        groove.MouseLeftButtonDown += (_, e) =>
        {
            _isSeeking = true;
            groove.CaptureMouse();
            SeekTo(lanes, e);
        };
        groove.MouseMove += (_, e) =>
        {
            if (_isSeeking)
            {
                SeekTo(lanes, e);
            }
        };
        groove.MouseLeftButtonUp += (_, e) =>
        {
            if (!_isSeeking)
            {
                return;
            }

            SeekTo(lanes, e);
            _isSeeking = false;
            groove.ReleaseMouseCapture();
        };
        Grid.SetColumn(groove, 1);
        progressRow.Children.Add(groove);

        var controls = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(controls, 2);
        player.Children.Add(controls);

        // 开着是作物绿、关了是灰——同一张点阵换个颜色重画，尺寸不变。
        var loopIcon = Skin.Icon(Skin.Art.Loop, 3, Skin.Accent);
        var loopControl = BuildControlButton(loopIcon, "循环播放", () =>
        {
            _isLooping = !_isLooping;
            loopIcon.Source = Skin.IconSource(Skin.Art.Loop, _isLooping ? Skin.Accent : Skin.Muted);
        });
        loopControl.HorizontalAlignment = HorizontalAlignment.Left;
        controls.Children.Add(loopControl);

        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        transport.Children.Add(
            BuildControlButton(Skin.Icon(Skin.Art.Previous, 2, Skin.Ink), "上一首", RestartSong));

        playButton = Skin.CreateButton(_playIcon, 54, 54, TogglePlayback, Skin.Accent);
        playButton.ToolTip = "暂停";
        playButton.Margin = new Thickness(14, 0, 14, 0);
        transport.Children.Add(playButton);

        transport.Children.Add(
            BuildControlButton(Skin.Icon(Skin.Art.Next, 2, Skin.Ink), "下一首", RestartSong));
        Grid.SetColumn(transport, 1);
        controls.Children.Add(transport);

        // 点一下把图标换成“第几首”，再点换回来。两块叠在一格里轮流显示。
        var queueIcon = Skin.Icon(Skin.Art.Queue, 3, Skin.Ink);
        var queueCount = Skin.Label("1 / 1", 12);
        queueCount.Visibility = Visibility.Collapsed;
        var queueFace = new Grid();
        queueFace.Children.Add(queueIcon);
        queueFace.Children.Add(queueCount);
        var queue = BuildControlButton(queueFace, "播放列表", () => ShowQueueHint(queueIcon, queueCount));
        queue.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(queue, 2);
        controls.Children.Add(queue);
        return player;
    }

    private static TextBlock BuildTimeText(string text, HorizontalAlignment alignment) => new()
    {
        Text = text,
        FontFamily = Skin.Font,
        FontSize = 11,
        Foreground = Skin.Muted,
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center
    };

    // 三个走带键、循环键、列表键都是同一张 42x42 的木牌，只有点阵不同。
    private static Button BuildControlButton(UIElement face, string toolTip, Action onClick)
    {
        var button = Skin.CreateButton(face, 42, 42, onClick);
        button.ToolTip = toolTip;
        return button;
    }

    private Button BuildFavoriteButton()
    {
        var heart = Skin.Icon(Skin.Art.Heart, 2, Skin.Crimson);
        heart.Margin = new Thickness(0, 0, 6, 0);
        var count = Skin.Label("82", 14, Skin.Crimson);
        var face = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        face.Children.Add(heart);
        face.Children.Add(count);

        var button = Skin.CreateButton(face, 84, 36, () =>
        {
            _isFavorite = !_isFavorite;
            heart.Source = Skin.IconSource(Skin.Art.Heart, _isFavorite ? Skin.Crimson : Skin.Muted);
            count.Text = _isFavorite ? "82" : "81";
            count.Foreground = _isFavorite ? Skin.Crimson : Skin.Muted;
        });
        button.ToolTip = "收藏";
        return button;
    }

    private void LoadCoverImage()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(CoverResourceName)
                ?? throw new InvalidDataException("找不到音乐封面图片资源。");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            _coverImage.Source = bitmap;
        }
        catch
        {
            _coverImage.Source = null;
            _coverImage.ToolTip = "封面加载失败";
        }
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        var now = _tickClock.Elapsed.TotalSeconds;
        var delta = Math.Min(0.1, now - _lastTickSeconds);
        _lastTickSeconds = now;
        // 拖动时不让时钟继续推进，否则槽里那段绿色会在手指位置和播放位置之间来回跳。
        if (!_isPlaying || _isSeeking)
        {
            return;
        }

        _elapsedSeconds += delta;
        // WPF 的正角度就是顺时针。30 度/秒 = 12 秒一圈：看得出在转，又不闹眼。
        _recordRotation.Angle = (_recordRotation.Angle + (delta * 30)) % 360;

        if (_elapsedSeconds >= SongDurationSeconds)
        {
            if (_isLooping)
            {
                _elapsedSeconds = 0;
            }
            else
            {
                _elapsedSeconds = SongDurationSeconds;
                SetPlayback(false);
            }
        }

        UpdatePlaybackUi();
    }

    private void TogglePlayback() => SetPlayback(!_isPlaying);

    private void SetPlayback(bool isPlaying)
    {
        _isPlaying = isPlaying;
        _playIcon.Source = Skin.IconSource(isPlaying ? Skin.Art.Pause : Skin.Art.Play, Skin.Parchment);
        _playButton.ToolTip = isPlaying ? "暂停" : "播放";
    }

    private void RestartSong()
    {
        _elapsedSeconds = 0;
        SetPlayback(true);
        UpdatePlaybackUi();
    }

    // 拖动时按落点在槽里的比例换算时间；用 lanes 而不是外层的槽，
    // 免得把斜面那 4px 也算进总长。
    private void SeekTo(Grid lanes, MouseEventArgs e)
    {
        if (lanes.ActualWidth <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(e.GetPosition(lanes).X / lanes.ActualWidth, 0, 1);
        _elapsedSeconds = ratio * SongDurationSeconds;
        UpdatePlaybackUi();
    }

    private void UpdatePlaybackUi()
    {
        _currentTimeText.Text = FormatTime(_elapsedSeconds);
        var progress = Math.Clamp(_elapsedSeconds / SongDurationSeconds, 0, 1);
        _playedLane.Width = new GridLength(progress, GridUnitType.Star);
        _remainingLane.Width = new GridLength(1 - progress, GridUnitType.Star);
        // 只重绘状态真正变化的那几条；条数不变时整段循环都可以跳过。
        var activeBars = (int)Math.Round(progress * _waveBars.Length);
        if (activeBars == _paintedActiveBars)
        {
            return;
        }

        var start = Math.Min(activeBars, _paintedActiveBars);
        var end = Math.Max(activeBars, _paintedActiveBars);
        for (var index = start; index < end; index++)
        {
            _waveBars[index].Fill = index < activeBars ? Skin.Accent : Skin.Muted;
        }

        _paintedActiveBars = activeBars;
    }

    private static void ShowQueueHint(UIElement icon, UIElement count)
    {
        var showCount = icon.Visibility == Visibility.Visible;
        icon.Visibility = showCount ? Visibility.Collapsed : Visibility.Visible;
        count.Visibility = showCount ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            TogglePlayback();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }
}
