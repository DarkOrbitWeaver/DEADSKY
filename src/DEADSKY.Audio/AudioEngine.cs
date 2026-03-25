namespace DEADSKY.Audio;

public enum SoundEvent
{
    SystemOnline,
    SystemOffline,
    NewContact,
    LockOnWarning,
    MissileLaunch,
    MissileImpact,
    MissileMiss,
    AlertKlaxon,
    FlashMessageAlert,
    RadioSquelchOpen,
    ChannelSwitch
}

/// <summary>
/// Lightweight audio facade for now. We keep the public surface realistic so we can
/// swap in proper sampled audio later without rewriting the UI and simulation hooks.
/// </summary>
public sealed class AudioEngine
{
    public bool IsInitialized { get; private set; }
    public SoundEvent? LastPlayed { get; private set; }

    public void Initialize()
    {
        IsInitialized = true;
    }

    public void Play(SoundEvent sound)
    {
        LastPlayed = sound;
        Console.WriteLine($"[AUDIO] {sound}");
    }
}
