using System.IO.Pipes;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;

namespace CamTurn.Testing;

/// <summary>A camera whose frames and failures are pushed by the test.</summary>
public sealed class FakeCamera(CameraDescriptor? descriptor = null) : ICameraSource
{
    public CameraDescriptor Descriptor { get; } = descriptor ?? new CameraDescriptor("fake", "Fake camera", "Front", [new(1280, 720, 30, "NV12")]);

    public CameraState State { get; private set; } = CameraState.Closed;

    public bool Busy { get; set; }

    public int StartCount { get; private set; }

    public event EventHandler<VideoFrame>? FrameArrived;

    public event EventHandler<CameraState>? StateChanged;

    public bool Start(VideoFormat format)
    {
        StartCount++;
        Set(Busy ? CameraState.Busy : CameraState.Streaming);
        return !Busy;
    }

    public void Stop() => Set(CameraState.Closed);

    public void Push(VideoFrame frame)
    {
        if (State == CameraState.Streaming)
        {
            FrameArrived?.Invoke(this, frame);
        }
    }

    public void Unplug() => Set(CameraState.Disconnected);

    private void Set(CameraState s)
    {
        if (s != State)
        {
            State = s;
            StateChanged?.Invoke(this, s);
        }
    }
}

/// <summary>Virtual camera registrar that only records calls.</summary>
public sealed class NullVirtualCamera : IVirtualCameraRegistrar
{
    public VirtualCameraState State { get; set; } = VirtualCameraState.Registered;

    public List<string> Calls { get; } = [];

    public VirtualCameraState GetState() => State;

    public void Register(bool allUsers)
    {
        Calls.Add($"register allUsers={allUsers}");
        State = VirtualCameraState.Registered;
    }

    public void Unregister(bool allUsers)
    {
        Calls.Add($"unregister allUsers={allUsers}");
        State = VirtualCameraState.NotRegistered;
    }
}

public static class TestPipes
{
    /// <summary>A unique pipe name so parallel tests do not collide.</summary>
    public static string UniqueName() => "CamTurn.Test." + Guid.NewGuid().ToString("N");

    /// <summary>Plain (no ACL) named-pipe accept function; works on Windows and on Linux (Unix domain sockets).</summary>
    public static Func<CancellationToken, ValueTask<Stream>> Acceptor(string name) => async ct =>
    {
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    };
}
