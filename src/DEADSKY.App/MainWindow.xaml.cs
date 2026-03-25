using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
        StateChanged += OnWindowStateChanged;
        RadarScope.TrackClicked += trackId => ViewModel.SelectTrack(trackId);
        RadarScope.RadarClicked += (bearingDeg, rangeNm) => ViewModel.SelectRadarPoint(bearingDeg, rangeNm);
        ThreatList.SelectionChanged += (_, _) =>
        {
            if (ThreatList.SelectedItem is TrackRowViewModel row)
                ViewModel.SelectTrack(row.TrackId);
        };
        PreviewKeyDown += OnWindowPreviewKeyDown;
        MessageInput.KeyDown += OnMessageInputKeyDown;
        CommsTabs.SelectionChanged += OnCommsTabsSelectionChanged;
        ViewModel.AllMessagesRecent.CollectionChanged += OnMessagesChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetActiveCommsTab(CommsTabs.SelectedIndex);
        ViewModel.LoadStartupScenario();
        if (ViewModel.StartMissionCommand.CanExecute(null))
            ViewModel.StartMissionCommand.Execute(null);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StateChanged -= OnWindowStateChanged;
        CommsTabs.SelectionChanged -= OnCommsTabsSelectionChanged;
        ViewModel.AllMessagesRecent.CollectionChanged -= OnMessagesChanged;
        ViewModel.SaveCampaignState();
        ViewModel.Sim.Dispose();
    }

    private void OnMessageInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (ViewModel.SendMessageCommand.CanExecute(null))
            ViewModel.SendMessageCommand.Execute(null);

        e.Handled = true;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Add && e.Action is not NotifyCollectionChangedAction.Reset)
            return;

        if (!ViewModel.IsCommsDrawerOpen || CommsTabs.SelectedIndex != 0 || CommsList.Items.Count == 0)
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => CommsList.ScrollIntoView(CommsList.Items[^1])));
    }

    private void OnCommsTabsSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ViewModel.SetActiveCommsTab(CommsTabs.SelectedIndex);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement == MessageInput;

        if (e.Key == Key.Escape)
        {
            ViewModel.ToggleCommandMenu();
            e.Handled = true;
            return;
        }

        if (typing)
            return;

        switch (e.Key)
        {
            case Key.Space:
                if (ViewModel.SimulationRunning)
                    ViewModel.PauseMission();
                else if (ViewModel.StartMissionCommand.CanExecute(null))
                    ViewModel.StartMission();
                e.Handled = true;
                break;
            case Key.Tab:
                ViewModel.CycleTrackSelection((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                e.Handled = true;
                break;
            case Key.D:
                if (ViewModel.DesignateSelectedCommand.CanExecute(null))
                    ViewModel.DesignateSelected();
                e.Handled = true;
                break;
            case Key.F:
                if (ViewModel.FireSingleCommand.CanExecute(null))
                    ViewModel.FireSingle();
                e.Handled = true;
                break;
            case Key.G:
                if (ViewModel.FireSalvoCommand.CanExecute(null))
                    ViewModel.FireSalvo();
                e.Handled = true;
                break;
            case Key.Q:
                ViewModel.SetRadarSearch();
                e.Handled = true;
                break;
            case Key.W:
                ViewModel.SetRadarTWS();
                e.Handled = true;
                break;
            case Key.E:
                ViewModel.SetRadarSilent();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                ViewModel.SetRadarRangePreset("40");
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                ViewModel.SetRadarRangePreset("80");
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                ViewModel.SetRadarRangePreset("120");
                e.Handled = true;
                break;
            case Key.M:
                OnOpenTacticalMap(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.OemTilde:
                if (!ViewModel.IsCommsDrawerOpen)
                    ViewModel.IsCommsDrawerOpen = true;

                Dispatcher.BeginInvoke(MessageInput.Focus);
                e.Handled = true;
                break;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Maximized)
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                Activate();
                Focus();
                if (!MessageInput.IsKeyboardFocusWithin)
                    RadarScope.Focus();
            }));
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
