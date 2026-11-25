using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using LyricalWidget.Common;
using LyricalWidget.Services;

namespace LyricalWidget.Media;

public partial class SmtcMediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private readonly object _lock = new();

    private TimeSpan _basePosition = TimeSpan.Zero;
    private long _baseTimestampStopwatch = 0;
    private double _playbackRate = 1.0;
    private bool _isPlaying = false;
    private TimeSpan _trackDuration = TimeSpan.Zero;
    private TimeSpan _lastReportedRawPosition = TimeSpan.Zero;
    private DateTimeOffset _lastReportedSmtcTimestamp = DateTimeOffset.MinValue;

    private Timer? _pollingTimer;
    private Timer? _watchdogTimer;
    private int _isPolling = 0;

    // Browser sessions report their duration late (ads, page loads): track it until it settles
    private TimeSpan _pendingBrowserDuration = TimeSpan.Zero;
    private int _pendingBrowserDurationTicks = 0;
    private int _browserDurationRefreshes = 0;
    private bool _nullSessionReported = false;

    public event EventHandler<TrackInfo>? TrackChanged;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? TimelineResynced;
    public event EventHandler? BrowserSuppressionChanged;

    public BrowserMediaBehavior BrowserBehavior { get; set; } = BrowserMediaBehavior.YouTubeLyricsWithClockFallback;

    /// <summary>True while a browser session is being ignored and nothing else is playing.</summary>
    public bool IsBrowserSuppressed { get; private set; }

    public TrackInfo CurrentTrack { get; private set; } = new();

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _isPlaying;
            }
        }
    }

    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                if (!_isPlaying || _playbackRate <= 0.0)
                {
                    return _lastReportedRawPosition > TimeSpan.Zero ? _lastReportedRawPosition : _basePosition;
                }

                bool isLastUpdatedValid = _lastReportedSmtcTimestamp > DateTimeOffset.MinValue &&
                                          _lastReportedSmtcTimestamp <= DateTimeOffset.UtcNow.AddSeconds(1) &&
                                          (DateTimeOffset.UtcNow - _lastReportedSmtcTimestamp).TotalSeconds <= 30.0 &&
                                          (DateTimeOffset.UtcNow - _lastReportedSmtcTimestamp) >= TimeSpan.Zero;

                TimeSpan current;
                // Browsers only post a timeline on play/pause/seek and may play at other speeds, so run them off the local clock
                if (isLastUpdatedValid && !CurrentTrack.IsBrowserSource)
                {
                    current = _lastReportedRawPosition + (DateTimeOffset.UtcNow - _lastReportedSmtcTimestamp);
                }
                else
                {
                    // LastUpdatedTime is invalid or frozen: run on the local clock
                    long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - _baseTimestampStopwatch;
                    double elapsedSeconds = (double)elapsedTicks / System.Diagnostics.Stopwatch.Frequency;
                    current = _basePosition + TimeSpan.FromSeconds(elapsedSeconds * _playbackRate);
                }

                if (_trackDuration > TimeSpan.Zero && current > _trackDuration)
                {
                    return _trackDuration;
                }

                return current < TimeSpan.Zero ? TimeSpan.Zero : current;
            }
        }
    }

    public static bool IsBrowserApp(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return false;
        return appId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("edge", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("brave", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("opera", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("vivaldi", StringComparison.OrdinalIgnoreCase) ||
               // Installed Firefox builds register under a hash of their install path
               appId.Contains("308046B0AF4A39CB", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("E7CF176E110C211B", StringComparison.OrdinalIgnoreCase);
    }

    public static (string Title, string Artist) CleanWebMetadata(string rawTitle, string rawArtist, string appId)
    {
        string title = rawTitle?.Trim() ?? string.Empty;
        string artist = rawArtist?.Trim() ?? string.Empty;

        title = Regex.Replace(title, @"\s*[-•|]\s*Spotify(\s+Free)?\s*$", "", RegexOptions.IgnoreCase).Trim();
        artist = Regex.Replace(artist, @"\s*[-•|]\s*Spotify(\s+Free)?\s*$", "", RegexOptions.IgnoreCase).Trim();

        if (IsBrowserApp(appId))
        {
            var guesses = MetadataCleaner.ParseBrowserMedia(title, artist);
            return guesses.Count > 0 ? (guesses[0].Title, guesses[0].Artist) : (title, artist);
        }

        // If artist is empty or generic while title contains delimiters (" - " or " • ")
        bool isBrowser = IsBrowserApp(appId);
        bool artistIsGeneric = string.IsNullOrWhiteSpace(artist) ||
                               artist.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
                               (isBrowser && (artist.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                                             artist.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                                             artist.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ||
                                             artist.Contains("Brave", StringComparison.OrdinalIgnoreCase) ||
                                             artist.Contains("Opera", StringComparison.OrdinalIgnoreCase)));

        if (artistIsGeneric && !string.IsNullOrWhiteSpace(title))
        {
            string[] delimiters = new[] { " • ", " - ", " – ", " — " };
            foreach (var delimiter in delimiters)
            {
                int idx = title.IndexOf(delimiter, StringComparison.Ordinal);
                if (idx > 0)
                {
                    string part1 = title.Substring(0, idx).Trim();
                    string part2 = title.Substring(idx + delimiter.Length).Trim();
                    part2 = Regex.Replace(part2, @"\s*[-•|]\s*Spotify(\s+Free)?\s*$", "", RegexOptions.IgnoreCase).Trim();

                    if (!string.IsNullOrEmpty(part1) && !string.IsNullOrEmpty(part2))
                    {
                        title = part1;
                        artist = part2;
                        break;
                    }
                }
            }
        }

        return (title, artist);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await ReacquireSessionManagerAsync();

            // Polled as well: change events can arrive late or not at all
            _pollingTimer = new Timer(OnPollingTick, null, 250, 250);

            // Re-binds if the session manager dies or the PC wakes
            _watchdogTimer = new Timer(OnWatchdogTick, null, 3000, 3000);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize SMTC Session Manager: {ex.Message}");
        }
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        try
        {
            await RefreshTrackInfoAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnMediaPropertiesChanged: {ex.Message}");
        }
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        try
        {
            RefreshPlaybackInfo();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnPlaybackInfoChanged: {ex.Message}");
        }
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        try
        {
            RefreshTimeline();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnTimelinePropertiesChanged: {ex.Message}");
        }
    }

    private async Task RefreshAllMediaDataAsync()
    {
        try
        {
            await RefreshTrackInfoAsync();
            RefreshPlaybackInfo();
            RefreshTimeline();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in RefreshAllMediaDataAsync: {ex.Message}");
        }
    }

    private async Task RefreshTrackInfoAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            session = _currentSession;
        }

        if (session == null)
        {
            lock (_lock) { _isPlaying = false; }
            CurrentTrack = new TrackInfo();
            TrackChanged?.Invoke(this, CurrentTrack);
            PlaybackStateChanged?.Invoke(this, false);
            return;
        }

        try
        {
            var mediaProps = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();

            string rawTitle = mediaProps?.Title ?? string.Empty;
            string rawArtist = mediaProps?.Artist ?? string.Empty;
            string appId = session.SourceAppUserModelId ?? string.Empty;

            var (cleanedTitle, cleanedArtist) = CleanWebMetadata(rawTitle, rawArtist, appId);

            var newTrack = new TrackInfo
            {
                Title = cleanedTitle,
                Artist = cleanedArtist,
                Album = mediaProps?.AlbumTitle ?? string.Empty,
                SourceAppId = appId,
                Duration = timeline != null && timeline.EndTime > TimeSpan.Zero ? timeline.EndTime : TimeSpan.Zero,
                IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            };

            if (newTrack.IsBrowserSource)
            {
                newTrack.LookupCandidates = MetadataCleaner.ParseBrowserMedia(rawTitle, rawArtist);
                if (newTrack.Title != CurrentTrack.Title || newTrack.Artist != CurrentTrack.Artist)
                {
                    _browserDurationRefreshes = 0;
                }
            }

            BitmapImage? thumbnail = null;
            if (mediaProps?.Thumbnail != null)
            {
                try
                {
                    using IRandomAccessStreamWithContentType stream = await mediaProps.Thumbnail.OpenReadAsync();
                    if (stream != null && stream.Size > 0)
                    {
                        using Stream netStream = stream.AsStreamForRead();
                        using MemoryStream memoryStream = new();
                        await netStream.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = memoryStream;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        thumbnail = bitmap;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load thumbnail: {ex.Message}");
                }
            }

            // Thumbnails can arrive late: keep the current cover until then
            if (thumbnail == null && CurrentTrack.Thumbnail != null &&
                string.Equals(cleanedTitle, CurrentTrack.Title, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cleanedArtist, CurrentTrack.Artist, StringComparison.OrdinalIgnoreCase))
            {
                thumbnail = CurrentTrack.Thumbnail;
            }

            newTrack.Thumbnail = thumbnail;

            CurrentTrack = newTrack;
            lock (_lock)
            {
                _trackDuration = newTrack.Duration;
                _basePosition = TimeSpan.Zero;
                _baseTimestampStopwatch = System.Diagnostics.Stopwatch.GetTimestamp();
                _lastReportedRawPosition = TimeSpan.Zero;
                if (newTrack.IsBrowserSource)
                {
                    _lastReportedSmtcTimestamp = DateTimeOffset.MinValue;
                }
            }

            TrackChanged?.Invoke(this, newTrack);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read media properties: {ex.Message}");
        }
    }

    private void RefreshPlaybackInfo()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            session = _currentSession;
        }

        if (session == null) return;

        try
        {
            var playback = session.GetPlaybackInfo();
            if (playback != null)
            {
                bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                double rate = playback.PlaybackRate ?? 1.0;

                lock (_lock)
                {
                    _isPlaying = playing;
                    _playbackRate = rate;
                    _baseTimestampStopwatch = System.Diagnostics.Stopwatch.GetTimestamp();
                }

                RefreshTimeline();
                PlaybackStateChanged?.Invoke(this, playing);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to refresh playback info: {ex.Message}");
        }
    }

    private void RefreshTimeline()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            session = _currentSession;
        }

        if (session == null) return;

        try
        {
            var timeline = session.GetTimelineProperties();
            if (timeline != null)
            {
                TimeSpan rawPos = timeline.Position;
                DateTimeOffset lastUpdated = timeline.LastUpdatedTime;

                // Ignore phantom Position = 0 resets emitted when switching browser tabs
                if (_isPlaying && rawPos == TimeSpan.Zero && CurrentPosition > TimeSpan.FromSeconds(2.0))
                {
                    return;
                }

                TimeSpan anchorPos = rawPos;

                bool isLastUpdatedValid = lastUpdated > DateTimeOffset.MinValue &&
                                          lastUpdated <= DateTimeOffset.UtcNow.AddSeconds(1) &&
                                          (DateTimeOffset.UtcNow - lastUpdated).TotalSeconds <= 30.0 &&
                                          (DateTimeOffset.UtcNow - lastUpdated) >= TimeSpan.Zero;

                if (_isPlaying && CurrentTrack.IsBrowserSource)
                {
                    // Extrapolate from the last posted position however old it is, as long as it lands inside the video
                    var age = DateTimeOffset.UtcNow - lastUpdated;
                    if (age > TimeSpan.Zero && age < TimeSpan.FromHours(12))
                    {
                        var extrapolated = rawPos + (age * _playbackRate);
                        if (timeline.EndTime <= TimeSpan.Zero || extrapolated <= timeline.EndTime)
                        {
                            anchorPos = extrapolated;
                        }
                    }
                }
                else if (_isPlaying && isLastUpdatedValid)
                {
                    anchorPos += (DateTimeOffset.UtcNow - lastUpdated);
                }

                lock (_lock)
                {
                    _lastReportedRawPosition = rawPos;
                    _lastReportedSmtcTimestamp = lastUpdated;
                    _basePosition = _isPlaying ? anchorPos : rawPos;
                    _baseTimestampStopwatch = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (timeline.EndTime > TimeSpan.Zero)
                    {
                        _trackDuration = timeline.EndTime;
                    }
                }

                TimelineResynced?.Invoke(this, _basePosition);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to refresh timeline: {ex.Message}");
        }
    }

    /// <summary>
    /// Reloads a browser track once its reported length has changed and held still for a second.
    /// Capped per title so a live stream's growing length cannot cause endless reloads.
    /// </summary>
    private async Task<bool> RefreshIfBrowserDurationSettledAsync(TimeSpan endTime)
    {
        var track = CurrentTrack;
        if (!track.IsBrowserSource || endTime <= TimeSpan.Zero ||
            _browserDurationRefreshes >= 3 || (endTime - track.Duration).Duration() <= TimeSpan.FromSeconds(2))
        {
            _pendingBrowserDurationTicks = 0;
            return false;
        }

        if (endTime != _pendingBrowserDuration)
        {
            _pendingBrowserDuration = endTime;
            _pendingBrowserDurationTicks = 0;
            return false;
        }

        if (++_pendingBrowserDurationTicks < 4) return false;

        _pendingBrowserDurationTicks = 0;
        _browserDurationRefreshes++;
        await RefreshAllMediaDataAsync();
        return true;
    }

    private async void OnPollingTick(object? state)
    {
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0) return;
        try
        {
            GlobalSystemMediaTransportControlsSession? session;
            lock (_lock)
            {
                session = _currentSession;
            }

            if (session == null)
            {
                EvaluateAndSelectBestSession();
                return;
            }

            var mediaProps = await session.TryGetMediaPropertiesAsync();
            if (mediaProps != null)
            {
                var (cleanedTitle, cleanedArtist) = CleanWebMetadata(mediaProps.Title, mediaProps.Artist, session.SourceAppUserModelId ?? string.Empty);
                if (!string.Equals(cleanedTitle, CurrentTrack.Title, StringComparison.Ordinal) ||
                    !string.Equals(cleanedArtist, CurrentTrack.Artist, StringComparison.Ordinal))
                {
                    await RefreshTrackInfoAsync();
                    return;
                }
            }

            var playback = session.GetPlaybackInfo();
            if (playback != null)
            {
                bool isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                bool wasPlaying;
                lock (_lock) { wasPlaying = _isPlaying; }
                if (isPlaying != wasPlaying)
                {
                    RefreshPlaybackInfo();
                }
            }

            var timeline = session.GetTimelineProperties();
            if (timeline != null)
            {
                if (await RefreshIfBrowserDurationSettledAsync(timeline.EndTime)) return;

                TimeSpan rawPos = timeline.Position;
                DateTimeOffset lastUpdated = timeline.LastUpdatedTime;

                if (!(_isPlaying && rawPos == TimeSpan.Zero && CurrentPosition > TimeSpan.FromSeconds(2.0)))
                {
                    if (rawPos != _lastReportedRawPosition || lastUpdated != _lastReportedSmtcTimestamp || !_isPlaying)
                    {
                        RefreshTimeline();
                    }
                }
            }
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMTC] Polling tick COM warning: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMTC] Polling tick warning: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    public void Dispose()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;

        _watchdogTimer?.Dispose();
        _watchdogTimer = null;

        lock (_lock)
        {
            if (_currentSession != null)
            {
                try
                {
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SMTC] Event unsubscription warning: {ex.Message}");
                }
                _currentSession = null;
            }

            if (_sessionManager != null)
            {
                try
                {
                    _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                    _sessionManager.SessionsChanged -= OnSessionsChanged;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SMTC] SessionManager unsubscription warning: {ex.Message}");
                }
                _sessionManager = null;
            }
        }
    }
}
