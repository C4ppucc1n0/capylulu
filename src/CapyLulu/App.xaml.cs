using System.Windows;
using System.Windows.Threading;

namespace CapyLulu;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\CapyLulu.DesktopPet.SingleInstance";
    private const string ActivationEventName = "Local\\CapyLulu.DesktopPet.Activate";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        _ownsSingleInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            TrySignalRunningInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _activationCancellation = new CancellationTokenSource();
        _ = Task.Run(() => WaitForActivationRequests(_activationCancellation.Token));

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation?.Cancel();
        _activationEvent?.Set();
        _activationEvent?.Dispose();
        _activationCancellation?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            if (_ownsSingleInstanceMutex)
            {
                _singleInstanceMutex.ReleaseMutex();
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static void TrySignalRunningInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 首个实例正处于启动或退出的极短窗口，第二个实例直接退出即可。
        }
    }

    private void WaitForActivationRequests(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _activationEvent is not null)
            {
                _activationEvent.WaitOne();
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow window)
                    {
                        window.RevealFromSecondLaunch();
                    }
                });
            }
        }
        catch (ObjectDisposedException)
        {
            // 程序正在退出。
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"桌宠遇到了无法恢复的问题：\n\n{e.Exception.Message}",
            "CapyLulu",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }
}
