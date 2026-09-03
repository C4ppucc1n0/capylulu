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
        Width = 740;
        Height = 580;
        MinWidth = 700;
        MinHeight = 560;
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

        var content = new Grid { Margin = new Thickness(38, 4, 38, 24) };
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var stage = new Grid();
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(142) });
        content.Children.Add(stage);

        var trackHeader = BuildTrackHeader();
        Grid.SetRow(trackHeader, 0);
        stage.Children.Add(trackHeader);

        var record = BuildRecord(out _coverImage, out var rotatingVinyl);
        rotatingVinyl.RenderTransform = _recordRotation;
        rotatingVinyl.RenderTransformOrigin = new Point(0.5, 0.5);
        Grid.SetRow(record, 1);
        stage.Children.Add(record);

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
            Margin = new Thickness(24, 8, 16, 0)
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
        brand.Children.Add(new Rectangle
        {
            Width = 10,
            Height = 10,
            Fill = Skin.Accent,
            Stroke = Skin.Outline,
            StrokeThickness = 2,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        brand.Children.Add(new TextBlock
        {
            Text = "CAPYLULU  ·  RADIO",
            Foreground = Skin.Ink,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        bar.Children.Add(brand);

        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var minimize = Skin.CreateButton("－", 34, 34, () => WindowState = WindowState.Minimized, 15);
        minimize.ToolTip = "最小化";
        var close = Skin.CreateButton("×", 34, 34, Close, 17);
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

    private static Grid BuildRecord(out Image coverImage, out Grid vinylLayer)
    {
        var holder = new Grid
        {
            Width = 300,
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -4, 0, 0)
        };

        vinylLayer = new Grid();
        holder.Children.Add(vinylLayer);

        // 唱片压成两段平色 + 一圈硬描边。原来是三段径向渐变，
        // 那种写实打光是另一套语言，跟别处的平涂对不上。
        vinylLayer.Children.Add(new Ellipse
        {
            Fill = Skin.Frozen(Color.FromRgb(30, 26, 24)),
            Stroke = Skin.Outline,
            StrokeThickness = 4
        });
        vinylLayer.Children.Add(new Ellipse
        {
            Margin = new Thickness(20),
            Fill = Skin.Frozen(Color.FromRgb(46, 40, 36)),
            IsHitTestVisible = false
        });

        // 纹路留着，但改成实色细线，不再靠半透明叠出层次。
        for (var inset = 30; inset <= 66; inset += 12)
        {
            vinylLayer.Children.Add(new Ellipse
            {
                Margin = new Thickness(inset),
                Stroke = Skin.Frozen(Color.FromRgb(72, 62, 55)),
                StrokeThickness = 2,
                IsHitTestVisible = false
            });
        }

        coverImage = new Image
        {
            Width = 198,
            Height = 198,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Clip = new EllipseGeometry(new Point(99, 99), 99, 99)
        };
        RenderOptions.SetBitmapScalingMode(coverImage, BitmapScalingMode.HighQuality);
        holder.Children.Add(coverImage);

        holder.Children.Add(new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Skin.Parchment,
            Stroke = Skin.Outline,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        return holder;
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
            Width = 300,
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
        groove.Height = 16;
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

        Button? loopControl = null;
        loopControl = BuildControlButton("↻", "循环播放", 22, () =>
        {
            _isLooping = !_isLooping;
            Skin.LabelOf(loopControl!).Foreground = _isLooping ? Skin.Accent : Skin.Ink;
        });
        loopControl.HorizontalAlignment = HorizontalAlignment.Left;
        Skin.LabelOf(loopControl).Foreground = Skin.Accent;
        controls.Children.Add(loopControl);

        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        transport.Children.Add(BuildControlButton("◀", "上一首", 17, RestartSong));

        playButton = Skin.CreateButton(
            "Ⅱ", 54, 54, TogglePlayback, 22, Skin.Accent, Skin.Parchment, "Segoe UI Symbol");
        playButton.ToolTip = "暂停";
        playButton.Margin = new Thickness(14, 0, 14, 0);
        transport.Children.Add(playButton);

        transport.Children.Add(BuildControlButton("▶", "下一首", 17, RestartSong));
        Grid.SetColumn(transport, 1);
        controls.Children.Add(transport);

        Button? queue = null;
        queue = BuildControlButton("≡", "播放列表", 22, () => ShowQueueHint(queue!));
        queue.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(queue, 2);
        controls.Children.Add(queue);
        return player;
    }

    private static TextBlock BuildTimeText(string text, HorizontalAlignment alignment) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 11,
        Foreground = Skin.Muted,
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center
    };

    // 三个走带键、循环键、列表键都是同一张 42x42 的木牌，只有字不同。
    private static Button BuildControlButton(string content, string toolTip, double fontSize, Action onClick)
    {
        var button = Skin.CreateButton(
            content, 42, 42, onClick, fontSize, fontFamily: "Segoe UI Symbol");
        button.ToolTip = toolTip;
        return button;
    }

    private Button BuildFavoriteButton()
    {
        Button? button = null;
        button = Skin.CreateButton("♥  82", 80, 36, () =>
        {
            _isFavorite = !_isFavorite;
            var label = Skin.LabelOf(button!);
            label.Text = _isFavorite ? "♥  82" : "♡  81";
            label.Foreground = _isFavorite ? Skin.Crimson : Skin.Muted;
        }, 16, foreground: Skin.Crimson, fontFamily: "Segoe UI Symbol");
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
        _recordRotation.Angle = (_recordRotation.Angle + delta * 8.5) % 360;

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
        Skin.LabelOf(_playButton).Text = isPlaying ? "Ⅱ" : "▶";
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

    private static void ShowQueueHint(Button queueButton)
    {
        var label = Skin.LabelOf(queueButton);
        label.Text = label.Text == "≡" ? "1 / 1" : "≡";
        label.FontSize = label.Text == "≡" ? 22 : 12;
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
