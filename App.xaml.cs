namespace DiskHealthAdvisor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\DiskHealthAdvisor.SingleInstance";
    private const string ShowEventName = "Local\\DiskHealthAdvisor.ShowMainWindow";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showMainWindowEvent;
    private RegisteredWaitHandle? _showMainWindowRegistration;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showMainWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showMainWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showMainWindowEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.BeginInvoke(ShowExistingMainWindow);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _showMainWindowRegistration?.Unregister(null);
        _showMainWindowEvent?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Shutdown should still continue if the mutex is already released.
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void ShowExistingMainWindow()
    {
        if (MainWindow is MainWindow window)
        {
            window.ShowFromSingleInstanceSignal();
        }
    }

    private static void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
                showEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
