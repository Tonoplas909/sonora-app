namespace Sonora.Appearance.Features;

/// <summary>When Sonora looks for a new version on its own.</summary>
public static class UpdateSchedule
{
    /// <summary>
    /// Opening the app checks, but never twice within this gap. GitHub allows an anonymous client only so many
    /// requests an hour, and an app that is restarted again and again (a crash loop, a developer at work) has no
    /// business asking each time. The manual button in the settings ignores this.
    /// </summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromHours(1);

    /// <summary>True when the check at startup is switched on and the last one is old enough.</summary>
    public static bool ShouldCheckAtStartup(bool enabled, DateTime? lastCheckUtc, DateTime nowUtc)
    {
        if (!enabled) return false;
        if (lastCheckUtc is not { } last) return true;
        return last > nowUtc // the clock went backwards: the stored time cannot be trusted
               || nowUtc - last >= MinimumGap;
    }
}
