namespace DEADSKY.App.ViewModels;

/// <summary>
/// Lightweight helper that tracks collapse/expand state per section for the
/// current game session.  All sections default to expanded (true).
/// </summary>
public static class CollapsibleSectionState
{
    private static readonly Dictionary<string, bool> _state = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the persisted expanded state for <paramref name="sectionId"/>.
    /// Defaults to <c>true</c> (expanded) if the section has never been toggled.</summary>
    public static bool GetState(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return true;

        return _state.TryGetValue(sectionId, out bool value) ? value : true;
    }

    /// <summary>Persists the expanded state for <paramref name="sectionId"/>.</summary>
    public static void SetState(string sectionId, bool isExpanded)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return;

        _state[sectionId] = isExpanded;
    }

    /// <summary>Clears all persisted state (e.g. on new session start).</summary>
    public static void Reset() => _state.Clear();
}
