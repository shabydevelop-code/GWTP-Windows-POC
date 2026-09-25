using System.Diagnostics;
using System.IO;
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
    private IntPtr _elementWindow;
    private IntPtr _rootWindow;
    private IntPtr _ownerWindow;
    private IntPtr _locationWinEventHook;
    private IntPtr _destroyWinEventHook;
    private IntPtr _hideWinEventHook;
    private IntPtr _windowEventHook;
    private WinEventDelegate? _winEventDelegate;
    private Process? _hostProcess;
    private bool _disposed;

    public event Action<Rect>? BoundsChanged;
    public event Action? ElementTemporarilyHidden;
    public event Action? ElementUnavailable;
    public event Action<string>? VisibilityDiagnostic;

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

        CaptureWindowChain(_element);
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
        _dispatcher.BeginInvoke(() =>
        {
            LogVisibilityState($"UIA {e.Property.ProgrammaticName}");
            RefreshBounds();
        });
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
            _dispatcher.BeginInvoke(() =>
            {
                LogVisibilityState("EVENT_OBJECT_HIDE");
                EvaluateHostWindowVisibility();
            });
            return;
        }

        if (eventType == EventSystemMinimizeStart && IsHostWindowEvent(hwnd))
        {
            _dispatcher.BeginInvoke(() =>
            {
                LogVisibilityState("EVENT_SYSTEM_MINIMIZESTART");
                ElementTemporarilyHidden?.Invoke();
            });
            return;
        }

        if (eventType == EventSystemMinimizeEnd && IsHostWindowEvent(hwnd))
        {
            _dispatcher.BeginInvoke(RefreshBounds);
            return;
        }

        if (eventType == EventObjectLocationChange && (hwnd == _hostWindow || IsChild(_hostWindow, hwnd)))
        {
            _dispatcher.BeginInvoke(() =>
            {
                LogVisibilityState("EVENT_OBJECT_LOCATIONCHANGE");
                RefreshBounds();
            });
        }
    }

    private bool IsHostWindowEvent(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _hostWindow == IntPtr.Zero)
        {
            return false;
        }

        return hwnd == _hostWindow ||
               hwnd == _elementWindow ||
               hwnd == _rootWindow ||
               (_ownerWindow != IntPtr.Zero && hwnd == _ownerWindow) ||
               GetAncestor(hwnd, GaRoot) == _rootWindow;
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

    private void LogVisibilityState(string source)
    {
        if (_disposed) return;

        try
        {
            var isWindow = _hostWindow != IntPtr.Zero && IsWindow(_hostWindow);
            var isVisible = isWindow && IsWindowVisible(_hostWindow);
            var isIconic = isWindow && IsIconic(_hostWindow);

            bool? isOffscreen = null;
            Rect bounds = Rect.Empty;
            try
            {
                isOffscreen = _element.Current.IsOffscreen;
                bounds = _element.Current.BoundingRectangle;
            }
            catch (ElementNotAvailableException)
            {
            }

            var line = $"{DateTime.Now:HH:mm:ss.fff} {source} | IsWindow={isWindow} IsVisible={isVisible} IsIconic={isIconic} IsOffscreen={isOffscreen?.ToString() ?? "<unavailable>"} Bounds={bounds}";
            Debug.WriteLine($"[GWTP Visibility] {line}");
            VisibilityDiagnostic?.Invoke(line);

            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GWTP",
                "Logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "windows-visibility.log"),
                $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect runtime tracking.
        }
    }

    private void NotifyUnavailable()
    {
        if (!_disposed)
        {
            ElementUnavailable?.Invoke();
        }
    }

    private void CaptureWindowChain(AutomationElement element)
    {
        try
        {
            var current = element;
            while (current is not null)
            {
                var handle = new IntPtr(current.Current.NativeWindowHandle);
                if (handle != IntPtr.Zero)
                {
                    _elementWindow = handle;
                    _rootWindow = GetAncestor(handle, GaRoot);
                    _ownerWindow = GetWindow(_rootWindow, GwOwner);
                    _hostWindow = _rootWindow;
                    return;
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "<none>";
        var buffer = new System.Text.StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "<unknown>";
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

        if (_windowEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_windowEventHook);
            _windowEventHook = IntPtr.Zero;
        }

        _winEventDelegate = null;
    }

    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const uint GaRoot = 2;
    private const uint GwOwner = 4;
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
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);

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
