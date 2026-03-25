using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.Windows;
using System.Windows.Input;
using DEADSKY.Core.Physics;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.App.Controls;

/// <summary>
/// The main radar display. Renders at 60fps using SkiaSharp GPU-accelerated drawing.
/// Shows: sweep line, range rings, contact blips, tracks, missile trails, ECM effects.
/// CRT phosphor persistence effect: contacts glow brightest at sweep moment, fade slowly.
/// </summary>
public class RadarDisplay : SKElement
{
    // ── Dependency properties ─────────────────────────────────────────
    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(nameof(Snapshot), typeof(SimulationSnapshot),
            typeof(RadarDisplay), new PropertyMetadata(null, OnSnapshotChanged));

    public SimulationSnapshot? Snapshot
    {
        get => (SimulationSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public static readonly DependencyProperty SelectedTrackIdProperty =
        DependencyProperty.Register(nameof(SelectedTrackId), typeof(string),
            typeof(RadarDisplay), new PropertyMetadata(null));

    public string? SelectedTrackId
    {
        get => (string?)GetValue(SelectedTrackIdProperty);
        set => SetValue(SelectedTrackIdProperty, value);
    }

    // Events
    public event Action<string>? TrackClicked;   // Track ID
    public event Action<double, double>? RadarClicked; // bearing, range

    // State
    private SimulationSnapshot? _snapshot;
    private float _displayRadius;
    private SKPoint _center;

    // Phosphor persistence: track last-seen times for fade
    private readonly Dictionary<string, (float px, float py, double sweepTime)> _phosphorTrails = new();

    // Colors (cached SkiaSharp paints)
    private readonly SKPaint _bgPaint = new() { Color = new SKColor(5, 15, 5) };
    private readonly SKPaint _gridPaint = new() { Color = new SKColor(0, 80, 0, 180), IsStroke = true, StrokeWidth = 0.5f };
    private readonly SKPaint _sweepPaint = new() { Color = new SKColor(0, 255, 0, 200), IsStroke = true, StrokeWidth = 2f };
    private readonly SKPaint _ringLabelPaint = new()
    {
        Color = new SKColor(0, 150, 0), IsAntialias = true,
        TextSize = 10, Typeface = SKTypeface.FromFamilyName("Consolas")
    };
    private readonly SKPaint _hostilePaint = new() { Color = new SKColor(255, 50, 50), IsAntialias = true };
    private readonly SKPaint _friendlyPaint = new() { Color = new SKColor(50, 150, 255), IsAntialias = true };
    private readonly SKPaint _unknownPaint = new() { Color = new SKColor(255, 255, 50), IsAntialias = true };
    private readonly SKPaint _missilePaint = new() { Color = new SKColor(255, 100, 0), IsAntialias = true };
    private readonly SKPaint _trackLabelPaint = new()
    {
        Color = new SKColor(0, 200, 0), IsAntialias = true,
        TextSize = 9, Typeface = SKTypeface.FromFamilyName("Consolas")
    };
    private readonly SKPaint _selectedPaint = new()
    {
        Color = new SKColor(0, 255, 0), IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true
    };
    private readonly SKPaint _vectorPaint = new()
    {
        Color = new SKColor(0, 180, 0, 150), IsStroke = true, StrokeWidth = 1f
    };
    private readonly SKPaint _trailPaint = new() { IsAntialias = true };
    private readonly SKPaint _scanlinePaint = new() { Color = new SKColor(0, 255, 0, 12) };
    private readonly SKPaint _vignettePaint = new() { IsAntialias = true };

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        _displayRadius = Math.Min(info.Width, info.Height) / 2f - 4f;
        _center = new SKPoint(info.Width / 2f, info.Height / 2f);

        canvas.Clear(new SKColor(5, 15, 5));

        // Clip to radar circle
        canvas.Save();
        using var clipPath = new SKPath();
        clipPath.AddCircle(_center.X, _center.Y, _displayRadius);
        canvas.ClipPath(clipPath);

        // ── Draw layers back to front ─────────────────────────────────

        DrawBackground(canvas, info);
        DrawRangeRings(canvas);
        DrawBearingMarkers(canvas);
        DrawEngagementZone(canvas);
        DrawEcmEffects(canvas);
        DrawPhosphorTrails(canvas);
        DrawContacts(canvas);
        DrawMissiles(canvas);
        DrawSweepLine(canvas);
        DrawScanLines(canvas, info);
        DrawVignette(canvas);

        canvas.Restore();

        DrawHUD(canvas, info);
    }

    private void DrawBackground(SKCanvas canvas, SKImageInfo info)
    {
        canvas.DrawCircle(_center, _displayRadius, _bgPaint);

        // Subtle noise texture (simple pattern approximation)
        _gridPaint.Color = new SKColor(0, 60, 0, 60);
    }

    private void DrawRangeRings(SKCanvas canvas)
    {
        double rangeNm = _snapshot?.RadarRangeNm ?? 80;
        int ringCount = 4;

        for (int i = 1; i <= ringCount; i++)
        {
            float fraction = (float)i / ringCount;
            float radius = _displayRadius * fraction;
            canvas.DrawCircle(_center, radius, _gridPaint);

            double ringRange = rangeNm * fraction;
            string label = $"{ringRange:F0}";
            float labelX = _center.X + 4;
            float labelY = _center.Y - radius + 12;
            canvas.DrawText(label, labelX, labelY, _ringLabelPaint);
        }

        // Crosshair
        _gridPaint.Color = new SKColor(0, 80, 0, 100);
        canvas.DrawLine(_center.X, _center.Y - _displayRadius, _center.X, _center.Y + _displayRadius, _gridPaint);
        canvas.DrawLine(_center.X - _displayRadius, _center.Y, _center.X + _displayRadius, _center.Y, _gridPaint);
    }

    private void DrawBearingMarkers(SKCanvas canvas)
    {
        _ringLabelPaint.Color = new SKColor(0, 150, 0);
        var (labels, angles) = (new[] { "N", "045", "E", "135", "S", "225", "W", "315" },
                                  new[] { 0, 45, 90, 135, 180, 225, 270, 315 });

        for (int i = 0; i < labels.Length; i++)
        {
            double rad = angles[i] * Math.PI / 180.0;
            float x = _center.X + (float)Math.Sin(rad) * (_displayRadius - 12);
            float y = _center.Y - (float)Math.Cos(rad) * (_displayRadius - 12);
            canvas.DrawText(labels[i], x - 8, y + 4, _ringLabelPaint);

            // Tick mark
            float tx1 = _center.X + (float)Math.Sin(rad) * (_displayRadius - 6);
            float ty1 = _center.Y - (float)Math.Cos(rad) * (_displayRadius - 6);
            float tx2 = _center.X + (float)Math.Sin(rad) * _displayRadius;
            float ty2 = _center.Y - (float)Math.Cos(rad) * _displayRadius;
            _gridPaint.Color = new SKColor(0, 120, 0);
            canvas.DrawLine(tx1, ty1, tx2, ty2, _gridPaint);
        }
    }

    private void DrawEngagementZone(SKCanvas canvas)
    {
        double rangeNm = _snapshot?.RadarRangeNm ?? 80;
        var battery = _snapshot?.Battery;
        if (battery == null) return;

        float maxRangeRadius = _displayRadius * (float)(battery.MissileMaxRangeNm / rangeNm);
        float minRangeRadius = _displayRadius * (float)(battery.MissileMinRangeNm / rangeNm);

        using var mezPaint = new SKPaint
        {
            Color = new SKColor(0, 100, 0, 30),
            IsStroke = false
        };
        canvas.DrawCircle(_center, maxRangeRadius, mezPaint);

        mezPaint.Color = new SKColor(0, 180, 0, 50);
        mezPaint.IsStroke = true;
        mezPaint.StrokeWidth = 1;
        canvas.DrawCircle(_center, maxRangeRadius, mezPaint);

        // Min range exclusion zone
        mezPaint.Color = new SKColor(255, 100, 0, 20);
        mezPaint.IsStroke = false;
        canvas.DrawCircle(_center, minRangeRadius, mezPaint);
    }

    private void DrawEcmEffects(SKCanvas canvas)
    {
        if (_snapshot == null) return;
        double rangeNm = _snapshot.RadarRangeNm;

        foreach (var ecm in _snapshot.ActiveEcmEffects)
        {
            // Draw jamming strobe — a noise sector
            double rad = ecm.BearingDeg * Math.PI / 180.0;
            float spread = 25f * (float)ecm.StrengthNormalized;

            using var jammingPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, (byte)(30 * ecm.StrengthNormalized)),
                IsAntialias = true
            };

            using var path = new SKPath();
            path.MoveTo(_center);
            path.ArcTo(
                new SKRect(_center.X - _displayRadius, _center.Y - _displayRadius,
                           _center.X + _displayRadius, _center.Y + _displayRadius),
                (float)ecm.BearingDeg - 90f - spread,
                spread * 2, false);
            path.Close();
            canvas.DrawPath(path, jammingPaint);

            // Label
            float lx = _center.X + (float)Math.Sin(rad) * _displayRadius * 0.7f;
            float ly = _center.Y - (float)Math.Cos(rad) * _displayRadius * 0.7f;
            _trackLabelPaint.Color = new SKColor(255, 255, 255, 200);
            canvas.DrawText("MUSIC", lx, ly, _trackLabelPaint);
        }
    }

    private void DrawPhosphorTrails(SKCanvas canvas)
    {
        if (_snapshot == null) return;
        double now = DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        double rangeNm = _snapshot.RadarRangeNm;

        foreach (var track in _snapshot.FirmTracks)
        {
            if (track.History.Count < 2) continue;

            for (int i = 0; i < track.History.Count - 1; i++)
            {
                double age = (DateTime.UtcNow - track.History[i].Time).TotalSeconds;
                byte alpha = (byte)Math.Max(0, 180 - age * 8);
                if (alpha == 0) continue;

                var (px1, py1) = CoordinateSystem.ToRadarScreen(track.History[i].Position, rangeNm, _displayRadius);
                var (px2, py2) = CoordinateSystem.ToRadarScreen(track.History[i + 1].Position, rangeNm, _displayRadius);

                _trailPaint.Color = GetTrackColor(track) with { Alpha = alpha };
                _trailPaint.IsStroke = true;
                _trailPaint.StrokeWidth = 1.5f;
                canvas.DrawLine(
                    _center.X + px1, _center.Y + py1,
                    _center.X + px2, _center.Y + py2, _trailPaint);
            }
        }
    }

    private void DrawContacts(SKCanvas canvas)
    {
        if (_snapshot == null) return;
        double rangeNm = _snapshot.RadarRangeNm;

        foreach (var track in _snapshot.AllTracks)
        {
            var (px, py) = CoordinateSystem.ToRadarScreen(track.Position, rangeNm, _displayRadius);
            float cx = _center.X + px;
            float cy = _center.Y + py;

            // Skip if outside display
            if (Math.Sqrt(px * px + py * py) > _displayRadius - 8) continue;

            var paint = GetTrackPaint(track);
            float blipSize = track.Quality == TrackQuality.Fading ? 3f : 5f;

            // Draw symbol based on classification
            DrawTrackSymbol(canvas, cx, cy, blipSize, track, paint);

            // Selected highlight
            if (track.TrackId == SelectedTrackId)
            {
                canvas.DrawCircle(cx, cy, blipSize + 4, _selectedPaint);
                canvas.DrawCircle(cx, cy, blipSize + 8, _selectedPaint with
                {
                    Color = _selectedPaint.Color with { Alpha = 80 }
                });
            }

            // Velocity vector
            if (track.SpeedMps > 10)
            {
                double headingRad = track.HeadingDeg * Math.PI / 180.0;
                float vecLen = (float)(track.SpeedMps * 30 / CoordinateSystem.NmToMeters(rangeNm) * _displayRadius);
                vecLen = Math.Clamp(vecLen, 5, 40);
                float vx = cx + (float)Math.Sin(headingRad) * vecLen;
                float vy = cy - (float)Math.Cos(headingRad) * vecLen;
                canvas.DrawLine(cx, cy, vx, vy, _vectorPaint);
            }

            // Track ID label
            _trackLabelPaint.Color = GetTrackColor(track) with { Alpha = 200 };
            canvas.DrawText(track.TrackId.Replace("TRK-", ""), cx + 7, cy - 3, _trackLabelPaint);

            // Altitude (small)
            _trackLabelPaint.TextSize = 8;
            _trackLabelPaint.Color = new SKColor(0, 150, 0, 150);
            canvas.DrawText($"FL{track.AltitudeFt / 100:F0}", cx + 7, cy + 7, _trackLabelPaint);
            _trackLabelPaint.TextSize = 9;
        }
    }

    private void DrawMissiles(SKCanvas canvas)
    {
        if (_snapshot == null) return;
        double rangeNm = _snapshot.RadarRangeNm;

        foreach (var missile in _snapshot.ActiveMissiles)
        {
            var (px, py) = CoordinateSystem.ToRadarScreen(missile.Position, rangeNm, _displayRadius);
            float cx = _center.X + px;
            float cy = _center.Y + py;

            // Small triangle for missile
            float s = 4f;
            using var path = new SKPath();
            double headingRad = missile.HeadingDeg * Math.PI / 180.0;
            float tipX = cx + (float)Math.Sin(headingRad) * s * 2;
            float tipY = cy - (float)Math.Cos(headingRad) * s * 2;
            float l1X = cx + (float)Math.Cos(headingRad) * s;
            float l1Y = cy + (float)Math.Sin(headingRad) * s;
            float l2X = cx - (float)Math.Cos(headingRad) * s;
            float l2Y = cy - (float)Math.Sin(headingRad) * s;
            path.MoveTo(tipX, tipY);
            path.LineTo(l1X, l1Y);
            path.LineTo(l2X, l2Y);
            path.Close();

            _missilePaint.IsStroke = false;
            canvas.DrawPath(path, _missilePaint);

            // Missile trail
            _trailPaint.Color = new SKColor(255, 100, 0, 100);
            _trailPaint.IsStroke = true;
            _trailPaint.StrokeWidth = 1f;
            double backRad = (missile.HeadingDeg + 180) * Math.PI / 180.0;
            canvas.DrawLine(cx, cy,
                cx + (float)Math.Sin(backRad) * 15,
                cy - (float)Math.Cos(backRad) * 15, _trailPaint);
        }
    }

    private void DrawSweepLine(SKCanvas canvas)
    {
        if (_snapshot == null) return;
        double sweepRad = _snapshot.RadarSweepAngle * Math.PI / 180.0;

        float sweepX = _center.X + (float)Math.Sin(sweepRad) * _displayRadius;
        float sweepY = _center.Y - (float)Math.Cos(sweepRad) * _displayRadius;

        // Bright leading edge
        _sweepPaint.Color = new SKColor(0, 255, 0, 220);
        _sweepPaint.StrokeWidth = 2;
        canvas.DrawLine(_center.X, _center.Y, sweepX, sweepY, _sweepPaint);

        // Fading arc behind sweep (phosphor persistence)
        using var arcPaint = new SKPaint
        {
            Color = new SKColor(0, 255, 0, 8),
            IsStroke = false,
            IsAntialias = true
        };

        float startAngle = (float)_snapshot.RadarSweepAngle - 90f - 45f;
        using var sweepPath = new SKPath();
        sweepPath.MoveTo(_center);
        sweepPath.ArcTo(
            new SKRect(_center.X - _displayRadius, _center.Y - _displayRadius,
                       _center.X + _displayRadius, _center.Y + _displayRadius),
            startAngle, 45f, false);
        sweepPath.Close();
        canvas.DrawPath(sweepPath, arcPaint);
    }

    private void DrawScanLines(SKCanvas canvas, SKImageInfo info)
    {
        _scanlinePaint.Color = new SKColor(0, 255, 0, 10);
        for (int y = 0; y < info.Height; y += 3)
            canvas.DrawLine(0, y, info.Width, y, _scanlinePaint);
    }

    private void DrawVignette(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true };
        using var shader = SKShader.CreateRadialGradient(
            _center, _displayRadius,
            new[] { SKColors.Transparent, new SKColor(0, 0, 0, 100) },
            new[] { 0.7f, 1.0f },
            SKShaderTileMode.Clamp);
        paint.Shader = shader;
        canvas.DrawCircle(_center, _displayRadius, paint);
    }

    private void DrawHUD(SKCanvas canvas, SKImageInfo info)
    {
        if (_snapshot?.Battery == null) return;
        using var hudPaint = new SKPaint
        {
            Color = new SKColor(0, 200, 0),
            IsAntialias = true,
            TextSize = 10,
            Typeface = SKTypeface.FromFamilyName("Consolas")
        };

        var battery = _snapshot.Battery;
        float y = info.Height - 30f;

        canvas.DrawText($"RNG: {_snapshot.RadarRangeNm:F0}nm  MODE: {_snapshot.RadarMode,-12}  TRACKS: {_snapshot.AllTracks.Count}",
            8, y, hudPaint);
        canvas.DrawText($"SWEEP: {_snapshot.RadarSweepAngle:F0}°  KILLS: {battery.ConfirmedKills}  MSLS: {battery.ReserveMissiles}",
            8, y + 14, hudPaint);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void DrawTrackSymbol(SKCanvas canvas, float cx, float cy, float size,
        TrackFile track, SKPaint paint)
    {
        switch (track.Classification)
        {
            case TrackClassification.Hostile:
            case TrackClassification.AssumedHostile:
                // Filled diamond
                using (var path = new SKPath())
                {
                    path.MoveTo(cx, cy - size);
                    path.LineTo(cx + size, cy);
                    path.LineTo(cx, cy + size);
                    path.LineTo(cx - size, cy);
                    path.Close();
                    canvas.DrawPath(path, paint);
                }
                break;
            case TrackClassification.Friendly:
                // Circle
                canvas.DrawCircle(cx, cy, size, paint);
                break;
            case TrackClassification.Civilian:
                // Square
                canvas.DrawRect(cx - size, cy - size, size * 2, size * 2, paint);
                break;
            default:
                // Square outline (unknown)
                paint.IsStroke = true;
                paint.StrokeWidth = 1.5f;
                canvas.DrawRect(cx - size, cy - size, size * 2, size * 2, paint);
                paint.IsStroke = false;
                break;
        }
    }

    private SKColor GetTrackColor(TrackFile track)
    {
        return track.Classification switch
        {
            TrackClassification.Hostile or TrackClassification.AssumedHostile => new SKColor(255, 50, 50),
            TrackClassification.Friendly => new SKColor(50, 150, 255),
            TrackClassification.Civilian or TrackClassification.Neutral => new SKColor(50, 255, 50),
            _ => new SKColor(255, 255, 50)
        };
    }

    private SKPaint GetTrackPaint(TrackFile track)
    {
        return track.Classification switch
        {
            TrackClassification.Hostile or TrackClassification.AssumedHostile => _hostilePaint,
            TrackClassification.Friendly => _friendlyPaint,
            _ => _unknownPaint
        };
    }

    // ── Mouse interaction ──────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        float dx = (float)pos.X - _center.X;
        float dy = (float)pos.Y - _center.Y;

        if (_snapshot == null) return;
        double rangeNm = _snapshot.RadarRangeNm;

        // Find nearest track within 15px
        string? nearest = null;
        float minDist = 15f;

        foreach (var track in _snapshot.AllTracks)
        {
            var (px, py) = CoordinateSystem.ToRadarScreen(track.Position, rangeNm, _displayRadius);
            float tdx = dx - px;
            float tdy = dy - py;
            float dist = (float)Math.Sqrt(tdx * tdx + tdy * tdy);
            if (dist < minDist) { minDist = dist; nearest = track.TrackId; }
        }

        if (nearest != null)
            TrackClicked?.Invoke(nearest);
        else
        {
            // Click on empty space — report bearing/range
            double bearingDeg = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
            double clickRange = Math.Sqrt(dx * dx + dy * dy) / _displayRadius * rangeNm;
            RadarClicked?.Invoke(bearingDeg, clickRange);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_snapshot == null) return;
        double current = _snapshot.RadarRangeNm;
        double newRange = e.Delta > 0 ? current * 0.75 : current * 1.33;
        newRange = Math.Clamp(newRange, 10, 240);
        // Raise event for ViewModel to handle
        RangeChanged?.Invoke(newRange);
    }

    public event Action<double>? RangeChanged;

    private static void OnSnapshotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RadarDisplay rd)
        {
            rd._snapshot = e.NewValue as SimulationSnapshot;
            rd.InvalidateVisual();
        }
    }
}
