using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Entities;
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
    private readonly MindHub _soulHub = new();
    private readonly DevConsole _devConsole = new();
    private GameSession? _session;
    private RunResultReport? _resultReport;
    private GameState _pauseReturnState = GameState.GameRun;
    /// <summary>Which state the compact stash panel (GameState.Dossier) should return to on close -- it's reachable from both a run and The Mind now.</summary>
    private GameState _dossierReturnState = GameState.GameRun;
    /// <summary>GameTime.TotalGameTime.TotalSeconds when the stash panel was last opened -- drives its slide/fade-in reveal animation in DrawDossier.</summary>
    private double _dossierOpenedAt = -1000;
    private const double DossierRevealSeconds = .22;

    private KeyboardState _previousKeyboardState;
    private ButtonState _previousMouseButtonState = ButtonState.Released;
    private int _previousScrollWheelValue;
    private GamePadState _previousGamePadState;
    private Point _uiRepeatDirection;
    private double _uiNavigationClock;
    private double _uiNextRepeat;
    private int _windowedWidth = 1280;
    private int _windowedHeight = 720;
    private int _appliedMaxFrameRate;
    private bool _appliedVSync;
    private double _returnToMindRemaining;

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
        ApplyFramePacing(applyGraphicsChanges: false);
    }

    protected override void Initialize()
    {
        Window.ClientSizeChanged += (_, _) =>
            _session?.Resize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        // The only source of actual typed characters (shift/caps/layout already
        // resolved) -- everything else in this codebase only ever tracks raw
        // Keys, which is fine for discrete binds but not for free text entry.
        Window.TextInput += (_, e) => _devConsole.HandleTextInput(e.Character);
        base.Initialize();
        if (GameProfile.Profile.Fullscreen)
            ApplyFullscreen(true, persist: false);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        UiTheme.Initialize(GraphicsDevice);
        Primitives2D.Initialize(GraphicsDevice);
        Sprites.Initialize(GraphicsDevice);
        BossAudio.Initialize();
        EnemyCatalog.Warmup();
        UiTheme.PrewarmDungeonText(_spriteBatch);
    }

    protected override void UnloadContent()
    {
        BossAudio.Shutdown();
        base.UnloadContent();
    }

    // ----- Update -----

    protected override void Update(GameTime gameTime)
    {
        Simulation.SetDeltaTime(gameTime.ElapsedGameTime.TotalMilliseconds);
        _uiNavigationClock += Math.Min(.1, gameTime.ElapsedGameTime.TotalSeconds);
        if (State == GameState.TitleScreen)
            _titleScreen.AdvancePresentation(
                gameTime.ElapsedGameTime.TotalSeconds);
        if (State is GameState.Paused or GameState.Results)
            _menus.AdvancePresentation(
                gameTime.ElapsedGameTime.TotalSeconds);
        CollectInput();

        if (_returnToMindRemaining > 0)
        {
            _returnToMindRemaining -= Math.Min(.1, gameTime.ElapsedGameTime.TotalSeconds);
            if (_returnToMindRemaining <= 0)
                FinishReturnToMind();
            base.Update(gameTime);
            return;
        }

        IsMouseVisible = State != GameState.GameRun || _session is null
            || _session.FooterHud.Contains(InputState.MousePosition)
            || _session.InformationSheet.DragInProgress;

        // Opening/closing the console is checked before anything else reads
        // InputState this frame -- while open, none of F11/pause/gameplay
        // should react to keystrokes that are actually meant as typed text
        // (see DevConsole's doc comment on why simulation pauses too).
        if (!_devConsole.IsOpen && Keybinds.Pressed("console_toggle") && _session is not null)
            _devConsole.Open();
        else if (_devConsole.IsOpen && (Keybinds.Pressed("console_toggle") || InputState.KeysPressed.Contains(Keys.Escape)))
            _devConsole.Close();

        var consoleResult = _devConsole.Update(_session, InputState.KeysPressed, gameTime.ElapsedGameTime.TotalSeconds);
        if (consoleResult.Kind == ConsoleActionKind.ExtractRequested
            && _session is not null
            && State == GameState.GameRun)
        {
            BeginReturnToMind(force: true);
        }

        if (_devConsole.IsOpen)
        {
            base.Update(gameTime);
            return;
        }

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
                if (_session?.InformationSheet.DragInProgress == true)
                    _session.InformationSheet.CancelDrag();
                else
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
            else if (State == GameState.Dossier && InputState.ControllerPausePressed)
            {
                // The stash panel is reachable from a run or from The Mind now
                // (see GameState.Dossier's redesign) -- pause needs to resume
                // wherever it was actually opened from, not always GameRun.
                _pauseReturnState = _dossierReturnState;
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

        bool enteredDossier = (State == GameState.GameRun || (State == GameState.Soul && !_soulHub.OverlayOpen))
            && (Keybinds.Pressed("hud_toggle") || InputState.ControllerViewPressed);
        UpdateInputToggles(gameTime);
        UpdateCameraControls(gameTime);

        // Do not let the Escape press that opened pause immediately resume it.
        if (enteredPause || enteredDossier)
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
            case GameState.Reforging:
                UpdateReforging();
                break;
            case GameState.Dossier:
                UpdateDossier();
                break;
            case GameState.Paused:
                UpdatePaused();
                break;
            case GameState.Results:
                UpdateResults(gameTime);
                break;
            case GameState.Soul:
                UpdateSoul(gameTime);
                break;
        }

        base.Update(gameTime);
    }

    private void ToggleFullscreen() => ApplyFullscreen(!_graphics.IsFullScreen, persist: true);

    private void ApplyFramePacing(bool applyGraphicsChanges)
    {
        int maxFrameRate = FramePacing.NormalizeFrameRate(
            GameProfile.Profile.MaxFrameRate);
        bool verticalSync = GameProfile.Profile.VSync;
        bool verticalSyncChanged = verticalSync != _appliedVSync;

        GameProfile.Profile.MaxFrameRate = maxFrameRate;
        IsFixedTimeStep = true;
        TargetElapsedTime = FramePacing.TargetElapsedTime(maxFrameRate);
        _graphics.SynchronizeWithVerticalRetrace = verticalSync;
        _appliedMaxFrameRate = maxFrameRate;
        _appliedVSync = verticalSync;

        if (applyGraphicsChanges && verticalSyncChanged)
        {
            _graphics.ApplyChanges();
            _session?.Resize(
                GraphicsDevice.Viewport.Width,
                GraphicsDevice.Viewport.Height);
        }
    }

    private void ReconcileFramePacing()
    {
        int maxFrameRate = FramePacing.NormalizeFrameRate(
            GameProfile.Profile.MaxFrameRate);
        if (maxFrameRate != _appliedMaxFrameRate
            || GameProfile.Profile.VSync != _appliedVSync)
        {
            ApplyFramePacing(applyGraphicsChanges: true);
        }
    }

    /// <summary>
    /// Applies (and, once GraphicsDevice exists, persists) fullscreen state
    /// directly rather than toggling blindly -- lets both the F11 hotkey and
    /// the pause menu's OPTIONS-tab checkbox drive the same idempotent path,
    /// and lets startup restore a saved preference without needing a spurious
    /// toggle. `persist` is false on the very first call from Initialize
    /// (before GraphicsDevice/GraphicsAdapter are guaranteed ready and before
    /// there's anything new to save back to a profile that already has this
    /// exact value).
    /// </summary>
    private void ApplyFullscreen(bool fullscreen, bool persist)
    {
        if (fullscreen)
        {
            _windowedWidth = Math.Max(640, GraphicsDevice.Viewport.Width);
            _windowedHeight = Math.Max(360, GraphicsDevice.Viewport.Height);
            var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.HardwareModeSwitch = false;
            _graphics.PreferredBackBufferWidth = mode.Width;
            _graphics.PreferredBackBufferHeight = mode.Height;
            _graphics.IsFullScreen = true;
        }
        else
        {
            _graphics.IsFullScreen = false;
            _graphics.PreferredBackBufferWidth = _windowedWidth;
            _graphics.PreferredBackBufferHeight = _windowedHeight;
        }
        _graphics.ApplyChanges();
        _session?.Resize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        if (persist)
        {
            GameProfile.Profile.Fullscreen = fullscreen;
            GameProfile.SaveProfile();
        }
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
        InputState.ScrollWheelDelta = mouseState.ScrollWheelValue - _previousScrollWheelValue;
        _previousScrollWheelValue = mouseState.ScrollWheelValue;

        // Always poll (cheap, and keeps _previousGamePadState's edge-detection
        // baseline correct) but only let a controller actually do anything
        // -- movement, menu navigation, firing -- once the player has opted
        // into Settings' beta controller support. Otherwise every
        // Controller*/Ui* field stays neutral, same as no pad connected.
        var gamePadState = GamePad.GetState(PlayerIndex.One);
        if (GameProfile.Profile.ControllerSupportBeta)
        {
            var left = gamePadState.ThumbSticks.Left;
            var right = gamePadState.ThumbSticks.Right;
            InputState.ControllerMove = left.Length() > .2f ? new Vector2(left.X, -left.Y) : Vector2.Zero;
            InputState.ControllerAim = right.Length() > .25f
                ? Vector2.Normalize(new Vector2(right.X, -right.Y))
                : Vector2.Zero;
            InputState.ControllerFireHeld = gamePadState.Triggers.Right > .35f;
            InputState.ControllerDashPressed = gamePadState.Buttons.A == ButtonState.Pressed
                && _previousGamePadState.Buttons.A == ButtonState.Released;
            InputState.ControllerAutofirePressed = gamePadState.Buttons.X == ButtonState.Pressed
                && _previousGamePadState.Buttons.X == ButtonState.Released;
            InputState.ControllerInteractPressed = gamePadState.Buttons.B == ButtonState.Pressed
                && _previousGamePadState.Buttons.B == ButtonState.Released;
            InputState.ControllerPausePressed = gamePadState.Buttons.Start == ButtonState.Pressed
                && _previousGamePadState.Buttons.Start == ButtonState.Released;
            InputState.ControllerViewPressed = gamePadState.Buttons.Back == ButtonState.Pressed
                && _previousGamePadState.Buttons.Back == ButtonState.Released;
            InputState.ControllerConfirmPressed = gamePadState.Buttons.A == ButtonState.Pressed
                && _previousGamePadState.Buttons.A == ButtonState.Released;
            InputState.ControllerBackPressed = gamePadState.Buttons.B == ButtonState.Pressed
                && _previousGamePadState.Buttons.B == ButtonState.Released;
            InputState.ControllerDpadUpPressed = gamePadState.DPad.Up == ButtonState.Pressed
                && _previousGamePadState.DPad.Up == ButtonState.Released;
            InputState.ControllerDpadDownPressed = gamePadState.DPad.Down == ButtonState.Pressed
                && _previousGamePadState.DPad.Down == ButtonState.Released;
            InputState.ControllerDpadLeftPressed = gamePadState.DPad.Left == ButtonState.Pressed
                && _previousGamePadState.DPad.Left == ButtonState.Released;
            InputState.ControllerDpadRightPressed = gamePadState.DPad.Right == ButtonState.Pressed
                && _previousGamePadState.DPad.Right == ButtonState.Released;
            int uiX = gamePadState.DPad.Left == ButtonState.Pressed ? -1
                : gamePadState.DPad.Right == ButtonState.Pressed ? 1
                : left.X < -.55f ? -1 : left.X > .55f ? 1 : 0;
            int uiY = gamePadState.DPad.Up == ButtonState.Pressed ? -1
                : gamePadState.DPad.Down == ButtonState.Pressed ? 1
                : left.Y > .55f ? -1 : left.Y < -.55f ? 1 : 0;
            // Prefer the stronger stick axis so a diagonal does not double-step.
            if (uiX != 0 && uiY != 0)
            {
                if (Math.Abs(left.X) >= Math.Abs(left.Y)) uiY = 0;
                else uiX = 0;
            }
            var uiDirection = new Point(uiX, uiY);
            bool uiPulse = false;
            if (uiDirection != Point.Zero)
            {
                if (uiDirection != _uiRepeatDirection)
                {
                    uiPulse = true;
                    _uiNextRepeat = _uiNavigationClock + .38;
                }
                else if (_uiNavigationClock >= _uiNextRepeat)
                {
                    uiPulse = true;
                    _uiNextRepeat = _uiNavigationClock + .09;
                }
            }
            _uiRepeatDirection = uiDirection;
            InputState.UiUpPressed = uiPulse && uiY < 0;
            InputState.UiDownPressed = uiPulse && uiY > 0;
            InputState.UiLeftPressed = uiPulse && uiX < 0;
            InputState.UiRightPressed = uiPulse && uiX > 0;
        }
        else
        {
            InputState.ControllerMove = Vector2.Zero;
            InputState.ControllerAim = Vector2.Zero;
            InputState.ControllerFireHeld = false;
            InputState.ControllerDashPressed = false;
            InputState.ControllerAutofirePressed = false;
            InputState.ControllerInteractPressed = false;
            InputState.ControllerPausePressed = false;
            InputState.ControllerViewPressed = false;
            InputState.ControllerConfirmPressed = false;
            InputState.ControllerBackPressed = false;
            InputState.ControllerDpadUpPressed = false;
            InputState.ControllerDpadDownPressed = false;
            InputState.ControllerDpadLeftPressed = false;
            InputState.ControllerDpadRightPressed = false;
            InputState.UiUpPressed = false;
            InputState.UiDownPressed = false;
            InputState.UiLeftPressed = false;
            InputState.UiRightPressed = false;
            _uiRepeatDirection = Point.Zero;
        }
        _previousGamePadState = gamePadState;
    }

    /// <summary>
    /// Beta controller aim scheme: the right stick's direction
    /// (InputState.ControllerAim) places an orbiting reticle a fixed
    /// distance from the player rather than acting as a free-roaming
    /// virtual mouse position -- firing is the right trigger
    /// (InputState.ControllerFireHeld), decoupled from stick deflection.
    /// </summary>
    private const float ControllerAimRadiusPx = 150f;

    private static Vector2 ControllerAimTarget(Vector2 origin, int viewportWidth, int viewportHeight) =>
        origin + InputState.ControllerAim * (ControllerAimRadiusPx * UiTheme.DisplayScale(viewportWidth, viewportHeight));

    /// <summary>Ported from main.py's update_input_toggles().</summary>
    private void UpdateInputToggles(GameTime gameTime)
    {
        if (Keybinds.Pressed("autofire") || InputState.ControllerAutofirePressed)
        {
            GameProfile.Profile.AutoFire = !GameProfile.Profile.AutoFire;
            if (_session is not null)
                _session.State.AutoFire = GameProfile.Profile.AutoFire;
            GameProfile.SaveProfile();
        }
        // Reachable from a run and from The Mind alike -- see GameState.Dossier's
        // redesign doc comment on InformationSheet.DrawDossier. Skipped while a
        // Soul totem menu is already open so the two overlay systems never fight.
        if ((Keybinds.Pressed("hud_toggle") || InputState.ControllerViewPressed)
            && (State == GameState.GameRun || (State == GameState.Soul && !_soulHub.OverlayOpen)))
        {
            _dossierReturnState = State;
            _dossierOpenedAt = gameTime.TotalGameTime.TotalSeconds;
            State = GameState.Dossier;
            _session!.InformationSheet.CancelDrag();
        }
    }

    /// <summary>Ported from main.py's update_camera_controls().</summary>
    private void UpdateCameraControls(GameTime gameTime)
    {
        if ((State != GameState.GameRun && State != GameState.Soul) || _session is null)
            return;
        if (Keybinds.Pressed("zoom_out"))
            _session.Camera.AdjustZoom(-Camera.ZoomStep);
        if (Keybinds.Pressed("zoom_in"))
            _session.Camera.AdjustZoom(Camera.ZoomStep);
        if (InputState.ScrollWheelDelta != 0 && (State != GameState.Soul || !_soulHub.OverlayOpen))
        {
            int notches = Math.Clamp(Math.Abs(InputState.ScrollWheelDelta) / 120, 1, 3);
            _session.Camera.AdjustZoom(Math.Sign(InputState.ScrollWheelDelta) * Camera.ZoomStep * notches);
        }
        if (Keybinds.Pressed("camera_reset"))
        {
            _session.Camera.ResetView();
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
            case TitleAction.EnterMind:
            {
                _resultReport = null;
                var battleground = Battleground.GenerateMind();
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
        if (Keybinds.Pressed("extract") && session.CanExtract)
        {
            BeginReturnToMind();
            return;
        }
        session.State.RunTimeSeconds += Math.Min(gameTime.ElapsedGameTime.TotalMilliseconds, 50) / 1000.0;

        bool quickLootConsumed = session.HandleQuickLootInput(InputState.MousePosition,
            InputState.MouseDown, InputState.MousePressed);

        var footerAction = session.HandleFooterAction(InputState.MousePosition, InputState.MousePressed);
        if (footerAction == FooterAction.OpenLevelUp && session.TryPurchaseLevelUp())
        {
            State = GameState.Leveling;
            return;
        }
        if (footerAction == FooterAction.OpenDossier)
        {
            _dossierReturnState = GameState.GameRun;
            _dossierOpenedAt = gameTime.TotalGameTime.TotalSeconds;
            State = GameState.Dossier;
            return;
        }

        bool moveUp = Keybinds.Held("move_up"), moveDown = Keybinds.Held("move_down");
        bool moveLeft = Keybinds.Held("move_left"), moveRight = Keybinds.Held("move_right");
        session.RecordControllerActivity(
            InputState.ControllerMove != Vector2.Zero
            || InputState.ControllerAim != Vector2.Zero
            || InputState.ControllerFireHeld
            || quickLootConsumed
            || InputState.ControllerDpadUpPressed
            || InputState.ControllerDpadDownPressed
            || InputState.ControllerDpadLeftPressed
            || InputState.ControllerDpadRightPressed
            || InputState.ControllerAutofirePressed
            || InputState.ControllerInteractPressed);
        session.MovePlayer(moveLeft, moveRight, moveUp, moveDown,
            Keybinds.Pressed("dash") || (InputState.ControllerDashPressed && !quickLootConsumed),
            InputState.ControllerMove);

        var mouseScreen = new Vector2(InputState.MousePosition.X, InputState.MousePosition.Y);
        if (InputState.ControllerAim != Vector2.Zero)
            mouseScreen = ControllerAimTarget(session.Camera.Lock, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        InputState.EffectiveAimPosition = new Point((int)mouseScreen.X, (int)mouseScreen.Y);
        session.HandleBulletCreation(mouseScreen, InputState.MouseDown,
            session.InformationSheet.DragInProgress || session.FooterHud.Contains(InputState.MousePosition),
            controllerFiring: InputState.ControllerFireHeld);
        session.UpdateBullets();

        session.HandleEnemyCreation(interactPressed:
            Keybinds.Pressed("interact") || InputState.ControllerInteractPressed);
        session.HandleBossDebugControls(InputState.KeysPressed);
        session.UpdateEnemies();
        if (session.State.GameCompleted && _resultReport is null)
            CaptureRunResult(retained: true, session.LastRunRewardSummary);
        session.UpdateEnemyProjectiles();
        session.HandleDamagingEnemies();
        session.UpdateVisualEffects(gameTime.ElapsedGameTime.TotalSeconds);
        session.UpdateEntrySplash(gameTime.ElapsedGameTime.TotalSeconds);

        session.UpdateDamageTexts();
        session.UpdateExperience();
        session.ExpForPlayer();
        session.UpdateCrateInteraction();
        session.RecoverPlayerHealth();

        bool fatalHit = session.HurtPlayer();
        if (fatalHit)
        {
            CaptureRunResult(retained: false, rewards: null);
            MetaProgression.ClearCarriedItems();
            _menus.BeginResults();
            State = GameState.Results;
            return;
        }
        if (session.State.GameCompleted
            && ResultsRequested(InputState.KeysPressed,
                InputState.ControllerConfirmPressed))
        {
            CaptureRunResult(retained: true, session.LastRunRewardSummary);
            _menus.BeginResults();
            State = GameState.Results;
            return;
        }
    }

    internal static bool ResultsRequested(
        IReadOnlySet<Keys> keysPressed,
        bool controllerConfirm) =>
        controllerConfirm || keysPressed.Contains(Keys.Enter);

    private void UpdateLeveling()
    {
        var outcome = _session!.HandleLevelingInput(InputState.KeysPressed, InputState.MousePosition, InputState.MouseDown);
        if (outcome == LevelUpOutcome.ReturnToGame)
            State = GameState.GameRun;
    }

    private void UpdateReforging()
    {
        var outcome = _session!.HandleReforgeInput(InputState.KeysPressed, InputState.MousePosition, InputState.MousePressed);
        if (outcome == ReforgeOutcome.Closed)
            State = GameState.GameRun;
    }

    private void UpdateDossier()
    {
        var session = _session!;
        session.ScrollDossier(InputState.ScrollWheelDelta);
        bool loadoutHandled = session.HandleLoadoutNavigation(
            InputState.KeysPressed, dossier: true);
        if (InputState.ControllerViewPressed
            || InputState.ControllerBackPressed && !loadoutHandled)
        {
            session.InformationSheet.CancelDrag();
            State = _dossierReturnState;
            return;
        }
        if (loadoutHandled)
            return;

        DossierAction action = session.HandleDossierAction(
            InputState.KeysPressed, InputState.MousePosition, InputState.MousePressed);
        switch (action)
        {
            case DossierAction.Close:
                State = _dossierReturnState;
                return;
            case DossierAction.LevelUp:
                if (session.TryPurchaseLevelUp())
                    State = GameState.Leveling;
                return;
            case DossierAction.Reforge:
                State = GameState.Reforging;
                return;
        }
        // Number-key shortcuts: instantly swap a stash slot with its
        // equipped counterpart (see GameSession.SwapStashSlotWithEquipment).
        // Skipped mid-drag for the same reason HandleDossierAction already
        // ignores its own buttons then -- a held item shouldn't also react
        // to unrelated key presses.
        if (!session.InformationSheet.DragInProgress)
        {
            for (int index = 0; index < InformationSheet.InventorySlotCount; index++)
            {
                if (Keybinds.Pressed($"stash_swap_{index + 1}"))
                    session.SwapStashSlotWithEquipment(index);
            }
        }
        session.HandleDossierDrag(
            InputState.MousePosition, InputState.MouseDown, InputState.MousePressed);
    }

    private void UpdatePaused()
    {
        bool soulContext = _pauseReturnState == GameState.Soul;
        bool settingsOnly = _pauseReturnState == GameState.TitleScreen;
        bool canExtract = _session is not null && !soulContext
            && _session.CanExtract;
        var action = _menus.HandlePause(InputState.KeysPressed, InputState.MousePosition, InputState.MouseDown,
            InputState.MousePressed, canExtract, soulContext, settingsOnly, InputState.ScrollWheelDelta);
        // Menus edits the persisted default; the live run keeps a cached copy.
        if (_session is not null)
        {
            _session.State.AutoFire = GameProfile.Profile.AutoFire;
            _session.Resize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        }
        // The OPTIONS-tab checkbox flips GameProfile.Profile.Fullscreen directly
        // (same as any other GameplayOptions toggle); reconcile the actual
        // window state against it here, same path F11's ToggleFullscreen uses.
        if (GameProfile.Profile.Fullscreen != _graphics.IsFullScreen)
            ApplyFullscreen(GameProfile.Profile.Fullscreen, persist: false);
        ReconcileFramePacing();
        switch (action)
        {
            case MenuAction.Resume:
                State = _pauseReturnState;
                break;
            case MenuAction.Dossier:
                if (_session is not null && _pauseReturnState == GameState.GameRun)
                    State = GameState.Dossier;
                break;
            case MenuAction.Restart:
                if (_session is null) break;
                // A plain restart didn't kill the player, so under "persist unless you
                // die" it carries the loadout forward same as extracting/completing would.
                MetaProgression.SyncCarriedItems(_session.State);
                GameProfile.SaveProfile();
                _session.RestartCurrentRun();
                State = GameState.GameRun;
                break;
            case MenuAction.ReturnToTitle:
                GameProfile.SaveProfile();
                State = GameState.TitleScreen;
                break;
            case MenuAction.Extract:
                if (_session is null) break;
                BeginReturnToMind();
                break;
            case MenuAction.Quit:
                GameProfile.SaveProfile();
                Exit();
                break;
        }
    }

    private void UpdateResults(GameTime gameTime)
    {
        if (_session is not null
            && ResultsWorldContinues(_resultReport?.Outcome))
        {
            // Let the defeated scene breathe behind the banner. No new waves,
            // player attacks, pickups, rewards, or damage are processed here.
            _session.UpdateEnemies();
            _session.UpdateEnemyProjectiles();
            _session.UpdateVisualEffects(gameTime.ElapsedGameTime.TotalSeconds);
            _session.UpdateDamageTexts();
            _session.AdvancePlayerVisuals(gameTime.ElapsedGameTime.TotalSeconds);
        }
        var action = _menus.HandleResults(InputState.KeysPressed, InputState.MousePosition, InputState.MousePressed);
        switch (action)
        {
            case MenuAction.Restart:
                GameProfile.SaveProfile();
                _resultReport = null;
                _session!.RestartCurrentRun();
                State = GameState.GameRun;
                break;
            case MenuAction.ReturnToTitle:
                State = GameState.TitleScreen;
                break;
            case MenuAction.EnterSoul:
            {
                _resultReport = null;
                var battleground = Battleground.GenerateMind();
                _session!.ResetAll(battleground);
                _soulHub.Enter(_session);
                State = GameState.Soul;
                break;
            }
        }
    }

    internal static bool ResultsWorldContinues(string? outcome) =>
        outcome == RunOutcomes.Defeated;

    private void UpdateSoul(GameTime gameTime)
    {
        var session = _session!;
        bool ordinaryMovementAdvancedVisuals = !_soulHub.IsEnteringPortal;
        // Once a portal's pull-in animation is running, SoulHub.UpdatePortalTravel
        // drives the player's world position directly -- ordinary WASD input would
        // just fight that interpolation every frame.
        if (!_soulHub.IsEnteringPortal)
        {
            session.MovePlayer(Keybinds.Held("move_left"), Keybinds.Held("move_right"), Keybinds.Held("move_up"), Keybinds.Held("move_down"),
                Keybinds.Pressed("dash") || InputState.ControllerDashPressed, InputState.ControllerMove);
        }
        var enteredPathKey = _soulHub.HandleInput(session, InputState.KeysPressed, InputState.MousePosition, InputState.MouseDown, InputState.MousePressed);
        if (enteredPathKey is not null)
        {
            if (enteredPathKey == SoulHub.BodyPortalKey)
            {
                session.StartExpedition(CampaignWorld.Body);
            }
            else if (enteredPathKey == SoulHub.CorePortalKey)
            {
                session.StartPathRun();
            }
            else if (enteredPathKey == SoulHub.AphantasiaPortalKey)
            {
                session.StartAphantasia();
                State = GameState.Leveling;
                return;
            }
            else
            {
                session.StartArena(enteredPathKey);
            }
            State = GameState.GameRun;
            return;
        }
        if (!_soulHub.OverlayOpen && !_soulHub.IsEnteringPortal)
        {
            var aim = new Vector2(InputState.MousePosition.X, InputState.MousePosition.Y);
            if (InputState.ControllerAim != Vector2.Zero)
                aim = ControllerAimTarget(session.Camera.Lock, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            session.HandleBulletCreation(aim, InputState.MouseDown, session.InformationSheet.DragInProgress, controllerFiring: InputState.ControllerFireHeld);
            session.UpdateBullets();
        }
        // Always ticks, even mid-overlay/confirm/animation, so the portal
        // pull/fade clock (both keyed off SoulHub's own _seconds) never stalls.
        _soulHub.Update(session, gameTime.ElapsedGameTime.TotalSeconds);
        if (!ordinaryMovementAdvancedVisuals)
            session.AdvancePlayerVisuals(gameTime.ElapsedGameTime.TotalSeconds);
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
            case GameState.Reforging:
                DrawReforging();
                break;
            case GameState.Dossier:
                DrawDossier(gameTime);
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

        if (_devConsole.IsOpen)
        {
            _spriteBatch.Begin();
            _devConsole.Draw(_spriteBatch, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            _spriteBatch.End();
        }

        if (_returnToMindRemaining > 0)
            DrawReturnToMind();

        base.Draw(gameTime);
    }

    private void BeginReturnToMind(bool force = false)
    {
        if (_session is null || _returnToMindRemaining > 0
            || !force && !_session.CanExtract)
            return;
        RunRewardSummary rewards = _session.FinalizeSuccessfulRun(
            RunOutcomes.Extracted, completed: false);
        CaptureRunResult(retained: true, rewards);
        _returnToMindRemaining = 2.0;
    }

    private void FinishReturnToMind()
    {
        if (_session is null)
            return;
        _menus.BeginResults();
        State = GameState.Results;
    }

    private void DrawReturnToMind()
    {
        float progress = (float)Math.Clamp(1.0 - _returnToMindRemaining / 2.0, 0, 1);
        float scale = UiTheme.DisplayScale(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _spriteBatch.Begin();
        Primitives2D.FillRect(_spriteBatch,
            new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
            UiTheme.Void * MathHelper.Lerp(.72f, 1f, progress));
        float pulse = .5f + .5f * MathF.Sin(progress * MathF.PI * 6f);
        UiTheme.DrawText(_spriteBatch, "EXTRACTION COMPLETE", 24 * scale,
            Color.Lerp(UiTheme.Purple, UiTheme.Cream, pulse),
            new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f), "center");
        UiTheme.DrawText(_spriteBatch,
            "YOUR SPOILS HAVE BEEN REMEMBERED  //  DEBRIEF FOLLOWS", 9 * scale,
            UiTheme.Muted, new Vector2(GraphicsDevice.Viewport.Width / 2f,
                GraphicsDevice.Viewport.Height / 2f + 34 * scale), "center");
        _spriteBatch.End();
    }

    /// <summary>
    /// Ported from character.py's per-frame draw calls (interleaved with
    /// their update counterparts in Python; separated here since Update/Draw
    /// are split throughout this port). DrawBackground manages its own
    /// scissor-clipped SpriteBatch.Begin/End pair and must run before this
    /// method's own Begin() -- see its doc comment.
    /// </summary>
    private void DrawGameRun(bool drawHud = true)
    {
        var session = _session!;
        GraphicsDevice.Clear(Color.Black);
        session.DrawBackground(_spriteBatch, GraphicsDevice);

        _spriteBatch.Begin(transformMatrix: session.Camera.WorldTransform);
        // Floor-only boss masks (e.g. Aphantasia's void vortex) paint here,
        // before anything else, so they can never cover the player/boss/
        // projectiles drawn later this frame -- see DrawBossFloorOcclusion's
        // doc comment.
        session.DrawBossFloorOcclusion(_spriteBatch);
        session.DrawPathAmbience(_spriteBatch);
        session.DrawExpeditionSecrets(_spriteBatch);
        session.DrawVisualEffects(_spriteBatch, BitVfxLayer.Ground);
        session.DrawGroundEnemyProjectiles(_spriteBatch);
        // Actors, shots, and raised scenery share a camera-relative painter
        // pass so wall caps/faces can cover anything physically behind them.
        session.DrawDepthSortedCombatWorld(_spriteBatch);
        session.DrawVisualEffects(_spriteBatch, BitVfxLayer.World);
        session.DrawDamageTexts(_spriteBatch);
        session.DrawExperience(_spriteBatch);
        session.DrawVisualEffects(_spriteBatch, BitVfxLayer.Overlay);
        _spriteBatch.End();
        session.DrawAtmosphericLighting(_spriteBatch, GraphicsDevice);
        _spriteBatch.Begin(transformMatrix: session.Camera.WorldTransform);
        session.DrawPathFogOfWar(_spriteBatch);
        session.DrawBossArenaOcclusion(_spriteBatch);
        _spriteBatch.End();

        if (drawHud)
        {
            _spriteBatch.Begin();
            session.DrawCombatOverlays(_spriteBatch, InputState.MousePosition);
            BountyInfo? bounty = session.SelectBountyTarget();
            session.DrawBountyIndicator(_spriteBatch, bounty);
            session.DrawBossPortalIndicator(_spriteBatch);
            session.DrawExpeditionHint(_spriteBatch);
            session.DrawFooter(_spriteBatch, InputState.MousePosition);
            session.DrawAimReticle(_spriteBatch, InputState.MousePosition);
            session.DrawEntrySplash(_spriteBatch);
            _spriteBatch.End();
        }
    }

    private void DrawDossier(GameTime gameTime)
    {
        // Draws whichever scene it was actually opened over -- a run or The
        // Mind -- behind the stash panel, same idea as DrawPaused below.
        if (_dossierReturnState == GameState.Soul)
            DrawSoul();
        else
            DrawGameRun();
        var session = _session!;
        float revealT = (float)Math.Clamp(
            (gameTime.TotalGameTime.TotalSeconds - _dossierOpenedAt) / DossierRevealSeconds, 0, 1);
        _spriteBatch.Begin();
        session.DrawDossier(_spriteBatch, InputState.MousePosition, revealT);
        _spriteBatch.End();
    }

    private void DrawLeveling()
    {
        GraphicsDevice.Clear(UiTheme.Void);
        _spriteBatch.Begin();
        _session!.DrawLevelingScreen(_spriteBatch, InputState.MousePosition, InputState.MouseDown);
        _spriteBatch.End();
    }

    private void DrawReforging()
    {
        GraphicsDevice.Clear(UiTheme.Void);
        _spriteBatch.Begin();
        _session!.DrawReforgeScreen(_spriteBatch, InputState.MousePosition, InputState.MouseDown);
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
        switch (_pauseReturnState)
        {
            case GameState.GameRun:
            case GameState.Dossier:
                DrawGameRun();
                break;
            case GameState.Soul:
                DrawSoul();
                break;
            case GameState.Leveling:
                DrawLeveling();
                break;
            default:
                GraphicsDevice.Clear(Color.Black);
                break;
        }
        _spriteBatch.Begin();
        bool soulContext = _pauseReturnState == GameState.Soul;
        bool settingsOnly = _pauseReturnState == GameState.TitleScreen;
        bool canExtract = _session is not null && !soulContext
            && _session.CanExtract;
        _menus.DrawPause(_spriteBatch, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
            InputState.MousePosition, InputState.MouseDown, canExtract, soulContext, settingsOnly);
        _spriteBatch.End();
    }

    private void DrawResults()
    {
        RunResultReport report = _resultReport
            ?? CaptureRunResult(retained: true, _session!.LastRunRewardSummary);
        if (ResultsWorldContinues(report.Outcome) && _session is not null)
            DrawGameRun(drawHud: false);
        else
            GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin();
        _menus.DrawResults(_spriteBatch, GraphicsDevice.Viewport.Width,
            GraphicsDevice.Viewport.Height, report, InputState.MousePosition,
            InputState.MouseDown);
        _spriteBatch.End();
    }

    private RunResultReport CaptureRunResult(bool retained,
        RunRewardSummary? rewards)
    {
        if (_resultReport is not null)
            return _resultReport;
        GameSession session = _session!;
        string pathKey = session.PathRun?.CurrentSenseKey
            ?? session.CampaignActivitySense
            ?? GamePaths.Active().Key;
        _resultReport = RunResultReport.Capture(session.State, pathKey,
            retained, rewards);
        return _resultReport;
    }

    private void DrawSoul()
    {
        var session = _session!;
        GraphicsDevice.Clear(Color.Black);
        session.DrawBackground(_spriteBatch, GraphicsDevice);
        _spriteBatch.Begin(transformMatrix: session.Camera.WorldTransform);
        session.DrawBullets(_spriteBatch);
        // Stations/portals draw first, then the player on top of them (not
        // behind), then the overlay/confirm/sidebar/fade layer on top of
        // the player -- see SoulHub.DrawWorld/DrawForeground's doc comments.
        _soulHub.DrawWorld(_spriteBatch, session, InputState.MousePosition, InputState.MouseDown);
        session.DrawPlayer(_spriteBatch, _soulHub.PlayerDrawScale);
        session.DrawDamageTexts(_spriteBatch);
        session.DrawRaisedScenery(_spriteBatch, GraphicsDevice);
        _soulHub.DrawMindFog(_spriteBatch, session);
        _spriteBatch.End();

        // Soul panels and prompts stay in unzoomed screen space, exactly like
        // the combat HUD. Only the sanctuary world participates in camera zoom.
        _spriteBatch.Begin();
        _soulHub.DrawForeground(_spriteBatch, session, InputState.MousePosition, InputState.MouseDown);
        session.DrawEntrySplash(_spriteBatch);
        _spriteBatch.End();
    }
}
