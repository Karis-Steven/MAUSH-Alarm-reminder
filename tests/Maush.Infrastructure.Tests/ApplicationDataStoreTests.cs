using Maush.Core;
using Maush.Infrastructure;

namespace Maush.Infrastructure.Tests;

public sealed class ApplicationDataStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"maush-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadRoundTripConfiguration()
    {
        var store = new ApplicationDataStore(_root);
        var configuration = new AppConfiguration
        {
            Timer = new TimerSettings { WorkDurationMinutes = 45, RestDurationMinutes = 10, IsLoopEnabled = false }
        };

        await store.SaveAsync(configuration);
        var result = await store.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(configuration.Timer, result.Configuration.Timer);
    }

    [Fact]
    public async Task CorruptSettingsReturnDefaultsWithoutOverwritingFile()
    {
        var store = new ApplicationDataStore(_root);
        Directory.CreateDirectory(store.CurrentDirectory);
        await File.WriteAllTextAsync(store.SettingsPath, "{not json");

        var result = await store.LoadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(30, result.Configuration.Timer.WorkDurationMinutes);
        Assert.Equal("{not json", await File.ReadAllTextAsync(store.SettingsPath));
    }

    [Theory]
    [InlineData("sample.mp3")]
    [InlineData("sample.wav")]
    [InlineData("sample.m4a")]
    public async Task ImportAudioCreatesIndependentManagedCopy(string fileName)
    {
        var source = Path.Combine(_root, fileName);
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var store = new ApplicationDataStore(Path.Combine(_root, "data"));

        var asset = await store.ImportAudioAsync(source);
        File.Delete(source);

        Assert.True(File.Exists(Path.Combine(store.AudioDirectory, asset.ManagedFileName)));
        Assert.Equal(fileName, asset.OriginalFileName);
    }

    [Fact]
    public async Task ImportAudioRejectsUnsupportedExtension()
    {
        var source = Path.Combine(_root, "sample.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(source, "not audio");
        var store = new ApplicationDataStore(Path.Combine(_root, "data"));

        await Assert.ThrowsAsync<NotSupportedException>(() => store.ImportAudioAsync(source));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
