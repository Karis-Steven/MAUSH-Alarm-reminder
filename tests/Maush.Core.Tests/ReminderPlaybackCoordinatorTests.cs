using Maush.Core;

namespace Maush.Core.Tests;

public sealed class ReminderPlaybackCoordinatorTests
{
    private static TimerSettings Settings => new()
    {
        WorkDurationMinutes = 30,
        RestDurationMinutes = 5,
        IsLoopEnabled = true,
        RestReminderAudioId = "rest-audio",
        ResumeWorkReminderAudioId = "work-audio"
    };

    [Fact]
    public void WorkCompletionStartsRestCountdownAndAudioInSameOperation()
    {
        var timer = new CycleTimer(Settings);
        var player = new RecordingAudioPlayer();
        using var coordinator = new ReminderPlaybackCoordinator(timer, player);
        timer.Start();

        timer.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(TimerState.Resting, timer.State);
        Assert.Equal("rest-audio", Assert.Single(player.Played));
    }

    [Fact]
    public void RestCompletionUsesLatestConfiguredAudio()
    {
        var timer = new CycleTimer(Settings);
        var player = new RecordingAudioPlayer();
        using var coordinator = new ReminderPlaybackCoordinator(timer, player);
        timer.Start();
        timer.Advance(TimeSpan.FromMinutes(30));
        timer.ApplySettings(Settings with { ResumeWorkReminderAudioId = "new-work-audio" });

        timer.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal("new-work-audio", player.Played[^1]);
    }

    [Fact]
    public void StopImmediatelyStopsAudio()
    {
        var timer = new CycleTimer(Settings);
        var player = new RecordingAudioPlayer();
        using var coordinator = new ReminderPlaybackCoordinator(timer, player);
        timer.Start();
        timer.Advance(TimeSpan.FromMinutes(30));

        timer.Stop();

        Assert.Equal(1, player.StopCount);
        Assert.Equal(TimerState.Stopped, timer.State);
    }

    [Fact]
    public void MissingAudioDoesNotBlockTransition()
    {
        var timer = new CycleTimer(Settings with { RestReminderAudioId = null });
        var player = new RecordingAudioPlayer();
        using var coordinator = new ReminderPlaybackCoordinator(timer, player);
        timer.Start();

        timer.Advance(TimeSpan.FromMinutes(30));

        Assert.Empty(player.Played);
        Assert.Equal(TimerState.Resting, timer.State);
    }

    [Fact]
    public void PlayerFailureDoesNotBlockTransition()
    {
        var timer = new CycleTimer(Settings);
        using var coordinator = new ReminderPlaybackCoordinator(timer, new ThrowingAudioPlayer());
        Exception? failure = null;
        coordinator.PlaybackFailed += (_, exception) => failure = exception;
        timer.Start();

        timer.Advance(TimeSpan.FromMinutes(30));

        Assert.IsType<IOException>(failure);
        Assert.Equal(TimerState.Resting, timer.State);
    }

    private sealed class RecordingAudioPlayer : IAudioPlayer
    {
        public List<string> Played { get; } = [];
        public int StopCount { get; private set; }

        public void Play(string audioId) => Played.Add(audioId);
        public void Stop() => StopCount++;
    }

    private sealed class ThrowingAudioPlayer : IAudioPlayer
    {
        public void Play(string audioId) => throw new IOException("test failure");
        public void Stop() { }
    }
}
