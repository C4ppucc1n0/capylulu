namespace CapyLulu;

// 棋盘规则的唯一实现，不引用任何 WPF 类型，所以验证程序可以直接驱动它。
// 每个会改变棋盘的方法都返回「发生了什么」，由窗口决定怎么播动画。
internal sealed class MatchBoard
{
    private const int Empty = -1;
    private const int MaxShuffleAttempts = 200;

    private readonly int[,] _kinds;
    private readonly Random _random;

    // 随机源由外部注入，测试用固定种子即可复现同一局棋盘。
    public MatchBoard(Random random)
    {
        _random = random;
        _kinds = new int[MatchGameOptions.Rows, MatchGameOptions.Columns];
        Reset();
    }

    private MatchBoard(int[,] kinds, Random random)
    {
        _kinds = kinds;
        _random = random;
    }

    // 从字面棋盘构造，供测试写出精确的匹配形状。
    public static MatchBoard FromKinds(int[,] kinds, Random random) =>
        new((int[,])kinds.Clone(), random);

    public int Rows => _kinds.GetLength(0);

    public int Columns => _kinds.GetLength(1);

    // 只有 Clear 会 +1，所以无效交换和纯下落天然不增加奖励进度。
    public int ClearedWaveCount { get; private set; }

    // 消除后、重力前的空位返回 -1。
    public int GetKind(int row, int column) => _kinds[row, column];

    public bool IsInside(int row, int column) =>
        row >= 0 && row < Rows && column >= 0 && column < Columns;

    public bool IsInside(Cell cell) => IsInside(cell.Row, cell.Column);

    public static bool AreAdjacent(Cell a, Cell b) =>
        Math.Abs(a.Row - b.Row) + Math.Abs(a.Column - b.Column) == 1;

    public void ResetWaveCount() => ClearedWaveCount = 0;

    // 重新生成一局：填满、无天然三连、有路可走但不能一眼全是路。
    // 合法交换压在 OpeningSwapCap 以内，玩家得真的扫一遍盘面才找得到；
    // 抽不到就退回"至少有一步"的底线，循环有界。
    public void Reset()
    {
        for (var attempt = 0; ; attempt++)
        {
            FillWithoutTriples();
            var swaps = CountLegalSwaps();
            if (swaps == 0)
            {
                continue;
            }

            if (swaps <= MatchGameOptions.OpeningSwapCap || attempt >= MatchGameOptions.OpeningTries)
            {
                return;
            }
        }
    }

    // 试探交换能否成三连；不留下任何数据变化。
    public bool WouldMatch(Cell a, Cell b)
    {
        SwapKinds(a, b);
        var matched = CreatesMatchAt(a) || CreatesMatchAt(b);
        SwapKinds(a, b);
        return matched;
    }

    public void Swap(Cell a, Cell b) => SwapKinds(a, b);

    // 去重后的全部待消除格，按行优先顺序返回，顺序稳定便于断言。
    public IReadOnlyList<Cell> FindMatches()
    {
        var marked = new bool[Rows, Columns];
        MarkHorizontalRuns(marked);
        MarkVerticalRuns(marked);

        var cells = new List<Cell>();
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if (marked[row, column])
                {
                    cells.Add(new Cell(row, column));
                }
            }
        }

        return cells;
    }

    // 消除动画播完之后才调用，真正抹掉数据。
    public void Clear(IReadOnlyList<Cell> cells)
    {
        if (cells.Count == 0)
        {
            return;
        }

        foreach (var cell in cells)
        {
            _kinds[cell.Row, cell.Column] = Empty;
        }

        ClearedWaveCount++;
    }

    // 每列独立结算重力并补满顶部，返回谁从哪里移到哪里、谁是新生成的。
    public FallResult Collapse()
    {
        var moves = new List<TileMove>();
        var spawns = new List<TileSpawn>();

        for (var column = 0; column < Columns; column++)
        {
            var target = Rows - 1;
            for (var row = Rows - 1; row >= 0; row--)
            {
                if (_kinds[row, column] == Empty)
                {
                    continue;
                }

                if (row != target)
                {
                    _kinds[target, column] = _kinds[row, column];
                    _kinds[row, column] = Empty;
                    moves.Add(new TileMove(new Cell(row, column), new Cell(target, column)));
                }

                target--;
            }

            // target 以上全是空位。新方块整体从棋盘上方滑入，落距等于本列空位数。
            var holes = target + 1;
            for (var row = target; row >= 0; row--)
            {
                var kind = _random.Next(MatchGameOptions.TileKindCount);
                _kinds[row, column] = kind;
                spawns.Add(new TileSpawn(new Cell(row, column), kind, row - holes));
            }
        }

        return new FallResult(moves, spawns);
    }

    public bool HasLegalSwap() => CountLegalSwaps() > 0;

    // 盘面上一共有多少步能成型的交换。这个数就是「找消除有多容易」的度量：
    // 开局难度靠它定，也靠它守住——纯色种类或生成规则一变，它立刻跟着变。
    public int CountLegalSwaps()
    {
        var count = 0;
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var cell = new Cell(row, column);
                if (column + 1 < Columns && WouldMatch(cell, new Cell(row, column + 1)))
                {
                    count++;
                }

                if (row + 1 < Rows && WouldMatch(cell, new Cell(row + 1, column)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    // 洗牌是一次纯置换，所以窗口可以让同一批方块滑到新位置而不是整盘重建。
    public ShuffleResult Shuffle()
    {
        var cells = new Cell[Rows * Columns];
        var originalKinds = new int[cells.Length];
        var order = new int[cells.Length];
        var index = 0;
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                cells[index] = new Cell(row, column);
                originalKinds[index] = _kinds[row, column];
                order[index] = index;
                index++;
            }
        }

        for (var attempt = 0; attempt < MaxShuffleAttempts; attempt++)
        {
            ShuffleInPlace(order);
            for (var i = 0; i < cells.Length; i++)
            {
                _kinds[cells[i].Row, cells[i].Column] = originalKinds[order[i]];
            }

            if (FindMatches().Count != 0 || !HasLegalSwap())
            {
                continue;
            }

            var moves = new List<TileMove>();
            for (var i = 0; i < cells.Length; i++)
            {
                if (order[i] != i)
                {
                    moves.Add(new TileMove(cells[order[i]], cells[i]));
                }
            }

            return new ShuffleResult(moves, false);
        }

        // 置换找不到可玩排列时退回重新生成；调用方据此重建视觉层。
        Reset();
        return new ShuffleResult([], true);
    }

    // 位移不足阈值返回 (0, 0)；否则只沿位移较大的那个方向走一格。
    public static (int RowStep, int ColumnStep) ResolveDragStep(double deltaX, double deltaY)
    {
        if (Math.Abs(deltaX) < MatchGameOptions.DragThresholdDip
            && Math.Abs(deltaY) < MatchGameOptions.DragThresholdDip)
        {
            return (0, 0);
        }

        return Math.Abs(deltaX) >= Math.Abs(deltaY)
            ? (0, Math.Sign(deltaX))
            : (Math.Sign(deltaY), 0);
    }

    private void FillWithoutTriples()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                _kinds[row, column] = PickKindWithoutTriple(row, column);
            }
        }
    }

    // 按行优先填充时，只要避开「左边两格同色」和「上面两格同色」，就不会出现天然三连。
    private int PickKindWithoutTriple(int row, int column)
    {
        var horizontalBan = column >= 2 && _kinds[row, column - 1] == _kinds[row, column - 2]
            ? _kinds[row, column - 1]
            : Empty;
        var verticalBan = row >= 2 && _kinds[row - 1, column] == _kinds[row - 2, column]
            ? _kinds[row - 1, column]
            : Empty;

        while (true)
        {
            var kind = _random.Next(MatchGameOptions.TileKindCount);
            if (kind != horizontalBan && kind != verticalBan)
            {
                return kind;
            }
        }
    }

    private void SwapKinds(Cell a, Cell b) =>
        (_kinds[a.Row, a.Column], _kinds[b.Row, b.Column]) =
            (_kinds[b.Row, b.Column], _kinds[a.Row, a.Column]);

    // 一次交换只动两格，任何新出现的连线必然穿过其中一格，所以只查这一格的行和列。
    private bool CreatesMatchAt(Cell cell)
    {
        if (_kinds[cell.Row, cell.Column] == Empty)
        {
            return false;
        }

        return CountRun(cell, 0, -1) + CountRun(cell, 0, 1) >= 2
            || CountRun(cell, -1, 0) + CountRun(cell, 1, 0) >= 2;
    }

    private int CountRun(Cell cell, int rowStep, int columnStep)
    {
        var kind = _kinds[cell.Row, cell.Column];
        var count = 0;
        var row = cell.Row + rowStep;
        var column = cell.Column + columnStep;
        while (IsInside(row, column) && _kinds[row, column] == kind)
        {
            count++;
            row += rowStep;
            column += columnStep;
        }

        return count;
    }

    private void MarkHorizontalRuns(bool[,] marked)
    {
        for (var row = 0; row < Rows; row++)
        {
            var runStart = 0;
            for (var column = 1; column <= Columns; column++)
            {
                if (column < Columns && _kinds[row, column] == _kinds[row, runStart])
                {
                    continue;
                }

                MarkRun(marked, row, runStart, row, column - 1);
                runStart = column;
            }
        }
    }

    private void MarkVerticalRuns(bool[,] marked)
    {
        for (var column = 0; column < Columns; column++)
        {
            var runStart = 0;
            for (var row = 1; row <= Rows; row++)
            {
                if (row < Rows && _kinds[row, column] == _kinds[runStart, column])
                {
                    continue;
                }

                MarkRun(marked, runStart, column, row - 1, column);
                runStart = row;
            }
        }
    }

    // 四连五连整段标记；重叠的十字、T、L 形只会把同一格标一次，天然去重。
    private void MarkRun(bool[,] marked, int startRow, int startColumn, int endRow, int endColumn)
    {
        if (_kinds[startRow, startColumn] == Empty)
        {
            return;
        }

        var length = (endRow - startRow) + (endColumn - startColumn) + 1;
        if (length < 3)
        {
            return;
        }

        for (var row = startRow; row <= endRow; row++)
        {
            for (var column = startColumn; column <= endColumn; column++)
            {
                marked[row, column] = true;
            }
        }
    }

    private void ShuffleInPlace(int[] values)
    {
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}

internal readonly record struct Cell(int Row, int Column);

internal readonly record struct TileMove(Cell From, Cell To);

// EntryRow 为负数：新方块从棋盘上方进入，不直接出现在最终位置。
internal readonly record struct TileSpawn(Cell To, int Kind, int EntryRow);

internal sealed record FallResult(
    IReadOnlyList<TileMove> Moves,
    IReadOnlyList<TileSpawn> Spawns);

// Regenerated 为 true 时方块种类已经变了，调用方必须重建视觉层而不能复用元素。
internal sealed record ShuffleResult(
    IReadOnlyList<TileMove> Moves,
    bool Regenerated);
