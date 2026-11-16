using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace HyperNotch.Services;

public sealed class MediaInfo
{
    public bool HasMedia { get; init; }
    public bool IsPlaying { get; init; }
    public string Title { get; init; } = "Nessun brano";
    public string Artist { get; init; } = "In attesa di riproduzione";
    public string SourceApp { get; init; } = string.Empty;
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public BitmapImage? Thumbnail { get; init; }
}

public sealed class MediaService : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string _lastTrackKey = string.Empty;
    private BitmapImage? _cachedThumb;

    public event Action<MediaInfo>? MediaUpdated;

    public void Start()
    {
        _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                await RefreshOnceAsync();
            }
            catch
            {
                // Ignore transient WinRT errors
            }

            try
            {
                await Task.Delay(1000, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshOnceAsync()
    {
        if (_manager is null) return;

        var session = _manager.GetCurrentSession();
        if (session is null)
        {
            if (_lastTrackKey != "NONE")
            {
                _lastTrackKey = "NONE";
                _cachedThumb = null;
                MediaUpdated?.Invoke(new MediaInfo());
            }
            return;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        string title = props?.Title?.Trim() ?? string.Empty;
        string artist = props?.Artist?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
        {
            if (_lastTrackKey != "NONE")
            {
                _lastTrackKey = "NONE";
                _cachedThumb = null;
                MediaUpdated?.Invoke(new MediaInfo());
            }
            return;
        }

        string trackKey = $"{title}|{artist}";
        if (trackKey != _lastTrackKey)
        {
            _lastTrackKey = trackKey;
            _cachedThumb = await LoadThumbnailAsync(props);
        }

        TimeSpan duration = timeline?.EndTime ?? TimeSpan.Zero;
        TimeSpan position = timeline?.Position ?? TimeSpan.Zero;
        if (isPlaying && timeline is not null && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
        {
            var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(12))
                position += elapsed;
        }
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (position > duration && duration > TimeSpan.Zero)
            position = duration;

        MediaUpdated?.Invoke(new MediaInfo
        {
            HasMedia = true,
            IsPlaying = isPlaying,
            Title = string.IsNullOrEmpty(title) ? "Titolo sconosciuto" : title,
            Artist = string.IsNullOrEmpty(artist) ? "Artista sconosciuto" : artist,
            SourceApp = FormatSourceApp(session.SourceAppUserModelId),
            Position = position,
            Duration = duration,
            Thumbnail = _cachedThumb
        });
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        if (props?.Thumbnail is null) return null;
        try
        {
            using var winrtStream = await props.Thumbnail.OpenReadAsync();
            using var netStream = winrtStream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatSourceApp(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return string.Empty;
        string lower = aumid.ToLowerInvariant();
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("applemusic") || lower.Contains("apple.music")) return "Apple Music";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("vlc")) return "VLC";
        if (lower.Contains("zune") || lower.Contains("mediaplayer")) return "Media Player";
        return "Media";
    }

    public async Task TogglePlayPauseAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession();
            if (session is not null)
            {
                await session.TryTogglePlayPauseAsync();
                await Task.Delay(120);
                await RefreshOnceAsync();
            }
        }
        catch { }
    }

    public async Task NextAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession();
            if (session is not null)
            {
                await session.TrySkipNextAsync();
                await Task.Delay(220);
                _lastTrackKey = string.Empty;
                await RefreshOnceAsync();
            }
        }
        catch { }
    }

    public async Task PreviousAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession();
            if (session is not null)
            {
                await session.TrySkipPreviousAsync();
                await Task.Delay(220);
                _lastTrackKey = string.Empty;
                await RefreshOnceAsync();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
