using DEADSKY.Core.Comms;

namespace DEADSKY.Backend.Tests;

public class RadioRulesTests
{
    [Fact]
    public void CreateMessage_EnemyOnCommandNet_ReroutesToIntercept()
    {
        var hostile = RadioRules.CreateEnemyProfile("RAVEN ACTUAL", "RAVEN NET", "STRIKE CTRL");
        var message = CommManager.CreateMessage(
            hostile,
            RadioChannel.CommandNet,
            "Move now, fuck this battery.",
            MessagePriority.Priority,
            MessageType.Normal);

        Assert.Equal(RadioChannel.IntelNet, message.Channel);
        Assert.Equal(MessageType.Intercept, message.Type);
        Assert.DoesNotContain("fuck", message.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateOpenFrequencyMessage_HostileTraffic_PreservesRawTone()
    {
        var hostile = RadioRules.CreateEnemyProfile("RAVEN ACTUAL", "OPEN NET", "HOSTILE", allowProfanity: true);
        var message = CommManager.CreateOpenFrequencyMessage(
            hostile,
            "Alpha battery, fuck you, we are not turning away.",
            MessagePriority.Priority);

        Assert.Equal(RadioChannel.OpenFreq, message.Channel);
        Assert.Contains("fuck", message.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(message.CanReply);
    }
}
