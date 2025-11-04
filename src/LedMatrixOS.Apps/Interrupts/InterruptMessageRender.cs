using LedMatrixOS.Apps.Common;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;

namespace LedMatrixOS.Apps.Interrupts;

public class InterruptMessageRender : IDisposable
{
    private readonly string _message;
    private readonly int _maxWidth;
    private ScrollOverflowTextForFrameBuffer _scrollingText;
    private bool _finishedScroll = false;
    private DateTime? _firstRender;

    public InterruptMessageRender(string message, int maxWidth)
    {
        _message = message;
        _maxWidth = maxWidth;
        _scrollingText = new ScrollOverflowTextForFrameBuffer(0, 32, maxWidth, Fonts.Big, new Pixel(200, 0, 0));
        _scrollingText.OnResetPosition += HandleTextReset;
    }
    public void Render(FrameBuffer frame)
    {
        _firstRender ??= DateTime.Now;
        _scrollingText.Draw(frame, _message);
    }

    public bool HasFinished()
    {
        return _finishedScroll || (_scrollingText.TextFits is true && _firstRender.HasValue && DateTime.Now >  _firstRender.Value.AddSeconds(5));
    }

    public void Dispose()
    {
        _scrollingText.OnResetPosition -= HandleTextReset;
    }

    private void HandleTextReset(object? sender, EventArgs e)
    {
        _finishedScroll = true;
    }
}
