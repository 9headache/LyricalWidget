using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace LyricalWidget.Services;

/// <summary>
/// Controls Windows master volume and mute state via WASAPI.
/// </summary>
public sealed class SystemVolumeService : IDisposable
{
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _defaultDevice;
    private AudioEndpointVolume? _endpointVolume;
    private AudioEndpointVolumeNotificationDelegate? _volumeNotificationDelegate;
    private readonly object _lock = new();
    private bool _isDisposed = false;

    public event Action<float, bool>? VolumeChanged;

    public SystemVolumeService()
    {
        InitializeDevice();
    }

    private void InitializeDevice()
    {
        lock (_lock)
        {
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_defaultDevice != null)
                {
                    _endpointVolume = _defaultDevice.AudioEndpointVolume;
                    if (_endpointVolume != null)
                    {
                        _volumeNotificationDelegate = (data) =>
                        {
                            VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
                        };
                        _endpointVolume.OnVolumeNotification += _volumeNotificationDelegate;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemVolumeService] Audio endpoint init warning: {ex.Message}");
            }
        }
    }

    public float Volume
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    return _endpointVolume?.MasterVolumeLevelScalar ?? 0.5f;
                }
                catch
                {
                    return 0.5f;
                }
            }
        }
        set
        {
            lock (_lock)
            {
                try
                {
                    if (_endpointVolume != null)
                    {
                        float clamped = Math.Clamp(value, 0f, 1f);
                        _endpointVolume.MasterVolumeLevelScalar = clamped;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemVolumeService] Set volume warning: {ex.Message}");
                }
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    return _endpointVolume?.Mute ?? false;
                }
                catch
                {
                    return false;
                }
            }
        }
        set
        {
            lock (_lock)
            {
                try
                {
                    if (_endpointVolume != null)
                    {
                        _endpointVolume.Mute = value;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemVolumeService] Set mute warning: {ex.Message}");
                }
            }
        }
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    public void StepVolume(float delta)
    {
        lock (_lock)
        {
            try
            {
                if (_endpointVolume != null)
                {
                    float cur = _endpointVolume.MasterVolumeLevelScalar;
                    float next = Math.Clamp(cur + delta, 0f, 1f);
                    _endpointVolume.MasterVolumeLevelScalar = next;
                    if (delta > 0 && _endpointVolume.Mute)
                    {
                        _endpointVolume.Mute = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemVolumeService] Step volume warning: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                if (_endpointVolume != null && _volumeNotificationDelegate != null)
                {
                    _endpointVolume.OnVolumeNotification -= _volumeNotificationDelegate;
                }
            }
            catch { }

            try
            {
                _endpointVolume?.Dispose();
            }
            catch { }

            try
            {
                _defaultDevice?.Dispose();
            }
            catch { }

            try
            {
                _deviceEnumerator?.Dispose();
            }
            catch { }

            _endpointVolume = null;
            _defaultDevice = null;
            _deviceEnumerator = null;
        }
    }
}
