using Maush.Core;

namespace Maush.Core.Tests;

public sealed class AcceptanceFlowTests
{
    [Fact]
    public void OneMinutePlanCompletesTwoFullCyclesWithoutStopping()
    {
        var timer = new CycleTimer(new TimerSettings
        {
            WorkDurationMinutes = 1,
            RestDurationMinutes = 1,
            IsLoopEnabled = true
        });
        var transitions = new List<TimerState>();
        timer.Changed += (_, snapshot) => transitions.Add(snapshot.State);

        timer.Start();
        timer.Advance(TimeSpan.FromMinutes(1));
        timer.Advance(TimeSpan.FromMinutes(1));
        timer.Advance(TimeSpan.FromMinutes(1));
        timer.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(
            [TimerState.Working, TimerState.Resting, TimerState.Working, TimerState.Resting, TimerState.Working],
            transitions);
        Assert.Equal(TimerState.Working, timer.State);
    }

    [Fact]
    public void WorkAndRestDurationsChangedMidRunApplyToTheirNextRoundsOnly()
    {
        var initial = new TimerSettings
        {
            WorkDurationMinutes = 30,
            RestDurationMinutes = 5,
            IsLoopEnabled = true
        };
        var timer = new CycleTimer(initial);
        timer.Start();
        timer.Advance(TimeSpan.FromMinutes(10));

        timer.ApplySettings(initial with { WorkDurationMinutes = 45, RestDurationMinutes = 15 });

        Assert.Equal(TimeSpan.FromMinutes(20), timer.Remaining);
        timer.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Remaining);
        timer.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(TimeSpan.FromMinutes(45), timer.Remaining);
    }

    [Fact]
    public void ResetFromActiveRoundCancelsPlaybackAndReturnsToIdleUsingLatestWorkSetting()
    {
        var timer = new CycleTimer(new TimerSettings { WorkDurationMinutes = 30, RestDurationMinutes = 5 });
        var cancelCount = 0;
        timer.PlaybackCancellationRequested += (_, _) => cancelCount++;
        timer.Start();
        timer.ApplySettings(timer.Settings with { WorkDurationMinutes = 60 });

        timer.Reset();

        Assert.Equal(TimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(60), timer.Remaining);
        Assert.Equal(1, cancelCount);
    }
}
