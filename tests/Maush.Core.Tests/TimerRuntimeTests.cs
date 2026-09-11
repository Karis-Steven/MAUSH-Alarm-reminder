using Maush.Core;

namespace Maush.Core.Tests;

public sealed class TimerRuntimeTests
{
    private static TimerSettings Settings => new()
    {
        WorkDurationMinutes = 30,
        RestDurationMinutes = 5,
        IsLoopEnabled = true
    };

    [Fact]
    public void PulseUsesElapsedClockTimeInsteadOfPulseCount()
    {
        var clock = new ManualTimeProvider();
        var runtime = new TimerRuntime(new CycleTimer(Settings), clock);
        runtime.Start();

        clock.Advance(TimeSpan.FromSeconds(12.5));
        runtime.Pulse();

        Assert.Equal(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(12.5), runtime.Timer.Remaining);
    }

    [Fact]
    public void ManualPauseAccountsForTimeSinceLastPulse()
    {
        var clock = new ManualTimeProvider();
        var runtime = new TimerRuntime(new CycleTimer(Settings), clock);
        runtime.Start();
        clock.Advance(TimeSpan.FromSeconds(7));

        runtime.Pause();

        Assert.Equal(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(7), runtime.Timer.Remaining);
    }

    [Fact]
    public void SystemPauseDoesNotCountTimeSpentSuspended()
    {
        var clock = new ManualTimeProvider();
        var runtime = new TimerRuntime(new CycleTimer(Settings), clock);
        runtime.Start();
        runtime.Pulse();
        clock.Advance(TimeSpan.FromHours(8));

        runtime.Pause(PauseReason.System);
        runtime.Resume();
        clock.Advance(TimeSpan.FromSeconds(1));
        runtime.Pulse();

        Assert.Equal(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1), runtime.Timer.Remaining);
    }

    [Fact]
    public void PausedTimeIsNotSubtractedAfterResume()
    {
        var clock = new ManualTimeProvider();
        var runtime = new TimerRuntime(new CycleTimer(Settings), clock);
        runtime.Start();
        clock.Advance(TimeSpan.FromMinutes(1));
        runtime.Pause();
        clock.Advance(TimeSpan.FromHours(1));

        runtime.Resume();
        clock.Advance(TimeSpan.FromMinutes(1));
        runtime.Pulse();

        Assert.Equal(TimeSpan.FromMinutes(28), runtime.Timer.Remaining);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
