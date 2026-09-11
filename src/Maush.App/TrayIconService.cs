using System.ComponentModel;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Maush.App;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _pauseResumeItem;
    private readonly Forms.ToolStripMenuItem _stopItem;
    private bool _hasShownBackgroundTip;
    private bool _disposed;

    public TrayIconService(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _statusItem = new Forms.ToolStripMenuItem { Enabled = false };
        _pauseResumeItem = new Forms.ToolStripMenuItem();
        _stopItem = new Forms.ToolStripMenuItem("停止");

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => Dispatch(ShowWindow));
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_pauseResumeItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => Dispatch(ExitApplication));

        _pauseResumeItem.Click += (_, _) => Dispatch(_viewModel.PauseOrResume);
        _stopItem.Click += (_, _) => Dispatch(_viewModel.Stop);
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "久坐提醒器",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(ShowWindow);
        _window.HiddenToTray += OnHiddenToTray;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateMenu();
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void ExitApplication()
    {
        if (!_viewModel.ConfirmExit())
        {
            return;
        }

        _viewModel.Stop();
        _window.PrepareForExit();
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnHiddenToTray(object? sender, EventArgs e)
    {
        if (_hasShownBackgroundTip)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "久坐提醒器仍在运行";
        _notifyIcon.BalloonTipText = "计时会在后台继续，可双击托盘图标重新打开。";
        _notifyIcon.ShowBalloonTip(3000);
        _hasShownBackgroundTip = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State) or nameof(MainViewModel.RemainingText) or nameof(MainViewModel.PauseResumeText))
        {
            UpdateMenu();
        }
    }

    private void UpdateMenu()
    {
        _statusItem.Text = $"当前：{_viewModel.StatusText} · {_viewModel.RemainingText}";
        _pauseResumeItem.Text = _viewModel.PauseResumeText;
        _pauseResumeItem.Enabled = _viewModel.PauseResumeCommand.CanExecute(null);
        _stopItem.Enabled = _viewModel.StopCommand.CanExecute(null);
        _notifyIcon.Text = $"久坐提醒器 - {_viewModel.StatusText}";
    }

    private static void Dispatch(Action action)
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) action();
        else System.Windows.Application.Current.Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _window.HiddenToTray -= OnHiddenToTray;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }
}
