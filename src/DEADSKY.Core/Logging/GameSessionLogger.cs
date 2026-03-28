using System.Collections.Concurrent;
using System.Text;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Logging;

/// <summary>
/// Raw session data logger for AI improvement.
///
/// Two files per session:
///   session_TIMESTAMP.events.log  — every event, one line, key=val format, grep-friendly
///   session_TIMESTAMP.path.csv    — position sample per entity every PATH_INTERVAL_SEC seconds
///                                   one row per sample, append-only, full flight path reconstruction
///
/// Design goals:
///   - Every line is self-contained and grep-able by entity ID, track ID, or missile ID
///   - Behavior changes include the trigger reason (missile_inbound, radar_lock, bingo_fuel, ai_command, etc.)
///   - Path CSV has consistent time intervals so flight paths can be reconstructed exactly
///   - Engagement chain is linked by missile_id across FIRE → GUIDANCE → RESULT lines
///   - Nothing is summarized or omitted — this is raw data for AI training/debugging
/// </summary>
public sealed class GameSessionLogger : IDisposable
{
    // ── Config ─────────────────────────────────────────────────────────
    private const double StateCheckIntervalSec = 5.0;  // delta check frequency
    private const double PathSampleIntervalSec = 3.0;  // path CSV sample frequency
    private const double PosDeltaNm            = 0.4;  // position delta threshold for event log
    private const double HdgDeltaDeg           = 8.0;
    private const double SpdDeltaKts           = 15.0;

    // ── File paths ─────────────────────────────────────────────────────
    private readonly string _eventsPath;
    private readonly string _pathCsvPath;

    // ── Queues ─────────────────────────────────────────────────────────
    private readonly ConcurrentQueue<string> _eventQueue = new();
    private readonly ConcurrentQueue<string> _pathQueue  = new();
    private readonly object _fileLock = new();
    private readonly Thread _writerThread;
    private volatile bool _stop;

    // ── Time ───────────────────────────────────────────────────────────
    private double _gameTimeSec;
    private double _lastStateCheckSec = -999;
    private double _lastPathSampleSec = -999;

    // ── Per-entity last state (for delta detection) ────────────────────
    private readonly Dictionary<string, AcState>  _prevAc  = new();
    private readonly Dictionary<string, TrkState> _prevTrk = new();

    // ── Short stable IDs (entity GUID → short label) ──────────────────
    // Avoids truncation collisions. Labels are AC01, AC02, MSL01, etc.
    private readonly Dictionary<string, string> _shortIds = new();
    private int _acCounter, _mslCounter;

    // ── Singleton ──────────────────────────────────────────────────────
    private static GameSessionLogger? _instance;
    public static GameSessionLogger? Current => _instance;

    public static GameSessionLogger StartSession(string? folder = null)
    {
        _instance?.Dispose();
        _instance = new GameSessionLogger(folder);
        return _instance;
    }

    // ── Constructor ────────────────────────────────────────────────────
    private GameSessionLogger(string? folder)
    {
        folder ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DEADSKY", "Sessions");
        Directory.CreateDirectory(folder);

        var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _eventsPath  = Path.Combine(folder, $"session_{ts}.events.log");
        _pathCsvPath = Path.Combine(folder, $"session_{ts}.path.csv");

        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "SessionLogger" };
        _writerThread.Start();

        // Events header
        Evt($"# DEADSKY RAW SESSION LOG  started={ts}");
        Evt($"# os={Environment.OSVersion}  dotnet={Environment.Version}");
        Evt("# Each line: [wall][T+game] CATEGORY  key=val key=val ...");
        Evt("# Entity IDs: AC01/AC02... = hostile aircraft  MSL01... = SAM missiles  TRK-XXXX = radar tracks");
        Evt("# Behavior triggers: missile_inbound | radar_lock | hard_lock | bingo_fuel | ai_cmd | feint_range | sead_arm | inner_ring | toc_orbit");
        Evt("SESSION_START");

        // Path CSV header
        Path_("game_time,entity_id,designation,type,affiliation,pos_x_nm,pos_y_nm,rng_nm,brg_deg,alt_ft,spd_kts,hdg_deg,behavior,ecm,fuel_kg,radar_lock,hard_lock,missile_inbound,chaff_rem,flare_rem");
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void OnTick(double gameTimeSec, SimulationSnapshot snapshot)
    {
        _gameTimeSec = gameTimeSec;

        if (gameTimeSec - _lastPathSampleSec >= PathSampleIntervalSec)
        {
            _lastPathSampleSec = gameTimeSec;
            SamplePaths(snapshot);
        }

        if (gameTimeSec - _lastStateCheckSec >= StateCheckIntervalSec)
        {
            _lastStateCheckSec = gameTimeSec;
            CheckAcDeltas(snapshot);
            CheckTrkDeltas(snapshot);
        }
    }

    public void OnScenarioLoaded(ScenarioDefinition scenario, bool realisticMode)
    {
        Evt($"SCENARIO  name=\"{scenario.Name}\" realistic={realisticMode} duration_min={scenario.DurationMinutes}");
        Evt($"BATTERY   callsign={scenario.PlayerBattery?.Callsign} missile={scenario.PlayerBattery?.MissileType} " +
            $"pk={scenario.PlayerBattery?.MissilePk:F2} eng_range={scenario.PlayerBattery?.EngagementRangeNm}nm " +
            $"radar={scenario.PlayerBattery?.RadarRangeNm}nm launchers={scenario.PlayerBattery?.Launchers} " +
            $"reserve={scenario.PlayerBattery?.ReserveMissiles} roe={scenario.PlayerBattery?.InitialROE} alert={scenario.PlayerBattery?.InitialAlert}");

        if (scenario.Weather is { } wx)
            Evt($"WEATHER   desc=\"{wx.Description}\" vis={wx.VisibilityNm}nm ceil={wx.CloudCeilingFt}ft " +
                $"precip={wx.PrecipitationMmHr}mm/hr wind={wx.WindSpeedKts}kts/{wx.WindDirectionDeg}deg");

        if (scenario.EnemyForces?.Waves != null)
            foreach (var w in scenario.EnemyForces.Waves)
            {
                Evt($"WAVE      t={w.TimeMinutes}min pkg=\"{w.PackageName}\" role={w.PackageRole} target={w.TargetObjectiveId}");
                foreach (var ac in w.Aircraft)
                    Evt($"WAVE_AC   type={ac.Type} count={ac.Count} brg={ac.SpawnBearingDeg:0}deg " +
                        $"rng={ac.SpawnRangeNm:0}nm alt={ac.SpawnAltitudeFt:0}ft hdg={ac.SpawnHeadingDeg:0}deg " +
                        $"spd={ac.SpawnSpeedKts:0}kts beh={ac.InitialBehavior} agg={ac.Aggressiveness:F2}");
            }

        if (scenario.SectorMap?.Objectives != null)
            foreach (var o in scenario.SectorMap.Objectives)
                Evt($"OBJECTIVE id={o.Id} name=\"{o.Name}\" importance={o.Importance} brg={o.BearingDeg:0}deg rng={o.RangeNm:0}nm");
    }

    public void OnRadioMessage(RadioMessage msg)
    {
        string dir = msg.SpeakerAffiliation == SpeakerAffiliation.Player ? ">>" : "<<";
        Evt($"RADIO     dir={dir} ch={msg.Channel} pri={msg.Priority} type={msg.Type} " +
            $"from=\"{msg.SenderCallsign}\" to=\"{msg.RecipientCallsign ?? "ALL"}\" msg=\"{msg.Content}\"");
    }

    public void OnPlayerAction(string action, string? detail = null)
    {
        Evt(detail != null
            ? $"PLAYER    action={action} detail=\"{detail}\""
            : $"PLAYER    action={action}");
    }

    public void OnSupportRequest(string requestor, string type, bool accepted, string summary, double eta)
    {
        Evt($"SUPPORT   from={requestor} type={type} accepted={accepted} eta={eta:0}s summary=\"{summary}\"");
    }

    /// <summary>
    /// Called when a weapon is fired. missile_id links this to the eventual ENGAGE result.
    /// </summary>
    public void OnWeaponFired(string trackId, string designation, string missileType,
                               double rangeNm, double altFt, string missileId = "")
    {
        Evt($"FIRE      track={trackId} designation=\"{designation}\" weapon={missileType} " +
            $"range={rangeNm:F1}nm alt={altFt:F0}ft missile_id={missileId}");
    }

    /// <summary>
    /// Called when an engagement resolves. missile_id links back to FIRE line.
    /// </summary>
    public void OnEngagementResult(string trackId, string result, string detail, string missileId = "")
    {
        Evt($"ENGAGE    track={trackId} result={result} missile_id={missileId} detail=\"{detail}\"");
    }

    /// <summary>
    /// Called when an aircraft behavior changes. trigger explains WHY it changed — this is
    /// the most important field for debugging AI decisions.
    /// </summary>
    public void OnBehaviorChanged(string entityId, string designation,
                                   string oldBehavior, string newBehavior, string trigger)
    {
        string sid = ShortId(entityId, "AC");
        Evt($"BEHAVIOR  id={sid} designation=\"{designation}\" " +
            $"from={oldBehavior} to={newBehavior} trigger={trigger}");
    }

    public void OnAlertLevelChanged(string from, string to, string authority)
    {
        Evt($"ALERT     from={from} to={to} authority=\"{authority}\"");
    }

    public void OnRoeChanged(string from, string to, string authority)
    {
        Evt($"ROE       from={from} to={to} authority=\"{authority}\"");
    }

    public void OnTrackInitiated(TrackFile track)
    {
        Evt($"TRACK+    id={track.TrackId} designation=\"{track.TrackDesignation}\" " +
            $"brg={track.BearingDeg:F0}deg rng={track.RangeNm:F1}nm alt={track.AltitudeFt:F0}ft " +
            $"spd={track.SpeedKts:F0}kts hdg={track.HeadingDeg:F0}deg class={track.Classification} threat={track.ThreatLevel:F2}");
    }

    public void OnTrackDropped(TrackFile track)
    {
        Evt($"TRACK-    id={track.TrackId} designation=\"{track.TrackDesignation}\" coast={track.TimeSinceLastDetectionSec:F0}s");
        _prevTrk.Remove(track.TrackId);
    }

    public void OnAiAction(string agent, string action, string? detail = null)
    {
        Evt(detail != null
            ? $"AI        agent={agent} action={action} detail=\"{detail}\""
            : $"AI        agent={agent} action={action}");
    }

    public void OnGameEvent(string type, string detail)
    {
        Evt($"EVENT     type={type} detail=\"{detail}\"");
    }

    public void OnMissionEnd(string outcome, SimulationSnapshot snap)
    {
        Evt($"MISSION_END outcome={outcome} time=\"{snap.GameTimeString}\" " +
            $"kills={snap.Battery?.ConfirmedKills} fired={snap.Battery?.MissilesFired} " +
            $"misses={snap.Battery?.Misses} alert={snap.Battery?.AlertLevel} roe={snap.Battery?.ROE} " +
            $"tracks={snap.AllTracks.Count} hostile={snap.HostileTracks.Count}");
    }

    // ── Path sampling ──────────────────────────────────────────────────
    // Every PATH_INTERVAL_SEC seconds, append one CSV row per active entity.
    // This gives full flight path reconstruction at 3-second resolution.

    private void SamplePaths(SimulationSnapshot snap)
    {
        foreach (var ac in snap.HostileAircraft)
        {
            string sid = ShortId(ac.Id, "AC");
            double rng = ac.Position.Length / 1852.0;
            double brg = Math.Atan2(ac.Position.X, ac.Position.Y) * 180.0 / Math.PI;
            if (brg < 0) brg += 360;
            Path_($"{_gameTimeSec:F1},{sid},\"{ac.Designation}\",AC,{ac.Affiliation}," +
                  $"{ac.Position.X / 1852.0:F2},{ac.Position.Y / 1852.0:F2},{rng:F2},{brg:F1}," +
                  $"{ac.AltitudeM * 3.281:F0},{ac.SpeedMps * 1.944:F0},{ac.HeadingDeg:F1}," +
                  $"{ac.CurrentBehavior},{ac.ECMActive},{ac.FuelRemainingKg:F0}," +
                  $"{ac.RadarLockDetected},{ac.HardLockDetected},{ac.MissileInbound}," +
                  $"{ac.ChaffCharges},{ac.FlareCharges}");
        }

        foreach (var m in snap.ActiveMissiles)
        {
            string sid = ShortId(m.Id, "MSL");
            double rng = m.Position.Length / 1852.0;
            double brg = Math.Atan2(m.Position.X, m.Position.Y) * 180.0 / Math.PI;
            if (brg < 0) brg += 360;
            Path_($"{_gameTimeSec:F1},{sid},\"{m.Designation}\",MSL,FRIENDLY," +
                  $"{m.Position.X / 1852.0:F2},{m.Position.Y / 1852.0:F2},{rng:F2},{brg:F1}," +
                  $"{m.AltitudeM * 3.281:F0},{m.SpeedMps * 1.944:F0},{m.HeadingDeg:F1}," +
                  $"INBOUND,false,0,false,false,false,0,0");
        }
    }

    // ── Delta detection (event log only) ──────────────────────────────

    private void CheckAcDeltas(SimulationSnapshot snap)
    {
        var activeIds = new HashSet<string>();

        foreach (var ac in snap.HostileAircraft)
        {
            activeIds.Add(ac.Id);
            string sid = ShortId(ac.Id, "AC");
            double rng = ac.Position.Length / 1852.0;
            double spd = ac.SpeedMps * 1.944;
            string beh = ac.CurrentBehavior.ToString();

            if (_prevAc.TryGetValue(ac.Id, out var prev))
            {
                bool posChg = Math.Abs(rng - prev.Rng) >= PosDeltaNm;
                bool hdgChg = HdgDelta(ac.HeadingDeg, prev.Hdg) >= HdgDeltaDeg;
                bool spdChg = Math.Abs(spd - prev.Spd) >= SpdDeltaKts;
                bool behChg = beh != prev.Beh;
                bool ecmChg = ac.ECMActive != prev.Ecm;
                bool lockChg = ac.RadarLockDetected != prev.RadarLock || ac.HardLockDetected != prev.HardLock;
                bool mslChg  = ac.MissileInbound != prev.MslInbound;

                if (!posChg && !hdgChg && !spdChg && !behChg && !ecmChg && !lockChg && !mslChg)
                    goto update;

                var parts = new List<string>();
                if (posChg)  parts.Add($"rng={rng:F1}nm brg={Math.Atan2(ac.Position.X, ac.Position.Y) * 180 / Math.PI:F0}deg");
                if (hdgChg)  parts.Add($"hdg={ac.HeadingDeg:F0}deg");
                if (spdChg)  parts.Add($"spd={spd:F0}kts");
                if (behChg)  parts.Add($"beh_from={prev.Beh} beh_to={beh}");
                if (ecmChg)  parts.Add($"ecm={ac.ECMActive}");
                if (lockChg) parts.Add($"radar_lock={ac.RadarLockDetected} hard_lock={ac.HardLockDetected}");
                if (mslChg)  parts.Add($"missile_inbound={ac.MissileInbound}");

                Evt($"AC_DELTA  id={sid} designation=\"{ac.Designation}\" alt={ac.AltitudeM * 3.281:F0}ft fuel={ac.FuelRemainingKg:F0}kg | {string.Join(" ", parts)}");
            }
            else
            {
                double brg = Math.Atan2(ac.Position.X, ac.Position.Y) * 180.0 / Math.PI;
                if (brg < 0) brg += 360;
                Evt($"AC_SPAWN  id={sid} designation=\"{ac.Designation}\" aff={ac.Affiliation} role={ac.Role} " +
                    $"rng={rng:F1}nm brg={brg:F0}deg alt={ac.AltitudeM * 3.281:F0}ft spd={spd:F0}kts " +
                    $"hdg={ac.HeadingDeg:F0}deg beh={beh} ecm_capable={ac.HasECM} arm={ac.HasARMCapability} " +
                    $"fuel_cap={ac.FuelCapacityKg:F0}kg bingo={ac.BingoFuelKg:F0}kg agg={ac.AggressivenessLevel:F2}");
            }

            update:
            _prevAc[ac.Id] = new AcState(rng, ac.HeadingDeg, spd, beh, ac.ECMActive,
                ac.RadarLockDetected, ac.HardLockDetected, ac.MissileInbound);
        }

        foreach (var gone in _prevAc.Keys.Where(k => !activeIds.Contains(k)).ToList())
        {
            Evt($"AC_GONE   id={ShortId(gone, "AC")}");
            _prevAc.Remove(gone);
        }
    }

    private void CheckTrkDeltas(SimulationSnapshot snap)
    {
        foreach (var t in snap.AllTracks)
        {
            if (_prevTrk.TryGetValue(t.TrackId, out var prev))
            {
                bool posChg    = Math.Abs(t.RangeNm - prev.Rng) >= PosDeltaNm;
                bool classChg  = t.Classification != prev.Class;
                bool threatChg = Math.Abs(t.ThreatLevel - prev.Threat) >= 0.12;
                bool qualChg   = t.Quality != prev.Qual;
                bool engChg    = t.IsBeingEngaged != prev.Engaged;
                bool hotChg    = t.IsHot != prev.Hot;

                if (!posChg && !classChg && !threatChg && !qualChg && !engChg && !hotChg)
                    goto update;

                var parts = new List<string>();
                if (posChg)    parts.Add($"rng={t.RangeNm:F1}nm brg={t.BearingDeg:F0}deg");
                if (classChg)  parts.Add($"class={t.Classification}");
                if (threatChg) parts.Add($"threat={t.ThreatLevel:F2}");
                if (qualChg)   parts.Add($"qual={t.Quality}");
                if (engChg)    parts.Add($"engaged={t.IsBeingEngaged}");
                if (hotChg)    parts.Add($"hot={t.IsHot}");

                Evt($"TRK_DELTA id={t.TrackId} designation=\"{t.TrackDesignation}\" " +
                    $"ttt={t.TimeToThreatSec:F0}s closing={t.ClosingSpeedMps:F0}m/s | {string.Join(" ", parts)}");
            }
            else
            {
                // First time seeing this track in delta checker — no event, TRACK+ already logged it
            }

            update:
            _prevTrk[t.TrackId] = new TrkState(t.RangeNm, t.Classification, t.ThreatLevel, t.Quality, t.IsBeingEngaged, t.IsHot);
        }
    }

    // ── Writer loop ────────────────────────────────────────────────────

    private void WriterLoop()
    {
        while (!_stop)
        {
            bool work = false;
            while (_eventQueue.TryDequeue(out var line))
            {
                try { lock (_fileLock) File.AppendAllText(_eventsPath, line + "\n"); }
                catch { }
                work = true;
            }
            while (_pathQueue.TryDequeue(out var row))
            {
                try { lock (_fileLock) File.AppendAllText(_pathCsvPath, row + "\n"); }
                catch { }
                work = true;
            }
            if (!work) Thread.Sleep(20);
        }
        // Final flush
        while (_eventQueue.TryDequeue(out var line))
            try { lock (_fileLock) File.AppendAllText(_eventsPath, line + "\n"); } catch { }
        while (_pathQueue.TryDequeue(out var row))
            try { lock (_fileLock) File.AppendAllText(_pathCsvPath, row + "\n"); } catch { }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void Evt(string line)
    {
        string ts = $"[{DateTime.Now:HH:mm:ss.fff}][T+{_gameTimeSec,7:F1}s]";
        _eventQueue.Enqueue($"{ts} {line}");
    }

    private void Path_(string row) => _pathQueue.Enqueue(row);

    private string ShortId(string fullId, string prefix)
    {
        if (_shortIds.TryGetValue(fullId, out var sid)) return sid;
        int n = prefix == "MSL" ? ++_mslCounter : ++_acCounter;
        sid = $"{prefix}{n:D2}";
        _shortIds[fullId] = sid;
        return sid;
    }

    private static double HdgDelta(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    private record AcState(double Rng, double Hdg, double Spd, string Beh, bool Ecm,
                            bool RadarLock, bool HardLock, bool MslInbound);
    private record TrkState(double Rng, TrackClassification Class, double Threat,
                             TrackQuality Qual, bool Engaged, bool Hot);

    public void Dispose()
    {
        _stop = true;
        _writerThread.Join(2000);
        _instance = null;
    }
}
