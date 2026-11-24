using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LyricalWidget.Media;

public class TrackInfo
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public BitmapImage? Thumbnail { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public bool IsPlaying { get; set; }
    public string SourceAppId { get; set; } = string.Empty;

    public bool IsValid => !string.IsNullOrWhiteSpace(Title);

    public bool IsDjOrInterstitial
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title)) return true;

            string t = Title.Trim();
            string a = Artist.Trim();
            double durSec = Duration.TotalSeconds;

            // Title exact matches
            if (t.Equals("DJ", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("DJ X", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Artist is "Spotify" or empty while duration is 0 or under 18 seconds
            if ((string.IsNullOrWhiteSpace(a) || a.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) &&
                durSec <= 18.0)
            {
                return true;
            }

            // Known DJ interstitial / commentary patterns
            if (t.StartsWith("DJ ", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("DJ Commentary", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Spotify DJ", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Audio Ad", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Advertisement", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (a.Equals("DJ", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("DJ X", StringComparison.OrdinalIgnoreCase) ||
                a.Contains("Spotify DJ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    public string CacheKey
    {
        get
        {
            int durSec = (int)Duration.TotalSeconds;
            return $"{Artist}_{Title}_{Album}_{durSec}";
        }
    }
}

public class SmtcMediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private readonly object _lock = new();

    // Timestamp interpolation state
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

    public event EventHandler<TrackInfo>? TrackChanged;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? TimelineResynced;

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
                if (isLastUpdatedValid)
                {
                    // Anchor directly to SMTC Position + (UtcNow - LastUpdatedTime)
                    current = _lastReportedRawPosition + (DateTimeOffset.UtcNow - _lastReportedSmtcTimestamp);
                }
                else
                {
                    // Fall back to local Stopwatch when LastUpdatedTime is invalid or frozen
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
               appId.Contains("opera", StringComparison.OrdinalIgnoreCase);
    }

    public static (string Title, string Artist) CleanWebMetadata(string rawTitle, string rawArtist, string appId)
    {
        string title = rawTitle?.Trim() ?? string.Empty;
        string artist = rawArtist?.Trim() ?? string.Empty;

        title = Regex.Replace(title, @"\s*[-•|]\s*Spotify(\s+Free)?\s*$", "", RegexOptions.IgnoreCase).Trim();
        artist = Regex.Replace(artist, @"\s*[-•|]\s*Spotify(\s+Free)?\s*$", "", RegexOptions.IgnoreCase).Trim();

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

    public async Task<bool> TrySkipPreviousAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock) { session = _currentSession; }
        if (session != null)
        {
            try
            {
                return await session.TrySkipPreviousAsync();
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] SkipPrevious COM error: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] SkipPrevious error: {ex.Message}");
            }
        }
        return false;
    }

    public async Task<bool> TryTogglePlayPauseAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock) { session = _currentSession; }
        if (session != null)
        {
            try
            {
                return await session.TryTogglePlayPauseAsync();
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] TogglePlayPause COM error: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] TogglePlayPause error: {ex.Message}");
            }
        }
        return false;
    }

    public async Task<bool> TrySkipNextAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock) { session = _currentSession; }
        if (session != null)
        {
            try
            {
                return await session.TrySkipNextAsync();
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] SkipNext COM error: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] SkipNext error: {ex.Message}");
            }
        }
        return false;
    }

    public async Task<bool> TrySeekAsync(TimeSpan position)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock) { session = _currentSession; }
        if (session != null)
        {
            try
            {
                long ticks = position.Ticks;
                bool success = await session.TryChangePlaybackPositionAsync(ticks);
                if (success)
                {
                    lock (_lock)
                    {
                        _basePosition = position;
                        _baseTimestampStopwatch = System.Diagnostics.Stopwatch.GetTimestamp();
                        _lastReportedRawPosition = position;
                    }
                    TimelineResynced?.Invoke(this, position);
                }
                return success;
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] Seek COM error: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMTC] Seek error: {ex.Message}");
            }
        }
        return false;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await ReacquireSessionManagerAsync();

            // Tight 250ms polling loop so track skips and seeks feel instant
            _pollingTimer = new Timer(OnPollingTick, null, 250, 250);

            // Lightweight 3-second health watchdog: auto re-binds if session/manager becomes invalid or PC wakes
            _watchdogTimer = new Timer(OnWatchdogTick, null, 3000, 3000);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize SMTC Session Manager: {ex.Message}");
        }
    }

    private async void OnWatchdogTick(object? state)
    {
        try
        {
            bool needsReacquire = false;
            lock (_lock)
            {
                if (_sessionManager == null)
                {
                    needsReacquire = true;
                }
            }

            if (!needsReacquire && _sessionManager != null)
            {
                try
                {
                    var sessions = _sessionManager.GetSessions();
                    lock (_lock)
                    {
                        if (_currentSession == null && sessions.Count > 0)
                        {
                            EvaluateAndSelectBestSession();
                        }
                    }
                }
                catch (COMException)
                {
                    needsReacquire = true;
                }
                catch (Exception)
                {
                    needsReacquire = true;
                }
            }

            if (needsReacquire)
            {
                await ReacquireSessionManagerAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Watchdog tick error: {ex.Message}");
        }
    }

    public async Task ReacquireSessionManagerAsync()
    {
        try
        {
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
                        System.Diagnostics.Debug.WriteLine($"[SMTC] SessionManager event unsubscription warning: {ex.Message}");
                    }
                    _sessionManager = null;
                }
            }

            var sm = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (sm != null)
            {
                lock (_lock)
                {
                    _sessionManager = sm;
                    try
                    {
                        _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                        _sessionManager.SessionsChanged += OnSessionsChanged;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SMTC] SessionManager event subscription warning: {ex.Message}");
                    }
                }
                EvaluateAndSelectBestSession();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to reacquire SMTC Session Manager: {ex.Message}");
        }
    }

    public async Task ForceReconnectAsync()
    {
        await ReacquireSessionManagerAsync();
        await RefreshAllMediaDataAsync();
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        try
        {
            EvaluateAndSelectBestSession();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error handling SessionsChanged: {ex.Message}");
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            EvaluateAndSelectBestSession();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error handling CurrentSessionChanged: {ex.Message}");
        }
    }

    private void EvaluateAndSelectBestSession()
    {
        if (_sessionManager == null) return;

        try
        {
            var sessions = _sessionManager.GetSessions();
            GlobalSystemMediaTransportControlsSession? bestSession = null;
            int bestScore = -100;

            foreach (var session in sessions)
            {
                int score = ScoreSession(session);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSession = session;
                }
            }

            // Fallback to default current session if score is poor
            if (bestSession == null)
            {
                bestSession = _sessionManager.GetCurrentSession();
            }

            UpdateCurrentSession(bestSession);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error selecting SMTC session: {ex.Message}");
        }
    }

    private static int ScoreSession(GlobalSystemMediaTransportControlsSession session)
    {
        string appId = session.SourceAppUserModelId ?? string.Empty;
        var playback = session.GetPlaybackInfo();
        bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        int score = 0;

        // Dedicated Spotify desktop app
        if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
            if (isPlaying) score += 100;
            return score;
        }

        // Other dedicated music desktop apps
        if (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) ||
            appId.Contains("TIDAL", StringComparison.OrdinalIgnoreCase) ||
            appId.Contains("MusicBee", StringComparison.OrdinalIgnoreCase) ||
            appId.Contains("AIMP", StringComparison.OrdinalIgnoreCase) ||
            appId.Contains("Foobar", StringComparison.OrdinalIgnoreCase) ||
            appId.Contains("Deezer", StringComparison.OrdinalIgnoreCase) ||
            appId.Contains("AmazonMusic", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
            if (isPlaying) score += 60;
            return score;
        }

        // Video / Twitch streams to ignore or severely deprioritize
        if (appId.Contains("Twitch", StringComparison.OrdinalIgnoreCase))
        {
            return -50;
        }

        // Generic browser sessions (Chrome, Edge, Firefox, Brave, Opera)
        if (IsBrowserApp(appId))
        {
            // Actively playing browser session strongly preferred over paused tabs
            score += isPlaying ? 80 : 5;
            return score;
        }

        if (isPlaying) score += 30;
        return score;
    }

    private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? newSession)
    {
        lock (_lock)
        {
            if (_currentSession == newSession && _currentSession != null)
            {
                return;
            }

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
            }

            _currentSession = newSession;

            if (_currentSession != null)
            {
                try
                {
                    _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to subscribe to session events: {ex.Message}");
                }
            }
        }

        _ = RefreshAllMediaDataAsync();
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
            CurrentTrack = new TrackInfo();
            TrackChanged?.Invoke(this, CurrentTrack);
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

            // Safely handle delayed or null SMTC album thumbnails without throwing or clearing a valid cover prematurely
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
                // If the track changed, reset base position to 0 to prevent stale position from old song
                _basePosition = TimeSpan.Zero;
                _baseTimestampStopwatch = System.Diagnostics.Stopwatch.GetTimestamp();
                _lastReportedRawPosition = TimeSpan.Zero;
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

                // Precision SMTC Clock: Accept valid recent LastUpdatedTime within 30 seconds
                bool isLastUpdatedValid = lastUpdated > DateTimeOffset.MinValue &&
                                          lastUpdated <= DateTimeOffset.UtcNow.AddSeconds(1) &&
                                          (DateTimeOffset.UtcNow - lastUpdated).TotalSeconds <= 30.0 &&
                                          (DateTimeOffset.UtcNow - lastUpdated) >= TimeSpan.Zero;

                if (_isPlaying && isLastUpdatedValid)
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

            // Fast detection of track changes (e.g. Spotify skipped to next song)
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

            // Fast check if playback status changed
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

            // 3. Fast check if timeline drifted or was sought
            var timeline = session.GetTimelineProperties();
            if (timeline != null)
            {
                TimeSpan rawPos = timeline.Position;
                DateTimeOffset lastUpdated = timeline.LastUpdatedTime;

                // Ignore phantom 0 tab switches
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
