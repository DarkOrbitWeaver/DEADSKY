using System.Windows;
using DEADSKY.App.ViewModels;

namespace DEADSKY.App.Views;

public partial class TacticalMapWindow : Window
{
    public TacticalMapWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MapDisplay.TrackClicked += OnTrackClicked;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        MapDisplay.TrackClicked -= OnTrackClicked;
    }

    private void OnTrackClicked(string trackId)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectTrack(trackId);
    }
}
