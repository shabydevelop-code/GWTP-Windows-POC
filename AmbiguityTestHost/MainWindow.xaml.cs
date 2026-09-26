using System.Windows;

namespace GWTP_Ambiguity_TestHost;

public partial class MainWindow : Window
{
    private int _dynamicNameVersion = 1;
    private SecondTestWindow? _secondWindow;

    public MainWindow()
    {
        InitializeComponent();
        RecreatedTargetHost.Content = CreateRecreatedTarget();
        Closed += (_, _) => _secondWindow?.Close();
    }

internal static class ObjectExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}

    private void ChangeDynamicName_Click(object sender, RoutedEventArgs e)
    {
        _dynamicNameVersion++;
        DynamicNameButton.Content = $"Dynamic target {_dynamicNameVersion}";
    }

    private void ToggleDynamicTarget_Click(object sender, RoutedEventArgs e)
    {
        DynamicTargetButton.Visibility =
            DynamicTargetButton.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private static System.Windows.Controls.Button CreateRecreatedTarget()
        => new()
        {
            Width = 180,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = "Recreated Target"
        }.Also(button => System.Windows.Automation.AutomationProperties.SetAutomationId(button, "RecreatedTarget"));

    private void RecreateTarget_Click(object sender, RoutedEventArgs e)
    {
        RecreatedTargetHost.Content = null;
        RecreatedTargetHost.Content = CreateRecreatedTarget();
    }

    private void ChangeWindowTitle_Click(object sender, RoutedEventArgs e)
    {
        Title = Title == "GWTP Windows UIA Test Host"
            ? "GWTP Windows UIA Test Host — Changed"
            : "GWTP Windows UIA Test Host";
    }

    private void OpenSecondWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_secondWindow is { IsVisible: true })
        {
            _secondWindow.Activate();
            return;
        }

        _secondWindow = new SecondTestWindow();
        _secondWindow.Closed += (_, _) => _secondWindow = null;
        _secondWindow.Show();
    }
}
