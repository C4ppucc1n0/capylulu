using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Polygon = System.Windows.Shapes.Polygon;

namespace CapyLulu;

internal sealed class MainWindow : Window
{
    private const double MinimumScale = 0.50;
    private const double MaximumScale = 1.00;
    private const double ScaleStep = 0.25;
    private const double DragThreshold = 5.0;
    private const int ToggleHotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;

    private static readonly string[] BubbleMessages =
    {
        "今天也要可可爱爱！",
        "你刚刚是不是点我啦？",
        "嘿嘿，抓到你了。",
        "休息一下再继续吧。",
        "我有在认真陪你哦。",
        "这次是什么动作呢？",
        "给你一点好运气！",
        "再点一下也可以呀。",
        "保持好心情～",
        "让我活动一下！"
    };

    private static readonly string[] DragBubbleMessages =
    {
        "要出发啦！",
        "轻一点抱我嘛～",
        "带我去看看！",
        "我跟上啦！",
        "唔，飞起来了！",
        "这个位置不错！",
        "慢慢放下我哦。"
    };

    private static readonly string[] HappyMessages =
    {
        "今天也要元气满满！", "好耶，继续前进！", "开心陪着你～"
    };

    private static readonly string[] SleepyMessages =
    {
        "让我眯一会儿……", "呼噜……我还醒着。", "今天慢一点也没关系。"
    };

    private static readonly string[] WorkingMessages =
    {
        "专注模式启动！", "一起认真完成它。", "我在旁边给你加油。"
    };

    private static readonly IReadOnlyDictionary<string, string> CharacterNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["2289a0bd9b469e69aafce7687053de85-action-sprite.webp"] = "小肚噜噜",
            ["3090265bbbee19cf4c27530cc2f75e19-action-sprite-without-columns-5-6-7.webp"] = "眨眼噜噜",
            ["3728b93ded95d888375ff85204f24e13-action-sprite-rows-1-4-aligned-last-column.webp"] = "眼睛噜噜",
            ["7c3d1f66c30bf03a042889fe8f435555-action-sprite-without-last-column.webp"] = "读书噜噜",
            ["86de7611b82965839a0286940531cbc6-action-sprite.webp"] = "背带裤噜噜"
        };

    private readonly Image _petImage;
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _idleEasterEggTimer;
    private readonly DispatcherTimer _settleTimer;
    private readonly Random _random = new();
    private readonly PetSettings _settings;
    private readonly IReadOnlyList<string> _characterResources;
    private readonly MenuItem _characterMenu;
    private readonly MenuItem _sizeMenu;
    private readonly MenuItem _opacityMenu;
    private readonly MenuItem _moodMenu;
    private readonly MenuItem _topmostMenu;
    private readonly ScaleTransform _moodScale = new(1, 1);
    private readonly RotateTransform _settleRotate = new(0);

    private SpriteSheet? _spriteSheet;
    private int _currentCharacterIndex;
    private int _currentRow;
    private int _currentFrame;
    private int _nextInteractionRow = 1;
    private bool _isPlayingInteraction;
    private bool _hasBufferedClick;
    private bool _mouseDown;
    private bool _isDragging;
    private Point _mouseDownScreen;
    private Point _windowDownPosition;
    private double _scale;
    private int _lastBubbleIndex = -1;
    private int _settlePhase;
    private PetMood _mood;
    private HwndSource? _windowSource;

    public MainWindow()
    {
        Title = "CapyLulu";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        SnapsToDevicePixels = true;

        _settings = SettingsStore.Load();
        _scale = Math.Clamp(_settings.Scale, MinimumScale, MaximumScale);
        Opacity = Math.Clamp(_settings.Opacity, 0.35, 1.0);
        Topmost = _settings.Topmost;
        _mood = Enum.TryParse<PetMood>(_settings.Mood, ignoreCase: true, out var savedMood)
            ? savedMood
            : PetMood.Happy;

        _bubbleText = new TextBlock
        {
            Foreground = Brushes.Black,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(16, 9, 16, 9)
        };

        _bubble = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Child = _bubbleText,
            Visibility = Visibility.Hidden,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 270,
            MinWidth = 150
        };

        var bubbleLayer = new Grid
        {
            Height = 78,
            Background = null,
            IsHitTestVisible = false
        };
        bubbleLayer.Children.Add(_bubble);
        bubbleLayer.Children.Add(new Polygon
        {
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            StrokeThickness = 1.5,
            Points = new PointCollection
            {
                new(0, 0),
                new(14, 0),
                new(7, 9)
            },
            Width = 14,
            Height = 9,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -7),
            Visibility = Visibility.Hidden,
            Tag = "BubbleTail"
        });

        _petImage = new Image
        {
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand
        };
        _petImage.RenderTransformOrigin = new Point(0.5, 1.0);
        _petImage.RenderTransform = new TransformGroup
        {
            Children = { _moodScale, _settleRotate }
        };
        RenderOptions.SetBitmapScalingMode(_petImage, BitmapScalingMode.HighQuality);

        var root = new Grid { Background = null };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bubbleLayer, 0);
        Grid.SetRow(_petImage, 1);
        root.Children.Add(bubbleLayer);
        root.Children.Add(_petImage);
        Content = root;

        _characterMenu = new MenuItem { Header = "更换角色" };
        _sizeMenu = new MenuItem { Header = "调整大小" };
        _opacityMenu = new MenuItem { Header = "透明度" };
        _moodMenu = new MenuItem { Header = "当前状态" };
        _topmostMenu = new MenuItem
        {
            Header = "始终置顶",
            IsCheckable = true,
            IsChecked = Topmost
        };
        _topmostMenu.Click += (_, _) =>
        {
            Topmost = _topmostMenu.IsChecked;
            SaveSettings();
        };

        var exitItem = new MenuItem { Header = "退出程序" };
        exitItem.Click += (_, _) => Close();

        var contextMenu = new ContextMenu
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13
        };
        contextMenu.Items.Add(_characterMenu);
        contextMenu.Items.Add(_sizeMenu);
        contextMenu.Items.Add(_opacityMenu);
        contextMenu.Items.Add(_moodMenu);
        contextMenu.Items.Add(_topmostMenu);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);
        _petImage.ContextMenu = contextMenu;

        BuildSizeMenu();
        BuildOpacityMenu();
        BuildMoodMenu();
        ApplyMoodAppearance();

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _animationTimer.Tick += OnAnimationTick;

        _bubbleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1600)
        };
        _bubbleTimer.Tick += (_, _) => HideBubble();

        _idleEasterEggTimer = new DispatcherTimer();
        _idleEasterEggTimer.Tick += (_, _) => StartIdleEasterEgg();
        ResetIdleEasterEggTimer();

        _settleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _settleTimer.Tick += OnSettleTick;

        _petImage.MouseLeftButtonDown += OnPetMouseLeftButtonDown;
        _petImage.MouseMove += OnPetMouseMove;
        _petImage.MouseLeftButtonUp += OnPetMouseLeftButtonUp;
        _petImage.MouseWheel += OnPetMouseWheel;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;

        _characterResources = DiscoverCharacterResources();
        BuildCharacterMenu();
        UpdatePetSize();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_characterResources.Count == 0)
        {
            MessageBox.Show(
                "EXE 内未找到动作图资源。请重新使用打包脚本生成 EXE。",
                "CapyLulu",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Close();
            return;
        }

        var savedIndex = -1;
        if (!string.IsNullOrWhiteSpace(_settings.SelectedCharacter))
        {
            savedIndex = _characterResources
                .Select((resource, index) => new { resource, index })
                .Where(item => string.Equals(
                    item.resource,
                    _settings.SelectedCharacter,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        }

        await SelectCharacterAsync(savedIndex >= 0 ? savedIndex : 0);

        UpdateLayout();
        if (_settings.Left is double left && _settings.Top is double top)
        {
            Left = left;
            Top = top;
            KeepWindowReachable();
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 36;
            Top = workArea.Bottom - ActualHeight - 30;
        }
    }

    private static IReadOnlyList<string> DiscoverCharacterResources()
    {
        const string resourcePrefix = "CapyLulu.GeneratedActions.";
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal)
                && (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void BuildCharacterMenu()
    {
        _characterMenu.Items.Clear();
        for (var index = 0; index < _characterResources.Count; index++)
        {
            var capturedIndex = index;
            var item = new MenuItem
            {
                Header = GetCharacterDisplayName(_characterResources[index], index),
                IsCheckable = true,
                IsChecked = index == _currentCharacterIndex
            };
            item.Click += async (_, _) => await SelectCharacterAsync(capturedIndex);
            _characterMenu.Items.Add(item);
        }

        _characterMenu.IsEnabled = _characterResources.Count > 0;
    }

    private void BuildSizeMenu()
    {
        _sizeMenu.Items.Clear();
        foreach (var value in new[] { 0.50, 0.75, 1.00 })
        {
            var capturedValue = value;
            var item = new MenuItem
            {
                Header = $"{value:P0}",
                IsCheckable = true,
                IsChecked = Math.Abs(_scale - value) < 0.001,
                Tag = value
            };
            item.Click += (_, _) => SetScale(capturedValue);
            _sizeMenu.Items.Add(item);
        }
    }

    private void BuildOpacityMenu()
    {
        _opacityMenu.Items.Clear();
        foreach (var value in new[] { 1.00, 0.85, 0.70, 0.55 })
        {
            var capturedValue = value;
            var item = new MenuItem
            {
                Header = $"{value:P0}",
                IsCheckable = true,
                IsChecked = Math.Abs(Opacity - value) < 0.01
            };
            item.Click += (_, _) => SetOpacity(capturedValue);
            _opacityMenu.Items.Add(item);
        }
    }

    private void BuildMoodMenu()
    {
        _moodMenu.Items.Clear();
        AddMoodMenuItem(PetMood.Happy, "开心");
        AddMoodMenuItem(PetMood.Sleepy, "困困");
        AddMoodMenuItem(PetMood.Working, "工作中");
    }

    private void AddMoodMenuItem(PetMood mood, string label)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = mood == _mood
        };
        item.Click += (_, _) => SetMood(mood);
        _moodMenu.Items.Add(item);
    }

    private void SetOpacity(double value)
    {
        Opacity = Math.Clamp(value, 0.35, 1.0);
        BuildOpacityMenu();
        SaveSettings();
    }

    private void SetMood(PetMood mood)
    {
        _mood = mood;
        ApplyMoodAppearance();
        BuildMoodMenu();
        StartIdle();
        ShowRandomBubble(GetMoodMessages());
        SaveSettings();
    }

    private void ApplyMoodAppearance()
    {
        switch (_mood)
        {
            case PetMood.Happy:
                _moodScale.ScaleX = 1.03;
                _moodScale.ScaleY = 1.03;
                break;
            case PetMood.Sleepy:
                _moodScale.ScaleX = 0.99;
                _moodScale.ScaleY = 0.97;
                break;
            default:
                _moodScale.ScaleX = 1.0;
                _moodScale.ScaleY = 1.0;
                break;
        }
    }

    private async Task SelectCharacterAsync(int index)
    {
        if (index < 0 || index >= _characterResources.Count)
        {
            return;
        }

        _animationTimer.Stop();
        Cursor = Cursors.Wait;
        try
        {
            var resourceName = _characterResources[index];
            var sheet = await Task.Run(() =>
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                    ?? throw new InvalidDataException($"无法打开内嵌动作资源：{resourceName}");
                return SpriteSheet.Load(stream, resourceName);
            });
            var previousCenterX = Left + (ActualWidth / 2);
            var previousBottom = Top + ActualHeight;
            _spriteSheet = sheet;
            _currentCharacterIndex = index;
            _nextInteractionRow = 1;
            _hasBufferedClick = false;
            UpdatePetSize();
            UpdateLayout();
            if (IsLoaded)
            {
                Left = previousCenterX - (ActualWidth / 2);
                Top = previousBottom - ActualHeight;
                KeepWindowReachable();
            }
            StartIdle();
            UpdateCharacterMenuChecks();
            SaveSettings();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法读取这套动作图：\n\n{exception.Message}",
                "CapyLulu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void UpdateCharacterMenuChecks()
    {
        for (var index = 0; index < _characterMenu.Items.Count; index++)
        {
            if (_characterMenu.Items[index] is MenuItem item)
            {
                item.IsChecked = index == _currentCharacterIndex;
            }
        }
    }

    private void StartIdle()
    {
        _isPlayingInteraction = false;
        _currentRow = 0;
        _currentFrame = 0;
        _animationTimer.Interval = TimeSpan.FromMilliseconds(GetIdleFrameInterval());
        ShowCurrentFrame();
        _animationTimer.Start();
        ResetIdleEasterEggTimer();
    }

    private void StartNextInteraction(bool showBubble = true)
    {
        if (_spriteSheet is null || _spriteSheet.Rows < 2)
        {
            return;
        }

        _isPlayingInteraction = true;
        _idleEasterEggTimer.Stop();
        _currentRow = _nextInteractionRow;
        _currentFrame = 0;
        _nextInteractionRow++;
        if (_nextInteractionRow >= _spriteSheet.Rows)
        {
            _nextInteractionRow = 1;
        }

        _animationTimer.Interval = TimeSpan.FromMilliseconds(165);
        ShowCurrentFrame();
        if (showBubble)
        {
            ShowRandomBubble();
        }
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_spriteSheet is null)
        {
            return;
        }

        _currentFrame++;
        if (_currentFrame < _spriteSheet.Columns)
        {
            ShowCurrentFrame();
            return;
        }

        if (_isPlayingInteraction)
        {
            if (_hasBufferedClick)
            {
                _hasBufferedClick = false;
                StartNextInteraction();
            }
            else
            {
                StartIdle();
            }
        }
        else
        {
            _currentFrame = 0;
            ShowCurrentFrame();
        }
    }

    private void ShowCurrentFrame()
    {
        if (_spriteSheet is not null)
        {
            _petImage.Source = _spriteSheet[_currentRow, _currentFrame];
        }
    }

    private void OnPetMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ResetIdleEasterEggTimer();
        _mouseDown = true;
        _isDragging = false;
        _mouseDownScreen = GetCursorPositionInDip();
        _windowDownPosition = new Point(Left, Top);
        _petImage.CaptureMouse();
        e.Handled = true;
    }

    private void OnPetMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var cursor = GetCursorPositionInDip();
        var delta = cursor - _mouseDownScreen;
        if (!_isDragging && Math.Abs(delta.X) + Math.Abs(delta.Y) >= DragThreshold)
        {
            _isDragging = true;
            StartDragInteraction();
            _petImage.Cursor = Cursors.SizeAll;
        }

        if (_isDragging)
        {
            Left = _windowDownPosition.X + delta.X;
            Top = _windowDownPosition.Y + delta.Y;
        }

        e.Handled = true;
    }

    private void OnPetMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown)
        {
            return;
        }

        _mouseDown = false;
        _petImage.ReleaseMouseCapture();
        _petImage.Cursor = Cursors.Hand;

        if (_isDragging)
        {
            KeepWindowReachable();
            StartDragSettle();
            SaveSettings();
        }
        else if (_isPlayingInteraction)
        {
            _hasBufferedClick = true;
        }
        else
        {
            StartNextInteraction();
        }

        _isDragging = false;
        e.Handled = true;
    }

    private void StartDragInteraction()
    {
        _hasBufferedClick = false;
        HideBubble();
        StartNextInteraction(showBubble: false);
        ShowRandomBubble(DragBubbleMessages);
    }

    private void StartDragSettle()
    {
        _settlePhase = 0;
        _settleRotate.Angle = 0;
        _settleTimer.Start();
    }

    private void OnSettleTick(object? sender, EventArgs e)
    {
        var angles = new[] { 2.5, -2.5, 1.5, -1.5, 0.0 };
        if (_settlePhase >= angles.Length)
        {
            _settleTimer.Stop();
            _settleRotate.Angle = 0;
            return;
        }

        _settleRotate.Angle = angles[_settlePhase++];
    }

    private void OnPetMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetScale(_scale + (e.Delta > 0 ? ScaleStep : -ScaleStep));
        e.Handled = true;
    }

    private void SetScale(double value)
    {
        value = Math.Clamp(Math.Round(value, 2), MinimumScale, MaximumScale);
        if (Math.Abs(value - _scale) < 0.001)
        {
            return;
        }

        var oldCenterX = Left + ActualWidth / 2;
        var oldBottom = Top + ActualHeight;
        _scale = value;
        UpdatePetSize();
        UpdateLayout();
        Left = oldCenterX - ActualWidth / 2;
        Top = oldBottom - ActualHeight;
        KeepWindowReachable();
        BuildSizeMenu();
        SaveSettings();
    }

    private void UpdatePetSize()
    {
        var frameWidth = _spriteSheet?.FrameWidth ?? 288;
        var frameHeight = _spriteSheet?.FrameHeight ?? 312;
        _petImage.Width = frameWidth * _scale;
        _petImage.Height = frameHeight * _scale;
    }

    private void ShowRandomBubble()
    {
        ShowRandomBubble(BubbleMessages);
    }

    private void ShowRandomBubble(IReadOnlyList<string> messages)
    {
        var index = _random.Next(messages.Count);
        if (messages.Count > 1 && index == _lastBubbleIndex)
        {
            index = (index + 1) % messages.Count;
        }

        _lastBubbleIndex = index;
        _bubbleText.Text = messages[index];
        _bubble.Visibility = Visibility.Visible;
        SetBubbleTailVisibility(Visibility.Visible);
        _bubbleTimer.Stop();
        _bubbleTimer.Start();
    }

    private void HideBubble()
    {
        _bubbleTimer.Stop();
        _bubble.Visibility = Visibility.Hidden;
        SetBubbleTailVisibility(Visibility.Hidden);
    }

    private void ResetIdleEasterEggTimer()
    {
        _idleEasterEggTimer?.Stop();
        if (_idleEasterEggTimer is null)
        {
            return;
        }

        _idleEasterEggTimer.Interval = TimeSpan.FromSeconds(_random.Next(35, 71));
        _idleEasterEggTimer.Start();
    }

    private void StartIdleEasterEgg()
    {
        if (_mouseDown || _isPlayingInteraction || _spriteSheet?.Rows < 2)
        {
            ResetIdleEasterEggTimer();
            return;
        }

        StartNextInteraction(showBubble: false);
        ShowRandomBubble(GetMoodMessages());
    }

    private int GetIdleFrameInterval() => _mood switch
    {
        PetMood.Happy => 185,
        PetMood.Sleepy => 340,
        _ => 260
    };

    private IReadOnlyList<string> GetMoodMessages() => _mood switch
    {
        PetMood.Sleepy => SleepyMessages,
        PetMood.Working => WorkingMessages,
        _ => HappyMessages
    };

    private void SetBubbleTailVisibility(Visibility visibility)
    {
        if (Content is Grid root
            && root.Children[0] is Grid bubbleLayer)
        {
            foreach (var child in bubbleLayer.Children)
            {
                if (child is Polygon { Tag: "BubbleTail" } tail)
                {
                    tail.Visibility = visibility;
                }
            }
        }
    }

    private void KeepWindowReachable()
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        const double visibleEdge = 48;

        Left = Math.Clamp(Left, virtualLeft - ActualWidth + visibleEdge, virtualRight - visibleEdge);
        Top = Math.Clamp(Top, virtualTop, virtualBottom - visibleEdge);
    }

    private void SaveSettings()
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Scale = _scale;
        _settings.Opacity = Opacity;
        _settings.Topmost = Topmost;
        _settings.Mood = _mood.ToString();
        _settings.SelectedCharacter = _characterResources.Count > 0
            ? _characterResources[_currentCharacterIndex]
            : null;
        SettingsStore.Save(_settings);
    }

    private static string GetCharacterDisplayName(string resourceName, int index)
    {
        var withExtension = resourceName.StartsWith("CapyLulu.GeneratedActions.", StringComparison.Ordinal)
            ? resourceName["CapyLulu.GeneratedActions.".Length..]
            : resourceName;
        return CharacterNames.TryGetValue(withExtension, out var displayName)
            ? displayName
            : $"角色 {index + 1}";
    }

    private Point GetCursorPositionInDip()
    {
        GetCursorPos(out var point);
        var devicePoint = new Point(point.X, point.Y);
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(devicePoint) ?? devicePoint;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = (HwndSource)PresentationSource.FromVisual(this)!;
        _windowSource.AddHook(WindowMessageHook);
        RegisterHotKey(
            _windowSource.Handle,
            ToggleHotkeyId,
            ModControl | ModAlt,
            (uint)KeyInterop.VirtualKeyFromKey(Key.P));
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == ToggleHotkeyId)
        {
            ToggleVisibility();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
        Topmost = _settings.Topmost;
    }

    internal void RevealFromSecondLaunch()
    {
        if (Visibility != Visibility.Visible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = _settings.Topmost;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settleTimer.Stop();
        _idleEasterEggTimer.Stop();
        if (_windowSource is not null)
        {
            UnregisterHotKey(_windowSource.Handle, ToggleHotkeyId);
            _windowSource.RemoveHook(WindowMessageHook);
        }

        SaveSettings();
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum PetMood
    {
        Happy,
        Sleepy,
        Working
    }
}
