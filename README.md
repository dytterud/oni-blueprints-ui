# oni-blueprints-ui

The Unity project that builds the `blueprints_ui` AssetBundle for
[Blueprints Included](https://github.com/dytterud/oni-mods), an Oxygen Not Included mod.

The bundle holds five uGUI prefabs, the mod's screens and dialogs. Until now Blueprints Included
shipped the bundle it was forked with, built by someone else and pinned to the last revision
published under MIT. This project builds it here instead.

## How it works

The prefabs are not stored as prefabs. `spec/<Prefab>.json` records each one's serialized state:
the GameObject tree, each component's type, and every field under Unity's own serialized name
(`m_AnchorMin`, `m_Colors.m_NormalColor`, …). `Assets/Editor/PrefabBuilder.cs` walks that and
sets each field through `SerializedObject`, so a single generic walker handles every component
type, and a field the editor does not recognise stops the build instead of being dropped.

- **The spec came from the MIT bundle.** `tools/extract_spec.py` extracted it. Provenance is in
  [NOTICE](NOTICE).
- **`tools/compare.py` closes the loop.** It runs the same extraction over a freshly built bundle
  and diffs every node, component, field and sprite against the spec. The build is correct when
  it prints `identical to spec`.
- **No game assemblies are involved.** The prefabs use only stock `UnityEngine.UI` and
  `Unity.TextMeshPro` components. The mod attaches its behaviour at runtime, by child path.

### The child paths are an API

Every `transform.Find("…")` in the mod's `src/BlueprintsIncluded/UnityUI/` addresses a node by
name. Renaming, reparenting or reordering a node is a breaking change to the mod. Change a path
here and in the mod in the same breath.

### Sprites

- `Assets/Sprites/Upstream/` holds 22 sprites, carried over from the source bundle.
- `Assets/Sprites/Game/` holds 12 placeholders for sprites that are Oxygen Not Included's own art.
  Each one is white, and has the same name, size and border as the original. The mod swaps in the
  game's sprite by name at load, so a placeholder showing up in-game means that swap failed.

`spec/sprites.json` sets each sprite's import settings (border, pivot, pixels per unit, filter).
`SpriteImport` applies them, so no `.meta` needs hand-editing.

## Building

Requirements:

- **Unity 6000.3.x.** The project pins 6000.3.5f2, the version the game itself runs; any 6000.3 works.
- **Build support modules:** Windows, **Mac Build Support (Mono)** and **Linux Build Support (Mono)**.
- **For the compare step:** Python 3 with `pip install UnityPy Pillow`.

```powershell
powershell ./build.ps1
```

The script:

1. Opens the project headless.
2. Rebuilds `Assets/UIs/*.prefab` from `spec/`.
3. Builds LZMA bundles for Windows, macOS and Linux into `out/{windows,mac,linux}/blueprints_ui`.
4. Diffs each bundle against the spec.

The Unity log is written to `Build/unity.log`. `-Unity <path>` picks a specific editor, and
`-SkipCompare` skips step 4.

The first build creates `ProjectSettings/*` and the `.meta` files. Commit those.

Interactively, open the project and use **Blueprints UI → Build prefabs and bundles**.

## Shipping a build

Copy `out/<platform>/blueprints_ui` over `src/BlueprintsIncluded/ModAssets/assets/<platform>/blueprints_ui`
in oni-mods. The mod version must include the game-sprite swap in `ModAssets.LoadAssets`.
Without it, the 12 placeholders show as white.

## Changing the UI

Edit the spec, rebuild, then check in-game. `compare.py` confirms the build matches the spec,
not that the spec is right. To add a node, copy a similar node's JSON as a starting point. A
`$ref` is an index path from the prefab root (`"0/3/1"`), so inserting a node before others
shifts the paths of everything after it.

A bigger redesign may be easier in the editor. Build once, edit `Assets/UIs/*.prefab` there, then
regenerate the spec from the result:

```
python tools/extract_spec.py out/windows/blueprints_ui
```
