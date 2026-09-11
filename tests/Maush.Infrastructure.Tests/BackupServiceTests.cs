using System.IO.Compression;
using Maush.Core;
using Maush.Infrastructure;

namespace Maush.Infrastructure.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"maush-backup-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportAndRestoreRecoverSettingsAndManagedAudio()
    {
        var store = new ApplicationDataStore(Path.Combine(_root, "app"));
        var configuration = await CreateConfigurationWithAudioAsync(store, 45);
        var backupPath = Path.Combine(_root, "backup.maushbackup");
        var service = new BackupService(store);
        await service.ExportAsync(configuration, backupPath);
        await store.SaveAsync(configuration with { Timer = configuration.Timer with { WorkDurationMinutes = 60 } });

        var restored = await service.RestoreAsync(backupPath);
        var loaded = await store.LoadAsync();

        Assert.Equal(45, restored.Timer.WorkDurationMinutes);
        Assert.Equal(45, loaded.Configuration.Timer.WorkDurationMinutes);
        var audioId = restored.Timer.RestReminderAudioId!;
        Assert.True(File.Exists(store.ResolveAudioPath(restored, audioId)));
    }

    [Fact]
    public async Task CorruptBackupDoesNotOverwriteCurrentConfiguration()
    {
        var store = new ApplicationDataStore(Path.Combine(_root, "app"));
        var configuration = new AppConfiguration
        {
            Timer = new TimerSettings { WorkDurationMinutes = 45, RestDurationMinutes = 10 }
        };
        await store.SaveAsync(configuration);
        var corruptBackup = Path.Combine(_root, "corrupt.maushbackup");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(corruptBackup, "not a zip");

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => new BackupService(store).RestoreAsync(corruptBackup));
        var loaded = await store.LoadAsync();

        Assert.Equal(45, loaded.Configuration.Timer.WorkDurationMinutes);
    }

    [Fact]
    public async Task BackupMissingListedAudioIsRejectedWithoutChangingCurrentData()
    {
        var store = new ApplicationDataStore(Path.Combine(_root, "app"));
        var configuration = await CreateConfigurationWithAudioAsync(store, 45);
        var backupPath = Path.Combine(_root, "backup.maushbackup");
        await new BackupService(store).ExportAsync(configuration, backupPath);

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Update))
        {
            archive.Entries.Single(entry => entry.FullName.StartsWith("audio/", StringComparison.Ordinal)).Delete();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(store).RestoreAsync(backupPath));
        var loaded = await store.LoadAsync();
        Assert.Equal(45, loaded.Configuration.Timer.WorkDurationMinutes);
    }

    private async Task<AppConfiguration> CreateConfigurationWithAudioAsync(ApplicationDataStore store, int workMinutes)
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "voice.mp3");
        await File.WriteAllBytesAsync(source, [10, 20, 30]);
        var asset = await store.ImportAudioAsync(source);
        var configuration = new AppConfiguration
        {
            Timer = new TimerSettings
            {
                WorkDurationMinutes = workMinutes,
                RestDurationMinutes = 10,
                RestReminderAudioId = asset.Id
            },
            AudioAssets = new Dictionary<string, AudioAsset>(StringComparer.OrdinalIgnoreCase)
            {
                [asset.Id] = asset
            }
        };
        await store.SaveAsync(configuration);
        return configuration;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
