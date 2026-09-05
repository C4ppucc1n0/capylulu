#if DEBUG
using System.Windows;

namespace CapyLulu;

// 只编进 Debug 的调试入口，用来跳过“启动桌宠 → 右键 → 一路点到目标界面”这段路。
// 改完 Skin 或某个窗口的布局后，直接开到要看的那一屏；Bonus 卡片和奖励 GIF
// 原本得真的玩到达成阈值才看得见，这里一条命令就到。
//
//   dotnet run --project src/CapyLulu -- --window=player
//   dotnet run --project src/CapyLulu -- --window=match
//   dotnet run --project src/CapyLulu -- --window=bonus
//   dotnet run --project src/CapyLulu -- --window=celebration
//
// 整个文件在 Release 下不参与编译，正式 EXE 不认识任何命令行参数，
// 和 README 里“运行时不包含额外功能”的说法保持一致。
internal static class DebugStartup
{
    private const string Prefix = "--window=";

    // 返回 true 表示已经按调试入口处理完毕，调用方不要再走正常启动流程。
    public static bool TryHandle(string[] args, Application app)
    {
        var target = args
            .FirstOrDefault(a => a.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            ?[Prefix.Length..];
        if (string.IsNullOrEmpty(target))
        {
            return false;
        }

        // 调试窗口不参与单实例互斥：桌宠开着的时候也要能单独开一个窗口来看。
        // 这条路上没有 MainWindow，所以把退出条件从“主窗口关闭”改成“最后一个窗口关闭”。
        app.ShutdownMode = ShutdownMode.OnLastWindowClose;

        switch (target.ToLowerInvariant())
        {
            case "player":
                Show(new MusicPlayerWindow());
                return true;

            case "match":
                MatchGameWindow.ShowSingle();
                return true;

            case "bonus":
                MatchGameWindow.ShowSingleCelebrating();
                return true;

            case "celebration":
                // Fire 在系统关闭了动画效果时会拒绝开窗。那种情况下一个窗口都没有，
                // OnLastWindowClose 永远等不到，进程会挂着不退——所以显式收场。
                if (!ScreenCelebration.Fire())
                {
                    Fail("屏幕庆祝动画没有启动。请检查 Windows 的“动画效果”辅助功能设置是否已关闭。");
                }

                return true;

            default:
                Fail($"未知的调试窗口：{target}\n\n可用值：player、match、bonus、celebration");
                return true;
        }

        void Fail(string message)
        {
            MessageBox.Show(message, "CapyLulu 调试入口", MessageBoxButton.OK, MessageBoxImage.Information);
            app.Shutdown();
        }
    }

    private static void Show(Window window)
    {
        window.Show();
        window.Activate();
    }
}
#endif
