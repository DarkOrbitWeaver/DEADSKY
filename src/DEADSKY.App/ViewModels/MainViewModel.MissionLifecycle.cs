namespace DEADSKY.App.ViewModels;

public enum MissionLifecycleState
{
    Briefing,
    Standby,
    Active,
    Paused,
    Debrief
}

public partial class MainViewModel
{
    private void SetMissionLifecycle(MissionLifecycleState state)
    {
        MissionLifecycle = state;
        SimulationRunning = state == MissionLifecycleState.Active;
        RefreshCommandStates();
        RefreshDerivedBindings();
    }
}
