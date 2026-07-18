using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Core;

/// <summary>
/// Entry point and top-level state dispatch. Ported from main.py's
/// main()/runGame()/runTitle()/runLeveling()/runPaused()/runResults() +
/// baseInputCollection()/update_input_toggles()/update_camera_controls().
///
/// Cleanup vs. the Python original:
/// - No pygame event queue exists in MonoGame; edge-triggered input
///   (`KeysPressed`, `MousePressed`) is derived by diffing this frame's
///   polled keyboard/mouse state against last frame's, a standard MonoGame
///   idiom (see <see cref="CollectInput"/>).
/// - Keyboard, mouse, and first-controller input are polled explicitly;
///   F11 toggles borderless fullscreen while preserving the window size.
/// - `hasBeenReset`'s two-call reset dance around the title screen has no
///   observable effect here (the title screen never reads run stats), so
///   returning to it just leaves the previous <see cref="GameSession"/>
///   alone; the next "start run" always freshly resets/constructs one
///   anyway via <see cref="GameSession.ResetAll"/>.
/// </summary>
public class RotBoiGame : Game
{
    private const float CameraRotationDegreesPerSecond = 180.0f;

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    private readonly Menus _menus = new();
    private readonly TitleScreen _titleScreen = new();
    private readonly SoulHub _soulHub = new();
    private GameSession? _session;
    private GameState _pauseReturnState = GameState.GameRun;

    private KeyboardState _previousKeyboardState;
    private ButtonState _previousMouseButtonState = ButtonState.Released;
    private GamePadState _previousGamePadState;
    private int _windowedWidth = 1280;
    private int _windowedHeight = 720;

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
        Window.ClientSizeChanged += (_, _) =>
            _session?.Resize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        UiTheme.Initialize(GraphicsDevice);
        Primitives2D.Initialize(GraphicsDevice);
    }

    // ----- Update -----

    protected override void Update(GameTime gameTime)
    {
        Simulation.SetDeltaTime(gameTime.ElapsedGameTime.TotalMilliseconds);
        CollectInput();

        IsMouseVisible = State != GameState.GameRun || _session is null
            || InputState.MousePosition.X >= _session.InformationSheet.ArenaWidth
            || _session.InformationSheet.DragInProgress;

        if (InputState.KeysPressed.Contains(Keys.F11))
            ToggleFullscreen();

        if (InputState.ControllerPausePressed && State == GameState.Paused)
        {
            State = _pauseReturnState;
            base.Update(gameTime);
            return;
        }

        bool enteredPause = false;
        if (InputState.KeysPressed.Contains(Keys.Escape) || InputState.ControllerPausePressed)
        {
            if (State == GameState.Soul && _soulHub.OverlayOpen)
            {
                _soulHub.CloseOverlay();
                enteredPause = true;
            }
            else if (State == GameState.GameRun || State == GameState.Soul)
            {
                _pauseReturnState = State;
                State = GameState.Paused;
                _session?.InformationSheet.CancelDrag();
                enteredPause = true;
            }
            else if (State == GameState.Leveling)
            {
                _pauseReturnState = GameState.Leveling;
                State = GameState.Paused;
                _session?.InformationSheet.CancelDrag();
                enteredPause = true;
            }
        }

        UpdateInputToggles();
        UpdateCameraControls(gameTime);

        // Do not let the Escape press that opened pause immediately resume it.
        if (enteredPause)
        {
            base.Update(gameTime);
            return;
        }

        switch (State)
        {
            case GameState.TitleScreen:
                UpdateTitleScreen();
                break;
            case GameState.GameRun:
                UpdateGameRun(gameTime);
                break;
            case GameState.Leveling:
                UpdateLeveling();
                break;
            case GameState.Paused:
                UpdatePaused();
                break;
            case GameState.Results:
                UpdateResults();
                break;
            case GameState.Soul:
                UpdateSoul(gameTime);
                break;
        }

        base.Update(gameTime);
    }

    private void ToggleFullscreen()
    {
        if (_graphics.IsFullScreen)
        {
            _graphics.IsFullScreen = false;
            _graphics.PreferredBackBufferWidth = _windowedWidth;
            _graphics.PreferredBackBufferHeight = _windowedHeight;
        }
        else
        {
            _windowedWidth = Math.Max(640, GraphicsDevice.Viewport.Width);
            _windowedHeight = Math.Max(360, GraphicsDevice.Viewport.Height);
            var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.HardwareModeSwitch = false;
            _graphics.PreferredBackBufferWidth = mode.Width;
            _graphics.PreferredBackBufferHeight = mode.Height;
            _graphics.IsFullScreen = true;
        }
        _graphics.ApplyChanges();
        _session?.Resize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    }

    /// <summary>Ported from main.py's baseInputCollection()'s event-drain shape, using polled state diffs instead of a pygame event queue.</summary>
    private void CollectInput()
    {
        var keyboardState = Keyboard.GetState();
        InputState.KeysPressed.Clear();
        foreach (var key in keyboardState.GetPressedKeys())
        {
            if (!_previousKeyboardState.IsKeyDown(key))
                InputState.KeysPressed.Add(key);
        }
        InputState.KeyboardState = keyboardState;
        _previousKeyboardState = keyboardState;

        var mouseState = Mouse.GetState();
        InputState.MousePosition = mouseState.Position;
        InputState.MouseDown = mouseState.LeftButton == ButtonState.Pressed;
        InputState.MousePressed = mouseState.LeftButton == ButtonState.Pressed && _previousMouseButtonState == ButtonState.Released;
        _previousMouseButtonState = mouseState.LeftButton;

        var gamePadState = GamePad.GetState(PlayerIndex.One);
        var left = gamePadState.ThumbSticks.Left;
        var right = gamePadState.ThumbSticks.Right;
        InputState.ControllerMove = left.Length() > .2f ? new Vector2(left.X, -left.Y) : Vector2.Zero;
        InputState.ControllerAim = right.Length() > .25f ? new Vector2(right.X, -right.Y) : Vector2.Zero;
        InputState.ControllerDashPressed = gamePadState.Buttons.A == ButtonState.Pressed
            && _previousGamePadState.Buttons.A == ButtonState.Released;
        InputState.ControllerAutofirePressed = gamePadState.Buttons.X == ButtonState.Pressed
            && _previousGamePadState.Buttons.X == ButtonState.Released;
        InputState.ControllerPausePressed = gamePadState.Buttons.Start == ButtonState.Pressed
            && _previousGamePadState.Buttons.Start == ButtonState.Released;
        _previousGamePadState = gamePadState;
    }

    /// <summary>Ported from main.py's update_input_toggles().</summary>
    private void UpdateInputToggles()
    {
        if (Keybinds.Pressed("dev_boss") && _session is not null && _session.State.ActiveBoss is null && !_session.State.BossDebugRequested)
            _session.State.BossDebugRequested = true;
        if (Keybinds.Pressed("autofire") || InputState.ControllerAutofirePressed)
        {
            GameProfile.Profile.AutoFire = !GameProfile.Profile.AutoFire;
            if (_session is not null)
                _session.State.AutoFire = GameProfile.Profile.AutoFire;
            GameProfile.SaveProfile();
        }
        if (Keybinds.Pressed("hud_toggle") && State == GameState.GameRun)
            _session!.ToggleHudMode();
        if (Keybinds.Pressed("dev_invincible") && _session is not null)
            _session.State.BossDebugInvincible = !_session.State.BossDebugInvincible;
        if (Keybinds.Pressed("dev_level_up") && State == GameState.GameRun)
            _session!.DebugForceLevelUp();
    }

    /// <summary>Ported from main.py's update_camera_controls().</summary>
    private void UpdateCameraControls(GameTime gameTime)
    {
        if ((State != GameState.GameRun && State != GameState.Soul) || _session is null)
            return;
        if (State == GameState.GameRun && Keybinds.Pressed("zoom_out"))
            _session.Camera.AdjustZoom(-Camera.ZoomStep);
        if (State == GameState.GameRun && Keybinds.Pressed("zoom_in"))
            _session.Camera.AdjustZoom(Camera.ZoomStep);
        if (Keybinds.Pressed("camera_reset"))
        {
            _session.Camera.SetAngle(0);
            return;
        }
        int direction = (Keybinds.Held("rotate_right") ? 1 : 0) - (Keybinds.Held("rotate_left") ? 1 : 0);
        if (direction == 0)
            return;
        double elapsedSeconds = Math.Clamp(gameTime.ElapsedGameTime.TotalMilliseconds, 0, 50) / 1000.0;
        _session.Camera.Rotate((float)(direction * CameraRotationDegreesPerSecond * elapsedSeconds));
    }

    private void UpdateTitleScreen()
    {
        var action = _titleScreen.HandleInput(InputState.KeysPressed, InputState.MousePosition, InputState.MousePressed);
        switch (action)
        {
            case TitleAction.StartRun:
            {
                var battleground = GamePaths.ActivateSelected();
                if (_session is null)
                    _session = new GameSession(battleground, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
                else
                    _session.ResetAll(battleground);
                _session.LoadStartingEquipment();
                State = GameState.GameRun;
                break;
            }
            case TitleAction.EnterSoul:
            {
                var battleground = GamePaths.ActivateSelected();
                if (_session is null)
                    _session = new GameSession(battleground, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
                else
                    _session.ResetAll(battleground);
                _soulHub.Enter(_session);
                State = GameState.Soul;
                break;
            }
            case TitleAction.Settings:
                _pauseReturnState = GameState.TitleScreen;
                State = GameState.Paused;
                break;
            case TitleAction.Quit:
                GameProfile.SaveProfile();
                Exit();
                break;
        }
    }

    /// <summary>Ported from main.py's runGame() body (character.py's per-frame update calls, in the same order).</summary>
    private void UpdateGameRun(GameTime gameTime)
    {
        var session = _session!;
        session.State.RunTimeSeconds += Math.Min(gameTime.ElapsedGameTime.TotalMilliseconds, 50) / 1000.0;

        bool moveUp = Keybinds.Held("move_up"), moveDown = Keybinds.Held("move_down");
        bool moveLeft = Keybinds.Held("move_left"), moveRight = Keybinds.Held("move_right");
        session.MovePlayer(moveLeft, moveRight, moveUp, moveDown,
            Keybinds.Pressed("dash") || InputState.ControllerDashPressed, InputState.ControllerMove);

        var mouseScreen = new Vector2(InputState.MousePosition.X, InputState.MousePosition.Y);
        bool controllerFiring = InputState.ControllerAim.LengthSquared() > .0625f;
        if (controllerFiring)
        {
            var origin = session.Camera.Lock;
            mouseScreen = origin + new Vector2(InputState.ControllerAim.X * GraphicsDevice.Viewport.Width,
                InputState.ControllerAim.Y * GraphicsDevice.Viewport.Height);
        }
        session.HandleBulletCreation(mouseScreen, InputState.MouseDown, session.InformationSheet.DragInProgress,
            controllerFiring: controllerFiring);
        session.UpdateBullets();

        session.HandleEnemyCreation();
        session.HandleBossDebugControls(InputState.KeysPressed);
        session.UpdateEnemies();
        session.UpdateEnemyProjectiles();
        session.HandleDamagingEnemies();

        session.UpdateDamageTexts();
        session.UpdateExperience();
        bool enteredLeveling = session.ExpForPlayer();
        session.UpdateCrateInteraction();
        session.RecoverPlayerHealth();

        bool fatalHit = session.HurtPlayer();
        if (fatalHit)
        {
            State = GameState.Results;
            return;
        }
        if (session.State.GameCompleted && InputState.KeysPressed.Contains(Keys.Enter))
        {
            State = GameState.Results;
            return;
        }
        if (enteredLeveling)
            State = GameState.Leveling;
    }

    private void UpdateLeveling()
    {
        var outcome = _session!.HandleLevelingInput(InputState.KeysPressed, InputState.MousePosition, InputState.MouseDown);
        if (outcome == LevelUpOutcome.ReturnToGame)
            State = GameState.GameRun;
    }

    private void UpdatePaused()
    {
        bool soulContext = _pauseReturnState == GameState.Soul;
        bool settingsOnly = _pauseReturnState == GameState.TitleScreen;
        bool canExtract = _session is not null && !soulContext && _session.State.BeaudisDefeated && !_session.State.GameCompleted;
        var action = _menus.HandlePause(InputState.KeysPressed, InputState.MousePosition, InputState.MouseDown,
            InputState.MousePressed, canExtract, soulContext, settingsOnly);
        // Menus edits the persisted default; the live run keeps a cached copy.
        if (_session is not null)
        {
            _session.State.AutoFire = GameProfile.Profile.AutoFire;
            _session.Resize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        }
        switch (action)
        {
            case MenuAction.Resume:
                State = _pauseReturnState;
                break;
            case MenuAction.Restart:
                if (_session is null) break;
                GameProfile.SaveProfile();
                _session.ResetAll(GamePaths.ActivateSelected());
                State = GameState.GameRun;
                break;
            case MenuAction.ReturnToTitle:
                GameProfile.SaveProfile();
                State = GameState.TitleScreen;
                break;
            case MenuAction.Extract:
                if (_session is null) break;
                _session.State.RunOutcome = "EXTRACTED";
                MetaProgression.RecordExtraction(_session.State, GamePaths.Selected().Key, completed: false);
                GameProfile.RecordRun(_session.State.CurrentLevel, _session.State.NumOfEnemiesKilled);
                State = GameState.Results;
                break;
        }
    }

    private void UpdateResults()
    {
        var action = _menus.HandleResults(InputState.KeysPressed, InputState.MousePosition, InputState.MousePressed);
        switch (action)
        {
            case MenuAction.Restart:
                _session!.ResetAll(GamePaths.ActivateSelected());
                State = GameState.GameRun;
                break;
            case MenuAction.ReturnToTitle:
                State = GameState.TitleScreen;
                break;
        }
    }

    private void UpdateSoul(GameTime gameTime)
    {
        var session = _session!;
        session.MovePlayer(Keybinds.Held("move_left"), Keybinds.Held("move_right"), Keybinds.Held("move_up"), Keybinds.Held("move_down"),
            Keybinds.Pressed("dash") || InputState.ControllerDashPressed, InputState.ControllerMove);
        _soulHub.HandleInput(session, InputState.KeysPressed, InputState.MousePosition, InputState.MousePressed);
        if (_soulHub.OverlayOpen)
            return;
        var aim = new Vector2(InputState.MousePosition.X, InputState.MousePosition.Y);
        bool controllerFiring = InputState.ControllerAim.LengthSquared() > .0625f;
        if (controllerFiring)
            aim = session.Camera.Lock + InputState.ControllerAim * GraphicsDevice.Viewport.Width;
        session.HandleBulletCreation(aim, InputState.MouseDown, dragInProgress: false, controllerFiring: controllerFiring);
        session.UpdateBullets();
        _soulHub.Update(session, gameTime.ElapsedGameTime.TotalSeconds);
    }

    // ----- Draw -----

    protected override void Draw(GameTime gameTime)
    {
        switch (State)
        {
            case GameState.GameRun:
                DrawGameRun();
                break;
            case GameState.Leveling:
                DrawLeveling();
                break;
            case GameState.TitleScreen:
                DrawTitleScreen();
                break;
            case GameState.Paused:
                DrawPaused();
                break;
            case GameState.Results:
                DrawResults();
                break;
            case GameState.Soul:
                DrawSoul();
                break;
        }

        base.Draw(gameTime);
    }

    /// <summary>
    /// Ported from character.py's per-frame draw calls (interleaved with
    /// their update counterparts in Python; separated here since Update/Draw
    /// are split throughout this port). DrawBackground manages its own
    /// scissor-clipped SpriteBatch.Begin/End pair and must run before this
    /// method's own Begin() -- see its doc comment.
    /// </summary>
    private void DrawGameRun()
    {
        var session = _session!;
        GraphicsDevice.Clear(Color.Black);
        session.DrawBackground(_spriteBatch, GraphicsDevice);

        _spriteBatch.Begin(transformMatrix: session.Camera.WorldTransform);
        session.DrawGroundEnemyProjectiles(_spriteBatch);
        session.DrawBullets(_spriteBatch);
        session.DrawEnemies(_spriteBatch);
        // Keep the player readable above enemy bodies while hostile projectiles
        // and telegraphs remain above the player as actionable threats (matches
        // GameSession's own doc comment on this ordering).
        session.DrawPlayer(_spriteBatch);
        session.DrawEnemyProjectiles(_spriteBatch);
        session.DrawDamageTexts(_spriteBatch);
        session.DrawExperience(_spriteBatch);
        session.DrawLootCrates(_spriteBatch);
        _spriteBatch.End();

        _spriteBatch.Begin();
        session.DrawCombatOverlays(_spriteBatch, InputState.MousePosition);
        session.DrawBountyIndicator(_spriteBatch);
        session.DrawInformationSheet(_spriteBatch, InputState.MousePosition);
        session.HandleInformationSheetDrag(InputState.MousePosition, InputState.MouseDown, InputState.MousePressed);
        _spriteBatch.End();
    }

    private void DrawLeveling()
    {
        GraphicsDevice.Clear(UiTheme.Void);
        _spriteBatch.Begin();
        _session!.DrawLevelingScreen(_spriteBatch, InputState.MousePosition, InputState.MouseDown);
        _spriteBatch.End();
    }

    private void DrawTitleScreen()
    {
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin();
        _titleScreen.Draw(_spriteBatch, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, InputState.MousePosition, InputState.MouseDown);
        _spriteBatch.End();
    }

    private void DrawPaused()
    {
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin();
        bool soulContext = _pauseReturnState == GameState.Soul;
        bool settingsOnly = _pauseReturnState == GameState.TitleScreen;
        bool canExtract = _session is not null && !soulContext && _session.State.BeaudisDefeated && !_session.State.GameCompleted;
        _menus.DrawPause(_spriteBatch, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
            InputState.MousePosition, InputState.MouseDown, canExtract, soulContext, settingsOnly);
        _spriteBatch.End();
    }

    private void DrawResults()
    {
        GraphicsDevice.Clear(Color.Black);
        var session = _session!;
        var snapshot = new RunResultsSnapshot
        {
            RunOutcome = session.State.RunOutcome,
            CurrentLevel = session.State.CurrentLevel,
            NumOfEnemiesKilled = session.State.NumOfEnemiesKilled,
            RunTimeSeconds = session.State.RunTimeSeconds,
            UpgradeTypeCounts = session.State.UpgradeTypeCounts,
        };
        _spriteBatch.Begin();
        _menus.DrawResults(_spriteBatch, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, snapshot, InputState.MousePosition, InputState.MouseDown);
        _spriteBatch.End();
    }

    private void DrawSoul()
    {
        var session = _session!;
        GraphicsDevice.Clear(Color.Black);
        session.DrawBackgroundFull(_spriteBatch, GraphicsDevice);
        _spriteBatch.Begin();
        session.DrawBullets(_spriteBatch);
        session.DrawPlayer(_spriteBatch);
        session.DrawDamageTexts(_spriteBatch);
        _soulHub.DrawWorld(_spriteBatch, session, InputState.MousePosition, InputState.MouseDown);
        _spriteBatch.End();
    }
}
