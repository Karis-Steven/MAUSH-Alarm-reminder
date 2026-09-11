using System.Text.Json;
using Maush.Core;

namespace Maush.Infrastructure;

public sealed class ApplicationDataStore
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a"
    };

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ApplicationDataStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("数据目录不能为空。", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }
    public string CurrentDirectory => Path.Combine(RootDirectory, "current");
    public string AudioDirectory => Path.Combine(CurrentDirectory, "audio");
    public string SettingsPath => Path.Combine(CurrentDirectory, "settings.json");

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(configuration, requireAudioFiles: false);
        Directory.CreateDirectory(CurrentDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, configuration, _jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, SettingsPath, true);
    }

    public async Task<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new ConfigurationLoadResult(new AppConfiguration(), null);
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var configuration = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream, _jsonOptions, cancellationToken)
                ?? throw new InvalidDataException("配置文件内容为空。");
            ValidateConfiguration(configuration, requireAudioFiles: false);
            return new ConfigurationLoadResult(configuration, null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or ArgumentException)
        {
            return new ConfigurationLoadResult(new AppConfiguration(), $"配置加载失败：{exception.Message}");
        }
    }

    public async Task<AudioAsset> ImportAudioAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("找不到所选音频文件。", fullSourcePath);
        }

        var extension = Path.GetExtension(fullSourcePath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException("仅支持 M4A、WAV 和 MP3 音频文件。");
        }

        Directory.CreateDirectory(AudioDirectory);
        var id = Guid.NewGuid().ToString("N");
        var managedFileName = id + extension.ToLowerInvariant();
        var destinationPath = Path.Combine(AudioDirectory, managedFileName);
        var temporaryPath = destinationPath + ".tmp";

        await using (var source = new FileStream(fullSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
        await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, destinationPath);
        return new AudioAsset(id, Path.GetFileName(fullSourcePath), managedFileName);
    }

    public string ResolveAudioPath(AppConfiguration configuration, string audioId)
    {
        if (!configuration.AudioAssets.TryGetValue(audioId, out var asset))
        {
            throw new FileNotFoundException("配置中找不到对应的音频。");
        }

        ValidateManagedFileName(asset.ManagedFileName);
        return Path.Combine(AudioDirectory, asset.ManagedFileName);
    }

    internal void ValidateConfiguration(AppConfiguration configuration, bool requireAudioFiles)
    {
        if (configuration.SchemaVersion != AppConfiguration.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持的配置版本：{configuration.SchemaVersion}。");
        }

        configuration.Timer.Validate();
        foreach (var pair in configuration.AudioAssets)
        {
            if (!string.Equals(pair.Key, pair.Value.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("音频索引与音频 ID 不一致。");
            }

            ValidateManagedFileName(pair.Value.ManagedFileName);
            if (requireAudioFiles && !File.Exists(Path.Combine(AudioDirectory, pair.Value.ManagedFileName)))
            {
                throw new InvalidDataException($"缺少音频文件：{pair.Value.OriginalFileName}。");
            }
        }

        ValidateAudioReference(configuration.Timer.RestReminderAudioId, configuration);
        ValidateAudioReference(configuration.Timer.ResumeWorkReminderAudioId, configuration);
    }

    private static void ValidateAudioReference(string? audioId, AppConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(audioId) && !configuration.AudioAssets.ContainsKey(audioId))
        {
            throw new InvalidDataException($"配置引用了不存在的音频：{audioId}。");
        }
    }

    private static void ValidateManagedFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
        {
            throw new InvalidDataException("音频文件名不安全。");
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(fileName)))
        {
            throw new InvalidDataException("音频文件格式不受支持。");
        }
    }
}
