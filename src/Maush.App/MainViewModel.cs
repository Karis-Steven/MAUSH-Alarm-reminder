using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Maush.Core;
using Maush.Infrastructure;

namespace Maush.App;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ApplicationDataStore _store;
    private readonly BackupService _backupService;
    private readonly ConfigurationContext _configurationContext;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IConfirmationService _confirmationService;
    private readonly CycleTimer _timer;
    private readonly TimerRuntime _runtime;
    private readonly ReminderPlaybackCoordinator _playbackCoordinator;
    private readonly DispatcherTimer? _uiTimer;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppConfiguration _configuration;
    private string _workMinutes;
    private string _restMinutes;
    private bool _isLoopEnabled;
    private string? _errorMessage;
    private string? _noticeMessage;
    private bool _isPreviewPlaying;
    private bool _disposed;

    public MainViewModel(
        ApplicationDataStore store,
        AppConfiguration configuration,
        ConfigurationContext configurationContext,
        IAudioPlayer audioPlayer,
        IConfirmationService confirmationService,
        bool enableUiTimer = true)
    {
        _store = store;
        _backupService = new BackupService(store);
        _configuration = configuration;
        _configurationContext = configurationContext;
        _audioPlayer = audioPlayer;
        _confirmationService = confirmationService;
        _workMinutes = configuration.Timer.WorkDurationMinutes.ToString();
        _restMinutes = configuration.Timer.RestDurationMinutes.ToString();
        _isLoopEnabled = configuration.Timer.IsLoopEnabled;
        _timer = new CycleTimer(configuration.Timer);
        _runtime = new TimerRuntime(_timer);
        _playbackCoordinator = new ReminderPlaybackCoordinator(_timer, audioPlayer);
        _timer.Changed += OnTimerChanged;

        StartCommand = new DelegateCommand(Start, () => State is TimerState.Idle or TimerState.Stopped);
        PauseResumeCommand = new DelegateCommand(PauseOrResume, () => State is TimerState.Working or TimerState.Resting or TimerState.Paused);
        StopCommand = new DelegateCommand(Stop);
        ResetCommand = new DelegateCommand(Reset);

        if (enableUiTimer)
        {
            _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _uiTimer.Tick += (_, _) => _runtime.Pulse();
            _uiTimer.Start();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand StartCommand { get; }
    public ICommand PauseResumeCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetCommand { get; }

    public TimerState State => _timer.State;
    public string StatusText => State switch
    {
        TimerState.Idle => "未开始",
        TimerState.Working => "工作中",
        TimerState.Resting => "休息中",
        TimerState.Paused when _timer.PausedFromState == TimerState.Working => "已暂停 · 工作中",
        TimerState.Paused when _timer.PausedFromState == TimerState.Resting => "已暂停 · 休息中",
        TimerState.Stopped => "已停止",
        _ => "已暂停"
    };

    public string RemainingText
    {
        get
        {
            var value = _timer.Remaining < TimeSpan.Zero ? TimeSpan.Zero : _timer.Remaining;
            var totalHours = (int)value.TotalHours;
            return totalHours > 0
                ? $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
                : $"{value.Minutes:00}:{value.Seconds:00}";
        }
    }

    public string NextStepText => State switch
    {
        TimerState.Working => "下一步：提醒休息",
        TimerState.Resting => "下一步：提醒继续工作",
        TimerState.Paused when _timer.CurrentPauseReason == PauseReason.System => "因系统暂停，等待继续",
        TimerState.Paused => "计时已暂停",
        TimerState.Stopped => "点击开始，启动新一轮",
        _ => "设置时长后开始"
    };

    public string PauseResumeText => State == TimerState.Paused ? "继续" : "暂停";
    public string PreviewButtonText => _isPreviewPlaying ? "停止试听" : "试听";
    public string StatusBackground => State switch
    {
        TimerState.Resting => "#2F855A",
        TimerState.Paused => "#B7791F",
        TimerState.Idle or TimerState.Stopped => "#59636E",
        _ => "#243B53"
    };

    public string WorkMinutes
    {
        get => _workMinutes;
        set
        {
            if (SetField(ref _workMinutes, value))
            {
                ApplyTextSettings();
            }
        }
    }

    public string RestMinutes
    {
        get => _restMinutes;
        set
        {
            if (SetField(ref _restMinutes, value))
            {
                ApplyTextSettings();
            }
        }
    }

    public bool IsLoopEnabled
    {
        get => _isLoopEnabled;
        set
        {
            if (SetField(ref _isLoopEnabled, value))
            {
                ApplyTextSettings();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? NoticeMessage
    {
        get => _noticeMessage;
        private set
        {
            if (SetField(ref _noticeMessage, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);
    public AppConfiguration Configuration => _configuration;
    public string RestAudioName => ResolveAudioName(_configuration.Timer.RestReminderAudioId);
    public string ResumeAudioName => ResolveAudioName(_configuration.Timer.ResumeWorkReminderAudioId);

    public void Start() => _runtime.Start();

    public void PauseOrResume()
    {
        if (State == TimerState.Paused)
        {
            _runtime.Resume();
        }
        else
        {
            _runtime.Pause();
        }
    }

    public void Stop()
    {
        _isPreviewPlaying = false;
        OnPropertyChanged(nameof(PreviewButtonText));
        _runtime.Stop();
    }

    public void Reset()
    {
        if (State is TimerState.Working or TimerState.Resting or TimerState.Paused &&
            !_confirmationService.Confirm("重置将结束当前计时，是否继续？", "确认重置"))
        {
            return;
        }

        _isPreviewPlaying = false;
        OnPropertyChanged(nameof(PreviewButtonText));
        _runtime.Reset();
    }

    public void AutoPause()
    {
        if (State is TimerState.Working or TimerState.Resting)
        {
            _runtime.Pause(PauseReason.System);
        }
    }

    public async Task SelectAudioAsync(ReminderKind kind, string sourcePath)
    {
        try
        {
            var asset = await _store.ImportAudioAsync(sourcePath);
            var assets = new Dictionary<string, AudioAsset>(_configuration.AudioAssets, StringComparer.OrdinalIgnoreCase)
            {
                [asset.Id] = asset
            };
            var timerSettings = kind == ReminderKind.Rest
                ? _configuration.Timer with { RestReminderAudioId = asset.Id }
                : _configuration.Timer with { ResumeWorkReminderAudioId = asset.Id };
            _configuration = _configuration with { Timer = timerSettings, AudioAssets = assets };
            _configurationContext.Current = _configuration;
            _runtime.ApplySettings(timerSettings);
            await PersistAsync();
            ErrorMessage = null;
            NoticeMessage = "提醒音已保存，将从下一次提醒起生效。";
            OnPropertyChanged(nameof(RestAudioName));
            OnPropertyChanged(nameof(ResumeAudioName));
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
    }

    public void TogglePreview(ReminderKind kind)
    {
        if (_isPreviewPlaying)
        {
            _audioPlayer.Stop();
            _isPreviewPlaying = false;
            OnPropertyChanged(nameof(PreviewButtonText));
            NoticeMessage = "试听已停止。";
            return;
        }

        var id = kind == ReminderKind.Rest
            ? _configuration.Timer.RestReminderAudioId
            : _configuration.Timer.ResumeWorkReminderAudioId;
        if (string.IsNullOrWhiteSpace(id))
        {
            ErrorMessage = "请先选择提醒音频。";
            return;
        }

        _audioPlayer.Play(id);
        _isPreviewPlaying = true;
        OnPropertyChanged(nameof(PreviewButtonText));
        ErrorMessage = null;
        NoticeMessage = "正在试听；再次点击可停止。";
    }

    public async Task ExportBackupAsync(string destinationPath)
    {
        try
        {
            await PersistAsync();
            await _backupService.ExportAsync(_configuration, destinationPath);
            ErrorMessage = null;
            NoticeMessage = "备份已成功导出。";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorMessage = $"备份导出失败：{exception.Message}";
        }
    }

    public async Task RestoreBackupAsync(string sourcePath)
    {
        if (!_confirmationService.Confirm("恢复备份将替换当前时长、循环和提醒音设置，是否继续？", "确认恢复"))
        {
            return;
        }

        try
        {
            var restored = await _backupService.RestoreAsync(sourcePath);
            ApplyConfiguration(restored);
            ErrorMessage = null;
            NoticeMessage = "备份已恢复，无需重启。运行中的当前阶段保持不变。";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorMessage = $"备份恢复失败：{exception.Message}";
        }
    }

    public bool ConfirmExit() => State is not (TimerState.Working or TimerState.Resting or TimerState.Paused) ||
        _confirmationService.Confirm("计时正在进行，退出将彻底结束循环，是否退出？", "确认退出");

    public void NotifyPlaybackEnded()
    {
        if (_isPreviewPlaying)
        {
            _isPreviewPlaying = false;
            OnPropertyChanged(nameof(PreviewButtonText));
            NoticeMessage = "试听已结束。";
        }
    }

    public void SetLoadError(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            ErrorMessage = error;
        }
    }

    private void ApplyTextSettings()
    {
        if (!int.TryParse(_workMinutes, out var work) || work is < TimerSettings.MinimumMinutes or > TimerSettings.MaximumMinutes ||
            !int.TryParse(_restMinutes, out var rest) || rest is < TimerSettings.MinimumMinutes or > TimerSettings.MaximumMinutes)
        {
            ErrorMessage = "工作和休息时长必须是 1–999 的整数分钟。";
            return;
        }

        var settings = _configuration.Timer with
        {
            WorkDurationMinutes = work,
            RestDurationMinutes = rest,
            IsLoopEnabled = _isLoopEnabled
        };
        _configuration = _configuration with { Timer = settings };
        _configurationContext.Current = _configuration;
        _runtime.ApplySettings(settings);
        ErrorMessage = null;
        NoticeMessage = State is TimerState.Working or TimerState.Resting or TimerState.Paused
            ? "已保存，将从下一轮生效。"
            : "设置已保存。";
        _ = PersistAsync();
    }

    private void ApplyConfiguration(AppConfiguration configuration)
    {
        _configuration = configuration;
        _configurationContext.Current = configuration;
        _workMinutes = configuration.Timer.WorkDurationMinutes.ToString();
        _restMinutes = configuration.Timer.RestDurationMinutes.ToString();
        _isLoopEnabled = configuration.Timer.IsLoopEnabled;
        _runtime.ApplySettings(configuration.Timer);
        OnPropertyChanged(nameof(WorkMinutes));
        OnPropertyChanged(nameof(RestMinutes));
        OnPropertyChanged(nameof(IsLoopEnabled));
        OnPropertyChanged(nameof(RestAudioName));
        OnPropertyChanged(nameof(ResumeAudioName));
    }

    private async Task PersistAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            await _store.SaveAsync(_configuration);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private string ResolveAudioName(string? id) =>
        !string.IsNullOrWhiteSpace(id) && _configuration.AudioAssets.TryGetValue(id, out var asset)
            ? asset.OriginalFileName
            : "尚未选择";

    private void OnTimerChanged(object? sender, TimerSnapshot snapshot)
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(NextStepText));
        OnPropertyChanged(nameof(PauseResumeText));
        OnPropertyChanged(nameof(StatusBackground));
        ((DelegateCommand)StartCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)PauseResumeCommand).RaiseCanExecuteChanged();
        ((DelegateCommand)StopCommand).RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _uiTimer?.Stop();
        _runtime.Stop();
        _timer.Changed -= OnTimerChanged;
        _playbackCoordinator.Dispose();
        if (_audioPlayer is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _saveGate.Dispose();
        _disposed = true;
    }
}
