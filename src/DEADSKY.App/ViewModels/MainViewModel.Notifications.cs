using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DEADSKY.Audio;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.App.ViewModels;

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical
}

public partial class MainViewModel
{
    private bool _hadDetectedHostilesLastTick;
    private bool _hadIncomingThreatLastTick;
    private bool _hadCloseThreatLastTick;

    public ObservableCollection<NotificationCardViewModel> ActiveNotifications { get; } = new();

    private void RefreshThreatWarnings(SimulationSnapshot snapshot)
    {
        bool hasHostiles = snapshot.HostileTracks.Count > 0;
        bool hasIncomingMissile = snapshot.AllTracks.Any(track => ContactAdvisor.Build(track).Callout == "VAMPIRE");
        bool hasCloseThreat = snapshot.HostileTracks.Any(track => track.RangeNm <= Math.Min(30, Math.Max(18, snapshot.RadarRangeNm * 0.35)));

        if (hasHostiles && !_hadDetectedHostilesLastTick)
        {
            Audio.Play(SoundEvent.RwrSearchPing);
            PushNotification(
                "RADAR",
                "CONTACTS DETECTED",
                $"{snapshot.HostileTracks.Count} hostile track{(snapshot.HostileTracks.Count == 1 ? "" : "s")} entered the sector picture.",
                NotificationSeverity.Warning,
                durationSeconds: 6);
        }

        if (hasIncomingMissile && !_hadIncomingThreatLastTick)
        {
            Audio.Play(SoundEvent.RwrLockWarning);
            PushNotification(
                "ALERT",
                "VAMPIRE WARNING",
                "Inbound missile track detected. Inner assets are at immediate risk.",
                NotificationSeverity.Critical,
                durationSeconds: 10);
        }

        if (hasCloseThreat && !_hadCloseThreatLastTick)
        {
            Audio.Play(SoundEvent.RwrLockWarning);
            PushNotification(
                "THREAT",
                "INNER RING PRESSURE",
                "Hostile tracks are inside the defended inner ring.",
                NotificationSeverity.Critical,
                durationSeconds: 8);
        }

        _hadDetectedHostilesLastTick = hasHostiles;
        _hadIncomingThreatLastTick = hasIncomingMissile;
        _hadCloseThreatLastTick = hasCloseThreat;
        PruneNotifications();
    }

    private void PushRadioNotification(RadioMessage message)
    {
        PushNotification(
            message.ChannelTag,
            $"{message.PriorityTag} // {message.DisplayHeader}",
            message.Content,
            message.Priority == MessagePriority.Flash ? NotificationSeverity.Critical : NotificationSeverity.Warning,
            durationSeconds: message.Priority == MessagePriority.Flash ? 10 : 7);
    }

    private void PushNotification(
        string sourceTag,
        string title,
        string body,
        NotificationSeverity severity,
        double durationSeconds = 7)
    {
        // CRITICAL: Dispatch to UI thread asynchronously to prevent deadlocks
        DispatchToUI(() =>
        {
            try
            {
                PruneNotifications();

                ActiveNotifications.Insert(0, new NotificationCardViewModel(
                    sourceTag,
                    title,
                    body,
                    severity,
                    DateTime.UtcNow.AddSeconds(durationSeconds)));

                IncrementAlertsUnread();

                while (ActiveNotifications.Count > 5)
                    ActiveNotifications.RemoveAt(ActiveNotifications.Count - 1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Notifications] ERROR in PushNotification: {ex.Message}");
                // Never crash - notifications are non-critical
            }
        });
    }

    private void PruneNotifications()
    {
        try
        {
            var expired = ActiveNotifications
                .Where(notification => notification.ExpiresAtUtc <= DateTime.UtcNow)
                .ToList();

            foreach (var notification in expired)
                ActiveNotifications.Remove(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notifications] ERROR in PruneNotifications: {ex.Message}");
            // Never crash - just skip pruning this time
        }
    }

    private void ResetNotificationState()
    {
        _hadDetectedHostilesLastTick = false;
        _hadIncomingThreatLastTick = false;
        _hadCloseThreatLastTick = false;
        AlertsUnreadCount = 0;
        ActiveNotifications.Clear();
    }
}

public sealed partial class NotificationCardViewModel : ObservableObject
{
    public NotificationCardViewModel(
        string sourceTag,
        string title,
        string body,
        NotificationSeverity severity,
        DateTime expiresAtUtc)
    {
        SourceTag = sourceTag;
        Title = title;
        Body = body;
        Severity = severity;
        CreatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string SourceTag { get; }
    public string Title { get; }
    public string Body { get; }
    public NotificationSeverity Severity { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime ExpiresAtUtc { get; }
    public string TimeText => CreatedAtUtc.ToString("HH:mm:ss");
    public string AccentHex => Severity switch
    {
        NotificationSeverity.Critical => "#FF6B57",
        NotificationSeverity.Warning => "#F4C14A",
        _ => "#7CB8FF"
    };
    public string BackgroundHex => Severity switch
    {
        NotificationSeverity.Critical => "#24100D",
        NotificationSeverity.Warning => "#231B0E",
        _ => "#0E1822"
    };
}
