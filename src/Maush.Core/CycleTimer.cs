namespace Maush.Core;

public sealed class CycleTimer
{
    private TimerSettings _settings;
    private TimerState? _pausedFromState;
    private PauseReason? _pauseReason;
    private TimeSpan? _lockedDuration;

    public CycleTimer(TimerSettings settings)
    {
        settings.Validate();
        _settings = settings;
        State = TimerState.Idle;
        Remaining = settings.WorkDuration;
    }

    public TimerState State { get; private set; }
    public TimeSpan Remaining { get; private set; }
    public TimerSettings Settings => _settings;
    public TimerState? PausedFromState => _pausedFromState;
    public PauseReason? CurrentPauseReason => _pauseReason;

    public event EventHandler<ReminderKind>? ReminderRequested;
    public event EventHandler? PlaybackCancellationRequested;
    public event EventHandler<TimerSnapshot>? Changed;

    public TimerSnapshot Snapshot => new(
        State,
        _pausedFromState,
        Remaining,
        _lockedDuration,
        _pauseReason,
        _settings);

    public void Start()
    {
        if (State is not (TimerState.Idle or TimerState.Stopped))
        {
            return;
        }

        BeginPhase(TimerState.Working, _settings.WorkDuration);
    }

    public void Pause(PauseReason reason = PauseReason.Manual)
    {
        if (State is not (TimerState.Working or TimerState.Resting))
        {
            return;
        }

        _pausedFromState = State;
        _pauseReason = reason;
        State = TimerState.Paused;
        RaiseChanged();
    }

    public void Resume()
    {
        if (State != TimerState.Paused || _pausedFromState is null)
        {
            return;
        }

        State = _pausedFromState.Value;
        _pausedFromState = null;
        _pauseReason = null;
        RaiseChanged();
    }

    public void Stop()
    {
        PlaybackCancellationRequested?.Invoke(this, EventArgs.Empty);
        State = TimerState.Stopped;
        Remaining = TimeSpan.Zero;
        _lockedDuration = null;
        _pausedFromState = null;
        _pauseReason = null;
        RaiseChanged();
    }

    public void Reset()
    {
        PlaybackCancellationRequested?.Invoke(this, EventArgs.Empty);
        State = TimerState.Idle;
        Remaining = _settings.WorkDuration;
        _lockedDuration = null;
        _pausedFromState = null;
        _pauseReason = null;
        RaiseChanged();
    }

    public void ApplySettings(TimerSettings settings)
    {
        settings.Validate();
        _settings = settings;

        if (State is TimerState.Idle or TimerState.Stopped)
        {
            Remaining = settings.WorkDuration;
        }

        RaiseChanged();
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        if (State is not (TimerState.Working or TimerState.Resting) || elapsed == TimeSpan.Zero)
        {
            return;
        }

        if (elapsed < Remaining)
        {
            Remaining -= elapsed;
            RaiseChanged();
            return;
        }

        Remaining = TimeSpan.Zero;
        CompleteCurrentPhase();
    }

    private void CompleteCurrentPhase()
    {
        if (State == TimerState.Working)
        {
            ReminderRequested?.Invoke(this, ReminderKind.Rest);
            if (State == TimerState.Stopped)
            {
                return;
            }

            if (_settings.IsLoopEnabled)
            {
                BeginPhase(TimerState.Resting, _settings.RestDuration);
            }
            else
            {
                TransitionToStopped();
            }

            return;
        }

        if (State == TimerState.Resting)
        {
            ReminderRequested?.Invoke(this, ReminderKind.ResumeWork);
            if (State == TimerState.Stopped)
            {
                return;
            }

            if (_settings.IsLoopEnabled)
            {
                BeginPhase(TimerState.Working, _settings.WorkDuration);
            }
            else
            {
                TransitionToStopped();
            }
        }
    }

    private void BeginPhase(TimerState state, TimeSpan duration)
    {
        State = state;
        Remaining = duration;
        _lockedDuration = duration;
        _pausedFromState = null;
        _pauseReason = null;
        RaiseChanged();
    }

    private void TransitionToStopped()
    {
        State = TimerState.Stopped;
        Remaining = TimeSpan.Zero;
        _lockedDuration = null;
        _pausedFromState = null;
        _pauseReason = null;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, Snapshot);
}
