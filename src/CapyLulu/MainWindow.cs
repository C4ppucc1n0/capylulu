using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Polygon = System.Windows.Shapes.Polygon;

namespace CapyLulu;

internal sealed class MainWindow : Window
{
    private const double MinimumScale = 0.50;
    private const double MaximumScale = 1.00;
    private const double ScaleStep = 0.25;
    private const double DragThreshold = 5.0;
    private const double DragDirectionEnterSpeed = 70.0;
    private const double DragDirectionSwitchSpeed = 135.0;
    private const double LiftDurationSeconds = 0.14;
    private const double DropDurationSeconds = 0.30;
    private const double GazeDeadZone = 90.0;
    private const double GazeMaximumDistance = 460.0;
    private const double GazeSectorDegrees = 22.5;
    private const double GazeHysteresisDegrees = 5.0;
    private const double GazeSampleIntervalSeconds = 0.045;
    private const double GazeActivationSpeed = 250.0;
    private const double GazeDirectionDwellSeconds = 0.20;
    private const double GazeStepIntervalSeconds = 0.12;
    private const double GazeExitDelaySeconds = 0.30;
    private static readonly TimeSpan FocusDuration = TimeSpan.FromMinutes(10);
    private const int ToggleHotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const string LoafingCharacterFileName =
        "c9d277a85ded3a459c8d55a368cdd9e8-action-sprite.webp";
    private const string SingingGifResourceName =
        "CapyLulu.GifResources.flycapylulu.gif";

    private static readonly string[] SingingLyrics =
    {
        "天空是绵绵的糖～",
        "就算塌下来又怎样～",
        "雨下再大又怎样～",
        "干脆开心的淋一场～",
        "彩虹是微笑的脸～",
        "悲伤bye bye 快乐不需要理由～"
    };

    private static readonly string[] BubbleMessages =
    {
        "你来啦，我刚好也在等你。",
        "今天过得怎么样？慢慢来，我听着呢。",
        "不用一直很厉害，做你自己就很好。",
        "累了就靠一会儿，我不催你。",
        "刚刚那一下，是在和我打招呼吗？",
        "我会安安静静陪你把今天过完。",
        "记得喝口水，也给心情补充一点能量。",
        "如果今天有点难，我们就把它切成小块。",
        "你认真做事的样子，我都有看到。",
        "偶尔发呆没关系，我陪你。",
        "今天也给自己留一点温柔吧。",
        "无论进度快慢，我都站你这边。",
        "要不要先深呼吸一下？我陪你一起。",
        "你不说话也没关系，陪伴不一定要有声音。",
        "刚才辛苦了，来收下一小份夸奖。",
        "偷偷告诉你：你已经做得比想象中好了。",
        "等你忙完，我们一起松口气。",
        "现在心情好吗？不好也可以。",
        "我在这里，不会突然跑掉。",
        "再点一下，就当我们碰了个拳。",
        "嘿，今日份陪伴已经签到。",
        "这次换我给你一点好运气。",
        "你负责向前走，我负责在旁边晃悠。",
        "今天的你，也值得被好好照顾。"
    };

    private static readonly string[] DragBubbleMessages =
    {
        "想换个位置陪你吗？",
        "轻一点抱我，我会乖乖跟着。",
        "放到你一抬眼就能看到的地方吧。",
        "带我一起去新的角落。",
        "慢慢来，我没有急着去哪儿。",
        "这里离你近一点，我喜欢。",
        "搬家途中也要牵好我哦。",
        "你把我放在哪里，我就在哪里陪你。",
        "这个位置不错，能看见你认真生活。",
        "好啦，安全落地，我继续陪你。"
    };

    private static readonly string[] LoafingBubbleMessages =
    {
        "摸鱼不是偷懒，是战略性休息。",
        "老板不在吧？我先帮你望风。",
        "窗口切得这么快，很熟练嘛。",
        "别紧张，我什么都没看见。",
        "这一分钟，先还给自己。",
        "认真摸鱼，也是一门技术。",
        "再发会儿呆，灵感也许就来了。",
        "今天的进度：成功让自己喘口气。"
    };

    private static readonly string[] LoafingDragBubbleMessages =
    {
        "换个更隐蔽的位置。",
        "慢点，别让老板看见我在移动！",
        "带我去一个适合摸鱼的角落。",
        "嘘——正在转移摸鱼阵地。"
    };

    private static readonly string[] LoafingIdleMessages =
    {
        "屏幕亮着，人也醒着，已经很努力了。",
        "我负责放风，你负责放空。",
        "偶尔停一下，脑子才有地方转弯。",
        "放心，刚才那几秒不算浪费。",
        "摸鱼时间很短，快乐要抓紧。"
    };

    private static readonly IReadOnlyDictionary<PetGesture, string[]> GestureMessages =
        new Dictionary<PetGesture, string[]>
        {
            [PetGesture.HorizontalFlick] =
            [
                "哇——慢一点，我想稳稳跟着你。",
                "差点被甩飞啦，还好没有把你弄丢。",
                "被你甩得心都飞起来啦。",
                "抓紧了，这次不会走丢。"
            ],
            [PetGesture.Shake] =
            [
                "晕乎乎的，不过还在你身边。",
                "世界在摇，我先抱紧一点。",
                "好啦好啦，陪你闹一会儿。",
                "摇完记得一起歇口气。"
            ],
            [PetGesture.LiftDrop] =
            [
                "安全落地！谢谢你稳稳接住我。",
                "起飞一小会儿，再回到你身边。",
                "这趟旅行很短，但有你就很好。",
                "落地成功，继续陪你过今天。"
            ]
        };

    private static readonly IReadOnlyDictionary<PetGesture, string[]> LoafingGestureMessages =
        new Dictionary<PetGesture, string[]>
        {
            [PetGesture.HorizontalFlick] = ["摸鱼证据差点被你甩出去！", "动静小一点，容易暴露。"],
            [PetGesture.Shake] = ["别摇啦，我的摸鱼计划要散架了。", "假装忙碌也不用这么卖力。"],
            [PetGesture.LiftDrop] = ["摸鱼巡逻结束，安全落地。", "阵地转移成功，继续放风。"]
        };

    private static readonly string[] HappyMessages =
    {
        "你一来，我今天的好心情就签到啦。",
        "今天有什么小开心？我想和你一起记住。",
        "好耶，我们慢慢把今天过得闪闪发亮。",
        "看到你，我就忍不住开心一点。",
        "不管今天忙不忙，我都给你留一个笑脸。",
        "走吧，我陪你去遇见一点好事。",
        "你的努力有被看到，我第一个鼓掌。",
        "今天也站在你这边，坚定不换队。",
        "给你一颗不会过期的好心情。",
        "哪怕只完成一件小事，也值得庆祝。"
    };

    private static readonly string[] SleepyMessages =
    {
        "困了就眯一会儿，我替你守着屏幕。",
        "今天慢一点也没关系，我陪你一起缓一缓。",
        "不想说话的话，我们就安静待着。",
        "呼噜……别担心，我没有走远。",
        "肩膀放松一点，先把自己照顾好。",
        "累不是你的错，是身体在提醒你休息。",
        "先闭一会儿眼，醒来我还在。",
        "事情可以等等，你也值得被温柔对待。",
        "今晚早点休息，好吗？我会乖乖陪着。",
        "把疲惫分我一点，你就轻松一点。"
    };

    private static readonly string[] WorkingMessages =
    {
        "我们只管眼前这一小步，其他的等会儿再想。",
        "你专心，我在旁边安静陪着。",
        "卡住也没关系，停一下再继续。",
        "认真工作的你很可靠，也别忘了照顾自己。",
        "不求一下子完成，稳稳推进就很好。",
        "我帮你守住这段专注时间。",
        "做完这一点，就奖励自己喝口水吧。",
        "别怕任务多，我们一个一个来。",
        "今天的进度不定义你，但每一步都算数。",
        "需要喘口气时，抬头就能看到我。"
    };

    private static readonly IReadOnlyDictionary<string, string> CharacterNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["2289a0bd9b469e69aafce7687053de85-action-sprite.webp"] = "小肚噜噜",
            ["3090265bbbee19cf4c27530cc2f75e19-action-sprite-without-columns-5-6-7.webp"] = "眨眼噜噜",
            ["3728b93ded95d888375ff85204f24e13-action-sprite-rows-1-4-aligned-last-column.webp"] = "眼睛噜噜",
            ["c9d277a85ded3a459c8d55a368cdd9e8-action-sprite.webp"] = "摸鱼噜噜",
            ["7c3d1f66c30bf03a042889fe8f435555-action-sprite-without-last-column.webp"] = "读书噜噜",
            ["86de7611b82965839a0286940531cbc6-action-sprite.webp"] = "背带裤噜噜"
        };

    private readonly Image _petImage;
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private readonly Border _focusCountdown;
    private readonly TextBlock _focusCountdownText;
    private readonly Button _singingSupportButton;
    private readonly Grid _singingEffectsLayer;
    private readonly Border _singingResponse;
    private readonly TextBlock _singingResponseText;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _idleEasterEggTimer;
    private readonly DispatcherTimer _focusTimer;
    private readonly DispatcherTimer _singingTimer;
    private readonly DispatcherTimer _singingResponseTimer;
    private readonly Random _random = new();
    private readonly PointerMotionTracker _motionTracker = new();
    private readonly PetSettings _settings;
    private readonly IReadOnlyList<string> _characterResources;
    private readonly MenuItem _characterMenu;
    private readonly MenuItem _loafingMenu;
    private readonly MenuItem _singingMenu;
    private readonly MenuItem _focusMenu;
    private readonly MenuItem _moodMenu;
    private readonly MenuItem _gazeModeMenu;
    private readonly MenuItem _topmostMenu;
    private readonly ScaleTransform _moodScale = new(1, 1);
    private readonly ScaleTransform _interactionScale = new(1, 1);
    private readonly RotateTransform _motionRotate = new(0);
    private readonly TranslateTransform _motionTranslate = new(0, 0);

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
    private PetMood _mood;
    private PetGazeMode _gazeMode;
    private DateTimeOffset? _focusEndsAt;
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
        Topmost = _settings.Topmost;
        _mood = Enum.TryParse<PetMood>(_settings.Mood, ignoreCase: true, out var savedMood)
            ? savedMood
            : PetMood.Happy;
        _gazeMode = Enum.TryParse<PetGazeMode>(_settings.GazeMode, ignoreCase: true, out var savedGazeMode)
            ? savedGazeMode
            : PetGazeMode.Follow;

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
            Children = { _moodScale, _interactionScale, _motionRotate, _motionTranslate }
        };
        RenderOptions.SetBitmapScalingMode(_petImage, BitmapScalingMode.HighQuality);

        _singingSupportButton = new Button
        {
            Content = "♥",
            ToolTip = "给宠物应援",
            Width = 44,
            Height = 44,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(244, 105, 145)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(187, 55, 96)),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _singingSupportButton.Click += (_, _) => RegisterSingingSupport();

        var performanceContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        performanceContent.Children.Add(_petImage);
        performanceContent.Children.Add(_singingSupportButton);

        _singingEffectsLayer = new Grid
        {
            IsHitTestVisible = false,
            ClipToBounds = true
        };

        var performanceLayer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        performanceLayer.Children.Add(performanceContent);
        performanceLayer.Children.Add(_singingEffectsLayer);

        _singingResponseText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(93, 42, 62)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _singingResponse = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 255, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 126, 157)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 6, 0, 0),
            Height = 38,
            MaxWidth = 290,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _singingResponseText
        };

        _focusCountdownText = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _focusCountdown = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 35, 35, 35)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _focusCountdownText
        };

        var root = new Grid { Background = null };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bubbleLayer, 0);
        Grid.SetRow(performanceLayer, 1);
        Grid.SetRow(_singingResponse, 2);
        Grid.SetRow(_focusCountdown, 3);
        root.Children.Add(bubbleLayer);
        root.Children.Add(performanceLayer);
        root.Children.Add(_singingResponse);
        root.Children.Add(_focusCountdown);
        Content = root;

        _characterMenu = new MenuItem { Header = "更换角色" };
        _loafingMenu = new MenuItem
        {
            Header = "摸鱼模式",
            IsCheckable = true
        };
        _loafingMenu.Click += async (_, _) => await ToggleLoafingModeAsync();
        _singingMenu = new MenuItem { Header = "唱歌" };
        _singingMenu.Click += (_, _) => StartSinging();
        _focusMenu = new MenuItem { Header = "专注模式：10 分钟" };
        _focusMenu.Click += (_, _) => StartFocusSession();
        _moodMenu = new MenuItem { Header = "当前状态" };
        _gazeModeMenu = new MenuItem();
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
        contextMenu.Items.Add(_loafingMenu);
        contextMenu.Items.Add(_singingMenu);
        contextMenu.Items.Add(_focusMenu);
        contextMenu.Items.Add(_moodMenu);
        contextMenu.Items.Add(_gazeModeMenu);
        contextMenu.Items.Add(_topmostMenu);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);
        _petImage.ContextMenu = contextMenu;
        contextMenu.Opened += (_, _) => _contextMenuOpen = true;
        contextMenu.Closed += (_, _) => _contextMenuOpen = false;

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

        _petImage.MouseLeftButtonDown += OnPetMouseLeftButtonDown;
        _petImage.MouseMove += OnPetMouseMove;
        _petImage.MouseLeftButtonUp += OnPetMouseLeftButtonUp;
        _petImage.MouseWheel += OnPetMouseWheel;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        CompositionTarget.Rendering += OnRendering;

        _characterResources = DiscoverCharacterResources();
        BuildCharacterMenu();
        BuildLoafingMenu();
        UpdateSingingMenuState();
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
            if (IsLoafingCharacter(index))
            {
                continue;
            }

            var capturedIndex = index;
            var item = new MenuItem
            {
                Header = GetCharacterDisplayName(_characterResources[index], index),
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
        for (var index = 0; index < _characterResources.Count; index++)
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
        for (var index = 0; index < _characterResources.Count; index++)
        {
            if (!IsLoafingCharacter(index))
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsNormalCharacterIndex(int index) =>
        index >= 0 && index < _characterResources.Count && !IsLoafingCharacter(index);

    private bool IsLoafingCharacter(int index) =>
        index >= 0
        && index < _characterResources.Count
        && _characterResources[index].EndsWith(LoafingCharacterFileName, StringComparison.OrdinalIgnoreCase);

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
        var songDuration = _singingAnimation.DurationSeconds * SingingLyrics.Length;
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
            SingingLyrics.Length - 1,
            (int)(elapsedSeconds / _singingAnimation.DurationSeconds));
        if (lyricIndex != _singingLyricIndex)
        {
            _singingLyricIndex = lyricIndex;
            ShowSingingLyric(SingingLyrics[lyricIndex]);
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
        if (_focusEndsAt is DateTimeOffset activeEnd && activeEnd > now)
        {
            UpdateFocusMenu(activeEnd - now);
            ShowRandomBubble(_isLoafingMode
                ? [$"先装忙一会儿，还剩 {FormatFocusRemaining(activeEnd - now)}。"]
                : [$"我还在陪你专注，剩下 {FormatFocusRemaining(activeEnd - now)}，慢慢来。"]);
            return;
        }

        _focusEndsAt = now + FocusDuration;
        UpdateFocusMenu(FocusDuration);
        _focusTimer.Start();
        ShowRandomBubble(_isLoafingMode
            ? ["摸鱼暂停，先认真十分钟。"]
            : ["接下来的 10 分钟，我安静陪你专注。"]);
    }

    private void UpdateFocusTimer()
    {
        if (_focusEndsAt is not DateTimeOffset focusEnd)
        {
            _focusTimer.Stop();
            _focusMenu.Header = "专注模式：10 分钟";
            HideFocusCountdown();
            return;
        }

        var remaining = focusEnd - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _focusEndsAt = null;
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
        if (index < 0 || index >= _characterResources.Count)
        {
            return false;
        }

        _animationTimer.Stop();
        Cursor = Cursors.Wait;
        try
        {
            var resourceName = _characterResources[index];
            var sheet = await Task.Run(() =>
            {
                var assembly = Assembly.GetExecutingAssembly();
                var actions = PetActionManifest.LoadForResource(assembly, resourceName);
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidDataException($"无法打开内嵌动作资源：{resourceName}");
                return SpriteSheet.Load(stream, resourceName, actions);
            });
            var previousCenterX = Left + (ActualWidth / 2);
            var previousBottom = Top + ActualHeight;
            _spriteSheet = sheet;
            _currentCharacterIndex = index;
            _clickRows = sheet.Actions.GetClickRows(sheet.Rows);
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
        ShowRandomBubble(_isLoafingMode ? LoafingDragBubbleMessages : DragBubbleMessages);
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

        var gestureMessages = _isLoafingMode ? LoafingGestureMessages : GestureMessages;
        if (gestureMessages.TryGetValue(gesture, out var messages))
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
            if (CircularAngleDistance(angle, currentCenter) <= (GazeSectorDegrees / 2) + GazeHysteresisDegrees)
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
            : StepGazeDirectionToward(_gazeDirection, candidate);
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

    private static int StepGazeDirectionToward(int current, int target)
    {
        var clockwiseSteps = (target - current + 16) % 16;
        if (clockwiseSteps == 0)
        {
            return current;
        }

        return clockwiseSteps <= 8
            ? (current + 1) % 16
            : (current + 15) % 16;
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

    private static double CircularAngleDistance(double first, double second)
    {
        var difference = Math.Abs(first - second) % 360;
        return difference > 180 ? 360 - difference : difference;
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
        ShowRandomBubble(_isLoafingMode ? LoafingBubbleMessages : BubbleMessages);
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
            return LoafingIdleMessages;
        }

        return _mood switch
        {
            PetMood.Sleepy => SleepyMessages,
            PetMood.Working => WorkingMessages,
            _ => HappyMessages
        };
    }

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
        _settings.Topmost = Topmost;
        _settings.Mood = _mood.ToString();
        _settings.GazeMode = _gazeMode.ToString();
        _settings.LoafingMode = _isLoafingMode;
        var savedCharacterIndex = _isLoafingMode && IsNormalCharacterIndex(_previousCharacterIndex)
            ? _previousCharacterIndex
            : _currentCharacterIndex;
        _settings.SelectedCharacter = savedCharacterIndex >= 0
            && savedCharacterIndex < _characterResources.Count
            ? _characterResources[savedCharacterIndex]
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
        CompositionTarget.Rendering -= OnRendering;
        _idleEasterEggTimer.Stop();
        _focusTimer.Stop();
        _singingTimer.Stop();
        _singingResponseTimer.Stop();
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

    private enum PetGazeMode
    {
        Quiet,
        Follow
    }
}
