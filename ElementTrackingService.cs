using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace GWTP_Windows_POC;

internal sealed class ElementTrackingService : IDisposable
{
    private readonly AutomationElement _element;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _healthTimer;
    private IntPtr _hostWindow;
    private IntPtr _winEventHook;
    private WinEventDelegate? _winEventDelegate;
    private bool _disposed;

    public event Action<Rect>? BoundsChanged;
    public event Action? ElementTemporarilyHidden;
    public event Action? ElementUnavailable;

    public ElementTrackingService(AutomationElement element, Dispatcher dispatcher)
    {
        _element = element;
        _dispatcher = dispatcher;

        _healthTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _healthTimer.Tick += (_, _) => RefreshBounds();
    }

    public void Start()
    {
        RefreshBounds();

        try
        {
            Automation.AddAutomationPropertyChangedEventHandler(
                _element,
                TreeScope.Element,
                OnAutomationPropertyChanged,
                AutomationElement.BoundingRectangleProperty,
                AutomationElement.IsOffscreenProperty);
        }
        catch (ElementNotAvailableException)
        {
            NotifyUnavailable();
            return;
        }

        _hostWindow = GetHostWindowHandle(_element);
        if (_hostWindow != IntPtr.Zero)
        {
            _winEventDelegate = OnWinEvent;
            _winEventHook = SetWinEventHook(
                EventObjectLocationChange,
                EventObjectLocationChange,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WineventOutofcontext | WineventSkipownprocess);
        }

        _healthTimer.Start();
    }

    private void OnAutomationPropertyChanged(object sender, AutomationPropertyChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(RefreshBounds);
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed || hwnd == IntPtr.Zero || _hostWindow == IntPtr.Zero)
        {
            return;
        }

        if (hwnd == _hostWindow || IsChild(_hostWindow, hwnd))
        {
            _dispatcher.BeginInvoke(RefreshBounds);
        }
    }

    private void RefreshBounds()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var bounds = _element.Current.BoundingRectangle;
            var isOffscreen = _element.Current.IsOffscreen;

            if (isOffscreen || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                ElementTemporarilyHidden?.Invoke();
                return;
            }

            BoundsChanged?.Invoke(bounds);
        }
        catch (ElementNotAvailableException)
        {
            NotifyUnavailable();
        }
        catch
        {
            NotifyUnavailable();
        }
    }

    private void NotifyUnavailable()
    {
        if (!_disposed)
        {
            ElementUnavailable?.Invoke();
        }
    }

    private static IntPtr GetHostWindowHandle(AutomationElement element)
    {
        try
        {
            var current = element;
            while (current is not null)
            {
                var handle = new IntPtr(current.Current.NativeWindowHandle);
                if (handle != IntPtr.Zero)
                {
                    return GetAncestor(handle, GaRoot);
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _healthTimer.Stop();

        try
        {
            Automation.RemoveAutomationPropertyChangedEventHandler(_element, OnAutomationPropertyChanged);
        }
        catch
        {
        }

        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        _winEventDelegate = null;
    }

    private const uint EventObjectLocationChange = 0x800B;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const uint GaRoot = 2;

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHook,
        WinEventDelegate eventProc,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr winEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parentWindow, IntPtr childWindow);
}
