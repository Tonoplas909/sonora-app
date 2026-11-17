using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class UpdateScheduleTests
{
    static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Disabled_NeverChecks()
    {
        Assert.False(UpdateSchedule.ShouldCheckAtStartup(false, null, Now));
        Assert.False(UpdateSchedule.ShouldCheckAtStartup(false, Now.AddDays(-30), Now));
    }

    [Fact]
    public void NeverChecked_ChecksAtOnce() =>
        Assert.True(UpdateSchedule.ShouldCheckAtStartup(true, null, Now));

    [Fact]
    public void CheckedMinutesAgo_WaitsSoRestartsDoNotHammerGitHub() =>
        Assert.False(UpdateSchedule.ShouldCheckAtStartup(true, Now.AddMinutes(-10), Now));

    [Fact]
    public void CheckedJustBeforeTheGap_StillWaits() =>
        Assert.False(UpdateSchedule.ShouldCheckAtStartup(true, Now - UpdateSchedule.MinimumGap + TimeSpan.FromSeconds(1), Now));

    [Fact]
    public void CheckedAtLeastTheGapAgo_Checks()
    {
        Assert.True(UpdateSchedule.ShouldCheckAtStartup(true, Now - UpdateSchedule.MinimumGap, Now));
        Assert.True(UpdateSchedule.ShouldCheckAtStartup(true, Now.AddDays(-3), Now));
    }

    [Fact]
    public void ClockWentBackwards_ChecksRatherThanWaitForever() =>
        Assert.True(UpdateSchedule.ShouldCheckAtStartup(true, Now.AddHours(5), Now));

    [Fact]
    public void Defaults_CheckOnOpenAndHaveNeverChecked()
    {
        var f = new FeatureSettings();
        Assert.True(f.UpdateCheck);
        Assert.Null(f.LastUpdateCheckUtc);
    }
}
