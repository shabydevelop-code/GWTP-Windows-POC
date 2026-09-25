using System.Windows.Automation;
using System.Windows.Threading;

namespace GWTP_Windows_POC;

internal sealed class PendingWindowTargetWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _windowOpened;
    private AutomationEventHandler? _handler;
    private bool _disposed;

    public PendingWindowTargetWatcher(Dispatcher dispatcher, Action windowOpened)
    {
        _dispatcher = dispatcher;
        _windowOpened = windowOpened;
    }

    public void Start()
    {
        if (_disposed || _handler is not null) return;

        _handler = (_, _) =>
        {
            if (_disposed) return;
            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed) _windowOpened();
            });
        };

        Automation.AddAutomationEventHandler(
            WindowPattern.WindowOpenedEvent,
            AutomationElement.RootElement,
            TreeScope.Descendants,
            _handler);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handler is not null)
        {
            try
            {
                Automation.RemoveAutomationEventHandler(
                    WindowPattern.WindowOpenedEvent,
                    AutomationElement.RootElement,
                    _handler);
            }
            catch
            {
                // Cleanup must not prevent runtime shutdown.
            }

            _handler = null;
        }
    }
}
