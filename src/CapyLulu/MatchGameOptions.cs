namespace CapyLulu;

internal static class MatchGameOptions
{
    public const int Rows = 7;
    public const int Columns = 7;
    public const int TileKindCount = 5;

    // 攒够这么多轮有效消除就放一次庆祝。改成关卡目标或分数目标时只动这一个值。
    public const int RewardWaveTarget = 10;

    public const double TileSize = 56;
    public const double TileGap = 8;
    public const double TileCornerRadius = 14;

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

    // 只认这一个名字；缺失时显示提示，不用别的 GIF 顶替。
    public const string CelebrationResourceName =
        "CapyLulu.GifResources.match-game-celebration.gif";

    public const double TilePitch = TileSize + TileGap;
    public const double BoardWidth = (Columns * TilePitch) - TileGap;
    public const double BoardHeight = (Rows * TilePitch) - TileGap;
}
