using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

/// <summary>
/// The single Path-floor curve applied after an enemy or boss has established
/// its authored baseline. Timing values below one make declarations more
/// frequent without mutating projectile travel or warning geometry.
/// </summary>
public readonly record struct DungeonFloorDifficultyProfile(
    double Health,
    double Damage,
    double Timing,
    int Complexity)
{
    private static readonly DungeonFloorDifficultyProfile[] Profiles =
    [
        new(1.00, 1.00, 1.00, 0),
        new(1.10, 1.05, .98, 0),
        new(1.20, 1.10, .96, 1),
        new(1.32, 1.15, .94, 1),
        new(1.50, 1.22, .92, 2),
        new(1.80, 1.38, .88, 3),
        new(1.98, 1.46, .86, 3),
        new(2.16, 1.54, .84, 4),
        new(2.36, 1.63, .82, 4),
        new(2.65, 1.75, .80, 5),
    ];

    public static DungeonFloorDifficultyProfile ForFloor(int floorNumber) =>
        Profiles[Math.Clamp(floorNumber, 1, Profiles.Length) - 1];
}

public enum BossArenaShape
{
    Circle,
    Raceway,
    Prison,
    Shutter,
    Reactor,
    DreamCourt,
    Basin,
    Timeline,
    NervousCourt,
}

/// <summary>Static construction data shared by arena-mode and Path-mode boss presentation.</summary>
public sealed record BossArenaDefinition(
    string BossKey,
    string SenseKey,
    BossArenaShape Shape,
    int SizeTiles,
    int PlayableRadiusTiles,
    bool UsesSolidContraction);

/// <summary>Inspectable metadata for authored phases and debug tooling.</summary>
public sealed record BossPhaseDefinition(
    string Label,
    string Intent,
    bool Survival = false,
    int MinimumDeclarations = 2);

public sealed record BossEncounterDefinition(
    string BossKey,
    int Tier,
    BossArenaDefinition Arena,
    IReadOnlyList<BossPhaseDefinition> Phases);

public enum GuardianActVariant
{
    FirstAct,
    SecondAct,
    TreasureCondensed,
}

public sealed record GuardianEncounterVariant(
    string SenseKey,
    GuardianActVariant Variant,
    double DurabilityScale,
    int PhaseCount,
    int AdditionalAttackFamiliesPerPhase);

/// <summary>
/// Small adaptive selector used by redesigned encounters. It prevents an
/// immediate repeat and forces the signature option at least once every
/// three committed declarations while still letting callers weight choices
/// from live player position and arena state.
/// </summary>
public sealed class BossAttackDirector
{
    private int _lastChoice = -1;
    private int _sinceSignature;

    public int LastChoice => _lastChoice;
    public int SinceSignature => _sinceSignature;

    public void Reset()
    {
        _lastChoice = -1;
        _sinceSignature = 0;
    }

    public int Choose(
        int choiceCount,
        int signatureChoice,
        ReadOnlySpan<float> weights,
        Random rng)
    {
        if (choiceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(choiceCount));
        signatureChoice = Math.Clamp(signatureChoice, 0, choiceCount - 1);
        int selected;
        if (_sinceSignature >= 2 && signatureChoice != _lastChoice)
        {
            selected = signatureChoice;
        }
        else
        {
            float total = 0;
            for (int index = 0; index < choiceCount; index++)
            {
                if (index != _lastChoice)
                    total += Math.Max(0, index < weights.Length ? weights[index] : 1f);
            }
            if (total <= 0)
                selected = (_lastChoice + 1 + choiceCount) % choiceCount;
            else
            {
                float roll = (float)rng.NextDouble() * total;
                selected = 0;
                for (int index = 0; index < choiceCount; index++)
                {
                    if (index == _lastChoice)
                        continue;
                    selected = index;
                    roll -= Math.Max(0, index < weights.Length ? weights[index] : 1f);
                    if (roll <= 0)
                        break;
                }
            }
        }

        _lastChoice = selected;
        _sinceSignature = selected == signatureChoice ? 0 : _sinceSignature + 1;
        return selected;
    }
}

/// <summary>
/// Common movement/arena surface used by the session instead of growing
/// another concrete boss-type switch for every authored encounter.
/// </summary>
public interface IBossArenaController
{
    Vector2 ArenaCenter { get; }
    float ArenaRadius { get; }
    float Contraction { get; }
    bool ContractionActive => Contraction > 0;
    IReadOnlyList<Rectangle> MovementObstacles { get; }
    IReadOnlyList<Rectangle> HazardBoundaries => Array.Empty<Rectangle>();
    float SafeRouteProgress => 0f;
    void CompleteSafeRoute() { }
    Vector2 ConstrainPlayer(Vector2 playerTopLeft, float playerSize);
}

/// <summary>
/// A shaped boss arena whose exterior mask and boundary must be rendered as
/// a persistent final-world pass. Keeping this separate from the boss body's
/// depth-sorted draw prevents later walls/effects from leaking outside the
/// arena and prevents camera culling from making the arena disappear.
/// </summary>
public interface IBossArenaOcclusion
{
    void DrawPersistentArena(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        Rectangle logicalViewport);
}

/// <summary>
/// A boss-owned mask that must paint only the floor/background layer --
/// before the player, boss body, and every projectile are drawn, rather than
/// after everything like <see cref="IBossArenaOcclusion"/>'s final pass.
/// Introduced for Aphantasia's end-of-fight void vortex, which used to share
/// the final occlusion pass and so painted over the boss/player/projectiles
/// once its radius grew past them.
/// </summary>
public interface IBossFloorOcclusion
{
    void DrawFloorOcclusion(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake);
}

/// <summary>
/// Runtime ownership for a Path floor-five/ten encounter. The old floor is
/// retained only as suspended provenance until the player advances; it is
/// never updated or rendered while this state is active.
/// </summary>
public sealed record DungeonBossInstanceState(
    Battleground SuspendedBattleground,
    PathFogOfWar? SuspendedFog,
    Battleground ArenaBattleground,
    string BossKey,
    int FloorNumber,
    Vector2 ArenaCenter);

/// <summary>Factory for the isolated battleground used by Path floors five and ten.</summary>
public static class BossArenaFactory
{
    private static readonly IReadOnlyDictionary<string, BossArenaDefinition> Definitions =
        new Dictionary<string, BossArenaDefinition>(StringComparer.Ordinal)
        {
            ["beaudis"] = new("beaudis", "sound", BossArenaShape.Raceway, 35, 14, false),
            ["dissonance"] = new("dissonance", "sound", BossArenaShape.Circle, 35, 14, false),
            ["bair"] = new("bair", "touch", BossArenaShape.Prison, 35, 14, true),
            ["rot"] = new("rot", "touch", BossArenaShape.Basin, 35, 14, false),
            ["ishe"] = new("ishe", "sight", BossArenaShape.Shutter, 35, 14, false),
            ["chronos"] = new("chronos", "sight", BossArenaShape.Timeline, 35, 14, false),
            ["kage"] = new("kage", "chemesthesis", BossArenaShape.Reactor, 35, 14, false),
            ["ache"] = new("ache", "chemesthesis", BossArenaShape.NervousCourt, 35, 14, false),
            ["hypno"] = new("hypno", "phantasia", BossArenaShape.DreamCourt, 35, 14, true),
            ["malady"] = new("malady", "phantasia", BossArenaShape.DreamCourt, 35, 14, false),
            // Larger than Malady's 1.5x Path-finale court (52.5 tiles / 21-tile
            // radius) so the final encounter has room for intersecting fields.
            ["aphantasia"] = new("aphantasia", "phantasia", BossArenaShape.DreamCourt, 59, 24, false),
        };

    public static BossArenaDefinition DefinitionFor(string bossKey) =>
        Definitions.TryGetValue(bossKey, out BossArenaDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"No arena is registered for boss '{bossKey}'.");

    public static Battleground Create(string bossKey, int floorNumber = 0,
        float scale = 1f)
    {
        BossArenaDefinition definition = DefinitionFor(bossKey);
        scale = Math.Clamp(scale, 1f, 1.5f);
        int size = (int)MathF.Round(definition.SizeTiles * scale);
        int playableRadius = (int)MathF.Round(definition.PlayableRadiusTiles * scale);
        int center = size / 2;
        var tiles = new TileType[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                bool inside = definition.Shape switch
                {
                    BossArenaShape.Prison or BossArenaShape.Basin =>
                        Math.Abs(dx) <= playableRadius
                        && Math.Abs(dy) <= playableRadius,
                    BossArenaShape.Shutter =>
                        Math.Abs(dx) / 1.08f + Math.Abs(dy) <= playableRadius * 1.35f,
                    BossArenaShape.Reactor or BossArenaShape.NervousCourt =>
                        Math.Abs(dx) <= playableRadius
                        && Math.Abs(dy) <= playableRadius
                        && Math.Abs(dx) + Math.Abs(dy) <= playableRadius * 1.65f,
                    _ => dx * dx + dy * dy <= playableRadius * playableRadius,
                };
                tiles[y, x] = inside ? TileType.BuildingFloor : TileType.OuterVoid;
            }
        }

        IReadOnlyList<BiomePalette> palettes = bossKey == "aphantasia"
            ? BiomePalettes.Aphantasia
            : definition.SenseKey switch
        {
            "touch" => BiomePalettes.Touch,
            "sight" => BiomePalettes.Sight,
            "chemesthesis" => BiomePalettes.Chemesthesis,
            "phantasia" => BiomePalettes.Phantasia,
            _ => BiomePalettes.Sound,
        };
        Vector2 spawn = new(
            (center + .5f) * Battleground.TileSize - Battleground.TileSize * .375f,
            (center + playableRadius * .68f) * Battleground.TileSize);
        return new Battleground(
            tiles,
            palettes,
            wallHeight: definition.SenseKey == "touch" ? 22 : 16,
            spawnPosition: spawn,
            visualThemeKey: bossKey == "aphantasia"
                ? "aphantasia"
                : definition.SenseKey,
            pathFloorNumber: floorNumber);
    }
}

/// <summary>
/// Authoritative authored-boss metadata for HUD/debug selectors and deterministic
/// encounter tests. Runtime classes own the concrete emissions; this catalog owns
/// the player-facing phase contract shared by arena and dungeon instances.
/// </summary>
public static class BossEncounterCatalog
{
    private static BossPhaseDefinition Phase(string label, string intent, bool survival = false) =>
        new(label, intent, survival, MinimumDeclarations: survival ? 0 : 2);

    private static BossEncounterDefinition Encounter(string key, int tier, params BossPhaseDefinition[] phases) =>
        new(key, tier, BossArenaFactory.DefinitionFor(key), phases);

    private static readonly IReadOnlyDictionary<string, BossEncounterDefinition> Definitions =
        new Dictionary<string, BossEncounterDefinition>(StringComparer.Ordinal)
        {
            ["beaudis"] = Encounter("beaudis", 10,
                Phase("APPROACH", "Doppler crescents react to radial movement."),
                Phase("FLYBY", "Crossing wakes resolve after the boss passes."),
                Phase("INTERFERENCE", "Mirrored wakes replay from the boundary.", true),
                Phase("REDLINE", "Crossing the declared wake releases edge pressure."),
                Phase("SONIC BOOM", "The complete pursuit grammar and one isolated dash check.")),
            ["bair"] = Encounter("bair", 10,
                Phase("INTAKE", "Four walls establish warning and motion language."),
                Phase("QUARTERING", "Paired walls close alternating quadrants."),
                Phase("MOVING CELL", "The safe chamber travels across the court.", true),
                Phase("RELEASE", "Breaking locks opens temporary exits."),
                Phase("SOLITARY", "Adaptive wall closures preserve collision-safe openings.")),
            ["ishe"] = Encounter("ishe", 10,
                Phase("EXPOSURE", "A snapshot becomes a moving curtain on the flash."),
                Phase("DOUBLE EXPOSURE", "Two snapshots resolve on separate beats."),
                Phase("SHUTTER", "Frozen declarations alternate with moving windows.", true),
                Phase("NEGATIVE", "Previous positions return from arena edges."),
                Phase("AFTERIMAGE", "Snapshots, negatives, and a late shutter combine.")),
            ["kage"] = Encounter("kage", 10,
                Phase("SPARK / FUEL", "Projectile families react only where they intersect."),
                Phase("PRESSURE / HEAT", "Slow banks activate dormant mines."),
                Phase("SOLVENT / CRYSTAL", "Cleared and hardened sectors alternate.", true),
                Phase("CHAIN REACTION", "Two readable reaction pairs resolve in sequence."),
                Phase("CRITICAL MIXTURE", "Adaptive pairs retain one guaranteed clean route.")),
            ["hypno"] = Encounter("hypno", 10,
                Phase("LAW OF MOTION", "The sigil declares whether movement or stillness is safe."),
                Phase("LAW OF DISTANCE", "Qualified inner and outer bands enforce the rule."),
                Phase("LAW OF FORM", "Shape and glyph identify damaging bodies."),
                Phase("HERESY", "Two laws overlap and one is explicitly inverted.", true),
                Phase("CONTRADICTION", "Correct solutions clear penalties and restore space.")),
            ["rot"] = Encounter("rot", 20,
                Phase("CASTOFF", "Refuse fronts decay into telegraphed sludge."),
                Phase("DIGESTION", "Sludge emits spores before becoming inert."),
                Phase("COMPOST", "Old matter feeds boundary banks until a route is completed."),
                Phase("METABOLISM", "Neglected sectors accelerate the decomposition cycle.", true),
                Phase("BLOOM", "Expired material generates secondary growth."),
                Phase("MIASMA", "Two material generations coexist."),
                Phase("CLOSED CYCLE", "Burial cycles every learned material state.", true)),
            ["chronos"] = Encounter("chronos", 20,
                Phase("FORK", "Position commits one of two fully previewed futures."),
                Phase("REJECTED HOUR", "The refused future returns as a weaker echo."),
                Phase("THIRD FUTURE", "Three previews create a harder commitment."),
                Phase("STILL SECOND", "Timed survival repeatedly commits futures.", true),
                Phase("PARADOX", "Chosen and rejected routes overlap on offset beats."),
                Phase("THORN OF TIME", "The killing line is shown among safer branches."),
                Phase("KING'S ATTRITION", "Commitments, echoes, Thorn, and harmless histories combine.", true)),
            ["ache"] = Encounter("ache", 20,
                Phase("TRESPASS", "Crossing borders provokes baitable counter-lanes."),
                Phase("RECOIL", "Repeated dodge directions provoke an opposite response."),
                Phase("FALSE ALARM", "Harmless warnings establish a distinct presentation."),
                Phase("PROVOCATION", "Brittle structures control retaliation."),
                Phase("SPLINTER", "Destroyed structures redistribute their reactions."),
                Phase("REFLEX STORM", "Observed actions receive independent telegraphs.", true),
                Phase("OVERREACTION", "Deliberate baiting creates stagger windows."),
                Phase("OVERLOAD", "Borders, structures, counters, and false alarms combine.", true)),
            ["dissonance"] = Encounter("dissonance", 20,
                Phase("PRESERVED", "Existing nine-phase encounter; presentation changes only.")),
            ["malady"] = Encounter("malady", 20,
                Phase("PRESERVED", "Existing ten-movement encounter; presentation changes only.")),
            ["aphantasia"] = Encounter("aphantasia", 20,
                Phase("ESSENCE I", "Light and Dark guard the first three movement families."),
                Phase("FIRST ECLIPSE", "The Minis become invulnerable during survival.", true),
                Phase("ESSENCE II", "The shared health bar accelerates its crossing fields."),
                Phase("SECOND ECLIPSE", "A denser survival closes the shared health bar.", true),
                Phase("TESSERACT", "Six movement families and the surviving empowered Mini overlap."),
                Phase("GRAND FINALE", "The remaining Mini and dancing fields resolve in sequence.", true),
                Phase("CORE OF THE VOID", "Both braziers unlock portals and the final survival.", true)),
        };

    public static IReadOnlyCollection<BossEncounterDefinition> All => Definitions.Values.ToArray();

    public static BossEncounterDefinition DefinitionFor(string bossKey) =>
        Definitions.TryGetValue(bossKey, out BossEncounterDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"No encounter is registered for boss '{bossKey}'.");

    public static GuardianEncounterVariant GuardianVariantFor(
        string senseKey, GuardianActVariant variant) => variant switch
        {
            GuardianActVariant.FirstAct => new(senseKey, variant, 1.0, 3, 0),
            GuardianActVariant.SecondAct => new(senseKey, variant, 1.0, 3, 1),
            _ => new(senseKey, variant, .65, 2, 1),
        };
}
