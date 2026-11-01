using System.IO.Pipes;
using CamTurn.Core.Video;

namespace CamTurn.Testing;

/// <summary>
/// Managed equivalent of the C++ media source's PipeClient: connects to the CamTurn pipe, sends Hello, and keeps
/// receiving frames. Used by tests and by <c>camturn --pipe-probe</c> to check the app side without Frame Server.
/// </summary>
public sealed class PipeConsumerClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private Task? _reader;
    private byte[]? _latest;
    private FrameHeader _latestHeader;
    private int _frames;

    public PipeConsumerClient(string pipeName = FrameProtocol.PipeName)
    {
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    public int FramesReceived => Volatile.Read(ref _frames);

    public async Task ConnectAsync(int width, int height, int fps, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await _pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
        var hello = new byte[FrameProtocol.HeaderSize];
        FrameProtocol.Encode(FrameProtocol.Hello(width, height, fps), hello);
        await _pipe.WriteAsync(hello, cts.Token).ConfigureAwait(false);
        await _pipe.FlushAsync(cts.Token).ConfigureAwait(false);
        _reader = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public (FrameHeader Header, byte[] Payload)? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest is null ? null : (_latestHeader, _latest);
            }
        }
    }

    public async Task<(FrameHeader Header, byte[] Payload)> WaitForFrameAsync(Func<FrameHeader, bool>? predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Latest is { } f && (predicate is null || predicate(f.Header)))
            {
                return f;
            }

            if (_reader is { IsCompleted: true })
            {
                break;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        throw new TimeoutException("No matching frame received.");
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        await _pipe.DisposeAsync().ConfigureAwait(false);
        if (_reader is not null)
        {
            try
            {
                await _reader.ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
        }

        _cts.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var header = new byte[FrameProtocol.HeaderSize];
        while (!ct.IsCancellationRequested)
        {
            await _pipe.ReadExactlyAsync(header, ct).ConfigureAwait(false);
            if (FrameProtocol.Decode(header, out var h) != FrameDecodeResult.Ok || h.Type != FrameMessageType.Frame)
            {
                throw new InvalidDataException("Protocol error from server.");
            }

            var payload = new byte[h.PayloadSize];
            await _pipe.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
            lock (_gate)
            {
                _latest = payload;
                _latestHeader = h;
            }

            Interlocked.Increment(ref _frames);
        }
    }
}
