namespace Maush.Core;

public sealed class ReminderPlaybackCoordinator : IDisposable
{
    private readonly CycleTimer _timer;
    private readonly IAudioPlayer _audioPlayer;
    private bool _disposed;

    public event EventHandler<Exception>? PlaybackFailed;

    public ReminderPlaybackCoordinator(CycleTimer timer, IAudioPlayer audioPlayer)
    {
        _timer = timer;
        _audioPlayer = audioPlayer;
        _timer.ReminderRequested += OnReminderRequested;
        _timer.PlaybackCancellationRequested += OnPlaybackCancellationRequested;
    }

    private void OnReminderRequested(object? sender, ReminderKind kind)
    {
        var audioId = kind == ReminderKind.Rest
            ? _timer.Settings.RestReminderAudioId
            : _timer.Settings.ResumeWorkReminderAudioId;

        if (!string.IsNullOrWhiteSpace(audioId))
        {
            try
            {
                _audioPlayer.Play(audioId);
            }
            catch (Exception exception)
            {
                PlaybackFailed?.Invoke(this, exception);
            }
        }
    }

    private void OnPlaybackCancellationRequested(object? sender, EventArgs e) => _audioPlayer.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.ReminderRequested -= OnReminderRequested;
        _timer.PlaybackCancellationRequested -= OnPlaybackCancellationRequested;
        _disposed = true;
    }
}
