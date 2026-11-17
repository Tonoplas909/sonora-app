using Sonora.Appearance.Localization;
namespace Sonora.Appearance.Features;

/// <summary>Sleep timer: stop playback after a delay, or when the current track ends.</summary>
public sealed class SleepTimer
{
    public DateTime? Deadline { get; private set; }
    public bool AtTrackEnd { get; private set; }
    public bool IsActive => Deadline != null || AtTrackEnd;

    public void StartMinutes(int minutes, DateTime now)
    {
        AtTrackEnd = false;
        Deadline = now.AddMinutes(Math.Max(1, minutes));
    }

    public void StartAtTrackEnd()
    {
        Deadline = null;
        AtTrackEnd = true;
    }

    public void Cancel()
    {
        Deadline = null;
        AtTrackEnd = false;
    }

    public TimeSpan? Remaining(DateTime now) => Deadline is { } d ? (d > now ? d - now : TimeSpan.Zero) : null;

    /// <summary>True once, when the delay has elapsed.</summary>
    public bool ShouldStop(DateTime now)
    {
        if (Deadline is not { } d || now < d) return false;
        Cancel();
        return true;
    }

    /// <summary>True once, at the first track change after "end of track" was chosen.</summary>
    public bool ShouldStopOnTrackChange()
    {
        if (!AtTrackEnd) return false;
        Cancel();
        return true;
    }

    public string Describe(DateTime now)
    {
        if (AtTrackEnd) return Loc.T("sleep.at_track_end");
        if (Remaining(now) is not { } left) return "";
        return Loc.T("sleep.in_minutes", Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
    }
}
