# DSTS models in DCGO VR

Tools for turning the *Digimon Story: Time Stranger* models (downloaded with
`_DSTSModels/Download-DSTSModels.ps1`) into ready-to-use prefabs.

> The models are Bandai Namco assets ripped from the game. Fine for personal use,
> but do not redistribute them in a public build.

## Requirements
- glTFast `com.unity.cloud.gltfast` 6.14.1 (added to `Packages/manifest.json`).
  It becomes the importer for `.glb` files and imports animations as Mecanim clips.

## Use
1. **DCGO > DSTS > Import Models...**
2. Search / filter, tick the models you want, press **Import / rebuild**.
3. Prefabs appear in `Assets/DSTS/Prefabs/<digimon|npcs>/<slug>.prefab`.

Other menu items:
- Right-click a `.glb` in the Project window → **DCGO DSTS > Build Prefab From GLB**
- **DCGO > DSTS > Rebuild All Imported Prefabs**: re-run after changing these scripts.

## What a prefab contains
| Part | Details |
|---|---|
| Materials | `DCGO/DSTS/Toon` for body parts (`chr_cel`), `mask_alpha` (cutout) and `eye_shine` (unlit additive); `DCGO/DSTS/Outline` for the game's outline hull (`outline_inflation`). Uses the game's CLUT ramp for cel bands. |
| Animations | Copied to `Models/<kind>/<slug>/Animations/` with friendly names (`Attack 01.anim`), loop flags set. |
| Animator | `<slug>.controller`: one state per clip, default **Idle**; non-looping states go back to Idle automatically. |
| `DSTSModel` | Info (name, chr id, category, field-guide scale) + `Play("Attack 01")`, `Play("ba01")`, `PlayAttack()`, `PlayHit()`, `PlayGuard()`, `PlayVictory()`, `PlaySpecial()`, `PlayIdle()`. Inspector has play buttons in Play Mode. |

```csharp
var digimon = Instantiate(prefab).GetComponent<DCGO.DSTS.DSTSModel>();
digimon.PlayAttack();          // random ba0x, returns to Idle by itself
digimon.Play("Damage (Hit)");
```

## Animation codes
`bn` idle · `ba` attack · `bd` damage · `bg` guard · `bs` special · `bv` victory ·
`bb` boss sequence · `br` run · `bf` field action · `fe` field emotes (eat, yawn...) ·
`fq` yes/no · `fw/fn/fr/fs/ft` walk/idle/run/sit/turn (field).

## Known gaps
- Texture animations (fire, scrolling eyes, glow) are not recreated yet; they live in the
  GLB `extras` and need per-effect shaders.
- Special shader behaviours (flame composite, env reflection, overlays) fall back to plain cel shading.
- Rebuilding overwrites the generated materials, animations, controller and prefab.
  Keep your own changes in prefab variants.
- Shaders support single-pass instanced stereo (VR) and URP main-light shadows.
