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
        MapDisplay.MarkerClicked += OnMarkerClicked;
        MapDisplay.MapZoomChanged += OnMapZoomChanged;
        UpdateMapZoomReadout(MapDisplay.MapRangeNm);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        MapDisplay.TrackClicked -= OnTrackClicked;
        MapDisplay.MarkerClicked -= OnMarkerClicked;
        MapDisplay.MapZoomChanged -= OnMapZoomChanged;
    }

    private void OnTrackClicked(string trackId)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectTrack(trackId);

        MapFocusText.Text = $"MAP FOCUS: TRACK {trackId}.";
    }

    private void OnMarkerClicked(DEADSKY.Core.Simulation.TacticalMarkerState marker)
    {
        MapDisplay.CenterOnMarker(marker);
        MapFocusText.Text = $"MAP FOCUS: {marker.Label.ToUpperInvariant()} // {marker.Details.ToUpperInvariant()}";
    }

    private void OnMapZoomChanged(double rangeNm) => UpdateMapZoomReadout(rangeNm);

    private void UpdateMapZoomReadout(double rangeNm)
    {
        MapZoomText.Text = $"MAP {rangeNm:0}NM";
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => MapDisplay.ZoomIn();

    private void OnZoomOut(object sender, RoutedEventArgs e) => MapDisplay.ZoomOut();

    private void OnResetZoom(object sender, RoutedEventArgs e) => MapDisplay.ResetZoom();

    private void OnCenterSelected(object sender, RoutedEventArgs e) => MapDisplay.CenterOnSelectedTrack();

    private void OnFitAction(object sender, RoutedEventArgs e) => MapDisplay.FitToAction();
}
