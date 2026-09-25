using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace GWTP_Windows_POC;

internal sealed class ElementTrackingService : IDisposable
{
    private readonly AutomationElement _element;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hostWindow;
    private IntPtr _locationWinEventHook;
    private IntPtr _destroyWinEventHook;
    private IntPtr _hideWinEventHook;
    private IntPtr _foregroundWinEventHook;
    private IntPtr _windowEventHook;
    private WinEventDelegate? _winEventDelegate;
    private Process? _hostProcess;
    private bool _disposed;

    public event Action<Rect>? BoundsChanged;
    public event Action? ElementTemporarilyHidden;
    public event Action? ElementUnavailable;

    public ElementTrackingService(AutomationElement element, Dispatcher dispatcher)
    {
        _element = element;
        _dispatcher = dispatcher;

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

            GetWindowThreadProcessId(_hostWindow, out var processId);
            if (processId != 0)
            {
                try
                {
                    _hostProcess = Process.GetProcessById((int)processId);
                    _hostProcess.EnableRaisingEvents = true;
                    _hostProcess.Exited += OnHostProcessExited;
                }
                catch (ArgumentException)
                {
                    NotifyUnavailable();
                    return;
                }
                catch (InvalidOperationException)
                {
                    NotifyUnavailable();
                    return;
                }
            }

            _locationWinEventHook = SetWinEventHook(
                EventObjectLocationChange,
                EventObjectLocationChange,
                IntPtr.Zero,
                _winEventDelegate,
                processId,
                0,
                WineventOutofcontext | WineventSkipownprocess);

            _destroyWinEventHook = SetWinEventHook(
                EventObjectDestroy,
                EventObjectDestroy,
                IntPtr.Zero,
                _winEventDelegate,
                processId,
                0,
                WineventOutofcontext | WineventSkipownprocess);

            _hideWinEventHook = SetWinEventHook(
                EventObjectHide,
                EventObjectHide,
                IntPtr.Zero,
                _winEventDelegate,
                processId,
                0,
                WineventOutofcontext | WineventSkipownprocess);

            _foregroundWinEventHook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WineventOutofcontext | WineventSkipownprocess);

            _windowEventHook = SetWinEventHook(
                EventSystemMinimizeStart,
                EventSystemMinimizeEnd,
                IntPtr.Zero,
                _winEventDelegate,
                processId,
                0,
                WineventOutofcontext | WineventSkipownprocess);
        }

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

        if (eventType == EventObjectDestroy &&
            hwnd == _hostWindow &&
            objectId == ObjidWindow)
        {
            _dispatcher.BeginInvoke(NotifyUnavailable);
            return;
        }

        if (eventType == EventObjectHide &&
            hwnd == _hostWindow &&
            objectId == ObjidWindow)
        {
            _dispatcher.BeginInvoke(EvaluateHostWindowVisibility);
            return;
        }

        if (eventType == EventSystemMinimizeStart && hwnd == _hostWindow)
        {
            _dispatcher.BeginInvoke(() => ElementTemporarilyHidden?.Invoke());
            return;
        }

        if (eventType == EventSystemMinimizeEnd && hwnd == _hostWindow)
        {
            _dispatcher.BeginInvoke(RefreshBounds);
            return;
        }

        if (eventType == EventSystemForeground)
        {
            _dispatcher.BeginInvoke(EvaluateHostWindowVisibility);
            return;
        }

        if (eventType == EventObjectLocationChange && (hwnd == _hostWindow || IsChild(_hostWindow, hwnd)))
        {
            _dispatcher.BeginInvoke(RefreshBounds);
        }
    }

    private void EvaluateHostWindowVisibility()
    {
        if (_disposed || _hostWindow == IntPtr.Zero)
        {
            return;
        }

        if (!IsWindow(_hostWindow))
        {
            NotifyUnavailable();
            return;
        }

        if (IsIconic(_hostWindow))
        {
            ElementTemporarilyHidden?.Invoke();
            return;
        }

        if (!IsWindowVisible(_hostWindow))
        {
            NotifyUnavailable();
        }
    }

    private void OnHostProcessExited(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _dispatcher.BeginInvoke(NotifyUnavailable);
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
        try
        {
            Automation.RemoveAutomationPropertyChangedEventHandler(_element, OnAutomationPropertyChanged);
        }
        catch
        {
        }

        if (_hostProcess is not null)
        {
            _hostProcess.Exited -= OnHostProcessExited;
            _hostProcess.Dispose();
            _hostProcess = null;
        }

        if (_locationWinEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_locationWinEventHook);
            _locationWinEventHook = IntPtr.Zero;
        }

        if (_destroyWinEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_destroyWinEventHook);
            _destroyWinEventHook = IntPtr.Zero;
        }

        if (_hideWinEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hideWinEventHook);
            _hideWinEventHook = IntPtr.Zero;
        }

        if (_foregroundWinEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundWinEventHook);
            _foregroundWinEventHook = IntPtr.Zero;
        }

        if (_windowEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_windowEventHook);
            _windowEventHook = IntPtr.Zero;
        }

        _winEventDelegate = null;
    }

    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const uint GaRoot = 2;
    private const int ObjidWindow = 0;

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
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parentWindow, IntPtr childWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);
}
