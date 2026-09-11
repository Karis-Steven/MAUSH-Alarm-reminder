namespace Maush.Core;

public sealed class TimerRuntime
{
    private readonly CycleTimer _timer;
    private readonly TimeProvider _timeProvider;
    private long? _lastTimestamp;

    public TimerRuntime(CycleTimer timer, TimeProvider? timeProvider = null)
    {
        _timer = timer;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CycleTimer Timer => _timer;

    public void Start()
    {
        _timer.Start();
        if (_timer.State == TimerState.Working)
        {
            _lastTimestamp = _timeProvider.GetTimestamp();
        }
    }

    public void Pause(PauseReason reason = PauseReason.Manual)
    {
        if (reason == PauseReason.Manual)
        {
            Pulse();
        }

        _timer.Pause(reason);
        _lastTimestamp = null;
    }

    public void Resume()
    {
        _timer.Resume();
        if (_timer.State is TimerState.Working or TimerState.Resting)
        {
            _lastTimestamp = _timeProvider.GetTimestamp();
        }
    }

    public void Stop()
    {
        _lastTimestamp = null;
        _timer.Stop();
    }

    public void Reset()
    {
        _lastTimestamp = null;
        _timer.Reset();
    }

    public void ApplySettings(TimerSettings settings) => _timer.ApplySettings(settings);

    public void Pulse()
    {
        if (_timer.State is not (TimerState.Working or TimerState.Resting))
        {
            return;
        }

        var now = _timeProvider.GetTimestamp();
        if (_lastTimestamp is null)
        {
            _lastTimestamp = now;
            return;
        }

        var elapsed = _timeProvider.GetElapsedTime(_lastTimestamp.Value, now);
        _lastTimestamp = now;
        _timer.Advance(elapsed);
    }
}
