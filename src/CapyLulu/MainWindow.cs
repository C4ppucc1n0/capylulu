using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static CapyLulu.PetBehaviorOptions;

namespace CapyLulu;

internal sealed partial class MainWindow : Window
{
    private const string SingingGifResourceName =
        "CapyLulu.GifResources.flycapylulu.gif";

    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _idleEasterEggTimer;
    private readonly DispatcherTimer _focusTimer;
    private readonly DispatcherTimer _singingTimer;
    private readonly DispatcherTimer _singingResponseTimer;
    private readonly Random _random = new();
    private readonly PointerMotionTracker _motionTracker = new();
    private readonly CharacterCatalog _characterCatalog;
    private readonly PetDialogueCatalog _dialogues;
    private readonly FocusSession _focusSession = new();
    private readonly PetSettings _settings;
    private readonly IReadOnlyList<CharacterDefinition> _characters;
    private SpriteSheet? _spriteSheet;
    private int _currentCharacterIndex;
    private int _previousCharacterIndex = -1;
    private int _currentRow;
    private int _currentFrame;
    private int _nextInteractionRow;
    private IReadOnlyList<int> _clickRows = [];
    private bool _isPlayingInteraction;
    private bool _hasBufferedClick;
    private bool _mouseDown;
    private bool _isDragging;
    private Point _mouseDownScreen;
    private Point _windowDownPosition;
    private Point _dragTargetPosition;
    private Point _lastRenderedWindowPosition;
    private Vector _smoothedVelocity;
    private Vector _springVelocity;
    private Vector _springOffset;
    private double _scale;
    private int _lastBubbleIndex = -1;
    private double _lastRenderSeconds;
    private double _dragStartedSeconds;
    private double _dragFrameDistance;
    private double _dropStartedSeconds;
    private double _lastGazeUpdateSeconds;
    private double _lastGazeCursorSampleSeconds;
    private double _pendingGazeDirectionSince;
    private double _lastGazeStepSeconds;
    private double _gazeOutsideSince = -1;
    private Point _lastGazeCursorPosition;
    private bool _hasGazeCursorSample;
    private int _pendingGazeDirection = -1;
    private int _gazeDirection = -1;
    private PetInteractionState _interactionState = PetInteractionState.Idle;
    private PetGesture _pendingGesture;
    private bool _contextMenuOpen;
    private bool _isLoafingMode;
    private bool _isSinging;
    private GifAnimation? _singingAnimation;
    private double _singingStartedSeconds;
    private int _singingFrameIndex = -1;
    private int _singingLyricIndex = -1;
    private int _singingSupportCount;
    private bool _singingNoSupportPromptShown;
    private MusicPlayerWindow? _musicPlayerWindow;
    private PetMood _mood;
    private PetGazeMode _gazeMode;
    private HwndSource? _windowSource;
    private bool _hotkeyRegistered;

    public MainWindow()
    {
        InitializeComponent();

        _characterCatalog = new CharacterCatalog();
        _dialogues = PetDialogueCatalog.Load();
        _settings = SettingsStore.Load();
        _scale = Math.Clamp(_settings.Scale, MinimumScale, MaximumScale);
        Topmost = _settings.Topmost;
        _topmostMenu.IsChecked = Topmost;
        _mood = _settings.Mood;
        _gazeMode = _settings.GazeMode;

        BuildMoodMenu();
        BuildGazeModeMenu();
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

        _focusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _focusTimer.Tick += (_, _) => UpdateFocusTimer();

        _singingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _singingTimer.Tick += OnSingingTick;

        _singingResponseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2400)
        };
        _singingResponseTimer.Tick += (_, _) => HideSingingResponse();

        CompositionTarget.Rendering += OnRendering;

        _characters = _characterCatalog.Discover();
        BuildCharacterMenu();
        BuildLoafingMenu();
        UpdateSingingMenuState();
        UpdatePetSize();
    }

    private async void OnLoafingMenuClick(object sender, RoutedEventArgs e) =>
        await ToggleLoafingModeAsync();

    private void OnSingingMenuClick(object sender, RoutedEventArgs e) => StartSinging();

    private void OnMusicPlayerMenuClick(object sender, RoutedEventArgs e) => OpenMusicPlayer();

    private void OnFocusMenuClick(object sender, RoutedEventArgs e) => StartFocusSession();

    private void OnSingingSupportClick(object sender, RoutedEventArgs e) => RegisterSingingSupport();

    private void OnTopmostMenuClick(object sender, RoutedEventArgs e)
    {
        Topmost = _topmostMenu.IsChecked;
        SaveSettings();
    }

    private void OnExitMenuClick(object sender, RoutedEventArgs e) => Close();

    private void OnPetContextMenuOpened(object sender, RoutedEventArgs e) => _contextMenuOpen = true;

    private void OnPetContextMenuClosed(object sender, RoutedEventArgs e) => _contextMenuOpen = false;

    private void OpenMusicPlayer()
    {
        if (_musicPlayerWindow is { IsLoaded: true })
        {
            if (_musicPlayerWindow.WindowState == WindowState.Minimized)
            {
                _musicPlayerWindow.WindowState = WindowState.Normal;
            }

            _musicPlayerWindow.Activate();
            return;
        }

        _musicPlayerWindow = new MusicPlayerWindow();
        _musicPlayerWindow.Closed += (_, _) => _musicPlayerWindow = null;
        _musicPlayerWindow.Show();
        _musicPlayerWindow.Activate();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_characters.Count == 0)
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
        if (!string.IsNullOrWhiteSpace(_settings.SelectedCharacterId))
        {
            savedIndex = _characters
                .Select((character, index) => new { character, index })
                .Where(item => string.Equals(
                    item.character.Id,
                    _settings.SelectedCharacterId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        }
        else if (!string.IsNullOrWhiteSpace(_settings.SelectedCharacter))
        {
            savedIndex = _characters
                .Select((character, index) => new { character, index })
                .Where(item => string.Equals(
                    item.character.ResourceName,
                    _settings.SelectedCharacter,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        }

        if (savedIndex >= 0 && IsLoafingCharacter(savedIndex))
        {
            savedIndex = -1;
        }

        _previousCharacterIndex = savedIndex >= 0 ? savedIndex : GetFirstNormalCharacterIndex();
        var loafingIndex = GetLoafingCharacterIndex();
        if (_settings.LoafingMode && loafingIndex >= 0)
        {
            _isLoafingMode = true;
            if (await SelectCharacterAsync(loafingIndex))
            {
                ShowRandomBubble(["你的胆子真是肥嘟嘟的"]);
            }
            else
            {
                _isLoafingMode = false;
                await SelectCharacterAsync(_previousCharacterIndex);
            }
        }
        else
        {
            await SelectCharacterAsync(_previousCharacterIndex);
        }

        BuildLoafingMenu();

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

    private void BuildCharacterMenu()
    {
        _characterMenu.Items.Clear();
        for (var index = 0; index < _characters.Count; index++)
        {
            if (IsLoafingCharacter(index))
            {
                continue;
            }

            var capturedIndex = index;
            var item = new MenuItem
            {
                Header = _characters[index].DisplayName,
                IsCheckable = true,
                IsChecked = !_isLoafingMode && index == _currentCharacterIndex,
                Tag = capturedIndex
            };
            item.Click += async (_, _) => await SelectNormalCharacterAsync(capturedIndex);
            _characterMenu.Items.Add(item);
        }

        _characterMenu.IsEnabled = !_isSinging && _characterMenu.Items.Count > 0;
    }

    private void BuildLoafingMenu()
    {
        _loafingMenu.Header = "摸鱼模式";
        _loafingMenu.IsChecked = _isLoafingMode;
        _loafingMenu.IsEnabled = !_isSinging && GetLoafingCharacterIndex() >= 0;
    }

    private void UpdateSingingMenuState()
    {
        _singingMenu.Header = _isSinging ? "唱歌中…" : "唱歌";
        _singingMenu.IsEnabled = !_isSinging;
        _characterMenu.IsEnabled = !_isSinging && _characterMenu.Items.Count > 0;
        _loafingMenu.IsEnabled = !_isSinging && GetLoafingCharacterIndex() >= 0;
        _focusMenu.IsEnabled = !_isSinging;
        _moodMenu.IsEnabled = !_isSinging;
        _gazeModeMenu.IsEnabled = !_isSinging;
    }

    private async Task ToggleLoafingModeAsync()
    {
        if (_isLoafingMode)
        {
            await ExitLoafingModeAsync();
        }
        else
        {
            await EnterLoafingModeAsync();
        }
    }

    private async Task EnterLoafingModeAsync()
    {
        var loafingIndex = GetLoafingCharacterIndex();
        if (loafingIndex < 0)
        {
            BuildLoafingMenu();
            return;
        }

        if (!IsLoafingCharacter(_currentCharacterIndex))
        {
            _previousCharacterIndex = _currentCharacterIndex;
        }

        _isLoafingMode = true;
        BuildLoafingMenu();
        if (await SelectCharacterAsync(loafingIndex))
        {
            ShowRandomBubble(["你的胆子真是肥嘟嘟的"]);
            return;
        }

        _isLoafingMode = false;
        BuildLoafingMenu();
    }

    private async Task ExitLoafingModeAsync()
    {
        _isLoafingMode = false;
        var targetIndex = IsNormalCharacterIndex(_previousCharacterIndex)
            ? _previousCharacterIndex
            : GetFirstNormalCharacterIndex();
        BuildLoafingMenu();
        if (await SelectCharacterAsync(targetIndex))
        {
            ShowRandomBubble(["好吧，先假装认真一会儿。"]);
            return;
        }

        _isLoafingMode = true;
        BuildLoafingMenu();
    }

    private async Task SelectNormalCharacterAsync(int index)
    {
        if (!IsNormalCharacterIndex(index))
        {
            return;
        }

        var wasLoafing = _isLoafingMode;
        _isLoafingMode = false;
        _previousCharacterIndex = index;
        BuildLoafingMenu();
        if (!await SelectCharacterAsync(index) && wasLoafing)
        {
            _isLoafingMode = true;
            BuildLoafingMenu();
        }
    }

    private int GetLoafingCharacterIndex()
    {
        for (var index = 0; index < _characters.Count; index++)
        {
            if (IsLoafingCharacter(index))
            {
                return index;
            }
        }

        return -1;
    }

    private int GetFirstNormalCharacterIndex()
    {
        for (var index = 0; index < _characters.Count; index++)
        {
            if (!IsLoafingCharacter(index))
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsNormalCharacterIndex(int index) =>
        index >= 0 && index < _characters.Count && !IsLoafingCharacter(index);

    private bool IsLoafingCharacter(int index) =>
        index >= 0 && index < _characters.Count && _characters[index].IsLoafing;

    private void StartSinging()
    {
        if (_isSinging)
        {
            return;
        }

        try
        {
            if (_singingAnimation is null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(SingingGifResourceName)
                    ?? throw new InvalidDataException("EXE 内未找到唱歌 GIF 资源。");
                _singingAnimation = GifAnimation.Load(stream);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法播放唱歌动画：\n\n{exception.Message}",
                "CapyLulu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var previousCenterX = Left + (ActualWidth / 2);
        var previousBottom = Top + ActualHeight;
        _animationTimer.Stop();
        _idleEasterEggTimer.Stop();
        _bubbleTimer.Stop();
        _isPlayingInteraction = false;
        _hasBufferedClick = false;
        _interactionState = PetInteractionState.Idle;
        _gazeDirection = -1;
        ResetGazeTracking();
        ResetMotionTransform();

        _isSinging = true;
        _singingStartedSeconds = NowSeconds;
        _singingFrameIndex = -1;
        _singingLyricIndex = -1;
        _singingSupportCount = 0;
        _singingNoSupportPromptShown = false;
        _bubble.Opacity = 1;
        _singingSupportButton.Visibility = Visibility.Visible;
        HideSingingResponse();
        _singingEffectsLayer.Children.Clear();
        UpdatePetSize();
        UpdateLayout();
        Left = previousCenterX - (ActualWidth / 2);
        Top = previousBottom - ActualHeight;
        KeepWindowReachable();
        UpdateSingingMenuState();
        UpdateSingingPlayback(0);
        _singingTimer.Start();
    }

    private void OnSingingTick(object? sender, EventArgs e)
    {
        if (!_isSinging || _singingAnimation is null)
        {
            _singingTimer.Stop();
            return;
        }

        var elapsedSeconds = Math.Max(0, NowSeconds - _singingStartedSeconds);
        var songDuration = _singingAnimation.DurationSeconds * _dialogues.SingingLyrics.Length;
        if (elapsedSeconds >= songDuration)
        {
            StopSinging();
            return;
        }

        UpdateSingingLyricPulse(elapsedSeconds);
        if (_singingSupportCount == 0
            && !_singingNoSupportPromptShown
            && elapsedSeconds >= songDuration * 0.65)
        {
            _singingNoSupportPromptShown = true;
            ShowSingingResponse("是不是唱得太投入，把你听睡着了？");
        }

        UpdateSingingPlayback(elapsedSeconds);
    }

    private void UpdateSingingPlayback(double elapsedSeconds)
    {
        if (_singingAnimation is null)
        {
            return;
        }

        var lyricIndex = Math.Min(
            _dialogues.SingingLyrics.Length - 1,
            (int)(elapsedSeconds / _singingAnimation.DurationSeconds));
        if (lyricIndex != _singingLyricIndex)
        {
            _singingLyricIndex = lyricIndex;
            ShowSingingLyric(_dialogues.SingingLyrics[lyricIndex]);
        }

        var loopSeconds = elapsedSeconds % _singingAnimation.DurationSeconds;
        var frameIndex = _singingAnimation.GetFrameIndex(loopSeconds);
        if (frameIndex != _singingFrameIndex)
        {
            _singingFrameIndex = frameIndex;
            _petImage.Source = _singingAnimation.Frames[frameIndex];
        }
    }

    private void ShowSingingLyric(string lyric)
    {
        _bubbleTimer.Stop();
        _bubbleText.Text = lyric;
        _bubble.Visibility = Visibility.Visible;
        SetBubbleTailVisibility(Visibility.Visible);
    }

    private void UpdateSingingLyricPulse(double elapsedSeconds)
    {
        const double pulseSeconds = 1.4;
        var phase = elapsedSeconds * Math.PI * 2 / pulseSeconds;
        _bubble.Opacity = 0.90 + ((Math.Sin(phase) + 1) * 0.05);
    }

    private void RegisterSingingSupport()
    {
        if (!_isSinging)
        {
            return;
        }

        _singingSupportCount++;
        ShowSingingResponse(_singingSupportCount == 1
            ? "你有在认真听诶～"
            : "好啦好啦，我知道我唱得不错。");
        SpawnSingingSupportEffect();
        PlaySingingReaction();
    }

    private void ShowSingingResponse(string message)
    {
        _singingResponseTimer.Stop();
        _singingResponseText.Text = message;
        _singingResponse.Visibility = Visibility.Visible;
        _singingResponseTimer.Start();
    }

    private void HideSingingResponse()
    {
        _singingResponseTimer.Stop();
        _singingResponse.Visibility = _isSinging
            ? Visibility.Hidden
            : Visibility.Collapsed;
    }

    private void SpawnSingingSupportEffect()
    {
        var symbol = (_singingSupportCount % 3) switch
        {
            1 => "♥",
            2 => "♪",
            _ => "♫"
        };
        var effect = new TextBlock
        {
            Text = symbol,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = symbol == "♥"
                ? new SolidColorBrush(Color.FromRgb(245, 82, 132))
                : new SolidColorBrush(Color.FromRgb(119, 83, 190)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            IsHitTestVisible = false
        };
        var scale = new ScaleTransform(0.85, 0.85);
        var translate = new TranslateTransform(_random.NextDouble() * 12 - 6, 18);
        effect.RenderTransformOrigin = new Point(0.5, 0.5);
        effect.RenderTransform = new TransformGroup
        {
            Children = { scale, translate }
        };
        _singingEffectsLayer.Children.Add(effect);

        var duration = TimeSpan.FromMilliseconds(720);
        var rise = new DoubleAnimation(18, -62, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var grow = new DoubleAnimation(0.85, 1.20, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420))
        {
            BeginTime = TimeSpan.FromMilliseconds(300),
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => _singingEffectsLayer.Children.Remove(effect);
        translate.BeginAnimation(TranslateTransform.YProperty, rise);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        effect.BeginAnimation(OpacityProperty, fade);
    }

    private void PlaySingingReaction()
    {
        var quick = TimeSpan.FromMilliseconds(145);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        DoubleAnimation Reaction(double target) => new(0, target, quick)
        {
            AutoReverse = true,
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        DoubleAnimation ScaleReaction(double target) => new(1, target, quick)
        {
            AutoReverse = true,
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };

        switch ((_singingSupportCount - 1) % 3)
        {
            case 0:
                _interactionScale.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleReaction(1.08));
                _interactionScale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleReaction(1.08));
                _motionTranslate.BeginAnimation(TranslateTransform.YProperty, Reaction(-8));
                break;
            case 1:
                _interactionScale.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleReaction(0.97));
                _interactionScale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleReaction(0.97));
                _motionRotate.BeginAnimation(RotateTransform.AngleProperty, Reaction(-6));
                break;
            default:
                _interactionScale.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleReaction(1.08));
                _interactionScale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleReaction(0.94));
                _motionRotate.BeginAnimation(RotateTransform.AngleProperty, Reaction(5));
                break;
        }
    }

    private void ResetSingingReactionAnimations()
    {
        _interactionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _interactionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _motionRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _motionTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ResetMotionTransform();
    }

    private void StopSinging()
    {
        if (!_isSinging)
        {
            return;
        }

        var previousCenterX = Left + (ActualWidth / 2);
        var previousBottom = Top + ActualHeight;
        var receivedSupport = _singingSupportCount > 0;
        _singingTimer.Stop();
        _isSinging = false;
        _singingFrameIndex = -1;
        _singingLyricIndex = -1;
        _bubble.Opacity = 1;
        _singingSupportButton.Visibility = Visibility.Collapsed;
        HideSingingResponse();
        _singingEffectsLayer.Children.Clear();
        ResetSingingReactionAnimations();
        HideBubble();
        UpdatePetSize();
        UpdateLayout();
        Left = previousCenterX - (ActualWidth / 2);
        Top = previousBottom - ActualHeight;
        KeepWindowReachable();
        UpdateSingingMenuState();
        StartIdle();
        ShowTemporaryBubble(
            receivedSupport
                ? "谢谢你的应援，再送你一个飞吻～"
                : "没有掌声也没关系，我自己鼓掌。",
            TimeSpan.FromMilliseconds(3600));
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

    private void BuildGazeModeMenu()
    {
        _gazeModeMenu.Header = $"注视：{(_gazeMode == PetGazeMode.Quiet ? "安静" : "跟随")}";
        _gazeModeMenu.Items.Clear();
        AddGazeModeMenuItem(PetGazeMode.Quiet, "安静");
        AddGazeModeMenuItem(PetGazeMode.Follow, "跟随");
    }

    private void AddGazeModeMenuItem(PetGazeMode mode, string label)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = mode == _gazeMode
        };
        item.Click += (_, _) => SetGazeMode(mode);
        _gazeModeMenu.Items.Add(item);
    }

    private void StartFocusSession()
    {
        var now = DateTimeOffset.UtcNow;
        if (_focusSession.IsActive(now))
        {
            var remaining = _focusSession.GetRemaining(now);
            UpdateFocusMenu(remaining);
            ShowRandomBubble(_isLoafingMode
                ? [$"先装忙一会儿，还剩 {FormatFocusRemaining(remaining)}。"]
                : [$"我还在陪你专注，剩下 {FormatFocusRemaining(remaining)}，慢慢来。"]);
            return;
        }

        var startedRemaining = _focusSession.Start(now, PetBehaviorOptions.FocusDuration);
        UpdateFocusMenu(startedRemaining);
        _focusTimer.Start();
        ShowRandomBubble(_isLoafingMode
            ? ["摸鱼暂停，先认真十分钟。"]
            : ["接下来的 10 分钟，我安静陪你专注。"]);
    }

    private void UpdateFocusTimer()
    {
        var remaining = _focusSession.GetRemaining(DateTimeOffset.UtcNow);
        if (remaining <= TimeSpan.Zero)
        {
            _focusTimer.Stop();
            _focusMenu.Header = "专注模式：10 分钟";
            HideFocusCountdown();
            if (!_isSinging)
            {
                ShowRandomBubble(_isLoafingMode
                    ? ["十分钟结束，奖励你光明正大摸会儿鱼。"]
                    : ["10 分钟完成啦，你很棒。伸个懒腰，我陪你休息一下～"]);
            }
            return;
        }

        UpdateFocusMenu(remaining);
    }

    private void UpdateFocusMenu(TimeSpan remaining)
    {
        _focusMenu.Header = "专注模式：进行中";
        _focusCountdownText.Text = $"专注 {FormatFocusRemaining(remaining)}";
        if (_focusCountdown.Visibility != Visibility.Visible)
        {
            _focusCountdown.Visibility = Visibility.Visible;
            UpdateLayout();
            if (IsLoaded)
            {
                KeepWindowReachable();
            }
        }
    }

    private void HideFocusCountdown()
    {
        if (_focusCountdown.Visibility == Visibility.Collapsed)
        {
            return;
        }

        _focusCountdown.Visibility = Visibility.Collapsed;
        UpdateLayout();
        if (IsLoaded)
        {
            KeepWindowReachable();
        }
    }

    private static string FormatFocusRemaining(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void SetGazeMode(PetGazeMode mode)
    {
        if (_gazeMode == mode)
        {
            BuildGazeModeMenu();
            return;
        }

        _gazeMode = mode;
        BuildGazeModeMenu();
        LeavePointerGaze();
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

    private async Task<bool> SelectCharacterAsync(int index)
    {
        if (index < 0 || index >= _characters.Count)
        {
            return false;
        }

        _animationTimer.Stop();
        Cursor = Cursors.Wait;
        try
        {
            var character = _characters[index];
            var sheet = await Task.Run(() => _characterCatalog.LoadSprite(character));
            var previousCenterX = Left + (ActualWidth / 2);
            var previousBottom = Top + ActualHeight;
            _spriteSheet = sheet;
            _currentCharacterIndex = index;
            _clickRows = sheet.GetPlayableClickRows();
            _nextInteractionRow = 0;
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
            BuildLoafingMenu();
            SaveSettings();
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法读取这套动作图：\n\n{exception.Message}",
                "CapyLulu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void UpdateCharacterMenuChecks()
    {
        foreach (var menuItem in _characterMenu.Items)
        {
            if (menuItem is MenuItem { Tag: int characterIndex } item)
            {
                item.IsChecked = !_isLoafingMode && characterIndex == _currentCharacterIndex;
            }
        }
    }

    private void StartIdle()
    {
        _isPlayingInteraction = false;
        _interactionState = PetInteractionState.Idle;
        _gazeDirection = -1;
        ResetGazeTracking();
        _currentRow = _spriteSheet?.Actions.GetRow(PetAction.Idle, _spriteSheet.Rows) ?? 0;
        _currentFrame = 0;
        ResetMotionTransform();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(GetIdleFrameInterval());
        ShowCurrentFrame();
        _animationTimer.Start();
        ResetIdleEasterEggTimer();
    }

    private void StartNextInteraction(bool showBubble = true)
    {
        if (_spriteSheet is null || _clickRows.Count == 0)
        {
            return;
        }

        _isPlayingInteraction = true;
        _interactionState = PetInteractionState.ClickAction;
        _idleEasterEggTimer.Stop();
        _currentRow = _clickRows[_nextInteractionRow % _clickRows.Count];
        _currentFrame = 0;
        _nextInteractionRow = (_nextInteractionRow + 1) % _clickRows.Count;

        _animationTimer.Interval = TimeSpan.FromMilliseconds(165);
        ShowCurrentFrame();
        if (showBubble)
        {
            ShowRandomBubble();
        }
        _animationTimer.Start();
    }

    private bool StartMappedAction(PetAction action, PetInteractionState state, bool showBubble = false)
    {
        if (_spriteSheet is null)
        {
            return false;
        }

        var row = _spriteSheet.Actions.GetRow(action, _spriteSheet.Rows);
        if (row is null)
        {
            return false;
        }

        _isPlayingInteraction = true;
        _interactionState = state;
        _idleEasterEggTimer.Stop();
        _currentRow = row.Value;
        _currentFrame = 0;
        _animationTimer.Interval = TimeSpan.FromMilliseconds(150);
        ShowCurrentFrame();
        if (showBubble)
        {
            ShowRandomBubble();
        }

        _animationTimer.Start();
        return true;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_spriteSheet is null)
        {
            return;
        }

        _currentFrame++;
        if (_currentFrame < _spriteSheet.GetPlaybackFrameCount(_currentRow))
        {
            ShowCurrentFrame();
            return;
        }

        if (_interactionState == PetInteractionState.GestureReaction)
        {
            StartIdle();
        }
        else if (_isPlayingInteraction)
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
        if (!_isSinging
            && _spriteSheet is not null
            && _currentRow >= 0
            && _currentRow < _spriteSheet.Rows)
        {
            _petImage.Source = _spriteSheet[_currentRow, _currentFrame];
        }
    }

    private void OnPetMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isSinging)
        {
            RegisterSingingSupport();
            e.Handled = true;
            return;
        }

        ResetIdleEasterEggTimer();
        _mouseDown = true;
        _isDragging = false;
        _mouseDownScreen = GetCursorPositionInDip();
        _windowDownPosition = new Point(Left, Top);
        _dragTargetPosition = _windowDownPosition;
        _lastRenderedWindowPosition = _windowDownPosition;
        _motionTracker.Reset(_mouseDownScreen, NowSeconds);
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
        _motionTracker.Add(cursor, NowSeconds);
        var delta = cursor - _mouseDownScreen;
        if (!_isDragging && Math.Abs(delta.X) + Math.Abs(delta.Y) >= DragThreshold)
        {
            _isDragging = true;
            StartDragInteraction(cursor);
            _petImage.Cursor = Cursors.SizeAll;
        }

        if (_isDragging)
        {
            _dragTargetPosition = new Point(
                _windowDownPosition.X + delta.X,
                _windowDownPosition.Y + delta.Y);
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
        var cursor = GetCursorPositionInDip();
        _motionTracker.Add(cursor, NowSeconds);
        _petImage.ReleaseMouseCapture();
        _petImage.Cursor = Cursors.Hand;

        if (_isDragging)
        {
            Left = _dragTargetPosition.X;
            Top = _dragTargetPosition.Y;
            KeepWindowReachable();
            _dragTargetPosition = new Point(Left, Top);
            var releaseVelocity = _motionTracker.GetVelocity();
            var gesture = _motionTracker.DetectGesture();
            StartDropInteraction(releaseVelocity, gesture);
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

    private void StartDragInteraction(Point cursor)
    {
        _hasBufferedClick = false;
        _isPlayingInteraction = false;
        _animationTimer.Stop();
        _idleEasterEggTimer.Stop();
        HideBubble();
        _interactionState = PetInteractionState.Lifting;
        _dragStartedSeconds = NowSeconds;
        _dragFrameDistance = 0;
        _smoothedVelocity = default;
        _springVelocity = default;
        _motionTracker.Add(cursor, _dragStartedSeconds);
        SetMotionAction(PetAction.Lift, resetFrame: true);
        ShowRandomBubble(_isLoafingMode
            ? _dialogues.LoafingDragBubbleMessages
            : _dialogues.DragBubbleMessages);
    }

    private void StartDropInteraction(Vector releaseVelocity, PetGesture gesture)
    {
        _interactionState = PetInteractionState.Dropping;
        _pendingGesture = gesture;
        _dropStartedSeconds = NowSeconds;
        _springVelocity = new Vector(
            Math.Clamp(releaseVelocity.X * 0.018, -22, 22),
            Math.Clamp(releaseVelocity.Y * 0.012, -16, 16));
        _springOffset = new Vector(
            Math.Clamp(_motionTranslate.X, -10, 10),
            Math.Clamp(_motionTranslate.Y, -8, 8));
        _animationTimer.Stop();

        if (_spriteSheet is not null)
        {
            var dropRow = _spriteSheet.Actions.GetRow(PetAction.Drop, _spriteSheet.Rows);
            if (dropRow is not null)
            {
                _currentRow = dropRow.Value;
                _currentFrame = Math.Max(0, (_spriteSheet.GetFrameCount(_currentRow) - 1) / 2);
                ShowCurrentFrame();
            }
        }
    }

    private void StartGestureReaction(PetGesture gesture)
    {
        var action = gesture switch
        {
            PetGesture.HorizontalFlick => PetAction.GestureFlick,
            PetGesture.Shake => PetAction.GestureShake,
            PetGesture.LiftDrop => PetAction.GestureLiftDrop,
            _ => PetAction.Click
        };

        var messages = _dialogues.GetGestureMessages(gesture, _isLoafingMode);
        if (messages.Count > 0)
        {
            ShowRandomBubble(messages);
        }

        if (!StartMappedAction(action, PetInteractionState.GestureReaction))
        {
            StartIdle();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = NowSeconds;
        if (_lastRenderSeconds <= 0)
        {
            _lastRenderSeconds = now;
            return;
        }

        var elapsed = Math.Clamp(now - _lastRenderSeconds, 0.001, 0.05);
        _lastRenderSeconds = now;
        if (!IsLoaded || Visibility != Visibility.Visible || _spriteSheet is null)
        {
            return;
        }

        if (_isSinging)
        {
            return;
        }

        if (_isDragging)
        {
            UpdateDragging(now, elapsed);
            return;
        }

        if (_interactionState == PetInteractionState.Dropping)
        {
            UpdateDropping(now, elapsed);
            return;
        }

        if (_interactionState is PetInteractionState.Idle or PetInteractionState.Looking)
        {
            UpdatePointerGaze(now);
        }
    }

    private void UpdateDragging(double now, double elapsed)
    {
        Left = _dragTargetPosition.X;
        Top = _dragTargetPosition.Y;
        var renderedDelta = new Point(Left, Top) - _lastRenderedWindowPosition;
        _lastRenderedWindowPosition = new Point(Left, Top);

        var sampledVelocity = _motionTracker.GetVelocity();
        var smoothing = 1.0 - Math.Exp(-elapsed * 14.0);
        _smoothedVelocity += (sampledVelocity - _smoothedVelocity) * smoothing;

        var speed = _smoothedVelocity.Length;
        _motionTranslate.X = Math.Clamp(-_smoothedVelocity.X * 0.007, -9, 9);
        _motionTranslate.Y = Math.Clamp(-_smoothedVelocity.Y * 0.004, -6, 6);
        _motionRotate.Angle = Math.Clamp(-_smoothedVelocity.X * 0.0032, -5, 5);
        var stretch = Math.Clamp(speed / 2400.0, 0, 0.035);
        _interactionScale.ScaleX = 1 + stretch;
        _interactionScale.ScaleY = 1 - (stretch * 0.65);

        if (now - _dragStartedSeconds < LiftDurationSeconds)
        {
            _interactionState = PetInteractionState.Lifting;
            SetMotionAction(PetAction.Lift);
            AdvanceTimedMotionFrame(now - _dragStartedSeconds, LiftDurationSeconds);
            return;
        }

        var verticalDominant = Math.Abs(_smoothedVelocity.Y) > Math.Abs(_smoothedVelocity.X) * 1.3
            && Math.Abs(_smoothedVelocity.Y) >= DragDirectionEnterSpeed;
        if (verticalDominant)
        {
            _interactionState = PetInteractionState.DraggingNeutral;
            SetMotionAction(PetAction.Lift);
        }
        else
        {
            UpdateDragDirectionWithHysteresis(_smoothedVelocity.X);
        }

        _dragFrameDistance += renderedDelta.Length;
        var frameCount = _spriteSheet?.GetFrameCount(_currentRow) ?? 0;
        if (frameCount > 0)
        {
            var strideDistance = Math.Max(12.0, (_petImage.ActualWidth > 0 ? _petImage.ActualWidth : _petImage.Width) * 0.09);
            _currentFrame = (int)Math.Floor(_dragFrameDistance / strideDistance) % frameCount;
            ShowCurrentFrame();
        }

    }

    private void UpdateDragDirectionWithHysteresis(double horizontalVelocity)
    {
        switch (_interactionState)
        {
            case PetInteractionState.DraggingLeft when horizontalVelocity > DragDirectionSwitchSpeed:
                _interactionState = PetInteractionState.DraggingRight;
                SetMotionAction(PetAction.DragRight, resetFrame: true);
                break;
            case PetInteractionState.DraggingRight when horizontalVelocity < -DragDirectionSwitchSpeed:
                _interactionState = PetInteractionState.DraggingLeft;
                SetMotionAction(PetAction.DragLeft, resetFrame: true);
                break;
            case PetInteractionState.DraggingLeft:
                SetMotionAction(PetAction.DragLeft);
                break;
            case PetInteractionState.DraggingRight:
                SetMotionAction(PetAction.DragRight);
                break;
            default:
                if (horizontalVelocity >= DragDirectionEnterSpeed)
                {
                    _interactionState = PetInteractionState.DraggingRight;
                    SetMotionAction(PetAction.DragRight, resetFrame: true);
                }
                else if (horizontalVelocity <= -DragDirectionEnterSpeed)
                {
                    _interactionState = PetInteractionState.DraggingLeft;
                    SetMotionAction(PetAction.DragLeft, resetFrame: true);
                }
                else
                {
                    _interactionState = PetInteractionState.DraggingNeutral;
                    SetMotionAction(PetAction.Lift);
                }

                break;
        }
    }

    private void AdvanceTimedMotionFrame(double progressSeconds, double durationSeconds)
    {
        if (_spriteSheet is null)
        {
            return;
        }

        var frameCount = _spriteSheet.GetFrameCount(_currentRow);
        if (frameCount <= 0)
        {
            return;
        }

        var progress = Math.Clamp(progressSeconds / Math.Max(0.01, durationSeconds), 0, 1);
        var apexFrame = Math.Max(0, (frameCount - 1) / 2);
        _currentFrame = Math.Min(apexFrame, (int)Math.Floor(progress * (apexFrame + 1)));
        _motionTranslate.Y = -Math.Sin(progress * Math.PI / 2) * 11;
        ShowCurrentFrame();
    }

    private void UpdateDropping(double now, double elapsed)
    {
        var spring = 145.0;
        var damping = 20.0;
        var accelerationX = (-spring * _springOffset.X) - (damping * _springVelocity.X);
        var accelerationY = (-spring * _springOffset.Y) - (damping * _springVelocity.Y);
        _springVelocity += new Vector(accelerationX * elapsed, accelerationY * elapsed);
        _springOffset += _springVelocity * elapsed;

        var relaxation = 1.0 - Math.Exp(-elapsed * 13.0);
        _interactionScale.ScaleX += (1 - _interactionScale.ScaleX) * relaxation;
        _interactionScale.ScaleY += (1 - _interactionScale.ScaleY) * relaxation;

        var dropProgress = Math.Clamp((now - _dropStartedSeconds) / DropDurationSeconds, 0, 1);
        _motionTranslate.X = _springOffset.X;
        _motionTranslate.Y = _springOffset.Y - ((1 - dropProgress) * 11);
        _motionRotate.Angle = Math.Clamp(_motionTranslate.X * 0.42, -5, 5);
        if (_spriteSheet is not null)
        {
            var frameCount = _spriteSheet.GetFrameCount(_currentRow);
            if (frameCount > 0)
            {
                var apexFrame = Math.Max(0, (frameCount - 1) / 2);
                _currentFrame = Math.Clamp(
                    apexFrame + (int)Math.Round(dropProgress * (frameCount - 1 - apexFrame)),
                    apexFrame,
                    frameCount - 1);
                ShowCurrentFrame();
            }
        }

        var springSettled = Math.Abs(_springOffset.X) < 0.25
            && Math.Abs(_springOffset.Y) < 0.25
            && _springVelocity.Length < 3;
        if (dropProgress < 1 || (!springSettled && now - _dropStartedSeconds < 0.58))
        {
            return;
        }

        var gesture = _pendingGesture;
        _pendingGesture = PetGesture.None;
        ResetMotionTransform();
        if (gesture == PetGesture.None)
        {
            StartIdle();
        }
        else
        {
            StartGestureReaction(gesture);
        }
    }

    private void SetMotionAction(PetAction action, bool resetFrame = false)
    {
        if (_spriteSheet is null)
        {
            return;
        }

        var row = _spriteSheet.Actions.GetRow(action, _spriteSheet.Rows)
            ?? (_clickRows.Count > 0 ? _clickRows[0] : 0);
        if (_currentRow == row && !resetFrame)
        {
            return;
        }

        _currentRow = row;
        if (resetFrame)
        {
            _currentFrame = 0;
            _dragFrameDistance = 0;
        }

        ShowCurrentFrame();
    }

    private void UpdatePointerGaze(double now)
    {
        if (_gazeMode == PetGazeMode.Quiet
            || _spriteSheet is null
            || !_spriteSheet.Actions.HasLookDirections
            || _contextMenuOpen)
        {
            LeavePointerGaze();
            return;
        }

        if (now - _lastGazeUpdateSeconds < GazeSampleIntervalSeconds)
        {
            return;
        }

        _lastGazeUpdateSeconds = now;
        var cursor = GetCursorPositionInDip();
        var cursorSpeed = SampleGazeCursorSpeed(cursor, now);
        var imageWidth = _petImage.ActualWidth > 0 ? _petImage.ActualWidth : _petImage.Width;
        var imageHeight = _petImage.ActualHeight > 0 ? _petImage.ActualHeight : _petImage.Height;
        var localFocus = _petImage.TranslatePoint(new Point(imageWidth * 0.5, imageHeight * 0.38), this);
        var focus = new Point(Left + localFocus.X, Top + localFocus.Y);
        var delta = cursor - focus;
        var distance = delta.Length;
        if (distance < GazeDeadZone || distance > GazeMaximumDistance)
        {
            _pendingGazeDirection = -1;
            if (_gazeOutsideSince < 0)
            {
                _gazeOutsideSince = now;
            }

            if (now - _gazeOutsideSince >= GazeExitDelaySeconds)
            {
                LeavePointerGaze();
            }

            return;
        }

        _gazeOutsideSince = -1;
        if (cursorSpeed > GazeActivationSpeed)
        {
            _pendingGazeDirection = -1;
            return;
        }

        var angle = (Math.Atan2(delta.X, -delta.Y) * 180 / Math.PI + 360) % 360;
        var candidate = (int)Math.Round(angle / GazeSectorDegrees) % 16;
        if (_gazeDirection >= 0)
        {
            var currentCenter = _gazeDirection * GazeSectorDegrees;
            if (GazeDirectionMath.CircularAngleDistance(angle, currentCenter)
                <= (GazeSectorDegrees / 2) + GazeHysteresisDegrees)
            {
                candidate = _gazeDirection;
            }
        }

        if (_pendingGazeDirection != candidate)
        {
            _pendingGazeDirection = candidate;
            _pendingGazeDirectionSince = now;
            return;
        }

        if (now - _pendingGazeDirectionSince < GazeDirectionDwellSeconds)
        {
            return;
        }

        var nextDirection = _gazeDirection < 0
            ? candidate
            : GazeDirectionMath.StepToward(_gazeDirection, candidate);
        if (_gazeDirection >= 0
            && nextDirection != _gazeDirection
            && now - _lastGazeStepSeconds < GazeStepIntervalSeconds)
        {
            return;
        }

        var lookFrame = _spriteSheet.GetLookFrame(nextDirection);
        if (lookFrame is null)
        {
            LeavePointerGaze();
            return;
        }

        if (_interactionState != PetInteractionState.Looking)
        {
            _animationTimer.Stop();
            _idleEasterEggTimer.Stop();
            _interactionState = PetInteractionState.Looking;
        }

        if (_gazeDirection != nextDirection)
        {
            _gazeDirection = nextDirection;
            _lastGazeStepSeconds = now;
            _petImage.Source = lookFrame;
        }
    }

    private void LeavePointerGaze()
    {
        if (_interactionState == PetInteractionState.Looking)
        {
            StartIdle();
        }

        _gazeDirection = -1;
        ResetGazeTracking();
    }

    private double SampleGazeCursorSpeed(Point cursor, double now)
    {
        if (!_hasGazeCursorSample || now <= _lastGazeCursorSampleSeconds)
        {
            _lastGazeCursorPosition = cursor;
            _lastGazeCursorSampleSeconds = now;
            _hasGazeCursorSample = true;
            return double.PositiveInfinity;
        }

        var elapsed = now - _lastGazeCursorSampleSeconds;
        var speed = (cursor - _lastGazeCursorPosition).Length / elapsed;
        _lastGazeCursorPosition = cursor;
        _lastGazeCursorSampleSeconds = now;
        return speed;
    }

    private void ResetGazeTracking()
    {
        _pendingGazeDirection = -1;
        _pendingGazeDirectionSince = 0;
        _lastGazeStepSeconds = 0;
        _gazeOutsideSince = -1;
        _hasGazeCursorSample = false;
        _lastGazeCursorSampleSeconds = 0;
    }

    private void ResetMotionTransform()
    {
        _interactionScale.ScaleX = 1;
        _interactionScale.ScaleY = 1;
        _motionRotate.Angle = 0;
        _motionTranslate.X = 0;
        _motionTranslate.Y = 0;
        _springVelocity = default;
        _springOffset = default;
        _smoothedVelocity = default;
    }

    private static double NowSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

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
        SaveSettings();
    }

    private void UpdatePetSize()
    {
        var frameWidth = _spriteSheet?.FrameWidth ?? 288;
        var frameHeight = _spriteSheet?.FrameHeight ?? 312;
        if (_isSinging && _singingAnimation is not null)
        {
            var fitScale = Math.Min(
                frameWidth / (double)_singingAnimation.PixelWidth,
                frameHeight / (double)_singingAnimation.PixelHeight);
            _petImage.Width = _singingAnimation.PixelWidth * fitScale * _scale;
            _petImage.Height = _singingAnimation.PixelHeight * fitScale * _scale;
            return;
        }

        _petImage.Width = frameWidth * _scale;
        _petImage.Height = frameHeight * _scale;
    }

    private void ShowRandomBubble()
    {
        ShowRandomBubble(_isLoafingMode ? _dialogues.LoafingBubbleMessages : _dialogues.BubbleMessages);
    }

    private void ShowRandomBubble(IReadOnlyList<string> messages)
    {
        var index = _random.Next(messages.Count);
        if (messages.Count > 1 && index == _lastBubbleIndex)
        {
            index = (index + 1) % messages.Count;
        }

        _lastBubbleIndex = index;
        var message = messages[index];
        _bubbleText.Text = message;
        _bubble.Visibility = Visibility.Visible;
        SetBubbleTailVisibility(Visibility.Visible);
        _bubbleTimer.Stop();
        var displayMilliseconds = Math.Clamp(1400 + (message.Length * 60), 1900, 3400);
        _bubbleTimer.Interval = TimeSpan.FromMilliseconds(displayMilliseconds);
        _bubbleTimer.Start();
    }

    private void ShowTemporaryBubble(string message, TimeSpan duration)
    {
        _bubbleTimer.Stop();
        _bubbleText.Text = message;
        _bubble.Visibility = Visibility.Visible;
        SetBubbleTailVisibility(Visibility.Visible);
        _bubbleTimer.Interval = duration;
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

    private IReadOnlyList<string> GetMoodMessages()
    {
        if (_isLoafingMode)
        {
            return _dialogues.LoafingIdleMessages;
        }

        return _mood switch
        {
            PetMood.Sleepy => _dialogues.SleepyMessages,
            PetMood.Working => _dialogues.WorkingMessages,
            _ => _dialogues.HappyMessages
        };
    }

    private void SetBubbleTailVisibility(Visibility visibility)
    {
        _bubbleTail.Visibility = visibility;
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
        _settings.Topmost = Topmost;
        _settings.Mood = _mood;
        _settings.GazeMode = _gazeMode;
        _settings.LoafingMode = _isLoafingMode;
        var savedCharacterIndex = _isLoafingMode && IsNormalCharacterIndex(_previousCharacterIndex)
            ? _previousCharacterIndex
            : _currentCharacterIndex;
        _settings.SelectedCharacterId = savedCharacterIndex >= 0
            && savedCharacterIndex < _characters.Count
            ? _characters[savedCharacterIndex].Id
            : null;
        _settings.SelectedCharacter = null;
        SettingsStore.Save(_settings);
    }

    private Point GetCursorPositionInDip()
    {
        var devicePoint = NativeCursor.GetScreenPosition();
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(devicePoint) ?? devicePoint;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = (HwndSource)PresentationSource.FromVisual(this)!;
        _windowSource.AddHook(WindowMessageHook);
        _hotkeyRegistered = GlobalHotkey.Register(
            _windowSource.Handle,
            GlobalHotkey.Control | GlobalHotkey.Alt,
            (uint)KeyInterop.VirtualKeyFromKey(Key.P));
        if (!_hotkeyRegistered)
        {
            MessageBox.Show(
                "无法注册 Ctrl + Alt + P，全局快捷键可能已被其他程序占用。",
                "CapyLulu",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == GlobalHotkey.HotkeyMessage && wParam.ToInt32() == GlobalHotkey.ToggleId)
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
        CompositionTarget.Rendering -= OnRendering;
        _idleEasterEggTimer.Stop();
        _focusTimer.Stop();
        _singingTimer.Stop();
        _singingResponseTimer.Stop();
        if (_windowSource is not null)
        {
            if (_hotkeyRegistered)
            {
                GlobalHotkey.Unregister(_windowSource.Handle);
            }
            _windowSource.RemoveHook(WindowMessageHook);
        }

        SaveSettings();
    }

}
