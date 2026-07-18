using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

/// <summary>Safe firing range and tile-based permanent progression sanctuary.</summary>
public sealed class SoulHub
{
    private const float StationOpenRadiusTiles = 1.45f;
    private const float StationCloseRadiusTiles = 1.85f;
    private sealed record DummyHit(double Time, double Damage);
    private readonly Queue<DummyHit> _dummyHits = new();
    private readonly Dictionary<string, Rectangle> _targets = new();
    private readonly Dictionary<string, Vector2> _stationWorld = new();
    private Vector2 _dummyWorld;
    private string? _overlay;
    private string? _tooltip;
    private double _seconds;
    private double _measurementStart;
    private double _lastHitTime = -99;
    private double _currentDps;
    private double _sessionBest;
    private double _lastRecordSave;
    public bool OverlayOpen => _overlay is not null;
    public void CloseOverlay() => _overlay = null;

    public void Enter(GameSession session)
    {
        session.State.EnemySpawningEnabled = false;
        session.State.AutoFire = false;
        session.State.EnemyHolster.Clear();
        session.State.EnemyProjectileHolster.Clear();
        session.LoadPreviewEquipment();
        _dummyWorld = session.PlayerWorldCenter + new Vector2(Simulation.TileSize * 4, 0);
        _stationWorld.Clear();
        _stationWorld["storage"] = session.PlayerWorldCenter - new Vector2(Simulation.TileSize * 4, 0);
        _stationWorld["quests"] = session.PlayerWorldCenter - new Vector2(0, Simulation.TileSize * 4);
        _stationWorld["skills"] = session.PlayerWorldCenter + new Vector2(0, Simulation.TileSize * 4);
        _stationWorld["wardrobe"] = session.PlayerWorldCenter + new Vector2(-Simulation.TileSize * 4, Simulation.TileSize * 4);
        _dummyHits.Clear();
        _seconds = 0;
        _measurementStart = 0;
        _lastHitTime = -99;
        _currentDps = 0;
        _sessionBest = 0;
        _lastRecordSave = 0;
        _overlay = null;
    }

    public void Update(GameSession session, double elapsedSeconds)
    {
        _seconds += Math.Min(.05, elapsedSeconds);
        var dummyRect = new Rectangle((int)(_dummyWorld.X - 34), (int)(_dummyWorld.Y - 44), 68, 88);
        foreach (var bullet in session.State.BulletHolster.Where(bullet => !bullet.RemFlag && dummyRect.Intersects(bullet.WorldRect())).ToArray())
        {
            bullet.RemFlag = true;
            if (_seconds - _lastHitTime > 2)
            {
                _dummyHits.Clear();
                _measurementStart = _seconds;
            }
            double damage = bullet.Damage;
            _dummyHits.Enqueue(new DummyHit(_seconds, damage));
            _lastHitTime = _seconds;
            session.State.DamageTextList.Add(new DamageText(_dummyWorld.X - 20, _dummyWorld.Y - 28,
                bullet.IsCritical ? UiTheme.Purple : UiTheme.Gold, damage, 40, Simulation.FrameRate));
            GameProfile.IncrementQuest("dummy_damage", Math.Max(1, (long)Math.Round(damage)));
        }
        session.State.BulletHolster.RemoveAll(bullet => bullet.RemFlag);
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

    public void HandleInput(GameSession session, IReadOnlySet<Keys> keysPressed, Point mouse, bool mousePressed)
    {
        if (_overlay is not null)
        {
            bool walkedAway = !_stationWorld.TryGetValue(_overlay, out var station)
                || !WithinStationRadius(session.PlayerWorldCenter, station, StationCloseRadiusTiles);
            if (keysPressed.Contains(Keys.F) || walkedAway)
            {
                _overlay = null;
                return;
            }
        }
        else if (keysPressed.Contains(Keys.F))
        {
            var nearby = NearbyStation(session);
            if (nearby is not null)
                _overlay = nearby;
        }
        if (!mousePressed)
            return;
        foreach (var (key, rect) in _targets.ToArray())
        {
            if (!rect.Contains(mouse)) continue;
            if (key.StartsWith("skill:")) MetaProgression.PurchaseSkill(key[6..]);
            else if (key.StartsWith("runitem:"))
            {
                var parts = key.Split(':');
                MetaProgression.TransferRunItemToStorage(parts[1], int.Parse(parts[2]));
            }
            else if (key.StartsWith("stored:")) MetaProgression.SelectStorageItem(int.Parse(key[7..]));
            else if (key.StartsWith("loadout:")) MetaProgression.ClearStartingLoadoutSlot(key[8..]);
            else if (key.StartsWith("cosmetic:"))
            {
                var parts = key.Split(':');
                if (parts.Length == 3 && Cosmetics.Select(parts[1], parts[2]))
                    session.State.ApplyCosmetics();
            }
            break;
        }
    }

    private string? NearbyStation(GameSession session) => _stationWorld
        .Where(station => WithinStationRadius(session.PlayerWorldCenter, station.Value, StationOpenRadiusTiles))
        .OrderBy(station => Vector2.DistanceSquared(station.Value, session.PlayerWorldCenter))
        .Select(station => station.Key)
        .FirstOrDefault();

    public static bool WithinStationRadius(Vector2 player, Vector2 station, float radiusTiles) =>
        Vector2.DistanceSquared(player, station) <= MathF.Pow(Simulation.TileSize * radiusTiles, 2);

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
        UiTheme.DrawText(spriteBatch, "SAFE GROUND  //  WALK TO A STATION  //  F INTERACT  //  ESC OPTIONS", 9, UiTheme.Muted, new Vector2(24, 54));
        DrawStations(spriteBatch, session);
        if (_overlay is not null) DrawOverlay(spriteBatch, session.ScreenWidth, session.ScreenHeight, mouse);
    }

    private void DrawStations(SpriteBatch spriteBatch, GameSession session)
    {
        var labels = new Dictionary<string, (string Label, Color Accent)>
        {
            ["storage"] = ("EXTRACTION CHEST", UiTheme.Gold),
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

    private void DrawOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Point mouse)
    {
        _tooltip = null;
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), UiTheme.Void * .94f);
        var panel = new Rectangle((int)(screenWidth * .055f), (int)(screenHeight * .07f),
            (int)(screenWidth * .89f), (int)(screenHeight * .80f));
        UiTheme.DrawPanel(spriteBatch, panel, UiTheme.PanelRaised, UiTheme.Green, shadow: 10);
        if (_overlay == "storage") DrawStorage(spriteBatch, panel, mouse);
        if (_overlay == "quests") DrawQuests(spriteBatch, panel, mouse);
        if (_overlay == "skills") DrawSkills(spriteBatch, panel, mouse);
        if (_overlay == "wardrobe") DrawWardrobe(spriteBatch, panel, mouse);
        if (_tooltip is not null) DrawTooltip(spriteBatch, mouse, panel);
    }

    private static string TimeLabel(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

    private static readonly (string Label, string Slot)[] NextRunSlots =
    {
        ("WEAPON", "weapon"), ("ARMOR", "armor"), ("RING", "ring"), ("ACC 1", "accessory_1"), ("ACC 2", "accessory_2"),
    };

    private void DrawStorage(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
    {
        UiTheme.DrawText(spriteBatch, "EXTRACTION CHEST", 24, UiTheme.Text, new Vector2(panel.X + 24, panel.Y + 18));
        UiTheme.DrawText(spriteBatch, $"PERMANENT STORAGE  {GameProfile.Profile.Storage.Count}/{MetaProgression.StorageCapacity}  //  CLICK A STORED ITEM TO STAGE IT BELOW  //  CLICK A STAGED SLOT TO REMOVE IT",
            9, UiTheme.Gold, new Vector2(panel.X + 26, panel.Y + 53));
        int slotSize = 44, gap = 8;
        for (int index = 0; index < MetaProgression.StorageCapacity; index++)
        {
            var rect = new Rectangle(panel.X + 26 + index * (slotSize + gap), panel.Y + 76, slotSize, slotSize);
            Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
            bool promised = index < GameProfile.Profile.Storage.Count
                && GameProfile.Profile.StartingLoadout.Values.Contains(GameProfile.Profile.Storage[index]);
            Primitives2D.RectOutline(spriteBatch, rect, promised ? UiTheme.Cream : UiTheme.Border, promised ? 3 : 2);
            if (index >= GameProfile.Profile.Storage.Count) continue;
            var drop = Items.Deserialize(GameProfile.Profile.Storage[index]);
            if (drop is null) continue;
            ItemCards.DrawItemCard(spriteBatch, rect, drop, rect.Contains(mouse));
            _targets[$"stored:{index}"] = rect;
            if (rect.Contains(mouse))
                _tooltip = promised
                    ? $"{drop.Rarity} {drop.Name}  //  Staged for the next run."
                    : $"{drop.Rarity} {drop.Name}  //  Click to prepare for the next run.";
        }

        UiTheme.DrawText(spriteBatch, "NEXT RUN LOADOUT", 11, UiTheme.Cream, new Vector2(panel.X + 26, panel.Y + 132));
        for (int index = 0; index < NextRunSlots.Length; index++)
        {
            var (label, slot) = NextRunSlots[index];
            var rect = new Rectangle(panel.X + 26 + index * (slotSize + gap), panel.Y + 152, slotSize, slotSize);
            Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
            var stored = GameProfile.Profile.StartingLoadout.GetValueOrDefault(slot);
            var staged = stored is null ? null : Items.Deserialize(stored);
            Primitives2D.RectOutline(spriteBatch, rect, staged is not null ? UiTheme.Cream : UiTheme.Border, staged is not null ? 3 : 2);
            UiTheme.DrawText(spriteBatch, label, 7, UiTheme.Muted, new Vector2(rect.Center.X, rect.Bottom + 3), "midtop");
            if (staged is null) continue;
            ItemCards.DrawItemCard(spriteBatch, rect, staged, rect.Contains(mouse));
            _targets[$"loadout:{slot}"] = rect;
            if (rect.Contains(mouse)) _tooltip = $"{staged.Rarity} {staged.Name}  //  Click to remove from the next run.";
        }

        int y = panel.Y + 204;
        if (GameProfile.Profile.ExtractedRuns.Count == 0)
        {
            UiTheme.DrawText(spriteBatch, "No extracted runs yet", 22, UiTheme.Muted, new Vector2(panel.Center.X, panel.Center.Y), "center");
            UiTheme.DrawText(spriteBatch, "Reach a path ending or extract after the midpoint boss.", 10, UiTheme.Cream,
                new Vector2(panel.Center.X, panel.Center.Y + 34), "center");
            return;
        }
        int columnGap = 14, runWidth = (panel.Width - 52 - columnGap) / 2;
        int runHeight = Math.Max(64, (panel.Bottom - y - 18) / 5 - 6);
        for (int index = 0; index < Math.Min(10, GameProfile.Profile.ExtractedRuns.Count); index++)
        {
            var run = GameProfile.Profile.ExtractedRuns[index];
            int column = index / 5, rowIndex = index % 5;
            var rect = new Rectangle(panel.X + 26 + column * (runWidth + columnGap), y + rowIndex * (runHeight + 6), runWidth, runHeight);
            UiTheme.DrawPanel(spriteBatch, rect, UiTheme.Panel, UiTheme.Green);
            UiTheme.DrawText(spriteBatch, $"{index + 1:00}  {run.Path.ToUpperInvariant()}  //  {run.Outcome}", 9, UiTheme.Text,
                new Vector2(rect.X + 9, rect.Y + 7));
            UiTheme.DrawText(spriteBatch, $"LV {run.Level:00}  •  {run.Kills} KILLS  •  {TimeLabel(run.Seconds)}", 8, UiTheme.Muted,
                new Vector2(rect.X + 9, rect.Y + 25));
            for (int itemIndex = 0; itemIndex < run.Items.Count; itemIndex++)
            {
                var item = Items.Deserialize(run.Items[itemIndex]);
                if (item is null) continue;
                var itemRect = new Rectangle(rect.Right - 24 - itemIndex * 27, rect.Bottom - 28, 23, 23);
                ItemCards.DrawItemCard(spriteBatch, itemRect, item, itemRect.Contains(mouse));
                _targets[$"runitem:{run.Id}:{itemIndex}"] = itemRect;
                if (itemRect.Contains(mouse)) _tooltip = $"SALVAGE {item.Rarity} {item.Name}  //  Click to move this item into permanent storage.";
            }
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
