using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static CapyLulu.MatchGameOptions;

namespace CapyLulu;

// 只负责把 MatchBoard 说的事情演出来：搬元素、播动画、锁输入。
// 一切规则都在 MatchBoard，方块外观在 MatchTileArt，界面外观在 Skin。
internal sealed class MatchGameWindow : Window
{
    // 棋盘底板的内边距。底板外框固定 476 DIP（Skin 的斜面占掉每边 4），
    // 所以这里是 14 而不是 18 —— 窗口里其余布局一格都没动。
    private const double BoardPadding = 14;

    private readonly MatchBoard _board = new(new Random());
    private readonly Border?[,] _tiles = new Border?[Rows, Columns];
    private readonly Canvas _boardCanvas;
    private readonly Canvas _confettiCanvas;
    private readonly Image[] _progressStars;
    private readonly TextBlock _hintText;
    private readonly Grid _showLayer;
    private readonly Border _bonusCard;
    private readonly Image _celebrationImage;
    private readonly TextBlock _celebrationText;
    private readonly Button _continueButton;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _celebrationTimer;
    private readonly Stopwatch _celebrationClock = new();

    // 同时只允许一个棋盘。守在窗口自己身上，任何调用方都绕不过去，
    // 也省得每个调用方各自记一个字段。
    private static MatchGameWindow? _open;

    private GifAnimation? _celebration;
    private TaskCompletionSource? _continueSignal;
    private Cell _dragCell;
    private Point _dragOrigin;
    private bool _isDragging;
    private bool _isBusy;

    public static void ShowSingle()
    {
        if (_open is not null)
        {
            if (_open.WindowState == WindowState.Minimized)
            {
                _open.WindowState = WindowState.Normal;
            }

            _open.Activate();
            return;
        }

        _open = new MatchGameWindow();
        _open.Show();
        _open.Activate();
    }

    private MatchGameWindow()
    {
        Title = "CapyLulu 消消乐";
        Width = 560;
        Height = 726;
        // 棋盘尺寸在动画期间不得变化，所以不让改大小；但 NoResize 会连
        // WS_MINIMIZEBOX 一起去掉，最小化按钮就点不动了，得用 CanMinimize。
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        Skin.ApplyChrome(this);

        var root = new Grid { ClipToBounds = true };
        Content = Skin.Shell(root);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(content);

        AddRow(content, 0, BuildTitleBar());
        AddRow(content, 1, BuildHeader(out _progressStars));
        AddRow(content, 2, BuildBoardArea(out _boardCanvas, out var boardLayers));
        AddRow(content, 3, BuildFooter(out _hintText));

        _showLayer = BuildShowLayer(
            out _confettiCanvas,
            out _bonusCard,
            out _celebrationImage,
            out _celebrationText,
            out _continueButton);
        boardLayers.Children.Add(_showLayer);

        _celebrationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _celebrationTimer.Tick += OnCelebrationTick;

        BuildTiles();
        UpdateProgress();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close();
            e.Handled = true;
        };
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _open = null;
        _celebrationTimer.Stop();
        // 取消会让所有等在 Task.Delay 上的动画流程立刻收场，不留悬挂的续体。
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    // ---------- 布局 ----------

    private static void AddRow(Grid grid, int row, UIElement child)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
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
        bar.MouseLeftButtonDown += (_, _) => DragMove();

        // 徽标换成麦穗点阵：原来那个纯色小方块是整条标题栏最像占位符的地方。
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
            Text = "CAPYLULU  ·  消消乐",
            Foreground = Skin.Ink,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        // 标题钉在一块木牌上，而不是浮在羊皮纸上——空着的那片奶油底是上一版
        // 最像"没做完"的地方。
        var plaque = Skin.Raised(brand, Skin.U * 1.5);
        plaque.HorizontalAlignment = HorizontalAlignment.Left;
        plaque.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(plaque);

        // 两个 34x34 紧挨着，所以最小化按钮的中心还在原来那个位置。
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var minimize = Skin.CreateButton(
            Skin.Icon(Skin.Art.Minimize, 2, Skin.Ink),
            34,
            34,
            () => WindowState = WindowState.Minimized);
        minimize.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(minimize);
        buttons.Children.Add(Skin.CreateButton(Skin.Icon(Skin.Art.Close, 2, Skin.Ink), 34, 34, Close));
        Grid.SetColumn(buttons, 1);
        bar.Children.Add(buttons);
        return bar;
    }

    // 进度做成内凹的读数槽，和棋盘底板一套语言。槽里是一排星星，
    // 每消一轮点亮一颗——比一行"3/10"直观，也是星露谷到处在用的读数方式。
    private static Border BuildHeader(out Image[] progressStars)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(14, 3, 14, 4)
        };
        row.Children.Add(new TextBlock
        {
            Text = "奖励进度",
            Foreground = Skin.Muted,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        progressStars = new Image[RewardWaveTarget];
        for (var index = 0; index < progressStars.Length; index++)
        {
            var star = Skin.Icon(Skin.Art.Star, 3, Skin.WoodMid);
            star.Margin = new Thickness(2, 0, 2, 0);
            progressStars[index] = star;
            row.Children.Add(star);
        }

        var badge = Skin.Sunken(row);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Margin = new Thickness(0, 2, 0, 10);
        return badge;
    }

    private Border BuildBoardArea(out Canvas boardCanvas, out Grid boardLayers)
    {
        boardCanvas = new Canvas
        {
            Width = BoardWidth,
            Height = BoardHeight,
            // 新方块从棋盘上方滑入，靠裁剪把它们挡在盘外直到落进来。
            ClipToBounds = true,
            Background = Brushes.Transparent
        };
        boardCanvas.MouseLeftButtonDown += OnBoardMouseDown;
        boardCanvas.MouseMove += OnBoardMouseMove;
        boardCanvas.MouseLeftButtonUp += OnBoardMouseUp;
        boardCanvas.LostMouseCapture += OnBoardLostCapture;

        // 演出层和棋盘共用这一格、同一尺寸，所以卡片、彩纸和 GIF 天然落在 7x7 区域里。
        boardLayers = new Grid
        {
            Width = BoardWidth,
            Height = BoardHeight,
            ClipToBounds = true
        };
        boardLayers.Children.Add(boardCanvas);

        // 棋盘是内凹的一块田：方块躺在里面，清场后 GIF 就在这块地方演。
        var panel = Skin.Plot(boardLayers, BoardPadding, Skin.Field);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        return panel;
    }

    private Grid BuildFooter(out TextBlock hintText)
    {
        var footer = new Grid { Margin = new Thickness(6, 10, 6, 8) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        hintText = new TextBlock
        {
            Text = "拖动相邻的两个方块交换位置",
            Foreground = Skin.Muted,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(hintText);

        var restart = Skin.CreateButton("重新开始", 96, 34, Restart, 13);
        Grid.SetColumn(restart, 1);
        footer.Children.Add(restart);
        return footer;
    }

    // 演出全程只占棋盘那块方形区域，不加全窗遮罩：地方是方块自己淡出腾出来的。
    private Grid BuildShowLayer(
        out Canvas confettiCanvas,
        out Border bonusCard,
        out Image celebrationImage,
        out TextBlock celebrationText,
        out Button continueButton)
    {
        var layer = new Grid
        {
            // 透明但吃点击，演出期间棋盘天然点不到。
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed
        };

        // 拉伸对齐 + Uniform：铺满腾空的 7x7 区域，同时保住 GIF 自己的画面比例。
        celebrationImage = new Image
        {
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed
        };
        layer.Children.Add(celebrationImage);

        celebrationText = new TextBlock
        {
            Text = "庆祝素材尚未提供",
            Foreground = Skin.Parchment,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        layer.Children.Add(celebrationText);

        // 卡片这一段方块还在，底下是花的，所以用金色底 + 硬描边把字托出来。
        bonusCard = Skin.Raised(
            new TextBlock
            {
                Text = "Bonus Time!",
                Foreground = Skin.Outline,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(28, 10, 28, 12)
            },
            body: Skin.Gold);
        bonusCard.HorizontalAlignment = HorizontalAlignment.Center;
        bonusCard.VerticalAlignment = VerticalAlignment.Center;
        bonusCard.Visibility = Visibility.Collapsed;
        layer.Children.Add(bonusCard);

        continueButton = Skin.CreateButton(
            "继续",
            132,
            40,
            () => _continueSignal?.TrySetResult(),
            15,
            Skin.Accent,
            Skin.Parchment);
        continueButton.HorizontalAlignment = HorizontalAlignment.Center;
        continueButton.VerticalAlignment = VerticalAlignment.Bottom;
        continueButton.Margin = new Thickness(0, 0, 0, 18);
        continueButton.Visibility = Visibility.Collapsed;
        layer.Children.Add(continueButton);

        confettiCanvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        layer.Children.Add(confettiCanvas);
        return layer;
    }

    // ---------- 棋盘视觉 ----------

    private static double CoordinateOf(int index) => index * TilePitch;

    private void BuildTiles()
    {
        _boardCanvas.Children.Clear();
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var tile = MatchTileArt.Create(_board.GetKind(row, column));
                PlaceTile(tile, row, column);
                _tiles[row, column] = tile;
                _boardCanvas.Children.Add(tile);
            }
        }
    }

    private static void PlaceTile(Border tile, int row, int column)
    {
        Canvas.SetLeft(tile, CoordinateOf(column));
        Canvas.SetTop(tile, CoordinateOf(row));
    }

    private void UpdateProgress()
    {
        for (var index = 0; index < _progressStars.Length; index++)
        {
            _progressStars[index].Source = Skin.IconSource(
                Skin.Art.Star, index < _board.ClearedWaveCount ? Skin.Gold : Skin.WoodMid);
        }
    }

    private void Restart()
    {
        if (_isBusy)
        {
            return;
        }

        _board.Reset();
        _board.ResetWaveCount();
        BuildTiles();
        UpdateProgress();
        _hintText.Text = "拖动相邻的两个方块交换位置";
    }

    // ---------- 手势 ----------

    private void OnBoardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var origin = e.GetPosition(_boardCanvas);
        if (CellAt(origin) is not { } cell)
        {
            return;
        }

        _dragCell = cell;
        _dragOrigin = origin;
        _isDragging = true;
        _boardCanvas.CaptureMouse();
    }

    private void OnBoardMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _isBusy)
        {
            return;
        }

        var delta = e.GetPosition(_boardCanvas) - _dragOrigin;
        var (rowStep, columnStep) = MatchBoard.ResolveDragStep(delta.X, delta.Y);
        if (rowStep == 0 && columnStep == 0)
        {
            FollowPointer(delta);
            return;
        }

        // 一次手势只换一次：先收手，再决定这一步算不算数。
        var source = _dragCell;
        var target = new Cell(source.Row + rowStep, source.Column + columnStep);
        EndDrag();
        if (_tiles[source.Row, source.Column] is { } tile)
        {
            Settle(tile, source);
        }

        if (!_board.IsInside(target))
        {
            return;
        }

        RunGuarded(() => SwapAsync(source, target));
    }

    private void OnBoardMouseUp(object sender, MouseButtonEventArgs e) => ReleaseDrag();

    private void OnBoardLostCapture(object sender, MouseEventArgs e) => ReleaseDrag();

    // 松手或丢失捕获都走这里：把跟手的方块滑回原位，不产生交换。
    private void ReleaseDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        var cell = _dragCell;
        EndDrag();
        if (_tiles[cell.Row, cell.Column] is { } tile)
        {
            GlideTo(tile, 0, 0, FollowSnapBackMs);
        }
    }

    private void EndDrag()
    {
        // 先清标记再释放：ReleaseMouseCapture 会同步回调 LostMouseCapture。
        _isDragging = false;
        _boardCanvas.ReleaseMouseCapture();
        if (_tiles[_dragCell.Row, _dragCell.Column] is { } tile)
        {
            Panel.SetZIndex(tile, 0);
        }
    }

    private void FollowPointer(Vector delta)
    {
        if (_tiles[_dragCell.Row, _dragCell.Column] is not { } tile)
        {
            return;
        }

        var offset = MatchTileArt.OffsetOf(tile);
        offset.X = Math.Clamp(delta.X, -FollowMaxOffset, FollowMaxOffset);
        offset.Y = Math.Clamp(delta.Y, -FollowMaxOffset, FollowMaxOffset);
        Panel.SetZIndex(tile, 1);
    }

    private Cell? CellAt(Point point)
    {
        var column = (int)Math.Floor(point.X / TilePitch);
        var row = (int)Math.Floor(point.Y / TilePitch);
        return _board.IsInside(row, column) ? new Cell(row, column) : null;
    }

    // ---------- 流程 ----------

    private async void RunGuarded(Func<Task> work)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // 窗口在动画中途关掉了，剩下的步骤没有意义。
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task SwapAsync(Cell from, Cell to)
    {
        var valid = _board.WouldMatch(from, to);
        await GlideSwapAsync(from, to, 1, SwapMs);

        if (!valid)
        {
            // 退回去：棋盘数据和奖励进度自始至终没被碰过。
            await GlideSwapAsync(from, to, 0, RollbackMs);
            return;
        }

        _board.Swap(from, to);
        ExchangeTiles(from, to);
        await ResolveAsync();
    }

    private async Task ResolveAsync()
    {
        while (_board.FindMatches() is { Count: > 0 } matches)
        {
            await PopAsync(matches);
            _board.Clear(matches);
            UpdateProgress();
            await DropAsync(_board.Collapse());
        }

        if (!_board.HasLegalSwap())
        {
            _hintText.Text = "没有可消的了，帮你重新洗牌～";
            await ShuffleAsync();
            _hintText.Text = "拖动相邻的两个方块交换位置";
        }

        // 第二轮必须先把自己的连锁走完，才轮到奖励展示。
        if (_board.ClearedWaveCount >= RewardWaveTarget)
        {
            await CelebrateAsync();
        }
    }

    private async Task PopAsync(IReadOnlyList<Cell> cells)
    {
        var duration = TimeSpan.FromMilliseconds(ClearMs);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        foreach (var cell in cells)
        {
            if (_tiles[cell.Row, cell.Column] is not { } tile)
            {
                continue;
            }

            var scale = MatchTileArt.ScaleOf(tile);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 0.15, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, 0.15, duration) { EasingFunction = ease });
            tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
        }

        await Delay(ClearMs);

        // 动画播完才把元素摘掉，模型也在这之后才清空。
        foreach (var cell in cells)
        {
            if (_tiles[cell.Row, cell.Column] is not { } tile)
            {
                continue;
            }

            _boardCanvas.Children.Remove(tile);
            _tiles[cell.Row, cell.Column] = null;
        }
    }

    private async Task DropAsync(FallResult fall)
    {
        var longest = 0;

        // 同列的目标格就在源格下方，所以先整批摘引用再逐个落位，避免写到还没读的格子。
        var moving = new List<(Border Tile, TileMove Move)>();
        foreach (var move in fall.Moves)
        {
            if (_tiles[move.From.Row, move.From.Column] is not { } tile)
            {
                continue;
            }

            moving.Add((tile, move));
            _tiles[move.From.Row, move.From.Column] = null;
        }

        foreach (var (tile, move) in moving)
        {
            _tiles[move.To.Row, move.To.Column] = tile;
            longest = Math.Max(longest, SlideIn(tile, move.From, move.To));
        }

        foreach (var spawn in fall.Spawns)
        {
            var tile = MatchTileArt.Create(spawn.Kind);
            _tiles[spawn.To.Row, spawn.To.Column] = tile;
            _boardCanvas.Children.Add(tile);
            longest = Math.Max(longest, SlideIn(tile, new Cell(spawn.EntryRow, spawn.To.Column), spawn.To));
        }

        if (longest > 0)
        {
            await Delay(longest);
        }
    }

    // 元素直接落到目标格，再用一段反向位移把它拉回起点滑下来——期间棋盘不重建。
    private int SlideIn(Border tile, Cell from, Cell to)
    {
        PlaceTile(tile, to.Row, to.Column);
        var rows = to.Row - from.Row;
        var milliseconds = Math.Min(FallMaxMs, FallBaseMs + (rows * FallPerRowMs));
        Glide(tile, 0, -rows * TilePitch, 0, 0, milliseconds);
        return milliseconds;
    }

    private async Task ShuffleAsync()
    {
        var result = _board.Shuffle();
        if (result.Regenerated)
        {
            // 种类整盘换过了，复用元素只会画错，直接重建。
            BuildTiles();
            await Delay(ShuffleMs);
            return;
        }

        // Moves 是一次纯置换，每个目标格恰好被写一次，先读齐再写就不会互相覆盖。
        var moving = new List<(Border Tile, TileMove Move)>();
        foreach (var move in result.Moves)
        {
            if (_tiles[move.From.Row, move.From.Column] is { } tile)
            {
                moving.Add((tile, move));
            }
        }

        foreach (var (tile, move) in moving)
        {
            _tiles[move.To.Row, move.To.Column] = tile;
        }

        foreach (var (tile, move) in moving)
        {
            PlaceTile(tile, move.To.Row, move.To.Column);
            Glide(
                tile,
                (move.From.Column - move.To.Column) * TilePitch,
                (move.From.Row - move.To.Row) * TilePitch,
                0,
                0,
                ShuffleMs);
        }

        await Delay(ShuffleMs);
    }

    // ---------- 奖励展示 ----------

    private async Task CelebrateAsync()
    {
        _celebrationImage.Visibility = Visibility.Collapsed;
        _celebrationText.Visibility = Visibility.Collapsed;
        _continueButton.Visibility = Visibility.Collapsed;
        _showLayer.Visibility = Visibility.Visible;

        // 一、Bonus Time 卡片压在棋盘正中，彩纸同时开始落
        _bonusCard.BeginAnimation(OpacityProperty, null);
        _bonusCard.Opacity = 1;
        _bonusCard.Visibility = Visibility.Visible;
        SpawnConfetti();
        await Delay(BonusHoldMs);

        // 二、卡片退场
        _bonusCard.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(CardExitMs)));
        await Delay(CardExitMs);
        _bonusCard.Visibility = Visibility.Collapsed;

        // 三、方块沿反对角线逐格淡出，把 7x7 这块地方腾出来；再补一批彩纸接到 GIF 阶段
        SpawnConfetti();
        await DissolveBoardAsync();

        // 四、GIF 铺满腾空的棋盘区域
        await ShowCelebrationAsync();

        _continueSignal = new TaskCompletionSource();
        _continueButton.Visibility = Visibility.Visible;
        await _continueSignal.Task.WaitAsync(_lifetime.Token);

        StopCelebration();
        _showLayer.Visibility = Visibility.Collapsed;
        _board.ResetWaveCount();
        UpdateProgress();
        await RestoreBoardAsync();
    }

    // 反对角线推进：row + column 相同的一批同时淡出，看着像从左上角扫到右下角。
    private async Task DissolveBoardAsync()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if (_tiles[row, column] is { } tile)
                {
                    FadeOut(tile, (row + column) * DissolveStepMs);
                }
            }
        }

        await Delay(DissolveTotalMs);
    }

    // 领完奖励接着玩同一局：方块一直在，只是被淡出了，
    // 所以统一清掉淡出动画再整盘淡回来，不重建元素也不换棋盘。
    private async Task RestoreBoardAsync()
    {
        foreach (var tile in _tiles)
        {
            if (tile is null)
            {
                continue;
            }

            tile.BeginAnimation(OpacityProperty, null);
            tile.Opacity = 1;
            var scale = MatchTileArt.ScaleOf(tile);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        _boardCanvas.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(BoardRestoreMs))
            {
                FillBehavior = FillBehavior.Stop
            });
        await Delay(BoardRestoreMs);
    }

    // BeginTime 之前属性保持原值，之后淡出并停在 0，所以一行就能排出推进顺序。
    private static void FadeOut(Border tile, int beginMs)
    {
        var begin = TimeSpan.FromMilliseconds(beginMs);
        var duration = TimeSpan.FromMilliseconds(DissolveFadeMs);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        tile.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, duration) { BeginTime = begin });

        var scale = MatchTileArt.ScaleOf(tile);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.55, duration) { BeginTime = begin, EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.55, duration) { BeginTime = begin, EasingFunction = ease });
    }

    private async Task ShowCelebrationAsync()
    {
        _celebration = LoadCelebration();
        if (_celebration is null)
        {
            // 素材没放或读坏了都走这条路：说清楚，不拿别的 GIF 顶替。
            _celebrationText.Visibility = Visibility.Visible;
            await Delay(MissingAssetHoldMs);
            return;
        }

        _celebrationImage.Visibility = Visibility.Visible;
        _celebrationClock.Restart();
        _celebrationTimer.Start();
        OnCelebrationTick(this, EventArgs.Empty);
        await Delay((int)(_celebration.DurationSeconds * 1000));
    }

    private void OnCelebrationTick(object? sender, EventArgs e)
    {
        if (_celebration is not { DurationSeconds: > 0 } animation)
        {
            return;
        }

        var loopSeconds = _celebrationClock.Elapsed.TotalSeconds % animation.DurationSeconds;
        _celebrationImage.Source = animation.Frames[animation.GetFrameIndex(loopSeconds)];
    }

    private void StopCelebration()
    {
        _celebrationTimer.Stop();
        _celebrationClock.Reset();
        _celebration = null;
        _celebrationImage.Source = null;
        _celebrationImage.Visibility = Visibility.Collapsed;
        _celebrationText.Visibility = Visibility.Collapsed;
        // 还没落完的彩纸不该留到下一次展示。
        _confettiCanvas.Children.Clear();
    }

    private static GifAnimation? LoadCelebration()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(CelebrationResourceName);
            return stream is null ? null : GifAnimation.Load(stream);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private void SpawnConfetti()
    {
        // 用常量而不是 ActualWidth：演出层刚从 Collapsed 变可见，这一刻还没排版，
        // 量出来会是 0。彩纸画布本来就铺满棋盘那块方形区域，尺寸编译期就定了。
        var random = new Random();
        for (var i = 0; i < 48; i++)
        {
            var piece = new Rectangle
            {
                Width = 7,
                Height = 13,
                RadiusX = 2,
                RadiusY = 2,
                Fill = Skin.Confetti[i % Skin.Confetti.Length],
                RenderTransform = new RotateTransform(random.Next(360)),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            Canvas.SetLeft(piece, random.NextDouble() * (BoardWidth - piece.Width));
            Canvas.SetTop(piece, -20);
            _confettiCanvas.Children.Add(piece);

            var duration = TimeSpan.FromMilliseconds(ConfettiMs + random.Next(600));
            var drop = new DoubleAnimation(-20, BoardHeight + 40, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            drop.Completed += (_, _) => _confettiCanvas.Children.Remove(piece);
            piece.BeginAnimation(Canvas.TopProperty, drop);
            piece.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
        }
    }

    // ---------- 动画基元 ----------

    private Task Delay(int milliseconds) => Task.Delay(milliseconds, _lifetime.Token);

    // 两个方块互相滑到对方位置；reach 为 0 就是原样退回。只播动画，不改数据。
    private async Task GlideSwapAsync(Cell a, Cell b, double reach, int milliseconds)
    {
        if (_tiles[a.Row, a.Column] is not { } first || _tiles[b.Row, b.Column] is not { } second)
        {
            return;
        }

        var dx = (b.Column - a.Column) * TilePitch * reach;
        var dy = (b.Row - a.Row) * TilePitch * reach;
        Panel.SetZIndex(first, 1);
        GlideTo(first, dx, dy, milliseconds);
        GlideTo(second, -dx, -dy, milliseconds);
        await Delay(milliseconds);
        Panel.SetZIndex(first, 0);
    }

    private void ExchangeTiles(Cell a, Cell b)
    {
        (_tiles[a.Row, a.Column], _tiles[b.Row, b.Column]) =
            (_tiles[b.Row, b.Column], _tiles[a.Row, a.Column]);
        if (_tiles[a.Row, a.Column] is { } first)
        {
            Settle(first, a);
        }

        if (_tiles[b.Row, b.Column] is { } second)
        {
            Settle(second, b);
        }
    }

    // 元素落到目标格的真实坐标，并把动画留下的位移清零；视觉上不会跳。
    private static void Settle(Border tile, Cell cell)
    {
        var offset = MatchTileArt.OffsetOf(tile);
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        offset.BeginAnimation(TranslateTransform.YProperty, null);
        offset.X = 0;
        offset.Y = 0;
        PlaceTile(tile, cell.Row, cell.Column);
    }

    private static void GlideTo(Border tile, double toX, double toY, int milliseconds)
    {
        var offset = MatchTileArt.OffsetOf(tile);
        Glide(tile, offset.X, offset.Y, toX, toY, milliseconds);
    }

    // 位移动画的唯一入口。基值先写成终点，动画用 FillBehavior.Stop 只负责过程，
    // 播完属性正好停在终点，不需要 Completed 回调收尾。
    private static void Glide(
        Border tile,
        double fromX,
        double fromY,
        double toX,
        double toY,
        int milliseconds)
    {
        var offset = MatchTileArt.OffsetOf(tile);
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        offset.BeginAnimation(TranslateTransform.YProperty, null);
        offset.X = toX;
        offset.Y = toY;

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        offset.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(fromX, toX, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        offset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromY, toY, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

}
