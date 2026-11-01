using CamTurn.Core.Video;

namespace CamTurn.Core.Transport;

/// <summary>A connected virtual-camera consumer (one media source stream) and the size/rate it asked for.</summary>
public sealed record ConsumerInfo(int Id, int Width, int Height, int Fps);

/// <summary>
/// Server side of the frame protocol (<see cref="FrameProtocol"/>). Each accepted connection must start with a
/// Hello; after that the host pushes the latest frame of the requested size. Slow consumers drop frames rather
/// than queueing them. Transport-agnostic: the platform layer supplies connected streams (an ACL-protected named
/// pipe on Windows; a plain named pipe in tests).
/// </summary>
public sealed class FramePipeHost : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _accept;
    private readonly TimeSpan _helloTimeout;
    private readonly object _gate = new();
    private readonly Dictionary<int, Client> _clients = [];
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private int _nextId;

    public FramePipeHost(Func<CancellationToken, ValueTask<Stream>> accept, TimeSpan? helloTimeout = null)
    {
        _accept = accept;
        _helloTimeout = helloTimeout ?? TimeSpan.FromSeconds(5);
    }

    public event EventHandler<ConsumerInfo>? ConsumerConnected;

    public event EventHandler<ConsumerInfo>? ConsumerDisconnected;

    /// <summary>Raised for connections that failed the handshake (bad magic, wrong version, timeout).</summary>
    public event EventHandler<string>? ProtocolError;

    public IReadOnlyList<ConsumerInfo> Consumers
    {
        get
        {
            lock (_gate)
            {
                return _clients.Values.Select(c => c.Info).ToList();
            }
        }
    }

    public void Start() => _acceptLoop ??= Task.Run(AcceptLoopAsync);

    /// <summary>Queues a packed NV12 frame for every consumer that asked for this size. Copies; never blocks.</summary>
    public int Publish(ReadOnlySpan<byte> nv12, int width, int height, ulong counter, long timestamp100ns, FrameFlags flags, int rotation)
    {
        Client[] targets;
        lock (_gate)
        {
            targets = _clients.Values.Where(c => c.Info.Width == width && c.Info.Height == height).ToArray();
        }

        var header = FrameProtocol.FrameFor(width, height, counter, timestamp100ns, flags, (uint)rotation);
        foreach (var c in targets)
        {
            c.Offer(header, nv12);
        }

        return targets.Length;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        Client[] clients;
        lock (_gate)
        {
            clients = [.. _clients.Values];
        }

        foreach (var c in clients)
        {
            c.Close();
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = await _accept(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // All pipe instances busy, or a client vanished mid-connect: try again shortly.
                await Task.Delay(200, ct).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => ServeAsync(stream, ct), ct);
        }
    }

    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        Client? client = null;
        try
        {
            var hello = new byte[FrameProtocol.HeaderSize];
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(_helloTimeout);
                await stream.ReadExactlyAsync(hello, timeout.Token).ConfigureAwait(false);
            }

            var result = FrameProtocol.Decode(hello, out var h);
            if (result != FrameDecodeResult.Ok || h.Type != FrameMessageType.Hello || h.Width == 0 || h.Height == 0)
            {
                ProtocolError?.Invoke(this, $"bad hello: {result}, type {h.Type}, {h.Width}x{h.Height}");
                return;
            }

            var info = new ConsumerInfo(Interlocked.Increment(ref _nextId), (int)h.Width, (int)h.Height, (int)Math.Max(1, h.Param));
            client = new Client(info, stream);
            lock (_gate)
            {
                _clients[info.Id] = client;
            }

            ConsumerConnected?.Invoke(this, info);

            // The consumer never sends anything after Hello, so a completed read means it went away.
            var watch = WatchForDisconnectAsync(stream, ct);
            var write = client.WriteLoopAsync(ct);
            await Task.WhenAny(watch, write).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or EndOfStreamException or ObjectDisposedException)
        {
            if (client is null && !ct.IsCancellationRequested)
            {
                ProtocolError?.Invoke(this, $"handshake failed: {e.GetType().Name}");
            }
        }
        finally
        {
            if (client is not null)
            {
                lock (_gate)
                {
                    _clients.Remove(client.Info.Id);
                }

                client.Close();
                ConsumerDisconnected?.Invoke(this, client.Info);
            }

            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task WatchForDisconnectAsync(Stream stream, CancellationToken ct)
    {
        var buf = new byte[1];
        try
        {
            while (await stream.ReadAsync(buf, ct).ConfigureAwait(false) > 0)
            {
                // Ignore anything unexpected; v1 consumers send nothing after Hello.
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>One consumer with a latest-frame slot (double-buffered, no per-frame allocation).</summary>
    private sealed class Client(ConsumerInfo info, Stream stream)
    {
        private readonly object _slot = new();
        private readonly SemaphoreSlim _signal = new(0, 1);
        private byte[] _pending = new byte[FrameProtocol.HeaderSize + FrameProtocol.Nv12Size(info.Width, info.Height)];
        private byte[] _sending = new byte[FrameProtocol.HeaderSize + FrameProtocol.Nv12Size(info.Width, info.Height)];
        private bool _hasPending;
        private volatile bool _closed;

        public ConsumerInfo Info { get; } = info;

        public void Offer(FrameHeader header, ReadOnlySpan<byte> nv12)
        {
            if (_closed)
            {
                return;
            }

            lock (_slot)
            {
                FrameProtocol.Encode(header, _pending);
                nv12[..(int)header.PayloadSize].CopyTo(_pending.AsSpan(FrameProtocol.HeaderSize));
                if (_hasPending)
                {
                    return; // replaced an unsent frame; the writer is already signalled
                }

                _hasPending = true;
            }

            _signal.Release();
        }

        public async Task WriteLoopAsync(CancellationToken ct)
        {
            while (!_closed)
            {
                await _signal.WaitAsync(ct).ConfigureAwait(false);
                lock (_slot)
                {
                    (_pending, _sending) = (_sending, _pending);
                    _hasPending = false;
                }

                await stream.WriteAsync(_sending, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
        }

        public void Close()
        {
            _closed = true;
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }
}
