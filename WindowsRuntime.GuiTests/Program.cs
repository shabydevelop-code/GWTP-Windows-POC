using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

internal static class Program
{
    private const int TimeoutMs = 3000;
    private const int LaunchTimeoutMs = 10000;
    private const int HumanStepPauseMs = 100;
    private const int VisualPickerPauseMs = 200;
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        var selectedTests = ParseSelectedTests(args);
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
            if (selectedTests is not null && selectedTests.SetEquals(new[] { 18 }))
            {
                var focusedRuntimeWindow = WaitForWindow(runtime.Id, "GWTP Windows POC");
                Invoke(FindByName(focusedRuntimeWindow, "Open Ambiguity Test"));
                var focusedHostWindow = WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);

                Invoke(FindByName(focusedHostWindow, "Open second test window"));
                var focusedSecondWindow = WaitForTopLevelWindow("GWTP UIA Test Host — Second Window", LaunchTimeoutMs);
                var focusedSecondDuplicate = FindByAutomationId(focusedSecondWindow, "SharedContinue");
                Require(focusedSecondDuplicate.Current.ProcessId == focusedHostWindow.Current.ProcessId,
                    "Focused T08 setup did not create the duplicate in the same process.");

                var focusedSecondHwnd = new IntPtr(focusedSecondWindow.Current.NativeWindowHandle);
                ShowWindow(focusedSecondHwnd, SwMinimize);
                WaitUntil(() =>
                {
                    var pattern = (WindowPattern)focusedSecondWindow.GetCurrentPattern(WindowPattern.Pattern);
                    return pattern.Current.WindowVisualState == WindowVisualState.Minimized;
                }, "Focused T08 setup could not minimize the second window.");

                PrepareHostTargetForPicker(focusedRuntimeWindow, focusedHostWindow, automationId: "GroupA");
                var focusedAuthored = FindTargetWithinGroup(focusedHostWindow, "GroupA", "SharedContinue");
                SelectThroughRealPicker(focusedRuntimeWindow, focusedAuthored);
                WaitUntil(() => IsOverlayAttached(runtime.Id, focusedAuthored),
                    "Focused T08 setup could not author the main-window duplicate.");

                Run("18. T08 duplicate leaf in second same-process window does not steal authored target", () =>
                {
                    var hostHwnd = new IntPtr(focusedHostWindow.Current.NativeWindowHandle);
                    ShowWindow(focusedSecondHwnd, SwRestore);
                    SetForegroundWindow(hostHwnd);
                    WaitUntil(() => GetForegroundWindow() == hostHwnd,
                        "Authored host did not regain foreground before focused T08 rediscovery.");

                    Invoke(FindByAutomationId(focusedRuntimeWindow, "FindElementButton"));
                    SetForegroundWindow(hostHwnd);
                    WaitUntil(() => IsOverlayAttached(runtime.Id, focusedAuthored),
                        "Same-process second-window duplicate stole the authored target.");
                });

                Console.WriteLine();
                Console.WriteLine($"Windows GUI sanity: {_passed} passed, {_failed} failed");
                return _failed == 0 ? 0 : 1;
            }

            if (selectedTests is not null && selectedTests.SetEquals(new[] { 20 }))
            {
                var focusedRuntimeWindow = WaitForWindow(runtime.Id, "GWTP Windows POC");
                Invoke(FindByName(focusedRuntimeWindow, "Open Ambiguity Test"));
                var authoredHost = WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);
                PrepareHostTargetForPicker(focusedRuntimeWindow, authoredHost, automationId: "GroupA");
                var authoredTarget = FindTargetWithinGroup(authoredHost, "GroupA", "SharedContinue");
                SelectThroughRealPicker(focusedRuntimeWindow, authoredTarget);
                WaitUntil(() => IsOverlayAttached(runtime.Id, authoredTarget),
                    "Focused setup could not author the cross-launch target.");

                var authoredHwnd = new IntPtr(authoredHost.Current.NativeWindowHandle);
                SendMessage(authoredHwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                WaitUntil(() => TryFindTopLevelWindow("GWTP Windows UIA Test Host") is null,
                    "Focused setup host did not close.");
                Invoke(FindByName(focusedRuntimeWindow, "Open Ambiguity Test"));
                WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);

                Run("20. Cross-launch rediscovery finds authored target in a new process instance", () =>
                {
                    WaitUntil(() => Process.GetProcessesByName("AmbiguityTestHost").Length == 1,
                        "Previous test-host process did not exit before cross-launch rediscovery.",
                        LaunchTimeoutMs);
                    var reopenedHost = WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);
                    var scenarioScroll = TryFindByAutomationId(reopenedHost, "ScenarioScrollViewer");
                    if (scenarioScroll is not null &&
                        scenarioScroll.TryGetCurrentPattern(ScrollPattern.Pattern, out var reopenedScrollObject) &&
                        reopenedScrollObject is ScrollPattern reopenedScroll &&
                        reopenedScroll.Current.VerticallyScrollable)
                    {
                        reopenedScroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
                    }

                    var reopenedTarget = FindTargetWithinGroup(reopenedHost, "GroupA", "SharedContinue");
                    var reopenedHwnd = new IntPtr(reopenedHost.Current.NativeWindowHandle);
                    SetForegroundWindow(reopenedHwnd);
                    Invoke(FindByAutomationId(focusedRuntimeWindow, "FindElementButton"));
                    SetForegroundWindow(reopenedHwnd);
                    WaitUntil(() => IsOverlayAttached(runtime.Id, reopenedTarget),
                        "Authored target was not rediscovered after the target application restarted.");
                });

                Console.WriteLine();
                Console.WriteLine($"Windows GUI sanity: {_passed} passed, {_failed} failed");
                return _failed == 0 ? 0 : 1;
            }

            var runtimeWindow = WaitForWindow(runtime.Id, "GWTP Windows POC");
            Run("01. Open UIA Test Host through GUI", () =>
            {
                Invoke(FindByName(runtimeWindow, "Open Ambiguity Test"));
                WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);
            });

            var hostWindow = WaitForTopLevelWindow("GWTP Windows UIA Test Host");
            ArrangeWindowsForPicker(runtimeWindow, hostWindow);

            Run("02. T01 picker selects duplicate target A and shows attached overlays", () =>
            {
                var targets = FindAllByAutomationId(hostWindow, "SharedContinue");
                Require(targets.Count == 2, "Expected two duplicate Continue targets.");
                SelectThroughRealPicker(runtimeWindow, targets[0]);
                WaitForRuntimeWindow(runtime.Id, "GWTP Guidance");
                WaitForRuntimeWindow(runtime.Id, "GWTP Highlight");
                AssertOverlayAttached(runtime.Id, targets[0]);
            });

            Run("03. T01 picker selects duplicate target B and Previous/Next rediscover correct ancestors", () =>
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

            Run("04. Tracking follows target when host window moves", () =>
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

            Run("05. Minimize hides overlays and Restore reattaches them", () =>
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

            Run("06. Stress Previous/Next with repeated Minimize/Restore stays attached", () =>
            {
                var targets = FindAllByAutomationId(hostWindow, "SharedContinue");
                Require(targets.Count == 2, "Expected two SharedContinue targets for stress test.");
                var hwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);

                for (var cycle = 0; cycle < 10; cycle++)
                {
                    var guidance = WaitForRuntimeWindow(runtime.Id, "GWTP Guidance");
                    Invoke(FindByName(guidance, "Previous"));
                    WaitUntil(() => IsOverlayAttached(runtime.Id, targets[0]),
                        $"Stress cycle {cycle + 1}: Previous did not attach to Group A.");

                    guidance = WaitForRuntimeWindow(runtime.Id, "GWTP Guidance");
                    Invoke(FindByName(guidance, "Next"));
                    WaitUntil(() => IsOverlayAttached(runtime.Id, targets[1]),
                        $"Stress cycle {cycle + 1}: Next did not attach to Group B.");

                    ShowWindow(hwnd, SwMinimize);
                    WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null &&
                                    TryFindRuntimeWindow(runtime.Id, "GWTP Highlight") is null,
                        $"Stress cycle {cycle + 1}: overlays remained visible while minimized.");

                    ShowWindow(hwnd, SwRestore);
                    SetForegroundWindow(hwnd);
                    WaitUntil(() => IsOverlayAttached(runtime.Id, targets[1]),
                        $"Stress cycle {cycle + 1}: overlays did not reattach after Restore.");
                }
            });

            Run("07. Foreground loss hides overlays and returning to host restores them", () =>
            {
                var target = FindAllByAutomationId(hostWindow, "SharedContinue")[1];
                var runtimeHwnd = new IntPtr(runtimeWindow.Current.NativeWindowHandle);
                Require(SetForegroundWindow(runtimeHwnd), "Could not request unrelated foreground window.");
                WaitUntil(() => GetForegroundWindow() == runtimeHwnd,
                    "Unrelated window did not actually become foreground.");
                WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null,
                    "Guidance remained visible over unrelated foreground window.");
                var hostHwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);
                Require(SetForegroundWindow(hostHwnd), "Could not request host foreground window.");
                WaitUntil(() => GetForegroundWindow() == hostHwnd,
                    "Host window did not actually regain foreground.");
                WaitUntil(() => IsOverlayAttached(runtime.Id, target),
                    "Guidance did not return when host regained foreground.");
            });

            Run("08. T02 TextBox with AutomationId is exposed through UIA", () =>
            {
                var target = FindByAutomationId(hostWindow, "StableTextBox");
                Require(target.Current.ControlType == ControlType.Edit, "StableTextBox is not exposed as Edit.");
            });

            Run("09. T03 TextBox without authored AutomationId remains selectable in GUI", () =>
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

            Run("10. T04 Button without authored AutomationId remains selectable in GUI", () =>
            {
                var target = FindByName(hostWindow, "No AutomationId Button");
                Require(target.Current.ControlType == ControlType.Button, "No-id button is not exposed as Button.");
            });

            Run("11. T05 Dynamic Name changes through GUI", () =>
            {
                Invoke(FindByName(hostWindow, "Change target name"));
                WaitUntil(() => TryFindByName(hostWindow, "Dynamic target 2") is not null,
                    "Dynamic target name did not change.");
                Require(FindByAutomationId(hostWindow, "DynamicNameTarget").Current.Name == "Dynamic target 2",
                    "Stable AutomationId no longer resolves the renamed target.");
            });

            Run("12. T06 Deep hierarchy target is exposed through UIA", () =>
            {
                var target = FindByAutomationId(hostWindow, "DeepTarget");
                Require(target.Current.Name == "Deep Target", "Deep target could not be resolved.");
            });

            Run("13. T07 Dynamic target disappears and returns through GUI", () =>
            {
                Invoke(FindByName(hostWindow, "Toggle dynamic target"));
                WaitUntil(() => TryFindByAutomationId(hostWindow, "AppearingTarget") is null,
                    "Dynamic target did not disappear.");
                Invoke(FindByName(hostWindow, "Toggle dynamic target"));
                WaitUntil(() => TryFindByAutomationId(hostWindow, "AppearingTarget") is not null,
                    "Dynamic target did not return.");
            });

            Run("14. T08 Second top-level window opens in same process through GUI", () =>
            {
                Invoke(FindByName(hostWindow, "Open second test window"));
                var second = WaitForTopLevelWindow("GWTP UIA Test Host — Second Window");
                Require(FindByAutomationId(second, "SameProcessTarget").Current.ProcessId == hostWindow.Current.ProcessId,
                    "Second window is not owned by the same process.");
            });

            Run("15. T03 no-AutomationId target survives picker rediscovery", () =>
            {
                PrepareHostTargetForPicker(runtimeWindow, hostWindow, "No AutomationId Button");
                var target = FindByName(hostWindow, "No AutomationId Button");
                SelectThroughRealPicker(runtimeWindow, target);
                Require(IsOverlayAttached(runtime.Id, target),
                    "No-AutomationId target did not remain attached after picker rediscovery.");
            });

            Run("16. T05 dynamic Name exposes current descriptor limitation after authored selection", () =>
            {
                PrepareHostTargetForPicker(runtimeWindow, hostWindow, automationId: "DynamicNameTarget");
                var target = FindByAutomationId(hostWindow, "DynamicNameTarget");
                var authoredName = target.Current.Name;
                SelectThroughRealPicker(runtimeWindow, target);
                Invoke(FindByName(hostWindow, "Change target name"));
                WaitUntil(() => FindByAutomationId(hostWindow, "DynamicNameTarget").Current.Name != authoredName,
                    "Dynamic target name did not change after authored selection.");
                Invoke(FindByAutomationId(runtimeWindow, "FindElementButton"));
                WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null,
                    "Current descriptor unexpectedly rediscovered a target whose persisted Name changed.");
            });

            Run("17. T07 active dynamic target hides when removed and returns event-driven", () =>
            {
                PrepareHostTargetForPicker(runtimeWindow, hostWindow, automationId: "AppearingTarget");
                var target = FindByAutomationId(hostWindow, "AppearingTarget");
                SelectThroughRealPicker(runtimeWindow, target);
                Invoke(FindByName(hostWindow, "Toggle dynamic target"));
                WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null &&
                                TryFindRuntimeWindow(runtime.Id, "GWTP Highlight") is null,
                    "Active dynamic target overlays remained after target disappeared.");
                Invoke(FindByName(hostWindow, "Toggle dynamic target"));
                WaitUntil(() => TryFindByAutomationId(hostWindow, "AppearingTarget") is not null,
                    "Dynamic target did not return to the host.");
            });

            Run("18. T08 duplicate leaf in second same-process window does not steal authored target", () =>
            {
                var second = WaitForTopLevelWindow("GWTP UIA Test Host — Second Window");
                var secondDuplicate = FindByAutomationId(second, "SharedContinue");
                Require(secondDuplicate.Current.ProcessId == hostWindow.Current.ProcessId,
                    "Second-window duplicate is not in the same process.");

                var secondHwnd = new IntPtr(second.Current.NativeWindowHandle);
                ShowWindow(secondHwnd, SwMinimize);
                WaitUntil(() =>
                {
                    var pattern = (WindowPattern)second.GetCurrentPattern(WindowPattern.Pattern);
                    return pattern.Current.WindowVisualState == WindowVisualState.Minimized;
                }, "Second test window did not minimize before authored target selection.");

                // Earlier lifecycle tests may leave the host scrolled, minimized/restored
                // or moved. Normalize it through the same public-UIA picker preparation
                // used by the other authored-target cases.
                PrepareHostTargetForPicker(runtimeWindow, hostWindow, automationId: "GroupA");
                var hostHwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);

                AutomationElement? authored = null;
                WaitUntil(() =>
                {
                    // Query the main host by its distinguishing Group A ancestor.
                    // A process-wide AutomationId lookup is intentionally ambiguous now
                    // because T08 adds another SharedContinue in the second window.
                    var scenarioScroll = TryFindByAutomationId(hostWindow, "ScenarioScrollViewer");
                    if (scenarioScroll is null ||
                        !scenarioScroll.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrollPatternObject) ||
                        scrollPatternObject is not ScrollPattern scenarioScrollPattern)
                    {
                        return false;
                    }

                    if (scenarioScrollPattern.Current.VerticallyScrollable)
                    {
                        scenarioScrollPattern.SetScrollPercent(
                            ScrollPattern.NoScroll,
                            0);
                    }

                    var groupA = TryFindByAutomationId(hostWindow, "GroupA");
                    if (groupA is null) return false;

                    // GroupBox is not guaranteed to be the Button's parent in the
                    // UIA Control View. Resolve the duplicate leafs from the host,
                    // then identify the authored one by its screen geometry relative
                    // to Group A. Bring it into view before the physical picker.
                    // The host's ScrollViewer can clip T01 completely after later
                    // scenarios have scrolled down. Scroll the known Group A container
                    // itself into view first; a clipped child can report Empty bounds
                    // and therefore cannot be identified geometrically yet.
                    if (groupA.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var groupScrollPattern) &&
                        groupScrollPattern is ScrollItemPattern groupScrollItem)
                    {
                        groupScrollItem.ScrollIntoView();
                    }

                    var groupRect = groupA.Current.BoundingRectangle;
                    authored = FindAllByAutomationId(hostWindow, "SharedContinue")
                        .FirstOrDefault(candidate =>
                        {
                            var rect = candidate.Current.BoundingRectangle;
                            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return false;
                            var centerX = rect.Left + rect.Width / 2;
                            return centerX >= groupRect.Left && centerX <= groupRect.Right;
                        });
                    if (authored is null) return false;

                    if (authored.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scrollPattern) &&
                        scrollPattern is ScrollItemPattern scrollItem)
                    {
                        scrollItem.ScrollIntoView();
                    }

                    var rect = authored.Current.BoundingRectangle;
                    if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return false;
                    var center = new System.Drawing.Point(
                        (int)Math.Round(rect.Left + rect.Width / 2),
                        (int)Math.Round(rect.Top + rect.Height / 2));
                    return Forms.Screen.AllScreens.Any(screen => screen.Bounds.Contains(center));
                }, BuildPickerGeometryDiagnostic(hostWindow, authored));
                SelectThroughRealPicker(runtimeWindow, authored!);
                ShowWindow(secondHwnd, SwRestore);

                // Restoring the competing window can make it foreground. The runtime
                // correctly hides guidance whenever the authored host is not foreground,
                // so explicitly return foreground ownership to the authored host before
                // asserting target rediscovery. The competing window remains restored
                // and present in the same process, which preserves the T08 ambiguity.
                SetForegroundWindow(hostHwnd);
                WaitUntil(() => GetForegroundWindow() == hostHwnd,
                    "Authored host did not regain foreground before T08 rediscovery.");

                Invoke(FindByAutomationId(runtimeWindow, "FindElementButton"));
                SetForegroundWindow(hostHwnd);
                WaitUntil(() => IsOverlayAttached(runtime.Id, authored),
                    "Same-process second-window duplicate stole the authored target.");
            });

            Run("19. Closing tracked host removes overlays and runtime stays responsive", () =>
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

            Run("20. Cross-launch rediscovery finds authored target in a new process instance", () =>
            {
                // Test 19 closes the authored host and opens a replacement. Ensure the
                // old process is fully gone before exercising cross-launch identity;
                // otherwise Process.GetProcessesByName can temporarily expose both
                // generations and correctly make discovery ambiguous.
                WaitUntil(() => Process.GetProcessesByName("AmbiguityTestHost").Length == 1,
                    "Previous test-host process did not exit before cross-launch rediscovery.",
                    LaunchTimeoutMs);
                var reopenedHost = WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);
                // The replacement host is a fresh process and starts at its default
                // scroll position. Bring the authored T01 target into the UIA viewport
                // before asserting attachment; rediscovery identity must not depend on
                // the old process instance, while overlay visibility still requires
                // the target to have usable on-screen bounds.
                var scenarioScroll = TryFindByAutomationId(reopenedHost, "ScenarioScrollViewer");
                if (scenarioScroll is not null &&
                    scenarioScroll.TryGetCurrentPattern(ScrollPattern.Pattern, out var reopenedScrollObject) &&
                    reopenedScrollObject is ScrollPattern reopenedScroll &&
                    reopenedScroll.Current.VerticallyScrollable)
                {
                    reopenedScroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
                }
                var reopenedTarget = FindTargetWithinGroup(reopenedHost, "GroupA", "SharedContinue");
                var reopenedHwnd = new IntPtr(reopenedHost.Current.NativeWindowHandle);
                SetForegroundWindow(reopenedHwnd);
                Invoke(FindByAutomationId(runtimeWindow, "FindElementButton"));
                SetForegroundWindow(reopenedHwnd);
                WaitUntil(() => IsOverlayAttached(runtime.Id, reopenedTarget),
                    "Authored target was not rediscovered after the target application restarted.");
            });

            Run("21. Two identical app instances fail safely instead of choosing an arbitrary target", () =>
            {
                var firstHost = WaitForTopLevelWindow("GWTP Windows UIA Test Host", LaunchTimeoutMs);
                using var secondInstance = Process.Start(new ProcessStartInfo(hostExe) { UseShellExecute = true });
                Require(secondInstance is not null, "Could not start second identical test-host instance.");
                WaitForWindow(secondInstance!.Id, "GWTP Windows UIA Test Host");

                Invoke(FindByAutomationId(runtimeWindow, "FindElementButton"));
                WaitUntil(() => TryFindRuntimeWindow(runtime.Id, "GWTP Guidance") is null &&
                                TryFindRuntimeWindow(runtime.Id, "GWTP Highlight") is null,
                    "Runtime chose an arbitrary target while two indistinguishable app instances were available.");

                secondInstance.Kill(true);
                secondInstance.WaitForExit(LaunchTimeoutMs);
                var firstHwnd = new IntPtr(firstHost.Current.NativeWindowHandle);
                SetForegroundWindow(firstHwnd);
                Invoke(FindByAutomationId(runtimeWindow, "FindElementButton"));
                SetForegroundWindow(firstHwnd);
                var firstTarget = FindTargetWithinGroup(firstHost, "GroupA", "SharedContinue");
                WaitUntil(() => IsOverlayAttached(runtime.Id, firstTarget),
                    "Runtime did not recover after the ambiguous second instance closed.");
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
        Thread.Sleep(VisualPickerPauseMs);

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
        Thread.Sleep(VisualPickerPauseMs);
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
        Thread.Sleep(VisualPickerPauseMs);

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

    private static string BuildPickerGeometryDiagnostic(
        AutomationElement hostWindow,
        AutomationElement? target)
    {
        string RectText(System.Windows.Rect rect)
            => $"L={rect.Left:F1},T={rect.Top:F1},R={rect.Right:F1},B={rect.Bottom:F1},W={rect.Width:F1},H={rect.Height:F1}";

        var hostRect = hostWindow.Current.BoundingRectangle;
        var targetText = target is null
            ? "<not found>"
            : RectText(target.Current.BoundingRectangle);
        var groupAText = TryFindByAutomationId(hostWindow, "GroupA") is { } groupA
            ? RectText(groupA.Current.BoundingRectangle)
            : "<not found>";
        var sharedCount = FindAllByAutomationId(hostWindow, "SharedContinue").Count;
        var visualState = "<unavailable>";
        try
        {
            var pattern = (WindowPattern)hostWindow.GetCurrentPattern(WindowPattern.Pattern);
            visualState = pattern.Current.WindowVisualState.ToString();
        }
        catch { }

        var screens = string.Join(" | ", Forms.Screen.AllScreens.Select((screen, index) =>
            $"Screen{index}[Bounds={screen.Bounds}; WorkingArea={screen.WorkingArea}; Primary={screen.Primary}]"));

        var cursor = GetCursorPos(out var point)
            ? $"X={point.X},Y={point.Y}"
            : "<unavailable>";

        return $"Authored target did not reach a physically accessible picker position. " +
               $"Host={RectText(hostRect)}; HostState={visualState}; GroupA={groupAText}; SharedContinueCount={sharedCount}; Target={targetText}; " +
               $"Cursor={cursor}; Screens={screens}";
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

    private static HashSet<int>? ParseSelectedTests(string[] args)
    {
        if (args.Length == 0) return null;

        var selected = new HashSet<int>();
        foreach (var arg in args)
        {
            foreach (var part in arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out var number) || number < 1 || number > 21)
                {
                    throw new ArgumentException($"Invalid test number '{part}'. Expected 1-21.");
                }
                selected.Add(number);
            }
        }
        return selected;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Thread.Sleep(HumanStepPauseMs);
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

    private static void PrepareHostTargetForPicker(
        AutomationElement runtimeWindow,
        AutomationElement hostWindow,
        string? name = null,
        string? automationId = null)
    {
        var hostHwnd = new IntPtr(hostWindow.Current.NativeWindowHandle);
        ShowWindow(hostHwnd, SwRestore);
        ArrangeWindowsForPicker(runtimeWindow, hostWindow);

        var scenarioScroll = TryFindByAutomationId(hostWindow, "ScenarioScrollViewer");
        if (scenarioScroll is not null &&
            scenarioScroll.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrollObject) &&
            scrollObject is ScrollPattern scroll &&
            scroll.Current.VerticallyScrollable)
        {
            AutomationElement? target = automationId is not null
                ? TryFindByAutomationId(hostWindow, automationId)
                : name is not null ? TryFindByName(hostWindow, name) : null;

            if (target is not null &&
                target.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var itemObject) &&
                itemObject is ScrollItemPattern item)
            {
                item.ScrollIntoView();
            }

            // WPF controls inside this ScrollViewer do not consistently expose
            // ScrollItemPattern. If the requested target is still clipped, use the
            // public ScrollPattern to move through the viewport until its UIA bounds
            // become physically reachable by the real picker.
            if (target is not null)
            {
                for (var percent = 0.0; percent <= 100.0; percent += 10.0)
                {
                    var rect = target.Current.BoundingRectangle;
                    var center = new System.Drawing.Point(
                        (int)Math.Round(rect.Left + rect.Width / 2),
                        (int)Math.Round(rect.Top + rect.Height / 2));
                    if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0 &&
                        IsPointInsideHostClientArea(hostHwnd, center))
                    {
                        break;
                    }

                    scroll.SetScrollPercent(ScrollPattern.NoScroll, percent);
                }
            }
        }

        SetForegroundWindow(hostHwnd);
    }

    private static bool IsPointInsideHostClientArea(IntPtr hwnd, System.Drawing.Point point)
    {
        if (!GetClientRect(hwnd, out var client)) return false;
        var topLeft = new NativePoint { X = client.Left, Y = client.Top };
        var bottomRight = new NativePoint { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(hwnd, ref topLeft) || !ClientToScreen(hwnd, ref bottomRight)) return false;
        return point.X >= topLeft.X && point.X < bottomRight.X &&
               point.Y >= topLeft.Y && point.Y < bottomRight.Y;
    }

    private static AutomationElement FindTargetWithinGroup(
        AutomationElement window,
        string groupAutomationId,
        string targetAutomationId)
    {
        var group = FindByAutomationId(window, groupAutomationId);
        if (group.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var groupScrollObject) &&
            groupScrollObject is ScrollItemPattern groupScroll)
        {
            groupScroll.ScrollIntoView();
        }

        var groupRect = group.Current.BoundingRectangle;
        return FindAllByAutomationId(window, targetAutomationId)
            .FirstOrDefault(candidate =>
            {
                var rect = candidate.Current.BoundingRectangle;
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return false;
                var centerX = rect.Left + rect.Width / 2;
                return centerX >= groupRect.Left && centerX <= groupRect.Right;
            })
            ?? throw new InvalidOperationException(
                $"Target '{targetAutomationId}' was not found inside group '{groupAutomationId}'.");
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

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
