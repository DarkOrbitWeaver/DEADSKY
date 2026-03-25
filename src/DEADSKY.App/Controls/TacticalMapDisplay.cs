using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.App.Controls;

public class TacticalMapDisplay : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(
            nameof(Snapshot),
            typeof(SimulationSnapshot),
            typeof(TacticalMapDisplay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewSourceChanged));

    public static readonly DependencyProperty SelectedTrackIdProperty =
        DependencyProperty.Register(
            nameof(SelectedTrackId),
            typeof(string),
            typeof(TacticalMapDisplay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScenarioProperty =
        DependencyProperty.Register(
            nameof(Scenario),
            typeof(ScenarioDefinition),
            typeof(TacticalMapDisplay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public event Action<string>? TrackClicked;

    private double _viewRangeNm = 180;
    private Vec2 _viewOffset = Vec2.Zero;
    private bool _isPanning;
    private Point _lastPanPoint;

    public TacticalMapDisplay()
    {
        Focusable = true;
    }

    public SimulationSnapshot? Snapshot
    {
        get => (SimulationSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public string? SelectedTrackId
    {
        get => (string?)GetValue(SelectedTrackIdProperty);
        set => SetValue(SelectedTrackIdProperty, value);
    }

    public ScenarioDefinition? Scenario
    {
        get => (ScenarioDefinition?)GetValue(ScenarioProperty);
        set => SetValue(ScenarioProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 16, 12)), null, bounds);

        if (ActualWidth < 10 || ActualHeight < 10)
            return;

        var mapRect = GetMapRect();
        DrawBackdrop(dc, mapRect);
        DrawTerrain(dc, mapRect);
        DrawGrid(dc, mapRect);

        if (Snapshot == null)
            return;

        DrawDefenseRings(dc, mapRect, Snapshot);
        DrawScenarioOverlays(dc, mapRect);
        DrawEntities(dc, mapRect, Snapshot);
        DrawMissiles(dc, mapRect, Snapshot);
        DrawViewHud(dc, mapRect);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Snapshot == null)
            return;

        var pos = e.GetPosition(this);
        var mapRect = GetMapRect();
        if (!mapRect.Contains(pos))
            return;

        string? nearest = null;
        double nearestDistance = 14;
        foreach (var track in Snapshot.AllTracks)
        {
            var point = GetMapPoint(mapRect, track.Position);
            double distance = (point - pos).Length;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = track.TrackId;
            }
        }

        if (nearest != null)
            TrackClicked?.Invoke(nearest);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Snapshot == null)
            return;

        _isPanning = true;
        _lastPanPoint = e.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPanning || Snapshot == null)
            return;

        var current = e.GetPosition(this);
        var delta = current - _lastPanPoint;
        _lastPanPoint = current;
        _viewOffset -= ScreenDeltaToWorld(delta, GetMapRect());
        ClampViewOffset();
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        EndPan();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndPan();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_isPanning && e.RightButton != MouseButtonState.Pressed)
            EndPan();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Snapshot == null)
            return;

        var mapRect = GetMapRect();
        var focusWorld = ScreenToWorld(e.GetPosition(this), mapRect);
        double zoomFactor = e.Delta > 0 ? 0.86 : 1.18;
        _viewRangeNm = Math.Clamp(_viewRangeNm * zoomFactor, 30, 420);
        _viewOffset = focusWorld - ScreenToWorldOffset(e.GetPosition(this), mapRect, _viewRangeNm);
        ClampViewOffset();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            ResetView();
            e.Handled = true;
        }
    }

    private void DrawBackdrop(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 28, 21)), new Pen(new SolidColorBrush(Color.FromRgb(56, 84, 63)), 1), rect);
    }

    private void DrawTerrain(DrawingContext dc, Rect rect)
    {
        var ridgePen = new Pen(new SolidColorBrush(Color.FromRgb(92, 104, 58)), 2);
        var valleyPen = new Pen(new SolidColorBrush(Color.FromRgb(42, 78, 98)), 1.4);
        var sectorBrush = new SolidColorBrush(Color.FromArgb(40, 134, 122, 76));

        var ridge = new StreamGeometry();
        using (var ctx = ridge.Open())
        {
            ctx.BeginFigure(new Point(rect.Left + 20, rect.Top + rect.Height * 0.28), false, false);
            ctx.PolyLineTo(new[]
            {
                new Point(rect.Left + rect.Width * 0.22, rect.Top + rect.Height * 0.18),
                new Point(rect.Left + rect.Width * 0.42, rect.Top + rect.Height * 0.24),
                new Point(rect.Left + rect.Width * 0.58, rect.Top + rect.Height * 0.15),
                new Point(rect.Left + rect.Width * 0.84, rect.Top + rect.Height * 0.2),
                new Point(rect.Right - 22, rect.Top + rect.Height * 0.12)
            }, true, true);
        }

        ridge.Freeze();
        dc.DrawGeometry(null, ridgePen, ridge);

        var river = new StreamGeometry();
        using (var ctx = river.Open())
        {
            ctx.BeginFigure(new Point(rect.Left + rect.Width * 0.13, rect.Bottom - 16), false, false);
            ctx.BezierTo(
                new Point(rect.Left + rect.Width * 0.18, rect.Top + rect.Height * 0.68),
                new Point(rect.Left + rect.Width * 0.36, rect.Top + rect.Height * 0.54),
                new Point(rect.Left + rect.Width * 0.48, rect.Top + rect.Height * 0.48), true, true);
            ctx.BezierTo(
                new Point(rect.Left + rect.Width * 0.64, rect.Top + rect.Height * 0.4),
                new Point(rect.Left + rect.Width * 0.74, rect.Top + rect.Height * 0.22),
                new Point(rect.Right - 28, rect.Top + rect.Height * 0.08), true, true);
        }

        river.Freeze();
        dc.DrawGeometry(null, valleyPen, river);

        dc.DrawRectangle(sectorBrush, null, new Rect(rect.Left + rect.Width * 0.62, rect.Top + rect.Height * 0.56, rect.Width * 0.18, rect.Height * 0.14));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(46, 70, 42, 24)), null, new Rect(rect.Left + rect.Width * 0.16, rect.Top + rect.Height * 0.68, rect.Width * 0.16, rect.Height * 0.12));
    }

    private void DrawGrid(DrawingContext dc, Rect rect)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(55, 81, 104, 86)), 0.8);
        for (int i = 1; i < 6; i++)
        {
            var x = rect.Left + rect.Width * i / 6.0;
            var y = rect.Top + rect.Height * i / 6.0;
            dc.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }

    private void DrawDefenseRings(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        var batteryPoint = GetMapPoint(rect, Vec2.Zero);
        var rangeFactor = rect.Width / 2.4 / _viewRangeNm;
        var maxRing = snapshot.Battery?.MissileMaxRangeNm ?? 18;
        var radarRing = snapshot.RadarRangeNm;

        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 220, 120)), 1), batteryPoint, radarRing * rangeFactor, radarRing * rangeFactor);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(24, 0, 160, 70)), new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 220, 120)), 1), batteryPoint, maxRing * rangeFactor, maxRing * rangeFactor);
    }

    private void DrawScenarioOverlays(DrawingContext dc, Rect rect)
    {
        if (Scenario?.SectorMap.Objectives.Count > 0)
        {
            foreach (var objective in Scenario.SectorMap.Objectives)
            {
                var point = GetMapPoint(rect, CoordinateSystem.FromBearingRange(objective.BearingDeg, objective.RangeNm));
                DrawObjectiveMarker(dc, point, objective.Importance);
            }
        }
    }

    private void DrawEntities(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        DrawBattery(dc, GetMapPoint(rect, Vec2.Zero));

        foreach (var track in snapshot.AllTracks.OrderByDescending(t => t.ThreatLevel))
        {
            var point = GetMapPoint(rect, track.Position);
            var fill = track.Classification switch
            {
                TrackClassification.Hostile or TrackClassification.AssumedHostile => new SolidColorBrush(Color.FromRgb(214, 73, 73)),
                TrackClassification.Friendly => new SolidColorBrush(Color.FromRgb(74, 152, 224)),
                _ => new SolidColorBrush(Color.FromRgb(219, 206, 78))
            };

            DrawTrack(dc, point, fill, track.TrackId == SelectedTrackId);
            if (track.TrackId == SelectedTrackId)
            {
                DrawLabel(dc, track.TrackDesignation.ToUpperInvariant(), new Point(point.X + 8, point.Y - 4), fill, 10, FontWeights.SemiBold);
                DrawLabel(dc, $"{track.TrackId} {track.RangeNm:0.0}NM", new Point(point.X + 8, point.Y + 10), Brushes.Gainsboro, 9, FontWeights.Normal);
            }
        }
    }

    private void DrawMissiles(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 64)), 1.6);
        foreach (var missile in snapshot.ActiveMissiles)
        {
            var point = GetMapPoint(rect, missile.Position);
            var heading = CoordinateSystem.DegToRad(missile.HeadingDeg);
            var tail = new Point(point.X - Math.Sin(heading) * 10, point.Y + Math.Cos(heading) * 10);
            dc.DrawLine(pen, tail, point);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 180, 110)), null, point, 3.2, 3.2);
        }
    }

    private void DrawViewHud(DrawingContext dc, Rect rect)
    {
        DrawLabel(dc, $"VIEW {_viewRangeNm:0}NM", new Point(rect.Left + 8, rect.Bottom - 28), Brushes.Gainsboro, 10, FontWeights.Bold);
        DrawLabel(dc, $"PAN {CoordinateSystem.MetersToNm(_viewOffset.Length):0.0}NM | RMB DRAG | WHEEL ZOOM | MMB RESET",
            new Point(rect.Left + 8, rect.Bottom - 14), Brushes.DarkSeaGreen, 9, FontWeights.Normal);
    }

    private static void DrawBattery(DrawingContext dc, Point point)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(point.X, point.Y - 10), true, true);
            ctx.LineTo(new Point(point.X + 9, point.Y + 8), true, false);
            ctx.LineTo(new Point(point.X - 9, point.Y + 8), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(60, 225, 128)), new Pen(Brushes.Black, 1), geometry);
        DrawLabel(dc, "BATTERY", new Point(point.X + 10, point.Y - 16), Brushes.LightGreen, 10, FontWeights.Bold);
    }

    private static void DrawTrack(DrawingContext dc, Point point, Brush fill, bool selected)
    {
        var diamond = new StreamGeometry();
        using (var ctx = diamond.Open())
        {
            ctx.BeginFigure(new Point(point.X, point.Y - 6), true, true);
            ctx.LineTo(new Point(point.X + 6, point.Y), true, false);
            ctx.LineTo(new Point(point.X, point.Y + 6), true, false);
            ctx.LineTo(new Point(point.X - 6, point.Y), true, false);
        }

        diamond.Freeze();
        dc.DrawGeometry(fill, new Pen(Brushes.Black, 1), diamond);
        if (selected)
            dc.DrawEllipse(null, new Pen(Brushes.Lime, 1.2), point, 10, 10);
    }

    private static void DrawObjectiveMarker(DrawingContext dc, Point point, string importance)
    {
        var brush = importance.Equals("primary", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(255, 214, 92))
            : new SolidColorBrush(Color.FromRgb(115, 190, 255));
        var pen = new Pen(brush, 1.4);
        dc.DrawEllipse(null, pen, point, 7, 7);
        dc.DrawLine(pen, new Point(point.X - 9, point.Y), new Point(point.X + 9, point.Y));
        dc.DrawLine(pen, new Point(point.X, point.Y - 9), new Point(point.X, point.Y + 9));
    }

    private Point GetMapPoint(Rect rect, Vec2 position)
    {
        var centered = position - _viewOffset;
        double rangeM = CoordinateSystem.NmToMeters(_viewRangeNm);
        double x = rect.Left + rect.Width / 2 + centered.X / rangeM * (rect.Width / 2);
        double y = rect.Top + rect.Height / 2 - centered.Y / rangeM * (rect.Height / 2);
        return new Point(x, y);
    }

    private Vec2 ScreenToWorld(Point point, Rect rect) => _viewOffset + ScreenToWorldOffset(point, rect, _viewRangeNm);

    private static Vec2 ScreenToWorldOffset(Point point, Rect rect, double viewRangeNm)
    {
        double rangeM = CoordinateSystem.NmToMeters(viewRangeNm);
        double x = (point.X - (rect.Left + rect.Width / 2)) / (rect.Width / 2) * rangeM;
        double y = -((point.Y - (rect.Top + rect.Height / 2)) / (rect.Height / 2) * rangeM);
        return new Vec2(x, y);
    }

    private Vec2 ScreenDeltaToWorld(Vector delta, Rect rect)
    {
        double rangeM = CoordinateSystem.NmToMeters(_viewRangeNm);
        double worldX = delta.X / (rect.Width / 2) * rangeM;
        double worldY = -(delta.Y / (rect.Height / 2) * rangeM);
        return new Vec2(worldX, worldY);
    }

    private Rect GetMapRect() => new(18, 18, Math.Max(10, ActualWidth - 36), Math.Max(10, ActualHeight - 36));

    private void ResetView()
    {
        _viewRangeNm = 180;
        _viewOffset = Vec2.Zero;
        InvalidateVisual();
    }

    private void ClampViewOffset()
    {
        double maxOffsetM = CoordinateSystem.NmToMeters(_viewRangeNm * 1.35);
        _viewOffset = new Vec2(
            Math.Clamp(_viewOffset.X, -maxOffsetM, maxOffsetM),
            Math.Clamp(_viewOffset.Y, -maxOffsetM, maxOffsetM));
    }

    private void EndPan()
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
    }

    private static void DrawLabel(DrawingContext dc, string text, Point point, Brush brush, double size, FontWeight weight)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);

        dc.DrawText(formatted, point);
    }

    private static void OnViewSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TacticalMapDisplay display && e.OldValue == null && e.NewValue != null)
            display.ResetView();
    }
}
