using Microsoft.Win32;

namespace Maush.App;

public sealed class SystemPauseMonitor : IDisposable
{
    private readonly MainViewModel _viewModel;
    private bool _disposed;

    public SystemPauseMonitor(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            DispatchAutoPause();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            DispatchAutoPause();
        }
    }

    private void DispatchAutoPause()
    {
        var application = System.Windows.Application.Current;
        if (application?.Dispatcher is null || application.Dispatcher.CheckAccess())
        {
            _viewModel.AutoPause();
        }
        else
        {
            application.Dispatcher.BeginInvoke(_viewModel.AutoPause);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _disposed = true;
    }
}
