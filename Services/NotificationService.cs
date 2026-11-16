using System.IO;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace HyperNotch.Services;

public sealed class LiveNotification
{
    public string AppName { get; init; } = "Notifica";
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public BitmapImage? AppIcon { get; init; }
}

public sealed class NotificationService : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<uint> _seenIds = new();
    private UserNotificationListener? _listener;

    public event Action<LiveNotification>? NotificationReceived;

    public void Start()
    {
        _ = Task.Run(() => InitAndPollAsync(_cts.Token));
    }

    private async Task InitAndPollAsync(CancellationToken token)
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            if (status != UserNotificationListenerAccessStatus.Allowed)
                return;

            // Seed existing notifications so we only show newly arriving ones
            var initial = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in initial)
                _seenIds.Add(n.Id);
        }
        catch
        {
            return; // Graceful fallback if notification access is unavailable
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1200, token);
                if (_listener is null) break;

                var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                if (_seenIds.Count > 500)
                {
                    _seenIds.Clear();
                    foreach (var item in list)
                        _seenIds.Add(item.Id);
                    continue;
                }

                foreach (var n in list)
                {
                    if (_seenIds.Add(n.Id))
                    {
                        var parsed = await ParseNotificationAsync(n);
                        if (parsed is not null)
                            NotificationReceived?.Invoke(parsed);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient WinRT errors
            }
        }
    }

    private static async Task<LiveNotification?> ParseNotificationAsync(UserNotification notif)
    {
        try
        {
            var binding = notif.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) return null;

            var textElements = binding.GetTextElements().ToList();
            string title = textElements.Count > 0 ? textElements[0].Text : string.Empty;
            string body = textElements.Count > 1
                ? string.Join(" ", textElements.Skip(1).Select(t => t.Text).Where(s => !string.IsNullOrWhiteSpace(s)))
                : string.Empty;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                return null;

            string appName = notif.AppInfo?.DisplayInfo?.DisplayName ?? "Windows";
            BitmapImage? icon = null;

            try
            {
                var logoRef = notif.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
                if (logoRef is not null)
                {
                    using var stream = await logoRef.OpenReadAsync();
                    using var netStream = stream.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await netStream.CopyToAsync(ms);
                    ms.Position = 0;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 256;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    icon = bmp;
                }
            }
            catch { }

            return new LiveNotification
            {
                AppName = appName,
                Title = title,
                Body = body,
                AppIcon = icon
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
