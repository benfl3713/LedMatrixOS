using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Engine;

/// <summary>
/// The core contract every Matrix scene (app) must fulfil.
/// Scenes are created fresh on each activation and disposed when the next scene takes over.
/// </summary>
public interface IMatrixScene
{
    /// <summary>Unique identifier matching the app-management system.</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string Name { get; }

    /// <summary>
    /// Called once after creation.  Sets up any GraphicsDevice-dependent resources
    /// (vertex buffers, render targets, etc.).
    /// </summary>
    void Initialize(GraphicsDevice graphicsDevice, int matrixWidth, int matrixHeight);

    /// <summary>Called once after <see cref="Initialize"/>.  Load textures, fonts, sounds.</summary>
    void LoadContent(ContentManager content);

    /// <summary>
    /// Called when this scene is about to be replaced.
    /// Cancel background work and dispose scene-owned resources here.
    /// </summary>
    void Unload();

    /// <summary>Per-frame logic update.</summary>
    void Update(GameTime gameTime, CancellationToken cancellationToken);

    /// <summary>
    /// Per-frame draw.  The <see cref="GraphicsDevice"/> render target is already set to
    /// the matrix render target (width × height) when this is called.
    /// The scene is responsible for calling <see cref="SpriteBatch.Begin"/> and
    /// <see cref="SpriteBatch.End"/> itself to allow per-scene blend/sampler state.
    /// </summary>
    void Draw(SpriteBatch spriteBatch);
}
