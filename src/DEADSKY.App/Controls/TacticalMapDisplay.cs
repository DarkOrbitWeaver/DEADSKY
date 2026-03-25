using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.App.Controls;

public class TacticalMapDisplay : FrameworkElement
{
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

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 16, 12)), null, bounds);

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
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 28, 21)), new Pen(new SolidColorBrush(Color.FromRgb(56, 84, 63)), 1), rect);
    }

    private static void DrawTerrain(DrawingContext dc, Rect rect)
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

    private static void DrawGrid(DrawingContext dc, Rect rect)
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

    private static void DrawDefenseRings(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        var outer = GetMapPoint(rect, Vec2.Zero, snapshot, 180);
        var rangeFactor = rect.Width / 2.4 / 180;
        var maxRing = snapshot.Battery?.MissileMaxRangeNm ?? 18;
        var radarRing = snapshot.RadarRangeNm;

        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 220, 120)), 1), outer, radarRing * rangeFactor, radarRing * rangeFactor);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(24, 0, 160, 70)), new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 220, 120)), 1), outer, maxRing * rangeFactor, maxRing * rangeFactor);
    }

    private static void DrawLandmarks(DrawingContext dc, Rect rect)
    {
        DrawLabel(dc, "SABLE RIDGE", new Point(rect.Left + rect.Width * 0.46, rect.Top + rect.Height * 0.12), Brushes.DarkKhaki, 12, FontWeights.Bold);
        DrawLabel(dc, "VANTA RIVER", new Point(rect.Left + rect.Width * 0.57, rect.Top + rect.Height * 0.42), Brushes.SteelBlue, 11, FontWeights.SemiBold);
        DrawLabel(dc, "KOVRAN DEPOT", new Point(rect.Left + rect.Width * 0.54, rect.Top + rect.Height * 0.62), Brushes.LightGoldenrodYellow, 11, FontWeights.Bold);
        DrawLabel(dc, "ASHEN FARMS", new Point(rect.Left + rect.Width * 0.2, rect.Top + rect.Height * 0.78), Brushes.OliveDrab, 10, FontWeights.Normal);
    }

    private void DrawEntities(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        var batteryPoint = GetMapPoint(rect, Vec2.Zero, snapshot, 180);
        DrawBattery(dc, batteryPoint);

        foreach (var track in snapshot.AllTracks.OrderByDescending(t => t.ThreatLevel))
        {
            var point = GetMapPoint(rect, track.Position, snapshot, 180);
            var fill = track.Classification switch
            {
                TrackClassification.Hostile or TrackClassification.AssumedHostile => new SolidColorBrush(Color.FromRgb(214, 73, 73)),
                TrackClassification.Friendly => new SolidColorBrush(Color.FromRgb(74, 152, 224)),
                _ => new SolidColorBrush(Color.FromRgb(219, 206, 78))
            };

            DrawTrack(dc, point, fill, track.TrackId == SelectedTrackId);
            DrawLabel(dc, $"{track.TrackDesignation} {track.TrackId}", new Point(point.X + 8, point.Y - 4), fill, 10, FontWeights.SemiBold);
            DrawLabel(dc, $"{track.RangeNm:0.0}nm / FL{track.AltitudeFt / 100:0}", new Point(point.X + 8, point.Y + 10), Brushes.Gainsboro, 9, FontWeights.Normal);
        }
    }

    private static void DrawMissiles(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 64)), 1.6);
        foreach (var missile in snapshot.ActiveMissiles)
        {
            var point = GetMapPoint(rect, missile.Position, snapshot, 180);
            var heading = CoordinateSystem.DegToRad(missile.HeadingDeg);
            var tail = new Point(point.X - Math.Sin(heading) * 10, point.Y + Math.Cos(heading) * 10);
            dc.DrawLine(pen, tail, point);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 180, 110)), null, point, 3.2, 3.2);
        }
    }

    private static void DrawLegend(DrawingContext dc, Rect rect, SimulationSnapshot snapshot)
    {
        DrawLabel(dc, $"SECTOR 7A // {snapshot.GameTimeString}", new Point(rect.Left + 10, rect.Top + 10), Brushes.Gainsboro, 12, FontWeights.Bold);
        DrawLabel(dc, $"Hostiles {snapshot.HostileTracks.Count}  |  Missiles {snapshot.ActiveMissiles.Count}  |  Mode {snapshot.RadarMode}", new Point(rect.Left + 10, rect.Top + 28), Brushes.LightGreen, 10, FontWeights.Normal);
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
        DrawLabel(dc, "ALPHA BATTERY", new Point(point.X + 10, point.Y - 16), Brushes.LightGreen, 10, FontWeights.Bold);
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

    private static Point GetMapPoint(Rect rect, Vec2 position, SimulationSnapshot snapshot, double mapRangeNm)
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
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);

        dc.DrawText(formatted, point);
    }
}
