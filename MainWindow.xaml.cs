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

    public MainWindow()
    {
        InitializeComponent();

        _selectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _selectionTimer.Tick += SelectionTimer_Tick;

        SourceInitialized += (_, _) =>
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
        };
    }

    private void SelectElementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSelecting)
        {
            StopSelection("Selection cancelled.");
            return;
        }

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

        try
        {
            var element = FindElement(_selectedIdentity);

            if (element is null)
            {
                StatusText.Text = "Element not found.";
                return;
            }

            ShowElement(element);
            StatusText.Text = "Element found again.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Find failed: {ex.Message}";
        }
    }

    private void SelectionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        var isMouseDown = IsLeftMouseButtonDown();

        if (isMouseDown && !_mouseWasDown)
        {
            var cursorPosition = Forms.Cursor.Position;
            var windowAtPoint = WindowFromPoint(cursorPosition);

            if (windowAtPoint != IntPtr.Zero && !IsOurWindow(windowAtPoint))
            {
                CaptureElement(cursorPosition);
            }
        }

        _mouseWasDown = isMouseDown;
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
        var processIds = Process.GetProcessesByName(identity.ProcessName)
            .Select(process => process.Id)
            .ToHashSet();

        if (processIds.Count == 0)
        {
            return null;
        }

        var conditions = new List<System.Windows.Automation.Condition>
        {
            new PropertyCondition(AutomationElement.ControlTypeProperty, identity.ControlType)
        };

        if (!string.IsNullOrWhiteSpace(identity.AutomationId))
        {
            conditions.Add(new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                identity.AutomationId));
        }

        if (!string.IsNullOrWhiteSpace(identity.Name))
        {
            conditions.Add(new PropertyCondition(
                AutomationElement.NameProperty,
                identity.Name));
        }

        var candidates = root.FindAll(
            TreeScope.Descendants,
            new AndCondition(conditions.ToArray()));

        foreach (AutomationElement candidate in candidates)
        {
            try
            {
                if (processIds.Contains(candidate.Current.ProcessId))
                {
                    return candidate;
                }
            }
            catch (ElementNotAvailableException)
            {
                // Candidate disappeared while the UI Automation tree was being enumerated.
            }
        }

        return null;
    }

    private void ShowElement(AutomationElement element)
    {
        var processId = element.Current.ProcessId;
        var processName = GetProcessName(processId);

        NameValue.Text = DisplayValue(element.Current.Name);
        AutomationIdValue.Text = DisplayValue(element.Current.AutomationId);
        ControlTypeValue.Text = DisplayValue(element.Current.ControlType?.ProgrammaticName);
        ProcessValue.Text = DisplayValue(processName);
        ProcessIdValue.Text = processId > 0 ? processId.ToString() : "—";
    }

    private bool IsOurWindow(IntPtr windowHandle)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return false;
        }

        return windowHandle == _windowHandle || IsChild(_windowHandle, windowHandle);
    }

    private void StopSelection(string status)
    {
        _selectionTimer.Stop();
        _isSelecting = false;
        _mouseWasDown = false;
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
        if (processId <= 0)
        {
            return string.Empty;
        }

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
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

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
