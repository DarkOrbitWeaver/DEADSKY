using System.Windows;
using DEADSKY.App.ViewModels;

namespace DEADSKY.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += OnLoaded;
        Closed += OnClosed;
        RadarScope.TrackClicked += trackId => ViewModel.SelectTrack(trackId);
        RadarScope.RangeChanged += rangeNm => ViewModel.SetRadarRange(rangeNm);
        ThreatList.SelectionChanged += (_, _) =>
        {
            if (ThreatList.SelectedItem is TrackRowViewModel row)
                ViewModel.SelectTrack(row.TrackId);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadScenario("");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.Sim.Dispose();
    }
}
