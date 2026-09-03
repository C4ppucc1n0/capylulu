namespace CapyLulu;

internal static class MatchGameOptions
{
    public const int Rows = 7;
    public const int Columns = 7;
    // 方块种类。5 种时开局的合法交换中位数是 14 步（84 组相邻里有 14 组能成型），
    // 一眼就能撞见一处，所以提到 6 —— 中位数降到 10。
    // 上限就是 MatchTileArt 备好的图案数，再往上加会有两种方块长得一模一样。
    public const int TileKindCount = 6;

    // 开局盘面允许的合法交换数上限。「找消除太容易」的直接度量就是这个数，
    // 所以直接卡住它：种类数把中位数压到 10，这条再把开局压到 6 以内。
    public const int OpeningSwapCap = 6;

    // 抽这么多次仍达不到上限，就退回"至少有一步"这条底线。
    // 循环有界，运气差也不会卡在生成里。
    public const int OpeningTries = 200;

    // 攒够这么多轮有效消除就放一次庆祝。改成关卡目标或分数目标时只动这一个值。
    public const int RewardWaveTarget = 10;

    public const double TileSize = 56;
    public const double TileGap = 8;
    // 小圆角保留柔和感，但不再像移动端圆角卡片；硬朗轮廓更贴近像素游戏棋子。
    public const double TileCornerRadius = 6;
    public const double BlockPlaybackRate = 0.5;

    // 与桌宠的 PetBehaviorOptions.DragThreshold 无关，棋盘用自己的阈值。
    public const double DragThresholdDip = 16;

    public const int SwapMs = 150;
    public const int RollbackMs = 190;
    public const int ClearMs = 220;
    public const int FallBaseMs = 240;
    public const int FallPerRowMs = 22;
    // 落得再远也不超过这个时长，免得整列清空时慢得像卡住。
    public const int FallMaxMs = 320;
    public const int ShuffleMs = 380;
    public const int FollowSnapBackMs = 120;
    public const int BonusHoldMs = 1250;
    public const int CardExitMs = 260;
    public const int ConfettiMs = 1400;
    public const int MissingAssetHoldMs = 1600;

    // 清场：反对角线上的方块同时淡出，每条对角线错开 DissolveStepMs。
    // 整段时长 = 12 条对角线的错位 + 最后一格的淡出。
    public const int DissolveStepMs = 55;
    public const int DissolveFadeMs = 240;
    public const int DissolveTotalMs = ((Rows - 1 + Columns - 1) * DissolveStepMs) + DissolveFadeMs;
    public const int BoardRestoreMs = 260;

    // 手势没到阈值时，方块最多跟着指针挪这么远，让人看出「正在拖谁」。
    public const double FollowMaxOffset = TileSize * 0.42;

    public const string BlockResourcePrefix =
        "CapyLulu.GifResources.MatchGame.Block.";

    public const string CelebrationResourcePrefix =
        "CapyLulu.GifResources.MatchGame.Celebrate.";

    public const double TilePitch = TileSize + TileGap;
    public const double BoardWidth = (Columns * TilePitch) - TileGap;
    public const double BoardHeight = (Rows * TilePitch) - TileGap;
}
