using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace GWTP_Windows_POC;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _selectionTimer;
    private bool _isSelecting;
    private bool _mouseWasDown;
    private IntPtr _windowHandle;
    private ElementIdentity? _selectedIdentity;
    private HighlightWindow? _highlightWindow;
    private AutomationElement? _hoveredElement;
    private GuidanceWindow? _guidanceWindow;
    private ElementTrackingService? _elementTracker;

    public MainWindow()
    {
        InitializeComponent();

        _selectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _selectionTimer.Tick += SelectionTimer_Tick;

        SourceInitialized += (_, _) => _windowHandle = new WindowInteropHelper(this).Handle;
        Closed += (_, _) => CloseTrainingOverlay();
    }

    private void SelectElementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSelecting)
        {
            StopSelection("Selection cancelled.");
            return;
        }

        CloseTrainingOverlay();
        _hoveredElement = null;
        _isSelecting = true;
        _mouseWasDown = IsLeftMouseButtonDown();
        SelectElementButton.Content = "Cancel";
        FindElementButton.IsEnabled = false;
        StatusText.Text = "Move to another application and left-click the control to select it.";
        _selectionTimer.Start();
    }

    private void FindElementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIdentity is null)
        {
            StatusText.Text = "Select an element first.";
            return;
        }

        UpdateTrackedHighlight(showFoundStatus: true);
    }

    private void UpdateTrackedHighlight(bool showFoundStatus)
    {
        if (_selectedIdentity is null)
        {
            CloseTrainingOverlay();
            return;
        }

        try
        {
            var element = FindElement(_selectedIdentity);

            if (element is null)
            {
                CloseTrainingOverlay();
                StatusText.Text = "Element is no longer available.";
                return;
            }

            ShowElement(element);
            StartElementTracking(element);

            if (showFoundStatus)
            {
                StatusText.Text = "Element found and highlighted.";
            }
        }
        catch (ElementNotAvailableException)
        {
            CloseTrainingOverlay();
            StatusText.Text = "Element is no longer available.";
        }
        catch (Exception ex)
        {
            CloseTrainingOverlay();
            StatusText.Text = $"Highlight failed: {ex.Message}";
        }
    }

    private void StartElementTracking(AutomationElement element)
    {
        StopElementTracking();

        _highlightWindow ??= new HighlightWindow();
        _guidanceWindow ??= new GuidanceWindow();

        _elementTracker = new ElementTrackingService(element, Dispatcher);
        _elementTracker.BoundsChanged += OnTrackedElementBoundsChanged;
        _elementTracker.ElementTemporarilyHidden += OnTrackedElementTemporarilyHidden;
        _elementTracker.ElementUnavailable += OnTrackedElementUnavailable;
        _elementTracker.Start();
    }

    private void OnTrackedElementBoundsChanged(Rect bounds)
    {
        _highlightWindow ??= new HighlightWindow();
        _highlightWindow.ShowAt(bounds);

        _guidanceWindow ??= new GuidanceWindow();
        _guidanceWindow.ShowNear(bounds);
    }

    private void OnTrackedElementTemporarilyHidden()
    {
        CloseHighlight();

        if (_guidanceWindow is not null)
        {
            _guidanceWindow.Close();
            _guidanceWindow = null;
        }
    }

    private void OnTrackedElementUnavailable()
    {
        CloseTrainingOverlay();
        StatusText.Text = "Element is no longer available.";
    }

    private void StopElementTracking()
    {
        if (_elementTracker is null)
        {
            return;
        }

        _elementTracker.BoundsChanged -= OnTrackedElementBoundsChanged;
        _elementTracker.ElementTemporarilyHidden -= OnTrackedElementTemporarilyHidden;
        _elementTracker.ElementUnavailable -= OnTrackedElementUnavailable;
        _elementTracker.Dispose();
        _elementTracker = null;
    }

    private void SelectionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isSelecting) return;

        var isMouseDown = IsLeftMouseButtonDown();
        var cursorPosition = Forms.Cursor.Position;
        var windowAtPoint = WindowFromPoint(cursorPosition);

        if (windowAtPoint != IntPtr.Zero && !IsOurWindow(windowAtPoint))
        {
            UpdateHoverHighlight(cursorPosition);

            if (isMouseDown && !_mouseWasDown)
            {
                CaptureElement(cursorPosition);
            }
        }
        else
        {
            ClearHoverHighlight();
        }

        _mouseWasDown = isMouseDown;
    }

    private void UpdateHoverHighlight(DrawingPoint cursorPosition)
    {
        try
        {
            var element = AutomationElement.FromPoint(
                new System.Windows.Point(cursorPosition.X, cursorPosition.Y));

            if (element is null)
            {
                ClearHoverHighlight();
                return;
            }

            if (_hoveredElement is not null && AreSameElement(_hoveredElement, element))
            {
                return;
            }

            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                ClearHoverHighlight();
                return;
            }

            _hoveredElement = element;
            _highlightWindow ??= new HighlightWindow();
            _highlightWindow.ShowAt(bounds);
        }
        catch (ElementNotAvailableException)
        {
            ClearHoverHighlight();
        }
        catch
        {
            ClearHoverHighlight();
        }
    }

    private void ClearHoverHighlight()
    {
        _hoveredElement = null;

        if (_isSelecting && _highlightWindow is not null)
        {
            _highlightWindow.Close();
            _highlightWindow = null;
        }
    }

    private static bool AreSameElement(AutomationElement first, AutomationElement second)
    {
        try
        {
            return first.Equals(second);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private void CaptureElement(DrawingPoint cursorPosition)
    {
        try
        {
            var element = AutomationElement.FromPoint(
                new System.Windows.Point(cursorPosition.X, cursorPosition.Y));

            if (element is null)
            {
                StopSelection("No UI Automation element was found.");
                return;
            }

            _selectedIdentity = CreateIdentity(element);
            _hoveredElement = null;
            ShowElement(element);
            StopSelection("Element selected.");
        }
        catch (ElementNotAvailableException)
        {
            StopSelection("The element disappeared before it could be inspected. Try again.");
        }
        catch (Exception ex)
        {
            StopSelection($"Selection failed: {ex.Message}");
        }
    }

    private static ElementIdentity CreateIdentity(AutomationElement element)
    {
        var processId = element.Current.ProcessId;
        return new ElementIdentity(
            element.Current.Name ?? string.Empty,
            element.Current.AutomationId ?? string.Empty,
            element.Current.ControlType,
            GetProcessName(processId));
    }

    private static AutomationElement? FindElement(ElementIdentity identity)
    {
        var root = AutomationElement.RootElement;
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var processIds = Process.GetProcessesByName(identity.ProcessName)
            .Where(process =>
            {
                try
                {
                    return process.SessionId == currentSessionId;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    process.Dispose();
                }
            })
            .Select(process => process.Id)
            .ToHashSet();

        if (processIds.Count == 0) return null;

        var conditions = new List<System.Windows.Automation.Condition>
        {
            new PropertyCondition(AutomationElement.ControlTypeProperty, identity.ControlType)
        };

        if (!string.IsNullOrWhiteSpace(identity.AutomationId))
        {
            conditions.Add(new PropertyCondition(
                AutomationElement.AutomationIdProperty, identity.AutomationId));
        }

        if (!string.IsNullOrWhiteSpace(identity.Name))
        {
            conditions.Add(new PropertyCondition(
                AutomationElement.NameProperty, identity.Name));
        }

        var candidates = root.FindAll(
            TreeScope.Descendants,
            new AndCondition(conditions.ToArray()));

        foreach (AutomationElement candidate in candidates)
        {
            try
            {
                if (processIds.Contains(candidate.Current.ProcessId)) return candidate;
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        return null;
    }

    private void ShowElement(AutomationElement element)
    {
        var processId = element.Current.ProcessId;
        NameValue.Text = DisplayValue(element.Current.Name);
        AutomationIdValue.Text = DisplayValue(element.Current.AutomationId);
        ControlTypeValue.Text = DisplayValue(element.Current.ControlType?.ProgrammaticName);
        ProcessValue.Text = DisplayValue(GetProcessName(processId));
        ProcessIdValue.Text = processId > 0 ? processId.ToString() : "—";
    }

    private void CloseTrainingOverlay()
    {
        StopElementTracking();
        CloseHighlight();

        if (_guidanceWindow is not null)
        {
            _guidanceWindow.Close();
            _guidanceWindow = null;
        }
    }

    private void CloseHighlight()
    {
        if (_highlightWindow is not null)
        {
            _highlightWindow.Close();
            _highlightWindow = null;
        }
    }

    private bool IsOurWindow(IntPtr windowHandle)
    {
        if (_windowHandle == IntPtr.Zero) return false;
        return windowHandle == _windowHandle || IsChild(_windowHandle, windowHandle);
    }

    private void StopSelection(string status)
    {
        _selectionTimer.Stop();
        _isSelecting = false;
        _mouseWasDown = false;
        _hoveredElement = null;
        if (_highlightWindow is not null)
        {
            _highlightWindow.Close();
            _highlightWindow = null;
        }
        SelectElementButton.Content = "Select Element";
        FindElementButton.IsEnabled = _selectedIdentity is not null;
        StatusText.Text = status;
    }

    private static bool IsLeftMouseButtonDown()
    {
        const int leftMouseButton = 0x01;
        return (GetAsyncKeyState(leftMouseButton) & 0x8000) != 0;
    }

    private static string GetProcessName(int processId)
    {
        if (processId <= 0) return string.Empty;

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DisplayValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(DrawingPoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parentWindow, IntPtr childWindow);

    private sealed record ElementIdentity(
        string Name,
        string AutomationId,
        ControlType ControlType,
        string ProcessName);
}
