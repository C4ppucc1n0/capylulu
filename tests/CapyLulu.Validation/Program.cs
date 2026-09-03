using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CapyLulu;

var tests = new (string Name, Action Run)[]
{
    ("gaze direction wraps clockwise", TestGazeClockwiseWrap),
    ("gaze direction chooses shortest path", TestGazeShortestPath),
    ("circular angle distance wraps", TestCircularDistance),
    ("pointer tracker detects horizontal flick", TestHorizontalFlick),
    ("pointer tracker detects lift and drop", TestLiftDrop),
    ("active focus session does not restart", TestFocusSession),
    ("dialogue resource contains required groups", TestDialogueCatalog),
    ("character manifests expose stable unique ids", TestCharacterCatalog),
    ("all shipped sprite sheets match their manifests", TestSpriteSheets),
    ("match board deals a playable opening board", TestMatchBoardOpening),
    ("match board finds runs and dedupes overlaps", TestMatchBoardFindsRuns),
    ("match board validates swaps without mutating", TestMatchBoardValidatesSwaps),
    ("match board clear empties cells and counts a wave", TestMatchBoardClear),
    ("match board gravity keeps column order", TestMatchBoardCollapse),
    ("match board shuffle permutes the same tiles", TestMatchBoardShuffle),
    ("match board shuffle rescues an unwinnable deadlock", TestMatchBoardShuffleDeadlock),
    ("match board resolves the dominant drag axis", TestMatchBoardDragStep),
    ("match board progress counts only real clears", TestMatchBoardProgress),
    ("match game options stay inside the spec bands", TestMatchGameOptions),
    ("skin bevels mirror each other and press reversibly", TestSkin),
    ("match board opening keeps matches hard to spot", TestMatchBoardOpeningDifficulty),
    ("tile art gives every kind its own look", TestTileArt)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Validated {tests.Length} scenarios.");

static void TestGazeClockwiseWrap() => Equal(0, GazeDirectionMath.StepToward(15, 1));

static void TestGazeShortestPath() => Equal(15, GazeDirectionMath.StepToward(0, 14));

static void TestCircularDistance() => Near(2, GazeDirectionMath.CircularAngleDistance(359, 1));

static void TestHorizontalFlick()
{
    var tracker = new PointerMotionTracker();
    tracker.Reset(new Point(0, 0), 0);
    tracker.Add(new Point(45, 2), 0.04);
    tracker.Add(new Point(110, 3), 0.08);
    Equal(PetGesture.HorizontalFlick, tracker.DetectGesture());
}

static void TestLiftDrop()
{
    var tracker = new PointerMotionTracker();
    tracker.Reset(new Point(0, 120), 0);
    tracker.Add(new Point(2, 10), 0.25);
    tracker.Add(new Point(3, 105), 0.55);
    Equal(PetGesture.LiftDrop, tracker.DetectGesture());
}

static void TestFocusSession()
{
    var now = DateTimeOffset.Parse("2026-08-31T00:00:00Z");
    var session = new FocusSession();
    Equal(TimeSpan.FromMinutes(10), session.Start(now, TimeSpan.FromMinutes(10)));
    Equal(TimeSpan.FromMinutes(9), session.Start(now.AddMinutes(1), TimeSpan.FromMinutes(10)));
    Equal(TimeSpan.Zero, session.GetRemaining(now.AddMinutes(11)));
}

static void TestDialogueCatalog()
{
    var catalog = PetDialogueCatalog.Load(typeof(PetDialogueCatalog).Assembly);
    True(catalog.BubbleMessages.Length >= 20, "陪伴文案数量不足");
    True(catalog.SingingLyrics.Length >= 6, "歌词数量不足");
}

static void TestCharacterCatalog()
{
    var characters = new CharacterCatalog(typeof(CharacterCatalog).Assembly).Discover();
    True(characters.Count > 0, "没有发现任何角色资源");
    True(characters.Select(character => character.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == characters.Count,
        "角色 ID 不唯一");
    True(characters.Count(character => character.IsLoafing) == 1, "摸鱼角色必须且只能有一个");
    True(characters.All(character => !string.IsNullOrWhiteSpace(character.DisplayName)), "角色显示名不能为空");
}

static void TestSpriteSheets()
{
    var catalog = new CharacterCatalog(typeof(CharacterCatalog).Assembly);
    foreach (var character in catalog.Discover())
    {
        var sheet = catalog.LoadSprite(character);
        True(sheet.Rows > 0, $"{character.Id} 没有动作行");
        True(sheet.GetFrameCount(0) > 0, $"{character.Id} 没有待机帧");
        True(sheet.GetPlaybackFrameCount(0) > 0, $"{character.Id} 待机动作的播放帧数为 0");
        True(sheet.GetPlayableClickRows().Count > 0, $"{character.Id} 没有互动帧");

        // v1 资源沿用按行号推断的旧路径，不要求动作表；v2 必须能解析出完整映射。
        if (sheet.Actions.SpriteVersionNumber < 2)
        {
            continue;
        }

        foreach (var action in Enum.GetValues<PetAction>())
        {
            var row = sheet.Actions.GetRow(action, sheet.Rows);
            True(row is not null, $"{character.Id} 的动作 {action} 没有映射到有效行");
            True(sheet.GetFrameCount(row!.Value) > 0, $"{character.Id} 的动作 {action} 映射到空行 {row}");
        }

        // 只有满 11 行的 v2 图集才带注视行；行数不足的 v2 图集本就不该被安上注视功能。
        if (sheet.Rows < 11)
        {
            continue;
        }

        True(sheet.Actions.HasLookDirections, $"{character.Id} 是完整 v2 资源但没有注视行");
        for (var direction = 0; direction < 16; direction++)
        {
            True(sheet.GetLookFrame(direction) is not null, $"{character.Id} 缺少第 {direction} 个注视方向");
        }
    }
}

static void TestMatchBoardOpening()
{
    var random = new Random(20260901);
    for (var trial = 0; trial < 1000; trial++)
    {
        var board = new MatchBoard(random);
        Equal(MatchGameOptions.Rows, board.Rows);
        Equal(MatchGameOptions.Columns, board.Columns);
        True(board.FindMatches().Count == 0, $"第 {trial} 局开局就有天然三连");
        True(board.HasLegalSwap(), $"第 {trial} 局开局没有任何合法交换");
        for (var row = 0; row < board.Rows; row++)
        {
            for (var column = 0; column < board.Columns; column++)
            {
                var kind = board.GetKind(row, column);
                True(kind >= 0 && kind < MatchGameOptions.TileKindCount,
                    $"第 {trial} 局的 ({row},{column}) 种类越界：{kind}");
            }
        }
    }
}

static void TestMatchBoardFindsRuns()
{
    var horizontal = BaseGrid();
    horizontal[0, 0] = 0;
    horizontal[0, 1] = 0;
    horizontal[0, 2] = 0;
    Equal("(0,0) (0,1) (0,2)", Matches(horizontal));

    var vertical = BaseGrid();
    vertical[0, 0] = 0;
    vertical[1, 0] = 0;
    vertical[2, 0] = 0;
    Equal("(0,0) (1,0) (2,0)", Matches(vertical));

    // 四连要整段消掉，不能只算头三个。
    var four = BaseGrid();
    four[3, 0] = 1;
    four[3, 1] = 1;
    four[3, 2] = 1;
    four[3, 3] = 1;
    Equal("(3,0) (3,1) (3,2) (3,3)", Matches(four));

    // T 形：横三 + 竖三共用 (3,2)，那一格只能出现一次。
    var tee = BaseGrid();
    tee[3, 1] = 2;
    tee[3, 2] = 2;
    tee[3, 3] = 2;
    tee[4, 2] = 2;
    tee[5, 2] = 2;
    Equal("(3,1) (3,2) (3,3) (4,2) (5,2)", Matches(tee));

    // 两格不算数。
    var pair = BaseGrid();
    pair[6, 0] = 3;
    pair[6, 1] = 3;
    Equal(string.Empty, Matches(pair));
}

static void TestMatchBoardValidatesSwaps()
{
    var kinds = BaseGrid();
    kinds[0, 5] = 4;
    kinds[1, 5] = 4;
    kinds[2, 5] = 1;
    kinds[3, 5] = 4;
    var board = MatchBoard.FromKinds(kinds, new Random(0));
    Equal(0, board.FindMatches().Count);

    True(board.WouldMatch(new Cell(2, 5), new Cell(3, 5)), "把第 3 行的 4 换上来应当连成竖三");
    True(!board.WouldMatch(new Cell(0, 0), new Cell(0, 1)), "这一步连不成任何三连");

    // 试探不得留下痕迹：棋盘、进度都要和试探前一模一样。
    Equal(0, board.FindMatches().Count);
    Equal(1, board.GetKind(2, 5));
    Equal(4, board.GetKind(3, 5));
    Equal(0, board.GetKind(0, 0));
    Equal(1, board.GetKind(0, 1));
    Equal(0, board.ClearedWaveCount);

    True(MatchBoard.AreAdjacent(new Cell(2, 5), new Cell(3, 5)), "上下相邻");
    True(!MatchBoard.AreAdjacent(new Cell(2, 5), new Cell(3, 4)), "斜角不算相邻");
    True(!MatchBoard.AreAdjacent(new Cell(2, 5), new Cell(2, 5)), "同一格不算相邻");
    True(!board.IsInside(new Cell(-1, 0)), "棋盘外的格子要被挡住");
    True(!board.IsInside(new Cell(0, MatchGameOptions.Columns)), "右边界外的格子要被挡住");
}

static void TestMatchBoardClear()
{
    var kinds = BaseGrid();
    kinds[0, 0] = 0;
    kinds[0, 1] = 0;
    kinds[0, 2] = 0;
    var board = MatchBoard.FromKinds(kinds, new Random(0));

    var matches = board.FindMatches();
    Equal(3, matches.Count);

    board.Clear(matches);
    Equal(1, board.ClearedWaveCount);
    foreach (var cell in matches)
    {
        Equal(-1, board.GetKind(cell.Row, cell.Column));
    }

    // 空位不能被当成第四种方块又连成一片。
    Equal(0, board.FindMatches().Count);

    board.Clear([]);
    Equal(1, board.ClearedWaveCount);
}

static void TestMatchBoardCollapse()
{
    var before = BaseGrid();
    var board = MatchBoard.FromKinds(before, new Random(0));
    board.Clear([new Cell(4, 0), new Cell(5, 0)]);

    var fall = board.Collapse();

    // 第 0 列上方 4 个方块整体下落两格；扫描自下而上，所以列表也是自下而上。
    Equal("(3,0)->(5,0) (2,0)->(4,0) (1,0)->(3,0) (0,0)->(2,0)", DescribeMoves(fall.Moves));

    // 落下来的还是原来那几个，先后顺序没被打乱。
    Equal(before[0, 0], board.GetKind(2, 0));
    Equal(before[1, 0], board.GetKind(3, 0));
    Equal(before[2, 0], board.GetKind(4, 0));
    Equal(before[3, 0], board.GetKind(5, 0));
    Equal(before[6, 0], board.GetKind(6, 0));

    // 补位的两个从棋盘上方成组滑入，落距都等于本列空位数 2。
    Equal(2, fall.Spawns.Count);
    Equal(new Cell(1, 0), fall.Spawns[0].To);
    Equal(-1, fall.Spawns[0].EntryRow);
    Equal(fall.Spawns[0].Kind, board.GetKind(1, 0));
    Equal(new Cell(0, 0), fall.Spawns[1].To);
    Equal(-2, fall.Spawns[1].EntryRow);
    Equal(fall.Spawns[1].Kind, board.GetKind(0, 0));

    for (var row = 0; row < board.Rows; row++)
    {
        for (var column = 0; column < board.Columns; column++)
        {
            True(board.GetKind(row, column) >= 0, $"重力结算后 ({row},{column}) 还是空的");
        }
    }
}

static void TestMatchBoardShuffle()
{
    var before = BaseGrid();
    var board = MatchBoard.FromKinds(before, new Random(2026));

    var result = board.Shuffle();

    True(!result.Regenerated, "这局还能靠置换救回来，不该整盘重新生成");
    True(result.Moves.Count > 0, "洗牌至少要挪动一部分方块");
    True(board.FindMatches().Count == 0, "洗牌后不能直接躺着三连");
    True(board.HasLegalSwap(), "洗牌后必须留下至少一步可走");

    // 每条 Move 都要如实说明方块去了哪里，视觉层才敢照着搬。
    foreach (var move in result.Moves)
    {
        Equal(before[move.From.Row, move.From.Column], board.GetKind(move.To.Row, move.To.Column));
        True(move.From != move.To, "原地不动的格子不该出现在 Moves 里");
    }
    Equal(result.Moves.Count, result.Moves.Select(move => move.To).Distinct().Count());
    Equal(result.Moves.Count, result.Moves.Select(move => move.From).Distinct().Count());

    // 置换不生成也不销毁方块。
    Equal(Census(before), Census(Snapshot(board)));
}

static void TestMatchBoardShuffleDeadlock()
{
    // 每种方块都不足 3 个，任何排列都连不成三连，所以这是置换救不回来的死局。
    var deadlock = new[,]
    {
        { 0, 1, 2 },
        { 3, 4, 0 },
        { 1, 2, 3 }
    };
    var board = MatchBoard.FromKinds(deadlock, new Random(11));
    True(!board.HasLegalSwap(), "这个棋盘不该存在任何合法交换");

    var result = board.Shuffle();

    True(result.Regenerated, "置换救不回来时必须重新生成并告知调用方");
    Equal(0, result.Moves.Count);
    True(board.HasLegalSwap(), "重新生成后必须可玩");
    True(board.FindMatches().Count == 0, "重新生成后不该有天然三连");
}

static void TestMatchBoardDragStep()
{
    // 阈值以下不成手势。
    Equal("0,0", DragStep(10, 4));
    Equal("0,0", DragStep(-15.9, 15.9));

    Equal("0,1", DragStep(20, 4));
    Equal("0,-1", DragStep(-20, 4));
    Equal("1,0", DragStep(4, 20));
    Equal("-1,0", DragStep(4, -20));

    // 只走位移大的那个轴，不会斜着走两格。
    Equal("1,0", DragStep(20, 25));
    Equal("0,1", DragStep(25, 20));

    // 打平时取水平，保证结果确定。
    Equal("0,1", DragStep(20, 20));
}

static void TestMatchBoardProgress()
{
    var board = MatchBoard.FromKinds(BaseGrid(), new Random(3));
    Equal(0, board.ClearedWaveCount);

    // 无效交换不推进奖励进度。
    True(!board.WouldMatch(new Cell(0, 0), new Cell(0, 1)), "这一步本就不成型");
    Equal(0, board.ClearedWaveCount);

    // 满盘无空位时的重力结算是一次空操作，同样不推进进度。
    var fall = board.Collapse();
    Equal(0, fall.Moves.Count);
    Equal(0, fall.Spawns.Count);
    Equal(0, board.ClearedWaveCount);

    // 连锁里的每一轮都单独计数。
    board.Clear([new Cell(0, 0), new Cell(0, 1), new Cell(0, 2)]);
    Equal(1, board.ClearedWaveCount);
    board.Collapse();
    Equal(1, board.ClearedWaveCount);
    board.Clear([new Cell(1, 1), new Cell(2, 1), new Cell(3, 1)]);
    Equal(2, board.ClearedWaveCount);

    // 领完奖励后重新开始计数。
    board.ResetWaveCount();
    Equal(0, board.ClearedWaveCount);
}

// 需求文档给动画时长划了区间。调参调过头会让手感变形，所以这里替文档看着。
static void TestMatchGameOptions()
{
    Between(120, 180, MatchGameOptions.SwapMs, "交换动画");
    Between(160, 220, MatchGameOptions.RollbackMs, "回退动画");
    Between(180, 260, MatchGameOptions.ClearMs, "消除动画");
    Between(220, 320, MatchGameOptions.FallMaxMs, "下落动画上限");
    Between(220, 320, MatchGameOptions.FallBaseMs + MatchGameOptions.FallPerRowMs, "下落一格");
    Between(1000, 1500, MatchGameOptions.BonusHoldMs, "Bonus 卡片停留");

    // 落得再远也要留在区间里，所以上限必须真的能压住线性增长。
    var longestFall = Math.Min(
        MatchGameOptions.FallMaxMs,
        MatchGameOptions.FallBaseMs + ((MatchGameOptions.Rows - 1) * MatchGameOptions.FallPerRowMs));
    Between(220, 320, longestFall, "整列清空后的下落");

    True(MatchGameOptions.RewardWaveTarget > 0, "奖励目标必须为正");
    True(MatchGameOptions.OpeningSwapCap > 0, "开局合法交换上限必须为正");
    True(MatchGameOptions.OpeningTries > 0, "开局重抽次数必须为正");
    True(MatchGameOptions.BlockPlaybackRate > 0 && MatchGameOptions.BlockPlaybackRate <= 1,
        "方块 GIF 播放倍率必须在 0–1 之间");

    // 清场是「反对角线依次淡出」，总时长必须真的等于最后一条对角线的起始
    // 加一次淡出，否则 GIF 会压在还没淡完的方块上。
    Equal(
        ((MatchGameOptions.Rows - 1 + MatchGameOptions.Columns - 1) * MatchGameOptions.DissolveStepMs)
            + MatchGameOptions.DissolveFadeMs,
        MatchGameOptions.DissolveTotalMs);
    Between(600, 1400, MatchGameOptions.DissolveTotalMs, "整盘清场");
    Between(150, 400, MatchGameOptions.DissolveFadeMs, "单格淡出");
    Between(150, 400, MatchGameOptions.BoardRestoreMs, "棋盘淡回");
    True(MatchGameOptions.DragThresholdDip > 0, "拖动阈值必须为正");
    Near(MatchGameOptions.TileSize + MatchGameOptions.TileGap, MatchGameOptions.TilePitch);
    Near(
        (MatchGameOptions.Columns * MatchGameOptions.TilePitch) - MatchGameOptions.TileGap,
        MatchGameOptions.BoardWidth);
    Near(
        (MatchGameOptions.Rows * MatchGameOptions.TilePitch) - MatchGameOptions.TileGap,
        MatchGameOptions.BoardHeight);

    var resources = typeof(CharacterCatalog).Assembly.GetManifestResourceNames();
    var blocks = resources
        .Where(name => name.StartsWith(MatchGameOptions.BlockResourcePrefix, StringComparison.Ordinal))
        .ToArray();
    Equal(24, blocks.Length);

    var celebrations = resources
        .Where(name => name.StartsWith(MatchGameOptions.CelebrationResourcePrefix, StringComparison.Ordinal))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    Equal(3, celebrations.Length);
    Equal(
        "CapyLulu.GifResources.MatchGame.Celebrate.61534aaa-01-smile.gif",
        celebrations[0]);
    Equal(
        "CapyLulu.GifResources.MatchGame.Celebrate.61534aaa-03-scooter.gif",
        celebrations[^1]);

    True(resources.Any(name => name.StartsWith("CapyLulu.GifResources.", StringComparison.Ordinal)),
        "gif_resources/ 的嵌入约定变了，庆祝素材的资源名也要跟着改");
}

static void Between(int low, int high, int actual, string subject)
{
    if (actual < low || actual > high)
    {
        throw new InvalidOperationException($"{subject}应在 {low}–{high}ms，实际 {actual}ms");
    }
}

// 基准棋盘 (2r+c)%5：同行相邻差 1、同列相邻差 2，所以天然一个三连都没有，
// 可以在上面精确地摆出要测的形状而不引入意外匹配。
//
// 模数写死 5，不跟着 TileKindCount 走。偶数模会让同一列每 3 行重复一次
// （6 的时候 2*3 ≡ 0），于是摆出来的三连会被基准盘自己接上一格变成四连，
// 摆 T 形时更是直接多出一格。第 6 种方块在这个夹具里不出现，
// 不影响任何一条断言 —— 夹具只需要一块没有三连的填充盘。
static int[,] BaseGrid()
{
    const int fillerKinds = 5;
    var kinds = new int[MatchGameOptions.Rows, MatchGameOptions.Columns];
    for (var row = 0; row < MatchGameOptions.Rows; row++)
    {
        for (var column = 0; column < MatchGameOptions.Columns; column++)
        {
            kinds[row, column] = ((2 * row) + column) % fillerKinds;
        }
    }

    return kinds;
}

static void TestSkin() => OnSta(AssertSkin);

static void TestTileArt() => OnSta(AssertTileArt);

// WPF 控件只能在 STA 线程上造，而顶层语句的 Main 是 MTA，所以借一条线程跑。
static void OnSta(Action assert)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            assert();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
    {
        throw failure;
    }
}

// 开局难度就是"有多少步能成型的交换"。这条断言把它钉住：种类数或生成规则
// 一被调松，可走的步数立刻涨回去，肉眼玩几局未必察觉，断言会立刻红。
static void TestMatchBoardOpeningDifficulty()
{
    var random = new Random(20260903);
    var worst = 0;
    for (var trial = 0; trial < 2000; trial++)
    {
        var swaps = new MatchBoard(random).CountLegalSwaps();
        True(swaps >= 1, $"第 {trial} 局开局无路可走");
        True(swaps <= MatchGameOptions.OpeningSwapCap,
            $"第 {trial} 局开局有 {swaps} 步合法交换，超过上限 {MatchGameOptions.OpeningSwapCap}");
        worst = Math.Max(worst, swaps);
    }

    // 上限得真的在起作用。要是它高到从来碰不到，上面那条就只是同义反复。
    Equal(MatchGameOptions.OpeningSwapCap, worst);
}

// TileKindCount 超过 MatchTileArt 备好的图案数时，Apply 会取模回绕，
// 于是两种方块长得一模一样 —— 棋盘照样能玩，只是这两种永远分不清。
static void AssertTileArt()
{
    var looks = new List<(Color Background, string Glyph)>();
    for (var kind = 0; kind < MatchGameOptions.TileKindCount; kind++)
    {
        var tile = MatchTileArt.Create(kind);
        var glyph = (Shape)tile.Child;
        looks.Add((((SolidColorBrush)tile.Background).Color, DescribeGlyph(glyph)));
    }

    Equal(MatchGameOptions.TileKindCount, looks.Distinct().Count());

    // 底色也必须两两不同：只靠形状区分的话，截图取色那一整套验证就分不出种类了。
    Equal(MatchGameOptions.TileKindCount, looks.Select(look => look.Background).Distinct().Count());

    var selected = MatchTileArt.LoadRandomAnimations(new Random(20260903));
    Equal(MatchGameOptions.TileKindCount, selected.Length);
    True(selected.All(animation => animation is not null), "24 个方块 GIF 中必须能随机加载出 6 个");
    var animatedTile = MatchTileArt.Create(0, selected[0]);
    True(animatedTile.Child is Image, "正式方块应该显示 GIF 画面而不是几何占位图");
}

// 形状用"类型 + 顶点数"概括，够区分圆、方和各种角数的星。
static string DescribeGlyph(Shape glyph) => glyph switch
{
    Polygon polygon => $"polygon{polygon.Points.Count}",
    _ => glyph.GetType().Name
};

static void AssertSkin()
{
    // 漏冻结不会报错，只会让画刷每帧被克隆一份 —— 这种退化只有断言看得住。
    foreach (var (name, brush) in SkinBrushes())
    {
        True(brush.IsFrozen, $"{name} 没有冻结");
    }

    // Sunken 是"把 Raised 的两条斜面边对调"实现的。写反了界面照样跑，
    // 肉眼也很难分出凸起和内凹，正是需要断言的地方。
    var raised = Skin.Raised();
    var sunken = Skin.Sunken();
    Equal(Skin.Highlight, TopLeftEdge(raised));
    Equal(Skin.Shadow, BottomRightEdge(raised));
    Equal(Skin.Shadow, TopLeftEdge(sunken));
    Equal(Skin.Highlight, BottomRightEdge(sunken));

    // 按下要真的把两条边换过来，松开要换得回来 —— 连按两次不该越按越深。
    // 计划里原本写的是"模板里有 IsPressed 触发器"，实现改用了真实元素 + 鼠标事件，
    // 所以这里断言的是行为本身，比断言模板结构更贴近会坏的东西。
    var button = Skin.CreateButton("测试", 40, 40, () => { });
    var face = (Border)button.Content;
    Skin.SetPressed(face, true);
    Equal(Skin.Shadow, TopLeftEdge(face));
    Equal(Skin.Highlight, BottomRightEdge(face));
    Skin.SetPressed(face, true);
    Equal(Skin.Shadow, TopLeftEdge(face));
    Skin.SetPressed(face, false);
    Equal(Skin.Highlight, TopLeftEdge(face));
    Equal(Skin.Shadow, BottomRightEdge(face));

    // LabelOf 拆的正是 CreateButton 自己搭的那三层，两边一旦对不上就是运行时崩。
    Equal("测试", Skin.LabelOf(button).Text);

    // 框的总厚度决定棋盘那块 Field 区还是不是 468 DIP，而 match-harness 就是靠
    // 这个尺寸认出棋盘的。改厚一层不会有任何编译或运行错误，只会让 harness 全线失准。
    Equal(Skin.U, Inset(Skin.Raised()));
    Equal(Skin.U, Inset(Skin.Sunken()));
    Equal(Skin.U * 3, Inset(Skin.Plot(null, 0, Skin.Field)));

    // 点阵按 Bgra32 逐字节写，通道写反了图标照样出得来，只是颜色不对。
    var icon = Skin.IconSource([".0"], Skin.Crimson);
    var read = new uint[2];
    icon.CopyPixels(read, 8, 0);
    Equal(0u, read[0]);
    Equal(0xFF000000u | ((uint)Skin.Crimson.Color.R << 16)
        | ((uint)Skin.Crimson.Color.G << 8) | Skin.Crimson.Color.B, read[1]);
}

// 一层层数过去把左边框加起来 —— Frame 是由外向内套的，左边框的总和就是内容的缩进。
static double Inset(Border frame)
{
    var total = 0.0;
    for (Border? band = frame; band is not null; band = band.Child as Border)
    {
        total += band.BorderThickness.Left;
    }

    return total;
}

// 反射枚举而不是逐个点名：以后往调色板里加颜色，冻结这条自动就管上了。
static IEnumerable<(string Name, Brush Brush)> SkinBrushes()
{
    foreach (var field in typeof(Skin).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        switch (field.GetValue(null))
        {
            case Brush brush:
                yield return (field.Name, brush);
                break;
            case SolidColorBrush[] palette:
                for (var index = 0; index < palette.Length; index++)
                {
                    yield return ($"{field.Name}[{index}]", palette[index]);
                }

                break;
        }
    }
}

static SolidColorBrush TopLeftEdge(Border bevel) =>
    (SolidColorBrush)((Border)bevel.Child!).BorderBrush;

static SolidColorBrush BottomRightEdge(Border bevel) =>
    (SolidColorBrush)((Border)((Border)bevel.Child!).Child!).BorderBrush;

static string Matches(int[,] kinds) =>
    DescribeCells(MatchBoard.FromKinds(kinds, new Random(0)).FindMatches());

static string DescribeCells(IReadOnlyList<Cell> cells) =>
    string.Join(" ", cells.Select(cell => $"({cell.Row},{cell.Column})"));

static string DescribeMoves(IReadOnlyList<TileMove> moves) =>
    string.Join(" ", moves.Select(move =>
        $"({move.From.Row},{move.From.Column})->({move.To.Row},{move.To.Column})"));

static string DragStep(double deltaX, double deltaY)
{
    var (rowStep, columnStep) = MatchBoard.ResolveDragStep(deltaX, deltaY);
    return $"{rowStep},{columnStep}";
}

static int[,] Snapshot(MatchBoard board)
{
    var kinds = new int[board.Rows, board.Columns];
    for (var row = 0; row < board.Rows; row++)
    {
        for (var column = 0; column < board.Columns; column++)
        {
            kinds[row, column] = board.GetKind(row, column);
        }
    }

    return kinds;
}

static string Census(int[,] kinds)
{
    var counts = new int[MatchGameOptions.TileKindCount];
    foreach (var kind in kinds)
    {
        if (kind >= 0)
        {
            counts[kind]++;
        }
    }

    return string.Join(",", counts);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }
}

static void Near(double expected, double actual)
{
    if (Math.Abs(expected - actual) > 0.001)
    {
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
