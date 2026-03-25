namespace DEADSKY.Audio;

/// <summary>
/// Minimal audio facade so the solution has the planned project boundary in place.
/// We'll grow this into the real audio subsystem once the simulation core is stable.
/// </summary>
public sealed class AudioEngine
{
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        IsInitialized = true;
    }
}
