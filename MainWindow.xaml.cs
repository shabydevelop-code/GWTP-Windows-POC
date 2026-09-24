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
        StatusText.Text = "Move to another application and left-click the control to select it.";
        _selectionTimer.Start();
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

            var processId = element.Current.ProcessId;
            var processName = GetProcessName(processId);

            NameValue.Text = DisplayValue(element.Current.Name);
            AutomationIdValue.Text = DisplayValue(element.Current.AutomationId);
            ControlTypeValue.Text = DisplayValue(element.Current.ControlType?.ProgrammaticName);
            ProcessValue.Text = DisplayValue(processName);
            ProcessIdValue.Text = processId > 0 ? processId.ToString() : "—";

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
}
