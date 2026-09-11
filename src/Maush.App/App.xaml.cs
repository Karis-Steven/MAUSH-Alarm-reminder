using System.IO;
using System.Windows;
using Maush.Infrastructure;

namespace Maush.App;

public partial class App : System.Windows.Application
{
    private MainViewModel? _viewModel;
    private TrayIconService? _trayIcon;
    private SystemPauseMonitor? _systemPauseMonitor;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maush");
        var store = new ApplicationDataStore(root);
        var loadResult = await store.LoadAsync();
        var context = new ConfigurationContext(loadResult.Configuration);
        var audioPlayer = new WpfAudioPlayer(store, context);
        _viewModel = new MainViewModel(store, loadResult.Configuration, context, audioPlayer, new MessageBoxConfirmationService());
        audioPlayer.PlaybackFailed += (_, message) => _viewModel.SetLoadError($"音频播放失败：{message}");
        audioPlayer.PlaybackEnded += (_, _) => _viewModel.NotifyPlaybackEnded();
        _viewModel.SetLoadError(loadResult.Error);

        var window = new MainWindow(_viewModel);
        MainWindow = window;
        _trayIcon = new TrayIconService(window, _viewModel);
        _systemPauseMonitor = new SystemPauseMonitor(_viewModel);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _systemPauseMonitor?.Dispose();
        _trayIcon?.Dispose();
        _viewModel?.Dispose();
        base.OnExit(e);
    }
}
