using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

internal static class Program
{
    private const int TimeoutMs = 10000;
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
                WaitForTopLevelWindow("GWTP Windows UIA Test Host");
            });

            var hostWindow = WaitForTopLevelWindow("GWTP Windows UIA Test Host");

            Run("T02 TextBox with AutomationId is exposed through UIA", () =>
            {
                var target = FindByAutomationId(hostWindow, "StableTextBox");
                Require(target.Current.ControlType == ControlType.Edit, "StableTextBox is not exposed as Edit.");
            });

            Run("T03 TextBox without authored AutomationId remains selectable in GUI", () =>
            {
                var target = FindByName(hostWindow, "No automation id");
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

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine($"PASS  {name}"); }
        catch (Exception ex) { _failed++; Console.WriteLine($"FAIL  {name} — {ex.Message}"); }
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

    private static AutomationElement WaitForTopLevelWindow(string name)
    {
        AutomationElement? found = null;
        WaitUntil(() =>
        {
            found = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.NameProperty, name));
            return found is not null;
        }, $"Window '{name}' did not appear.");
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

    private static void WaitUntil(Func<bool> condition, string error)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < TimeoutMs)
        {
            try { if (condition()) return; } catch (ElementNotAvailableException) { }
            Thread.Sleep(50);
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
