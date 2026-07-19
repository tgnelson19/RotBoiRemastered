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
    /// <summary>
    /// Ring radius for the path portals -- well outside the four station
    /// offsets (4 tiles) and the DPS dummy (also 4 tiles), and still clear of
    /// every GamePaths generator's nearest building/block (the tightest
    /// being Touch, whose closest cardinal-lane block sits ~18.8 tiles out),
    /// so a 14-tile ring stays open ground in all five.
    /// </summary>
    private const float PathPortalRingRadiusTiles = 14f;
    private const float PathPortalInteractRadiusTiles = 1.6f;
    private const float PathPortalConfirmCloseRadiusTiles = 2.1f;
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
    private float _playerDrawScale = 1f;
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
        _dummyWorld = session.PlayerWorldCenter + new Vector2(Simulation.TileSize * 4, 0);
        _dummy = new TrainingDummy(_dummyWorld.X, _dummyWorld.Y);
        _stationWorld.Clear();
        _stationWorld["storage"] = session.PlayerWorldCenter - new Vector2(Simulation.TileSize * 4, 0);
        _stationWorld["quests"] = session.PlayerWorldCenter - new Vector2(0, Simulation.TileSize * 4);
        _stationWorld["skills"] = session.PlayerWorldCenter + new Vector2(0, Simulation.TileSize * 4);
        _stationWorld["wardrobe"] = session.PlayerWorldCenter + new Vector2(-Simulation.TileSize * 4, Simulation.TileSize * 4);
        _pathPortalWorld.Clear();
        var paths = GamePaths.Paths;
        for (int index = 0; index < paths.Count; index++)
        {
            // Start pointing straight up and go clockwise so the ring reads
            // left-to-right the same order as GamePaths.Paths / the old
            // title-screen selector.
            float angle = -MathHelper.PiOver2 + MathHelper.TwoPi * index / paths.Count;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Simulation.TileSize * PathPortalRingRadiusTiles;
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
                _overlay = nearby;
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
        UiTheme.DrawText(spriteBatch, "THE SOUL", 27, UiTheme.Text, new Vector2(22, 18));
        UiTheme.DrawText(spriteBatch, "SAFE GROUND  //  WALK TO A STATION OR PATH PORTAL  //  F INTERACT  //  ESC OPTIONS", 9, UiTheme.Muted, new Vector2(24, 54));
        DrawStations(spriteBatch, session);
        DrawPathPortals(spriteBatch, session);
    }

    /// <summary>
    /// UI-layer draw: overlay panels, the portal confirm modal, the carried-
    /// loadout sidebar, and the portal fade -- everything meant to sit *on
    /// top of* the player. Call after GameSession.DrawPlayer; see
    /// <see cref="DrawWorld"/>.
    /// </summary>
    public void DrawForeground(SpriteBatch spriteBatch, GameSession session, Point mouse, bool mouseDown)
    {
        if (_overlay is not null) DrawOverlay(spriteBatch, session, mouse);
        if (_confirmingPortalKey is not null) DrawPortalConfirm(spriteBatch, session);
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
        };
        foreach (var (key, world) in _stationWorld)
        {
            var position = session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
            var (label, accent) = labels[key];
            var baseRect = new Rectangle((int)position.X - 28, (int)position.Y - 24, 56, 48);
            Primitives2D.FillRect(spriteBatch, new Rectangle(baseRect.X + 5, baseRect.Y + 6, baseRect.Width, baseRect.Height), UiTheme.Shadow);
            Primitives2D.FillRect(spriteBatch, baseRect, UiTheme.PanelRaised);
            Primitives2D.RectOutline(spriteBatch, baseRect, accent, 3);
            Primitives2D.FillCircle(spriteBatch, new Vector2(position.X, position.Y), 9, accent);
            UiTheme.DrawText(spriteBatch, label, 8, accent, new Vector2(position.X, baseRect.Bottom + 7), "midtop");
        }
        var nearby = NearbyStation(session);
        if (nearby is not null)
            UiTheme.DrawText(spriteBatch, $"F  //  OPEN {labels[nearby].Label}", 13, labels[nearby].Accent,
                new Vector2(session.ScreenWidth / 2f, session.ScreenHeight - 42), "center");
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
            float radius = Simulation.TileSize * 1.05f;
            // Committing spins the destination portal up hard during the pull
            // (see UpdatePortalTravel) so it visibly reels the player in,
            // instead of sitting there identical to every other portal.
            bool committing = path.Key == _enteringPortalKey;
            float pullT = committing ? (float)Math.Clamp((_seconds - _portalAnimationStart) / PortalPullSeconds, 0, 1) : 0f;
            float intensity = 1f + pullT * 2.2f;
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
            // Suppressed while confirming/entering that same portal -- the center
            // confirmation panel (DrawPortalConfirm) already explains the prompt.
            if (path.Key == nearbyPortal && path.Key != _confirmingPortalKey && _enteringPortalKey is null)
                UiTheme.DrawText(spriteBatch, "F  //  ENTER", 9, UiTheme.Cream, new Vector2(screen.X, screen.Y + radius + 26), "midtop");
        }
    }

    /// <summary>Centered "ENTER {PATH}?" modal shown while _confirmingPortalKey is set -- F commits, walking away or Escape cancels (Escape via OverlayOpen/CloseOverlay in Core/RotBoiGame.cs).</summary>
    private void DrawPortalConfirm(SpriteBatch spriteBatch, GameSession session)
    {
        var path = GamePaths.PathsByKey[_confirmingPortalKey!];
        int width = (int)(session.ScreenWidth * .34f), height = (int)(session.ScreenHeight * .2f);
        var rect = new Rectangle(session.ScreenWidth / 2 - width / 2, (int)(session.ScreenHeight * .32f), width, height);
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, session.ScreenWidth, session.ScreenHeight), UiTheme.Void * .55f);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, path.Accent, shadow: 10);
        UiTheme.DrawText(spriteBatch, $"ENTER {path.Title}?", 22, path.Accent, new Vector2(rect.Center.X, rect.Y + 26), "center");
        UiTheme.DrawText(spriteBatch, path.Subtitle, 11, UiTheme.Cream, new Vector2(rect.Center.X, rect.Y + 62), "center");
        UiTheme.DrawText(spriteBatch, "F  CONFIRM   //   WALK AWAY OR ESC  CANCEL", 10, UiTheme.Muted,
            new Vector2(rect.Center.X, rect.Bottom - 24), "center");
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
        UiTheme.DrawText(spriteBatch, "VAULT", 24, UiTheme.Text, new Vector2(panel.X + 24, panel.Y + 18));
        UiTheme.DrawText(spriteBatch, "SAFE, PERMANENT  //  DRAG ITEMS TO AND FROM YOUR INVENTORY", 9, UiTheme.Gold,
            new Vector2(panel.X + 26, panel.Y + 53));

        int slotSize = 44, gap = 8;
        const int vaultColumns = 5;
        int vaultLeft = panel.X + 26, vaultTop = panel.Y + 80;
        _vaultSlotRects = new List<Rectangle>();
        for (int index = 0; index < MetaProgression.StorageCapacity; index++)
        {
            int column = index % vaultColumns, row = index / vaultColumns;
            var rect = new Rectangle(vaultLeft + column * (slotSize + gap), vaultTop + row * (slotSize + gap), slotSize, slotSize);
            _vaultSlotRects.Add(rect);
            Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Border, 2);
            if (index >= GameProfile.Profile.Storage.Count) continue;
            var drop = Items.Deserialize(GameProfile.Profile.Storage[index]);
            if (drop is null) continue;
            ItemCards.DrawItemCard(spriteBatch, rect, drop, rect.Contains(mouse));
            if (rect.Contains(mouse)) _tooltip = $"{drop.Rarity} {drop.Name}  //  Drag to your inventory to carry it into a run.";
        }
        int vaultRows = (MetaProgression.StorageCapacity + vaultColumns - 1) / vaultColumns;
        UiTheme.DrawText(spriteBatch, $"{GameProfile.Profile.Storage.Count}/{MetaProgression.StorageCapacity}", 9, UiTheme.Muted,
            new Vector2(vaultLeft, vaultTop + vaultRows * (slotSize + gap) + 4));

        int y = vaultTop + vaultRows * (slotSize + gap) + 34;
        UiTheme.DrawText(spriteBatch, "RUN HISTORY", 11, UiTheme.Muted, new Vector2(panel.X + 26, y));
        y += 20;
        if (GameProfile.Profile.ExtractedRuns.Count == 0)
        {
            UiTheme.DrawText(spriteBatch, "No runs logged yet -- reach a path ending or extract after the midpoint boss.",
                10, UiTheme.Cream, new Vector2(panel.X + 26, y));
            return;
        }
        int runWidth = panel.Width - 52;
        int shown = Math.Min(6, GameProfile.Profile.ExtractedRuns.Count);
        int runHeight = Math.Max(26, (panel.Bottom - y - 18) / shown - 6);
        for (int index = 0; index < shown; index++)
        {
            var run = GameProfile.Profile.ExtractedRuns[index];
            var rect = new Rectangle(panel.X + 26, y + index * (runHeight + 6), runWidth, runHeight);
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, UiTheme.Green);
            UiTheme.DrawText(spriteBatch, $"{index + 1:00}  {run.Path.ToUpperInvariant()}  //  {run.Outcome}", 9, UiTheme.Text,
                new Vector2(rect.X + 9, rect.Center.Y), "midleft");
            UiTheme.DrawText(spriteBatch, $"LV {run.Level:00}  •  {run.Kills} KILLS  •  {TimeLabel(run.Seconds)}", 8, UiTheme.Muted,
                new Vector2(rect.Right - 9, rect.Center.Y), "midright");
        }
    }

    private void DrawQuests(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
    {
        MetaProgression.CompleteReadyQuests();
        UiTheme.DrawText(spriteBatch, "QUEST GRID", 24, UiTheme.Text, new Vector2(panel.X + 24, panel.Y + 18));
        UiTheme.DrawText(spriteBatch, "GENERIC OBJECTIVES PERSIST ACROSS RUNS  //  GREEN BARS SHOW COMPLETION", 9, UiTheme.Green,
            new Vector2(panel.X + 26, panel.Y + 53));
        int columns = 4, gap = 9, tileWidth = (panel.Width - 52 - gap * 3) / 4;
        int tileHeight = (panel.Height - 105 - gap * 5) / 6;
        for (int index = 0; index < MetaProgression.Quests.Count; index++)
        {
            var quest = MetaProgression.Quests[index];
            int column = index % columns, row = index / columns;
            var rect = new Rectangle(panel.X + 26 + column * (tileWidth + gap), panel.Y + 78 + row * (tileHeight + gap), tileWidth, tileHeight);
            long value = Math.Min(quest.Target, GameProfile.Profile.QuestProgress.GetValueOrDefault(quest.Counter));
            bool complete = GameProfile.Profile.CompletedQuests.Contains(quest.Key);
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, complete ? UiTheme.Green : UiTheme.Border, hovered: rect.Contains(mouse));
            var symbol = new Rectangle(rect.X + 8, rect.Y + 8, 36, 36);
            Primitives2D.FillRect(spriteBatch, symbol, complete ? UiTheme.Green : UiTheme.Ink);
            DrawQuestSymbol(spriteBatch, quest.Symbol, symbol, complete ? UiTheme.Ink : UiTheme.Gold);
            UiTheme.DrawText(spriteBatch, quest.Name.ToUpperInvariant(), 9, UiTheme.Text, new Vector2(symbol.Right + 8, rect.Y + 9));
            UiTheme.DrawText(spriteBatch, complete ? "COMPLETE" : $"{value:N0} / {quest.Target:N0}", 8, complete ? UiTheme.Green : UiTheme.Muted,
                new Vector2(symbol.Right + 8, rect.Y + 27));
            UiTheme.DrawProgress(spriteBatch, new Rectangle(rect.X + 8, rect.Bottom - 15, rect.Width - 16, 8),
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
        UiTheme.DrawText(spriteBatch, "SOUL GRID", 24, UiTheme.Text, new Vector2(panel.X + 24, panel.Y + 18));
        UiTheme.DrawText(spriteBatch, $"SOUL TOKENS  {GameProfile.Profile.SoulTokens}  //  CLICK A TILE TO BUY ONE RANK", 9, UiTheme.Purple,
            new Vector2(panel.X + 26, panel.Y + 53));
        int columns = 4, gap = 12, tileWidth = (panel.Width - 52 - gap * 3) / 4;
        int tileHeight = (panel.Height - 112 - gap * 2) / 3;
        for (int index = 0; index < MetaProgression.SkillNodes.Count; index++)
        {
            var node = MetaProgression.SkillNodes[index];
            int column = index % columns, row = index / columns;
            var rect = new Rectangle(panel.X + 26 + column * (tileWidth + gap), panel.Y + 80 + row * (tileHeight + gap), tileWidth, tileHeight);
            int level = GameProfile.Profile.SkillLevels.GetValueOrDefault(node.Key), cost = node.BaseCost + level / 2;
            bool maxed = level >= node.MaxLevel, affordable = GameProfile.Profile.SoulTokens >= cost;
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, level > 0 ? UiTheme.Green : UiTheme.Purple, hovered: rect.Contains(mouse));
            var symbol = new Rectangle(rect.X + 12, rect.Y + 13, 48, 48);
            StatCards.DrawStatSymbol(spriteBatch, node.Stat, symbol, level > 0 ? UiTheme.Green : UiTheme.Purple);
            UiTheme.DrawText(spriteBatch, node.Name.ToUpperInvariant(), 11, UiTheme.Text, new Vector2(symbol.Right + 10, rect.Y + 15));
            UiTheme.DrawText(spriteBatch, maxed ? "MASTERED" : $"{cost} TOKEN{(cost == 1 ? "" : "S")}", 8,
                maxed ? UiTheme.Green : affordable ? UiTheme.Gold : UiTheme.Red, new Vector2(symbol.Right + 10, rect.Y + 39));
            UiTheme.DrawProgress(spriteBatch, new Rectangle(rect.X + 12, rect.Bottom - 25, rect.Width - 24, 12),
                (float)level / node.MaxLevel, UiTheme.Green, segments: node.MaxLevel);
            _targets[$"skill:{node.Key}"] = rect;
            if (rect.Contains(mouse)) _tooltip = $"{node.Description}  Rank {level}/{node.MaxLevel}.";
        }
    }

    private void DrawWardrobe(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "THE WARDROBE", 24, UiTheme.Text, new Vector2(panel.X + 24, panel.Y + 18));
        UiTheme.DrawText(spriteBatch, "COSMETIC ONLY  //  CORE FILLS THE BODY, EDGE FRAMES IT, SHOTS USE A TWO-TONE PALETTE",
            9, UiTheme.Blue, new Vector2(panel.X + 26, panel.Y + 53));

        int gap = 12;
        int columnWidth = (panel.Width - 52 - gap * 3) / 4;
        int top = panel.Y + 84;
        DrawColorColumn(spriteBatch, new Rectangle(panel.X + 26, top, columnWidth, panel.Height - 104),
            "CORE COLOR", "core", Cosmetics.CoreColors, GameProfile.Profile.PlayerCoreColor, mouse);
        DrawColorColumn(spriteBatch, new Rectangle(panel.X + 26 + columnWidth + gap, top, columnWidth, panel.Height - 104),
            "EDGE COLOR", "edge", Cosmetics.EdgeColors, GameProfile.Profile.PlayerEdgeColor, mouse);
        DrawProjectileColorColumn(spriteBatch, new Rectangle(panel.X + 26 + 2 * (columnWidth + gap), top, columnWidth, panel.Height - 104), mouse);
        DrawProjectileDesignColumn(spriteBatch, new Rectangle(panel.X + 26 + 3 * (columnWidth + gap), top, columnWidth, panel.Height - 104), mouse);

        var preview = new Rectangle(panel.Center.X - 65, panel.Bottom - 150, 130, 112);
        UiTheme.DrawPanel(spriteBatch, preview, UiTheme.Panel, UiTheme.Blue, shadow: 5);
        var body = new Rectangle(preview.X + 18, preview.Y + 25, 42, 42);
        Primitives2D.FillRect(spriteBatch, new Rectangle(body.X + 4, body.Y + 5, body.Width, body.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, body, Cosmetics.SelectedCore.Color);
        Primitives2D.RectOutline(spriteBatch, body, Cosmetics.SelectedEdge.Color, 4);
        ProjectileVisuals.Draw(spriteBatch, new Vector2(preview.X + 94, preview.Y + 46), Vector2.UnitX, 27,
            Cosmetics.SelectedProjectile.Core, Cosmetics.SelectedProjectile.Edge, Cosmetics.SelectedDesign.Id);
        UiTheme.DrawText(spriteBatch, "LIVE PREVIEW", 8, UiTheme.Muted, new Vector2(preview.Center.X, preview.Bottom - 18), "center");
    }

    private void DrawColorColumn(SpriteBatch spriteBatch, Rectangle column, string title, string category,
        IReadOnlyList<CosmeticColor> colors, string selected, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, title, 12, UiTheme.Text, new Vector2(column.X, column.Y));
        int tile = Math.Min(48, (column.Width - 12) / 3), gap = 6;
        int startY = column.Y + 30;
        for (int index = 0; index < colors.Count; index++)
        {
            var option = colors[index];
            int row = index / 3, col = index % 3;
            var rect = new Rectangle(column.X + col * (tile + gap), startY + row * (tile + gap), tile, tile);
            Primitives2D.FillRect(spriteBatch, rect, option.Color);
            Primitives2D.RectOutline(spriteBatch, rect, option.Id == selected ? UiTheme.Cream : UiTheme.Ink, option.Id == selected ? 4 : 2);
            _targets[$"cosmetic:{category}:{option.Id}"] = rect;
            if (rect.Contains(mouse)) _tooltip = $"{option.Name} {title.ToLowerInvariant()}.";
        }
    }

    private void DrawProjectileColorColumn(SpriteBatch spriteBatch, Rectangle column, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "SHOT COLOR", 12, UiTheme.Text, new Vector2(column.X, column.Y));
        int tile = Math.Min(48, (column.Width - 12) / 3), gap = 6;
        int startY = column.Y + 30;
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
            Primitives2D.RectOutline(spriteBatch, rect, selected ? UiTheme.Cream : UiTheme.Ink, selected ? 4 : 2);
            _targets[$"cosmetic:projectile:{option.Id}"] = rect;
            if (rect.Contains(mouse)) _tooltip = $"{option.Name} projectile palette.";
        }
    }

    private void DrawProjectileDesignColumn(SpriteBatch spriteBatch, Rectangle column, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "SHOT DESIGN", 12, UiTheme.Text, new Vector2(column.X, column.Y));
        int y = column.Y + 30;
        foreach (var option in Cosmetics.ProjectileDesigns)
        {
            var rect = new Rectangle(column.X, y, column.Width, 58);
            bool selected = option.Id == GameProfile.Profile.ProjectileDesign;
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, selected ? UiTheme.Cream : UiTheme.Border, hovered: rect.Contains(mouse));
            ProjectileVisuals.Draw(spriteBatch, new Vector2(rect.X + 38, rect.Center.Y), Vector2.UnitX, 25,
                Cosmetics.SelectedProjectile.Core, Cosmetics.SelectedProjectile.Edge, option.Id);
            UiTheme.DrawText(spriteBatch, option.Name.ToUpperInvariant(), 9, UiTheme.Text, new Vector2(rect.X + 72, rect.Center.Y), "midleft");
            _targets[$"cosmetic:design:{option.Id}"] = rect;
            if (rect.Contains(mouse)) _tooltip = option.Description;
            y += 65;
        }
    }

    private void DrawTooltip(SpriteBatch spriteBatch, Point mouse, Rectangle bounds)
    {
        int width = Math.Min(360, bounds.Width / 2);
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
        var rect = new Rectangle(mouse.X + 15, mouse.Y + 15, width, 24 + lines.Count * 17);
        rect.X = Math.Clamp(rect.X, bounds.X + 6, bounds.Right - rect.Width - 6);
        rect.Y = Math.Clamp(rect.Y, bounds.Y + 6, bounds.Bottom - rect.Height - 6);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Ink, UiTheme.Cream, shadow: 4);
        for (int index = 0; index < lines.Count; index++)
            UiTheme.DrawText(spriteBatch, lines[index], 9, UiTheme.Text, new Vector2(rect.X + 10, rect.Y + 9 + index * 17));
    }
}
