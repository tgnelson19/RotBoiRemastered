using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RotBoiRemastered.Core;

/// <summary>
/// Entry point and top-level state dispatch. Mirrors main.py's main()/runGame()/
/// runTitle()/runLeveling()/runPaused()/runResults() family -- each branch below
/// is a placeholder for the corresponding Python function until it's ported.
/// </summary>
public class RotBoiGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    public GameState State { get; set; } = GameState.TitleScreen;

    public RotBoiGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            // The Python version defaults to native-resolution fullscreen
            // (variableHolster.py). Windowed here is friendlier for dev builds;
            // revisit once resolution/scale handling (uiTheme.display_scale) is ported.
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "RotBoi Remastered";
    }

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        switch (State)
        {
            case GameState.GameRun:
                // TODO: port character.py's runGame() body.
                break;
            case GameState.TitleScreen:
                // TODO: port character.py's runTheTitleScreen() + main.py's runTitle().
                break;
            case GameState.Leveling:
                // TODO: port character.py's handleLevelingProcess() (main.py's runLeveling()).
                break;
            case GameState.Paused:
                // TODO: port menus.py's draw_pause()/handle_pause().
                break;
            case GameState.Results:
                // TODO: port menus.py's draw_results()/handle_results().
                break;
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        // TODO: dispatch to per-state draw calls once ported (uiTheme.py draw_*
        // helpers are the natural first module to port, since nearly everything
        // else calls into them).

        base.Draw(gameTime);
    }
}
