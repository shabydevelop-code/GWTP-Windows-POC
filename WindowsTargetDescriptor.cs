using System.Windows.Automation;

namespace GWTP_Windows_POC;

public sealed record WindowsTargetDescriptor(
    string ProcessName,
    WindowsWindowDescriptor Window,
    WindowsElementDescriptor Element,
    IReadOnlyList<WindowsAncestorDescriptor> Ancestors);

public sealed record WindowsWindowDescriptor(string? AutomationId, string? Name);

public sealed record WindowsElementDescriptor(string ControlType, string? AutomationId, string? Name);

public sealed record WindowsAncestorDescriptor(string ControlType, string? AutomationId, string? Name);

internal static class WindowsTargetDescriptorFactory
{
    public static WindowsTargetDescriptor Create(AutomationElement element)
    {
        var processName = GetProcessName(element.Current.ProcessId);
        if (string.IsNullOrWhiteSpace(processName))
            throw new InvalidOperationException("The target process could not be identified.");

        var controlType = ProgrammaticControlType(element.Current.ControlType);
        var automationId = NullIfBlank(element.Current.AutomationId);
        var name = NullIfBlank(element.Current.Name);
        if (string.IsNullOrWhiteSpace(automationId) && string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("The selected control has neither AutomationId nor Name.");

        var walker = TreeWalker.ControlViewWalker;
        var ancestors = new List<WindowsAncestorDescriptor>();
        WindowsWindowDescriptor? window = null;
        AutomationElement? current = walker.GetParent(element);

        for (var depth = 0; current is not null && depth < 32; depth++)
        {
            try
            {
                if (current.Current.ControlType == ControlType.Window && current.Current.NativeWindowHandle != 0)
                {
                    window = new WindowsWindowDescriptor(
                        NullIfBlank(current.Current.AutomationId),
                        NullIfBlank(current.Current.Name));
                    break;
                }

                var ancestorAutomationId = NullIfBlank(current.Current.AutomationId);
                var ancestorName = NullIfBlank(current.Current.Name);
                if (ancestorAutomationId is not null || ancestorName is not null)
                {
                    ancestors.Add(new WindowsAncestorDescriptor(
                        ProgrammaticControlType(current.Current.ControlType),
                        ancestorAutomationId,
                        ancestorName));
                }

                current = walker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                break;
            }
        }

        window ??= new WindowsWindowDescriptor(null, null);
        return new WindowsTargetDescriptor(
            processName,
            window,
            new WindowsElementDescriptor(controlType, automationId, name),
            ancestors);
    }

    private static string ProgrammaticControlType(ControlType? controlType)
        => controlType?.ProgrammaticName ?? "ControlType.Custom";

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string GetProcessName(int processId)
    {
        if (processId <= 0) return string.Empty;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
