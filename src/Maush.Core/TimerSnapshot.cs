namespace Maush.Core;

public sealed record TimerSnapshot(
    TimerState State,
    TimerState? PausedFromState,
    TimeSpan Remaining,
    TimeSpan? LockedDuration,
    PauseReason? PauseReason,
    TimerSettings Settings);
