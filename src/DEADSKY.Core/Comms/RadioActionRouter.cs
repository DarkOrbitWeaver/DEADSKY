using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Comms;

public static class RadioActionRouter
{
    public static RadioMessage? BuildFallbackReply(
        RadioChannel channel,
        string playerMessage,
        SimulationSnapshot snapshot,
        ScenarioDefinition? scenario)
    {
        string lower = playerMessage.Trim().ToLowerInvariant();
        return channel switch
        {
            RadioChannel.CommandNet => BuildCommandReply(lower, snapshot, scenario),
            RadioChannel.IntelNet => BuildIntelReply(lower, snapshot),
            RadioChannel.AirDefenseNet => BuildAirDefenseReply(lower, snapshot),
            RadioChannel.Guard => BuildGuardReply(lower),
            RadioChannel.OpenFreq => BuildOpenFrequencyReply(lower, snapshot),
            _ => null
        };
    }

    private static RadioMessage BuildCommandReply(string message, SimulationSnapshot snapshot, ScenarioDefinition? scenario)
    {
        string objective = scenario?.SectorMap.Objectives.FirstOrDefault()?.Name?.ToUpperInvariant() ?? "PRIMARY ASSETS";
        string reply = message switch
        {
            _ when message.Contains("hello", StringComparison.Ordinal) || message.Contains("hi", StringComparison.Ordinal) || message.Contains("check in", StringComparison.Ordinal) =>
                "ALPHA, ECHO. READ YOU FIVE BY FIVE. GO AHEAD.",
            _ when message.Contains("thanks", StringComparison.Ordinal) || message.Contains("thank you", StringComparison.Ordinal) =>
                "ALPHA, ECHO. ROGER.",
            _ when message.Contains("ack", StringComparison.Ordinal) || message.Contains("roger", StringComparison.Ordinal) =>
                "ALPHA, ECHO. ROGER. MAINTAIN SECTOR WATCH AND REPORT ANY RAID COMMIT.",
            _ when message.Contains("picture", StringComparison.Ordinal) =>
                $"ALPHA, ECHO. PICTURE {snapshot.HostileTracks.Count} HOSTILE TRACKS. DEFEND {objective}.",
            _ when message.Contains("status", StringComparison.Ordinal) || message.Contains("sitrep", StringComparison.Ordinal) =>
                $"ALPHA, ECHO. SECTOR HOLDING. {snapshot.HostileTracks.Count} HOSTILE TRACKS. KEEP THE BASKET CLOSED.",
            _ when message.Contains("weapons free", StringComparison.Ordinal) || message.Contains("weapons_free", StringComparison.Ordinal) =>
                snapshot.HostileTracks.Count > 0
                    ? "ALPHA, ECHO. NEGATIVE WEAPONS FREE. HOLD WEAPONS TIGHT UNLESS HOSTILE ACT OR DECLARE CONFIRMED."
                    : "ALPHA, ECHO. NEGATIVE. NO HOSTILE DECLARE JUSTIFIES ROE CHANGE AT THIS TIME.",
            _ =>
                $"ALPHA, ECHO. ROGER. HOLD {objective} AND MAINTAIN CURRENT ROE."
        };

        return CommManager.CreateAlliedHQMessage(reply, MessagePriority.Priority);
    }

    private static RadioMessage BuildIntelReply(string message, SimulationSnapshot snapshot)
    {
        var primary = snapshot.HostileTracks
            .OrderByDescending(track => track.ThreatLevel)
            .FirstOrDefault();

        string reply = primary == null
            ? "INTEL-1, ALPHA. CURRENT PICTURE SPARSE. NO HIGH-CONFIDENCE HOSTILE DECLARE."
            : $"INTEL-1, ALPHA. PRIMARY CONTACT {primary.TrackId} {ContactAdvisor.Build(primary).IdentityLabel}. BRAA {primary.BearingDeg:000}/{primary.RangeNm:0.0}.";

        if (message.Contains("thanks", StringComparison.Ordinal) || message.Contains("roger", StringComparison.Ordinal))
            return CommManager.CreateIntelMessage("INTEL-1, ALPHA. ROGER.");

        if (message.Contains("picture", StringComparison.Ordinal))
            return CommManager.CreateIntelMessage(primary == null
                ? "INTEL-1, ALPHA. PICTURE THIN. NO FIRM HOSTILE DECLARE."
                : $"INTEL-1, ALPHA. PICTURE {snapshot.HostileTracks.Count} HOSTILE TRACKS. PRIMARY {primary.TrackId}.");

        if (message.Contains("highest threat", StringComparison.Ordinal) || message.Contains("assess", StringComparison.Ordinal) || message.Contains("what do you have", StringComparison.Ordinal))
            return CommManager.CreateIntelMessage(reply);

        return CommManager.CreateIntelMessage("INTEL-1, ALPHA. ROGER. STAND BY FOR UPDATED TRACK CORRELATION.");
    }

    private static RadioMessage BuildAirDefenseReply(string message, SimulationSnapshot snapshot)
    {
        string reply = message.Contains("acknowledge", StringComparison.Ordinal) || message.Contains("check in", StringComparison.Ordinal)
            ? "BRAVO ACTUAL, ALPHA. BRAVO COPIES. HOLDING CROSS-FIRE LANE AND MONITORING EASTERN AXIS."
            : $"BRAVO ACTUAL, ALPHA. BRAVO REPORTS {snapshot.HostileTracks.Count} HOSTILE TRACKS IN SECTOR PICTURE. READY TO SUPPORT.";

        return CommManager.CreateMessage(
            RadioRules.CreateFriendlySupportProfile("BRAVO ACTUAL", "BATTERY", "BRAVO", "ADJACENT SAM BATTERY"),
            RadioChannel.AirDefenseNet,
            reply,
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA",
            canReply: true,
            staticLevel: 0.1);
    }

    private static RadioMessage BuildGuardReply(string message)
    {
        string reply = message.Contains("mayday", StringComparison.Ordinal) || message.Contains("emergency", StringComparison.Ordinal)
            ? "GUARD, THIS IS ECHO ACTUAL. MAYDAY TRAFFIC RECEIVED. ALL STATIONS HOLD NON-ESSENTIAL TRANSMISSIONS."
            : "GUARD, THIS IS ECHO ACTUAL. GUARD IS EMERGENCY-ONLY. SHIFT TO COMMAND NET.";

        return CommManager.CreateMessage(
            RadioRules.CreateAlliedHQProfile(),
            RadioChannel.Guard,
            reply,
            MessagePriority.Immediate,
            MessageType.Alert,
            recipient: "ALL STATIONS",
            canReply: true,
            staticLevel: 0.12);
    }

    private static RadioMessage? BuildOpenFrequencyReply(string message, SimulationSnapshot snapshot)
    {
        if (message.Contains("radio check", StringComparison.Ordinal) ||
            message.Contains("copy", StringComparison.Ordinal) ||
            message.Contains("roger", StringComparison.Ordinal) ||
            message.Contains("any station", StringComparison.Ordinal))
        {
            return CommManager.CreateOpenFrequencyMessage(
                RadioRules.CreateEnemyProfile("RAVEN LEAD", "RAVEN NET", "HOSTILE STRIKE PACKAGE", allowProfanity: true),
                "UNKNOWN STATION, RAVEN LEAD. READ YOU. CONTINUING MISSION.",
                MessagePriority.Routine,
                recipient: "ALPHA",
                canReply: true);
        }

        if (message.Contains("turn away", StringComparison.Ordinal) || message.Contains("surrender", StringComparison.Ordinal) || message.Contains("defended sector", StringComparison.Ordinal))
        {
            string reply = snapshot.HostileTracks.Count > 0
                ? "UNKNOWN STATION, RAVEN LEAD. NEGATIVE. CONTINUING INBOUND."
                : "UNKNOWN STATION, RAVEN LEAD. TRAFFIC RECEIVED.";

            return CommManager.CreateOpenFrequencyMessage(
                RadioRules.CreateEnemyProfile("RAVEN LEAD", "RAVEN NET", "HOSTILE STRIKE PACKAGE", allowProfanity: true),
                reply,
                MessagePriority.Priority,
                recipient: "ALPHA",
                canReply: true);
        }

        if (message.Contains("identify", StringComparison.Ordinal))
        {
            return CommManager.CreateOpenFrequencyMessage(
                RadioRules.CreateSystemProfile("OPEN NET"),
                "OPEN NET, ALPHA. MULTIPLE UNVERIFIED TRANSMITTERS PRESENT. NO RELIABLE IDENTIFICATION.",
                MessagePriority.Routine,
                recipient: "ALPHA",
                canReply: false);
        }

        if (snapshot.HostileTracks.Count > 0)
        {
            return CommManager.CreateOpenFrequencyMessage(
                RadioRules.CreateEnemyProfile("RAVEN LEAD", "RAVEN NET", "HOSTILE STRIKE PACKAGE", allowProfanity: true),
                "UNKNOWN STATION, RAVEN LEAD. MESSAGE RECEIVED. REMAIN CLEAR OF OUR ROUTE.",
                MessagePriority.Priority,
                recipient: "ALPHA",
                canReply: true);
        }

        return CommManager.CreateOpenFrequencyMessage(
            RadioRules.CreateSystemProfile("OPEN NET"),
            "OPEN NET, ALPHA. TRANSMISSION RECEIVED. NO VERIFIED RESPONDER ON CHANNEL.",
            MessagePriority.Routine,
            recipient: "ALPHA",
            canReply: false);
    }
}
