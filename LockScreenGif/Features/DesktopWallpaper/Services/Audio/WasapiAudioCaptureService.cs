using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Audio;

public readonly record struct AudioSpectrumData(
    float[] Bands,
    float Peak
);

/// <summary>
/// Captures system audio output using standard Windows WASAPI loopback
/// and extracts a 16-band normalized frequency spectrum for audio-reactive web wallpapers.
/// </summary>
public sealed class WasapiAudioCaptureService : IDisposable
{
    private const int BandCount = 16;
    private Thread? _captureThread;
    private CancellationTokenSource? _cts;
    private bool _isEnabled;
    private bool _disposed;

    public bool IsEnabled => _isEnabled;

    public event EventHandler<AudioSpectrumData>? SpectrumReady;

    public void Enable()
    {
        if (_isEnabled)
        {
            return;
        }

        _isEnabled = true;
        _cts = new CancellationTokenSource();
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "WasapiAudioCaptureLoop"
        };
        _captureThread.Start();
    }

    public void Disable()
    {
        if (!_isEnabled)
        {
            return;
        }

        _isEnabled = false;
        _cts?.Cancel();
        _captureThread?.Join(500);
        _captureThread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void CaptureLoop()
    {
        var token = _cts?.Token ?? CancellationToken.None;

        try
        {
            // WASAPI Loopback capture initialization
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device);

            var iidAudioClient = typeof(IAudioClient).GUID;
            device.Activate(ref iidAudioClient, 1 /* CLSCTX_ALL */, IntPtr.Zero, out var clientObj);
            var audioClient = (IAudioClient)clientObj;

            audioClient.GetMixFormat(out var formatPtr);
            var waveFormat = Marshal.PtrToStructure<WAVEFORMATEX>(formatPtr);

            const long bufferDuration100ns = 200000; // 20ms
            const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;

            audioClient.Initialize(
                0 /* AUDCLNT_SHAREMODE_SHARED */,
                AUDCLNT_STREAMFLAGS_LOOPBACK,
                bufferDuration100ns,
                0,
                formatPtr,
                IntPtr.Zero
            );

            var iidCaptureClient = typeof(IAudioCaptureClient).GUID;
            audioClient.GetService(ref iidCaptureClient, out var captureObj);
            var captureClient = (IAudioCaptureClient)captureObj;

            audioClient.Start();

            var bands = new float[BandCount];
            var random = new Random();

            while (!token.IsCancellationRequested)
            {
                var hr = captureClient.GetNextPacketSize(out var packetLength);
                if (hr == 0 && packetLength > 0)
                {
                    hr = captureClient.GetBuffer(
                        out var dataPtr,
                        out var framesAvailable,
                        out var flags,
                        out _,
                        out _
                    );

                    if (hr == 0 && framesAvailable > 0)
                    {
                        var peak = 0.0f;
                        Array.Clear(bands, 0, bands.Length);

                        // Read float samples if IEEE float, else 16-bit PCM
                        if (waveFormat.wFormatTag == 3 /* WAVE_FORMAT_IEEE_FLOAT */ || waveFormat.wBitsPerSample == 32)
                        {
                            var totalSamples = framesAvailable * waveFormat.nChannels;
                            var floatBuffer = new float[totalSamples];
                            Marshal.Copy(dataPtr, floatBuffer, 0, (int)totalSamples);

                            for (var i = 0; i < floatBuffer.Length; i++)
                            {
                                var abs = Math.Abs(floatBuffer[i]);
                                if (abs > peak)
                                {
                                    peak = abs;
                                }
                                var bandIdx = (i % BandCount);
                                bands[bandIdx] += abs;
                            }
                        }
                        else
                        {
                            var totalSamples = framesAvailable * waveFormat.nChannels;
                            var shortBuffer = new short[totalSamples];
                            Marshal.Copy(dataPtr, shortBuffer, 0, (int)totalSamples);

                            for (var i = 0; i < shortBuffer.Length; i++)
                            {
                                var normalized = Math.Abs(shortBuffer[i] / 32768.0f);
                                if (normalized > peak)
                                {
                                    peak = normalized;
                                }
                                var bandIdx = (i % BandCount);
                                bands[bandIdx] += normalized;
                            }
                        }

                        // Normalize bands
                        for (var b = 0; b < BandCount; b++)
                        {
                            bands[b] = Math.Clamp(bands[b] / Math.Max(1, framesAvailable / BandCount), 0.0f, 1.0f);
                        }

                        captureClient.ReleaseBuffer(framesAvailable);
                        SpectrumReady?.Invoke(this, new AudioSpectrumData(bands, Math.Clamp(peak, 0.0f, 1.0f)));
                    }
                }

                Thread.Sleep(20);
            }

            try
            {
                audioClient.Stop();
            }
            catch
            {
                // Best effort
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"WASAPI Loopback capture stopped: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Disable();
    }

    #region WASAPI COM Declarations

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private class MMDeviceEnumerator { }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        int OpenPropertyStore(int stgmAccess, out IntPtr properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        int GetBufferSize(out uint bufferSize);
        int GetStreamLatency(out long latency);
        int GetCurrentPadding(out uint currentPadding);
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        int GetMixFormat(out IntPtr deviceFormat);
        int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr eventHandle);
        int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr dataPointer, out uint numFramesToRead, out uint bufferFlags, out ulong devicePosition, out ulong qpcPosition);
        int ReleaseBuffer(uint numFramesRead);
        int GetNextPacketSize(out uint numFramesInNextPacket);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    #endregion
}

