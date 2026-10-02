namespace LedMatrixOS.Core;

/// <summary>
/// Holds the most recent presented frame as packed RGB so any number of viewers (WebSocket previews) can read it without touching the render loop.
/// Copying only happens while someone is subscribed.
/// </summary>
public sealed class FrameBroadcaster
{
    private readonly object _gate = new();
    private byte[] _rgb = [];
    private int _width, _height, _subscribers;
    private long _sequence;

    public int Subscribers { get { lock (_gate) return _subscribers; } }

    public void Publish(FrameBuffer frame)
    {
        lock (_gate)
        {
            if (_subscribers == 0) return;
            int size = frame.Width * frame.Height * 3;
            if (_rgb.Length != size) _rgb = new byte[size];
            _width = frame.Width;
            _height = frame.Height;

            var pixels = frame.GetPixelsSpan();
            for (int i = 0, o = 0; i < pixels.Length; i++)
            {
                _rgb[o++] = pixels[i].R;
                _rgb[o++] = pixels[i].G;
                _rgb[o++] = pixels[i].B;
            }
            _sequence++;
        }
    }

    /// <summary>Counts a viewer; dispose it when they leave.</summary>
    public IDisposable Subscribe()
    {
        lock (_gate) _subscribers++;
        return new Subscription(this);
    }

    /// <summary>
    /// Copies the latest frame into <paramref name="message"/> as [width u16 LE][height u16 LE][RGB...] when it is newer than
    /// <paramref name="lastSequence"/>. Returns the message length, or 0 when there is nothing new.
    /// </summary>
    public int TryRead(ref long lastSequence, ref byte[] message)
    {
        lock (_gate)
        {
            if (_sequence == 0 || _sequence == lastSequence) return 0;
            int length = 4 + _rgb.Length;
            if (message.Length < length) message = new byte[length];
            message[0] = (byte)_width; message[1] = (byte)(_width >> 8);
            message[2] = (byte)_height; message[3] = (byte)(_height >> 8);
            Buffer.BlockCopy(_rgb, 0, message, 4, _rgb.Length);
            lastSequence = _sequence;
            return length;
        }
    }

    private sealed class Subscription(FrameBroadcaster owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                lock (owner._gate) owner._subscribers--;
        }
    }
}
