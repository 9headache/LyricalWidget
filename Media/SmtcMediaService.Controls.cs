using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace LyricalWidget.Media;

// Transport controls
public partial class SmtcMediaService
{
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
}
