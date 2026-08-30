using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CapyLulu;

internal sealed class MatchGameWindow : Window
{
    private const int Rows = 7;
    private const int Columns = 7;
    private const int RewardClearWaves = 2;
    private const string PreferredCelebrationGif = "CapyLulu.GifResources.match-game-celebration.gif";
    private const string FallbackCelebrationGif = "CapyLulu.GifResources.flycapylulu.gif";

    private static readonly TileKind[] TileKinds =
    [
        new("噜噜", "●", Color.FromRgb(255, 187, 86), Color.FromRgb(227, 116, 46)),
        new("星星", "★", Color.FromRgb(255, 224, 85), Color.FromRgb(227, 164, 29)),
        new("爱心", "♥", Color.FromRgb(255, 139, 169), Color.FromRgb(215, 68, 112)),
        new("叶子", "♣", Color.FromRgb(124, 214, 136), Color.FromRgb(45, 151, 87)),
        new("宝石", "◆", Color.FromRgb(134, 183, 255), Color.FromRgb(59, 108, 205)),
        new("花花", "✿", Color.FromRgb(202, 157, 244), Color.FromRgb(125, 76, 182))
    ];

    private static readonly Color[] ConfettiColors =
    [
        Color.FromRgb(255, 93, 115), Color.FromRgb(255, 204, 67),
        Color.FromRgb(65, 201, 190), Color.FromRgb(105, 146, 255),
        Color.FromRgb(190, 112, 235)
    ];

    private readonly Random _random = new();
    private readonly int[,] _cells = new int[Rows, Columns];
    private readonly Button?[,] _tileButtons = new Button?[Rows, Columns];
    private readonly Grid _boardGrid;
    private readonly TextBlock _rewardProgressText;
    private readonly TextBlock _movesText;
    private readonly TextBlock _statusText;
    private readonly Button _restartButton;
    private readonly Grid _rewardOverlay;
    private readonly Border _bonusCard;
    private readonly Border _celebrationPanel;
    private readonly Image _celebrationImage;
    private readonly TextBlock _animationSourceText;
    private readonly Canvas _effectsCanvas;
    private readonly DispatcherTimer _celebrationTimer;
    private readonly Stopwatch _celebrationClock = new();

    private GifAnimation? _celebrationAnimation;
    private int _celebrationFrameIndex = -1;
    private (int Row, int Column)? _selectedCell;
    private (int Row, int Column)? _pointerCell;
    private Point _pointerStart;
    private bool _dragPerformed;
    private bool _suppressClick;
    private bool _busy;
    private int _moves;
    private int _clearWaveCount;
    private int _roundVersion;

    public MatchGameWindow(bool topmost)
    {
        Title = "噜噜消消乐";
        Width = 500;
        Height = 740;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = topmost;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Background = new SolidColorBrush(Color.FromRgb(218, 246, 247));

        var root = new Grid
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(64, 188, 220), Color.FromRgb(220, 248, 235), 90)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var header = new Grid { Margin = new Thickness(25, 18, 25, 10) };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "噜噜消消乐", FontSize = 28, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.25 }
        });
        _rewardProgressText = new TextBlock
        {
            FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(187, 87, 36)),
            Background = new SolidColorBrush(Color.FromArgb(225, 255, 247, 190)),
            Padding = new Thickness(12, 6, 12, 6), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_rewardProgressText, 1);
        header.Children.Add(_rewardProgressText);
        _movesText = new TextBlock
        {
            FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(30, 96, 109)),
            Margin = new Thickness(2, 5, 0, 0)
        };
        Grid.SetRow(_movesText, 1);
        header.Children.Add(_movesText);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _boardGrid = new Grid { Margin = new Thickness(10) };
        for (var row = 0; row < Rows; row++) _boardGrid.RowDefinitions.Add(new RowDefinition());
        for (var column = 0; column < Columns; column++) _boardGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var boardFrame = new Border
        {
            Width = 450, Height = 450, Padding = new Thickness(5), Margin = new Thickness(20, 3, 20, 12),
            CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(110, 17, 101, 145)),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18, ShadowDepth = 5, Opacity = 0.23, Color = Color.FromRgb(14, 72, 93)
            },
            Child = _boardGrid
        };
        Grid.SetRow(boardFrame, 1);
        root.Children.Add(boardFrame);

        var footer = new Grid { Margin = new Thickness(26, 0, 26, 20) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(39, 91, 102)),
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(_statusText);
        _restartButton = CreateActionButton("重新开局", Color.FromRgb(255, 145, 67));
        _restartButton.Click += (_, _) => StartNewRound();
        Grid.SetColumn(_restartButton, 1);
        footer.Children.Add(_restartButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        _rewardOverlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(184, 10, 51, 65)),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRowSpan(_rewardOverlay, 3);
        _bonusCard = BuildBonusCard();
        _celebrationImage = new Image
        {
            Width = 285, Height = 260, Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true, Margin = new Thickness(0, 5, 0, 7)
        };
        RenderOptions.SetBitmapScalingMode(_celebrationImage, BitmapScalingMode.HighQuality);
        _animationSourceText = new TextBlock
        {
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(118, 113, 119)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10)
        };
        _celebrationPanel = BuildCelebrationPanel();
        _celebrationPanel.Visibility = Visibility.Collapsed;
        _rewardOverlay.Children.Add(_bonusCard);
        _rewardOverlay.Children.Add(_celebrationPanel);
        root.Children.Add(_rewardOverlay);

        _effectsCanvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetRowSpan(_effectsCanvas, 3);
        Panel.SetZIndex(_effectsCanvas, 10);
        root.Children.Add(_effectsCanvas);

        _celebrationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _celebrationTimer.Tick += (_, _) => UpdateCelebrationFrame();
        Closed += (_, _) => _celebrationTimer.Stop();
        StartNewRound();
    }

    private Border BuildBonusCard()
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new TextBlock
        {
            Text = "●", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 105,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 183, 72)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -18, 0, -21)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Bonus Time", FontFamily = new FontFamily("Segoe UI"), FontSize = 36,
            FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(148, 70, 17), BlurRadius = 4, ShadowDepth = 2, Opacity = 0.75
            }
        });
        stack.Children.Add(new TextBlock
        {
            Text = "已完成 2 次消除！", FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 65, 28)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0)
        });
        return new Border
        {
            Width = 365, Height = 275, CornerRadius = new CornerRadius(34),
            BorderThickness = new Thickness(4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 244, 185)),
            Background = new LinearGradientBrush(
                Color.FromRgb(255, 239, 140), Color.FromRgb(255, 147, 68), 90),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(20, 48, 57), BlurRadius = 26, ShadowDepth = 8, Opacity = 0.44
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.65, 0.65), Child = stack
        };
    }

    private Border BuildCelebrationPanel()
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new TextBlock
        {
            Text = "消除奖励！", FontSize = 32, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 135, 62)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = "噜噜来庆祝一下～", FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(76, 81, 94)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(_celebrationImage);
        stack.Children.Add(_animationSourceText);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var continueButton = CreateActionButton("继续消除", Color.FromRgb(64, 174, 171));
        continueButton.Margin = new Thickness(0, 0, 10, 0);
        continueButton.Click += (_, _) => ContinueAfterReward();
        var restartButton = CreateActionButton("重新开局", Color.FromRgb(255, 145, 67));
        restartButton.Click += (_, _) => StartNewRound();
        buttons.Children.Add(continueButton);
        buttons.Children.Add(restartButton);
        stack.Children.Add(buttons);
        return new Border
        {
            Width = 405, Padding = new Thickness(20, 18, 20, 22),
            CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 220, 132)),
            Background = new SolidColorBrush(Color.FromRgb(255, 252, 240)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 28, ShadowDepth = 8, Opacity = 0.34
            },
            Child = stack
        };
    }

    private static Button CreateActionButton(string label, Color color) => new()
    {
        Content = label, Padding = new Thickness(17, 8, 17, 8), FontSize = 13,
        FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
        Background = new SolidColorBrush(color), BorderThickness = new Thickness(0), Cursor = Cursors.Hand
    };

    private void StartNewRound()
    {
        _roundVersion++;
        _busy = false;
        _restartButton.IsEnabled = true;
        _moves = 0;
        _clearWaveCount = 0;
        _selectedCell = null;
        _rewardOverlay.Visibility = Visibility.Collapsed;
        _bonusCard.Visibility = Visibility.Visible;
        _celebrationPanel.Visibility = Visibility.Collapsed;
        _effectsCanvas.Children.Clear();
        StopCelebrationPlayback();
        GeneratePlayableBoard();
        RebuildBoard();
        UpdateCounters();
        SetStatus("拖动方块，或依次点击两个相邻方块进行交换");
    }

    private void GeneratePlayableBoard()
    {
        do
        {
            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var candidates = Enumerable.Range(0, TileKinds.Length).ToList();
                    if (column >= 2 && _cells[row, column - 1] == _cells[row, column - 2])
                        candidates.Remove(_cells[row, column - 1]);
                    if (row >= 2 && _cells[row - 1, column] == _cells[row - 2, column])
                        candidates.Remove(_cells[row - 1, column]);
                    _cells[row, column] = candidates[_random.Next(candidates.Count)];
                }
            }
        }
        while (!HasPossibleMove());
    }

    private void RebuildBoard(IReadOnlyDictionary<(int Row, int Column), int>? fallRows = null)
    {
        _boardGrid.Children.Clear();
        Array.Clear(_tileButtons);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var cellRow = row;
                var cellColumn = column;
                if (_cells[row, column] < 0)
                {
                    var empty = new Border
                    {
                        Margin = new Thickness(3), CornerRadius = new CornerRadius(10),
                        Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255))
                    };
                    Grid.SetRow(empty, row);
                    Grid.SetColumn(empty, column);
                    _boardGrid.Children.Add(empty);
                    continue;
                }

                var button = CreateTileButton(row, column, _cells[row, column]);
                _tileButtons[row, column] = button;
                Grid.SetRow(button, row);
                Grid.SetColumn(button, column);
                _boardGrid.Children.Add(button);
                button.Click += async (_, _) =>
                {
                    if (_suppressClick)
                    {
                        _suppressClick = false;
                        return;
                    }
                    await HandleCellClickAsync(cellRow, cellColumn);
                };
                button.PreviewMouseLeftButtonDown += (_, args) =>
                {
                    _pointerCell = (cellRow, cellColumn);
                    _pointerStart = args.GetPosition(_boardGrid);
                    _dragPerformed = false;
                    _suppressClick = false;
                };
                button.PreviewMouseMove += async (_, args) =>
                {
                    if (_busy || _dragPerformed || _pointerCell != (cellRow, cellColumn)
                        || args.LeftButton != MouseButtonState.Pressed)
                        return;
                    var delta = args.GetPosition(_boardGrid) - _pointerStart;
                    if (Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)) < 20) return;
                    var targetRow = cellRow;
                    var targetColumn = cellColumn;
                    if (Math.Abs(delta.X) >= Math.Abs(delta.Y)) targetColumn += Math.Sign(delta.X);
                    else targetRow += Math.Sign(delta.Y);
                    _dragPerformed = true;
                    _suppressClick = true;
                    if (IsInside(targetRow, targetColumn))
                    {
                        _selectedCell = null;
                        await TrySwapAsync((cellRow, cellColumn), (targetRow, targetColumn));
                    }
                };

                if (fallRows is not null && fallRows.TryGetValue((row, column), out var distance))
                {
                    var translate = GetTranslate(button);
                    translate.Y = -Math.Max(1, distance) * 61;
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(translate.Y, 0, TimeSpan.FromMilliseconds(270))
                        {
                            EasingFunction = new BounceEase
                            {
                                Bounces = 1, Bounciness = 2.2, EasingMode = EasingMode.EaseOut
                            }
                        });
                }
            }
        }
    }

    private Button CreateTileButton(int row, int column, int kindIndex)
    {
        var kind = TileKinds[kindIndex];
        var face = new Grid();
        face.Children.Add(new Border
        {
            Margin = new Thickness(2), CornerRadius = new CornerRadius(11),
            BorderThickness = _selectedCell == (row, column) ? new Thickness(4) : new Thickness(2),
            BorderBrush = _selectedCell == (row, column)
                ? Brushes.White : new SolidColorBrush(Color.FromArgb(195, 255, 255, 255)),
            Background = new LinearGradientBrush(kind.LightColor, kind.DarkColor, 90),
            Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 2, Opacity = 0.24 }
        });
        face.Children.Add(new TextBlock
        {
            Text = kind.Glyph, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 29,
            FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.30 }
        });
        return new Button
        {
            Content = face, Margin = new Thickness(0), Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, ToolTip = $"{kind.Name}（拖动交换）",
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1, 1), new TranslateTransform() }
            }
        };
    }

    private async Task HandleCellClickAsync(int row, int column)
    {
        if (_busy) return;
        if (_selectedCell is null)
        {
            _selectedCell = (row, column);
            RebuildBoard();
            SetStatus("再选择一个相邻方块完成交换");
            return;
        }
        var first = _selectedCell.Value;
        if (first == (row, column))
        {
            _selectedCell = null;
            RebuildBoard();
            return;
        }
        if (!AreAdjacent(first, (row, column)))
        {
            _selectedCell = (row, column);
            RebuildBoard();
            SetStatus("只能交换上下左右相邻的方块");
            return;
        }
        _selectedCell = null;
        await TrySwapAsync(first, (row, column));
    }

    private async Task TrySwapAsync((int Row, int Column) first, (int Row, int Column) second)
    {
        if (_busy || !AreAdjacent(first, second)) return;
        _busy = true;
        _restartButton.IsEnabled = false;
        _moves++;
        SwapCells(first, second);
        var createsMatch = FindMatches().Count > 0;
        SwapCells(first, second);
        if (!createsMatch)
        {
            await AnimateSwapAsync(first, second, returnToStart: true);
            SetStatus("这一步没有形成三连，方块已回到原位", warning: true);
            UpdateCounters();
            _busy = false;
            _restartButton.IsEnabled = true;
            return;
        }
        await AnimateSwapAsync(first, second, returnToStart: false);
        SwapCells(first, second);
        RebuildBoard();
        await ResolveMatchesAsync();
    }

    private async Task AnimateSwapAsync(
        (int Row, int Column) first,
        (int Row, int Column) second,
        bool returnToStart)
    {
        var firstButton = _tileButtons[first.Row, first.Column];
        var secondButton = _tileButtons[second.Row, second.Column];
        if (firstButton is null || secondButton is null) return;
        var width = Math.Max(1, firstButton.ActualWidth);
        var height = Math.Max(1, firstButton.ActualHeight);
        var firstTranslate = GetTranslate(firstButton);
        var secondTranslate = GetTranslate(secondButton);
        var duration = TimeSpan.FromMilliseconds(150);
        var firstX = (second.Column - first.Column) * width;
        var firstY = (second.Row - first.Row) * height;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        firstTranslate.BeginAnimation(TranslateTransform.XProperty,
            CreateSwapAnimation(firstX, duration, returnToStart, ease));
        firstTranslate.BeginAnimation(TranslateTransform.YProperty,
            CreateSwapAnimation(firstY, duration, returnToStart, ease));
        secondTranslate.BeginAnimation(TranslateTransform.XProperty,
            CreateSwapAnimation(-firstX, duration, returnToStart, ease));
        secondTranslate.BeginAnimation(TranslateTransform.YProperty,
            CreateSwapAnimation(-firstY, duration, returnToStart, ease));
        await Task.Delay(returnToStart ? 310 : 155);
    }

    private static DoubleAnimation CreateSwapAnimation(
        double target, TimeSpan duration, bool autoReverse, IEasingFunction ease) => new(0, target, duration)
    {
        AutoReverse = autoReverse,
        EasingFunction = ease,
        FillBehavior = autoReverse ? FillBehavior.Stop : FillBehavior.HoldEnd
    };

    private async Task ResolveMatchesAsync()
    {
        while (true)
        {
            var matches = FindMatches();
            if (matches.Count == 0) break;
            _clearWaveCount++;
            UpdateCounters();
            SetStatus(matches.Count >= 4 ? $"漂亮！一次消除了 {matches.Count} 个方块" : "三连消除！");
            await AnimateMatchesAsync(matches);
            foreach (var cell in matches) _cells[cell.Row, cell.Column] = -1;
            RebuildBoard();
            SpawnMatchSparks(matches.Count);
            await Task.Delay(90);
            var fallRows = CollapseAndRefill();
            RebuildBoard(fallRows);
            await Task.Delay(290);
        }
        UpdateCounters();
        if (!HasPossibleMove())
        {
            GeneratePlayableBoard();
            RebuildBoard();
            SetStatus("没有可交换组合，棋盘已自动重新排列");
        }
        if (_clearWaveCount >= RewardClearWaves)
        {
            await RunRewardSequenceAsync();
            return;
        }
        _busy = false;
        _restartButton.IsEnabled = true;
    }

    private async Task AnimateMatchesAsync(IReadOnlyCollection<(int Row, int Column)> matches)
    {
        foreach (var cell in matches)
        {
            var button = _tileButtons[cell.Row, cell.Column];
            if (button is null) continue;
            var scale = GetScale(button);
            var duration = TimeSpan.FromMilliseconds(235);
            var shrink = new DoubleAnimation(1, 0.15, duration)
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseIn }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            button.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
        }
        await Task.Delay(245);
    }

    private Dictionary<(int Row, int Column), int> CollapseAndRefill()
    {
        var falls = new Dictionary<(int Row, int Column), int>();
        for (var column = 0; column < Columns; column++)
        {
            var existing = new List<(int Value, int OriginalRow)>();
            for (var row = Rows - 1; row >= 0; row--)
            {
                if (_cells[row, column] >= 0) existing.Add((_cells[row, column], row));
                _cells[row, column] = -1;
            }
            var writeRow = Rows - 1;
            foreach (var item in existing)
            {
                _cells[writeRow, column] = item.Value;
                if (writeRow != item.OriginalRow) falls[(writeRow, column)] = writeRow - item.OriginalRow;
                writeRow--;
            }
            while (writeRow >= 0)
            {
                _cells[writeRow, column] = _random.Next(TileKinds.Length);
                falls[(writeRow, column)] = writeRow + 2;
                writeRow--;
            }
        }
        return falls;
    }

    private HashSet<(int Row, int Column)> FindMatches()
    {
        var result = new HashSet<(int Row, int Column)>();
        for (var row = 0; row < Rows; row++)
        {
            var runStart = 0;
            for (var column = 1; column <= Columns; column++)
            {
                if (column < Columns && _cells[row, column] >= 0
                    && _cells[row, column] == _cells[row, runStart]) continue;
                if (_cells[row, runStart] >= 0 && column - runStart >= 3)
                    for (var matchColumn = runStart; matchColumn < column; matchColumn++)
                        result.Add((row, matchColumn));
                runStart = column;
            }
        }
        for (var column = 0; column < Columns; column++)
        {
            var runStart = 0;
            for (var row = 1; row <= Rows; row++)
            {
                if (row < Rows && _cells[row, column] >= 0
                    && _cells[row, column] == _cells[runStart, column]) continue;
                if (_cells[runStart, column] >= 0 && row - runStart >= 3)
                    for (var matchRow = runStart; matchRow < row; matchRow++)
                        result.Add((matchRow, column));
                runStart = row;
            }
        }
        return result;
    }

    private bool HasPossibleMove()
    {
        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
        {
            if (column + 1 < Columns && SwapWouldMatch((row, column), (row, column + 1))) return true;
            if (row + 1 < Rows && SwapWouldMatch((row, column), (row + 1, column))) return true;
        }
        return false;
    }

    private bool SwapWouldMatch((int Row, int Column) first, (int Row, int Column) second)
    {
        SwapCells(first, second);
        var hasMatch = FindMatches().Count > 0;
        SwapCells(first, second);
        return hasMatch;
    }

    private void SwapCells((int Row, int Column) first, (int Row, int Column) second)
    {
        (_cells[first.Row, first.Column], _cells[second.Row, second.Column]) =
            (_cells[second.Row, second.Column], _cells[first.Row, first.Column]);
    }

    private async Task RunRewardSequenceAsync()
    {
        _busy = true;
        var version = _roundVersion;
        _rewardOverlay.Visibility = Visibility.Visible;
        _bonusCard.Visibility = Visibility.Visible;
        _celebrationPanel.Visibility = Visibility.Collapsed;
        var bonusScale = (ScaleTransform)_bonusCard.RenderTransform;
        var pop = new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(480))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
        };
        bonusScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        bonusScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        SpawnConfetti(45, 1750);
        await Task.Delay(1450);
        if (version != _roundVersion || !IsVisible) return;
        _bonusCard.Visibility = Visibility.Collapsed;
        _celebrationPanel.Visibility = Visibility.Visible;
        _celebrationPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        StartCelebrationPlayback();
        SpawnConfetti(75, 3400);
    }

    private void ContinueAfterReward()
    {
        StopCelebrationPlayback();
        _rewardOverlay.Visibility = Visibility.Collapsed;
        _effectsCanvas.Children.Clear();
        _clearWaveCount = 0;
        _busy = false;
        _restartButton.IsEnabled = true;
        UpdateCounters();
        SetStatus("奖励已领取，再完成 2 次消除可再次播放");
    }

    private void StartCelebrationPlayback()
    {
        try
        {
            if (_celebrationAnimation is null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().Contains(
                    PreferredCelebrationGif, StringComparer.Ordinal)
                    ? PreferredCelebrationGif : FallbackCelebrationGif;
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException("未找到消消乐庆祝 GIF。");
                _celebrationAnimation = GifAnimation.Load(stream);
                _animationSourceText.Text = resourceName == PreferredCelebrationGif
                    ? "正在播放：match-game-celebration.gif"
                    : "验证素材：flycapylulu.gif（可替换为 match-game-celebration.gif）";
            }
            _celebrationFrameIndex = -1;
            _celebrationClock.Restart();
            UpdateCelebrationFrame();
            _celebrationTimer.Start();
        }
        catch (Exception exception)
        {
            _celebrationImage.Source = null;
            _animationSourceText.Text = $"GIF 尚未提供：{exception.Message}";
        }
    }

    private void UpdateCelebrationFrame()
    {
        if (_celebrationAnimation is null || _celebrationAnimation.DurationSeconds <= 0) return;
        var loopSeconds = _celebrationClock.Elapsed.TotalSeconds % _celebrationAnimation.DurationSeconds;
        var frameIndex = _celebrationAnimation.GetFrameIndex(loopSeconds);
        if (frameIndex != _celebrationFrameIndex)
        {
            _celebrationFrameIndex = frameIndex;
            _celebrationImage.Source = _celebrationAnimation.Frames[frameIndex];
        }
    }

    private void StopCelebrationPlayback()
    {
        _celebrationTimer.Stop();
        _celebrationClock.Reset();
        _celebrationFrameIndex = -1;
        _celebrationImage.Source = null;
    }

    private void SpawnMatchSparks(int matchedCount)
    {
        for (var index = 0; index < Math.Min(18, matchedCount * 3); index++)
        {
            var spark = new TextBlock
            {
                Text = index % 2 == 0 ? "✦" : "•", FontSize = _random.Next(14, 24),
                Foreground = new SolidColorBrush(ConfettiColors[_random.Next(ConfettiColors.Length)]),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(spark, (Width / 2) + _random.Next(-150, 151));
            Canvas.SetTop(spark, 450 + _random.Next(-40, 41));
            _effectsCanvas.Children.Add(spark);
            var duration = TimeSpan.FromMilliseconds(_random.Next(420, 720));
            var fade = new DoubleAnimation(1, 0, duration);
            fade.Completed += (_, _) => _effectsCanvas.Children.Remove(spark);
            spark.BeginAnimation(Canvas.TopProperty,
                new DoubleAnimation(Canvas.GetTop(spark), Canvas.GetTop(spark) - _random.Next(30, 90), duration));
            spark.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void SpawnConfetti(int count, int durationMilliseconds)
    {
        for (var index = 0; index < count; index++)
        {
            var confetti = new Rectangle
            {
                Width = _random.Next(5, 11), Height = _random.Next(10, 21), RadiusX = 2, RadiusY = 2,
                Fill = new SolidColorBrush(ConfettiColors[_random.Next(ConfettiColors.Length)]),
                IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(_random.Next(0, 180))
            };
            Canvas.SetLeft(confetti, _random.NextDouble() * Math.Max(100, ActualWidth - 20));
            Canvas.SetTop(confetti, -30 - _random.Next(0, 160));
            _effectsCanvas.Children.Add(confetti);
            var duration = TimeSpan.FromMilliseconds(durationMilliseconds + _random.Next(-350, 650));
            var fall = new DoubleAnimation(Canvas.GetTop(confetti), ActualHeight + 45, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fall.Completed += (_, _) => _effectsCanvas.Children.Remove(confetti);
            confetti.BeginAnimation(Canvas.TopProperty, fall);
            confetti.BeginAnimation(Canvas.LeftProperty,
                new DoubleAnimation(Canvas.GetLeft(confetti), Canvas.GetLeft(confetti) + _random.Next(-80, 81), duration));
            ((RotateTransform)confetti.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(((RotateTransform)confetti.RenderTransform).Angle, _random.Next(300, 900), duration));
        }
    }

    private void UpdateCounters()
    {
        _rewardProgressText.Text = $"奖励进度 {Math.Min(_clearWaveCount, RewardClearWaves)}/{RewardClearWaves}";
        _movesText.Text = $"交换步数 {_moves}";
    }

    private void SetStatus(string message, bool warning = false)
    {
        _statusText.Text = message;
        _statusText.Foreground = new SolidColorBrush(warning
            ? Color.FromRgb(186, 62, 66) : Color.FromRgb(39, 91, 102));
    }

    private static bool AreAdjacent((int Row, int Column) first, (int Row, int Column) second) =>
        Math.Abs(first.Row - second.Row) + Math.Abs(first.Column - second.Column) == 1;

    private static bool IsInside(int row, int column) =>
        row >= 0 && row < Rows && column >= 0 && column < Columns;

    private static ScaleTransform GetScale(Button button) =>
        (ScaleTransform)((TransformGroup)button.RenderTransform).Children[0];

    private static TranslateTransform GetTranslate(Button button) =>
        (TranslateTransform)((TransformGroup)button.RenderTransform).Children[1];

    private sealed record TileKind(string Name, string Glyph, Color LightColor, Color DarkColor);
}
