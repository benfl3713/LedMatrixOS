namespace LedMatrixOS.Core.Input;

/// <summary>
/// Thread-safe, bounded, allocation-free input queue. Network threads call <see cref="Enqueue"/> (usually through an
/// <see cref="InputClient"/>); the render engine calls <see cref="Dispatch"/> once per frame on the render thread, which updates
/// the held-button state and hands events to the active app. Redundant events (Down for a held button, Up for a released one)
/// are dropped. Held state is released when the active app changes (<see cref="ReleaseAll"/>) or a client disconnects.
/// </summary>
public sealed class InputHub
{
    public const int Capacity = 256;
    private const int DownReserve = 64; // Ups may use the last slots so a button can never stay stuck for lack of room
    private const int BitCount = InputEvent.MaxPlayers * InputEvent.ButtonCount;

    /// <summary>A hub that never receives anything; the default for apps running without an engine.</summary>
    public static InputHub None { get; } = new();

    private readonly object _gate = new();
    private readonly InputEvent[] _queue = new InputEvent[Capacity];
    private int _head, _count;
    private uint _held; // bit = player * 8 + button, as delivered to the app
    private long _dropped;

    /// <summary>Events dropped because the queue was full (diagnostics).</summary>
    public long Dropped { get { lock (_gate) return _dropped; } }

    internal static uint BitOf(int player, InputButton button) => 1u << (player * InputEvent.ButtonCount + (int)button);

    /// <summary>True while the button is held, as of the last <see cref="Dispatch"/>.</summary>
    public bool IsDown(int player, InputButton button)
    {
        if ((uint)player >= InputEvent.MaxPlayers) return false;
        lock (_gate) return (_held & BitOf(player, button)) != 0;
    }

    /// <summary>True while any player holds the button.</summary>
    public bool IsDownAny(InputButton button)
    {
        for (int p = 0; p < InputEvent.MaxPlayers; p++) if (IsDown(p, button)) return true;
        return false;
    }

    /// <summary>Queues an event. Returns false when it was dropped (invalid or queue full).</summary>
    public bool Enqueue(in InputEvent e)
    {
        if ((uint)e.Player >= InputEvent.MaxPlayers || !Enum.IsDefined(e.Button)) return false;
        lock (_gate) return EnqueueLocked(e);
    }

    private bool EnqueueLocked(in InputEvent e)
    {
        int limit = e.State == InputState.Down ? Capacity - DownReserve : Capacity;
        if (_count >= limit) { _dropped++; return false; }
        _queue[(_head + _count) % Capacity] = e;
        _count++;
        return true;
    }

    /// <summary>Queues a press: Down immediately followed by Up (both or neither).</summary>
    public bool EnqueuePress(int player, InputButton button)
    {
        if ((uint)player >= InputEvent.MaxPlayers || !Enum.IsDefined(button)) return false;
        lock (_gate)
        {
            if (_count + 2 > Capacity - DownReserve) { _dropped++; return false; }
            EnqueueLocked(new InputEvent(player, button, InputState.Down));
            EnqueueLocked(new InputEvent(player, button, InputState.Up));
            return true;
        }
    }

    /// <summary>
    /// Render thread, start of a frame: applies queued events in order and passes each state change to <paramref name="consumer"/>.
    /// Returns how many events were delivered.
    /// </summary>
    public int Dispatch(IInputConsumer? consumer)
    {
        int delivered = 0;
        while (true)
        {
            InputEvent e;
            lock (_gate)
            {
                if (_count == 0) return delivered;
                e = _queue[_head];
                _head = (_head + 1) % Capacity;
                _count--;

                uint bit = BitOf(e.Player, e.Button);
                bool down = (_held & bit) != 0;
                if (e.State == InputState.Down) { if (down) continue; _held |= bit; }
                else { if (!down) continue; _held &= ~bit; }
            }
            delivered++;
            consumer?.OnInput(e);
        }
    }

    /// <summary>
    /// Drops everything queued and releases every held button, telling <paramref name="previous"/> (if any) about each release
    /// so it can clean up. Called by the engine when the active app changes.
    /// </summary>
    public void ReleaseAll(IInputConsumer? previous = null)
    {
        uint held;
        lock (_gate) { held = _held; _held = 0; _head = 0; _count = 0; }
        if (previous is null || held == 0) return;
        for (int bit = 0; bit < BitCount; bit++)
            if ((held & (1u << bit)) != 0)
                previous.OnInput(new InputEvent(bit / InputEvent.ButtonCount, (InputButton)(bit % InputEvent.ButtonCount), InputState.Up));
    }

    /// <summary>A sender (one per websocket) that remembers what it holds so it can release on disconnect.</summary>
    public InputClient CreateClient() => new(this);

    internal void EnqueueRelease(uint mask)
    {
        lock (_gate)
            for (int bit = 0; bit < BitCount; bit++)
                if ((mask & (1u << bit)) != 0)
                    EnqueueLocked(new InputEvent(bit / InputEvent.ButtonCount, (InputButton)(bit % InputEvent.ButtonCount), InputState.Up));
    }
}

/// <summary>Sends input on behalf of one connection; disposing it releases whatever that connection still holds.</summary>
public sealed class InputClient : IDisposable
{
    private readonly InputHub _hub;
    private readonly object _gate = new();
    private uint _held;

    internal InputClient(InputHub hub) => _hub = hub;

    public bool Send(in InputEvent e)
    {
        if ((uint)e.Player >= InputEvent.MaxPlayers || !Enum.IsDefined(e.Button)) return false;
        lock (_gate)
        {
            uint bit = InputHub.BitOf(e.Player, e.Button);
            if (e.State == InputState.Down) _held |= bit; else _held &= ~bit;
        }
        return _hub.Enqueue(e);
    }

    public bool SendPress(int player, InputButton button) => _hub.EnqueuePress(player, button);

    public void Dispose()
    {
        uint held;
        lock (_gate) { held = _held; _held = 0; }
        if (held != 0) _hub.EnqueueRelease(held);
    }
}
