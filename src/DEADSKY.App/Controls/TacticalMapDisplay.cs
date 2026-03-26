using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.App.Controls;

public class TacticalMapDisplay : FrameworkElement
{
    private static readonly double[] ZoomPresetsNm = [60, 90, 120, 180, 240];
    private static readonly Typeface MapTypeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface MapTypefaceBold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface MapTypefaceSemiBold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush RootBackgroundBrush = CreateFrozenBrush(Color.FromRgb(11, 16, 12));
    private static readonly Brush MapBackgroundBrush = CreateFrozenBrush(Color.FromRgb(18, 28, 21));
    private static readonly Pen MapBorderPen = CreateFrozenPen(Color.FromRgb(56, 84, 63), 1);
    private static readonly Pen RidgePen = CreateFrozenPen(Color.FromRgb(92, 104, 58), 2);
    private static readonly Pen ValleyPen = CreateFrozenPen(Color.FromRgb(42, 78, 98), 1.4);
    private static readonly Brush SectorBrush = CreateFrozenBrush(Color.FromArgb(40, 134, 122, 76));
    private static readonly Brush FarmBrush = CreateFrozenBrush(Color.FromArgb(46, 70, 42, 24));
    private static readonly Pen GridPen = CreateFrozenPen(Color.FromArgb(55, 81, 104, 86), 0.8);
    private static readonly Pen DefenseRingPen = CreateFrozenPen(Color.FromArgb(90, 0, 220, 120), 1);
    private static readonly Brush DefenseRingFill = CreateFrozenBrush(Color.FromArgb(24, 0, 160, 70));
    private static readonly Brush HostileTrackBrush = CreateFrozenBrush(Color.FromRgb(214, 73, 73));
    private static readonly Brush FriendlyTrackBrush = CreateFrozenBrush(Color.FromRgb(74, 152, 224));
    private static readonly Brush UnknownTrackBrush = CreateFrozenBrush(Color.FromRgb(219, 206, 78));
    private static readonly Pen MissilePen = CreateFrozenPen(Color.FromRgb(255, 140, 64), 1.6);
    private static readonly Brush MissileBrush = CreateFrozenBrush(Color.FromRgb(255, 180, 110));
    private static readonly Brush BatteryBrush = CreateFrozenBrush(Color.FromRgb(60, 225, 128));
    private static readonly Pen OutlinePen = CreateFrozenPen(Colors.Black, 1);
    private static readonly Pen SelectedTrackPen = CreateFrozenPen(Colors.Lime, 1.2);
    private static readonly Pen HeldTrackPen = CreateFrozenPen(Colors.LightGreen, 1);

    private StreamGeometry? _cachedRidgeGeometry;
    private StreamGeometry? _cachedRiverGeometry;
    private Rect _cachedTerrainRect;

    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(
            nameof(Snapshot),
            typeof(SimulationSnapshot),
            typeof(TacticalMapDisplay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedTrackIdProperty =
        DependencyProperty.Register(
            nameof(SelectedTrackId),
            typeof(string),
            typeof(TacticalMapDisplay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(
            nameof(ShowLabels),
            typeof(bool),
            typeof(TacticalMapDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FriendlyForcesProperty =
        DependencyProperty.Register(
            nameof(FriendlyForces),
            typeof(IEnumerable<FriendlyForceState>),
            typeof(TacticalMapDisplay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _mapRangeNm = 180;

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

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public IEnumerable<FriendlyForceState>? FriendlyForces
    {
        get => (IEnumerable<FriendlyForceState>?)GetValue(FriendlyForcesProperty);
        set => SetValue(FriendlyForcesProperty, value);
    }

    public double MapRangeNm => _mapRangeNm;

    public event Action<string>? TrackClicked;
    public event Action<double>? MapZoomChanged;

    public TacticalMapDisplay()
    {
        SizeChanged += (_, _) => InvalidateTerrainCache();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(RootBackgroundBrush, null, bounds);

        if (ActualWidth < 10 || ActualHeight < 10)
            return;

        var mapRect = new Rect(18, 18, Math.Max(10, ActualWidth - 36), Math.Max(10, ActualHeight - 36));
        DrawBackdrop(dc, mapRect);
        DrawTerrain(dc, mapRect);
        DrawGrid(dc, mapRect);

        if (Snapshot == null)
            return;

        DrawDefenseRings(dc, mapRect, Snapshot);
        DrawLandmarks(dc, mapRect);
        DrawEntities(dc, mapRect, Snapshot);
        DrawMissiles(dc, mapRect, Snapshot);
        DrawLegend(dc, mapRect, Snapshot);
    }

    private static void DrawBackdrop(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(MapBackgroundBrush, MapBorderPen, rect);
    }

    private void DrawTerrain(DrawingContext dc, Rect rect)
    {
        EnsureTerrainCache(rect);
        dc.DrawGeometry(null, RidgePen, _cachedRidgeGeometry);
        dc.DrawGeometry(null, ValleyPen, _cachedRiverGeometry);
        dc.DrawRectangle(SectorBrush, null, new Rect(rect.Left + rect.Width * 0.62, rect.Top + rect.Height * 0.56, rect.Width * 0.18, rect.Height * 0.14));
        dc.DrawRectangle(FarmBrush, null, new Rect(rect.Left + rect.Width * 0.16, rect.Top + rect.Height * 0.68, rect.Width * 0.16, rect.Height * 0.12));
    }

    private static void DrawGrid(DrawingContext dc, Rect rect)
    {
        for (int i = 1; i < 6; i++)
        {
            var x = rect.Left + rect.Width * i / 6.0;
            var y = rect.Top + rect.Height * i / 6.0;
            dc.DrawLine(GridPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            dc.DrawLine(GridPen, new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }

    private void DrawDefenseRings(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        var outer = GetMapPoint(rect, Vec2.Zero, _mapRangeNm);
        var rangeFactor = rect.Width / 2.4 / _mapRangeNm;
        var maxRing = snapshot.Battery?.MissileMaxRangeNm ?? 18;
        var radarRing = snapshot.RadarRangeNm;

        dc.DrawEllipse(null, DefenseRingPen, outer, radarRing * rangeFactor, radarRing * rangeFactor);
        dc.DrawEllipse(DefenseRingFill, DefenseRingPen, outer, maxRing * rangeFactor, maxRing * rangeFactor);
    }

    private void DrawLandmarks(DrawingContext dc, Rect rect)
    {
        if (!ShowLabels)
            return;

        DrawLabel(dc, "SABLE RIDGE", new Point(rect.Left + rect.Width * 0.46, rect.Top + rect.Height * 0.12), Brushes.DarkKhaki, 12, FontWeights.Bold);
        DrawLabel(dc, "VANTA RIVER", new Point(rect.Left + rect.Width * 0.57, rect.Top + rect.Height * 0.42), Brushes.SteelBlue, 11, FontWeights.SemiBold);
        DrawLabel(dc, "KOVRAN DEPOT", new Point(rect.Left + rect.Width * 0.54, rect.Top + rect.Height * 0.62), Brushes.LightGoldenrodYellow, 11, FontWeights.Bold);
        DrawLabel(dc, "ASHEN FARMS", new Point(rect.Left + rect.Width * 0.2, rect.Top + rect.Height * 0.78), Brushes.OliveDrab, 10, FontWeights.Normal);
    }

    private void DrawEntities(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        var batteryPoint = GetMapPoint(rect, Vec2.Zero, _mapRangeNm);
        DrawBattery(dc, batteryPoint);

        foreach (var track in snapshot.AllTracks.OrderByDescending(t => t.ThreatLevel))
        {
            var point = GetMapPoint(rect, track.Position, _mapRangeNm);
            var fill = track.Classification switch
            {
                TrackClassification.Hostile or TrackClassification.AssumedHostile => HostileTrackBrush,
                TrackClassification.Friendly => FriendlyTrackBrush,
                _ => UnknownTrackBrush
            };

            DrawTrack(dc, point, fill, track.TrackId == SelectedTrackId, track.IsTrackHeld || track.IsDesignated);

            if (ShowLabels)
            {
                DrawLabel(dc, $"{track.TrackDesignation} {track.TrackId}", new Point(point.X + 8, point.Y - 4), fill, 10, FontWeights.SemiBold);
                DrawLabel(dc, $"{track.RangeNm:0.0}nm / FL{track.AltitudeFt / 100:0}", new Point(point.X + 8, point.Y + 10), Brushes.Gainsboro, 9, FontWeights.Normal);
            }
        }

        if (FriendlyForces == null)
            return;

        foreach (var force in FriendlyForces.Where(force => force.VisibleInPicture))
        {
            var point = GetMapPoint(rect, force.Position, _mapRangeNm);
            DrawFriendlySupport(dc, point, force);
        }
    }

    private void DrawMissiles(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        foreach (var missile in snapshot.ActiveMissiles)
        {
            var point = GetMapPoint(rect, missile.Position, _mapRangeNm);
            var heading = CoordinateSystem.DegToRad(missile.HeadingDeg);
            var tail = new Point(point.X - Math.Sin(heading) * 10, point.Y + Math.Cos(heading) * 10);
            dc.DrawLine(MissilePen, tail, point);
            dc.DrawEllipse(MissileBrush, null, point, 3.2, 3.2);
        }
    }

    private void DrawLegend(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        DrawLabel(dc, $"SECTOR 7A // {snapshot.GameTimeString}", new Point(rect.Left + 10, rect.Top + 10), Brushes.Gainsboro, 12, FontWeights.Bold);
        DrawLabel(dc, $"Hostiles {snapshot.HostileTracks.Count}  |  Missiles {snapshot.ActiveMissiles.Count}  |  Mode {snapshot.RadarMode}", new Point(rect.Left + 10, rect.Top + 28), Brushes.LightGreen, 10, FontWeights.Normal);
        DrawLabel(dc, $"Map range {_mapRangeNm:0}nm  |  Wheel zoom  |  Click track to select", new Point(rect.Left + 10, rect.Top + 44), Brushes.DarkSeaGreen, 9, FontWeights.Normal);
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

        dc.DrawGeometry(BatteryBrush, OutlinePen, geometry);
        DrawLabel(dc, "ALPHA BATTERY", new Point(point.X + 10, point.Y - 16), Brushes.LightGreen, 10, FontWeights.Bold);
    }

    private static void DrawTrack(DrawingContext dc, Point point, Brush fill, bool selected, bool held)
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

        dc.DrawGeometry(fill, OutlinePen, diamond);
        if (selected)
            dc.DrawEllipse(null, SelectedTrackPen, point, 10, 10);
        else if (held)
            dc.DrawEllipse(null, HeldTrackPen, point, 8, 8);
    }

    private static void DrawFriendlySupport(DrawingContext dc, Point point, FriendlyForceState force)
    {
        dc.DrawEllipse(FriendlyTrackBrush, OutlinePen, point, 5.2, 5.2);
        dc.DrawLine(HeldTrackPen, new Point(point.X - 8, point.Y), new Point(point.X + 8, point.Y));
        dc.DrawLine(HeldTrackPen, new Point(point.X, point.Y - 8), new Point(point.X, point.Y + 8));
        DrawLabel(dc, $"{force.Callsign} {force.Role.ToUpperInvariant()}", new Point(point.X + 9, point.Y - 4), FriendlyTrackBrush, 9, FontWeights.SemiBold);
    }

    private static Point GetMapPoint(Rect rect, Vec2 position, double mapRangeNm)
    {
        var rangeM = CoordinateSystem.NmToMeters(mapRangeNm);
        var x = rect.Left + rect.Width / 2 + position.X / rangeM * (rect.Width / 2);
        var y = rect.Top + rect.Height / 2 - position.Y / rangeM * (rect.Height / 2);
        return new Point(x, y);
    }

    private static void DrawLabel(DrawingContext dc, string text, Point point, Brush brush, double size, FontWeight weight)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            ResolveTypeface(weight),
            size,
            brush,
            1.0);

        dc.DrawText(formatted, point);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        int index = Array.FindIndex(ZoomPresetsNm, preset => Math.Abs(preset - _mapRangeNm) < 0.1);
        if (index < 0)
            index = Array.FindIndex(ZoomPresetsNm, preset => preset >= _mapRangeNm);
        if (index < 0)
            index = ZoomPresetsNm.Length - 1;

        int nextIndex = e.Delta > 0
            ? Math.Max(0, index - 1)
            : Math.Min(ZoomPresetsNm.Length - 1, index + 1);

        SetMapRange(ZoomPresetsNm[nextIndex]);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (Snapshot == null || ActualWidth < 10 || ActualHeight < 10)
            return;

        var mapRect = new Rect(18, 18, Math.Max(10, ActualWidth - 36), Math.Max(10, ActualHeight - 36));
        var click = e.GetPosition(this);
        string? nearestTrack = null;
        double nearestDistance = 16;

        foreach (var track in Snapshot.AllTracks)
        {
            Point point = GetMapPoint(mapRect, track.Position, _mapRangeNm);
            double distance = (point - click).Length;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTrack = track.TrackId;
            }
        }

        if (nearestTrack != null)
        {
            TrackClicked?.Invoke(nearestTrack);
            e.Handled = true;
        }
    }

    public void ZoomIn() => StepZoom(-1);

    public void ZoomOut() => StepZoom(1);

    public void ResetZoom() => SetMapRange(180);

    private void StepZoom(int delta)
    {
        int index = Array.FindIndex(ZoomPresetsNm, preset => Math.Abs(preset - _mapRangeNm) < 0.1);
        if (index < 0)
            index = Array.FindIndex(ZoomPresetsNm, preset => preset >= _mapRangeNm);
        if (index < 0)
            index = ZoomPresetsNm.Length - 1;

        int nextIndex = Math.Clamp(index + delta, 0, ZoomPresetsNm.Length - 1);
        SetMapRange(ZoomPresetsNm[nextIndex]);
    }

    private void SetMapRange(double rangeNm)
    {
        if (Math.Abs(_mapRangeNm - rangeNm) < 0.1)
            return;

        _mapRangeNm = rangeNm;
        MapZoomChanged?.Invoke(_mapRangeNm);
        InvalidateVisual();
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Typeface ResolveTypeface(FontWeight weight) =>
        weight == FontWeights.Bold
            ? MapTypefaceBold
            : weight == FontWeights.SemiBold
                ? MapTypefaceSemiBold
                : MapTypeface;

    private void InvalidateTerrainCache()
    {
        _cachedTerrainRect = Rect.Empty;
        _cachedRidgeGeometry = null;
        _cachedRiverGeometry = null;
    }

    private void EnsureTerrainCache(Rect rect)
    {
        if (_cachedRidgeGeometry != null && _cachedRiverGeometry != null && _cachedTerrainRect == rect)
            return;

        _cachedTerrainRect = rect;

        var ridge = new StreamGeometry();
        using (var ctx = ridge.Open())
        {
            ctx.BeginFigure(new Point(rect.Left + 20, rect.Top + rect.Height * 0.28), false, false);
            ctx.PolyLineTo(
                [
                    new Point(rect.Left + rect.Width * 0.22, rect.Top + rect.Height * 0.18),
                    new Point(rect.Left + rect.Width * 0.42, rect.Top + rect.Height * 0.24),
                    new Point(rect.Left + rect.Width * 0.58, rect.Top + rect.Height * 0.15),
                    new Point(rect.Left + rect.Width * 0.84, rect.Top + rect.Height * 0.2),
                    new Point(rect.Right - 22, rect.Top + rect.Height * 0.12)
                ],
                true,
                true);
        }
        ridge.Freeze();
        _cachedRidgeGeometry = ridge;

        var river = new StreamGeometry();
        using (var ctx = river.Open())
        {
            ctx.BeginFigure(new Point(rect.Left + rect.Width * 0.13, rect.Bottom - 16), false, false);
            ctx.BezierTo(
                new Point(rect.Left + rect.Width * 0.18, rect.Top + rect.Height * 0.68),
                new Point(rect.Left + rect.Width * 0.36, rect.Top + rect.Height * 0.54),
                new Point(rect.Left + rect.Width * 0.48, rect.Top + rect.Height * 0.48),
                true,
                true);
            ctx.BezierTo(
                new Point(rect.Left + rect.Width * 0.64, rect.Top + rect.Height * 0.4),
                new Point(rect.Left + rect.Width * 0.74, rect.Top + rect.Height * 0.22),
                new Point(rect.Right - 28, rect.Top + rect.Height * 0.08),
                true,
                true);
        }
        river.Freeze();
        _cachedRiverGeometry = river;
    }
}
