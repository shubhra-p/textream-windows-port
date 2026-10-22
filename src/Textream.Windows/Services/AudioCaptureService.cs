using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Textream.Windows.Common;
using Textream.Windows.Interop;

namespace Textream.Windows.Services;

public interface IAudioCaptureService : IDisposable
{
    bool IsCapturing { get; }
    bool IsSpeaking { get; }
    IReadOnlyList<double> AudioLevels { get; }
    event Action<double, IReadOnlyList<double>, bool>? AudioLevelUpdated;
    void Start();
    void Stop();
}

public class AudioCaptureService : IAudioCaptureService
{
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int BufferCount = 4;
    private const int BufferDurationMs = 50; // 20 updates per second
    private const int BufferSize = SampleRate * (BitsPerSample / 8) * BufferDurationMs / 1000; // 1600 bytes = 800 samples

    private readonly VoiceActivityDetector _vad = new();
    private readonly List<double> _audioLevels = new(30);
    private readonly object _lock = new();

    private IntPtr _hWaveIn = IntPtr.Zero;
    private AutoResetEvent? _bufferReadyEvent;
    private Thread? _captureThread;
    private readonly IntPtr[] _pHeaders = new IntPtr[BufferCount];
    private readonly IntPtr[] _pBuffers = new IntPtr[BufferCount];

    private volatile bool _isCapturing;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public bool IsCapturing => _isCapturing;
    public bool IsSpeaking => _vad.IsActive(_stopwatch.Elapsed.TotalSeconds);
    public IReadOnlyList<double> AudioLevels
    {
        get
        {
            lock (_lock)
            {
                return _audioLevels.ToArray();
            }
        }
    }

    public event Action<double, IReadOnlyList<double>, bool>? AudioLevelUpdated;

    public AudioCaptureService()
    {
        for (int i = 0; i < 30; i++)
        {
            _audioLevels.Add(0.0);
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isCapturing) return;

            try
            {
                uint devCount = NativeMethods.waveInGetNumDevs();
                if (devCount == 0)
                {
                    AppLogger.Warn("No waveIn audio capture devices found.");
                    return;
                }

                var format = new NativeMethods.WAVEFORMATEX
                {
                    wFormatTag = NativeMethods.WAVE_FORMAT_PCM,
                    nChannels = Channels,
                    nSamplesPerSec = SampleRate,
                    wBitsPerSample = BitsPerSample,
                    nBlockAlign = (ushort)(Channels * (BitsPerSample / 8)),
                    nAvgBytesPerSec = (uint)(SampleRate * Channels * (BitsPerSample / 8)),
                    cbSize = 0
                };

                _bufferReadyEvent = new AutoResetEvent(false);
                IntPtr eventHandle = _bufferReadyEvent.SafeWaitHandle.DangerousGetHandle();

                int res = NativeMethods.waveInOpen(
                    out _hWaveIn,
                    NativeMethods.WAVE_MAPPER,
                    ref format,
                    eventHandle,
                    IntPtr.Zero,
                    NativeMethods.CALLBACK_EVENT);

                if (res != 0 || _hWaveIn == IntPtr.Zero)
                {
                    AppLogger.Warn($"waveInOpen failed with error code {res}");
                    _bufferReadyEvent.Dispose();
                    _bufferReadyEvent = null;
                    return;
                }

                uint hdrSize = (uint)Marshal.SizeOf<NativeMethods.WAVEHDR>();

                for (int i = 0; i < BufferCount; i++)
                {
                    _pBuffers[i] = Marshal.AllocHGlobal(BufferSize);
                    _pHeaders[i] = Marshal.AllocHGlobal((int)hdrSize);

                    var hdr = new NativeMethods.WAVEHDR
                    {
                        lpData = _pBuffers[i],
                        dwBufferLength = (uint)BufferSize,
                        dwBytesRecorded = 0,
                        dwUser = new IntPtr(i),
                        dwFlags = 0,
                        dwLoops = 0
                    };

                    Marshal.StructureToPtr(hdr, _pHeaders[i], false);

                    NativeMethods.waveInPrepareHeader(_hWaveIn, _pHeaders[i], hdrSize);
                    NativeMethods.waveInAddBuffer(_hWaveIn, _pHeaders[i], hdrSize);
                }

                _isCapturing = true;
                _captureThread = new Thread(CaptureLoop)
                {
                    Name = "AudioCaptureWorker",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _captureThread.Start();

                NativeMethods.waveInStart(_hWaveIn);
                AppLogger.Info("WaveIn audio capture started successfully with CALLBACK_EVENT (16kHz 16-bit Mono).");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to start audio capture", ex);
                CleanUpWaveIn();
            }
        }
    }

    private void CaptureLoop()
    {
        uint hdrSize = (uint)Marshal.SizeOf<NativeMethods.WAVEHDR>();

        while (_isCapturing)
        {
            if (_bufferReadyEvent == null || !_bufferReadyEvent.WaitOne(100))
            {
                continue;
            }

            if (!_isCapturing || _hWaveIn == IntPtr.Zero) break;

            for (int i = 0; i < BufferCount; i++)
            {
                if (_pHeaders[i] == IntPtr.Zero) continue;

                var header = Marshal.PtrToStructure<NativeMethods.WAVEHDR>(_pHeaders[i]);
                if ((header.dwFlags & NativeMethods.WHDR_DONE) != 0)
                {
                    int bytesRecorded = (int)header.dwBytesRecorded;
                    double level = 0;

                    if (bytesRecorded > 0 && header.lpData != IntPtr.Zero)
                    {
                        int sampleCount = bytesRecorded / 2;
                        double sum = 0;

                        for (int s = 0; s < sampleCount; s++)
                        {
                            short sample = Marshal.ReadInt16(header.lpData, s * 2);
                            double norm = sample / 32768.0;
                            sum += norm * norm;
                        }

                        double rms = Math.Sqrt(sum / Math.Max(1, sampleCount));
                        level = Math.Min(rms * 5.0, 1.0);
                    }

                    double uptime = _stopwatch.Elapsed.TotalSeconds;
                    _vad.Process(level, uptime);
                    bool isSpeaking = _vad.IsActive(uptime);

                    double[] snapshot;
                    lock (_lock)
                    {
                        _audioLevels.Add(level);
                        if (_audioLevels.Count > 30)
                        {
                            _audioLevels.RemoveAt(0);
                        }
                        snapshot = _audioLevels.ToArray();
                    }

                    try
                    {
                        AudioLevelUpdated?.Invoke(level, snapshot, isSpeaking);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"AudioLevelUpdated listener exception: {ex.Message}");
                    }

                    // Reset WHDR_DONE and re-queue buffer from user space thread
                    if (_isCapturing && _hWaveIn != IntPtr.Zero)
                    {
                        header.dwFlags &= ~NativeMethods.WHDR_DONE;
                        header.dwBytesRecorded = 0;
                        Marshal.StructureToPtr(header, _pHeaders[i], false);
                        NativeMethods.waveInAddBuffer(_hWaveIn, _pHeaders[i], hdrSize);
                    }
                }
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isCapturing) return;
            _isCapturing = false;
            CleanUpWaveIn();
            _vad.Reset();
            AppLogger.Info("WaveIn audio capture stopped.");
        }
    }

    private void CleanUpWaveIn()
    {
        _isCapturing = false;
        _bufferReadyEvent?.Set();

        if (_captureThread != null && _captureThread.IsAlive)
        {
            _captureThread.Join(300);
            _captureThread = null;
        }

        if (_hWaveIn != IntPtr.Zero)
        {
            NativeMethods.waveInStop(_hWaveIn);
            NativeMethods.waveInReset(_hWaveIn);

            uint hdrSize = (uint)Marshal.SizeOf<NativeMethods.WAVEHDR>();

            for (int i = 0; i < BufferCount; i++)
            {
                if (_pHeaders[i] != IntPtr.Zero)
                {
                    NativeMethods.waveInUnprepareHeader(_hWaveIn, _pHeaders[i], hdrSize);
                    Marshal.FreeHGlobal(_pHeaders[i]);
                    _pHeaders[i] = IntPtr.Zero;
                }

                if (_pBuffers[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_pBuffers[i]);
                    _pBuffers[i] = IntPtr.Zero;
                }
            }

            NativeMethods.waveInClose(_hWaveIn);
            _hWaveIn = IntPtr.Zero;
        }

        _bufferReadyEvent?.Dispose();
        _bufferReadyEvent = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
