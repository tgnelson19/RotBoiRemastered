using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public enum AphantasiaTrophyVisual
{
    Normal,
    Bloody,
    Cracked,
    BloodyAndCracked,
    Rainbow,
}

internal readonly record struct AphantasiaTrophyMotion(
    float Hover,
    float Pulse,
    float OrbitRadians);

/// <summary>Legacy source filename for The Mind's safe firing range and progression sanctuary.</summary>
public class SoulHub
{
    public static IReadOnlyList<ItemDefinition> DeveloperArmoryItems =>
        Items.Definitions.Concat(Items.Uniques).ToList();
    /// <summary>
    /// Synthetic portal key returned to RotBoiGame when the convergence
    /// portal finishes its pull animation. It is not a GamePaths sense key:
    /// the caller starts the ten-floor randomized composite Path instead.
    /// </summary>
    public const string BodyPortalKey = "__body";
    public const string CorePortalKey = "__core";
    public const string AphantasiaPortalKey = "__aphantasia";
    [Obsolete("Use CorePortalKey for the standalone dungeon.")]
    public const string CompositePathPortalKey = CorePortalKey;
    private const float StationOpenRadiusTiles = 1.45f;
    private const float StationCloseRadiusTiles = 1.85f;
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
    private int _overlayFocusIndex;
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
    private double _dummyHitFlash;
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
    private PathFogOfWar? _mindFog;
    private int _devSenseIndex;
    private int Px(float value) => Math.Max(1, (int)MathF.Round(value * _uiScale));
    private double Fs(double value) => value * _uiScale;
    public bool OverlayOpen => _overlay is not null || _confirmingPortalKey is not null;
    /// <summary>True once a portal has been confirmed -- movement and all other Soul interaction are suppressed for the remainder of the animation.</summary>
    public bool IsEnteringPortal => _enteringPortalKey is not null;
    /// <summary>Cosmetic player render scale (see Player.Draw) -- 1 outside the pull-in animation, easing down to PortalMinPlayerScale during it.</summary>
    public float PlayerDrawScale => _playerDrawScale;
    /// <summary>World center of the DPS dummy's hit rect -- exposed so callers (and tests) can place bullets on it without duplicating its layout.</summary>
    public Vector2 DummyWorld => _dummyWorld;
    /// <summary>World center of the always-open standalone dungeon portal.</summary>
    public Vector2 CompositePortalWorld => _pathPortalWorld.GetValueOrDefault(CorePortalKey);
    internal Vector2 StationWorld(string key) => _stationWorld.TryGetValue(key, out Vector2 world)
        ? world
        : SoulLayout.TileWorldCenter(SoulLayout.StationTiles[key]);
    internal Vector2 PortalWorld(string key) => _pathPortalWorld[key];
    public double CurrentDps => _currentDps;
    /// <summary>Whether the training dummy is currently carrying the given status effect (e.g. "bleed", "bane") -- lets tests confirm status effects actually land on it instead of only checking the DPS number they produce.</summary>
    public bool DummyHasStatus(string kind) => _dummy.StatusEffects.ContainsKey(kind);
    internal static AphantasiaTrophyVisual AphantasiaTrophyVisualFor(
        StatueProgress statue)
    {
        if (statue.Rainbow)
            return AphantasiaTrophyVisual.Rainbow;
        bool blood = statue.ChallengeClears.HasFlag(ChallengeClear.NoHealing);
        bool crack = statue.ChallengeClears.HasFlag(ChallengeClear.NoExtract);
        return (blood, crack) switch
        {
            (true, true) => AphantasiaTrophyVisual.BloodyAndCracked,
            (true, false) => AphantasiaTrophyVisual.Bloody,
            (false, true) => AphantasiaTrophyVisual.Cracked,
            _ => AphantasiaTrophyVisual.Normal,
        };
    }

    internal static AphantasiaTrophyMotion AphantasiaTrophyMotionAt(double seconds)
    {
        float time = (float)(seconds % 120.0);
        return new AphantasiaTrophyMotion(
            Hover: MathF.Sin(time * 1.55f) * 4f,
            Pulse: .5f + .5f * MathF.Sin(time * 2.1f),
            OrbitRadians: time * .82f);
    }
    public void CloseOverlay()
    {
        _overlay = null;
        _confirmingPortalKey = null;
        _overlayFocusIndex = 0;
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
        _dummyWorld = SoulLayout.TileWorldCenter(SoulLayout.DummyTile);
        _dummy = new TrainingDummy(_dummyWorld.X, _dummyWorld.Y);
        _stationWorld.Clear();
        foreach (var (key, tile) in SoulLayout.StationTiles)
            if (key != "developer_armory" || GameProfile.Profile.DeveloperArmory)
                _stationWorld[key] = SoulLayout.TileWorldCenter(tile);
        _pathPortalWorld.Clear();
        foreach (var (key, tile) in SoulLayout.PortalTiles)
            _pathPortalWorld[key] = SoulLayout.TileWorldCenter(tile);
        Vector2 nexus = SoulLayout.TileWorldCenter(SoulLayout.NexusTile);
        _pathPortalWorld[CorePortalKey] = nexus;
        _pathPortalWorld[BodyPortalKey] = SoulLayout.TileWorldCenter(SoulLayout.CorePortalTile);
        _pathPortalWorld[AphantasiaPortalKey] = SoulLayout.TileWorldCenter(SoulLayout.AphantasiaPortalTile);
        _dummyHits.Clear();
        _seconds = 0;
        _measurementStart = 0;
        _lastHitTime = -99;
        _currentDps = 0;
        _sessionBest = 0;
        _lastRecordSave = 0;
        _dummyHitFlash = 0;
        _overlay = null;
        _overlayFocusIndex = 0;
        _confirmingPortalKey = null;
        _enteringPortalKey = null;
        _playerDrawScale = 1f;
        _mindFog = new PathFogOfWar(session.Battleground);
        _mindFog.Update(session.PlayerWorldCenter);
        session.InformationSheet.CancelDrag();
        session.ShowEntrySplash("The Mind", "Safe ground, where every path begins as a thought.", UiTheme.Purple);
    }

    public void Update(GameSession session, double elapsedSeconds)
    {
        SyncDeveloperArmoryStation();
        _seconds += Math.Min(.05, elapsedSeconds);
        session.UpdateEntrySplash(elapsedSeconds);
        _dummyHitFlash = Math.Max(0, _dummyHitFlash - elapsedSeconds);
        _mindFog?.Update(session.PlayerWorldCenter);
        if (_enteringPortalKey is not null)
            UpdatePortalTravel(session);
        var dummyRect = new Rectangle((int)(_dummyWorld.X - 34), (int)(_dummyWorld.Y - 44), 68, 88);
        foreach (var bullet in session.State.BulletHolster.Where(bullet => !bullet.RemFlag && dummyRect.Intersects(bullet.WorldRect())).ToArray())
        {
            bullet.RemFlag = true;
            _dummyHitFlash = .13;
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

    public void DrawMindFog(SpriteBatch spriteBatch, GameSession session)
    {
        if (_mindFog is not null)
            session.DrawFogOfWar(spriteBatch, _mindFog);
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
        session.Player.SetAnimatedPosition(center.X - half, center.Y - half);
    }

    /// <summary>
    /// Returns the GamePaths key of a portal whose entry animation just
    /// finished (caller starts a run there), or null. A bare F near a portal
    /// only opens the "ENTER X?" confirmation (see DrawPortalConfirm); a
    /// second F commits, kicking off the pull-in/fade animation that this
    /// method later reports as complete once IsEnteringPortal's clock runs out.
    /// </summary>
    public string? HandleInput(GameSession session, IReadOnlySet<Keys> keysPressed, Point mouse, bool mouseDown, bool mousePressed)
    {
        if (keysPressed.Contains(Keys.F8))
        {
            ToggleDevUnlockTesting(session);
            return null;
        }
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
            if (InputState.ControllerBackPressed)
            {
                _confirmingPortalKey = null;
                return null;
            }
            bool lowerTier = keysPressed.Contains(Keys.Left) || keysPressed.Contains(Keys.A)
                || InputState.UiLeftPressed
                || (mousePressed && _ngMinusRect.Contains(mouse));
            bool higherTier = keysPressed.Contains(Keys.Right) || keysPressed.Contains(Keys.D)
                || InputState.UiRightPressed
                || (mousePressed && _ngPlusRect.Contains(mouse));
            if (_confirmingPortalKey is not (AphantasiaPortalKey or BodyPortalKey))
            {
                string newGamePlusKey = NewGamePlusKey(_confirmingPortalKey);
                if (lowerTier) AdjustNewGamePlus(newGamePlusKey, -1);
                if (higherTier) AdjustNewGamePlus(newGamePlusKey, 1);
            }
            if (keysPressed.Contains(Keys.F) || InputState.ControllerConfirmPressed)
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
            if (_overlay == "storage")
            {
                bool handled = session.HandleLoadoutNavigation(keysPressed,
                    _vaultSlotRects, dossier: false);
                if (handled)
                    return null;
                if (InputState.ControllerBackPressed)
                {
                    _overlay = null;
                    session.InformationSheet.CancelDrag();
                    return null;
                }
            }
            bool walkedAway = !_stationWorld.TryGetValue(_overlay, out var station)
                || !WithinStationRadius(session.PlayerWorldCenter, station, StationCloseRadiusTiles);
            if (keysPressed.Contains(Keys.F) || InputState.ControllerBackPressed || walkedAway)
            {
                _overlay = null;
                _overlayFocusIndex = 0;
                session.InformationSheet.CancelDrag();
                return null;
            }
            if (_overlay != "storage")
                HandleOverlayControllerInput(session);
        }
        else if (keysPressed.Contains(Keys.F) || InputState.ControllerInteractPressed)
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
                else if (nearby == "no_extract")
                    ToggleNoExtract(session);
                else
                {
                    _overlay = nearby;
                    _overlayFocusIndex = 0;
                }
            }
        }
        if (_overlay == "storage")
        {
            session.HandleCarriedLoadoutDrag(mouse, mouseDown, mousePressed,
                _vaultSlotRects);
        }
        if (!mousePressed)
            return null;
        string? clickedTarget = ClickTargetAt(_targets, mouse,
            overlayOpen: _overlay is not null);
        if (clickedTarget is not null)
            ActivateTarget(session, clickedTarget);
        return null;
    }

    internal static string? ClickTargetAt(
        IEnumerable<KeyValuePair<string, Rectangle>> targets,
        Point mouse,
        bool overlayOpen)
    {
        foreach ((string key, Rectangle rect) in targets)
        {
            if (overlayOpen && !IsOverlayTarget(key))
                continue;
            if (rect.Contains(mouse))
                return key;
        }
        return null;
    }

    private static bool IsOverlayTarget(string key) =>
        key.StartsWith("skill:", StringComparison.Ordinal)
        || key.StartsWith("cosmetic:", StringComparison.Ordinal)
        || key.StartsWith("armory:", StringComparison.Ordinal);

    private void HandleOverlayControllerInput(GameSession session)
    {
        List<string> targets = OrderedOverlayTargets();
        if (targets.Count == 0)
            return;
        if (InputState.UiUpPressed || InputState.UiLeftPressed)
            _overlayFocusIndex--;
        if (InputState.UiDownPressed || InputState.UiRightPressed)
            _overlayFocusIndex++;
        _overlayFocusIndex = (_overlayFocusIndex % targets.Count + targets.Count) % targets.Count;
        if (InputState.ControllerConfirmPressed)
            ActivateTarget(session, targets[_overlayFocusIndex]);
    }

    private List<string> OrderedOverlayTargets() => _targets
        .Where(pair => IsOverlayTarget(pair.Key))
        .OrderBy(pair => pair.Value.Y)
        .ThenBy(pair => pair.Value.X)
        .Select(pair => pair.Key)
        .ToList();

    private void ActivateTarget(GameSession session, string key)
    {
        if (key.StartsWith("skill:"))
            MetaProgression.PurchaseSkill(key[6..]);
        else if (key.StartsWith("cosmetic:"))
        {
            string[] parts = key.Split(':');
            if (parts.Length == 3 && Cosmetics.Select(parts[1], parts[2]))
                session.State.ApplyCosmetics();
        }
        else if (key.StartsWith("dev:"))
            HandleDevAction(session, key[4..]);
        else if (TryArmoryIndex(key, out int armoryIndex))
            TakeArmoryItem(session, armoryIndex);
    }

    public static void ToggleHardMode(GameSession session)
    {
        bool enabled = !GameProfile.Profile.NoHealingEnabled;
        GameProfile.Profile.NoHealingEnabled = enabled;
        session.State.SetHardMode(enabled);
        GameProfile.SaveProfile();
    }

    private void SyncDeveloperArmoryStation()
    {
        if (GameProfile.Profile.DeveloperArmory)
            _stationWorld["developer_armory"] = SoulLayout.TileWorldCenter(
                SoulLayout.StationTiles["developer_armory"]);
        else
        {
            _stationWorld.Remove("developer_armory");
            if (_overlay == "developer_armory") _overlay = null;
        }
    }

    internal static bool TakeArmoryItem(GameSession session, int index)
    {
        if (!GameProfile.Profile.DeveloperArmory || index < 0 || index >= DeveloperArmoryItems.Count)
            return false;
        int slot = session.State.Inventory.FindIndex(item => item is null);
        if (slot < 0) return false;
        session.State.Inventory[slot] = Items.DeveloperArmoryDrop(DeveloperArmoryItems[index]);
        GameProfile.IncrementQuest("items_found");
        return true;
    }

    internal static bool TryArmoryIndex(string target, out int index)
    {
        const string prefix = "armory:";
        index = -1;
        return target.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(target.AsSpan(prefix.Length), out index)
            && index >= 0;
    }

    public static void ToggleNoExtract(GameSession session)
    {
        bool enabled = !GameProfile.Profile.NoExtractEnabled;
        GameProfile.Profile.NoExtractEnabled = enabled;
        session.State.SetNoExtract(enabled);
        GameProfile.SaveProfile();
    }

    public static bool AdjustNewGamePlus(string pathKey, int direction) =>
        NewGamePlus.AdjustSelection(pathKey, direction);

    private void ToggleDevUnlockTesting(GameSession session)
    {
        GameProfile.Profile.DevUnlockTesting = !GameProfile.Profile.DevUnlockTesting;
        GameProfile.SaveProfile();
        RebuildMind(session);
    }

    private void RebuildMind(GameSession session)
    {
        session.RefreshMindBattleground(Battleground.GenerateMind());
        Enter(session);
    }

    private static string NewGamePlusKey(string portalKey) =>
        portalKey == CorePortalKey ? NewGamePlus.DungeonKey : portalKey;

    private string? NearbyStation(GameSession session) => _stationWorld
        .Where(station => WithinStationRadius(session.PlayerWorldCenter, station.Value, StationOpenRadiusTiles))
        .OrderBy(station => Vector2.DistanceSquared(station.Value, session.PlayerWorldCenter))
        .Select(station => station.Key)
        .FirstOrDefault();

    private string? NearbyPathPortal(GameSession session) => _pathPortalWorld
        .Where(portal => PortalAvailable(portal.Key))
        .Where(portal => WithinStationRadius(session.PlayerWorldCenter, portal.Value, PathPortalInteractRadiusTiles))
        .OrderBy(portal => Vector2.DistanceSquared(portal.Value, session.PlayerWorldCenter))
        .Select(portal => portal.Key)
        .FirstOrDefault();

    private static bool PortalAvailable(string key) => key switch
    {
        BodyPortalKey => CampaignProgression.PortalUnlocked("body"),
        CorePortalKey => true,
        AphantasiaPortalKey => CampaignProgression.PortalUnlocked("aphantasia"),
        _ => CampaignProgression.PortalUnlocked(key),
    };

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
        float visualIntensity = (float)GameProfile.Profile.VisualEffectsIntensity;
        SoulVisualRenderer.DrawEnvironment(
            spriteBatch, session, (float)_seconds, visualIntensity, _pathPortalWorld);
        DrawMindProgressionTentacles(spriteBatch, session, (float)_seconds, visualIntensity);
        var screen = session.Camera.WorldToScreen(_dummyWorld, session.PlayerWorldCenter, Vector2.Zero);
        Color effigyBody = _dummyHitFlash > 0 ? UiTheme.Cream : new Color(64, 50, 68);
        var body = new Rectangle((int)screen.X - 25, (int)screen.Y - 22, 50, 69);
        var plinth = new Rectangle((int)screen.X - 38, (int)screen.Y + 43, 76, 18);
        Primitives2D.FillRect(spriteBatch, new Rectangle(plinth.X + 6, plinth.Y + 7, plinth.Width, plinth.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, plinth, new Color(39, 32, 48));
        Primitives2D.RectOutline(spriteBatch, plinth, UiTheme.Red * .72f, 3);
        Primitives2D.FillRect(spriteBatch, new Rectangle(body.X + 6, body.Y + 8, body.Width, body.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, body, effigyBody);
        Primitives2D.RectOutline(spriteBatch, body, UiTheme.Red, 4);
        Primitives2D.Line(spriteBatch, new Vector2(screen.X - 48, screen.Y - 6),
            new Vector2(screen.X + 48, screen.Y - 6), UiTheme.Red * .72f, 9);
        Primitives2D.FillPolygon(spriteBatch, new[]
        {
            new Vector2(screen.X, screen.Y - 62), new Vector2(screen.X + 20, screen.Y - 43),
            new Vector2(screen.X + 14, screen.Y - 25), new Vector2(screen.X - 14, screen.Y - 25),
            new Vector2(screen.X - 20, screen.Y - 43),
        }, effigyBody);
        Primitives2D.PolygonOutline(spriteBatch, new[]
        {
            new Vector2(screen.X, screen.Y - 62), new Vector2(screen.X + 20, screen.Y - 43),
            new Vector2(screen.X + 14, screen.Y - 25), new Vector2(screen.X - 14, screen.Y - 25),
            new Vector2(screen.X - 20, screen.Y - 43),
        }, UiTheme.Red, 4);
        Primitives2D.RectOutline(spriteBatch,
            new Rectangle((int)screen.X - 13, (int)screen.Y - 9, 26, 26), UiTheme.Cream * .75f, 3);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)screen.X - 4, (int)screen.Y, 8, 8), UiTheme.Red);

        int sideX = screen.X < session.ScreenWidth * .68f ? (int)screen.X + 58 : (int)screen.X - 268;
        var readout = new Rectangle(sideX, (int)screen.Y - 68, 210, 128);
        UiTheme.DrawPanel(spriteBatch, readout, UiTheme.Panel, UiTheme.Red, shadow: 5);
        UiTheme.DrawText(spriteBatch, "THE EFFIGY REMEMBERS", Fs(10), UiTheme.Muted, new Vector2(readout.X + 12, readout.Y + 10));
        UiTheme.DrawText(spriteBatch, $"{_currentDps:0}", Fs(34), UiTheme.Text, new Vector2(readout.X + 12, readout.Y + 31));
        UiTheme.DrawText(spriteBatch, "DAMAGE PER SECOND", Fs(9), UiTheme.Red, new Vector2(readout.X + 14, readout.Y + 74));
        UiTheme.DrawText(spriteBatch, $"SESSION {_sessionBest:0}  //  RECORD {GameProfile.Profile.BestDummyDps:0}", Fs(8), UiTheme.Cream,
            new Vector2(readout.X + 14, readout.Y + 98));
        SoulVisualRenderer.DrawStations(
            spriteBatch, session, (float)_seconds, _stationWorld,
            NearbyStation(session), _overlay);
        DrawCompositePathPortal(spriteBatch, session, NearbyPathPortal(session),
            (float)_seconds);
        SoulVisualRenderer.DrawPortals(
            spriteBatch, session, (float)_seconds, _pathPortalWorld,
            NearbyPathPortal(session), _confirmingPortalKey, _enteringPortalKey,
            _portalAnimationStart);
        DrawCampaignGatePortals(spriteBatch, session, NearbyPathPortal(session),
            (float)_seconds);
        // Statues belong to the world layer so unexplored, wall-sealed wings
        // remain genuinely concealed by The Mind's fog of war.
        DrawCampaignStatues(spriteBatch, session);
    }

    /// <summary>
    /// Permanent campaign progress is written into The Mind itself. Silver
    /// arena clears grow one living conduit toward the Body / Soul door;
    /// gold Soul clears strengthen that sense with three faster, broader
    /// strands. Once every gold clear exists, a void braid opens leftward to
    /// Aphantasia and its mouth flowers into a rotating rainbow corona.
    /// </summary>
    /// <summary>
    /// Portal decoration shares one technique across the whole hub now
    /// (<see cref="Primitives2D.DrawTentacleSpike"/>, the same wiggling
    /// spike the Aphantasia portal uses at full scale): every sense portal
    /// gets its own small radiating cluster once its progression statue
    /// unlocks it, tuned deliberately duller than Aphantasia's -- fewer
    /// segments, the sense's own fixed accent color instead of the rainbow
    /// cycle, and no trailing after-image echoes -- so Aphantasia stays the
    /// most visually striking portal in the hub and the others read as
    /// simpler cousins of the same idea rather than a competing style.
    /// The old strands connecting the Core portal to Aphantasia are gone
    /// entirely: DrawAphantasiaPortal already draws a full tentacle display
    /// at that exact spot, so they were purely redundant.
    /// </summary>
    private void DrawMindProgressionTentacles(SpriteBatch spriteBatch,
        GameSession session, float time, float intensity)
    {
        // These communicate permanent progression, so their authored
        // silhouettes remain readable even at the minimum optional-VFX level.
        float effects = .24f + Math.Clamp(intensity, 0f, 1f) * .76f;
        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            GamePath path = GamePaths.Paths[index];
            if (!_pathPortalWorld.TryGetValue(path.Key, out Vector2 portal))
                continue;
            if (!EffectiveStatue(path.Key, StatueMaterial.Silver).Unlocked)
                continue;

            bool gold = EffectiveStatue(path.Key, StatueMaterial.Gold).Unlocked;
            Vector2 screen = WorldToScreen(portal, session);
            Color themeColor = path.Accent * effects;
            int spikeCount = gold ? 7 : 4;
            float targetLength = Simulation.TileSize * (gold ? 2.2f : 1.5f);
            float spin = time * .18f * (index % 2 == 0 ? 1f : -1f);
            for (int spike = 0; spike < spikeCount; spike++)
            {
                float baseAngle = spike * MathF.Tau / spikeCount + spin;
                float length = targetLength
                    * (.85f + .15f * MathF.Sin(time * 1.1f + spike + index));
                Primitives2D.DrawTentacleSpike(spriteBatch, screen, baseAngle, length,
                    targetLength * .09f, phase: spike * 1.7f + index,
                    colorPhase: 0f, time, segments: 14, themeColor: themeColor);
            }
        }

        if (!CampaignProgression.PortalUnlocked("aphantasia"))
            return;
        Vector2 aphantasia = SoulLayout.TileWorldCenter(SoulLayout.AphantasiaPortalTile);
        DrawAphantasiaRainbowCorona(spriteBatch, session, aphantasia, time, effects);
    }

    private static void DrawAphantasiaRainbowCorona(SpriteBatch spriteBatch,
        GameSession session, Vector2 world, float time, float intensity)
    {
        Vector2 center = WorldToScreen(world, session);
        const int arms = 12;
        float baseRadius = Simulation.TileSize * 1.05f;
        for (int arm = 0; arm < arms; arm++)
        {
            float angle = time * (arm % 2 == 0 ? .72f : -.54f)
                + arm * MathF.Tau / arms;
            float pulse = .78f + .22f * MathF.Sin(time * 5.4f + arm * .91f);
            Color color = RainbowColor(arm / (float)arms + time * .08f);
            Vector2 inner = center + Direction(angle) * baseRadius * .48f;
            Vector2 bend = center + Direction(angle + .42f) * baseRadius * 1.22f * pulse;
            Vector2 outer = center + Direction(angle + .18f) * baseRadius * 1.85f * pulse;
            Vector2 previous = inner;
            for (int segment = 1; segment <= 8; segment++)
            {
                float amount = segment / 8f;
                Vector2 next = Quadratic(inner, bend, outer, amount);
                Primitives2D.Line(spriteBatch, previous + new Vector2(0, 3),
                    next + new Vector2(0, 3), UiTheme.Shadow * (.6f * intensity), 9);
                Primitives2D.Line(spriteBatch, previous, next,
                    color * ((.58f + pulse * .36f) * intensity),
                    Math.Max(2, 7 - segment / 2));
                previous = next;
            }
        }
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
        Vector2 junction = SoulLayout.TileWorldCenter(SoulLayout.NexusTile);
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

        // The tunnel opens into a constructed convergence dais, then
        // separates into five authored colored paths. The dais supports the
        // composite portal drawn later in DrawPathPortals, so the randomized
        // run is physically made from the five senses instead of reading as
        // an unrelated sixth door.
        Vector2 junctionScreen = WorldToScreen(junction, session);
        DrawConvergenceDais(spriteBatch, session, junction, junctionScreen, pathColors, t);

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

    private void DrawConvergenceDais(
        SpriteBatch spriteBatch,
        GameSession session,
        Vector2 junction,
        Vector2 junctionScreen,
        IReadOnlyList<Color> pathColors,
        float time)
    {
        float tile = Simulation.TileSize;
        var shadow = new Rectangle(
            (int)(junctionScreen.X - tile * 2.25f),
            (int)(junctionScreen.Y - tile * .92f + 12),
            (int)(tile * 4.5f),
            (int)(tile * 1.84f));
        Primitives2D.FillEllipse(spriteBatch, shadow, UiTheme.Shadow * .72f);

        for (int ring = 0; ring < 5; ring++)
        {
            float radius = tile * (.52f + ring * .29f
                + .025f * MathF.Sin(time * 1.7f + ring));
            Color color = ring is 0 or 4
                ? Color.Lerp(UiTheme.Purple, UiTheme.Gold, ring / 4f)
                : new Color(119, 95, 145);
            Primitives2D.CircleOutline(spriteBatch, junctionScreen, radius,
                color * (.68f - ring * .055f), ring is 0 or 4 ? 3 : 2);
        }

        // Five inset spokes and paired threshold stones visibly continue into
        // the matching portal trails. Their actual portal positions determine
        // the direction, so the composition remains correct if the bay arc is
        // adjusted later.
        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            var path = GamePaths.Paths[index];
            if (!_pathPortalWorld.TryGetValue(path.Key, out var portal))
                continue;
            Vector2 direction = Vector2.Normalize(portal - junction);
            Vector2 innerWorld = junction + direction * tile * .78f;
            Vector2 outerWorld = junction + direction * tile * 1.72f;
            Vector2 inner = WorldToScreen(innerWorld, session);
            Vector2 outer = WorldToScreen(outerWorld, session);
            Color bright = Color.Lerp(pathColors[index], Color.White, .3f);
            Primitives2D.Line(spriteBatch, inner + new Vector2(0, 5),
                outer + new Vector2(0, 5), UiTheme.Shadow * .7f, 10);
            Primitives2D.Line(spriteBatch, inner, outer,
                pathColors[index] * (.58f + .16f * MathF.Sin(time * 2f + index)), 5);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)outer.X - 6, (int)outer.Y - 6, 12, 12),
                new Color(31, 27, 41));
            Primitives2D.RectOutline(spriteBatch,
                new Rectangle((int)outer.X - 6, (int)outer.Y - 6, 12, 12),
                bright * .8f, 2);
            DrawPixelReflection(spriteBatch, outer + new Vector2(0, 8),
                pathColors[index], 16, .2f);
        }

        // Gold cardinal ticks make the dais read as old Soul architecture,
        // while the five colored spokes read as the newer paths growing out.
        for (int tick = 0; tick < 12; tick++)
        {
            float angle = tick * MathF.Tau / 12f;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 a = junctionScreen + direction * tile * 1.93f;
            Vector2 b = junctionScreen + direction * tile * (tick % 3 == 0 ? 2.12f : 2.04f);
            Primitives2D.Line(spriteBatch, a, b,
                UiTheme.Gold * (tick % 3 == 0 ? .72f : .42f), tick % 3 == 0 ? 3 : 2);
        }
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
        UiTheme.DrawText(spriteBatch, "THE MIND", Fs(27), UiTheme.Text, new Vector2(Px(22), Px(18)));
        UiTheme.DrawText(spriteBatch,
            $"SAFE GROUND  //  NO HEALING {(GameProfile.Profile.NoHealingEnabled ? "ON" : "OFF")}  //  NO EXTRACT {(GameProfile.Profile.NoExtractEnabled ? "ON" : "OFF")}  //  F / B INTERACT  //  ESC / START OPTIONS",
            Fs(9), UiTheme.Muted, new Vector2(Px(24), Px(54)));
        DrawNearbyPrompt(spriteBatch, session);
        if (GameProfile.Profile.DevUnlockTesting)
        {
            DrawDevTestingToggle(spriteBatch, session, mouse, mouseDown);
            DrawDevUnlockControls(spriteBatch, session, mouse, mouseDown);
        }
        if (_overlay is not null)
        {
            DrawOverlay(spriteBatch, session, mouse);
            DrawOverlayControllerFocus(spriteBatch);
        }
        if (_confirmingPortalKey is not null) DrawPortalConfirm(spriteBatch, session, mouse, mouseDown);
        if (_overlay is null && _confirmingPortalKey is null)
            session.DrawSoulFooter(spriteBatch, mouse, (float)_seconds);
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
            ["skills"] = ("MIND GRID", UiTheme.Purple),
            ["wardrobe"] = ("WARDROBE", UiTheme.Blue),
            ["hard_mode"] = (GameProfile.Profile.NoHealingEnabled ? "NO HEALING ON" : "NO HEALING OFF",
                GameProfile.Profile.NoHealingEnabled ? UiTheme.Red : UiTheme.Muted),
            ["no_extract"] = (GameProfile.Profile.NoExtractEnabled ? "NO EXTRACT ON" : "NO EXTRACT OFF",
                GameProfile.Profile.NoExtractEnabled ? UiTheme.Purple : UiTheme.Muted),
        };
        foreach (var (key, world) in _stationWorld)
        {
            var position = session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
            var (label, accent) = labels[key];
            int width = key == "hard_mode" ? 88 : 56;
            var baseRect = new Rectangle((int)position.X - width / 2, (int)position.Y - 24, width, 48);
            if (key == "hard_mode" && GameProfile.Profile.NoHealingEnabled)
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
            UiTheme.DrawText(spriteBatch, label, Fs(8), accent, new Vector2(position.X, baseRect.Bottom + 7), "midtop");
        }
    }

    private void DrawNearbyPrompt(SpriteBatch spriteBatch, GameSession session)
    {
        var labels = new Dictionary<string, (string Label, Color Accent)>
        {
            ["storage"] = ("VAULT RELIQUARY", UiTheme.Gold), ["quests"] = ("VOW LECTERN", UiTheme.Green),
            ["skills"] = ("MIND ROSE", UiTheme.Purple), ["wardrobe"] = ("VESTMENT MIRROR", UiTheme.Blue),
            ["hard_mode"] = (GameProfile.Profile.NoHealingEnabled ? "EXTINGUISH NO HEALING" : "LIGHT NO HEALING",
                GameProfile.Profile.NoHealingEnabled ? UiTheme.Red : UiTheme.Gold),
            ["no_extract"] = (GameProfile.Profile.NoExtractEnabled ? "EXTINGUISH NO EXTRACT" : "LIGHT NO EXTRACT",
                GameProfile.Profile.NoExtractEnabled ? UiTheme.Purple : UiTheme.Gold),
            ["developer_armory"] = ("DEVELOPER ARMORY", UiTheme.Gold),
        };
        var nearby = NearbyStation(session);
        if (nearby is not null)
            UiTheme.DrawText(spriteBatch, $"F / B  //  OPEN {labels[nearby].Label}", Fs(13), labels[nearby].Accent,
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
        DrawCompositePathPortal(spriteBatch, session, nearbyPortal, t);
        DrawCampaignGatePortals(spriteBatch, session, nearbyPortal, t);
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
            UiTheme.DrawText(spriteBatch, path.Title, Fs(10), path.Accent, new Vector2(screen.X, screen.Y + radius + 8), "midtop");
            int unlockedNg = NewGamePlus.UnlockedLevel(path.Key);
            string ngLabel = unlockedNg == 0
                ? "NORMAL  //  COMPLETE TO UNLOCK NG+"
                : selectedNg == 0 ? $"NORMAL  //  NG+{unlockedNg} UNLOCKED" : $"NG+{selectedNg}  //  MAX {unlockedNg}";
            UiTheme.DrawText(spriteBatch, ngLabel,
                Fs(8), selectedNg == 0 ? UiTheme.Muted : UiTheme.Gold,
                new Vector2(screen.X, screen.Y + radius + 25), "midtop");
            // Suppressed while confirming/entering that same portal -- the center
            // confirmation panel (DrawPortalConfirm) already explains the prompt.
            bool unlocked = CampaignProgression.PortalUnlocked(path.Key);
            if (!unlocked)
                UiTheme.DrawText(spriteBatch, "SEALED", Fs(9), UiTheme.Red,
                    new Vector2(screen.X, screen.Y + radius + 42), "midtop");
            else if (path.Key == nearbyPortal && path.Key != _confirmingPortalKey && _enteringPortalKey is null)
                UiTheme.DrawText(spriteBatch, "F / B  //  ENTER", Fs(9), UiTheme.Cream, new Vector2(screen.X, screen.Y + radius + 42), "midtop");
        }
    }

    private void DrawOverlayControllerFocus(SpriteBatch spriteBatch)
    {
        List<string> targets = OrderedOverlayTargets();
        if (targets.Count == 0)
            return;
        _overlayFocusIndex = Math.Clamp(_overlayFocusIndex, 0, targets.Count - 1);
        Rectangle target = _targets[targets[_overlayFocusIndex]];
        target.Inflate(Px(3), Px(3));
        Primitives2D.RectOutline(spriteBatch, target, UiTheme.Cream, Math.Max(2, Px(2)));
    }

    private void DrawCampaignStatues(SpriteBatch spriteBatch, GameSession session)
    {
        foreach (string sense in CampaignProgression.SenseKeys)
        {
            if (!_pathPortalWorld.TryGetValue(sense, out Vector2 portal))
                continue;
            StatueProgress silver = EffectiveStatue(sense, StatueMaterial.Silver);
            if (silver.Unlocked)
                DrawStatue(spriteBatch, session, portal + new Vector2(Simulation.TileSize * 2f, 0),
                    silver, new Color(175, 184, 196), sense);
            StatueProgress gold = EffectiveStatue(sense, StatueMaterial.Gold);
            if (gold.Unlocked)
                DrawStatue(spriteBatch, session, portal - new Vector2(Simulation.TileSize * 2f, 0),
                    gold, UiTheme.Gold, sense);
        }
        StatueProgress aphantasia = EffectiveAphantasiaStatue();
        if (aphantasia.Unlocked)
            DrawAphantasiaStatue(spriteBatch, session, aphantasia);
    }

    private static StatueProgress EffectiveAphantasiaStatue()
    {
        if (GameProfile.Profile.DevUnlockTesting
            && CampaignDevOverrides.AphantasiaStatue is ChallengeClear clear)
        {
            return new StatueProgress
            {
                Unlocked = true,
                ChallengeClears = clear,
            };
        }
        return CampaignProgression.Data.AphantasiaStatue;
    }

    private static StatueProgress EffectiveStatue(string sense, StatueMaterial material)
    {
        StatueProgress saved = (material == StatueMaterial.Silver
            ? CampaignProgression.Data.SilverStatues : CampaignProgression.Data.GoldStatues)[sense];
        Dictionary<string, ChallengeClear> overrides = material == StatueMaterial.Silver
            ? CampaignDevOverrides.SilverStatues : CampaignDevOverrides.GoldStatues;
        return GameProfile.Profile.DevUnlockTesting && overrides.TryGetValue(sense, out ChallengeClear clear)
            ? new StatueProgress { Unlocked = true, ChallengeClears = clear }
            : saved;
    }

    private void DrawStatue(SpriteBatch spriteBatch, GameSession session,
        Vector2 world, StatueProgress statue, Color material, string sense)
    {
        Vector2 at = session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
        bool blood = statue.ChallengeClears.HasFlag(ChallengeClear.NoHealing) || statue.Rainbow;
        bool crack = statue.ChallengeClears.HasFlag(ChallengeClear.NoExtract);
        if (blood)
            Primitives2D.FillEllipse(spriteBatch,
                new Rectangle((int)at.X - 29, (int)at.Y + 15, 58, 18), new Color(94, 8, 18) * .82f);
        if (statue.Rainbow)
        {
            for (int piece = 0; piece < 6; piece++)
            {
                int x = (int)at.X - 22 + piece * 8;
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle(x, (int)at.Y + 5 + piece % 2 * 6, 11, 10), material * .72f);
            }
            float hover = MathF.Sin((float)_seconds * 1.5f) * 5f;
            Color rainbow = RainbowColor((float)_seconds * .18f);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 10, (int)(at.Y - 31 + hover), 20, 20), rainbow);
            Primitives2D.RectOutline(spriteBatch,
                new Rectangle((int)at.X - 10, (int)(at.Y - 31 + hover), 20, 20), UiTheme.Cream, 2);
            return;
        }
        Primitives2D.FillRect(spriteBatch, new Rectangle((int)at.X - 23, (int)at.Y + 15, 46, 10), material * .68f);
        Primitives2D.FillRect(spriteBatch, new Rectangle((int)at.X - 12, (int)at.Y - 20, 24, 38), material);
        Primitives2D.FillCircle(spriteBatch, at + new Vector2(0, -25), 12, material);
        // Each guardian keeps a recognizable crown silhouette even when VFX
        // are disabled; the plinth remains silver/gold to communicate tier.
        int crest = Array.IndexOf(CampaignProgression.SenseKeys, sense);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)at.X - 14 + crest % 3 * 3, (int)at.Y - 43,
                7 + crest % 2 * 4, 10 + crest % 3 * 2), material * .9f);
        if (blood)
        {
            Primitives2D.FillCircle(spriteBatch, at + new Vector2(-6, -8), 5, new Color(110, 7, 17));
            Primitives2D.FillCircle(spriteBatch, at + new Vector2(7, 5), 4, new Color(110, 7, 17));
        }
        if (crack)
        {
            Color rainbow = RainbowColor((float)_seconds * .2f);
            Primitives2D.Line(spriteBatch, at + new Vector2(-8, -31), at + new Vector2(5, -12), rainbow, 4);
            Primitives2D.Line(spriteBatch, at + new Vector2(5, -12), at + new Vector2(-4, 15), rainbow, 4);
        }
    }

    private void DrawAphantasiaStatue(SpriteBatch spriteBatch,
        GameSession session, StatueProgress statue)
    {
        Vector2 world = SoulLayout.TileWorldCenter(SoulLayout.AphantasiaStatueTile);
        Vector2 at = session.Camera.WorldToScreen(
            world, session.PlayerWorldCenter, Vector2.Zero);
        AphantasiaTrophyVisual visual = AphantasiaTrophyVisualFor(statue);
        AphantasiaTrophyMotion motion = AphantasiaTrophyMotionAt(_seconds);
        bool blood = visual is AphantasiaTrophyVisual.Bloody
            or AphantasiaTrophyVisual.BloodyAndCracked
            or AphantasiaTrophyVisual.Rainbow;
        bool cracked = visual is AphantasiaTrophyVisual.Cracked
            or AphantasiaTrophyVisual.BloodyAndCracked
            or AphantasiaTrophyVisual.Rainbow;
        bool rainbow = visual == AphantasiaTrophyVisual.Rainbow;
        Color accent = rainbow
            ? RainbowColor((float)_seconds * .11f)
            : Color.Lerp(UiTheme.Purple, UiTheme.Cream, .2f + motion.Pulse * .22f);
        Color dark = new(26, 17, 39);

        // The floor treatment remains visible at 0% VFX: this is permanent
        // progression communication, not optional spectacle.
        Primitives2D.FillEllipse(spriteBatch,
            new Rectangle((int)at.X - 58, (int)at.Y + 25, 116, 28),
            UiTheme.Shadow * .86f);
        if (blood)
        {
            Color bloodColor = new Color(104, 7, 20) * (.72f + motion.Pulse * .16f);
            Primitives2D.FillEllipse(spriteBatch,
                new Rectangle((int)at.X - 47, (int)at.Y + 20, 94, 25), bloodColor);
            Primitives2D.FillCircle(spriteBatch,
                at + new Vector2(-31, 35), 7 + motion.Pulse * 2f, bloodColor);
            Primitives2D.FillCircle(spriteBatch,
                at + new Vector2(37, 38), 5 + motion.Pulse, bloodColor);
        }

        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)at.X - 42, (int)at.Y + 20, 84, 13), new Color(38, 31, 48));
        Primitives2D.RectOutline(spriteBatch,
            new Rectangle((int)at.X - 42, (int)at.Y + 20, 84, 13), accent, 3);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)at.X - 29, (int)at.Y + 8, 58, 13), dark);
        Primitives2D.RectOutline(spriteBatch,
            new Rectangle((int)at.X - 29, (int)at.Y + 8, 58, 13), UiTheme.Gold * .72f, 2);

        Vector2 core = at + new Vector2(0, -27 + motion.Hover);
        float orbitRadiusX = 41f;
        float orbitRadiusY = 14f;
        var halo = new Rectangle((int)core.X - 51, (int)core.Y - 36, 102, 72);
        Primitives2D.Arc(spriteBatch, halo, motion.OrbitRadians,
            motion.OrbitRadians + MathF.PI * 1.25f, accent * .78f, 3);
        Primitives2D.Arc(spriteBatch, halo, motion.OrbitRadians + MathF.PI,
            motion.OrbitRadians + MathF.PI * 1.65f,
            (rainbow ? RainbowColor((float)_seconds * .11f + .42f) : UiTheme.Gold) * .66f, 2);

        Vector2 lightMini = core + new Vector2(
            MathF.Cos(motion.OrbitRadians) * orbitRadiusX,
            MathF.Sin(motion.OrbitRadians) * orbitRadiusY);
        Vector2 darkMini = core + new Vector2(
            MathF.Cos(motion.OrbitRadians + MathF.PI) * orbitRadiusX,
            MathF.Sin(motion.OrbitRadians + MathF.PI) * orbitRadiusY);
        Primitives2D.FillCircle(spriteBatch, lightMini, 7, UiTheme.Cream);
        Primitives2D.CircleOutline(spriteBatch, lightMini, 9, accent, 2);
        Primitives2D.FillCircle(spriteBatch, darkMini, 7, new Color(5, 4, 10));
        Primitives2D.CircleOutline(spriteBatch, darkMini, 9,
            rainbow ? RainbowColor((float)_seconds * .11f + .66f) : UiTheme.Purple, 2);

        Vector2[] diamond =
        [
            core + new Vector2(0, -27),
            core + new Vector2(24, 0),
            core + new Vector2(0, 28),
            core + new Vector2(-24, 0),
        ];
        Primitives2D.FillPolygon(spriteBatch, diamond,
            rainbow ? accent * .84f : dark);
        Primitives2D.PolygonOutline(spriteBatch, diamond, accent, 4);
        var inner = new Rectangle((int)core.X - 9, (int)core.Y - 9, 18, 18);
        Primitives2D.FillRect(spriteBatch, inner,
            rainbow ? RainbowColor((float)_seconds * .17f + .2f) : UiTheme.Purple * .78f);
        Primitives2D.RectOutline(spriteBatch, inner, UiTheme.Cream * .9f, 2);

        if (blood)
        {
            Color drip = new(126, 8, 23);
            Primitives2D.Line(spriteBatch, core + new Vector2(-13, 2),
                core + new Vector2(-10, 19 + motion.Pulse * 4), drip, 4);
            Primitives2D.FillCircle(spriteBatch,
                core + new Vector2(-10, 21 + motion.Pulse * 4), 3, drip);
        }
        if (cracked)
        {
            Color fracture = rainbow
                ? RainbowColor((float)_seconds * .23f + .73f)
                : new Color(205, 126, 245);
            Primitives2D.Line(spriteBatch, core + new Vector2(-3, -25),
                core + new Vector2(5, -8), fracture, 3);
            Primitives2D.Line(spriteBatch, core + new Vector2(5, -8),
                core + new Vector2(-7, 5), fracture, 3);
            Primitives2D.Line(spriteBatch, core + new Vector2(-7, 5),
                core + new Vector2(4, 26), fracture, 3);
            Primitives2D.Line(spriteBatch, core + new Vector2(-7, 5),
                core + new Vector2(-19, 11), fracture, 2);
        }

        int motes = SoulVisualRenderer.OptionalEffectCount(10,
            (float)GameProfile.Profile.VisualEffectsIntensity);
        for (int index = 0; index < motes; index++)
        {
            float phase = motion.OrbitRadians * (.45f + index % 3 * .11f)
                + index * MathF.Tau / Math.Max(1, motes);
            float radius = 38 + index % 4 * 9;
            Vector2 mote = core + new Vector2(
                MathF.Cos(phase) * radius,
                MathF.Sin(phase * 1.37f) * (18 + index % 3 * 5));
            Color moteColor = rainbow
                ? RainbowColor(index / (float)Math.Max(1, motes) + (float)_seconds * .05f)
                : index % 2 == 0 ? UiTheme.Purple : UiTheme.Gold;
            int size = 2 + index % 2;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)mote.X, (int)mote.Y, size, size), moteColor * .78f);
        }

        UiTheme.DrawText(spriteBatch, "APHANTASIA REMEMBERED", Fs(7),
            rainbow ? accent : UiTheme.Muted,
            new Vector2(at.X, at.Y + 48), "midtop");
    }

    private static Color RainbowColor(float phase) => new(
        .5f + .5f * MathF.Sin(phase * MathF.Tau),
        .5f + .5f * MathF.Sin(phase * MathF.Tau + MathF.Tau / 3f),
        .5f + .5f * MathF.Sin(phase * MathF.Tau + MathF.Tau * 2f / 3f));

    private static Vector2 Direction(float angle) =>
        new(MathF.Cos(angle), MathF.Sin(angle));

    private void DrawDevTestingToggle(SpriteBatch spriteBatch, GameSession session,
        Point mouse, bool mouseDown)
    {
        int width = Math.Min(230, session.ScreenWidth / 3);
        var rect = new Rectangle(session.ScreenWidth - width - 16, 18, width, 34);
        _targets["dev:toggle"] = rect;
        bool enabled = GameProfile.Profile.DevUnlockTesting;
        UiTheme.DrawButton(spriteBatch, rect,
            $"DEV UNLOCKS: {(enabled ? "ON" : "OFF")}  //  F8",
            mouse, mouseDown, true, enabled ? UiTheme.Gold : UiTheme.Muted,
            textSize: 8);
    }

    private void DrawDevUnlockControls(SpriteBatch spriteBatch, GameSession session,
        Point mouse, bool mouseDown)
    {
        int width = Math.Min(270, session.ScreenWidth / 3);
        int x = session.ScreenWidth - width - 16;
        int y = 82;
        var controls = new List<(string Action, string Label, Color Color)>();
        controls.AddRange(CampaignProgression.SenseKeys.Select(sense =>
            ($"portal:{sense}", $"{sense.ToUpperInvariant()} ARENA  {DevGateLabel(sense)}",
                GamePaths.PathsByKey[sense].Accent)));
        controls.Add(("portal:core", $"BODY GATE  {DevGateLabel("core")}", UiTheme.Gold));
        string selectedSense = CampaignProgression.SenseKeys[_devSenseIndex];
        controls.Add(("sense", $"TEST SENSE: {selectedSense.ToUpperInvariant()}  //  NEXT",
            GamePaths.PathsByKey[selectedSense].Accent));
        controls.Add(($"silver:{selectedSense}",
            $"SILVER STATUE  {DevStatueLabel(selectedSense, StatueMaterial.Silver)}",
            new Color(175, 184, 196)));
        controls.Add(($"gold:{selectedSense}",
            $"GOLD STATUE  {DevStatueLabel(selectedSense, StatueMaterial.Gold)}", UiTheme.Gold));
        controls.Add(("aphantasia_statue",
            $"APHANTASIA TROPHY  {DevAphantasiaStatueLabel()}", UiTheme.Purple));
        controls.Add(("rainbow", "TOGGLE ALL RAINBOW", UiTheme.Purple));
        controls.Add(("portal:aphantasia", $"APHANTASIA  {DevGateLabel("aphantasia")}", UiTheme.Purple));
        controls.Add(("reset", "RESET OVERRIDES TO SAVED", UiTheme.Red));
        UiTheme.DrawText(spriteBatch, "DEV UNLOCK TESTING", Fs(10), UiTheme.Gold,
            new Vector2(x + width / 2f, y - 18), "center");
        int rowHeight = Math.Max(23, Math.Min(29,
            (session.ScreenHeight - y - 18) / controls.Count));
        for (int index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var rect = new Rectangle(x, y + index * rowHeight, width, rowHeight - 3);
            _targets[$"dev:{control.Action}"] = rect;
            UiTheme.DrawButton(spriteBatch, rect, control.Label, mouse, mouseDown,
                true, control.Color, textSize: 7.5);
        }
    }

    private static string DevGateLabel(string gate) =>
        CampaignDevOverrides.PortalUnlocks.Contains(gate) ? "[DEV OPEN]"
        : SavedGateUnlocked(gate) ? "[SAVED OPEN]" : "[SEALED]";

    private static bool SavedGateUnlocked(string gate) => gate switch
    {
        "body" => true,
        "core" => CampaignProgression.Data.CoreUnlocked,
        "aphantasia" => CampaignProgression.Data.AphantasiaUnlocked,
        _ => CampaignProgression.Data.ArenaUnlocks.Contains(gate),
    };

    private static string DevStatueLabel(string sense, StatueMaterial material)
    {
        Dictionary<string, ChallengeClear> values = material == StatueMaterial.Silver
            ? CampaignDevOverrides.SilverStatues : CampaignDevOverrides.GoldStatues;
        ChallengeClear state = values.GetValueOrDefault(sense);
        return state.HasFlag(ChallengeClear.Both) ? "[RAINBOW]"
            : state == (ChallengeClear.NoHealing | ChallengeClear.NoExtract) ? "[BLOOD + CRACK]"
            : state.HasFlag(ChallengeClear.NoHealing) ? "[BLOOD]"
            : state.HasFlag(ChallengeClear.NoExtract) ? "[RAINBOW CRACK]"
            : values.ContainsKey(sense) ? "[INTACT]"
            : "[NORMAL/NONE]";
    }

    private static string DevAphantasiaStatueLabel()
    {
        ChallengeClear? state = CampaignDevOverrides.AphantasiaStatue;
        return state?.HasFlag(ChallengeClear.Both) == true ? "[RAINBOW]"
            : state == (ChallengeClear.NoHealing | ChallengeClear.NoExtract) ? "[BLOOD + CRACK]"
            : state?.HasFlag(ChallengeClear.NoHealing) == true ? "[BLOOD]"
            : state?.HasFlag(ChallengeClear.NoExtract) == true ? "[CRACKED]"
            : state.HasValue ? "[INTACT]"
            : "[SAVED/NONE]";
    }

    internal void HandleDevAction(GameSession session, string action)
    {
        if (action == "toggle")
        {
            ToggleDevUnlockTesting(session);
            return;
        }
        if (action == "reset")
        {
            CampaignDevOverrides.Reset();
            RebuildMind(session);
            return;
        }
        if (action.StartsWith("portal:"))
        {
            CampaignDevOverrides.TogglePortal(action[7..]);
            RebuildMind(session);
        }
        else if (action == "sense")
            _devSenseIndex = (_devSenseIndex + 1) % CampaignProgression.SenseKeys.Length;
        else if (action.StartsWith("silver:") || action.StartsWith("gold:"))
        {
            bool silver = action.StartsWith("silver:");
            CampaignDevOverrides.CycleStatue(action[(silver ? 7 : 5)..],
                silver ? StatueMaterial.Silver : StatueMaterial.Gold);
        }
        else if (action == "aphantasia_statue")
            CampaignDevOverrides.CycleAphantasiaStatue();
        else if (action == "rainbow")
            CampaignDevOverrides.ToggleAllRainbow();
    }

    private void DrawCampaignGatePortals(SpriteBatch spriteBatch,
        GameSession session, string? nearbyPortal, float time)
    {
        var gates = new[]
        {
            (BodyPortalKey, "THE BODY / THE SOUL", UiTheme.Gold, "body"),
        };
        foreach (var (key, label, color, gate) in gates)
        {
            if (!_pathPortalWorld.TryGetValue(key, out Vector2 world))
                continue;
            Vector2 screen = session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
            bool unlocked = CampaignProgression.PortalUnlocked(gate);
            // The unlocked Body / Soul rose is fully rendered by
            // SoulVisualRenderer. This method supplies its locked door only.
            if (key == BodyPortalKey && unlocked)
                continue;
            float radius = Simulation.TileSize * .72f;
            Primitives2D.FillCircle(spriteBatch, screen, radius * .8f, UiTheme.Ink);
            Primitives2D.CircleOutline(spriteBatch, screen, radius,
                unlocked ? color : UiTheme.Muted * .45f, unlocked ? 3 : 5);
            if (!unlocked)
            {
                Primitives2D.Line(spriteBatch, screen + new Vector2(-radius, -radius),
                    screen + new Vector2(radius, radius), UiTheme.Red * .75f, 4);
                Primitives2D.Line(spriteBatch, screen + new Vector2(radius, -radius),
                    screen + new Vector2(-radius, radius), UiTheme.Red * .75f, 4);
            }
            UiTheme.DrawText(spriteBatch, unlocked ? label : $"{label} // SEALED", Fs(8),
                unlocked ? color : UiTheme.Muted,
                new Vector2(screen.X, screen.Y + radius + 7), "midtop");
            if (unlocked && nearbyPortal == key && _confirmingPortalKey != key
                && _enteringPortalKey is null)
                UiTheme.DrawText(spriteBatch, "F / B  //  ENTER", Fs(8), UiTheme.Cream,
                    new Vector2(screen.X, screen.Y + radius + 23), "midtop");
        }
        DrawAphantasiaPortal(spriteBatch, session, nearbyPortal, time);
    }

    /// <summary>
    /// Aphantasia's gate gets its own treatment rather than the plain
    /// ink-disc-with-outline every other campaign gate shares: a true void
    /// core with a scatter of tiny stars (the same "leads somewhere else
    /// entirely" language as the boss's own void vortex finale), a
    /// rainbow-cycling rim instead of a flat color, and a handful of tiny,
    /// simplified tentacle spikes (<see cref="Primitives2D.DrawTentacleSpike"/>,
    /// the same routine the boss itself uses at full scale) curling around
    /// it. Meant to make this gate read as a preview of the fight behind it,
    /// not a recolored copy of every other portal in the hub.
    /// </summary>
    private void DrawAphantasiaPortal(SpriteBatch spriteBatch, GameSession session,
        string? nearbyPortal, float time)
    {
        if (!_pathPortalWorld.TryGetValue(AphantasiaPortalKey, out Vector2 world))
            return;
        Vector2 screen = session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
        bool unlocked = CampaignProgression.PortalUnlocked("aphantasia");
        float radius = Simulation.TileSize * .78f;
        Color accent = new(102, 61, 160);

        // Tusks draw first, before the portal's own disc -- so the disc that
        // follows paints over their roots, and only the length that grows
        // out past the disc's edge is visible. They read as rooted behind
        // the portal, rising from the floor around it, rather than floating
        // on top of its face.
        if (unlocked)
        {
            // Every tentacle here goes through this one loop and this one
            // call to Primitives2D.DrawTentacleSpike -- there is only ever
            // one spike routine drawing on this portal, confirmed by
            // grepping the whole repo and by rendering this exact block in
            // isolation. What that isolated render showed: the trailing
            // echoes were fully opaque, and a fast wiggle sampled only
            // ~80ms apart puts each echo's peaks/troughs at different
            // points along the spike, so stacking several opaque copies
            // doesn't blend into a soft trail -- it makes a rigid,
            // ladder-like interference pattern. That was the "non-wiggling
            // root/claw" look the whole time, not a second decoration.
            // Fading each echo's alpha alongside its darken fixes it.
            const int tuskCount = 9;
            const int shadowCount = 6;
            const float shadowDelay = .08f;
            for (int index = 0; index < tuskCount; index++)
            {
                void DrawTusk(float evalTime, float darken, float alpha)
                {
                    float angle = index * MathF.Tau / tuskCount + evalTime * .3f;
                    float length = radius * (3f + .8f * MathF.Sin(evalTime * 1.6f + index));
                    Primitives2D.DrawTentacleSpike(spriteBatch, screen, angle, length,
                        radius * .32f, phase: index * 2.3f, colorPhase: index / (float)tuskCount,
                        time: evalTime, segments: 48, darken: darken, alpha: alpha);
                }

                for (int shadow = shadowCount; shadow >= 1; shadow--)
                {
                    float t = shadow / (float)(shadowCount + 1);
                    DrawTusk(time - shadow * shadowDelay, darken: t, alpha: 1f - t * .85f);
                }
                DrawTusk(time, darken: 0f, alpha: 1f);
            }
        }

        // Opacity gradient instead of one flat-filled disc: layered from the
        // widest, most transparent ring down to the smallest, most opaque
        // one, so alpha compounds toward the center and the edge fades
        // rather than cutting off sharply.
        const int discGradientSteps = 8;
        for (int step = discGradientSteps; step >= 1; step--)
        {
            float t = step / (float)discGradientSteps;
            Primitives2D.FillCircle(spriteBatch, screen, radius * .82f * t,
                new Color(6, 5, 11) * (1f - t * t));
        }
        for (int index = 0; index < 10; index++)
        {
            float starAngle = index * 2.399963f; // golden angle -- an even, non-repeating scatter
            float starRadius = radius * (.15f + (index % 5) * .13f);
            Vector2 star = screen + new Vector2(MathF.Cos(starAngle), MathF.Sin(starAngle)) * starRadius;
            float twinkle = .4f + .6f * (.5f + .5f * MathF.Sin(time * 3f + index * 1.7f));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)star.X - 1, (int)star.Y - 1, 2, 2),
                UiTheme.Cream * (twinkle * (unlocked ? 1f : .35f)));
        }

        if (!unlocked)
        {
            Primitives2D.Line(spriteBatch, screen + new Vector2(-radius, -radius),
                screen + new Vector2(radius, radius), UiTheme.Red * .75f, 4);
            Primitives2D.Line(spriteBatch, screen + new Vector2(radius, -radius),
                screen + new Vector2(-radius, radius), UiTheme.Red * .75f, 4);
        }
        UiTheme.DrawText(spriteBatch, unlocked ? "APHANTASIA" : "APHANTASIA // SEALED", Fs(8),
            unlocked ? accent : UiTheme.Muted,
            new Vector2(screen.X, screen.Y + radius + 7), "midtop");
        if (unlocked && nearbyPortal == AphantasiaPortalKey
            && _confirmingPortalKey != AphantasiaPortalKey && _enteringPortalKey is null)
            UiTheme.DrawText(spriteBatch, "F / B  //  ENTER", Fs(8), UiTheme.Cream,
                new Vector2(screen.X, screen.Y + radius + 23), "midtop");
    }

    private void DrawCompositePathPortal(
        SpriteBatch spriteBatch,
        GameSession session,
        string? nearbyPortal,
        float time)
    {
        if (!_pathPortalWorld.TryGetValue(CorePortalKey, out var world))
            return;

        Vector2 screen = session.Camera.WorldToScreen(
            world, session.PlayerWorldCenter, Vector2.Zero);
        float radius = Simulation.TileSize * 1.36f;
        int selectedNg = NewGamePlus.SelectedLevel(NewGamePlus.DungeonKey);
        bool committing = _enteringPortalKey == CorePortalKey;
        float pullT = committing
            ? (float)Math.Clamp((_seconds - _portalAnimationStart) / PortalPullSeconds, 0, 1)
            : 0f;
        float intensity = 1f + selectedNg * .045f + pullT * 2.6f;
        float pulse = 1f + MathF.Sin(time * 2.35f * intensity) * (.045f + pullT * .08f);

        Primitives2D.FillEllipse(spriteBatch,
            new Rectangle((int)(screen.X - radius * 1.2f), (int)(screen.Y + radius * .48f),
                (int)(radius * 2.4f), (int)(radius * .62f)),
            UiTheme.Shadow * .82f);
        Primitives2D.FillCircle(spriteBatch, screen, radius * .84f * pulse,
            new Color(12, 10, 19));
        Primitives2D.CircleOutline(spriteBatch, screen, radius * 1.08f,
            UiTheme.Gold * (.7f + pullT * .3f), 4);
        Primitives2D.CircleOutline(spriteBatch, screen, radius * .92f,
            UiTheme.Purple * (.82f + pullT * .18f), 3);

        // Every sense owns one rotating segment. The alternating direction
        // and nested radius makes the five colors appear to braid inward.
        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            Color color = GamePaths.Paths[index].Accent;
            float direction = index % 2 == 0 ? 1f : -1f;
            float phase = time * (1.05f + index * .08f) * direction * intensity
                + index * MathF.Tau / GamePaths.Paths.Count;
            float arcRadius = radius * (.48f + index * .085f) * (1f - pullT * .28f);
            var arcRect = new Rectangle(
                (int)(screen.X - arcRadius),
                (int)(screen.Y - arcRadius),
                (int)(arcRadius * 2),
                (int)(arcRadius * 2));
            Primitives2D.Arc(spriteBatch, arcRect, phase,
                phase + MathF.PI * (.54f + index * .035f),
                Color.Lerp(color, Color.White, pullT * .36f), 3);

            Vector2 mote = screen + new Vector2(MathF.Cos(phase), MathF.Sin(phase))
                * arcRadius;
            int size = 4 + index % 2 * 2;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)mote.X - size / 2, (int)mote.Y - size / 2, size, size),
                color * (.78f + pullT * .22f));
        }

        float coreRadius = radius * (.2f - pullT * .07f);
        Primitives2D.FillCircle(spriteBatch, screen, coreRadius,
            Color.Lerp(UiTheme.Cream, UiTheme.Gold, .55f + .25f * MathF.Sin(time * 2.1f)));
        UiTheme.DrawText(spriteBatch, "THE DUNGEON", Fs(12), UiTheme.Gold,
            new Vector2(screen.X, screen.Y + radius + 12), "midtop");
        UiTheme.DrawText(spriteBatch, "FREE PLAY  //  NO CAMPAIGN UNLOCKS",
            Fs(8), selectedNg == 0 ? UiTheme.Cream : UiTheme.Gold,
            new Vector2(screen.X, screen.Y + radius + 31), "midtop");
        if (nearbyPortal == CorePortalKey
            && _confirmingPortalKey != CorePortalKey
            && _enteringPortalKey is null)
        {
            UiTheme.DrawText(spriteBatch, "F / B  //  ENTER", Fs(10), UiTheme.Gold,
                new Vector2(screen.X, screen.Y + radius + 49), "midtop");
        }
    }

    /// <summary>Centered "ENTER {PATH}?" modal shown while _confirmingPortalKey is set -- F commits, walking away or Escape cancels (Escape via OverlayOpen/CloseOverlay in Core/RotBoiGame.cs).</summary>
    private void DrawPortalConfirm(SpriteBatch spriteBatch, GameSession session, Point mouse, bool mouseDown)
    {
        if (_confirmingPortalKey == CorePortalKey)
        {
            DrawCompositePortalConfirm(spriteBatch, session, mouse, mouseDown);
            return;
        }
        if (_confirmingPortalKey is BodyPortalKey or AphantasiaPortalKey)
        {
            DrawCampaignPortalConfirm(spriteBatch, session);
            return;
        }
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
            $"ENEMIES x{NewGamePlus.EnemyMultiplier(selected):0.##}  //  SILVER STATUE CLEAR  //  NG+ UNLOCKED {unlocked}",
            Fs(9), UiTheme.Muted, new Vector2(rect.Center.X, rect.Y + Px(116)), "midtop");
        UiTheme.DrawText(spriteBatch, unlocked == 0
                ? "COMPLETE FOR ITS SILVER STATUE + NG+1"
                : "A / D OR ARROWS  //  SELECT TIER",
            Fs(9), unlocked == 0 ? UiTheme.Red : path.Accent,
            new Vector2(rect.Center.X, rect.Y + Px(138)), "midtop");
        if (GameProfile.Profile.NoHealingEnabled)
            UiTheme.DrawText(spriteBatch, "HARD MODE  //  NO HEALING  //  2X CLEAR TOKENS  //  CORE-FORGED DROPS",
                Fs(9), UiTheme.Red, new Vector2(rect.Center.X, rect.Y + Px(160)), "midtop");
        UiTheme.DrawText(spriteBatch, "F / A CONFIRM   //   B, ESC, OR WALK AWAY CANCEL", Fs(10), UiTheme.Muted,
            new Vector2(rect.Center.X, rect.Bottom - Px(24)), "center");
    }

    private void DrawCampaignPortalConfirm(SpriteBatch spriteBatch, GameSession session)
    {
        string label = _confirmingPortalKey switch
        {
            BodyPortalKey => "ENTER THE BODY / THE SOUL?",
            _ => "APPROACH APHANTASIA?",
        };
        int width = (int)(session.ScreenWidth * .42f);
        var rect = new Rectangle(session.ScreenWidth / 2 - width / 2,
            (int)(session.ScreenHeight * .34f), width, (int)(session.ScreenHeight * .2f));
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, session.ScreenWidth, session.ScreenHeight), UiTheme.Void * .58f);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Gold, shadow: 10);
        UiTheme.DrawText(spriteBatch, label, Fs(20), UiTheme.Cream,
            new Vector2(rect.Center.X, rect.Y + Px(30)), "center");
        UiTheme.DrawText(spriteBatch, "F / A CONFIRM   //   B, ESC, OR WALK AWAY CANCEL", Fs(10), UiTheme.Muted,
            new Vector2(rect.Center.X, rect.Bottom - Px(24)), "center");
    }

    private void DrawCompositePortalConfirm(SpriteBatch spriteBatch,
        GameSession session, Point mouse, bool mouseDown)
    {
        int selected = NewGamePlus.SelectedLevel(NewGamePlus.DungeonKey);
        int unlocked = NewGamePlus.UnlockedLevel(NewGamePlus.DungeonKey);
        int width = (int)(session.ScreenWidth * .46f);
        int height = (int)(session.ScreenHeight * .29f);
        var rect = new Rectangle(
            session.ScreenWidth / 2 - width / 2,
            (int)(session.ScreenHeight * .28f),
            width,
            height);
        Color accent = Color.Lerp(UiTheme.Purple, UiTheme.Gold, .58f);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(0, 0, session.ScreenWidth, session.ScreenHeight),
            UiTheme.Void * .58f);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, accent, shadow: 10);
        UiTheme.DrawText(spriteBatch, "ENTER THE DUNGEON?", Fs(22), UiTheme.Gold,
            new Vector2(rect.Center.X, rect.Y + Px(28)), "center");
        UiTheme.DrawText(spriteBatch,
            "TEN FLOORS  //  ALL SENSES INTERWOVEN",
            Fs(11), UiTheme.Cream,
            new Vector2(rect.Center.X, rect.Y + Px(66)), "center");
        _ngMinusRect = new Rectangle(rect.Center.X - Px(105), rect.Y + Px(82),
            Px(42), Px(30));
        _ngPlusRect = new Rectangle(rect.Center.X + Px(63), rect.Y + Px(82),
            Px(42), Px(30));
        UiTheme.DrawButton(spriteBatch, _ngMinusRect, "-", mouse, mouseDown,
            enabled: selected > 0, accentColor: accent, textSize: Fs(16));
        UiTheme.DrawButton(spriteBatch, _ngPlusRect, "+", mouse, mouseDown,
            enabled: selected < unlocked, accentColor: accent, textSize: Fs(16));
        string tier = selected == 0 ? "NORMAL" : $"NG+{selected}";
        UiTheme.DrawText(spriteBatch, tier, Fs(18),
            selected == 0 ? UiTheme.Cream : UiTheme.Gold,
            new Vector2(rect.Center.X, rect.Y + Px(88)), "midtop");
        UiTheme.DrawText(spriteBatch,
            $"ENEMIES x{NewGamePlus.EnemyMultiplier(selected):0.##}  //  CLEAR REWARD x{NewGamePlus.RewardMultiplier(selected)}  //  UNLOCKED TO NG+{unlocked}",
            Fs(9), UiTheme.Muted,
            new Vector2(rect.Center.X, rect.Y + Px(124)), "midtop");
        UiTheme.DrawText(spriteBatch, unlocked == 0
                ? "FREE PLAY  //  COMPLETION DOES NOT ADVANCE THE CAMPAIGN"
                : "A / D OR ARROWS  //  SELECT LEGACY UNLOCKED TIER",
            Fs(9), unlocked == 0 ? UiTheme.Cream : accent,
            new Vector2(rect.Center.X, rect.Y + Px(146)), "midtop");
        if (GameProfile.Profile.NoHealingEnabled)
        {
            UiTheme.DrawText(spriteBatch,
                "HARD MODE  //  NO HEALING",
                Fs(9), UiTheme.Red,
                new Vector2(rect.Center.X, rect.Y + Px(166)), "midtop");
        }
        UiTheme.DrawText(spriteBatch,
            "F / A CONFIRM   //   B, ESC, OR WALK AWAY CANCEL",
            Fs(10), UiTheme.Muted,
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
            Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), UiTheme.Void * .94f);
            int margin = Math.Max(Px(8),
                (int)(Math.Min(screenWidth, screenHeight) * .045f));
            int gap = Math.Max(Px(7), margin / 3);
            var workspace = new Rectangle(margin, margin,
                screenWidth - margin * 2, screenHeight - margin * 2);
            bool stack = screenWidth < 900
                || workspace.Width < workspace.Height * 1.25f;
            Rectangle vaultPanel;
            Rectangle loadoutPanel;
            if (stack)
            {
                int topHeight = (workspace.Height - gap) / 2;
                vaultPanel = new Rectangle(workspace.X, workspace.Y,
                    workspace.Width, topHeight);
                loadoutPanel = new Rectangle(workspace.X, vaultPanel.Bottom + gap,
                    workspace.Width, workspace.Bottom - vaultPanel.Bottom - gap);
            }
            else
            {
                int leftWidth = (workspace.Width - gap) / 2;
                vaultPanel = new Rectangle(workspace.X, workspace.Y,
                    leftWidth, workspace.Height);
                loadoutPanel = new Rectangle(vaultPanel.Right + gap, workspace.Y,
                    workspace.Right - vaultPanel.Right - gap, workspace.Height);
            }
            session.BeginLoadoutFocus();
            SoulVisualRenderer.DrawOverlayFrame(
                spriteBatch, vaultPanel, "storage", UiTheme.Gold);
            DrawVault(spriteBatch, vaultPanel, mouse, session);
            if (_tooltip is not null) DrawTooltip(spriteBatch, mouse, vaultPanel);
            session.DrawSoulLoadoutPanel(spriteBatch, loadoutPanel, mouse,
                (float)_seconds);
            return;
        }

        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), UiTheme.Void * .94f);
        var panel = new Rectangle((int)(screenWidth * .055f), (int)(screenHeight * .07f),
            (int)(screenWidth * .89f), (int)(screenHeight * .80f));
        Color accent = _overlay switch
        {
            "quests" => UiTheme.Green,
            "skills" => UiTheme.Purple,
            "wardrobe" => UiTheme.Blue,
            "developer_armory" => UiTheme.Gold,
            _ => UiTheme.Cream,
        };
        SoulVisualRenderer.DrawOverlayFrame(spriteBatch, panel, _overlay!, accent);
        if (_overlay == "quests") DrawQuests(spriteBatch, panel, mouse);
        if (_overlay == "skills") DrawSkills(spriteBatch, panel, mouse);
        if (_overlay == "wardrobe") DrawWardrobe(spriteBatch, panel, mouse);
        if (_overlay == "developer_armory") DrawDeveloperArmory(spriteBatch, panel, mouse, session);
        if (_tooltip is not null) DrawTooltip(spriteBatch, mouse, panel);
    }

    private void DrawDeveloperArmory(SpriteBatch spriteBatch, Rectangle panel, Point mouse, GameSession session)
    {
        UiTheme.DrawText(spriteBatch, "DEVELOPER ARMORY", Fs(24), UiTheme.Gold,
            new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        int free = session.State.Inventory.Count(item => item is null);
        UiTheme.DrawText(spriteBatch,
            $"MYTHICAL // FULL MODIFIER LADDER // CLICK TO COPY INTO INVENTORY // {free} FREE SLOTS",
            Fs(9), free > 0 ? UiTheme.Cream : UiTheme.Red,
            new Vector2(panel.X + Px(26), panel.Y + Px(53)));

        int count = DeveloperArmoryItems.Count;
        int columns = Math.Clamp(panel.Width / Px(82), 6, 12);
        int rows = (count + columns - 1) / columns;
        int gap = Px(7);
        int availableWidth = panel.Width - Px(52) - gap * (columns - 1);
        int availableHeight = panel.Height - Px(112) - gap * Math.Max(0, rows - 1);
        int size = Math.Max(Px(38), Math.Min(availableWidth / columns, availableHeight / Math.Max(1, rows)));
        int left = panel.Center.X - (columns * size + (columns - 1) * gap) / 2;
        int top = panel.Y + Px(82);
        for (int index = 0; index < count; index++)
        {
            int column = index % columns, row = index / columns;
            var rect = new Rectangle(left + column * (size + gap), top + row * (size + gap), size, size);
            ItemDrop drop = Items.DeveloperArmoryDrop(DeveloperArmoryItems[index]);
            bool hovered = rect.Contains(mouse);
            ItemCards.DrawItemCard(spriteBatch, rect, drop, hovered, (float)_seconds);
            if (free > 0) _targets[$"armory:{index}"] = rect;
            if (hovered)
                _tooltip = $"{drop.Name}  //  {drop.Rarity}  //  {Items.ModifierUnlockCount(drop.Rarity)}/{drop.Definition.ModifierLadder.Count} MODIFIERS";
        }
    }

    private static string TimeLabel(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

    /// <summary>
    /// Safe, permanent, MetaProgression.StorageCapacity-limited storage. Drag items
    /// between it and the paired carried-loadout panel; the drag itself lives in InformationSheet (see its
    /// VaultDragSource), fed this panel's slot rects via
    /// GameSession.HandleCarriedLoadoutDrag -- there's no click-to-stage step anymore,
    /// what's in your sidebar *is* what you're bringing into your next run, live.
    /// </summary>
    private void DrawVault(SpriteBatch spriteBatch, Rectangle panel, Point mouse,
        GameSession session)
    {
        UiTheme.DrawText(spriteBatch, "VAULT RELIQUARY", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, "SAFE MEMORY  //  DRAG RELICS TO AND FROM YOUR INVENTORY", Fs(9), UiTheme.Gold,
            new Vector2(panel.X + Px(26), panel.Y + Px(53)));

        int slotSize = Px(44), gap = Px(8);
        const int vaultColumns = 5;
        int vaultLeft = panel.X + Px(26), vaultTop = panel.Y + Px(80);
        _vaultSlotRects = new List<Rectangle>();
        for (int index = 0; index < MetaProgression.StorageCapacity; index++)
        {
            int column = index % vaultColumns, row = index / vaultColumns;
            _vaultSlotRects.Add(new Rectangle(
                vaultLeft + column * (slotSize + gap),
                vaultTop + row * (slotSize + gap), slotSize, slotSize));
        }
        session.RegisterVaultFocus(_vaultSlotRects);
        for (int index = 0; index < MetaProgression.StorageCapacity; index++)
        {
            Rectangle rect = _vaultSlotRects[index];
            Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Border, Px(2));
            if (index >= GameProfile.Profile.Storage.Count) continue;
            var drop = Items.Deserialize(GameProfile.Profile.Storage[index]);
            if (drop is null) continue;
            ItemCards.DrawItemCard(
                spriteBatch, rect, drop, rect.Contains(mouse), (float)_seconds);
            if (rect.Contains(mouse)) _tooltip = $"{drop.Rarity} {drop.Name}  //  Drag to your inventory to carry it into a run.";
            if (session.IsLoadoutFocused($"vault:{index}"))
                Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream, Px(2));
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
        UiTheme.DrawText(spriteBatch, "THE VOW LECTERN", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, "VOWS PERSIST ACROSS RUNS  //  GREEN SEALS MARK FULFILLMENT", Fs(9), UiTheme.Green,
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
            if (rect.Contains(mouse)) _tooltip = $"{quest.Description}  Reward: {quest.Reward} Mind Token{(quest.Reward == 1 ? "" : "s")}.";
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
        UiTheme.DrawText(spriteBatch, "THE MIND ROSE", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, $"MIND TOKENS  {GameProfile.Profile.MindTokens}  //  AWAKEN ONE PETAL PER RANK", Fs(9), UiTheme.Purple,
            new Vector2(panel.X + Px(26), panel.Y + Px(53)));
        int columns = 4, gap = Px(12), tileWidth = (panel.Width - Px(52) - gap * 3) / 4;
        int tileHeight = (panel.Height - Px(112) - gap * 2) / 3;
        for (int index = 0; index < MetaProgression.SkillNodes.Count; index++)
        {
            var node = MetaProgression.SkillNodes[index];
            int column = index % columns, row = index / columns;
            var rect = new Rectangle(panel.X + Px(26) + column * (tileWidth + gap), panel.Y + Px(80) + row * (tileHeight + gap), tileWidth, tileHeight);
            int level = GameProfile.Profile.SkillLevels.GetValueOrDefault(node.Key), cost = node.BaseCost + level / 2;
            bool maxed = level >= node.MaxLevel, affordable = GameProfile.Profile.MindTokens >= cost;
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
        UiTheme.DrawText(spriteBatch, "THE VESTMENT MIRROR", Fs(24), UiTheme.Text, new Vector2(panel.X + Px(24), panel.Y + Px(18)));
        UiTheme.DrawText(spriteBatch, "COSMETIC ONLY  //  THE MIRROR REMEMBERS BODY, EDGE, SHOT COLOR, AND SILHOUETTE",
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
            Cosmetics.SelectedProjectile.Core, Cosmetics.SelectedProjectile.Edge, Cosmetics.SelectedDesign.Id,
            animationTime: (float)_seconds, drawShadow: true,
            intensity: (float)GameProfile.Profile.VisualEffectsIntensity);
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
            bool unlocked = Cosmetics.IsUnlocked(category, option.Id);
            int row = index / 3, col = index % 3;
            var rect = new Rectangle(column.X + col * (tile + gap), startY + row * (tile + gap), tile, tile);
            Primitives2D.FillRect(spriteBatch, rect, unlocked ? option.Color : Color.Lerp(option.Color, UiTheme.Ink, .72f));
            Primitives2D.RectOutline(spriteBatch, rect, option.Id == selected ? UiTheme.Cream : UiTheme.Ink, option.Id == selected ? Px(4) : Px(2));
            if (!unlocked)
                UiTheme.DrawText(spriteBatch, "?", Fs(16), UiTheme.Muted, new Vector2(rect.Center.X, rect.Center.Y), "center");
            _targets[$"cosmetic:{category}:{option.Id}"] = rect;
            if (rect.Contains(mouse))
                _tooltip = unlocked
                    ? $"{option.Name} {title.ToLowerInvariant()}."
                    : $"LOCKED  //  {Cosmetics.LockDescription(category, option.Id) ?? Cosmetics.LockedHint}";
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
            bool unlocked = Cosmetics.IsUnlocked("projectile", option.Id);
            int row = index / 3, col = index % 3;
            var rect = new Rectangle(column.X + col * (tile + gap), startY + row * (tile + gap), tile, tile);
            Primitives2D.FillRect(spriteBatch, rect, unlocked ? option.Edge : Color.Lerp(option.Edge, UiTheme.Ink, .72f));
            var inner = rect;
            inner.Inflate(-Math.Max(5, tile / 5), -Math.Max(5, tile / 5));
            Primitives2D.FillRect(spriteBatch, inner, unlocked ? option.Core : Color.Lerp(option.Core, UiTheme.Ink, .72f));
            bool selected = option.Id == GameProfile.Profile.ProjectileColor;
            Primitives2D.RectOutline(spriteBatch, rect, selected ? UiTheme.Cream : UiTheme.Ink, selected ? Px(4) : Px(2));
            if (!unlocked)
                UiTheme.DrawText(spriteBatch, "?", Fs(16), UiTheme.Muted, new Vector2(rect.Center.X, rect.Center.Y), "center");
            _targets[$"cosmetic:projectile:{option.Id}"] = rect;
            if (rect.Contains(mouse))
                _tooltip = unlocked
                    ? $"{option.Name} projectile palette."
                    : $"LOCKED  //  {Cosmetics.LockDescription("projectile", option.Id) ?? Cosmetics.LockedHint}";
        }
    }

    private void DrawProjectileDesignColumn(SpriteBatch spriteBatch, Rectangle column, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "SHOT DESIGN", Fs(12), UiTheme.Text, new Vector2(column.X, column.Y));
        int y = column.Y + Px(30);
        foreach (var option in Cosmetics.ProjectileDesigns)
        {
            bool unlocked = Cosmetics.IsUnlocked("design", option.Id);
            var rect = new Rectangle(column.X, y, column.Width, Px(58));
            bool selected = option.Id == GameProfile.Profile.ProjectileDesign;
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, selected ? UiTheme.Cream : UiTheme.Border, hovered: rect.Contains(mouse));
            if (unlocked)
            {
                ProjectileVisuals.Draw(spriteBatch, new Vector2(rect.X + Px(38), rect.Center.Y), Vector2.UnitX, Px(25),
                    Cosmetics.SelectedProjectile.Core, Cosmetics.SelectedProjectile.Edge, option.Id,
                    animationTime: (float)_seconds, drawShadow: true,
                    intensity: (float)GameProfile.Profile.VisualEffectsIntensity);
                UiTheme.DrawText(spriteBatch, option.Name.ToUpperInvariant(), Fs(9), UiTheme.Text, new Vector2(rect.X + Px(72), rect.Center.Y), "midleft");
            }
            else
            {
                UiTheme.DrawText(spriteBatch, "?", Fs(20), UiTheme.Muted, new Vector2(rect.X + Px(38), rect.Center.Y), "center");
                UiTheme.DrawText(spriteBatch, "LOCKED", Fs(9), UiTheme.Muted, new Vector2(rect.X + Px(72), rect.Center.Y), "midleft");
            }
            _targets[$"cosmetic:design:{option.Id}"] = rect;
            if (rect.Contains(mouse))
                _tooltip = unlocked
                    ? option.Description
                    : $"LOCKED  //  {Cosmetics.LockDescription("design", option.Id) ?? Cosmetics.LockedHint}";
            y += Px(65);
        }
    }

    /// <summary>
    /// Wraps against the font's real measured width (see
    /// <see cref="UiTheme.WrapLines"/>) rather than a fixed character count,
    /// so the box is sized to what actually fits, and hands positioning to
    /// <see cref="UiTheme.ClampTooltipRect"/> -- the shared clamp every
    /// tooltip in the game uses -- so a long entry stretches upward and
    /// stays fully on screen instead of overflowing bounds.Bottom (the old
    /// hand-rolled Math.Clamp here would throw once a tall enough tooltip
    /// pushed its min above its max).
    /// </summary>
    private void DrawTooltip(SpriteBatch spriteBatch, Point mouse, Rectangle bounds)
    {
        int width = Math.Min(Px(360), bounds.Width / 2);
        double fontSize = Fs(9);
        var lines = UiTheme.WrapLines(_tooltip!, fontSize, width - Px(20));
        var rect = UiTheme.ClampTooltipRect(
            new Rectangle(mouse.X + Px(15), mouse.Y + Px(15), width, Px(24 + lines.Count * 17)),
            bounds, Px(6));
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Ink, UiTheme.Cream, shadow: 4);
        for (int index = 0; index < lines.Count; index++)
            UiTheme.DrawText(spriteBatch, lines[index], fontSize, UiTheme.Text, new Vector2(rect.X + Px(10), rect.Y + Px(9 + index * 17)));
    }
}
