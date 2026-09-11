using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Maush.Core;

namespace Maush.Infrastructure;

public sealed class BackupService
{
    private readonly ApplicationDataStore _store;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public BackupService(ApplicationDataStore store) => _store = store;

    public async Task ExportAsync(
        AppConfiguration configuration,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        _store.ValidateConfiguration(configuration, requireAudioFiles: true);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestinationPath)!);
        var temporaryPath = fullDestinationPath + ".tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["settings.json"] = _store.SettingsPath
        };
        foreach (var asset in configuration.AudioAssets.Values)
        {
            files[$"audio/{asset.ManagedFileName}"] = Path.Combine(_store.AudioDirectory, asset.ManagedFileName);
        }

        var manifest = new BackupManifest();
        foreach (var file in files)
        {
            manifest.Sha256ByPath[file.Key] = await ComputeSha256Async(file.Value, cancellationToken);
        }

        await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var source = File.OpenRead(file.Value);
                await source.CopyToAsync(entryStream, cancellationToken);
            }

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, fullDestinationPath, true);
    }

    public async Task<AppConfiguration> RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var fullBackupPath = Path.GetFullPath(backupPath);
        if (!File.Exists(fullBackupPath))
        {
            throw new FileNotFoundException("找不到备份文件。", fullBackupPath);
        }

        Directory.CreateDirectory(_store.RootDirectory);
        var stagingDirectory = Path.Combine(_store.RootDirectory, $".restore-{Guid.NewGuid():N}");
        var rollbackDirectory = Path.Combine(_store.RootDirectory, $".rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            await ExtractAndValidateAsync(fullBackupPath, stagingDirectory, cancellationToken);
            var configuration = await ReadConfigurationAsync(Path.Combine(stagingDirectory, "settings.json"), cancellationToken);
            ValidateStagedConfiguration(configuration, stagingDirectory);

            var hadCurrent = Directory.Exists(_store.CurrentDirectory);
            if (hadCurrent)
            {
                Directory.Move(_store.CurrentDirectory, rollbackDirectory);
            }

            try
            {
                Directory.Move(stagingDirectory, _store.CurrentDirectory);
            }
            catch
            {
                if (hadCurrent && Directory.Exists(rollbackDirectory) && !Directory.Exists(_store.CurrentDirectory))
                {
                    Directory.Move(rollbackDirectory, _store.CurrentDirectory);
                }

                throw;
            }

            if (Directory.Exists(rollbackDirectory))
            {
                Directory.Delete(rollbackDirectory, true);
            }

            return configuration;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
    }

    private async Task ExtractAndValidateAsync(string backupPath, string stagingDirectory, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("备份缺少 manifest.json。");
        BackupManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, _jsonOptions, cancellationToken)
                ?? throw new InvalidDataException("备份清单为空。");
        }

        if (manifest.BackupVersion != BackupManifest.CurrentBackupVersion)
        {
            throw new InvalidDataException($"不支持的备份版本：{manifest.BackupVersion}。");
        }

        if (!manifest.Sha256ByPath.ContainsKey("settings.json"))
        {
            throw new InvalidDataException("备份清单缺少配置文件。");
        }

        foreach (var listedFile in manifest.Sha256ByPath)
        {
            var normalizedPath = listedFile.Key.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(stagingDirectory, normalizedPath));
            var stagingPrefix = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
            if (!destinationPath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("备份包含不安全的文件路径。");
            }

            var entry = archive.GetEntry(listedFile.Key)
                ?? throw new InvalidDataException($"备份缺少文件：{listedFile.Key}。");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using (var input = entry.Open())
            await using (var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var actualHash = await ComputeSha256Async(destinationPath, cancellationToken);
            if (!string.Equals(actualHash, listedFile.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"备份文件校验失败：{listedFile.Key}。");
            }
        }
    }

    private async Task<AppConfiguration> ReadConfigurationAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppConfiguration>(stream, _jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("备份配置为空。");
    }

    private static void ValidateStagedConfiguration(AppConfiguration configuration, string stagingDirectory)
    {
        if (configuration.SchemaVersion != AppConfiguration.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持的配置版本：{configuration.SchemaVersion}。");
        }

        configuration.Timer.Validate();
        foreach (var pair in configuration.AudioAssets)
        {
            var asset = pair.Value;
            if (!string.Equals(pair.Key, asset.Id, StringComparison.OrdinalIgnoreCase) ||
                asset.ManagedFileName != Path.GetFileName(asset.ManagedFileName))
            {
                throw new InvalidDataException("备份中的音频索引无效。");
            }

            var audioPath = Path.Combine(stagingDirectory, "audio", asset.ManagedFileName);
            if (!File.Exists(audioPath))
            {
                throw new InvalidDataException($"备份缺少音频：{asset.OriginalFileName}。");
            }
        }

        ValidateReference(configuration.Timer.RestReminderAudioId, configuration);
        ValidateReference(configuration.Timer.ResumeWorkReminderAudioId, configuration);
    }

    private static void ValidateReference(string? id, AppConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(id) && !configuration.AudioAssets.ContainsKey(id))
        {
            throw new InvalidDataException($"备份配置引用了不存在的音频：{id}。");
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
