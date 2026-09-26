using System.Windows;

namespace GWTP_Ambiguity_TestHost;

public partial class MainWindow : Window
{
    private int _dynamicNameVersion = 1;
    private SecondTestWindow? _secondWindow;

    public MainWindow()
    {
        InitializeComponent();
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
