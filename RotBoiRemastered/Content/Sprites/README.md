# Custom sprites

Everything in this game renders procedurally by default (see `Core/Primitives2D.cs`).
Dropping a PNG in the right spot here overrides that procedural drawing for
just that one item/design/character -- nothing else changes, and nothing
needs to be "finished" before any of it ships. See `Core/Sprites.cs` for the
loader and `UI/ItemCards.cs` / `UI/ProjectileVisuals.cs` / `Entities/Player.cs`
for the fallback checks.

No build step needed: any `.png` placed under this folder is copied to the
output directory automatically (see the wildcard `<None>` glob in
`RotBoiRemastered.csproj`) and picked up at runtime. No `Content.mgcb`
registration required.

## Where files go

The filename (minus `.png`) is always an id that already exists elsewhere in
the code -- there's no separate id-to-file table to maintain.

| Folder | Filename = | Source of the id |
|---|---|---|
| `Weapons/` | `ItemDefinition.VisualKind` | `Systems/Items.cs` (`dagger`, `sword`, `spear`, `bow`, `wand`) |
| `Armor/` | `ItemDefinition.VisualKind` | `Systems/Items.cs` (`vest`, `chain`, `plate`) |
| `Rings/` | `ItemDefinition.VisualKind` | `Systems/Items.cs` (`band`, `signet`) |
| `Accessories/` | `ItemDefinition.VisualKind` | `Systems/Items.cs` (`charm`, `locket`, `badge`, `vial`, `bell`) |
| `Bullets/` | `ProjectileDesign.Id` | `Systems/Cosmetics.cs` (`bulb`, `shard`, `lance`, `comet`, `fork`) |
| `Player/` | fixed name `character.png` | one sprite, no selection yet -- see `Entities/Player.cs` |

Example: giving the Iron Dagger real art means adding `Weapons/dagger.png` --
every weapon whose `VisualKind` is `"dagger"` (Iron Dagger *and* Bloody
Dagger) picks it up, since the icon is keyed by silhouette, not by the
specific item name. Rarity/name differences still show through the item
card's colored frame and text, same as today.

## Art conventions

- **Bullets** are drawn rotated to face travel direction, authored pointing
  **right** (+X). `ProjectileVisuals.Draw` scales your sprite's larger
  dimension to roughly `size * 1.6` -- keep the design roughly centered in
  the canvas so rotation looks right.
- **Bullets are not tinted** by the in-game core/edge color picker (Wardrobe)
  once a sprite exists for that design -- they render at their authored
  colors. If you want a design to stay recolorable, that needs a tinting
  pass added later (e.g. author near-white/gray art and multiply by `core`).
- **The player sprite** (`Player/character.png`) is drawn axis-aligned, never
  rotated (the camera turns instead of the character). It's tinted cream
  while dashing and near-white on invulnerability flash frames, same as the
  procedural body, so those gameplay cues keep working once real art is in.
- Sprites should be authored close to their target on-screen size
  (`Simulation.TileSize` is 50px; the player draws at `TileSize * .75`) --
  they're scaled to fit, not cropped.
- Pixel art will render blurry until a `SamplerState.PointClamp` pass is
  added to the relevant `spriteBatch.Begin()` calls (nothing currently
  requests point filtering, since nothing but solid fills has existed to
  filter). Flag this once real art is in if it looks soft.
