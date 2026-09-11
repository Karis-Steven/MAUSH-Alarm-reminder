using Maush.Core;

namespace Maush.Core.Tests;

public sealed class CycleTimerTests
{
    private static readonly TimerSettings DefaultSettings = new()
    {
        WorkDurationMinutes = 30,
        RestDurationMinutes = 5,
        IsLoopEnabled = true
    };

    [Fact]
    public void StartsInIdleWithConfiguredWorkDuration()
    {
        var timer = new CycleTimer(DefaultSettings);

        Assert.Equal(TimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Remaining);
    }

    [Fact]
    public void StartEntersWorking()
    {
        var timer = new CycleTimer(DefaultSettings);

        timer.Start();

        Assert.Equal(TimerState.Working, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Remaining);
    }

    [Fact]
    public void WorkCompletionPlaysReminderAndStartsRestWhenLoopEnabled()
    {
        var timer = new CycleTimer(DefaultSettings);
        var reminders = new List<ReminderKind>();
        timer.ReminderRequested += (_, reminder) => reminders.Add(reminder);
        timer.Start();

        timer.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal([ReminderKind.Rest], reminders);
        Assert.Equal(TimerState.Resting, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Remaining);
    }

    [Fact]
    public void WorkCompletionStopsAfterReminderWhenLoopDisabled()
    {
        var timer = new CycleTimer(DefaultSettings with { IsLoopEnabled = false });
        var reminders = new List<ReminderKind>();
        timer.ReminderRequested += (_, reminder) => reminders.Add(reminder);
        timer.Start();

        timer.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal([ReminderKind.Rest], reminders);
        Assert.Equal(TimerState.Stopped, timer.State);
    }

    [Fact]
    public void RestCompletionStartsAnotherWorkPeriod()
    {
        var timer = new CycleTimer(DefaultSettings);
        var reminders = new List<ReminderKind>();
        timer.ReminderRequested += (_, reminder) => reminders.Add(reminder);
        timer.Start();
        timer.Advance(TimeSpan.FromMinutes(30));

        timer.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal([ReminderKind.Rest, ReminderKind.ResumeWork], reminders);
        Assert.Equal(TimerState.Working, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Remaining);
    }

    [Fact]
    public void LoopContinuesForAtLeastOneHundredCycles()
    {
        var timer = new CycleTimer(DefaultSettings);
        timer.Start();

        for (var index = 0; index < 100; index++)
        {
            timer.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(TimerState.Resting, timer.State);
            timer.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(TimerState.Working, timer.State);
        }
    }

    [Theory]
    [InlineData(TimerState.Working)]
    [InlineData(TimerState.Resting)]
    public void PauseAndResumePreserveRemainingTime(TimerState targetState)
    {
        var timer = MoveToState(targetState);
        timer.Advance(TimeSpan.FromMinutes(1));
        var remaining = timer.Remaining;

        timer.Pause();
        timer.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(TimerState.Paused, timer.State);
        Assert.Equal(targetState, timer.PausedFromState);
        Assert.Equal(remaining, timer.Remaining);

        timer.Resume();
        Assert.Equal(targetState, timer.State);
        Assert.Equal(remaining, timer.Remaining);
    }

    [Theory]
    [InlineData(TimerState.Idle)]
    [InlineData(TimerState.Working)]
    [InlineData(TimerState.Resting)]
    [InlineData(TimerState.Paused)]
    [InlineData(TimerState.Stopped)]
    public void StopAlwaysWinsAndCancelsPlayback(TimerState targetState)
    {
        var timer = MoveToState(targetState);
        var cancellationCount = 0;
        timer.PlaybackCancellationRequested += (_, _) => cancellationCount++;

        timer.Stop();
        timer.Advance(TimeSpan.FromHours(2));

        Assert.Equal(TimerState.Stopped, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        Assert.Equal(1, cancellationCount);
    }

    [Fact]
    public void StopRequestedInsideReminderCallbackCannotBeOverriddenByPhaseTransition()
    {
        var timer = new CycleTimer(DefaultSettings);
        timer.ReminderRequested += (_, _) => timer.Stop();
        timer.Start();

        timer.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(TimerState.Stopped, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }

    [Fact]
    public void NewWorkDurationDoesNotChangeCurrentRound()
    {
        var timer = new CycleTimer(DefaultSettings);
        timer.Start();
        timer.Advance(TimeSpan.FromMinutes(10));

        timer.ApplySettings(DefaultSettings with { WorkDurationMinutes = 45 });

        Assert.Equal(TimeSpan.FromMinutes(20), timer.Remaining);
        timer.Advance(TimeSpan.FromMinutes(20));
        timer.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(45), timer.Remaining);
    }

    [Fact]
    public void DisablingLoopDuringRestStopsAfterRestReminder()
    {
        var timer = new CycleTimer(DefaultSettings);
        var reminders = new List<ReminderKind>();
        timer.ReminderRequested += (_, reminder) => reminders.Add(reminder);
        timer.Start();
        timer.Advance(TimeSpan.FromMinutes(30));

        timer.ApplySettings(DefaultSettings with { IsLoopEnabled = false });
        timer.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal([ReminderKind.Rest, ReminderKind.ResumeWork], reminders);
        Assert.Equal(TimerState.Stopped, timer.State);
    }

    [Fact]
    public void SystemPauseKeepsReasonAndRequiresManualResume()
    {
        var timer = new CycleTimer(DefaultSettings);
        timer.Start();

        timer.Pause(PauseReason.System);

        Assert.Equal(TimerState.Paused, timer.State);
        Assert.Equal(PauseReason.System, timer.CurrentPauseReason);
        timer.Resume();
        Assert.Equal(TimerState.Working, timer.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void InvalidDurationsAreRejected(int minutes)
    {
        var settings = DefaultSettings with { WorkDurationMinutes = minutes };

        Assert.Throws<ArgumentOutOfRangeException>(() => new CycleTimer(settings));
    }

    private static CycleTimer MoveToState(TimerState state)
    {
        var timer = new CycleTimer(DefaultSettings);
        switch (state)
        {
            case TimerState.Idle:
                break;
            case TimerState.Working:
                timer.Start();
                break;
            case TimerState.Resting:
                timer.Start();
                timer.Advance(TimeSpan.FromMinutes(30));
                break;
            case TimerState.Paused:
                timer.Start();
                timer.Pause();
                break;
            case TimerState.Stopped:
                timer.Stop();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        return timer;
    }
}
