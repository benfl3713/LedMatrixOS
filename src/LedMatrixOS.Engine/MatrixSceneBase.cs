using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Engine;

/// <summary>
/// Convenience base class for all Matrix scenes.
/// Provides the same background-task and lifecycle-cancellation pattern
/// that <c>MatrixAppBase</c> offered for the legacy IMatrixApp system.
/// </summary>
public abstract class MatrixSceneBase : IMatrixScene
{
    private readonly ConcurrentBag<Task> _backgroundTasks = new();
    private CancellationTokenSource _lifecycleCts = new();

    // ── Protected helpers ────────────────────────────────────────────────────

    protected GraphicsDevice GraphicsDevice { get; private set; } = null!;
    protected ContentManager Content { get; private set; } = null!;
    protected int MatrixWidth { get; private set; }
    protected int MatrixHeight { get; private set; }

    // ── IMatrixScene ─────────────────────────────────────────────────────────

    public abstract string Id { get; }
    public abstract string Name { get; }

    public virtual void Initialize(GraphicsDevice graphicsDevice, int matrixWidth, int matrixHeight)
    {
        GraphicsDevice = graphicsDevice;
        MatrixWidth = matrixWidth;
        MatrixHeight = matrixHeight;
        _lifecycleCts = new CancellationTokenSource();
    }

    public virtual void LoadContent(ContentManager content)
    {
        Content = content;
    }

    public virtual void Unload()
    {
        _lifecycleCts.Cancel();

        while (_backgroundTasks.TryTake(out var task))
        {
            try
            {
                task.Wait(50);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Id}] Background task error during Unload: {ex.Message}");
            }
        }
    }

    public abstract void Update(GameTime gameTime, CancellationToken cancellationToken);
    public abstract void Draw(SpriteBatch spriteBatch);

    // ── Background task support ───────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="work"/> on a thread-pool thread, tied to this scene's
    /// lifecycle.  The task is automatically cancelled when the scene is unloaded.
    /// </summary>
    protected void RunInBackground(Func<CancellationToken, Task> work)
    {
        var token = _lifecycleCts.Token;
        _backgroundTasks.Add(Task.Run(() => work(token), token));
    }
}
