using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CamTurn.Core.Video;

namespace CamTurn.App.Views;

/// <summary>
/// Shows NV12 preview frames in an Image. Conversion runs on the caller's (camera) thread into a reused buffer;
/// only the pixel copy happens on the UI thread, and frames arriving while one is pending are dropped.
/// </summary>
public sealed class PreviewRenderer(Image target, Dispatcher dispatcher, Action firstFrame)
{
    private readonly object _gate = new();
    private byte[] _bgra = [];
    private WriteableBitmap? _bitmap;
    private int _pending;
    private bool _seenFirst;

    public void OnFrame(object? sender, VideoFrame frame)
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return; // UI still busy with the previous frame
        }

        int w = frame.Width, h = frame.Height;
        lock (_gate)
        {
            if (_bgra.Length != w * h * 4)
            {
                _bgra = new byte[w * h * 4];
            }

            FrameOps.Nv12ToBgra(frame, _bgra);
        }

        dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            try
            {
                if (_bitmap is null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                {
                    _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                    target.Source = _bitmap;
                }

                lock (_gate)
                {
                    _bitmap.WritePixels(new Int32Rect(0, 0, w, h), _bgra, w * 4, 0);
                }

                if (!_seenFirst)
                {
                    _seenFirst = true;
                    firstFrame();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _pending, 0);
            }
        });
    }
}
