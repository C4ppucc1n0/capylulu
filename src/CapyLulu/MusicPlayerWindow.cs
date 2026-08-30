using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CapyLulu;

internal sealed class MusicPlayerWindow : Window
{
    private const string CoverResourceName =
        "CapyLulu.GifResources.music-player-cover.png";
    private const double SongDurationSeconds = 200;

    private readonly Image _coverImage;
    private readonly RotateTransform _recordRotation = new();
    private readonly Button _playButton;
    private readonly TextBlock _currentTimeText;
    private readonly Slider _progressSlider;
    private readonly Rectangle[] _waveBars;
    private readonly Button _loopButton;
    private readonly Button _favoriteButton;
    private readonly DispatcherTimer _renderTimer;
    private readonly Stopwatch _tickClock = Stopwatch.StartNew();
    private double _elapsedSeconds = 86;
    private double _lastTickSeconds;
    private bool _isPlaying = true;
    private bool _isLooping = true;
    private bool _isFavorite = true;
    private bool _isSeeking;

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
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var shell = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 249, 245)),
            CornerRadius = new CornerRadius(30),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 35,
                ShadowDepth = 8,
                Opacity = 0.23,
                Color = Color.FromRgb(49, 58, 53)
            }
        };

        var root = new Grid { ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Child = root;
        Content = shell;

        AddAmbientBackground(root);
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
            out _progressSlider,
            out _waveBars,
            out _loopButton);
        Grid.SetRow(player, 2);
        stage.Children.Add(player);

        _favoriteButton = BuildFavoriteButton();
        _favoriteButton.HorizontalAlignment = HorizontalAlignment.Right;
        _favoriteButton.VerticalAlignment = VerticalAlignment.Center;
        _favoriteButton.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetRow(_favoriteButton, 0);
        stage.Children.Add(_favoriteButton);

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

    private static void AddAmbientBackground(Grid root)
    {
        var wash = new Rectangle
        {
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush(
                Color.FromRgb(252, 249, 241),
                Color.FromRgb(234, 244, 231),
                new Point(0, 0),
                new Point(1, 1))
        };
        Grid.SetRowSpan(wash, 2);
        root.Children.Add(wash);

        var peachGlow = new Ellipse
        {
            Width = 410,
            Height = 410,
            Fill = new SolidColorBrush(Color.FromArgb(48, 255, 178, 124)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -90, -125),
            Effect = new BlurEffect { Radius = 65 },
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(peachGlow, 2);
        root.Children.Add(peachGlow);

        var greenGlow = new Ellipse
        {
            Width = 460,
            Height = 360,
            Fill = new SolidColorBrush(Color.FromArgb(42, 123, 190, 145)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -110, 100, 0),
            Effect = new BlurEffect { Radius = 80 },
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(greenGlow, 2);
        root.Children.Add(greenGlow);
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
        brand.Children.Add(new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.FromRgb(42, 174, 117)),
            Margin = new Thickness(0, 0, 10, 0)
        });
        brand.Children.Add(new TextBlock
        {
            Text = "CAPYLULU  ·  RADIO",
            Foreground = new SolidColorBrush(Color.FromRgb(29, 37, 34)),
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        bar.Children.Add(brand);

        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var minimize = BuildChromeButton("—", "最小化");
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var close = BuildChromeButton("×", "关闭");
        close.FontSize = 22;
        close.Click += (_, _) => Close();
        windowButtons.Children.Add(minimize);
        windowButtons.Children.Add(close);
        Grid.SetColumn(windowButtons, 1);
        bar.Children.Add(windowButtons);
        return bar;
    }

    private static Button BuildChromeButton(string content, string toolTip)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = toolTip,
            Width = 38,
            Height = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(70, 78, 74)),
            Background = new SolidColorBrush(Color.FromArgb(24, 32, 39, 35)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        button.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;
        ApplyRoundedTemplate(button, 12);
        return button;
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
            Foreground = new SolidColorBrush(Color.FromRgb(28, 35, 32)),
            Margin = new Thickness(0, 0, 0, 3)
        });
        header.Children.Add(new TextBlock
        {
            Text = "CapyLulu  ·  花园散步电台",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(121, 127, 123))
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
            Margin = new Thickness(0, -4, 0, 0),
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 10,
                Direction = 285,
                Opacity = 0.33,
                Color = Color.FromRgb(14, 18, 16)
            }
        };

        vinylLayer = new Grid();
        holder.Children.Add(vinylLayer);

        vinylLayer.Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.40, 0.34),
                Center = new Point(0.48, 0.48),
                RadiusX = 0.6,
                RadiusY = 0.6,
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(59, 62, 60), 0),
                    new GradientStop(Color.FromRgb(18, 20, 19), 0.66),
                    new GradientStop(Color.FromRgb(7, 8, 8), 1)
                }
            },
            Stroke = new SolidColorBrush(Color.FromRgb(8, 9, 9)),
            StrokeThickness = 2
        });

        for (var inset = 12; inset <= 56; inset += 9)
        {
            vinylLayer.Children.Add(new Ellipse
            {
                Margin = new Thickness(inset),
                Stroke = new SolidColorBrush(Color.FromArgb(105, 118, 121, 119)),
                StrokeThickness = inset % 2 == 0 ? 1.2 : 0.75,
                IsHitTestVisible = false
            });
        }

        vinylLayer.Children.Add(new Ellipse
        {
            Margin = new Thickness(27),
            Stroke = new LinearGradientBrush(
                Color.FromArgb(90, 255, 255, 255),
                Color.FromArgb(8, 255, 255, 255),
                new Point(0, 0),
                new Point(1, 1)),
            StrokeThickness = 2,
            IsHitTestVisible = false
        });

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
            Width = 14,
            Height = 14,
            Fill = new SolidColorBrush(Color.FromRgb(247, 244, 233)),
            Stroke = new SolidColorBrush(Color.FromArgb(120, 42, 45, 43)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        return holder;
    }

    private Grid BuildPlayer(
        out Button playButton,
        out TextBlock currentTime,
        out Slider progress,
        out Rectangle[] waveBars,
        out Button loopButton)
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
                RadiusX = 1,
                RadiusY = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(Color.FromArgb(75, 40, 49, 45))
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

        var progressControl = new Slider
        {
            Minimum = 0,
            Maximum = SongDurationSeconds,
            Value = _elapsedSeconds,
            Margin = new Thickness(0, 0, 0, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Foreground = new SolidColorBrush(Color.FromRgb(36, 43, 40)),
            Background = new SolidColorBrush(Color.FromArgb(50, 36, 43, 40))
        };
        progressControl.PreviewMouseLeftButtonDown += (_, _) => _isSeeking = true;
        progressControl.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _elapsedSeconds = progressControl.Value;
            _isSeeking = false;
            UpdatePlaybackUi();
        };
        progressControl.ValueChanged += (_, _) =>
        {
            if (_isSeeking)
            {
                _elapsedSeconds = progressControl.Value;
                UpdatePlaybackUi(updateSlider: false);
            }
        };
        Grid.SetColumn(progressControl, 1);
        progressRow.Children.Add(progressControl);
        progress = progressControl;

        var controls = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(controls, 2);
        player.Children.Add(controls);

        var loopControl = BuildControlButton("↻", "循环播放", 25);
        loopControl.HorizontalAlignment = HorizontalAlignment.Left;
        loopControl.Click += (_, _) =>
        {
            _isLooping = !_isLooping;
            loopControl.Foreground = new SolidColorBrush(_isLooping
                ? Color.FromRgb(33, 167, 110)
                : Color.FromRgb(43, 50, 47));
        };
        controls.Children.Add(loopControl);
        loopButton = loopControl;

        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var previous = BuildControlButton("◀", "上一首", 19);
        previous.Click += (_, _) => RestartSong();
        transport.Children.Add(previous);

        playButton = BuildControlButton("Ⅱ", "暂停", 28);
        playButton.Width = 54;
        playButton.Height = 54;
        playButton.FontSize = 24;
        playButton.Margin = new Thickness(14, 0, 14, 0);
        playButton.BorderThickness = new Thickness(1.8);
        playButton.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 37, 34));
        playButton.Background = new SolidColorBrush(Color.FromArgb(175, 255, 255, 255));
        playButton.Click += (_, _) => TogglePlayback();
        transport.Children.Add(playButton);

        var next = BuildControlButton("▶", "下一首", 19);
        next.Click += (_, _) => RestartSong();
        transport.Children.Add(next);
        Grid.SetColumn(transport, 1);
        controls.Children.Add(transport);

        var queue = BuildControlButton("≡", "播放列表", 27);
        queue.HorizontalAlignment = HorizontalAlignment.Right;
        queue.Click += (_, _) => ShowQueueHint(queue);
        Grid.SetColumn(queue, 2);
        controls.Children.Add(queue);
        return player;
    }

    private static TextBlock BuildTimeText(string text, HorizontalAlignment alignment) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(117, 122, 119)),
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button BuildControlButton(string content, string toolTip, double fontSize)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = toolTip,
            Width = 42,
            Height = 42,
            Padding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(43, 50, 47)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        ApplyRoundedTemplate(button, 21);
        return button;
    }

    private static void ApplyRoundedTemplate(Button button, double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background))
        {
            RelativeSource = RelativeSource.TemplatedParent
        });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Button.BorderBrush))
        {
            RelativeSource = RelativeSource.TemplatedParent
        });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Button.BorderThickness))
        {
            RelativeSource = RelativeSource.TemplatedParent
        });

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Button.Content))
        {
            RelativeSource = RelativeSource.TemplatedParent
        });
        content.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(Button.Foreground))
        {
            RelativeSource = RelativeSource.TemplatedParent
        });
        border.AppendChild(content);

        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private Button BuildFavoriteButton()
    {
        var button = BuildControlButton("♥", "收藏", 24);
        button.Width = 80;
        button.Height = 36;
        button.Content = "♥  82";
        button.FontSize = 16;
        button.Foreground = new SolidColorBrush(Color.FromRgb(239, 105, 128));
        button.Background = new SolidColorBrush(Color.FromArgb(145, 255, 255, 255));
        button.BorderBrush = new SolidColorBrush(Color.FromArgb(45, 239, 105, 128));
        button.BorderThickness = new Thickness(1);
        button.Click += (_, _) =>
        {
            _isFavorite = !_isFavorite;
            button.Content = _isFavorite ? "♥  82" : "♡  81";
            button.Foreground = new SolidColorBrush(_isFavorite
                ? Color.FromRgb(239, 105, 128)
                : Color.FromRgb(104, 111, 107));
        };
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
        if (!_isPlaying)
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
        _playButton.Content = isPlaying ? "Ⅱ" : "▶";
        _playButton.ToolTip = isPlaying ? "暂停" : "播放";
    }

    private void RestartSong()
    {
        _elapsedSeconds = 0;
        SetPlayback(true);
        UpdatePlaybackUi();
    }

    private void UpdatePlaybackUi(bool updateSlider = true)
    {
        if (updateSlider && !_isSeeking)
        {
            _progressSlider.Value = _elapsedSeconds;
        }

        _currentTimeText.Text = FormatTime(_elapsedSeconds);
        var progress = Math.Clamp(_elapsedSeconds / SongDurationSeconds, 0, 1);
        var activeBars = (int)Math.Round(progress * _waveBars.Length);
        for (var index = 0; index < _waveBars.Length; index++)
        {
            _waveBars[index].Fill = new SolidColorBrush(index < activeBars
                ? Color.FromArgb(170, 42, 155, 105)
                : Color.FromArgb(68, 40, 49, 45));
        }

    }

    private void ShowQueueHint(Button queueButton)
    {
        queueButton.Content = Equals(queueButton.Content, "≡") ? "1 / 1" : "≡";
        queueButton.FontSize = Equals(queueButton.Content, "≡") ? 27 : 12;
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
