using System.Windows;
using System.Windows.Input;

namespace Textream.Windows.Views;

public partial class StopButtonWindow : Window
{
    public event Action? StopRequested;

    public StopButtonWindow()
    {
        InitializeComponent();
    }

    private void OnStopClick(object sender, MouseButtonEventArgs e)
    {
        StopRequested?.Invoke();
    }
}
