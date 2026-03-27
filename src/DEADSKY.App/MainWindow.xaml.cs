using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Specialized;
using DEADSKY.App.ViewModels;
using DEADSKY.App.Views;
using DEADSKY.Core.Logging;

namespace DEADSKY.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private TacticalMapWindow? _tacticalMapWindow;
    private SettingsWindow? _settingsWindow;
    private LogisticsWindow? _logisticsWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += OnWindowStateChanged;
        RadarScope.TrackClicked += trackId =>
        {
            GameLogger.Info("INPUT", $"Radar track clicked: {trackId}");
            ViewModel.SelectTrack(trackId);
        };
        RadarScope.RangeChanged += rangeNm =>
        {
            GameLogger.Info("INPUT", $"Radar range changed to {rangeNm:F1}nm");
            ViewModel.SetRadarRange(rangeNm);
        };
        RadarScope.RadarClicked += (bearingDeg, rangeNm) =>
        {
            GameLogger.Info("INPUT", $"Radar clicked at bearing {bearingDeg:F1}° range {rangeNm:F1}nm");
            ViewModel.SelectRadarPoint(bearingDeg, rangeNm);
        };
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
        _tacticalMapWindow?.Close();
        _settingsWindow?.Close();
        _logisticsWindow?.Close();
        ViewModel.SaveCampaignState();
        ViewModel.Dispose(); // Dispose ViewModel to clean up all event subscriptions
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

        // Alt+Enter toggles fullscreen
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

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
                GameLogger.Info("INPUT", ViewModel.SimulationRunning ? "Space pressed - pausing mission" : "Space pressed - starting mission");
                if (ViewModel.SimulationRunning)
                    ViewModel.PauseMission();
                else if (ViewModel.StartMissionCommand.CanExecute(null))
                    ViewModel.StartMission();
                e.Handled = true;
                break;
            case Key.Tab:
                GameLogger.Info("INPUT", $"Tab pressed - cycling track selection ({((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "backward" : "forward")})");
                ViewModel.CycleTrackSelection((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                e.Handled = true;
                break;
            case Key.D:
                if (ViewModel.DesignateSelectedCommand.CanExecute(null))
                {
                    GameLogger.Info("INPUT", $"D pressed - designating track {ViewModel.SelectedTrackId}");
                    ViewModel.DesignateSelected();
                }
                e.Handled = true;
                break;
            case Key.H:
                if (ViewModel.ToggleTrackHoldSelectedCommand.CanExecute(null))
                {
                    GameLogger.Info("INPUT", $"H pressed - toggling track hold for {ViewModel.SelectedTrackId}");
                    ViewModel.ToggleTrackHoldSelected();
                }
                e.Handled = true;
                break;
            case Key.R:
                if (ViewModel.ReleaseSelectedTrackCommand.CanExecute(null))
                {
                    GameLogger.Info("INPUT", $"R pressed - releasing track {ViewModel.SelectedTrackId}");
                    ViewModel.ReleaseSelectedTrack();
                }
                e.Handled = true;
                break;
            case Key.F:
                if (ViewModel.FireSingleCommand.CanExecute(null))
                {
                    GameLogger.Info("INPUT", $"F pressed - firing single missile at {ViewModel.SelectedTrackId}");
                    ViewModel.FireSingle();
                }
                e.Handled = true;
                break;
            case Key.G:
                if (ViewModel.FireSalvoCommand.CanExecute(null))
                {
                    GameLogger.Info("INPUT", $"G pressed - firing salvo at {ViewModel.SelectedTrackId}");
                    ViewModel.FireSalvo();
                }
                e.Handled = true;
                break;
            case Key.Q:
                GameLogger.Info("INPUT", "Q pressed - setting radar to Search mode");
                ViewModel.SetRadarSearch();
                e.Handled = true;
                break;
            case Key.W:
                GameLogger.Info("INPUT", "W pressed - setting radar to TWS mode");
                ViewModel.SetRadarTWS();
                e.Handled = true;
                break;
            case Key.E:
                GameLogger.Info("INPUT", "E pressed - setting radar to Silent mode");
                ViewModel.SetRadarSilent();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                GameLogger.Info("INPUT", "1 pressed - setting radar range to 40nm");
                ViewModel.SetRadarRangePreset("40");
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                GameLogger.Info("INPUT", "2 pressed - setting radar range to 80nm");
                ViewModel.SetRadarRangePreset("80");
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                GameLogger.Info("INPUT", "3 pressed - setting radar range to 120nm");
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

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow
            {
                Owner = this,
                DataContext = ViewModel
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            return;
        }

        if (!_settingsWindow.IsVisible)
            _settingsWindow.Show();

        _settingsWindow.Activate();
    }

    private void OnOpenLogistics(object sender, RoutedEventArgs e)
    {
        if (_logisticsWindow == null)
        {
            _logisticsWindow = new LogisticsWindow
            {
                Owner = this,
                DataContext = ViewModel
            };
            _logisticsWindow.Closed += (_, _) => _logisticsWindow = null;
            _logisticsWindow.Show();
            return;
        }

        if (!_logisticsWindow.IsVisible)
            _logisticsWindow.Show();

        _logisticsWindow.Activate();
    }

    private void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            // Exit fullscreen
            GameLogger.Info("INPUT", "Exiting fullscreen mode");
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.CanResize;
        }
        else
        {
            // Enter fullscreen
            GameLogger.Info("INPUT", "Entering fullscreen mode");
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }
    }
}
