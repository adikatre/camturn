using System.Runtime.InteropServices;
using CamTurn.Core.Video;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using WinRT;

namespace CamTurn.Platform.Windows;

/// <summary>A physical camera as found by enumeration (CamTurn's own virtual camera is excluded).</summary>
public sealed record CameraInfo(string GroupId, string SourceId, string Name, string Panel);

/// <summary>
/// Physical camera capture via WinRT MediaCapture + MediaFrameReader. The reader is created with NV12 as the output
/// subtype, so Windows decodes MJPG/YUY2 for us. Opening is asynchronous; <see cref="Start"/> returns immediately and
/// the outcome arrives through <see cref="StateChanged"/>.
/// </summary>
public sealed class MediaCaptureCamera : ICameraSource, IDisposable
{
    private readonly CameraInfo _info;
    private readonly SemaphoreSlim _ops = new(1, 1);
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private long _frames;
    private int _generation;

    private MediaCaptureCamera(CameraInfo info, IReadOnlyList<VideoFormat> formats)
    {
        _info = info;
        Descriptor = new CameraDescriptor(info.GroupId, info.Name, info.Panel, formats);
    }

    public CameraDescriptor Descriptor { get; }

    public CameraState State { get; private set; } = CameraState.Closed;

    /// <summary>Human-readable reason for the last failure, for the status panel.</summary>
    public string? LastError { get; private set; }

    public event EventHandler<VideoFrame>? FrameArrived;

    public event EventHandler<CameraState>? StateChanged;

    /// <summary>Colour video cameras, front panel first. Excludes any camera named like CamTurn's virtual camera.</summary>
    public static async Task<IReadOnlyList<CameraInfo>> FindAllAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var list = new List<CameraInfo>();
        foreach (var g in groups)
        {
            if (g.DisplayName.Contains("CamTurn", StringComparison.OrdinalIgnoreCase))
            {
                continue; // never capture our own output
            }

            var source = g.SourceInfos.FirstOrDefault(s => s.SourceKind == MediaFrameSourceKind.Color && s.MediaStreamType == MediaStreamType.VideoRecord)
                ?? g.SourceInfos.FirstOrDefault(s => s.SourceKind == MediaFrameSourceKind.Color && s.MediaStreamType == MediaStreamType.VideoPreview);
            if (source is null)
            {
                continue;
            }

            var panel = source.DeviceInformation?.EnclosureLocation?.Panel.ToString() ?? "Unknown";
            list.Add(new CameraInfo(g.Id, source.Id, g.DisplayName, panel));
        }

        return list.OrderBy(c => c.Panel == "Front" ? 0 : 1).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Wraps a found camera; with <paramref name="probeFormats"/> it is opened briefly (shared) to read its formats.</summary>
    public static async Task<MediaCaptureCamera> CreateAsync(CameraInfo info, bool probeFormats = false)
    {
        IReadOnlyList<VideoFormat> formats = [new(1920, 1080, 30, "NV12"), new(1280, 720, 30, "NV12")];
        if (probeFormats)
        {
            try
            {
                using var capture = new MediaCapture();
                await capture.InitializeAsync(await SettingsAsync(info, MediaCaptureSharingMode.SharedReadOnly));
                if (capture.FrameSources.TryGetValue(info.SourceId, out var src))
                {
                    var found = src.SupportedFormats
                        .Where(f => f.VideoFormat is not null && f.FrameRate.Denominator != 0)
                        .Select(f => new VideoFormat((int)f.VideoFormat.Width, (int)f.VideoFormat.Height, (int)Math.Round((double)f.FrameRate.Numerator / f.FrameRate.Denominator), f.Subtype))
                        .Distinct()
                        .ToList();
                    if (found.Count > 0)
                    {
                        formats = found;
                    }
                }
            }
            catch (Exception e) when (e is COMException or UnauthorizedAccessException)
            {
                // Busy or blocked: keep the defaults; Start reports the real problem.
            }
        }

        return new MediaCaptureCamera(info, formats);
    }

    public bool Start(VideoFormat format)
    {
        var generation = Interlocked.Increment(ref _generation);
        _ = Task.Run(() => StartAsync(format, generation));
        return true;
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _ = Task.Run(async () =>
        {
            await _ops.WaitAsync().ConfigureAwait(false);
            try
            {
                await CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                _ops.Release();
            }

            SetState(CameraState.Closed);
        });
    }

    public void Dispose()
    {
        Stop();
    }

    private static async Task<MediaCaptureInitializationSettings> SettingsAsync(CameraInfo info, MediaCaptureSharingMode mode) => new()
    {
        SourceGroup = await MediaFrameSourceGroup.FromIdAsync(info.GroupId),
        SharingMode = mode,
        MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        StreamingCaptureMode = StreamingCaptureMode.Video,
    };

    private async Task StartAsync(VideoFormat wanted, int generation)
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return; // superseded by Stop or another Start
            }

            await CloseAsync().ConfigureAwait(false);
            var capture = new MediaCapture();
            capture.Failed += (_, e) =>
            {
                LastError = $"Camera failed: {e.Message} (0x{e.Code:X8})";
                SetState(CameraState.Disconnected);
            };

            try
            {
                await capture.InitializeAsync(await SettingsAsync(_info, MediaCaptureSharingMode.ExclusiveControl));
            }
            catch (UnauthorizedAccessException)
            {
                capture.Dispose();
                LastError = "Camera access is blocked. Allow desktop apps to use the camera in Settings > Privacy & security > Camera.";
                SetState(CameraState.Busy);
                return;
            }
            catch (Exception e) when (e is COMException or InvalidOperationException)
            {
                capture.Dispose();
                var missing = (uint)e.HResult is 0xC00D36D5u /* MF_E_NOT_AVAILABLE */ or 0x80070490u /* not found */ or 0xC00DABE0u /* MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED */;
                LastError = missing ? $"{_info.Name} is not connected." : $"{_info.Name} is in use by another app (0x{e.HResult:X8}).";
                SetState(missing ? CameraState.Disconnected : CameraState.Busy);
                return;
            }

            if (!capture.FrameSources.TryGetValue(_info.SourceId, out var source))
            {
                capture.Dispose();
                LastError = $"{_info.Name} has no colour stream.";
                SetState(CameraState.Disconnected);
                return;
            }

            var format = PickFormat(source.SupportedFormats, wanted);
            if (format is not null)
            {
                await source.SetFormatAsync(format);
            }

            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Nv12);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnFrameArrived;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                reader.Dispose();
                capture.Dispose();
                LastError = $"{_info.Name} could not start: {status}.";
                SetState(status == MediaFrameReaderStartStatus.DeviceNotAvailable ? CameraState.Disconnected : CameraState.Busy);
                return;
            }

            _capture = capture;
            _reader = reader;
            LastError = null;
            SetState(CameraState.Streaming);
        }
        finally
        {
            _ops.Release();
        }
    }

    private static MediaFrameFormat? PickFormat(IReadOnlyList<MediaFrameFormat> formats, VideoFormat wanted)
    {
        static int SubtypeRank(string s) => s.ToUpperInvariant() switch
        {
            "NV12" => 0,
            "YUY2" => 1,
            "MJPG" => 2,
            _ => 3,
        };

        return formats
            .Where(f => f.VideoFormat is not null && f.FrameRate.Denominator != 0)
            .OrderBy(f => Math.Abs(((double)f.FrameRate.Numerator / f.FrameRate.Denominator) - wanted.FramesPerSecond) < 1 ? 0 : 1)
            .ThenBy(f => f.VideoFormat.Width >= wanted.Width && f.VideoFormat.Height >= wanted.Height ? 0 : 1)
            .ThenBy(f => Math.Abs(((long)f.VideoFormat.Width * f.VideoFormat.Height) - ((long)wanted.Width * wanted.Height)))
            .ThenBy(f => SubtypeRank(f.Subtype))
            .FirstOrDefault();
    }

    private async Task CloseAsync()
    {
        if (_reader is { } reader)
        {
            reader.FrameArrived -= OnFrameArrived;
            try
            {
                await reader.StopAsync();
            }
            catch (COMException)
            {
            }

            reader.Dispose();
            _reader = null;
        }

        _capture?.Dispose();
        _capture = null;
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var reference = sender.TryAcquireLatestFrame();
        var bitmap = reference?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null || bitmap.BitmapPixelFormat != BitmapPixelFormat.Nv12 || bitmap.PixelWidth % 2 != 0 || bitmap.PixelHeight % 2 != 0)
        {
            bitmap?.Dispose();
            return;
        }

        try
        {
            var frame = CopyNv12(bitmap, Interlocked.Increment(ref _frames), reference!.SystemRelativeTime ?? TimeSpan.Zero);
            FrameArrived?.Invoke(this, frame);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>Repacks the bitmap's (possibly padded) planes into a tightly packed NV12 frame.</summary>
    private static unsafe VideoFrame CopyNv12(SoftwareBitmap bitmap, long number, TimeSpan timestamp)
    {
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        var data = new byte[FrameProtocol.Nv12Size(w, h)];
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var memory = buffer.CreateReference();
        memory.As<IMemoryBufferByteAccess>().GetBuffer(out var ptr, out var capacity);
        var y = buffer.GetPlaneDescription(0);
        var uv = buffer.GetPlaneDescription(1);
        fixed (byte* dst = data)
        {
            for (var row = 0; row < h; row++)
            {
                Buffer.MemoryCopy(ptr + y.StartIndex + (row * y.Stride), dst + (row * w), w, w);
            }

            for (var row = 0; row < h / 2; row++)
            {
                Buffer.MemoryCopy(ptr + uv.StartIndex + (row * uv.Stride), dst + (w * h) + (row * w), w, w);
            }
        }

        _ = capacity;
        return new VideoFrame(w, h, PixelFormat.Nv12, data, w, number, timestamp);
    }

    private void SetState(CameraState state)
    {
        if (state != State)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
