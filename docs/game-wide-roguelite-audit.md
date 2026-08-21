# Game-Wide Roguelite Release Audit

Last updated: 2026-08-13

## Release target

- A genre newcomer can start a normal sense-arena run without outside documentation.
- A successful normal arena run targets 25-35 minutes and ends with an explicit debrief.
- Fresh-profile viability is the baseline. Skills and vaulted gear add options, not prerequisites.
- The five arena runs are the standalone product loop. Body/Soul, the Dungeon, challenges,
  Aphantasia, mastery, cosmetics, and NG+ are replay depth.
- Full 100% core-content completion (all five senses plus Body/Soul) targets roughly three
  hours end to end. The game should not feel sluggish or infinite unless a player chooses to
  chase post-game content; Aphantasia, NG+, Hard Mode, and additional Core-themed bosses that
  expand on each sense's theme are that optional depth, not the main line.
- Danger, friendly fire, pickups, rarity, interaction, and boss state keep universal meanings
  while each sense owns its palette, geometry, projectiles, architecture, and boss motif.

## Reproducible baseline

The repository pins `dotnet-mgcb` 3.8.5 in `.config/dotnet-tools.json`. Restore and verify with:

```powershell
dotnet tool restore
dotnet test RotBoiRemastered.Tests/RotBoiRemastered.Tests.csproj --no-restore
```

The audit baseline on 2026-08-13 is 1,147 passing tests, zero failures. Tests construct saves only
under disposable temporary directories. `GameProfileAuditScenarioTests` round-trips these states:

| Scenario | Purpose | Campaign state |
| --- | --- | --- |
| Fresh | First launch, migration defaults, no hidden power assumptions | No skills, statues, mastery, or NG+ |
| Mid-progression | Gate and partial-unlock behavior | Two silver statues, modest skills and vault state |
| Fully unlocked | Every portal and retained collection | All skills, silver/gold statues, Aphantasia, NG+7 |

Never point an audit build or test at the developer profile. The live Windows profile is
`%APPDATA%\RotBoiRemastered\profile.json`.

## Implemented findings

| ID | Severity | Reproduction | Expected contract | Resolution and regression evidence |
| --- | --- | --- | --- | --- |
| META-01 | Blocker | Start with no profile, earn the first statue | First clear persists safely | Default/corrupt fallback now normalizes campaign dictionaries; statue writes normalize defensively. `GameProfileTests`, `CampaignProgressionTests` |
| META-02 | High | Load negative, unknown, duplicated, oversized, or invalid profile fields | Old/malformed saves become safe, bounded state | Scalars, skills, quests, storage, equipment, inventory, mastery, history, NG+, and boss telemetry normalize. `GameProfileTests`, `GameProfileAuditScenarioTests` |
| FLOW-01 | High | Finish a run using controller only | The results screen is reachable | Controller confirm now opens completed-run results. `RotBoiGameTests` |
| FLOW-02 | High | Restart a sense arena, then clear it | The replay remains an arena clear | Restart preserves campaign activity and sense instead of degrading to an untracked generic run. `GameSessionTests` |
| FLOW-03 | High | Extract after the correct milestone | Banking has a visible terminal outcome | Extraction is milestone-gated, finalizes telemetry/rewards once, shows a transition, and opens the debrief. `GameSessionTests`, `MenusTests` |
| FLOW-04 | High | Use a controller in The Mind | Portals and permanent menus are operable | B interacts/backs out, A confirms, stick/D-pad changes tiers and overlay focus, and focused actions receive an outline. `SoulHubTests` |
| START-01 | Blocker | Launch the built game from the repository root or an installed shortcut | Raw content loads independently of the current directory | FontStash now resolves its copied font from `AppContext.BaseDirectory`; an eight-second native graphics/content startup smoke remains alive. `UiThemeTests` |
| INPUT-01 | Medium | Inspect/rebind controls, then press R in a run | One key has one advertised meaning | Removed the unused restart binding that conflicted with extraction; R is reroll only during drafts and extract after the midpoint milestone. `KeybindsTests`, `MenusTests` |
| PRESENT-01 | High | Aim at any camera rotation | Player muzzle, recoil, regalia, and shot direction agree | Player presentation now tracks screen-space aim independently of collision state. `PlayerVisualTests` |
| PRESENT-02 | High | Set VFX to 0% during dense combat | Telegraphs and ownership remain visible | Essential recipes, hostile trim, shadows, hit feedback, and typed boss poses bypass optional density. `VisualLanguageTests`, `BitVfxSystemTests`, boss tests |
| PRESENT-03 | Medium | Enter a mode behind an opaque entry banner | No unseen opening damage | Entry splash duration grants matching opening grace. `ModeEntrySplashTests`, `GameSessionTests` |
| PRESENT-04 | Medium | Defeat Aphantasia under normal, either single-brazier, or dual-brazier conditions | The finale has a persistent, readable home-base trophy | The central chapel gains an animated Aphantasia trophy: normal, bloody, cracked, accumulated blood+crack, and combined-trial rainbow variants. Its essential silhouette remains animated at 0% VFX. `CampaignProgressionTests`, `SoulHubTests` |
| RESULT-01 | Medium | Complete or extract from any supported mode | Outcome, rewards, timing, and retained gear are explicit | Canonical outcomes, mode titles, field/boss/total time, 25-35 minute target band, reward deltas, and correct upgrade count are captured. `RunResultReportTests`, `MenusTests` |
| COPY-01 | Medium | Compare hub, settings, quests, results, and README | One concept has one shipped name | Safe hub and currency are The Mind and Mind Tokens; hostile campaign worlds remain The Body/The Soul; standalone composite route is The Dungeon. String assertions and README |

## Visual QA coverage matrix

The automated column verifies deterministic state, timing, catalog coverage, semantic cues, and
layout invariants without a GPU. The native column is the release sweep for clipping, brightness,
audio/visual feel, device routing, and driver-specific presentation.

| Surface | Automated coverage | Native release cases |
| --- | --- | --- |
| Player | Idle/move pose, aim axes, recoil, dash/hit/death signals, cosmetic and Core-Forged identity | Aim and fire through 0/90/180/270-degree camera rotation; min/max zoom; low health; every projectile silhouette |
| Ordinary enemies | Every runtime catalog family maps to a typed visual profile; animation and locomotion samples are bounded | Every family at ordinary/elite tiers; occlusion, outline, death, loot handoff |
| Sense bosses | Guardian + midpoint + finale coverage for Sound, Touch, Sight, Chemesthesis, and Phantasia; entrance/pose priority/phase curves/telegraphs | Entrance, movement/facing, windup, active, recovery, stagger, phase transition, summons, defeat, reward portal |
| Special bosses | Chase boss, Arsenal mini-bosses, Aphantasia state and lifecycle tests | Arena changes, damage windows, darkness/contrast, death and finale handoff |
| Projectiles and VFX | Player/hostile projectile families, portals, essential VFX recipes, zero-intensity behavior, density director | Ownership at 0/50/100%; crowded overlap; trails, flashes, shake, shadows, audio sync |
| World and themes | Five path registries, room roles/states, fog visibility, theme fixtures, Mind gates/statues | Palette/architecture/ambience per sense; door seams; rotated rooms; portal and room transitions |
| Pickups and loot | EXP, Fragments, crates, item rarity/Core-Forged behavior, inventory retention rules | Pickup silhouette at every VFX level; chest opening; prompt and card clipping at GUI extremes |
| UI and transitions | Entry splash, settings geometry, footer/dossier, level draft, results, extraction/completion state | Title-to-Mind, room/floor, Body-to-Soul, results, unlock overlays at every target resolution |

### Sense identity checklist

| Sense | Midpoint | Finale | Universal readability check |
| --- | --- | --- | --- |
| Sound | Beaudis | Dissonance | Rhythmic/ring vocabulary never replaces the hostile red trim |
| Touch | Bair | Rot | Weight, vents, and decay stay distinct from safe pickup gold |
| Sight | Ishe | Chronos | Optic/time geometry keeps damaging windows aligned with telegraphs |
| Chemesthesis | Kage | Ache | Irritant/brittle shapes preserve outline and projectile ownership |
| Phantasia | Hypno | Malady | Dream/constellation motion blends continuously and remains legible at 0% VFX |

## Native release sweep

Run every row below for fresh, mid-progression, and fully unlocked disposable profiles. Record any
failure with mode, path, boss, profile state, setting values, input device, exact steps, expected
behavior, severity, and a screenshot/video. A native pass is required before tagging a release;
unit tests do not certify GPU, display, audio hardware, or a physical controller.

| Axis | Required values |
| --- | --- |
| Resolution | 1280x720, 1920x1080, 2560x1440, native maximum |
| Display | Windowed and borderless fullscreen |
| Frame cap | 30, 60, 120, 360/high refresh; VSync off/on where supported |
| VFX | 0%, 50%, 100% |
| Accessibility | High contrast off/on; min/default/max text and GUI scale; shake 0/100% |
| Input | Keyboard/mouse only; controller only from title through results and return to Mind |
| Outcomes | Death, restart, abandonment, extraction, arena clear, Body-to-Soul, Soul clear, Dungeon clear, challenge clear, Aphantasia clear |

Use `/vfxgallery 0 <path> <difficulty>` and `/vfxgallery 100 <path> <difficulty>` for the projectile,
enemy-family, room-glyph, and density comparison. Use the boss-practice shortcuts only on a copied
profile, and verify the natural encounter path separately because debug spawn does not prove gates,
reward handoff, or encounter cleanup.

## Acceptance gate

- Automated suite: zero failures.
- Native sweep: no open blocker or high-severity defects.
- Normal arena telemetry: representative newcomer runs cluster at 25-35 minutes; results expose
  field and boss time so travel/menu/encounter dead time can be distinguished before balance changes.
- Every lower-severity deferral is entered in this document with owner and release target.
