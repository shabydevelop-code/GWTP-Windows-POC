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
    private readonly List<ElementIdentity> _testSteps = new();
    private int _currentTestStepIndex = -1;
    private const string ValidationTestExpectedValue = "GWTP";
    private HighlightWindow? _highlightWindow;
    private AutomationElement? _hoveredElement;
    private GuidanceWindow? _guidanceWindow;
    private ElementTrackingService? _elementTracker;
    private PendingWindowTargetWatcher? _pendingTargetWatcher;

    public MainWindow()
    {
        InitializeComponent();

        _selectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _selectionTimer.Tick += SelectionTimer_Tick;

        SourceInitialized += (_, _) => _windowHandle = new WindowInteropHelper(this).Handle;
        Closing += (_, _) =>
        {
            _selectionTimer.Stop();
            CloseTrainingOverlay();
        };
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
                if (_currentTestStepIndex >= 0 && _currentTestStepIndex < _testSteps.Count)
                {
                    WaitForCurrentTestStepTarget();
                }
                else
                {
                    CloseTrainingOverlay();
                    StatusText.Text = "Element is no longer available.";
                }
                return;
            }

            StopPendingTargetWait();
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
        EnsureGuidanceWindow();

        _elementTracker = new ElementTrackingService(element, Dispatcher);
        _elementTracker.BoundsChanged += OnTrackedElementBoundsChanged;
        _elementTracker.ElementTemporarilyHidden += OnTrackedElementTemporarilyHidden;
        _elementTracker.ElementUnavailable += OnTrackedElementUnavailable;
        _elementTracker.HostActivated += OnTrackedHostActivated;
        _elementTracker.Start();
    }

    private void EnsureGuidanceWindow()
    {
        if (_guidanceWindow is not null) return;

        _guidanceWindow = new GuidanceWindow();
        _guidanceWindow.PreviousRequested += OnPreviousRequested;
        _guidanceWindow.NextRequested += OnNextRequested;
        UpdateGuidanceNavigationState();
    }

    private void OnPreviousRequested()
    {
        if (_currentTestStepIndex <= 0) return;
        _currentTestStepIndex--;
        ShowCurrentTestStep();
    }

    private void OnNextRequested()
    {
        if (_currentTestStepIndex < 0 || _currentTestStepIndex >= _testSteps.Count - 1) return;

        // The pending-target navigation harness intentionally advances without
        // requiring validation. Validation capability remains implemented below
        // for later API-driven authored rules.
        _guidanceWindow?.SetValidationMessage(null);
        _currentTestStepIndex++;
        ShowCurrentTestStep();
    }

    private static bool TryReadElementValue(AutomationElement element, out string value)
    {
        value = string.Empty;

        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
                valuePatternObject is ValuePattern valuePattern)
            {
                value = valuePattern.Current.Value ?? string.Empty;
                return true;
            }

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
                textPatternObject is TextPattern textPattern)
            {
                value = textPattern.DocumentRange.GetText(-1).TrimEnd('\r', '\n');
                return true;
            }

            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private void ShowCurrentTestStep()
    {
        if (_currentTestStepIndex < 0 || _currentTestStepIndex >= _testSteps.Count) return;

        StopPendingTargetWait();
        _selectedIdentity = _testSteps[_currentTestStepIndex];
        _guidanceWindow?.SetValidationMessage(null);
        UpdateTrackedHighlight(showFoundStatus: false);
        UpdateGuidanceNavigationState();
        StatusText.Text = $"Showing test step {_currentTestStepIndex + 1} of {_testSteps.Count}.";
    }

    private void WaitForCurrentTestStepTarget()
    {
        StopElementTracking();
        CloseHighlight();

        if (_guidanceWindow is not null)
        {
            _guidanceWindow.PreviousRequested -= OnPreviousRequested;
            _guidanceWindow.NextRequested -= OnNextRequested;
            _guidanceWindow.Close();
            _guidanceWindow = null;
        }

        StopPendingTargetWait();
        _pendingTargetWatcher = new PendingWindowTargetWatcher(Dispatcher, TryResumePendingTarget);
        _pendingTargetWatcher.Start();
        StatusText.Text = $"Waiting for the application window for test step {_currentTestStepIndex + 1}.";
        DiagnosticLog.Write("PendingTarget.Waiting");
    }

    private void TryResumePendingTarget()
    {
        if (_selectedIdentity is null) return;

        DiagnosticLog.Write("PendingTarget.WindowOpened");
        var element = FindElement(_selectedIdentity);
        if (element is null) return;

        DiagnosticLog.Write("PendingTarget.Resolved");
        StopPendingTargetWait();
        ShowElement(element);
        StartElementTracking(element);
        UpdateGuidanceNavigationState();
        StatusText.Text = $"Showing test step {_currentTestStepIndex + 1} of {_testSteps.Count}.";
    }

    private void StopPendingTargetWait()
    {
        _pendingTargetWatcher?.Dispose();
        _pendingTargetWatcher = null;
    }

    private void UpdateGuidanceNavigationState()
    {
        _guidanceWindow?.SetNavigationState(
            _currentTestStepIndex > 0,
            _currentTestStepIndex >= 0 && _currentTestStepIndex < _testSteps.Count - 1);
    }

    private void OnTrackedElementBoundsChanged(Rect bounds)
    {
        _highlightWindow ??= new HighlightWindow();
        _highlightWindow.ShowAt(bounds);

        EnsureGuidanceWindow();
        _guidanceWindow!.ShowNear(bounds);
    }

    private void OnTrackedElementTemporarilyHidden()
    {
        CloseHighlight();

        if (_guidanceWindow is not null)
        {
            _guidanceWindow.PreviousRequested -= OnPreviousRequested;
            _guidanceWindow.NextRequested -= OnNextRequested;
            _guidanceWindow.Close();
            _guidanceWindow = null;
        }
    }

    private void OnTrackedHostActivated()
    {
        // RefreshBounds from the tracker recreates/repositions overlays after activation.
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
        _elementTracker.HostActivated -= OnTrackedHostActivated;
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
            _testSteps.Add(_selectedIdentity);
            _currentTestStepIndex = _testSteps.Count - 1;
            _hoveredElement = null;
            ShowElement(element);
            StopSelection($"Element selected as test step {_testSteps.Count}.");
            ShowCurrentTestStep();
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
            GetProcessName(processId),
            CreateAncestorIdentity(element));
    }

    private static AncestorIdentity? CreateAncestorIdentity(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? current;

        try
        {
            current = walker.GetParent(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        for (var depth = 0; current is not null && depth < 8; depth++)
        {
            try
            {
                var automationId = current.Current.AutomationId ?? string.Empty;
                var name = current.Current.Name ?? string.Empty;
                var controlType = current.Current.ControlType;

                if (controlType == ControlType.Window ||
                    !string.IsNullOrWhiteSpace(automationId) ||
                    !string.IsNullOrWhiteSpace(name))
                {
                    return new AncestorIdentity(name, automationId, controlType);
                }

                current = walker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        return null;
    }

    private static AutomationElement? FindElement(ElementIdentity identity)
    {
        DiagnosticLog.Write("FindElement.Begin");
        var root = AutomationElement.RootElement;
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var processIds = new HashSet<int>();

        foreach (var process in Process.GetProcessesByName(identity.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == currentSessionId)
                    {
                        processIds.Add(process.Id);
                    }
                }
                catch
                {
                    // The process may exit while its session is being inspected.
                }
            }
        }

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

        DiagnosticLog.Write("FindElement.RootFindAll.Begin");
        var candidates = root.FindAll(
            TreeScope.Descendants,
            new AndCondition(conditions.ToArray()));
        DiagnosticLog.Write("FindElement.RootFindAll.End");

        var sessionCandidates = new List<AutomationElement>();
        foreach (AutomationElement candidate in candidates)
        {
            try
            {
                if (processIds.Contains(candidate.Current.ProcessId))
                {
                    sessionCandidates.Add(candidate);
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        if (sessionCandidates.Count == 1) return sessionCandidates[0];
        if (sessionCandidates.Count == 0) return null;

        DiagnosticLog.Write($"FindElement.Ambiguous count={sessionCandidates.Count}");

        if (identity.Ancestor is null)
        {
            DiagnosticLog.Write("FindElement.AmbiguousNoAncestor");
            return null;
        }

        var ancestorMatches = sessionCandidates
            .Where(candidate => HasMatchingAncestor(candidate, identity.Ancestor))
            .ToList();

        if (ancestorMatches.Count == 1)
        {
            DiagnosticLog.Write("FindElement.ResolvedByAncestor");
            return ancestorMatches[0];
        }

        DiagnosticLog.Write($"FindElement.AmbiguousAfterAncestor count={ancestorMatches.Count}");
        return null;
    }

    private static bool HasMatchingAncestor(AutomationElement element, AncestorIdentity identity)
    {
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? current;

        try
        {
            current = walker.GetParent(element);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        for (var depth = 0; current is not null && depth < 8; depth++)
        {
            try
            {
                if (MatchesAncestor(current, identity)) return true;
                current = walker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool MatchesAncestor(AutomationElement element, AncestorIdentity identity)
    {
        if (element.Current.ControlType != identity.ControlType) return false;

        if (!string.IsNullOrWhiteSpace(identity.AutomationId) &&
            !string.Equals(element.Current.AutomationId, identity.AutomationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(identity.Name) &&
            !string.Equals(element.Current.Name, identity.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
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
        StopPendingTargetWait();
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
        string ProcessName,
        AncestorIdentity? Ancestor);

    private sealed record AncestorIdentity(
        string Name,
        string AutomationId,
        ControlType ControlType);
}
