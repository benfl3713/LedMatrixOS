using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LedMatrixOS.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace LedMatrixOS.Engine;

/// <summary>
/// The MonoGame <see cref="Game"/> subclass that drives the entire LED matrix render pipeline.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Runs the 60 Hz fixed-timestep game loop.</item>
///   <item>Maintains a 256×64 (or configured size) <see cref="RenderTarget2D"/> that scenes draw into.</item>
///   <item>Upscales that target to the simulator window for development.</item>
///   <item>Reads pixels back from the GPU and forwards them to <see cref="IMatrixOutput"/> (hardware or null).</item>
///   <item>Manages scene lifetime and the horizontal slide transition animation.</item>
/// </list>
///
/// <b>Thread safety</b>: scene-switch requests arrive from the ASP.NET Core thread via
/// <see cref="AppManager.SceneActivationRequested"/> and are queued with a
/// <see cref="ConcurrentQueue{T}"/>.  They are processed on the game thread at the start
/// of each <see cref="Update"/> tick.
///
/// <b>Headless Pi mode</b>: set <c>SDL_VIDEODRIVER=offscreen</c> in the environment before
/// calling <see cref="Game.Run()"/> to suppress the OS window while keeping OpenGL context.
/// </summary>
public sealed class MatrixGame : Game
{
    // Cross-fade transition: 30 frames (~0.5 s at 60 Hz)
    private const int TransitionDurationFrames = 30;

    private readonly GraphicsDeviceManager _graphics;
    private readonly AppManager _appManager;
    private readonly IMatrixOutput _output;
    private readonly SceneRegistry _sceneRegistry;
    private readonly InterruptService? _interruptService;

    // Render targets
    private RenderTarget2D _matrixTarget = null!;       // active scene renders here
    private RenderTarget2D _transitionOldTarget = null!; // frozen snapshot of outgoing scene
    private RenderTarget2D _transitionNewTarget = null!; // incoming scene renders here during transition

    private SpriteBatch _spriteBatch = null!;
    private Color[] _pixelBuffer = null!; // reused for GPU readback

    // Scene state
    private IMatrixScene? _activeScene;
    private IMatrixScene? _incomingScene;  // non-null while a switch is in flight
    private bool _isTransitioning;
    private float _transitionProgress; // 0 → 1 during cross-fade

    // Thread-safe queue for app-switch requests from the HTTP thread
    private readonly ConcurrentQueue<string> _pendingSwitches = new();

    // Keyboard scene cycling (simulator only)
    private KeyboardState _prevKeyState;
    private List<string> _sceneIds = [];
    private int _sceneIndex;

    // HUD overlay (drawn on the simulator window, never sent to hardware)
    private string _hudText = string.Empty;
    private float _hudTimer;
    private const float HudDuration = 2.5f;

    // ── Public surface ────────────────────────────────────────────────────────

    public int MatrixWidth { get; }
    public int MatrixHeight { get; }
    public int SimulatorScale { get; }

    /// <summary>The currently active scene (null during startup before first activation).</summary>
    public IMatrixScene? ActiveScene => _activeScene;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="appManager">Used to subscribe to scene-activation requests.</param>
    /// <param name="output">Hardware output (or <see cref="NullMatrixOutput"/> in dev mode).</param>
    /// <param name="registry">Pre-populated scene factory registry.</param>
    /// <param name="interruptService">Optional; when supplied, MonoGame interrupts are drawn as overlays.</param>
    /// <param name="width">Physical LED matrix width in pixels (default 256).</param>
    /// <param name="height">Physical LED matrix height in pixels (default 64).</param>
    /// <param name="simulatorScale">Window upscale factor for the simulator window (default 4×).</param>
    public MatrixGame(
        AppManager appManager,
        IMatrixOutput output,
        SceneRegistry registry,
        InterruptService? interruptService = null,
        int width = 256,
        int height = 64,
        int simulatorScale = 4)
    {
        MatrixWidth = width;
        MatrixHeight = height;
        SimulatorScale = simulatorScale;
        _appManager = appManager;
        _output = output;
        _sceneRegistry = registry;
        _interruptService = interruptService;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = width * simulatorScale,
            PreferredBackBufferHeight = height * simulatorScale,
            IsFullScreen = false,
            SynchronizeWithVerticalRetrace = false
        };

        Content.RootDirectory = "Content";
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        IsMouseVisible = false;

        // Subscribe to scene-switch events raised by the HTTP layer on AppManager
        _appManager.SceneActivationRequested += (_, id) => _pendingSwitches.Enqueue(id);
    }

    // ── MonoGame lifecycle ────────────────────────────────────────────────────

    protected override void Initialize()
    {
        _matrixTarget = CreateMatrixRenderTarget();
        _transitionOldTarget = CreateMatrixRenderTarget();
        _transitionNewTarget = CreateMatrixRenderTarget();
        _pixelBuffer = new Color[MatrixWidth * MatrixHeight];
        _sceneIds = _sceneRegistry.RegisteredIds.ToList();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        // Process scene-switch requests queued from the HTTP thread.
        // Only process one per frame to avoid double-transitions.
        if (_pendingSwitches.TryDequeue(out var id))
        {
            PrepareIncomingScene(id);
            // Keep keyboard index in sync with API-driven switches too
            var apiIdx = _sceneIds.IndexOf(id);
            if (apiIdx >= 0) _sceneIndex = apiIdx;
        }

        // ── Keyboard scene cycling (simulator window) ─────────────────────────
        var keyState = Keyboard.GetState();
        if (_sceneIds.Count > 1 && !_isTransitioning)
        {
            bool goNext = IsKeyPressed(keyState, _prevKeyState, Keys.Right)
                       || IsKeyPressed(keyState, _prevKeyState, Keys.Tab);
            bool goPrev = IsKeyPressed(keyState, _prevKeyState, Keys.Left);
            if (goNext || goPrev)
            {
                _sceneIndex = ((goPrev ? _sceneIndex - 1 : _sceneIndex + 1) + _sceneIds.Count) % _sceneIds.Count;
                var nextId = _sceneIds[_sceneIndex];
                _pendingSwitches.Enqueue(nextId);
                _hudText = $"{_sceneIndex + 1}/{_sceneIds.Count}  {nextId}";
                _hudTimer = HudDuration;
            }
        }
        _prevKeyState = keyState;

        if (_hudTimer > 0f)
            _hudTimer -= (float)gameTime.ElapsedGameTime.TotalSeconds;

        _activeScene?.Update(gameTime, CancellationToken.None);

        // Keep the incoming scene ticking during the transition so it feels live
        if (_isTransitioning || _incomingScene != null)
        {
            _incomingScene?.Update(gameTime, CancellationToken.None);
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // ── 1. Start transition if incoming scene is ready but not yet animating ──
        if (_incomingScene != null && !_isTransitioning)
        {
            CaptureOldFrame();
            _isTransitioning = true;
            _transitionProgress = 0f;
        }

        // ── 2. Render scene(s) into _matrixTarget ────────────────────────────────
        if (_isTransitioning)
        {
            DrawTransitionFrame();
        }
        else
        {
            DrawNormalFrame();
        }

        // ── 3. Interrupt overlay (drawn on top of active scene) ───────────────────
        if (_interruptService?.HasMonoGameInterrupt() == true)
        {
            GraphicsDevice.SetRenderTarget(_matrixTarget);
            _interruptService.RunMonoGameInterrupt(req =>
            {
                req.Renderer(_spriteBatch);
            });
            GraphicsDevice.SetRenderTarget(null);
        }

        // ── 4. Upscale _matrixTarget to the simulator window ─────────────────────
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _spriteBatch.Draw(
            _matrixTarget,
            new Rectangle(0, 0, MatrixWidth * SimulatorScale, MatrixHeight * SimulatorScale),
            Color.White);
        _spriteBatch.End();

        // ── 5. GPU readback → hardware output ────────────────────────────────────
        if (_output.IsEnabled)
        {
            _matrixTarget.GetData(_pixelBuffer);
            _output.Present(MemoryMarshal.AsBytes<Color>(_pixelBuffer), MatrixWidth, MatrixHeight);
        }

        // ── 6. HUD overlay on the simulator window (never reaches hardware) ────────
        if (_hudTimer > 0f && _hudText.Length > 0 && MatrixFonts.Small is not null)
        {
            float alpha = Math.Min(1f, _hudTimer); // fade out in last second
            _spriteBatch.Begin(
                samplerState: SamplerState.PointClamp,
                transformMatrix: Matrix.CreateScale(2f));
            MatrixFonts.Small.DrawString(_spriteBatch, _hudText, 2, MatrixFonts.Small.Height + 2, Color.Black * alpha);
            MatrixFonts.Small.DrawString(_spriteBatch, _hudText, 1, MatrixFonts.Small.Height + 1, Color.White * alpha);
            _spriteBatch.End();
        }

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeScene?.Unload();
            _incomingScene?.Unload();
            _matrixTarget?.Dispose();
            _transitionOldTarget?.Dispose();
            _transitionNewTarget?.Dispose();
            _spriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private RenderTarget2D CreateMatrixRenderTarget() =>
        new(GraphicsDevice, MatrixWidth, MatrixHeight,
            mipMap: false, SurfaceFormat.Color, DepthFormat.None);

    private void PrepareIncomingScene(string id)
    {
        if (!_sceneRegistry.TryCreate(id, out var next) || next is null) return;

        next.Initialize(GraphicsDevice, MatrixWidth, MatrixHeight);
        next.LoadContent(Content);
        _incomingScene = next;
    }

    private void CaptureOldFrame()
    {
        // Render the current active scene into _transitionOldTarget as a frozen snapshot.
        GraphicsDevice.SetRenderTarget(_transitionOldTarget);
        GraphicsDevice.Clear(Color.Black);
        _activeScene?.Draw(_spriteBatch); // scene calls spriteBatch.Begin/End internally
        GraphicsDevice.SetRenderTarget(null);
    }

    private void DrawNormalFrame()
    {
        GraphicsDevice.SetRenderTarget(_matrixTarget);
        GraphicsDevice.Clear(Color.Black);
        _activeScene?.Draw(_spriteBatch);
        GraphicsDevice.SetRenderTarget(null);
    }

    private void DrawTransitionFrame()
    {
        // Render incoming scene to its own target (it may call GraphicsDevice.Clear internally).
        GraphicsDevice.SetRenderTarget(_transitionNewTarget);
        GraphicsDevice.Clear(Color.Black);
        _incomingScene!.Draw(_spriteBatch);

        // Advance progress (clamped to 1).
        _transitionProgress = Math.Min(1f, _transitionProgress + 1f / TransitionDurationFrames);

        // Cross-fade: draw old scene at full opacity, then blend new scene on top.
        // SpriteBatch with AlphaBlend: result = src * srcAlpha + dest * (1 - srcAlpha)
        // Drawing old first at Color.White then new at Color.White * progress gives:
        //   final = new * progress + old * (1 - progress)  — a clean linear cross-fade.
        GraphicsDevice.SetRenderTarget(_matrixTarget);
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _spriteBatch.Draw(_transitionOldTarget, Vector2.Zero, Color.White);
        _spriteBatch.Draw(_transitionNewTarget, Vector2.Zero, Color.White * _transitionProgress);
        _spriteBatch.End();
        GraphicsDevice.SetRenderTarget(null);

        if (_transitionProgress >= 1f)
        {
            _activeScene?.Unload();
            _activeScene = _incomingScene;
            _incomingScene = null;
            _isTransitioning = false;
        }
    }

    private static bool IsKeyPressed(KeyboardState current, KeyboardState previous, Keys key) =>
        current.IsKeyDown(key) && previous.IsKeyUp(key);
}
