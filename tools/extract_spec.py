"""Extract prefab specs and sprites from a blueprints_ui bundle.

The spec in spec/ was extracted once from the MIT-licensed bundle - BlueprintsV2's as of upstream
e856903 (2026-09-04), which Blueprints Included shipped at b1e3175, git blob 2335a6f (windows). See
NOTICE. The same extraction runs over a freshly built bundle in tools/compare.py, so the two can be
diffed field by field.

    python tools/extract_spec.py <path/to/windows/blueprints_ui>

Writes spec/<Prefab>.json, spec/sprites.json and the sprite PNGs under Assets/Sprites/. Needs
UnityPy (pip install UnityPy).
"""
import json
import os
import sys

import UnityPy
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The prefabs ModAssets.LoadAssets loads. The source bundle also carries blueprintInfoScreen,
# which nothing loads; it is not rebuilt.
PREFABS = ["blueprintSelector", "UseBlueprintStateContainer", "NoteToolStateContainer",
           "IconSelector", "BlueprintNameDialogue"]

# Sprites that are ONI's own art. The bundle carries a same-sized white placeholder under the same
# name, and the mod swaps in the game's sprite at load - so no Klei art is in this repository.
GAME_SPRITES = {"Background", "Checkmark", "action_cancel", "iconLeft", "iconRight",
                "icon_TrendArrows_Down_1", "icon_folder", "icon_pencil",
                "overview_jobs_icon_checkmark", "stresspanel_icon_expand_arrow",
                "stresspanel_icon_expand_arrow_up", "web_title"}

# Fields that describe the object's place in the file rather than its content.
SKIP = {"m_GameObject", "m_Script", "m_Children", "m_Father", "m_ObjectHideFlags",
        "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_PrefabInternal",
        "m_PrefabParentObject", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_Name"}


class Bundle:
    def __init__(self, path):
        self.env = UnityPy.load(path)
        self.objs = {o.path_id: o for o in self.env.objects}
        self.unhandled = set()
        self.sprites = {}
        self.container = {}
        for o in self.env.objects:
            if o.type.name == "AssetBundle":
                for p, info in o.read_typetree()["m_Container"]:
                    self.container[p] = info["asset"]["m_PathID"]

    def tt(self, o):
        return o.read_typetree()

    def script_type(self, o):
        s = self.tt(self.objs[self.tt(o)["m_Script"]["m_PathID"]])
        return f'{s["m_Namespace"]}.{s["m_ClassName"]}, {s["m_AssemblyName"]}'

    def comp_type(self, o):
        return self.script_type(o) if o.type.name == "MonoBehaviour" else o.type.name

    def _index(self, go, path, table):
        """Map every GameObject/component to its index path from the prefab root."""
        g = self.tt(go)
        comps = [self.objs[c["component"]["m_PathID"]] for c in g["m_Component"]]
        table[go.path_id] = {"path": path, "type": "GameObject", "name": g["m_Name"]}
        seen = {}
        for c in comps:
            t = self.comp_type(c)
            i = seen.get(t, 0)
            seen[t] = i + 1
            table[c.path_id] = {"path": path, "type": t, "index": i, "name": g["m_Name"]}
        rt = next(c for c in comps if c.type.name in ("RectTransform", "Transform"))
        for i, ch in enumerate(self.tt(rt).get("m_Children", [])):
            child = self.objs[self.tt(self.objs[ch["m_PathID"]])["m_GameObject"]["m_PathID"]]
            self._index(child, f"{path}/{i}" if path else str(i), table)

    def _convert(self, v, table, where):
        if isinstance(v, dict):
            if set(v) == {"m_FileID", "m_PathID"}:
                return self._ref(v, table, where)
            return {k: self._convert(x, table, f"{where}.{k}") for k, x in v.items() if k not in SKIP}
        if isinstance(v, list):
            return [self._convert(x, table, where) for x in v]
        return v

    def _ref(self, v, table, where):
        if v["m_PathID"] == 0:
            return None
        if v["m_FileID"] != 0:
            # Unity's built-in font (Arial.ttf / LegacyRuntime.ttf in unity default resources)
            if where.endswith("m_Font"):
                return {"$builtinFont": "LegacyRuntime.ttf"}
            self.unhandled.add(f"external ref at {where}: {v}")
            return None
        if v["m_PathID"] in table:
            e = table[v["m_PathID"]]
            r = {"$ref": e["path"], "$type": e["type"], "$name": e["name"]}
            if e.get("index"):
                r["$index"] = e["index"]
            return r
        o = self.objs[v["m_PathID"]]
        t = o.type.name
        if t == "Sprite":
            name = self.tt(o)["m_Name"]
            self.sprites[name] = o
            return {"$sprite": name}
        if t == "Font":
            return {"$builtinFont": "LegacyRuntime.ttf"}
        if t == "MonoBehaviour" and "TMP_FontAsset" in self.script_type(o):
            return None  # labels get the game's fonts at runtime (TMPConverter)
        self.unhandled.add(f"{t} ref at {where}")
        return None

    def _node(self, go, table):
        g = self.tt(go)
        node = {"name": g["m_Name"], "active": bool(g["m_IsActive"]), "layer": g["m_Layer"],
                "components": [], "children": []}
        rt = None
        for c in g["m_Component"]:
            co = self.objs[c["component"]["m_PathID"]]
            data = self.tt(co)
            node["components"].append({
                "type": self.comp_type(co),
                "props": self._convert(data, table, f'{g["m_Name"]}.{co.type.name}')})
            if co.type.name in ("RectTransform", "Transform"):
                rt = data
        for ch in rt.get("m_Children", []):
            child = self.objs[self.tt(self.objs[ch["m_PathID"]])["m_GameObject"]["m_PathID"]]
            node["children"].append(self._node(child, table))
        return node

    def prefab(self, name):
        go = self.objs[self.container[f"assets/uis/{name.lower()}.prefab"]]
        table = {}
        self._index(go, "", table)
        return {"name": name, "root": self._node(go, table)}

    def sprite_meta(self, name):
        o = self.sprites[name]
        s = self.tt(o)
        ts = o.read().m_RD.texture.read().m_TextureSettings
        b = s["m_Border"]
        return {
            "size": [int(round(s["m_Rect"]["width"])), int(round(s["m_Rect"]["height"]))],
            "border": [b["x"], b["y"], b["z"], b["w"]],  # left, bottom, right, top
            "pivot": [s["m_Pivot"]["x"], s["m_Pivot"]["y"]],
            "pixelsPerUnit": s["m_PixelsToUnits"],
            "filterMode": ts.m_FilterMode,
            "wrapMode": ts.m_WrapU,
        }

    def sprite_image(self, name):
        return self.sprites[name].read().image


def write_json(path, data):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1)
        f.write("\n")


def main(src):
    b = Bundle(src)
    os.makedirs(os.path.join(ROOT, "spec"), exist_ok=True)
    for name in PREFABS:
        write_json(os.path.join(ROOT, "spec", f"{name}.json"), b.prefab(name))

    meta = {}
    for name in sorted(b.sprites):
        game = name in GAME_SPRITES
        sub = "Game" if game else "Upstream"
        m = {"file": f"Assets/Sprites/{sub}/{name}.png", "game": game, **b.sprite_meta(name)}
        png = os.path.join(ROOT, m["file"])
        os.makedirs(os.path.dirname(png), exist_ok=True)
        if game:
            Image.new("RGBA", tuple(m["size"]), (255, 255, 255, 255)).save(png)
        else:
            b.sprite_image(name).save(png)
        meta[name] = m
    write_json(os.path.join(ROOT, "spec", "sprites.json"), meta)

    print(f"prefabs: {len(PREFABS)}, sprites: {len(meta)} "
          f"({sum(m['game'] for m in meta.values())} game placeholders)")
    for u in sorted(b.unhandled):
        print("UNHANDLED", u)
    return 1 if b.unhandled else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
