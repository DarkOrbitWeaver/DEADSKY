using System.Windows;
using System.Windows.Input;
using System.Collections.Specialized;
using DEADSKY.App.ViewModels;
using DEADSKY.App.Views;

namespace DEADSKY.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private TacticalMapWindow? _tacticalMapWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += OnLoaded;
        Closed += OnClosed;
        RadarScope.TrackClicked += trackId => ViewModel.SelectTrack(trackId);
        RadarScope.RadarClicked += (bearingDeg, rangeNm) => ViewModel.SelectRadarPoint(bearingDeg, rangeNm);
        RadarScope.RangeChanged += rangeNm => ViewModel.SetRadarRange(rangeNm);
        ThreatList.SelectionChanged += (_, _) =>
        {
            if (ThreatList.SelectedItem is TrackRowViewModel row)
                ViewModel.SelectTrack(row.TrackId);
        };
        MessageInput.KeyDown += OnMessageInputKeyDown;
        ViewModel.AllMessagesRecent.CollectionChanged += OnMessagesChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadScenario("");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.AllMessagesRecent.CollectionChanged -= OnMessagesChanged;
        ViewModel.Sim.Dispose();
    }

    private void OnMessageInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (ViewModel.SendMessageCommand.CanExecute(null))
            ViewModel.SendMessageCommand.Execute(null);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (CommsList.Items.Count == 0)
            return;

        Dispatcher.BeginInvoke(() => CommsList.ScrollIntoView(CommsList.Items[^1]));
    }

    private void OnOpenTacticalMap(object sender, RoutedEventArgs e)
    {
        if (_tacticalMapWindow == null)
        {
            _tacticalMapWindow = new TacticalMapWindow
            {
                Owner = this,
                DataContext = ViewModel
            };
            _tacticalMapWindow.Closed += (_, _) => _tacticalMapWindow = null;
            _tacticalMapWindow.Show();
            return;
        }

        if (!_tacticalMapWindow.IsVisible)
            _tacticalMapWindow.Show();

        _tacticalMapWindow.Activate();
    }
}
