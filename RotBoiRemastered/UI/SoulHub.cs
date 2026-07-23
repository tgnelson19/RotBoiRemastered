using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

/// <summary>Safe firing range and tile-based permanent progression sanctuary.</summary>
public sealed class SoulHub
{
    private const float StationOpenRadiusTiles = 1.45f;
    private const float StationCloseRadiusTiles = 1.85f;
    private const float PathPortalInteractRadiusTiles = 1.6f;
    private const float PathPortalConfirmCloseRadiusTiles = 2.1f;
    private const float PortalJunctionOffsetTiles = -28f;
    /// <summary>
    /// Five authored bays in the northern chamber, expressed relative to the
    /// southern holdout spawn. Their shallow arc leaves every title readable
    /// while letting each path visually claim a large slice of the room.
    /// </summary>
    private static readonly (float X, float Y)[] PathPortalOffsetsTiles =
    {
        (-24, -40), (-12, -45), (0, -47), (12, -45), (24, -40),
    };
    /// <summary>Time spent visibly pulling the player from where they confirmed into the portal's center.</summary>
    private const double PortalPullSeconds = 0.9;
    /// <summary>Time spent held at full black after the pull, so the scene swap underneath is never visible.</summary>
    private const double PortalFadeSeconds = 0.45;
    /// <summary>Smallest the player's cosmetic draw scale shrinks to mid-pull, selling a "falling in" look. Render-only (see Player.Draw) -- never touches RunState.PlayerSize, so there's nothing to reset when the next map loads at the default scale of 1.</summary>
    private const float PortalMinPlayerScale = 0.3f;
    private sealed record DummyHit(double Time, double Damage);
    private readonly Queue<DummyHit> _dummyHits = new();
    private readonly Dictionary<string, Rectangle> _targets = new();
    private readonly Dictionary<string, Vector2> _stationWorld = new();
    private readonly Dictionary<string, Vector2> _pathPortalWorld = new();
    private Vector2 _dummyWorld;
    private TrainingDummy _dummy = new(0, 0);
    private string? _overlay;
    private string? _tooltip;
    private double _seconds;
    private double _measurementStart;
    private double _lastHitTime = -99;
    private double _currentDps;
    private double _sessionBest;
    private double _lastRecordSave;
    /// <summary>Portal key awaiting an "ENTER X?" confirmation -- set on F near a portal, cleared by re-pressing F (confirm), walking away, or Escape.</summary>
    private string? _confirmingPortalKey;
    /// <summary>Portal key the player has committed to; drives the pull-in/fade animation until PortalPullSeconds + PortalFadeSeconds elapses.</summary>
    private string? _enteringPortalKey;
    private double _portalAnimationStart;
    private Vector2 _portalTravelStart;
    private Rectangle _ngMinusRect;
    private Rectangle _ngPlusRect;
    private float _playerDrawScale = 1f;
    private float _uiScale = 1f;
    private int Px(float value) => Math.Max(1, (int)MathF.Round(value * _uiScale));
    private double Fs(double value) => value * _uiScale;
    public bool OverlayOpen => _overlay is not null || _confirmingPortalKey is not null;
    /// <summary>True once a portal has been confirmed -- movement and all other Soul interaction are suppressed for the remainder of the animation.</summary>
    public bool IsEnteringPortal => _enteringPortalKey is not null;
    /// <summary>Cosmetic player render scale (see Player.Draw) -- 1 outside the pull-in animation, easing down to PortalMinPlayerScale during it.</summary>
    public float PlayerDrawScale => _playerDrawScale;
    /// <summary>World center of the DPS dummy's hit rect -- exposed so callers (and tests) can place bullets on it without duplicating its layout.</summary>
    public Vector2 DummyWorld => _dummyWorld;
    public double CurrentDps => _currentDps;
    /// <summary>Whether the training dummy is currently carrying the given status effect (e.g. "bleed", "bane") -- lets tests confirm status effects actually land on it instead of only checking the DPS number they produce.</summary>
    public bool DummyHasStatus(string kind) => _dummy.StatusEffects.ContainsKey(kind);
    public void CloseOverlay()
    {
        _overlay = null;
        _confirmingPortalKey = null;
    }

    /// <summary>
    /// Hit rects for the Vault grid, refreshed each frame by DrawVault and fed into
    /// GameSession.HandleCarriedLoadoutDrag -- the drag itself is owned by
    /// InformationSheet (see its VaultDragSource), not this class, so the Vault shares
    /// the exact same drag mechanic/feel as equipment/stash/crate dragging in a real run.
    /// </summary>
    private List<Rectangle> _vaultSlotRects = new();

    public void Enter(GameSession session)
    {
        session.State.EnemySpawningEnabled = false;
        session.State.AutoFire = false;
        session.State.EnemyHolster.Clear();
        session.State.EnemyProjectileHolster.Clear();
        _dummyWorld = session.PlayerWorldCenter + new Vector2(Simulation.TileSize * 8, Simulation.TileSize * -2);
        _dummy = new TrainingDummy(_dummyWorld.X, _dummyWorld.Y);
        _stationWorld.Clear();
        // The entire permanent-progression loop is visible in one glance
        // along the holdout's southern service wall.
        _stationWorld["storage"] = session.PlayerWorldCenter + new Vector2(Simulation.TileSize * -8, Simulation.TileSize * 6);
        _stationWorld["quests"] = session.PlayerWorldCenter + new Vector2(Simulation.TileSize * -4, Simulation.TileSize * 6);
        _stationWorld["skills"] = session.PlayerWorldCenter + new Vector2(0, Simulation.TileSize * 6);
        _stationWorld["wardrobe"] = session.PlayerWorldCenter + new Vector2(Simulation.TileSize * 4, Simulation.TileSize * 6);
        _stationWorld["hard_mode"] = session.PlayerWorldCenter + new Vector2(Simulation.TileSize * 8, Simulation.TileSize * 6);
        _pathPortalWorld.Clear();
        var paths = GamePaths.Paths;
        for (int index = 0; index < paths.Count; index++)
        {
            var authored = PathPortalOffsetsTiles[index];
            var offset = new Vector2(authored.X, authored.Y) * Simulation.TileSize;
            _pathPortalWorld[paths[index].Key] = session.PlayerWorldCenter + offset;
        }
        _dummyHits.Clear();
        _seconds = 0;
        _measurementStart = 0;
        _lastHitTime = -99;
        _currentDps = 0;
        _sessionBest = 0;
        _lastRecordSave = 0;
        _overlay = null;
        _confirmingPortalKey = null;
        _enteringPortalKey = null;
        _playerDrawScale = 1f;
        session.InformationSheet.CancelDrag();
    }

    public void Update(GameSession session, double elapsedSeconds)
    {
        _seconds += Math.Min(.05, elapsedSeconds);
        if (_enteringPortalKey is not null)
            UpdatePortalTravel(session);
        var dummyRect = new Rectangle((int)(_dummyWorld.X - 34), (int)(_dummyWorld.Y - 44), 68, 88);
        foreach (var bullet in session.State.BulletHolster.Where(bullet => !bullet.RemFlag && dummyRect.Intersects(bullet.WorldRect())).ToArray())
        {
            bullet.RemFlag = true;
            double hitDamage = bullet.Damage * StatusEffects.DamageMultiplier(_dummy, bullet);
            _dummy.TakeDamage(hitDamage);
            RecordDummyHit(session, _dummy.DrainUnrecordedDamage(), bullet.IsCritical);
            StatusEffects.RollPlayerHit(_dummy, bullet, session.State.Equipment.Values, session.State.ProjectileCount);
            if (session.State.Equipment.GetValueOrDefault("weapon") is { Definition.EffectIds.Count: > 0 } weapon)
                UniqueEffects.OnPlayerHit(_dummy, bullet, weapon, session.State);
        }
        session.State.BulletHolster.RemoveAll(bullet => bullet.RemFlag);
        // Ticks bleed/poison/bane the same as a real enemy would every frame,
        // so DoT from a hit-and-run playstyle keeps contributing to the DPS
        // meter instead of only ever counting direct impacts.
        StatusEffects.Update(_dummy, elapsedSeconds);
        RecordDummyHit(session, _dummy.DrainUnrecordedDamage(), isCritical: false);
        while (_dummyHits.Count > 0 && _seconds - _dummyHits.Peek().Time > 5)
            _dummyHits.Dequeue();
        double observation = Math.Min(5, Math.Max(.5, _seconds - _measurementStart));
        _currentDps = _dummyHits.Sum(hit => hit.Damage) / observation;
        if (_seconds - _lastHitTime > 2)
            _currentDps = 0;
        _sessionBest = Math.Max(_sessionBest, _currentDps);
        if (_seconds - _lastRecordSave >= 1 && _sessionBest > GameProfile.Profile.BestDummyDps)
        {
            GameProfile.RecordDummyDps(_sessionBest);
            _lastRecordSave = _seconds;
        }
        session.UpdateDamageTexts();
    }

    /// <summary>Folds a landed hit (direct bullet impact or a status-effect tick) into the DPS window, damage text, and quest counter -- shared by both so bleed/bane ticks show up exactly like a direct hit would.</summary>
    private void RecordDummyHit(GameSession session, double damage, bool isCritical)
    {
        if (damage <= 0)
            return;
        if (_seconds - _lastHitTime > 2)
        {
            _dummyHits.Clear();
            _measurementStart = _seconds;
        }
        _dummyHits.Enqueue(new DummyHit(_seconds, damage));
        _lastHitTime = _seconds;
        session.State.DamageTextList.Add(new DamageText(_dummyWorld.X - 20, _dummyWorld.Y - 28,
            isCritical ? UiTheme.Purple : UiTheme.Gold, damage, 40, Simulation.FrameRate));
        GameProfile.IncrementQuest("dummy_damage", Math.Max(1, (long)Math.Round(damage)));
    }

    /// <summary>
    /// Eases the player's world position from where they confirmed toward the
    /// portal's center over PortalPullSeconds -- since Player.Draw always
    /// renders at the camera lock (a fixed screen point), moving WorldX/Y
    /// scrolls the world around the player instead, which reads as the
    /// portal sliding in to meet them. Player.SetPosition takes a top-left
    /// corner, hence the half-size offset from the portal's stored center.
    /// </summary>
    private void UpdatePortalTravel(GameSession session)
    {
        double pullT = Math.Clamp((_seconds - _portalAnimationStart) / PortalPullSeconds, 0, 1);
        float eased = (float)(pullT * pullT);
        // Same eased factor drives the shrink as the position lerp, so the
        // player visibly gets smaller at exactly the rate they close the
        // distance -- reads as falling into the portal rather than just
        // walking up to it.
        _playerDrawScale = MathHelper.Lerp(1f, PortalMinPlayerScale, eased);
        if (!_pathPortalWorld.TryGetValue(_enteringPortalKey!, out var target))
            return;
        var center = Vector2.Lerp(_portalTravelStart, target, eased);
        float half = (float)session.State.PlayerSize / 2f;
        session.Player.SetPosition(center.X - half, center.Y - half);
    }

    /// <summary>True while the carried-loadout sidebar (right side, same style/drag as gameplay) should be visible -- free-roam or the Vault open, not while browsing the other three stations.</summary>
    private bool SidebarShown => _overlay is null || _overlay == "storage";

    /// <summary>
    /// Returns the GamePaths key of a portal whose entry animation just
    /// finished (caller starts a run there), or null. A bare F near a portal
    /// only opens the "ENTER X?" confirmation (see DrawPortalConfirm); a
    /// second F commits, kicking off the pull-in/fade animation that this
    /// method later reports as complete once IsEnteringPortal's clock runs out.
    /// </summary>
    public string? HandleInput(GameSession session, IReadOnlySet<Keys> keysPressed, Point mouse, bool mouseDown, bool mousePressed)
    {
        if (_enteringPortalKey is not null)
        {
            if (_seconds - _portalAnimationStart < PortalPullSeconds + PortalFadeSeconds)
                return null;
            string finishedKey = _enteringPortalKey;
            _enteringPortalKey = null;
            return finishedKey;
        }
        if (_confirmingPortalKey is not null)
        {
            if (!_pathPortalWorld.TryGetValue(_confirmingPortalKey, out var portal)
                || !WithinStationRadius(session.PlayerWorldCenter, portal, PathPortalConfirmCloseRadiusTiles))
            {
                _confirmingPortalKey = null;
                return null;
            }
            bool lowerTier = keysPressed.Contains(Keys.Left) || keysPressed.Contains(Keys.A)
                || (mousePressed && _ngMinusRect.Contains(mouse));
            bool higherTier = keysPressed.Contains(Keys.Right) || keysPressed.Contains(Keys.D)
                || (mousePressed && _ngPlusRect.Contains(mouse));
            if (lowerTier)
                AdjustNewGamePlus(_confirmingPortalKey, -1);
            if (higherTier)
                AdjustNewGamePlus(_confirmingPortalKey, 1);
            if (keysPressed.Contains(Keys.F))
            {
                _enteringPortalKey = _confirmingPortalKey;
                _confirmingPortalKey = null;
                _portalAnimationStart = _seconds;
                _portalTravelStart = session.PlayerWorldCenter;
            }
            return null;
        }
        if (_overlay is not null)
        {
            bool walkedAway = !_stationWorld.TryGetValue(_overlay, out var station)
                || !WithinStationRadius(session.PlayerWorldCenter, station, StationCloseRadiusTiles);
            if (keysPressed.Contains(Keys.F) || walkedAway)
            {
                _overlay = null;
                session.InformationSheet.CancelDrag();
                return null;
            }
        }
        else if (keysPressed.Contains(Keys.F))
        {
            var nearbyPortal = NearbyPathPortal(session);
            if (nearbyPortal is not null)
            {
                _confirmingPortalKey = nearbyPortal;
                return null;
            }
            var nearby = NearbyStation(session);
            if (nearby is not null)
            {
                if (nearby == "hard_mode")
                    ToggleHardMode(session);
                else
                    _overlay = nearby;
            }
        }
        if (SidebarShown)
        {
            // _vaultSlotRects only gets refreshed while the Vault panel is actually drawn
            // (DrawVault, gated on _overlay == "storage") -- pass an empty list otherwise,
            // or a stale rect from a since-closed Vault could still register a pick-up in
            // free-roam, over a spot nothing is being drawn anymore.
            var vaultSlotRects = _overlay == "storage" ? _vaultSlotRects : (IReadOnlyList<Rectangle>)Array.Empty<Rectangle>();
            session.HandleCarriedLoadoutDrag(mouse, mouseDown, mousePressed, vaultSlotRects);
        }
        if (!mousePressed)
            return null;
        foreach (var (key, rect) in _targets.ToArray())
        {
            if (!rect.Contains(mouse)) continue;
            if (key.StartsWith("skill:")) MetaProgression.PurchaseSkill(key[6..]);
            else if (key.StartsWith("cosmetic:"))
            {
                var parts = key.Split(':');
                if (parts.Length == 3 && Cosmetics.Select(parts[1], parts[2]))
                    session.State.ApplyCosmetics();
            }
            break;
        }
        return null;
    }

    public static void ToggleHardMode(GameSession session)
    {
        bool enabled = !GameProfile.Profile.HardModeEnabled;
        GameProfile.Profile.HardModeEnabled = enabled;
        session.State.SetHardMode(enabled);
        GameProfile.SaveProfile();
    }

    public static bool AdjustNewGamePlus(string pathKey, int direction) =>
        NewGamePlus.AdjustSelection(pathKey, direction);

    private string? NearbyStation(GameSession session) => _stationWorld
        .Where(station => WithinStationRadius(session.PlayerWorldCenter, station.Value, StationOpenRadiusTiles))
        .OrderBy(station => Vector2.DistanceSquared(station.Value, session.PlayerWorldCenter))
        .Select(station => station.Key)
        .FirstOrDefault();

    private string? NearbyPathPortal(GameSession session) => _pathPortalWorld
        .Where(portal => WithinStationRadius(session.PlayerWorldCenter, portal.Value, PathPortalInteractRadiusTiles))
        .OrderBy(portal => Vector2.DistanceSquared(portal.Value, session.PlayerWorldCenter))
        .Select(portal => portal.Key)
        .FirstOrDefault();

    public static bool WithinStationRadius(Vector2 player, Vector2 station, float radiusTiles) =>
        Vector2.DistanceSquared(player, station) <= MathF.Pow(Simulation.TileSize * radiusTiles, 2);

    /// <summary>
    /// World-layer draw: the dummy, stations, and path portals -- everything
    /// meant to sit *behind* the player. Call before GameSession.DrawPlayer;
    /// pair with <see cref="DrawForeground"/> (called after) for the overlay/
    /// confirm/sidebar/fade layers that must cover the player instead.
    /// </summary>
    public void DrawWorld(SpriteBatch spriteBatch, GameSession session, Point mouse, bool mouseDown)
    {
        _targets.Clear();
        DrawSoulEnergy(spriteBatch, session);
        var screen = session.Camera.WorldToScreen(_dummyWorld, session.PlayerWorldCenter, Vector2.Zero);
        var body = new Rectangle((int)screen.X - 24, (int)screen.Y - 20, 48, 68);
        Primitives2D.FillRect(spriteBatch, new Rectangle(body.X + 6, body.Y + 8, body.Width, body.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, body, UiTheme.PanelRaised);
        Primitives2D.RectOutline(spriteBatch, body, UiTheme.Red, 4);
        Primitives2D.FillCircle(spriteBatch, new Vector2(screen.X, screen.Y - 38), 18, UiTheme.PanelRaised);
        Primitives2D.CircleOutline(spriteBatch, new Vector2(screen.X, screen.Y - 38), 18, UiTheme.Red, 4);
        Primitives2D.Line(spriteBatch, new Vector2(screen.X, screen.Y + 48), new Vector2(screen.X, screen.Y + 76), UiTheme.Red, 5);

        int sideX = screen.X < session.ScreenWidth * .68f ? (int)screen.X + 58 : (int)screen.X - 268;
        var readout = new Rectangle(sideX, (int)screen.Y - 68, 210, 128);
        UiTheme.DrawPanel(spriteBatch, readout, UiTheme.Panel, UiTheme.Red, shadow: 5);
        UiTheme.DrawText(spriteBatch, "THE EFFIGY REMEMBERS", 10, UiTheme.Muted, new Vector2(readout.X + 12, readout.Y + 10));
        UiTheme.DrawText(spriteBatch, $"{_currentDps:0}", 34, UiTheme.Text, new Vector2(readout.X + 12, readout.Y + 31));
        UiTheme.DrawText(spriteBatch, "DAMAGE PER SECOND", 9, UiTheme.Red, new Vector2(readout.X + 14, readout.Y + 74));
        UiTheme.DrawText(spriteBatch, $"SESSION {_sessionBest:0}  //  RECORD {GameProfile.Profile.BestDummyDps:0}", 8, UiTheme.Cream,
            new Vector2(readout.X + 14, readout.Y + 98));
        DrawStations(spriteBatch, session);
        DrawPathPortals(spriteBatch, session);
    }

    /// <summary>
    /// Animated architecture layered over the Soul's neutral baked floor:
    /// raised-looking tunnel ribbons, a five-way luminous junction, path
    /// trails, floating motes, and portal bleed. All noise is deterministic
    /// and clock-driven, so the room feels alive without storing hundreds of
    /// particle objects or affecting simulation.
    /// </summary>
    private void DrawSoulEnergy(SpriteBatch spriteBatch, GameSession session)
    {
        float t = (float)_seconds;
        Vector2 spawn = session.Battleground.SpawnPosition + new Vector2(Simulation.TileSize / 2f);
        Vector2 tunnelStart = spawn + new Vector2(0, Simulation.TileSize * -7f);
        Vector2 junction = spawn + new Vector2(0, Simulation.TileSize * PortalJunctionOffsetTiles);
        var pathColors = GamePaths.Paths.Select(path => path.Accent).ToArray();
        float awakening = TunnelAwakening(session.PlayerWorldCenter.Y, tunnelStart.Y, junction.Y);
        float ambientAwakening = .12f + awakening * .88f;

        // A quiet foundation makes the moving light read as a constructed
        // conduit rather than loose particles sprinkled over the floor.
        foreach (float side in new[] { -4.15f, 4.15f })
        {
            Vector2 railStart = WorldToScreen(tunnelStart + new Vector2(side * Simulation.TileSize, 0), session);
            Vector2 railEnd = WorldToScreen(junction + new Vector2(side * Simulation.TileSize, 0), session);
            Primitives2D.Line(spriteBatch, railStart + new Vector2(0, 8), railEnd + new Vector2(0, 8), UiTheme.Shadow * .8f, 14);
            Primitives2D.Line(spriteBatch, railStart, railEnd, new Color(78, 64, 101) * (.22f + ambientAwakening * .58f), 5);
            Primitives2D.Line(spriteBatch, railStart - new Vector2(0, 3), railEnd - new Vector2(0, 3),
                new Color(211, 192, 231) * (.08f + ambientAwakening * .37f), 2);
        }

        // Five independently breathing ribbons gradually braid together as
        // the player approaches the portal room.
        const int tunnelSegments = 34;
        for (int ribbon = 0; ribbon < pathColors.Length; ribbon++)
        {
            Vector2? previous = null;
            float lane = (ribbon - 2) * .72f;
            for (int segment = 0; segment <= tunnelSegments; segment++)
            {
                float amount = segment / (float)tunnelSegments;
                float localAwakening = Math.Clamp((awakening + .16f - amount) / .16f, 0, 1);
                // Leave a dim one-pixel circuit behind the activation front,
                // then build shadow, body, and highlight as the player walks.
                float segmentLight = .08f + localAwakening * .92f;
                float wave = MathF.Sin(t * (1.3f + ribbon * .08f) - amount * 9f + ribbon * 1.4f);
                float braid = lane * (1f - amount * .76f) + wave * (.18f + amount * .46f);
                Vector2 world = Vector2.Lerp(tunnelStart, junction, amount)
                    + new Vector2(braid * Simulation.TileSize, 0);
                Vector2 screen = WorldToScreen(world, session);
                if (previous.HasValue)
                {
                    float breath = .58f + .42f * MathF.Sin(t * 2f - amount * 5f + ribbon);
                    Primitives2D.Line(spriteBatch, previous.Value + new Vector2(0, 5), screen + new Vector2(0, 5),
                        UiTheme.Shadow * (.2f + segmentLight * .45f), 7);
                    Primitives2D.Line(spriteBatch, previous.Value, screen,
                        pathColors[ribbon] * segmentLight * (.48f + breath * .34f), localAwakening > .12f ? 4 : 1);
                    Primitives2D.Line(spriteBatch, previous.Value - new Vector2(0, 2), screen - new Vector2(0, 2),
                        Color.Lerp(pathColors[ribbon], Color.White, .68f) * localAwakening * (.3f + breath * .36f), 1);
                }
                previous = screen;
            }
        }

        // Floating tunnel motes use a displaced shadow and height bob so they
        // read above the floor plane in the top-down camera.
        for (int mote = 0; mote < 28; mote++)
        {
            float travel = ((mote * .137f + t * (.032f + mote % 3 * .008f)) % 1f + 1f) % 1f;
            if (travel > awakening + .12f)
                continue;
            float lateral = MathF.Sin(mote * 2.17f + t * .7f) * Simulation.TileSize * 3.4f;
            float height = 8f + 13f * (.5f + .5f * MathF.Sin(t * 1.8f + mote));
            Vector2 world = Vector2.Lerp(tunnelStart, junction, travel) + new Vector2(lateral, 0);
            Vector2 screen = WorldToScreen(world, session);
            Color color = pathColors[mote % pathColors.Length];
            Primitives2D.FillCircle(spriteBatch, screen + new Vector2(3, 5), 4, UiTheme.Shadow * .6f);
            Primitives2D.FillCircle(spriteBatch, screen - new Vector2(0, height), 2.5f + mote % 3, color * .8f);
            DrawPixelReflection(spriteBatch, screen + new Vector2(0, 5), color, 8 + mote % 3 * 2, .26f);
        }

        // The tunnel opens into one luminous knot, then separates into five
        // authored colored paths. A quadratic bend keeps the routes organic.
        Vector2 junctionScreen = WorldToScreen(junction, session);
        for (int ring = 0; ring < 4; ring++)
        {
            float radius = Simulation.TileSize * (.42f + ring * .25f + .04f * MathF.Sin(t * 2f + ring));
            Primitives2D.CircleOutline(spriteBatch, junctionScreen, radius,
                Color.Lerp(new Color(137, 103, 178), Color.White, ring * .14f) * (.52f - ring * .07f), 2);
        }

        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            var path = GamePaths.Paths[index];
            if (!_pathPortalWorld.TryGetValue(path.Key, out var portal))
                continue;
            Vector2 control = new(
                MathHelper.Lerp(junction.X, portal.X, .48f),
                junction.Y - Simulation.TileSize * (5.5f + Math.Abs(index - 2) * 1.25f));
            DrawPortalTrail(spriteBatch, session, junction, control, portal, path.Accent, index, t);
            int corruptionLevel = NewGamePlus.SelectedLevel(path.Key);
            DrawPortalBleed(spriteBatch, session, portal, path.Accent, index, t, corruptionLevel);
            DrawCompletionMonument(spriteBatch, session, portal, path.Accent, path.Key, index, t);
        }
        DrawInterPortalTransfer(spriteBatch, session, t);
    }

    private static Vector2 WorldToScreen(Vector2 world, GameSession session) =>
        session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);

    public static float TunnelAwakening(float playerWorldY, float tunnelStartWorldY, float junctionWorldY)
    {
        if (Math.Abs(tunnelStartWorldY - junctionWorldY) < .001f)
            return 1;
        return Math.Clamp((tunnelStartWorldY - playerWorldY) / (tunnelStartWorldY - junctionWorldY), 0, 1);
    }

    private static void DrawPixelReflection(SpriteBatch spriteBatch, Vector2 floor, Color color, int width, float alpha)
    {
        int evenWidth = Math.Max(2, width / 2 * 2);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)floor.X - evenWidth / 2, (int)floor.Y + 3, evenWidth, 2), color * alpha);
        if (evenWidth >= 8)
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)floor.X - evenWidth / 4, (int)floor.Y + 7, evenWidth / 2, 1), color * alpha * .55f);
    }

    private static Vector2 Quadratic(Vector2 start, Vector2 control, Vector2 end, float amount)
    {
        float inverse = 1f - amount;
        return inverse * inverse * start + 2f * inverse * amount * control + amount * amount * end;
    }

    private static void DrawPortalTrail(SpriteBatch spriteBatch, GameSession session, Vector2 start,
        Vector2 control, Vector2 end, Color color, int pathIndex, float time)
    {
        const int segments = 28;
        Vector2 previous = WorldToScreen(start, session);
        for (int segment = 1; segment <= segments; segment++)
        {
            float amount = segment / (float)segments;
            Vector2 screen = WorldToScreen(Quadratic(start, control, end, amount), session);
            float pulse = .5f + .5f * MathF.Sin(time * 2.4f - amount * 12f + pathIndex);
            Primitives2D.Line(spriteBatch, previous + new Vector2(0, 5), screen + new Vector2(0, 5), UiTheme.Shadow * .6f, 11);
            Primitives2D.Line(spriteBatch, previous, screen, color * (.28f + pulse * .48f), 5);
            if ((segment + pathIndex * 2) % 7 == (int)(time * 5f) % 7)
            {
                Primitives2D.FillCircle(spriteBatch, screen - new Vector2(0, 5 + pulse * 8), 3.5f, Color.Lerp(color, Color.White, .55f));
                DrawPixelReflection(spriteBatch, screen + new Vector2(0, 4), color, 10, .22f);
            }
            previous = screen;
        }
    }

    /// <summary>
    /// Each portal stains the neutral chamber with a distinct silhouette:
    /// echo rings, weight-blocks, sight rays, chemical bubbles, or Phantasia
    /// petals. This is the key environmental storytelling beat—the paths are
    /// not doors placed in The Soul; they are actively rewriting it.
    /// </summary>
    private static void DrawPortalBleed(SpriteBatch spriteBatch, GameSession session, Vector2 world,
        Color color, int pathIndex, float time, int corruptionLevel)
    {
        Vector2 center = WorldToScreen(world, session);
        float corruption = PortalCorruptionScale(corruptionLevel);
        float baseRadius = Simulation.TileSize * (2.7f * corruption + .12f * MathF.Sin(time * 1.5f + pathIndex));
        Primitives2D.FillCircle(spriteBatch, center, baseRadius, color * (.055f + corruptionLevel * .008f));
        Primitives2D.CircleOutline(spriteBatch, center, baseRadius, color * (.3f + corruptionLevel * .035f), 2);

        int tendrilCount = 10 + corruptionLevel * 2;
        for (int tendril = 0; tendril < tendrilCount; tendril++)
        {
            float angle = tendril * MathHelper.TwoPi / tendrilCount + MathF.Sin(time * .35f + tendril) * .12f;
            float length = baseRadius * (.72f + (tendril % 3) * .17f);
            Vector2 inner = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * baseRadius * .43f;
            Vector2 outer = center + new Vector2(MathF.Cos(angle + .16f), MathF.Sin(angle + .16f)) * length;
            Primitives2D.Line(spriteBatch, inner + new Vector2(0, 4), outer + new Vector2(0, 4), UiTheme.Shadow * .55f, 6);
            Primitives2D.Line(spriteBatch, inner, outer, color * .48f, 2);
        }

        // NG+ adds square corruption motes instead of smooth bloom. Higher
        // tiers therefore read as denser and more unstable while remaining
        // faithful to the game's low-resolution primitive vocabulary.
        for (int mote = 0; mote < corruptionLevel * 3; mote++)
        {
            float angle = mote * 2.07f + time * (mote % 2 == 0 ? .22f : -.17f);
            float distance = baseRadius * (.58f + (mote % 5) * .1f);
            Vector2 at = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            int size = 3 + mote % 3 * 2;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - size / 2, (int)at.Y - size / 2, size, size),
                Color.Lerp(color, Color.White, .35f) * .72f);
            DrawPixelReflection(spriteBatch, at + new Vector2(0, 4), color, size * 2, .17f);
        }

        switch (pathIndex)
        {
            case 0: // Sound: expanding echo rings.
                for (int ring = 0; ring < 3; ring++)
                {
                    float radius = Simulation.TileSize * (.72f + ring * .55f)
                        + (time * 22f + ring * 19f) % (Simulation.TileSize * .48f);
                    Primitives2D.CircleOutline(spriteBatch, center, radius, color * (.52f - ring * .1f), 2);
                }
                break;
            case 1: // Touch: dense offset blocks suggest mass and pressure.
                for (int block = 0; block < 7; block++)
                {
                    float angle = block * MathHelper.TwoPi / 7f;
                    Vector2 at = center + new Vector2(MathF.Cos(angle) * baseRadius * .66f, MathF.Sin(angle) * baseRadius * .48f);
                    int size = 10 + block % 3 * 5;
                    var rect = new Rectangle((int)at.X - size / 2, (int)at.Y - size / 2, size, size);
                    Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 3, rect.Y + 5, rect.Width, rect.Height), UiTheme.Shadow * .65f);
                    Primitives2D.RectOutline(spriteBatch, rect, color * .75f, 3);
                }
                break;
            case 2: // Sight: long clean rays and a blinking central iris.
                for (int ray = 0; ray < 12; ray++)
                {
                    float angle = ray * MathHelper.TwoPi / 12f + time * .08f;
                    Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                    Primitives2D.Line(spriteBatch, center + direction * baseRadius * .58f,
                        center + direction * baseRadius * (1.05f + ray % 2 * .22f), color * .5f, ray % 2 + 1);
                }
                Primitives2D.FillCircle(spriteBatch, center, 13 + 4 * MathF.Sin(time * 2.3f), Color.Lerp(color, Color.White, .5f) * .72f);
                break;
            case 3: // Chemesthesis: buoyant contaminated bubbles.
                for (int bubble = 0; bubble < 9; bubble++)
                {
                    float angle = bubble * 2.31f + time * (bubble % 2 == 0 ? .16f : -.12f);
                    float radius = baseRadius * (.45f + bubble % 4 * .13f);
                    Vector2 at = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    Primitives2D.CircleOutline(spriteBatch, at - new Vector2(0, 4 + bubble % 3 * 3), 6 + bubble % 4 * 2, color * .68f, 2);
                }
                break;
            case 4: // Phantasia: counter-rotating petal ellipses made from arcs.
                for (int petal = 0; petal < 8; petal++)
                {
                    float angle = petal * MathHelper.TwoPi / 8f + time * (petal % 2 == 0 ? .18f : -.13f);
                    float radius = baseRadius * .73f;
                    Vector2 at = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    var rect = new Rectangle((int)at.X - 15, (int)at.Y - 9, 30, 18);
                    Primitives2D.Arc(spriteBatch, rect, angle, angle + MathF.PI * 1.35f, color * .8f, 2);
                }
                break;
        }
    }

    public static float PortalCorruptionScale(int newGamePlusLevel) =>
        1f + NewGamePlus.ClampLevel(newGamePlusLevel) * .085f;

    /// <summary>
    /// A cleared path grows a permanent, block-built reliquary beneath its
    /// portal. Repeat clears raise the central shard; unlocked NG+ tiers add
    /// gold memory pips along the plinth. Short floor cracks make the trophy
    /// feel grown from The Soul rather than placed on top of it.
    /// </summary>
    private static void DrawCompletionMonument(SpriteBatch spriteBatch, GameSession session, Vector2 portal,
        Color color, string pathKey, int pathIndex, float time)
    {
        int mastery = GameProfile.Profile.PathMastery.GetValueOrDefault(pathKey);
        if (mastery <= 0)
            return;

        Vector2 center = WorldToScreen(portal + new Vector2(0, Simulation.TileSize * 2.15f), session);
        int height = 24 + Math.Min(6, mastery) * 5;
        var shadow = new Rectangle((int)center.X - 28 + 5, (int)center.Y - height + 6, 56, height);
        var baseRect = new Rectangle((int)center.X - 28, (int)center.Y - 12, 56, 12);
        var pillar = new Rectangle((int)center.X - 8, (int)center.Y - height, 16, height - 8);
        Primitives2D.FillRect(spriteBatch, shadow, UiTheme.Shadow * .72f);
        Primitives2D.FillRect(spriteBatch, baseRect, new Color(33, 29, 43));
        Primitives2D.RectOutline(spriteBatch, baseRect, color * .78f, 2);
        Primitives2D.FillRect(spriteBatch, pillar, new Color(46, 39, 58));
        Primitives2D.RectOutline(spriteBatch, pillar, color * .82f, 2);

        // A path-specific pixel crown keeps the monument recognizable even
        // when its portal is off-screen above it.
        int crownY = pillar.Top - 7;
        switch (pathIndex)
        {
            case 0:
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X - 15, crownY, 30, 3), color);
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X - 10, crownY - 5, 20, 3), color * .8f);
                break;
            case 1:
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X - 13, crownY - 4, 10, 10), color);
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X + 3, crownY - 4, 10, 10), color);
                break;
            case 2:
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X - 16, crownY, 32, 3), color);
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X - 3, crownY - 8, 6, 16), UiTheme.Cream * .8f);
                break;
            case 3:
                for (int bubble = 0; bubble < 3; bubble++)
                    Primitives2D.FillRect(spriteBatch,
                        new Rectangle((int)center.X - 12 + bubble * 9, crownY - bubble % 2 * 5, 6, 6), color);
                break;
            default:
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X - 13, crownY, 26, 4), color);
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)center.X - 3, crownY - 10, 6, 24), UiTheme.Cream * .72f);
                break;
        }

        int pips = NewGamePlus.UnlockedLevel(pathKey);
        for (int pip = 0; pip < pips; pip++)
        {
            int x = baseRect.Center.X - (pips * 6 - 2) / 2 + pip * 6;
            Primitives2D.FillRect(spriteBatch, new Rectangle(x, baseRect.Y + 4, 4, 4), UiTheme.Gold);
        }

        int cracks = Math.Min(8, 3 + mastery);
        for (int crack = 0; crack < cracks; crack++)
        {
            float angle = crack * MathHelper.TwoPi / cracks + pathIndex * .21f;
            float length = 30 + (crack % 3) * 9;
            Vector2 start = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .45f) * 24;
            Vector2 end = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .45f) * length;
            Primitives2D.Line(spriteBatch, start, end, color * (.28f + .08f * MathF.Sin(time + crack)), 2);
        }
        DrawPixelReflection(spriteBatch, new Vector2(center.X, baseRect.Bottom), color, 38, .2f);
    }

    /// <summary>
    /// On a slow deterministic cadence, one portal sheds a packet of square
    /// pixels and another consumes it. The exchange hints that every path is
    /// part of one system without adding simulation objects or smooth VFX.
    /// </summary>
    private void DrawInterPortalTransfer(SpriteBatch spriteBatch, GameSession session, float time)
    {
        const float cycleSeconds = 7.2f;
        int cycle = (int)MathF.Floor(time / cycleSeconds);
        float phase = time - cycle * cycleSeconds;
        if (phase < 1.1f || phase > 3.8f)
            return;

        int sourceIndex = cycle % GamePaths.Paths.Count;
        int targetIndex = (sourceIndex + 2 + cycle % 3) % GamePaths.Paths.Count;
        var sourcePath = GamePaths.Paths[sourceIndex];
        var targetPath = GamePaths.Paths[targetIndex];
        if (!_pathPortalWorld.TryGetValue(sourcePath.Key, out var source)
            || !_pathPortalWorld.TryGetValue(targetPath.Key, out var target))
            return;

        Vector2 midpoint = (source + target) * .5f
            + new Vector2(0, -Simulation.TileSize * (4f + Math.Abs(targetIndex - sourceIndex) * .35f));
        float transferTime = phase - 1.1f;
        for (int particle = 0; particle < 8; particle++)
        {
            float amount = Math.Clamp((transferTime - particle * .11f) / 1.75f, 0, 1);
            if (amount <= 0 || amount >= 1)
                continue;
            // Twelve discrete positions make the packet visibly tick across
            // the room like an old-school projectile rather than glide.
            amount = MathF.Floor(amount * 12f) / 12f;
            Vector2 screen = WorldToScreen(Quadratic(source, midpoint, target, amount), session);
            Color color = Color.Lerp(sourcePath.Accent, targetPath.Accent, amount);
            int size = 4 + particle % 3 * 2;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)screen.X - size / 2, (int)screen.Y - size / 2, size, size),
                Color.Lerp(color, Color.White, .35f) * .88f);
            DrawPixelReflection(spriteBatch, screen + new Vector2(0, 8), color, size * 2, .2f);
        }

        if (transferTime > 1.55f)
        {
            Vector2 targetScreen = WorldToScreen(target, session);
            float absorb = Math.Clamp((transferTime - 1.55f) / .7f, 0, 1);
            int extent = (int)MathF.Round(28 * (1f - absorb));
            Color color = Color.Lerp(sourcePath.Accent, targetPath.Accent, absorb) * (1f - absorb);
            if (extent > 0)
                Primitives2D.RectOutline(spriteBatch,
                    new Rectangle((int)targetScreen.X - extent, (int)targetScreen.Y - extent, extent * 2, extent * 2),
                    color, 2);
        }
    }

    /// <summary>
    /// UI-layer draw: overlay panels, the portal confirm modal, the carried-
    /// loadout sidebar, and the portal fade -- everything meant to sit *on
    /// top of* the player. Call after GameSession.DrawPlayer; see
    /// <see cref="DrawWorld"/>.
    /// </summary>
    public void DrawForeground(SpriteBatch spriteBatch, GameSession session, Point mouse, bool mouseDown)
    {
        _uiScale = UiTheme.DisplayScale(session.ScreenWidth, session.ScreenHeight);
        UiTheme.DrawText(spriteBatch, "THE SOUL", Fs(27), UiTheme.Text, new Vector2(Px(22), Px(18)));
        UiTheme.DrawText(spriteBatch,
            $"SAFE GROUND  //  HARD MODE {(GameProfile.Profile.HardModeEnabled ? "ON" : "OFF")}  //  WALK TO A STATION OR PATH PORTAL  //  F INTERACT  //  ESC OPTIONS",
            Fs(9), UiTheme.Muted, new Vector2(Px(24), Px(54)));
        DrawNearbyPrompt(spriteBatch, session);
        if (_overlay is not null) DrawOverlay(spriteBatch, session, mouse);
        if (_confirmingPortalKey is not null) DrawPortalConfirm(spriteBatch, session, mouse, mouseDown);
        // Drawn last so the dragged-item icon (part of this call, see
        // InformationSheet.DrawCarriedLoadout) always renders on top, wherever the
        // cursor currently is -- including over the Vault panel drawn just above.
        if (SidebarShown) session.DrawCarriedLoadout(spriteBatch, mouse);
        // Absolute last: once committed, the fade must cover everything above
        // (sidebar included) so the ResetAll scene swap underneath is never visible.
        if (_enteringPortalKey is not null) DrawPortalFade(spriteBatch, session);
    }

    private void DrawStations(SpriteBatch spriteBatch, GameSession session)
    {
        var labels = new Dictionary<string, (string Label, Color Accent)>
        {
            ["storage"] = ("VAULT", UiTheme.Gold),
            ["quests"] = ("QUEST ALTAR", UiTheme.Green),
            ["skills"] = ("SOUL GRID", UiTheme.Purple),
            ["wardrobe"] = ("WARDROBE", UiTheme.Blue),
            ["hard_mode"] = (GameProfile.Profile.HardModeEnabled ? "HARD MODE ON" : "HARD MODE OFF",
                GameProfile.Profile.HardModeEnabled ? UiTheme.Red : UiTheme.Muted),
        };
        foreach (var (key, world) in _stationWorld)
        {
            var position = session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
            var (label, accent) = labels[key];
            int width = key == "hard_mode" ? 88 : 56;
            var baseRect = new Rectangle((int)position.X - width / 2, (int)position.Y - 24, width, 48);
            if (key == "hard_mode" && GameProfile.Profile.HardModeEnabled)
            {
                var glow = baseRect;
                glow.Inflate(5, 5);
                Primitives2D.RectOutline(spriteBatch, glow,
                    Color.Lerp(UiTheme.Red, UiTheme.Gold, .35f + .25f * MathF.Sin((float)_seconds * 4f)), 3);
            }
            Primitives2D.FillRect(spriteBatch, new Rectangle(baseRect.X + 5, baseRect.Y + 6, baseRect.Width, baseRect.Height), UiTheme.Shadow);
            Primitives2D.FillRect(spriteBatch, baseRect, UiTheme.PanelRaised);
            Primitives2D.RectOutline(spriteBatch, baseRect, accent, 3);
            Primitives2D.FillCircle(spriteBatch, new Vector2(position.X, position.Y), 9, accent);
            UiTheme.DrawText(spriteBatch, label, 8, accent, new Vector2(position.X, baseRect.Bottom + 7), "midtop");
        }
    }

    private void DrawNearbyPrompt(SpriteBatch spriteBatch, GameSession session)
    {
        var labels = new Dictionary<string, (string Label, Color Accent)>
        {
            ["storage"] = ("VAULT", UiTheme.Gold), ["quests"] = ("QUEST ALTAR", UiTheme.Green),
            ["skills"] = ("SOUL GRID", UiTheme.Purple), ["wardrobe"] = ("WARDROBE", UiTheme.Blue),
            ["hard_mode"] = (GameProfile.Profile.HardModeEnabled ? "DISABLE HARD MODE" : "ENABLE HARD MODE",
                GameProfile.Profile.HardModeEnabled ? UiTheme.Red : UiTheme.Gold),
        };
        var nearby = NearbyStation(session);
        if (nearby is not null)
            UiTheme.DrawText(spriteBatch, $"F  //  OPEN {labels[nearby].Label}", Fs(13), labels[nearby].Accent,
                new Vector2(session.ScreenWidth / 2f, session.ScreenHeight - Px(42)), "center");
    }

    /// <summary>
    /// One equally-spaced swirl portal per GamePaths entry, replacing the old
    /// title-screen path selector -- walking up and pressing F (see
    /// NearbyPathPortal/HandleInput) leaves the Soul and starts a run on that
    /// path directly. Visual mirrors GameSession.DrawBossPortal's swirl (same
    /// pulsing fill + rotating arcs) so an in-run boss portal and a Soul path
    /// portal read as the same language, just tinted per path.
    /// </summary>
    private void DrawPathPortals(SpriteBatch spriteBatch, GameSession session)
    {
        float t = (float)_seconds;
        string? nearbyPortal = NearbyPathPortal(session);
        foreach (var path in GamePaths.Paths)
        {
            if (!_pathPortalWorld.TryGetValue(path.Key, out var world)) continue;
            var screen = session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
            int selectedNg = NewGamePlus.SelectedLevel(path.Key);
            float corruption = PortalCorruptionScale(selectedNg);
            float radius = Simulation.TileSize * (1.05f + (corruption - 1f) * .16f);
            // Committing spins the destination portal up hard during the pull
            // (see UpdatePortalTravel) so it visibly reels the player in,
            // instead of sitting there identical to every other portal.
            bool committing = path.Key == _enteringPortalKey;
            float pullT = committing ? (float)Math.Clamp((_seconds - _portalAnimationStart) / PortalPullSeconds, 0, 1) : 0f;
            float intensity = 1f + selectedNg * .045f + pullT * 2.2f;
            float pulse = 1f + .06f * intensity * MathF.Sin(t * 2.2f * intensity + path.Key.GetHashCode());
            Primitives2D.FillCircle(spriteBatch, screen, radius * .78f * pulse, UiTheme.Ink);
            Primitives2D.CircleOutline(spriteBatch, screen, radius, path.Accent, 3);
            for (int index = 0; index < 3; index++)
            {
                float speed = (1.4f + index * .55f) * intensity;
                float phase = t * speed + index * (MathF.PI * 2f / 3f);
                float ringRadius = radius * (.55f + index * .18f) * (1f - pullT * .35f);
                var arcRect = new Rectangle((int)(screen.X - ringRadius), (int)(screen.Y - ringRadius), (int)(ringRadius * 2), (int)(ringRadius * 2));
                Primitives2D.Arc(spriteBatch, arcRect, phase, phase + MathF.PI * .62f, path.Accent, 2);
            }
            UiTheme.DrawText(spriteBatch, path.Title, 10, path.Accent, new Vector2(screen.X, screen.Y + radius + 8), "midtop");
            int unlockedNg = NewGamePlus.UnlockedLevel(path.Key);
            string ngLabel = unlockedNg == 0
                ? "NORMAL  //  COMPLETE TO UNLOCK NG+"
                : selectedNg == 0 ? $"NORMAL  //  NG+{unlockedNg} UNLOCKED" : $"NG+{selectedNg}  //  MAX {unlockedNg}";
            UiTheme.DrawText(spriteBatch, ngLabel,
                8, selectedNg == 0 ? UiTheme.Muted : UiTheme.Gold,
                new Vector2(screen.X, screen.Y + radius + 25), "midtop");
            // Suppressed while confirming/entering that same portal -- the center
            // confirmation panel (DrawPortalConfirm) already explains the prompt.
            if (path.Key == nearbyPortal && path.Key != _confirmingPortalKey && _enteringPortalKey is null)
                UiTheme.DrawText(spriteBatch, "F  //  ENTER", 9, UiTheme.Cream, new Vector2(screen.X, screen.Y + radius + 42), "midtop");
        }
    }

    /// <summary>Centered "ENTER {PATH}?" modal shown while _confirmingPortalKey is set -- F commits, walking away or Escape cancels (Escape via OverlayOpen/CloseOverlay in Core/RotBoiGame.cs).</summary>
    private void DrawPortalConfirm(SpriteBatch spriteBatch, GameSession session, Point mouse, bool mouseDown)
    {
        var path = GamePaths.PathsByKey[_confirmingPortalKey!];
        int selected = NewGamePlus.SelectedLevel(path.Key);
        int unlocked = NewGamePlus.UnlockedLevel(path.Key);
        int width = (int)(session.ScreenWidth * .42f), height = (int)(session.ScreenHeight * .29f);
        var rect = new Rectangle(session.ScreenWidth / 2 - width / 2, (int)(session.ScreenHeight * .28f), width, height);
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, session.ScreenWidth, session.ScreenHeight), UiTheme.Void * .55f);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, path.Accent, shadow: 10);
        UiTheme.DrawText(spriteBatch, $"ENTER {path.Title}?", Fs(22), path.Accent, new Vector2(rect.Center.X, rect.Y + Px(26)), "center");
        UiTheme.DrawText(spriteBatch, path.Subtitle, Fs(11), UiTheme.Cream, new Vector2(rect.Center.X, rect.Y + Px(62)), "center");

        _ngMinusRect = new Rectangle(rect.Center.X - Px(105), rect.Y + Px(79), Px(42), Px(30));
        _ngPlusRect = new Rectangle(rect.Center.X + Px(63), rect.Y + Px(79), Px(42), Px(30));
        UiTheme.DrawButton(spriteBatch, _ngMinusRect, "-", mouse, mouseDown, enabled: selected > 0,
            accentColor: path.Accent, textSize: Fs(16));
        UiTheme.DrawButton(spriteBatch, _ngPlusRect, "+", mouse, mouseDown, enabled: selected < unlocked,
            accentColor: path.Accent, textSize: Fs(16));
        string tier = selected == 0 ? "NORMAL" : $"NG+{selected}";
        UiTheme.DrawText(spriteBatch, tier, Fs(18), selected == 0 ? UiTheme.Cream : UiTheme.Gold,
            new Vector2(rect.Center.X, rect.Y + Px(85)), "midtop");
        UiTheme.DrawText(spriteBatch,
            $"ENEMIES x{NewGamePlus.EnemyMultiplier(selected):0.##}  //  CLEAR REWARD x{NewGamePlus.RewardMultiplier(selected)}  //  UNLOCKED TO NG+{unlocked}",
            Fs(9), UiTheme.Muted, new Vector2(rect.Center.X, rect.Y + Px(116)), "midtop");
        UiTheme.DrawText(spriteBatch, unlocked == 0
                ? "COMPLETE THIS PATH TO UNLOCK NG+1"
                : "A / D OR ARROWS  //  SELECT TIER",
            Fs(9), unlocked == 0 ? UiTheme.Red : path.Accent,
            new Vector2(rect.Center.X, rect.Y + Px(138)), "midtop");
        if (GameProfile.Profile.HardModeEnabled)
            UiTheme.DrawText(spriteBatch, "HARD MODE  //  NO HEALING  //  2X CLEAR TOKENS  //  CORE-FORGED DROPS",
                Fs(9), UiTheme.Red, new Vector2(rect.Center.X, rect.Y + Px(160)), "midtop");
        UiTheme.DrawText(spriteBatch, "F  CONFIRM   //   WALK AWAY OR ESC  CANCEL", Fs(10), UiTheme.Muted,
            new Vector2(rect.Center.X, rect.Bottom - Px(24)), "center");
    }

    /// <summary>Full-screen cover that ramps to solid black over the animation's second half, timed with UpdatePortalTravel so the arriving-at-portal moment and the fade-out land together.</summary>
    private void DrawPortalFade(SpriteBatch spriteBatch, GameSession session)
    {
        double fadeT = Math.Clamp((_seconds - _portalAnimationStart - PortalPullSeconds) / PortalFadeSeconds, 0, 1);
        if (fadeT <= 0)
            return;
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, session.ScreenWidth, session.ScreenHeight), UiTheme.Void * (float)fadeT);
    }

    private void DrawOverlay(SpriteBatch spriteBatch, GameSession session, Point mouse)
    {
        _tooltip = null;
        int screenWidth = session.ScreenWidth, screenHeight = session.ScreenHeight;
        if (_overlay == "storage")
        {
            // Bounded to the arena (left of the sidebar), a separate interface from the
            // always-visible carried-loadout sidebar on the right -- see DrawWorld/
            // SidebarShown. Doesn't darken/cover the sidebar, so it stays legible and
            // draggable while the Vault is open.
            int arenaWidth = session.InformationSheet.ArenaWidth;
            Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, arenaWidth, screenHeight), UiTheme.Void * .94f);
            var vaultPanel = new Rectangle((int)(arenaWidth * .07f), (int)(screenHeight * .12f),
                (int)(arenaWidth * .55f), (int)(screenHeight * .72f));
            UiTheme.DrawPanel(spriteBatch, vaultPanel, UiTheme.PanelRaised, UiTheme.Gold, shadow: 10);
            DrawVault(spriteBatch, vaultPanel, mouse);
            if (_tooltip is not null) DrawTooltip(spriteBatch, mouse, vaultPanel);
            return;
        }

        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), UiTheme.Void * .94f);
        var panel = new Rectangle((int)(screenWidth * .055f), (int)(screenHeight * .07f),
            (int)(screenWidth * .89f), (int)(screenHeight * .80f));
        UiTheme.DrawPanel(spriteBatch, panel, UiTheme.PanelRaised, UiTheme.Green, shadow: 10);
        if (_overlay == "quests") DrawQuests(spriteBatch, panel, mouse);
        if (_overlay == "skills") DrawSkills(spriteBatch, panel, mouse);
        if (_overlay == "wardrobe") DrawWardrobe(spriteBatch, panel, mouse);
        if (_tooltip is not null) DrawTooltip(spriteBatch, mouse, panel);
    }

    private static string TimeLabel(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

    /// <summary>
    /// Safe, permanent, MetaProgression.StorageCapacity-limited -- a separate interface
    /// from the carried-loadout sidebar (session.DrawCarriedLoadout, the same style/hub
    /// layout as a real run's sidebar, drawn on the right by DrawWorld). Drag items
    /// between the two; the drag itself lives in InformationSheet (see its
    /// VaultDragSource), fed this panel's slot rects via
    /// GameSession.HandleCarriedLoadoutDrag -- there's no click-to-stage step anymore,
    /// what's in your sidebar *is* what you're bringing into your next run, live.
    /// </summary>
    private void DrawVault(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "VAULT", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, "SAFE, PERMANENT  //  DRAG ITEMS TO AND FROM YOUR INVENTORY", Fs(9), UiTheme.Gold,
            new Vector2(panel.X + Px(26), panel.Y + Px(53)));

        int slotSize = Px(44), gap = Px(8);
        const int vaultColumns = 5;
        int vaultLeft = panel.X + Px(26), vaultTop = panel.Y + Px(80);
        _vaultSlotRects = new List<Rectangle>();
        for (int index = 0; index < MetaProgression.StorageCapacity; index++)
        {
            int column = index % vaultColumns, row = index / vaultColumns;
            var rect = new Rectangle(vaultLeft + column * (slotSize + gap), vaultTop + row * (slotSize + gap), slotSize, slotSize);
            _vaultSlotRects.Add(rect);
            Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Border, Px(2));
            if (index >= GameProfile.Profile.Storage.Count) continue;
            var drop = Items.Deserialize(GameProfile.Profile.Storage[index]);
            if (drop is null) continue;
            ItemCards.DrawItemCard(spriteBatch, rect, drop, rect.Contains(mouse));
            if (rect.Contains(mouse)) _tooltip = $"{drop.Rarity} {drop.Name}  //  Drag to your inventory to carry it into a run.";
        }
        int vaultRows = (MetaProgression.StorageCapacity + vaultColumns - 1) / vaultColumns;
        UiTheme.DrawText(spriteBatch, $"{GameProfile.Profile.Storage.Count}/{MetaProgression.StorageCapacity}", Fs(9), UiTheme.Muted,
            new Vector2(vaultLeft, vaultTop + vaultRows * (slotSize + gap) + Px(4)));

        int y = vaultTop + vaultRows * (slotSize + gap) + Px(34);
        UiTheme.DrawText(spriteBatch, "RUN HISTORY", Fs(11), UiTheme.Muted, new Vector2(panel.X + Px(26), y));
        y += Px(20);
        if (GameProfile.Profile.ExtractedRuns.Count == 0)
        {
            UiTheme.DrawText(spriteBatch, "No runs logged yet -- reach a path ending or extract after the midpoint boss.",
                Fs(10), UiTheme.Cream, new Vector2(panel.X + Px(26), y));
            return;
        }
        int runWidth = panel.Width - Px(52);
        int shown = Math.Min(6, GameProfile.Profile.ExtractedRuns.Count);
        int runHeight = Math.Max(Px(26), (panel.Bottom - y - Px(18)) / shown - Px(6));
        for (int index = 0; index < shown; index++)
        {
            var run = GameProfile.Profile.ExtractedRuns[index];
            var rect = new Rectangle(panel.X + Px(26), y + index * (runHeight + Px(6)), runWidth, runHeight);
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, UiTheme.Green);
            UiTheme.DrawText(spriteBatch, $"{index + 1:00}  {run.Path.ToUpperInvariant()}  //  {run.Outcome}", Fs(9), UiTheme.Text,
                new Vector2(rect.X + Px(9), rect.Center.Y), "midleft");
            UiTheme.DrawText(spriteBatch, $"LV {run.Level:00}  •  {run.Kills} KILLS  •  {TimeLabel(run.Seconds)}", Fs(8), UiTheme.Muted,
                new Vector2(rect.Right - Px(9), rect.Center.Y), "midright");
        }
    }

    private void DrawQuests(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
    {
        MetaProgression.CompleteReadyQuests();
        UiTheme.DrawText(spriteBatch, "QUEST GRID", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, "GENERIC OBJECTIVES PERSIST ACROSS RUNS  //  GREEN BARS SHOW COMPLETION", Fs(9), UiTheme.Green,
            new Vector2(panel.X + Px(26), panel.Y + Px(53)));
        int columns = 4, gap = Px(9), tileWidth = (panel.Width - Px(52) - gap * 3) / 4;
        int tileHeight = (panel.Height - Px(105) - gap * 5) / 6;
        for (int index = 0; index < MetaProgression.Quests.Count; index++)
        {
            var quest = MetaProgression.Quests[index];
            int column = index % columns, row = index / columns;
            var rect = new Rectangle(panel.X + Px(26) + column * (tileWidth + gap), panel.Y + Px(78) + row * (tileHeight + gap), tileWidth, tileHeight);
            long value = Math.Min(quest.Target, GameProfile.Profile.QuestProgress.GetValueOrDefault(quest.Counter));
            bool complete = GameProfile.Profile.CompletedQuests.Contains(quest.Key);
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, complete ? UiTheme.Green : UiTheme.Border, hovered: rect.Contains(mouse));
            var symbol = new Rectangle(rect.X + Px(8), rect.Y + Px(8), Px(36), Px(36));
            Primitives2D.FillRect(spriteBatch, symbol, complete ? UiTheme.Green : UiTheme.Ink);
            DrawQuestSymbol(spriteBatch, quest.Symbol, symbol, complete ? UiTheme.Ink : UiTheme.Gold);
            UiTheme.DrawText(spriteBatch, quest.Name.ToUpperInvariant(), Fs(9), UiTheme.Text, new Vector2(symbol.Right + Px(8), rect.Y + Px(9)));
            UiTheme.DrawText(spriteBatch, complete ? "COMPLETE" : $"{value:N0} / {quest.Target:N0}", Fs(8), complete ? UiTheme.Green : UiTheme.Muted,
                new Vector2(symbol.Right + Px(8), rect.Y + Px(27)));
            UiTheme.DrawProgress(spriteBatch, new Rectangle(rect.X + Px(8), rect.Bottom - Px(15), rect.Width - Px(16), Px(8)),
                (float)value / quest.Target, UiTheme.Green, segments: 8);
            if (rect.Contains(mouse)) _tooltip = $"{quest.Description}  Reward: {quest.Reward} Soul token{(quest.Reward == 1 ? "" : "s")}.";
        }
    }

    private static void DrawQuestSymbol(SpriteBatch spriteBatch, string symbol, Rectangle rect, Color color)
    {
        Rectangle inner = rect;
        inner.Inflate(-7, -7);
        string? stat = symbol switch
        {
            "DMG" or "DPS" => "Bullet Damage", "SHOT" => "Bullet Count", "CRIT" => "Crit Chance",
            "BOOT" => "Player Speed", "LEVEL" => "Exp Multiplier", _ => null,
        };
        if (stat is not null)
        {
            StatCards.DrawStatSymbol(spriteBatch, stat, inner, color);
            return;
        }
        var c = new Vector2(rect.Center.X, rect.Center.Y);
        float u = rect.Width / 36f;
        if (symbol == "SKULL")
        {
            Primitives2D.CircleOutline(spriteBatch, c - new Vector2(0, 3 * u), 8 * u, color, 2);
            Primitives2D.RectOutline(spriteBatch, new Rectangle((int)(c.X - 5 * u), (int)(c.Y + 3 * u), (int)(10 * u), (int)(6 * u)), color, 2);
            Primitives2D.FillCircle(spriteBatch, c + new Vector2(-3 * u, -4 * u), 1.5f * u, color);
            Primitives2D.FillCircle(spriteBatch, c + new Vector2(3 * u, -4 * u), 1.5f * u, color);
        }
        else if (symbol == "CHEST")
        {
            var box = new Rectangle((int)(c.X - 9 * u), (int)(c.Y - 5 * u), (int)(18 * u), (int)(13 * u));
            Primitives2D.RectOutline(spriteBatch, box, color, 2);
            Primitives2D.Line(spriteBatch, new Vector2(box.Left, c.Y), new Vector2(box.Right, c.Y), color, 2);
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)c.X - 1, (int)c.Y - 2, 3, 6), color);
        }
        else if (symbol == "CROWN")
        {
            Primitives2D.PolygonOutline(spriteBatch, new[]
            {
                c + new Vector2(-9*u, 7*u), c + new Vector2(-9*u, -5*u), c + new Vector2(-3*u, 1*u),
                c + new Vector2(0, -8*u), c + new Vector2(4*u, 1*u), c + new Vector2(9*u, -5*u), c + new Vector2(9*u, 7*u),
            }, color, 2);
        }
        else if (symbol == "DROP")
        {
            Primitives2D.PolygonOutline(spriteBatch, new[]
            {
                c + new Vector2(0, -10*u), c + new Vector2(7*u, 3*u), c + new Vector2(5*u, 8*u),
                c + new Vector2(0, 10*u), c + new Vector2(-5*u, 8*u), c + new Vector2(-7*u, 3*u),
            }, color, 2);
        }
        else
        {
            var door = new Rectangle((int)(c.X - 7 * u), (int)(c.Y - 10 * u), (int)(14 * u), (int)(20 * u));
            Primitives2D.RectOutline(spriteBatch, door, color, 2);
            Primitives2D.FillCircle(spriteBatch, c + new Vector2(3 * u, 1 * u), 1.3f * u, color);
        }
    }

    private void DrawSkills(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "SOUL GRID", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, $"SOUL TOKENS  {GameProfile.Profile.SoulTokens}  //  CLICK A TILE TO BUY ONE RANK", Fs(9), UiTheme.Purple,
            new Vector2(panel.X + Px(26), panel.Y + Px(53)));
        int columns = 4, gap = Px(12), tileWidth = (panel.Width - Px(52) - gap * 3) / 4;
        int tileHeight = (panel.Height - Px(112) - gap * 2) / 3;
        for (int index = 0; index < MetaProgression.SkillNodes.Count; index++)
        {
            var node = MetaProgression.SkillNodes[index];
            int column = index % columns, row = index / columns;
            var rect = new Rectangle(panel.X + Px(26) + column * (tileWidth + gap), panel.Y + Px(80) + row * (tileHeight + gap), tileWidth, tileHeight);
            int level = GameProfile.Profile.SkillLevels.GetValueOrDefault(node.Key), cost = node.BaseCost + level / 2;
            bool maxed = level >= node.MaxLevel, affordable = GameProfile.Profile.SoulTokens >= cost;
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, level > 0 ? UiTheme.Green : UiTheme.Purple, hovered: rect.Contains(mouse));
            var symbol = new Rectangle(rect.X + Px(12), rect.Y + Px(13), Px(48), Px(48));
            StatCards.DrawStatSymbol(spriteBatch, node.Stat, symbol, level > 0 ? UiTheme.Green : UiTheme.Purple);
            UiTheme.DrawText(spriteBatch, node.Name.ToUpperInvariant(), Fs(11), UiTheme.Text, new Vector2(symbol.Right + Px(10), rect.Y + Px(15)));
            UiTheme.DrawText(spriteBatch, maxed ? "MASTERED" : $"{cost} TOKEN{(cost == 1 ? "" : "S")}", Fs(8),
                maxed ? UiTheme.Green : affordable ? UiTheme.Gold : UiTheme.Red, new Vector2(symbol.Right + Px(10), rect.Y + Px(39)));
            UiTheme.DrawProgress(spriteBatch, new Rectangle(rect.X + Px(12), rect.Bottom - Px(25), rect.Width - Px(24), Px(12)),
                (float)level / node.MaxLevel, UiTheme.Green, segments: node.MaxLevel);
            _targets[$"skill:{node.Key}"] = rect;
            if (rect.Contains(mouse)) _tooltip = $"{node.Description}  Rank {level}/{node.MaxLevel}.";
        }
    }

    private void DrawWardrobe(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "THE WARDROBE", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, "COSMETIC ONLY  //  CORE FILLS THE BODY, EDGE FRAMES IT, SHOTS USE A TWO-TONE PALETTE",
            Fs(9), UiTheme.Blue, new Vector2(panel.X + Px(26), panel.Y + Px(53)));

        int gap = Px(12);
        int columnWidth = (panel.Width - Px(52) - gap * 3) / 4;
        int top = panel.Y + Px(84);
        DrawColorColumn(spriteBatch, new Rectangle(panel.X + Px(26), top, columnWidth, panel.Height - Px(104)),
            "CORE COLOR", "core", Cosmetics.CoreColors, GameProfile.Profile.PlayerCoreColor, mouse);
        DrawColorColumn(spriteBatch, new Rectangle(panel.X + Px(26) + columnWidth + gap, top, columnWidth, panel.Height - Px(104)),
            "EDGE COLOR", "edge", Cosmetics.EdgeColors, GameProfile.Profile.PlayerEdgeColor, mouse);
        DrawProjectileColorColumn(spriteBatch, new Rectangle(panel.X + Px(26) + 2 * (columnWidth + gap), top, columnWidth, panel.Height - Px(104)), mouse);
        DrawProjectileDesignColumn(spriteBatch, new Rectangle(panel.X + Px(26) + 3 * (columnWidth + gap), top, columnWidth, panel.Height - Px(104)), mouse);

        var preview = new Rectangle(panel.Center.X - Px(65), panel.Bottom - Px(150), Px(130), Px(112));
        UiTheme.DrawPanel(spriteBatch, preview, UiTheme.Panel, UiTheme.Blue, shadow: 5);
        var body = new Rectangle(preview.X + Px(18), preview.Y + Px(25), Px(42), Px(42));
        Primitives2D.FillRect(spriteBatch, new Rectangle(body.X + Px(4), body.Y + Px(5), body.Width, body.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, body, Cosmetics.SelectedCore.Color);
        Primitives2D.RectOutline(spriteBatch, body, Cosmetics.SelectedEdge.Color, Px(4));
        ProjectileVisuals.Draw(spriteBatch, new Vector2(preview.X + Px(94), preview.Y + Px(46)), Vector2.UnitX, Px(27),
            Cosmetics.SelectedProjectile.Core, Cosmetics.SelectedProjectile.Edge, Cosmetics.SelectedDesign.Id);
        UiTheme.DrawText(spriteBatch, "LIVE PREVIEW", Fs(8), UiTheme.Muted, new Vector2(preview.Center.X, preview.Bottom - Px(18)), "center");
    }

    private void DrawColorColumn(SpriteBatch spriteBatch, Rectangle column, string title, string category,
        IReadOnlyList<CosmeticColor> colors, string selected, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, title, Fs(12), UiTheme.Text, new Vector2(column.X, column.Y));
        int tile = Math.Min(Px(48), (column.Width - Px(12)) / 3), gap = Px(6);
        int startY = column.Y + Px(30);
        for (int index = 0; index < colors.Count; index++)
        {
            var option = colors[index];
            int row = index / 3, col = index % 3;
            var rect = new Rectangle(column.X + col * (tile + gap), startY + row * (tile + gap), tile, tile);
            Primitives2D.FillRect(spriteBatch, rect, option.Color);
            Primitives2D.RectOutline(spriteBatch, rect, option.Id == selected ? UiTheme.Cream : UiTheme.Ink, option.Id == selected ? Px(4) : Px(2));
            _targets[$"cosmetic:{category}:{option.Id}"] = rect;
            if (rect.Contains(mouse)) _tooltip = $"{option.Name} {title.ToLowerInvariant()}.";
        }
    }

    private void DrawProjectileColorColumn(SpriteBatch spriteBatch, Rectangle column, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "SHOT COLOR", Fs(12), UiTheme.Text, new Vector2(column.X, column.Y));
        int tile = Math.Min(Px(48), (column.Width - Px(12)) / 3), gap = Px(6);
        int startY = column.Y + Px(30);
        for (int index = 0; index < Cosmetics.ProjectileColors.Count; index++)
        {
            var option = Cosmetics.ProjectileColors[index];
            int row = index / 3, col = index % 3;
            var rect = new Rectangle(column.X + col * (tile + gap), startY + row * (tile + gap), tile, tile);
            Primitives2D.FillRect(spriteBatch, rect, option.Edge);
            var inner = rect;
            inner.Inflate(-Math.Max(5, tile / 5), -Math.Max(5, tile / 5));
            Primitives2D.FillRect(spriteBatch, inner, option.Core);
            bool selected = option.Id == GameProfile.Profile.ProjectileColor;
            Primitives2D.RectOutline(spriteBatch, rect, selected ? UiTheme.Cream : UiTheme.Ink, selected ? Px(4) : Px(2));
            _targets[$"cosmetic:projectile:{option.Id}"] = rect;
            if (rect.Contains(mouse)) _tooltip = $"{option.Name} projectile palette.";
        }
    }

    private void DrawProjectileDesignColumn(SpriteBatch spriteBatch, Rectangle column, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "SHOT DESIGN", Fs(12), UiTheme.Text, new Vector2(column.X, column.Y));
        int y = column.Y + Px(30);
        foreach (var option in Cosmetics.ProjectileDesigns)
        {
            var rect = new Rectangle(column.X, y, column.Width, Px(58));
            bool selected = option.Id == GameProfile.Profile.ProjectileDesign;
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, selected ? UiTheme.Cream : UiTheme.Border, hovered: rect.Contains(mouse));
            ProjectileVisuals.Draw(spriteBatch, new Vector2(rect.X + Px(38), rect.Center.Y), Vector2.UnitX, Px(25),
                Cosmetics.SelectedProjectile.Core, Cosmetics.SelectedProjectile.Edge, option.Id);
            UiTheme.DrawText(spriteBatch, option.Name.ToUpperInvariant(), Fs(9), UiTheme.Text, new Vector2(rect.X + Px(72), rect.Center.Y), "midleft");
            _targets[$"cosmetic:design:{option.Id}"] = rect;
            if (rect.Contains(mouse)) _tooltip = option.Description;
            y += Px(65);
        }
    }

    private void DrawTooltip(SpriteBatch spriteBatch, Point mouse, Rectangle bounds)
    {
        int width = Math.Min(Px(360), bounds.Width / 2);
        var words = _tooltip!.Split(' ');
        var lines = new List<string>();
        string line = "";
        foreach (string word in words)
        {
            if ((line.Length + word.Length + 1) > 64 && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else line = (line + " " + word).Trim();
        }
        if (line.Length > 0) lines.Add(line);
        var rect = new Rectangle(mouse.X + Px(15), mouse.Y + Px(15), width, Px(24 + lines.Count * 17));
        rect.X = Math.Clamp(rect.X, bounds.X + Px(6), bounds.Right - rect.Width - Px(6));
        rect.Y = Math.Clamp(rect.Y, bounds.Y + Px(6), bounds.Bottom - rect.Height - Px(6));
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Ink, UiTheme.Cream, shadow: 4);
        for (int index = 0; index < lines.Count; index++)
            UiTheme.DrawText(spriteBatch, lines[index], Fs(9), UiTheme.Text, new Vector2(rect.X + Px(10), rect.Y + Px(9 + index * 17)));
    }
}
