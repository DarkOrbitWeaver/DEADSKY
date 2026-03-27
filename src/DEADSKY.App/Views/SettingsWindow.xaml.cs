using System.Windows;

namespace DEADSKY.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        
        // Check if main window is in fullscreen mode
        if (Owner != null)
        {
            FullscreenCheckbox.IsChecked = Owner.WindowStyle == WindowStyle.None && Owner.WindowState == WindowState.Maximized;
        }
    }

    private void OnFullscreenChecked(object sender, RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Owner.WindowStyle = WindowStyle.None;
            Owner.WindowState = WindowState.Maximized;
            Owner.ResizeMode = ResizeMode.NoResize;
        }
    }

    private void OnFullscreenUnchecked(object sender, RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Owner.WindowStyle = WindowStyle.SingleBorderWindow;
            Owner.WindowState = WindowState.Maximized;
            Owner.ResizeMode = ResizeMode.CanResize;
        }
    }
}
