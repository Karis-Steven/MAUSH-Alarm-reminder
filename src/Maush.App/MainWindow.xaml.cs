using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Maush.Core;
using Microsoft.Win32;

namespace Maush.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += OnClosing;
    }

    public event EventHandler? HiddenToTray;

    public void PrepareForExit() => _allowClose = true;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void SetWorkPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value }) _viewModel.WorkMinutes = value;
    }

    private void SetRestPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value }) _viewModel.RestMinutes = value;
    }

    private async void SelectRestAudio_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectAudioFile();
        if (path is not null) await _viewModel.SelectAudioAsync(ReminderKind.Rest, path);
    }

    private async void SelectResumeAudio_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectAudioFile();
        if (path is not null) await _viewModel.SelectAudioAsync(ReminderKind.ResumeWork, path);
    }

    private void PreviewRestAudio_Click(object sender, RoutedEventArgs e) => _viewModel.TogglePreview(ReminderKind.Rest);
    private void PreviewResumeAudio_Click(object sender, RoutedEventArgs e) => _viewModel.TogglePreview(ReminderKind.ResumeWork);

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出久坐提醒器备份",
            Filter = "久坐提醒器备份 (*.maushbackup)|*.maushbackup",
            FileName = $"maush-backup-{DateTime.Now:yyyyMMdd}.maushbackup"
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.ExportBackupAsync(dialog.FileName);
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "恢复久坐提醒器备份",
            Filter = "久坐提醒器备份 (*.maushbackup)|*.maushbackup"
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.RestoreBackupAsync(dialog.FileName);
    }

    private static string? SelectAudioFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择本地提醒音频",
            Filter = "音频文件 (*.m4a;*.wav;*.mp3)|*.m4a;*.wav;*.mp3"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
