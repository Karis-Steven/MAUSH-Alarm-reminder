using Maush.App;
using Maush.Core;
using Maush.Infrastructure;

namespace Maush.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"maush-ui-tests-{Guid.NewGuid():N}");

    [Fact]
    public void StartPauseResumeAndStopUpdateVisibleState()
    {
        using var viewModel = CreateViewModel();

        viewModel.StartCommand.Execute(null);
        Assert.Equal("工作中", viewModel.StatusText);
        Assert.Equal("#243B53", viewModel.StatusBackground);
        Assert.True(viewModel.PauseResumeCommand.CanExecute(null));

        viewModel.PauseResumeCommand.Execute(null);
        Assert.Equal("已暂停 · 工作中", viewModel.StatusText);
        Assert.Equal("继续", viewModel.PauseResumeText);
        Assert.Equal("#B7791F", viewModel.StatusBackground);

        viewModel.PauseResumeCommand.Execute(null);
        viewModel.StopCommand.Execute(null);
        Assert.Equal("已停止", viewModel.StatusText);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public void StopRemainsAvailableInStoppedStateSoReminderCanBeSilenced()
    {
        using var viewModel = CreateViewModel();
        viewModel.Start();
        viewModel.Stop();

        Assert.Equal(TimerState.Stopped, viewModel.State);
        Assert.True(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public void InvalidManualDurationShowsErrorAndDoesNotApply()
    {
        using var viewModel = CreateViewModel();

        viewModel.WorkMinutes = "0";

        Assert.True(viewModel.HasError);
        Assert.Equal(30, viewModel.Configuration.Timer.WorkDurationMinutes);
    }

    [Fact]
    public void ResetCanBeCancelledWhileRunning()
    {
        using var viewModel = CreateViewModel(confirm: false);
        viewModel.Start();

        viewModel.Reset();

        Assert.Equal(TimerState.Working, viewModel.State);
    }

    [Fact]
    public void SystemAutoPauseRequiresExplicitResume()
    {
        using var viewModel = CreateViewModel();
        viewModel.Start();

        viewModel.AutoPause();

        Assert.Equal(TimerState.Paused, viewModel.State);
        Assert.Equal("因系统暂停，等待继续", viewModel.NextStepText);
        Assert.Equal("继续", viewModel.PauseResumeText);
    }

    [Fact]
    public async Task SelectingAudioCreatesManagedAssetAndUpdatesVisibleName()
    {
        using var viewModel = CreateViewModel();
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "my-reminder.mp3");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);

        await viewModel.SelectAudioAsync(ReminderKind.Rest, source);

        Assert.Equal("my-reminder.mp3", viewModel.RestAudioName);
        Assert.NotNull(viewModel.Configuration.Timer.RestReminderAudioId);
        Assert.False(viewModel.HasError);
    }

    private MainViewModel CreateViewModel(bool confirm = true)
    {
        var configuration = new AppConfiguration();
        return new MainViewModel(
            new ApplicationDataStore(Path.Combine(_root, "app")),
            configuration,
            new ConfigurationContext(configuration),
            new RecordingAudioPlayer(),
            new FixedConfirmationService(confirm),
            enableUiTimer: false);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class FixedConfirmationService(bool result) : IConfirmationService
    {
        public bool Confirm(string message, string title) => result;
    }

    private sealed class RecordingAudioPlayer : IAudioPlayer
    {
        public void Play(string audioId) { }
        public void Stop() { }
    }
}
