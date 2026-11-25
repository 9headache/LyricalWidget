using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media.Control;
using LyricalWidget.Common;

namespace LyricalWidget.Media;

// Session discovery, scoring and recovery
public partial class SmtcMediaService
{
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
                    bool hasSession;
                    string currentAppId;
                    lock (_lock)
                    {
                        hasSession = _currentSession != null;
                        currentAppId = _currentSession?.SourceAppUserModelId ?? string.Empty;
                    }

                    if (!hasSession && sessions.Count > 0)
                    {
                        EvaluateAndSelectBestSession();
                    }
                    else if (hasSession)
                    {
                        // Windows does not always announce play/pause handovers between apps (e.g. Spotify -> YouTube)
                        var best = SelectBestSession();
                        if ((best?.SourceAppUserModelId ?? string.Empty) != currentAppId)
                        {
                            UpdateCurrentSession(best);
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
            UpdateCurrentSession(SelectBestSession());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error selecting SMTC session: {ex.Message}");
        }
    }

    /// <summary>Re-picks the session after a settings change.</summary>
    public void ReevaluateSessions() => EvaluateAndSelectBestSession();

    private GlobalSystemMediaTransportControlsSession? SelectBestSession()
    {
        var manager = _sessionManager;
        if (manager == null) return null;

        bool ignoreBrowsers = BrowserBehavior == BrowserMediaBehavior.ShowClock;
        bool ignoredAny = false;
        bool ignoredPlaying = false;
        GlobalSystemMediaTransportControlsSession? bestSession = null;
        int bestScore = -100;

        foreach (var session in manager.GetSessions())
        {
            if (ignoreBrowsers && IsBrowserApp(session.SourceAppUserModelId ?? string.Empty))
            {
                ignoredAny = true;
                ignoredPlaying |= IsSessionPlaying(session);
                continue;
            }

            int score = ScoreSession(session);
            if (score > bestScore)
            {
                bestScore = score;
                bestSession = session;
            }
        }

        if (bestSession == null)
        {
            bestSession = manager.GetCurrentSession();
            if (ignoreBrowsers && bestSession != null && IsBrowserApp(bestSession.SourceAppUserModelId ?? string.Empty))
            {
                bestSession = null;
            }
        }

        bool suppressed = bestSession == null ? ignoredAny : ignoredPlaying && !IsSessionPlaying(bestSession);
        if (suppressed != IsBrowserSuppressed)
        {
            IsBrowserSuppressed = suppressed;
            BrowserSuppressionChanged?.Invoke(this, EventArgs.Empty);
        }

        return bestSession;
    }

    private static bool IsSessionPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        return session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    }

    private static int ScoreSession(GlobalSystemMediaTransportControlsSession session)
    {
        string appId = session.SourceAppUserModelId ?? string.Empty;
        var playback = session.GetPlaybackInfo();
        bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        int score = 0;

        if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
            if (isPlaying) score += 100;
            return score;
        }

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

        if (appId.Contains("Twitch", StringComparison.OrdinalIgnoreCase))
        {
            return -50;
        }

        if (IsBrowserApp(appId))
        {
            // A playing browser beats any paused music app (150 max) but never one that is playing (160+)
            score += isPlaying ? 155 : 5;
            return score;
        }

        if (isPlaying) score += 30;
        return score;
    }

    private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? newSession)
    {
        lock (_lock)
        {
            if (_currentSession == newSession && (newSession != null || _nullSessionReported))
            {
                return;
            }
            _nullSessionReported = newSession == null;

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
}
