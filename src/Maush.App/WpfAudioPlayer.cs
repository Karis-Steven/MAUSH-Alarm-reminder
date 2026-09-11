using System.IO;
using System.Windows.Media;
using Maush.Core;
using Maush.Infrastructure;

namespace Maush.App;

public sealed class WpfAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly ApplicationDataStore _store;
    private readonly ConfigurationContext _configuration;

    public WpfAudioPlayer(ApplicationDataStore store, ConfigurationContext configuration)
    {
        _store = store;
        _configuration = configuration;
        _player.MediaFailed += (_, args) => PlaybackFailed?.Invoke(this, args.ErrorException?.Message ?? "无法播放音频。");
        _player.MediaEnded += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler? PlaybackEnded;

    public void Play(string audioId)
    {
        try
        {
            var path = _store.ResolveAudioPath(_configuration.Current, audioId);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("提醒音频已被移动或删除。", path);
            }

            _player.Stop();
            _player.Close();
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            PlaybackFailed?.Invoke(this, exception.Message);
        }
    }

    public void Stop()
    {
        _player.Stop();
        _player.Close();
    }

    public void Dispose() => Stop();
}
