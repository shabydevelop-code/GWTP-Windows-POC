using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

internal static class Program
{
    private const int TimeoutMs = 3000;
    private const int LaunchTimeoutMs = 10000;
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        var root = FindRepoRoot();
        var runtimeExe = Path.Combine(root, "bin", "Debug", "net8.0-windows", "GWTP-Windows-POC.exe");
        var hostExe = Path.Combine(root, "AmbiguityTestHost", "bin", "Debug", "net8.0-windows", "AmbiguityTestHost.exe");

        if (!File.Exists(runtimeExe) || !File.Exists(hostExe))
        {
            Console.Error.WriteLine("Build outputs are missing. Run run-sanity.ps1 so both GUI applications are built first.");
            return 2;
        }

        using var runtime = Process.Start(new ProcessStartInfo(runtimeExe) { UseShellExecute = true });
        if (runtime is null) return 2;

        try
        {
            var runtimeWindow = WaitForWindow(runtime.Id, "GWTP Windows POC");
            Run("Open UIA Test Host through GUI", () =>
            {
                Invoke(FindByName(runtimeWindow, "Open Ambiguity Test"));
                WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);
            });

            var hostWindow = WaitForTopLevelWindow("GWTP Windows UIA Test Host");
            ArrangeWindowsForPicker(runtimeWindow, hostWindow);

            Run("T01 picker selects duplicate target A and shows attached overlays", () =>
            {
                var targets = FindAllByAutomationId(hostWindow, "SharedContinue");
                Require(targets.Count == 2, "Expected two duplicate Continue targets.");
                SelectThroughRealPicker(runtimeWindow, targets[0]);
                WaitForRuntimeWindow(runtime.Id, "GWTP Guidance");
                WaitForRuntimeWindow(runtime.Id, "GWTP Highlight");
                AssertOverlayAttached(runtime.Id, targets[0]);
            });

            Run("T01 picker selects duplicate target B and Previous/Next rediscover correct ancestors", () =>
            {
                var targets = FindAllByAutomationId(hostWindow, "SharedContinue");
                SelectThroughRealPicker(runtimeWindow, targets[1]);
                var guidance = WaitForRuntimeWindow(runtime.Id, "GWTP Guidance");
                AssertOverlayAttached(runtime.Id, targets[1]);

                Invoke(FindByName(guidance, "Previous"));
                WaitUntil(() => IsOverlayAttached(runtime.Id, targets[0]), "Previous did not return to Group A target.");
                guidance = WaitForRuntimeWindow(runtime.Id, "GWTP Guidance");
                Invoke(FindByName(guidance, "Next"));
                WaitUntil(() => IsOverlayAttached(runtime.Id, targets[1]), "Next did not return to Group B target.");
            });

            Run("Tracking follows target when host window moves", () =>
            {
                var target = FindAllByAutomationId(hostWindow, "SharedContinue")[1];
                var before = WaitForRuntimeWindow(runtime.Id, "GWTP Guidance").Current.BoundingRectangle;
                MoveWindow(hostWindow, 70, 45);
                WaitUntil(() =>
                {
                    var after = TryFindRuntimeWindow(runtime.Id, "GWTP Guidance")?.Current.BoundingRectangle;
                    return after is { } rect && Math.Abs(rect.Left - before.Left) > 20 && IsOverlayAttached(runtime.Id, target);
                }, "Guidance did not follow the moved target.");
            });

            Run("Minimize hides overlays and Restore reattaches them", () =>
            {
                var target = FindAllByAutomationId(hostWindow, "SharedContinue")[1];
                var hwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);
                ShowWindow(hwnd, SwMinimize);
                WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null &&
                                TryFindRuntimeWindow(runtime.Id, "GWTP Highlight") is null,
                    "Overlays remained visible while host was minimized.");
                ShowWindow(hwnd, SwRestore);
                SetForegroundWindow(hwnd);
                WaitUntil(() => IsOverlayAttached(runtime.Id, target),
                    "Overlays did not reattach after Restore.");
            });

            Run("Foreground loss hides overlays and returning to host restores them", () =>
            {
                var target = FindAllByAutomationId(hostWindow, "SharedContinue")[1];
                var runtimeHwnd = new IntPtr(runtimeWindow.Current.NativeWindowHandle);
                SetForegroundWindow(runtimeHwnd);
                WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null,
                    "Guidance remained visible over unrelated foreground window.");
                var hostHwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);
                SetForegroundWindow(hostHwnd);
                WaitUntil(() => IsOverlayAttached(runtime.Id, target),
                    "Guidance did not return when host regained foreground.");
            });

            Run("T02 TextBox with AutomationId is exposed through UIA", () =>
            {
                var target = FindByAutomationId(hostWindow, "StableTextBox");
                Require(target.Current.ControlType == ControlType.Edit, "StableTextBox is not exposed as Edit.");
            });

            Run("T03 TextBox without authored AutomationId remains selectable in GUI", () =>
            {
                var edits = hostWindow.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

                var target = edits.Cast<AutomationElement>()
                    .SingleOrDefault(element =>
                        string.IsNullOrEmpty(element.Current.AutomationId) &&
                        element.Current.BoundingRectangle.Top > 0);

                Require(target is not null, "No TextBox without AutomationId was exposed through UIA.");
                Require(target.Current.ControlType == ControlType.Edit, "No-id textbox is not exposed as Edit.");
            });

            Run("T04 Button without authored AutomationId remains selectable in GUI", () =>
            {
                var target = FindByName(hostWindow, "No AutomationId Button");
                Require(target.Current.ControlType == ControlType.Button, "No-id button is not exposed as Button.");
            });

            Run("T05 Dynamic Name changes through GUI", () =>
            {
                Invoke(FindByName(hostWindow, "Change target name"));
                WaitUntil(() => TryFindByName(hostWindow, "Dynamic target 2") is not null,
                    "Dynamic target name did not change.");
                Require(FindByAutomationId(hostWindow, "DynamicNameTarget").Current.Name == "Dynamic target 2",
                    "Stable AutomationId no longer resolves the renamed target.");
            });

            Run("T06 Deep hierarchy target is exposed through UIA", () =>
            {
                var target = FindByAutomationId(hostWindow, "DeepTarget");
                Require(target.Current.Name == "Deep Target", "Deep target could not be resolved.");
            });

            Run("T07 Dynamic target disappears and returns through GUI", () =>
            {
                Invoke(FindByName(hostWindow, "Toggle dynamic target"));
                WaitUntil(() => TryFindByAutomationId(hostWindow, "AppearingTarget") is null,
                    "Dynamic target did not disappear.");
                Invoke(FindByName(hostWindow, "Toggle dynamic target"));
                WaitUntil(() => TryFindByAutomationId(hostWindow, "AppearingTarget") is not null,
                    "Dynamic target did not return.");
            });

            Run("T08 Second top-level window opens in same process through GUI", () =>
            {
                Invoke(FindByName(hostWindow, "Open second test window"));
                var second = WaitForTopLevelWindow("GWTP UIA Test Host — Second Window");
                Require(FindByAutomationId(second, "SameProcessTarget").Current.ProcessId == hostWindow.Current.ProcessId,
                    "Second window is not owned by the same process.");
            });

            Run("Closing tracked host removes overlays and runtime stays responsive", () =>
            {
                var hostHwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);
                SendMessage(hostHwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                WaitUntil(() => TryFindTopLevelWindow("GWTP Windows UIA Test Host") is null,
                    "Tracked host did not close.");
                WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null &&
                                TryFindRuntimeWindow(runtime.Id, "GWTP Highlight") is null,
                    "Overlays remained after tracked host closed.");
                Invoke(FindByName(runtimeWindow, "Open Ambiguity Test"));
                WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);
            });
        }
        finally
        {
            TryCloseProcessByName("AmbiguityTestHost");
            if (!runtime.HasExited) runtime.Kill(true);
        }

        Console.WriteLine();
        Console.WriteLine($"Windows GUI sanity: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }


    private static List<AutomationElement> FindAllByAutomationId(AutomationElement root, string id)
        => root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            .Cast<AutomationElement>().ToList();

    private static void SelectThroughRealPicker(AutomationElement runtimeWindow, AutomationElement target)
    {
        Invoke(FindByAutomationId(runtimeWindow, "SelectElementButton"));
        WaitUntil(() => FindByAutomationId(runtimeWindow, "SelectElementButton").Current.Name == "Cancel",
            "Picker did not enter selection mode.");

        var hostHwnd = GetAncestorWindowFromElement(target);
        Require(hostHwnd != IntPtr.Zero, "Could not resolve target host window.");
        // Do not fight Windows foreground-lock. Instead make the target host
        // physically topmost for the click and prove that UIA FromPoint resolves
        // the intended authored element before clicking it.
        SetWindowPos(hostHwnd, HwndTop, 0, 0, 0, 0,
            SwpNomove | SwpNosize | SwpNoactivate);
        // Hide the runtime authoring window during the physical pick. This mirrors
        // the intended picker UX and prevents GWTP itself from obscuring the target.
        var runtimeHwnd = new IntPtr(runtimeWindow.Current.NativeWindowHandle);
        ShowWindow(runtimeHwnd, SwHide);

        var rect = target.Current.BoundingRectangle;
        var x = (int)Math.Round(rect.Left + rect.Width / 2);
        var y = (int)Math.Round(rect.Top + rect.Height / 2);
        Require(SetCursorPos(x, y), "Could not move cursor to target.");
        WaitUntil(() => IsCursorInside(rect), "Cursor did not reach target bounds.");
        WaitUntil(() =>
        {
            var atPoint = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (atPoint is null || atPoint.Current.ProcessId != target.Current.ProcessId)
            {
                return false;
            }

            // UIA FromPoint may legitimately return a child presentation element
            // inside the authored control. Accept the point when that element is
            // the target itself or descends from it.
            var current = atPoint;
            while (current is not null)
            {
                if (Automation.Compare(current, target))
                {
                    return true;
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }

            return false;
        }, "Intended picker target is obscured at click point.");
        mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);

        // The production picker samples the global mouse state every 40 ms.
        // Keep the button down only until the picker itself reports completion;
        // do not use a fixed transition delay.
        WaitUntil(() =>
        {
            var button = FindByAutomationId(runtimeWindow, "SelectElementButton");
            return button.Current.Name == "Select Element" && button.Current.IsEnabled;
        }, "Picker did not complete selection.");

        mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        ShowWindow(runtimeHwnd, SwShowNoActivate);

        // Do not let a failed/late synthetic click leak selection mode into the next test.
        mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
    }

    private static AutomationElement WaitForRuntimeWindow(int processId, string name)
    {
        AutomationElement? found = null;
        WaitUntil(() => (found = TryFindRuntimeWindow(processId, name)) is not null,
            $"Runtime window '{name}' did not appear.");
        return found!;
    }

    private static AutomationElement? TryFindRuntimeWindow(int processId, string name)
        => AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(AutomationElement.NameProperty, name)));

    private static AutomationElement? TryFindTopLevelWindow(string name)
        => AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new PropertyCondition(AutomationElement.NameProperty, name));

    private static bool IsOverlayAttached(int runtimeProcessId, AutomationElement target)
    {
        try
        {
            var highlight = TryFindRuntimeWindow(runtimeProcessId, "GWTP Highlight");
            var guidance = TryFindRuntimeWindow(runtimeProcessId, "GWTP Guidance");
            if (highlight is null || guidance is null) return false;

            var targetRect = target.Current.BoundingRectangle;
            var highlightRect = highlight.Current.BoundingRectangle;
            var guidanceRect = guidance.Current.BoundingRectangle;

            var highlightMatches = Math.Abs(highlightRect.Left - targetRect.Left) <= 8 &&
                                   Math.Abs(highlightRect.Top - targetRect.Top) <= 8 &&
                                   Math.Abs(highlightRect.Width - targetRect.Width) <= 12 &&
                                   Math.Abs(highlightRect.Height - targetRect.Height) <= 12;

            var horizontalGap = Math.Max(0, Math.Max(targetRect.Left - guidanceRect.Right, guidanceRect.Left - targetRect.Right));
            var verticalGap = Math.Max(0, Math.Max(targetRect.Top - guidanceRect.Bottom, guidanceRect.Top - targetRect.Bottom));
            return highlightMatches && horizontalGap <= 40 && verticalGap <= 40;
        }
        catch (ElementNotAvailableException) { return false; }
    }

    private static void AssertOverlayAttached(int runtimeProcessId, AutomationElement target)
        => Require(IsOverlayAttached(runtimeProcessId, target), "Highlight/guidance are not attached to the selected target.");

    private static void ArrangeWindowsForPicker(AutomationElement runtimeWindow, AutomationElement hostWindow)
    {
        var work = Forms.Screen.PrimaryScreen.WorkingArea;
        var runtime = runtimeWindow.Current.BoundingRectangle;
        var host = hostWindow.Current.BoundingRectangle;

        var runtimeHwnd = new IntPtr(runtimeWindow.Current.NativeWindowHandle);
        var hostHwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);

        // Put the runtime and authored target host in separate visible regions.
        // A real picker click cannot select a control that is physically covered by GWTP.
        var runtimeX = work.Left + 20;
        var runtimeY = work.Top + 20;
        Require(SetWindowPos(runtimeHwnd, IntPtr.Zero, runtimeX, runtimeY,
            (int)runtime.Width, (int)runtime.Height, SwpNozorder | SwpNoactivate),
            "Could not position runtime window.");

        var hostX = Math.Min(
            work.Right - (int)host.Width - 20,
            runtimeX + (int)runtime.Width + 40);
        var hostY = work.Top + 20;

        // If the screen is too narrow for side-by-side placement, put the host below.
        if (hostX < runtimeX + runtime.Width)
        {
            hostX = work.Left + 20;
            hostY = Math.Min(
                work.Bottom - (int)host.Height - 20,
                runtimeY + (int)runtime.Height + 40);
        }

        Require(SetWindowPos(hostHwnd, IntPtr.Zero, hostX, hostY,
            (int)host.Width, (int)host.Height, SwpNozorder | SwpNoactivate),
            "Could not position target host window.");
    }

    private static bool IsCursorInside(System.Windows.Rect rect)
    {
        if (!GetCursorPos(out var point)) return false;
        return point.X >= rect.Left && point.X <= rect.Right &&
               point.Y >= rect.Top && point.Y <= rect.Bottom;
    }

    private static IntPtr GetAncestorWindowFromElement(AutomationElement element)
    {
        try
        {
            var current = element;
            while (current is not null)
            {
                var hwnd = new IntPtr(current.Current.NativeWindowHandle);
                if (hwnd != IntPtr.Zero) return GetAncestor(hwnd, GaRoot);
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
        }
        catch (ElementNotAvailableException) { }
        return IntPtr.Zero;
    }

    private static void MoveWindow(AutomationElement window, int dx, int dy)
    {
        var hwnd = new IntPtr(window.Current.NativeWindowHandle);
        var rect = window.Current.BoundingRectangle;
        Require(SetWindowPos(hwnd, IntPtr.Zero, (int)rect.Left + dx, (int)rect.Top + dy,
            (int)rect.Width, (int)rect.Height, SwpNozorder | SwpNoactivate), "Could not move host window.");
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            WriteResult("PASS", name, ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            _failed++;
            WriteResult("FAIL", $"{name} — {ex.Message}", ConsoleColor.Red);
        }
    }

    private static void WriteResult(string status, string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(status);
        Console.ForegroundColor = previous;
        Console.WriteLine($"  {message}");
    }

    private static AutomationElement WaitForWindow(int processId, string name)
    {
        AutomationElement? found = null;
        WaitUntil(() =>
        {
            found = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                    new PropertyCondition(AutomationElement.NameProperty, name)));
            return found is not null;
        }, $"Window '{name}' did not appear.");
        return found!;
    }

    private static AutomationElement WaitForTopLevelWindow(string name, int timeoutMs = TimeoutMs)
    {
        AutomationElement? found = null;
        WaitUntil(() =>
        {
            found = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.NameProperty, name));
            return found is not null;
        }, $"Window '{name}' did not appear.", timeoutMs);
        return found!;
    }

    private static AutomationElement FindByName(AutomationElement root, string name)
        => TryFindByName(root, name) ?? throw new InvalidOperationException($"GUI element '{name}' not found.");

    private static AutomationElement? TryFindByName(AutomationElement root, string name)
        => root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));

    private static AutomationElement FindByAutomationId(AutomationElement root, string id)
        => TryFindByAutomationId(root, id) ?? throw new InvalidOperationException($"GUI element '{id}' not found.");

    private static AutomationElement? TryFindByAutomationId(AutomationElement root, string id)
        => root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    private static void Invoke(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) || pattern is not InvokePattern invoke)
            throw new InvalidOperationException($"'{element.Current.Name}' does not expose InvokePattern.");
        invoke.Invoke();
    }

    private static void WaitUntil(Func<bool> condition, string error, int timeoutMs = TimeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try { if (condition()) return; } catch (ElementNotAvailableException) { }
            Thread.Sleep(10);
        }
        throw new TimeoutException(error);
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GWTP-Windows-POC.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }


    private const int VkLbutton = 0x01;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;
    private const uint WmClose = 0x0010;
    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private static void TryCloseProcessByName(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try { process.Kill(true); } catch { }
            }
        }
    }
}
