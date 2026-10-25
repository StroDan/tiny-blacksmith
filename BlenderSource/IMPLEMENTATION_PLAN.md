# Tiny Blacksmith — environment asset pass (plan)

## Audit findings (2026-09-26)
- Unity 6000.6.2f1, URP, linear colour space. No existing environment prefabs: every house, prop and tree in the
  city is procedural `MeshBuilder` geometry written by `Assets/Editor/VillageBuilder*.cs` (menu
  *Tiny Blacksmith > Build Village Environment*, rebuilds the `Village` and `City` roots from scratch every run).
- Scale: 1 unit = 1 m. Seat camera at (0, 1.7, -3.5). Doors ~2.0 m, storeys 2.6–3.0 m, city frontages 4–7 m wide,
  8 m deep. Village house front = local +X.
- Buildings deliberately have **no colliders** (station mouse-raycasts, `Interactable` picks by view angle).
  Walkers are hidden by `CityOccluders` boxes and walk the `CityNav` graph; doors are graph nodes.
- DayCycle lights windows by **material colour signature** (r=1, g 0.72–0.8, b≈g-0.35, emissive) and instances
  materials per renderer, so every house keeps its own evening schedule.
- MeadowLife tints renderers named `Tree*` with exactly 3 materials (bark + 2 leaves) through the seasons, and any
  renderer named `MeadowGround`.
- `Spinner` component spins windmill sails / the water wheel.
- The player house (`PlayerHouse`, x -8.4) is a scene object with an `Interactable` door (End Day); the workshop
  stations sit in an open yard under a canvas canopy.
- Castle sightline: buildings within 32° of the start view stay under an ~8° roofline (`MaxFloors`).

## Pipeline
1. `BlenderSource/scripts/*.py` build every asset procedurally in Blender 5.2 (headless), save one `.blend` per
   category and export one FBX per asset (LOD0 + LOD1 meshes; water-wheel as its own object) into
   `Assets/TinyBlacksmith/Environment/Models/...`. Front of every building = Blender -Y.
2. Shared palette → `Assets/TinyBlacksmith/Environment/Materials/palette.json`; the Unity editor tool creates one
   URP/Lit material per palette entry (windows keep DayCycle's signature, lanterns/fire emissive).
3. `EnvironmentAssetImporter` (AssetPostprocessor) fixes import settings and remaps FBX materials to the shared
   library; `EnvironmentPrefabBuilder` makes prefabs (front turned to +X, LODGroup, Ignore-Raycast box colliders,
   static flags, spinner on the wheel, `Tree*` naming for seasonal trees).
4. `VillageBuilder` places prefabs instead of procedural houses/props, keeping every obstacle, occluder, door node,
   bench seat, chimney marker, lantern-light marker and the castle roofline rule.
5. Player house: the blacksmith house prefab replaces the `HouseStructure`/`HouseDecor` visuals; the End-Day door
   interactable stays where it is (the new door is built on the same spot).
6. Verify: CompositionAudit (noon/evening all round), CityLifeTest (walkers), DemoFlow/Prototype1 playtests.
