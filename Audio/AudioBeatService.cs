using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LyricalWidget.Audio;

/// <summary>
/// WASAPI loopback audio sampler that computes low-frequency bass energy.
/// </summary>
public sealed class AudioBeatService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly object _stateLock = new();
    private bool _isRunning = false;
    private bool _isDisposed = false;

    private float _filteredSample = 0f;
    private float _lowPassAlpha = 0.018f;
    private double _bassSumSquares = 0.0;
    private int _sampleCount = 0;
    private int _samplesPerWindow = 1470; // ~30Hz window at 44.1kHz / 48kHz

    private double _currentBassEnergy = 0.0;
    private double _currentBeatImpulse = 0.0;
    private double _baselineBass = 0.0;

    public double BassEnergy
    {
        get
        {
            lock (_stateLock)
            {
                return _currentBassEnergy;
            }
        }
    }

    public double BeatImpulse
    {
        get
        {
            lock (_stateLock)
            {
                return _currentBeatImpulse;
            }
        }
    }

    public event Action<double>? BassEnergyChanged;
    public event Action<double, double>? BeatUpdated; // (bassEnergy, beatImpulse)

    public void UpdateState(bool isPlaying, bool isGamingMode, bool isAudioReactiveEnabled)
    {
        bool shouldRun = isPlaying && !isGamingMode && isAudioReactiveEnabled;
        if (shouldRun)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isRunning || _isDisposed) return;

            try
            {
                _capture = new WasapiLoopbackCapture();
                var waveFormat = _capture.WaveFormat;

                int sampleRate = waveFormat.SampleRate > 0 ? waveFormat.SampleRate : 48000;
                double rc = 1.0 / (2.0 * Math.PI * 140.0); // 140Hz low-pass cutoff for bass
                double dt = 1.0 / sampleRate;
                _lowPassAlpha = (float)(dt / (rc + dt));
                _samplesPerWindow = Math.Max(500, sampleRate / 30); // ~30Hz update

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += (s, a) =>
                {
                    lock (_stateLock)
                    {
                        _isRunning = false;
                    }
                };

                _capture.StartRecording();
                _isRunning = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioBeatService] Loopback start skipped: {ex.Message}");
                CleanupCapture();
            }
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_isRunning && _capture == null) return;
            CleanupCapture();
            _currentBassEnergy = 0.0;
            _currentBeatImpulse = 0.0;
            _baselineBass = 0.0;
        }
        BassEnergyChanged?.Invoke(0.0);
        BeatUpdated?.Invoke(0.0, 0.0);
    }

    private void CleanupCapture()
    {
        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioBeatService] Cleanup exception: {ex.Message}");
        }
        finally
        {
            _capture = null;
            _isRunning = false;
            _filteredSample = 0f;
            _bassSumSquares = 0.0;
            _sampleCount = 0;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _capture == null) return;

        var format = _capture.WaveFormat;
        int bytesPerSample = format.BitsPerSample / 8;
        int channels = format.Channels;
        if (bytesPerSample < 2 || channels < 1) return;

        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        int stride = bytesPerSample * channels;
        int totalFrames = e.BytesRecorded / stride;

        float alpha = _lowPassAlpha;
        float filtered = _filteredSample;
        double sum = _bassSumSquares;
        int count = _sampleCount;
        int window = _samplesPerWindow;

        byte[] buffer = e.Buffer;

        for (int i = 0; i < totalFrames; i++)
        {
            int offset = i * stride;
            float monoSample = 0f;

            if (isFloat && bytesPerSample == 4)
            {
                // Read left channel 32-bit float
                monoSample = BitConverter.ToSingle(buffer, offset);
            }
            else if (bytesPerSample == 2)
            {
                // Read 16-bit PCM
                short s = (short)(buffer[offset] | (buffer[offset + 1] << 8));
                monoSample = s / 32768f;
            }

            // Low-pass filter for bass frequencies
            filtered += alpha * (monoSample - filtered);
            sum += filtered * filtered;
            count++;

            if (count >= window)
            {
                double rms = Math.Sqrt(sum / count);
                // Scale RMS to 0.0 - 1.0 (ambient music bass RMS is typically 0.02 - 0.20)
                double rawTarget = Math.Clamp(rms * 7.5, 0.0, 1.0);

                double currentBass;
                double currentImpulse;
                lock (_stateLock)
                {
                    // Smoothed bass energy (fast attack, natural decay)
                    double prev = _currentBassEnergy;
                    if (rawTarget > prev)
                    {
                        currentBass = prev + (rawTarget - prev) * 0.45; // Rapid attack
                    }
                    else
                    {
                        currentBass = prev + (rawTarget - prev) * 0.12; // Gentle, natural decay
                    }
                    _currentBassEnergy = currentBass;

                    // Rolling baseline bass energy (adapts over ~600-800ms)
                    if (_baselineBass <= 0.0)
                    {
                        _baselineBass = rawTarget;
                    }
                    else
                    {
                        _baselineBass = (_baselineBass * 0.88) + (rawTarget * 0.12);
                    }

                    // 3. Transient kick / beat detection: sudden spike above rolling baseline
                    double diff = rawTarget - (_baselineBass * 1.25);
                    double instantHit = 0.0;
                    if (diff > 0.05)
                    {
                        instantHit = Math.Clamp(diff * 3.5, 0.0, 1.0);
                    }

                    // Instant attack, smooth exponential decay (~160ms decay at ~30Hz update)
                    if (instantHit > _currentBeatImpulse)
                    {
                        currentImpulse = instantHit;
                    }
                    else
                    {
                        currentImpulse = Math.Max(0.0, _currentBeatImpulse * 0.78);
                    }
                    _currentBeatImpulse = currentImpulse;
                }

                BassEnergyChanged?.Invoke(currentBass);
                BeatUpdated?.Invoke(currentBass, currentImpulse);

                sum = 0.0;
                count = 0;
            }
        }

        _filteredSample = filtered;
        _bassSumSquares = sum;
        _sampleCount = count;
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            CleanupCapture();
        }
    }
}
