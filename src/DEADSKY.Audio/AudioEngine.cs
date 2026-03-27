using System.Media;
using System.Runtime.Versioning;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DEADSKY.Core.Logging;

namespace DEADSKY.Audio;

public enum SoundEvent
{
    SystemOnline,
    SystemOffline,
    NewContact,
    LockOnWarning,
    WeaponReady,
    RwrSearchPing,
    RwrLockWarning,
    MissileLaunch,
    MissileImpact,
    MissileMiss,
    AlertKlaxon,
    FlashMessageAlert,
    RadioSquelchOpen,
    ChannelSwitch,
    UiMenuOpen,
    UiButtonPress
}

/// <summary>
/// Lightweight audio facade for now. We keep the public surface realistic so we can
/// swap in proper sampled audio later without rewriting the UI and simulation hooks.
/// </summary>
public sealed class AudioEngine
{
    private readonly object _playLock = new();
    private readonly Dictionary<SoundEvent, DateTime> _lastPlaybackUtc = new();
    private readonly List<MediaPlayer> _activePlayers = new();
    private readonly Dictionary<SoundEvent, string[]> _assetMap = new()
    {
        [SoundEvent.SystemOnline] = ["ui/system_online_chirp.wav"],
        [SoundEvent.SystemOffline] = ["ui/system_offline_descend.wav"],
        [SoundEvent.NewContact] = ["Single RwR peep RADAR warning.mp3", "ui/radar_contact_ping_01.wav"],
        [SoundEvent.LockOnWarning] = ["radar-lock.mp3", "alerts/lock_warning_pulse.wav"],
        [SoundEvent.WeaponReady] = ["radar-lock.mp3", "Single RwR peep RADAR warning.mp3"],
        [SoundEvent.RwrSearchPing] = ["Single RwR peep RADAR warning.mp3", "alerts/lock_warning_pulse.wav"],
        [SoundEvent.RwrLockWarning] = ["RwR continus long beeps of being locked by radar Alert.mp3", "FAT Rwr Continus long beeps WARNING.mp3", "alerts/lock_warning_pulse.wav"],
        [SoundEvent.MissileLaunch] = ["Missle_Launch-loud.mp3", "weapons/missile_launch_01.wav"],
        [SoundEvent.MissileImpact] = ["impacts/battery_hit_near.wav", "impacts/missile_splash_far.wav"],
        [SoundEvent.MissileMiss] = ["impacts/missile_miss_soft.wav"],
        [SoundEvent.AlertKlaxon] = ["FAT Rwr Continus long beeps WARNING.mp3", "RwR continus long beeps of being locked by radar Alert.mp3", "alerts/alert_red_klaxon.wav"],
        [SoundEvent.FlashMessageAlert] = ["Single RwR peep RADAR warning.mp3", "alerts/flash_message_chime.wav"],
        [SoundEvent.RadioSquelchOpen] = ["voice_input/mic_open_beep.wav", "radio/common/radio_squelch_close.wav", "radio/common/radio_squelch_open.wav"],
        [SoundEvent.ChannelSwitch] = ["ui/channel_switch_click.wav"],
        [SoundEvent.UiMenuOpen] = ["ui/menu_open_soft.wav"],
        [SoundEvent.UiButtonPress] = ["ui/button_press_soft.wav"]
    };
    private readonly Dictionary<SoundEvent, TimeSpan> _cooldowns = new()
    {
        [SoundEvent.NewContact] = TimeSpan.FromMilliseconds(350),
        [SoundEvent.LockOnWarning] = TimeSpan.FromMilliseconds(900),
        [SoundEvent.WeaponReady] = TimeSpan.FromMilliseconds(850),
        [SoundEvent.RwrSearchPing] = TimeSpan.FromMilliseconds(900),
        [SoundEvent.RwrLockWarning] = TimeSpan.FromMilliseconds(1800),
        [SoundEvent.AlertKlaxon] = TimeSpan.FromMilliseconds(1600),
        [SoundEvent.FlashMessageAlert] = TimeSpan.FromMilliseconds(500),
        [SoundEvent.RadioSquelchOpen] = TimeSpan.FromMilliseconds(150),
        [SoundEvent.ChannelSwitch] = TimeSpan.FromMilliseconds(120),
        [SoundEvent.UiMenuOpen] = TimeSpan.FromMilliseconds(120),
        [SoundEvent.UiButtonPress] = TimeSpan.FromMilliseconds(80)
    };
    private readonly Dictionary<SoundEvent, double> _volumeJitter = new()
    {
        [SoundEvent.MissileLaunch] = 0.08,
        [SoundEvent.RadioSquelchOpen] = 0.05,
        [SoundEvent.LockOnWarning] = 0.03,
        [SoundEvent.WeaponReady] = 0.02
    };
    private readonly Dictionary<SoundEvent, (double Min, double Max)> _speedRanges = new()
    {
        [SoundEvent.MissileLaunch] = (0.92, 1.06),
        [SoundEvent.RadioSquelchOpen] = (0.96, 1.04),
        [SoundEvent.LockOnWarning] = (0.97, 1.02),
        [SoundEvent.WeaponReady] = (0.99, 1.03)
    };
    private readonly Dictionary<SoundEvent, double> _volumes = new()
    {
        [SoundEvent.UiButtonPress] = 0.45,
        [SoundEvent.UiMenuOpen] = 0.5,
        [SoundEvent.ChannelSwitch] = 0.55,
        [SoundEvent.RadioSquelchOpen] = 0.38,
        [SoundEvent.NewContact] = 0.64,
        [SoundEvent.RwrSearchPing] = 0.62,
        [SoundEvent.RwrLockWarning] = 0.76,
        [SoundEvent.LockOnWarning] = 0.58,
        [SoundEvent.WeaponReady] = 0.46,
        [SoundEvent.AlertKlaxon] = 0.72,
        [SoundEvent.MissileLaunch] = 0.34,
        [SoundEvent.MissileImpact] = 0.88
    };

    public bool IsInitialized { get; private set; }
    public SoundEvent? LastPlayed { get; private set; }
    public string AssetRoot { get; private set; } = "";
    public string? LastAssetPath { get; private set; }

    public void Initialize()
    {
        AssetRoot = ResolveAssetRoot();
        IsInitialized = true;
    }

    public void Play(SoundEvent sound)
    {
        GameLogger.Info("AUDIO", $"Play requested: {sound} on thread {Environment.CurrentManagedThreadId}");
        LastPlayed = sound;
        
        if (!IsInitialized)
        {
            GameLogger.Info("AUDIO", "AudioEngine not initialized, initializing now");
            Initialize();
        }

        if (IsCoolingDown(sound))
        {
            GameLogger.Debug("AUDIO", $"{sound} is cooling down, skipping playback");
            return;
        }

        string? assetPath = ResolveAssetPath(sound);
        LastAssetPath = assetPath;

        if (assetPath == null)
        {
            GameLogger.Warning("AUDIO", $"{sound} has no asset path");
            return;
        }

        GameLogger.Info("AUDIO", $"Playing {sound} from {Path.GetFileName(assetPath)}");

        if (Application.Current?.Dispatcher != null)
        {
            GameLogger.Debug("AUDIO", $"Dispatching {sound} to UI thread for MediaPlayer playback");
            Application.Current.Dispatcher.BeginInvoke(() => PlayMedia(assetPath, sound));
            return;
        }

        GameLogger.Debug("AUDIO", $"No dispatcher available, using Task.Run for {sound}");
        _ = Task.Run(() => PlayWave(assetPath, sound));
    }

    private string? ResolveAssetPath(SoundEvent sound)
    {
        if (!_assetMap.TryGetValue(sound, out var options) || options.Length == 0)
            return null;

        var available = options
            .Select(relative => Path.Combine(AssetRoot, relative))
            .Where(File.Exists)
            .ToList();

        if (available.Count == 0)
            return null;

        return available.Count == 1
            ? available[0]
            : available[Random.Shared.Next(available.Count)];
    }

    private void PlayWave(string assetPath, SoundEvent sound)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return;

            lock (_playLock)
            {
                if (string.Equals(Path.GetExtension(assetPath), ".wav", StringComparison.OrdinalIgnoreCase))
                    PlayWaveWindows(assetPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIO] {sound} playback failed: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private void PlayMedia(string assetPath, SoundEvent sound)
    {
        MediaPlayer? player = null;
        try
        {
            if (!OperatingSystem.IsWindows())
                return;

            player = new MediaPlayer();
            player.Open(new Uri(assetPath, UriKind.Absolute));
            double volume = _volumes.TryGetValue(sound, out var configuredVolume)
                ? configuredVolume
                : 0.75;

            if (_volumeJitter.TryGetValue(sound, out var jitter))
                volume = Math.Clamp(volume + ((Random.Shared.NextDouble() * 2.0) - 1.0) * jitter, 0.05, 1.0);

            player.Volume = volume;

            if (_speedRanges.TryGetValue(sound, out var speedRange))
                player.SpeedRatio = speedRange.Min + Random.Shared.NextDouble() * (speedRange.Max - speedRange.Min);

            player.MediaEnded += (_, _) => ReleasePlayer(player);
            player.MediaFailed += (_, e) =>
            {
                Console.WriteLine($"[AUDIO] {sound} playback failed: {e.ErrorException?.Message ?? "Unknown media error"}");
                ReleasePlayer(player);
            };

            lock (_playLock)
            {
                _activePlayers.Add(player);
            }

            player.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIO] {sound} playback failed: {ex.Message}");
            // Clean up player on exception to prevent resource leak
            if (player != null)
            {
                lock (_playLock)
                {
                    _activePlayers.Remove(player);
                }
                try
                {
                    player.Stop();
                    player.Close();
                }
                catch { /* Ignore disposal errors */ }
            }
        }
    }

    private bool IsCoolingDown(SoundEvent sound)
    {
        lock (_playLock)
        {
            if (!_cooldowns.TryGetValue(sound, out var cooldown))
            {
                _lastPlaybackUtc[sound] = DateTime.UtcNow;
                return false;
            }

            var now = DateTime.UtcNow;
            if (_lastPlaybackUtc.TryGetValue(sound, out var lastPlayed) && now - lastPlayed < cooldown)
                return true;

            _lastPlaybackUtc[sound] = now;
            return false;
        }
    }

    private void ReleasePlayer(MediaPlayer player)
    {
        lock (_playLock)
        {
            _activePlayers.Remove(player);
        }

        player.Stop();
        player.Close();
    }

    [SupportedOSPlatform("windows")]
    private static void PlayWaveWindows(string assetPath)
    {
        using var player = new SoundPlayer(assetPath);
        player.PlaySync();
    }

    private static string ResolveAssetRoot()
    {
        string baseDir = AppContext.BaseDirectory;
        string currentDir = Environment.CurrentDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "DEADSKY.Audio", "Assets")),
            Path.Combine(currentDir, "src", "DEADSKY.Audio", "Assets"),
            Path.Combine(currentDir, "Assets")
        };

        return candidates.FirstOrDefault(Directory.Exists)
            ?? Path.Combine(currentDir, "src", "DEADSKY.Audio", "Assets");
    }
}
