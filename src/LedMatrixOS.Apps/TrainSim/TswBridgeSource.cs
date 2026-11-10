using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace LedMatrixOS.Apps.TrainSim;

/// <summary>
/// Live telemetry from the tsw-companion bridge: a background WebSocket client on <c>ws://{address}/ws</c> that merges the bridge's snapshot and
/// update messages and reconnects (1 s, doubling to 10 s) whenever the bridge is down or closes the socket. <see cref="Current"/> is a reference
/// swapped on every change, so readers never see a half-merged state.
/// </summary>
public sealed class TswBridgeSource : ITrainSimSource, IDisposable
{
    public static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private string _address = "";
    private volatile TrainSimSnapshot _current = TrainSimSnapshot.Offline("");

    public TswBridgeSource(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public TrainSimSnapshot Current => _current;

    public void Start(string address)
    {
        address = NormaliseAddress(address);
        lock (_gate)
        {
            if (_cts is { IsCancellationRequested: false } && string.Equals(address, _address, StringComparison.Ordinal)) return;
            StopLocked();
            _address = address;
            _current = TrainSimSnapshot.Offline(address);
            var cts = _cts = new CancellationTokenSource();
            _ = Task.Run(() => RunAsync(address, cts.Token));
        }
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    public void Dispose() => Stop();

    private void StopLocked()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
        _current = TrainSimSnapshot.Offline(_address);
    }

    /// <summary>Accepts "host:port", "ws://host:port/ws" or "http://host:port/" and gives "host:port"; blank gives the default.</summary>
    public static string NormaliseAddress(string? address)
    {
        var a = (address ?? "").Trim();
        foreach (var scheme in new[] { "ws://", "wss://", "http://", "https://" })
            if (a.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) { a = a[scheme.Length..]; break; }
        int slash = a.IndexOf('/');
        if (slash >= 0) a = a[..slash];
        return a.Length == 0 ? "localhost:31280" : a;
    }

    public static Uri SocketUri(string address) => new($"ws://{NormaliseAddress(address)}/ws");

    private async Task RunAsync(string address, CancellationToken ct)
    {
        Uri uri;
        try { uri = SocketUri(address); }
        catch (UriFormatException) { return; }   // stays offline, showing the address that was tried

        var backoff = MinBackoff;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await socket.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
                }
                backoff = MinBackoff;
                await ReceiveLoopAsync(socket, address, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { /* bridge down or the socket dropped: back off and try again */ }

            if (ct.IsCancellationRequested) return;
            _current = TrainSimSnapshot.Offline(address);
            try { await Task.Delay(backoff, _time, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, string address, CancellationToken ct)
    {
        var state = new TswTelemetryState();
        var buffer = new ArrayBufferWriter<byte>(8192);
        bool haveSnapshot = false;

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            buffer.Clear();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.GetMemory(4096), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                buffer.Advance(result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            string json = Encoding.UTF8.GetString(buffer.WrittenSpan);
            if (!state.Apply(json)) continue;   // recorded features, unknown types, junk
            haveSnapshot = true;
            _current = state.Build(address, reachable: haveSnapshot, _time.GetUtcNow());
        }
    }
}
