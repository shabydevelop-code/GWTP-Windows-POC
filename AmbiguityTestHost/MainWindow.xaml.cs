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
    {
        var button = new System.Windows.Controls.Button
        {
            Width = 180,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = "Recreated Target"
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "RecreatedTarget");
        return button;
    }

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
