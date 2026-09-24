"""Diff a built blueprints_ui bundle against spec/, field by field.

    python tools/compare.py out/windows/blueprints_ui

Runs the same extraction as extract_spec.py over the built bundle and compares every prefab's
hierarchy, component list and serialized field against the committed spec, then every sprite's
size, border, pivot and pixels. Exits non-zero on any difference, so it can gate a build.

Floats are compared with a small tolerance; everything else must match exactly.
"""
import json
import os
import sys

from PIL import Image, ImageChops

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from extract_spec import PREFABS, ROOT, Bundle  # noqa: E402

TOL = 1e-4


def diff(a, b, where, out):
    if isinstance(a, dict) and isinstance(b, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a:
                out.append(f"{where}.{k}: only in built ({json.dumps(b[k])[:80]})")
            elif k not in b:
                out.append(f"{where}.{k}: missing from built (spec {json.dumps(a[k])[:80]})")
            else:
                diff(a[k], b[k], f"{where}.{k}", out)
    elif isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            out.append(f"{where}: length {len(a)} in spec, {len(b)} built")
        for i, (x, y) in enumerate(zip(a, b)):
            diff(x, y, f"{where}[{i}]", out)
    elif isinstance(a, float) or isinstance(b, float):
        if not (isinstance(a, (int, float)) and isinstance(b, (int, float))) or abs(a - b) > TOL:
            out.append(f"{where}: spec {a!r}, built {b!r}")
    elif a != b:
        out.append(f"{where}: spec {a!r}, built {b!r}")


def label(node_path, spec_root):
    """Turn an index path into a readable name path for reports."""
    names, n = [spec_root["name"]], spec_root
    for part in node_path:
        n = n["children"][part]
        names.append(n["name"])
    return "/".join(names)


def diff_nodes(a, b, path, root, out):
    where = label(path, root)
    for k in ("name", "active", "layer"):
        if a[k] != b[k]:
            out.append(f"{where}: {k} spec {a[k]!r}, built {b[k]!r}")
    ta = [c["type"] for c in a["components"]]
    tb = [c["type"] for c in b["components"]]
    if ta != tb:
        out.append(f"{where}: components spec {ta}, built {tb}")
    else:
        for ca, cb in zip(a["components"], b["components"]):
            diff(ca["props"], cb["props"], f"{where}<{ca['type'].split(',')[0]}>", out)
    if len(a["children"]) != len(b["children"]):
        out.append(f"{where}: {len(a['children'])} children in spec, {len(b['children'])} built")
    for i, (x, y) in enumerate(zip(a["children"], b["children"])):
        diff_nodes(x, y, path + [i], root, out)


def main(built_path):
    built = Bundle(built_path)
    out = []
    for name in PREFABS:
        with open(os.path.join(ROOT, "spec", f"{name}.json"), encoding="utf-8") as f:
            spec = json.load(f)
        if f"assets/uis/{name.lower()}.prefab" not in built.container:
            out.append(f"{name}: not in built bundle")
            continue
        got = built.prefab(name)
        diff_nodes(spec["root"], got["root"], [], spec["root"], out)

    extra = sorted(p for p in built.container
                   if p not in {f"assets/uis/{n.lower()}.prefab" for n in PREFABS})
    for p in extra:
        out.append(f"container: unexpected {p}")

    with open(os.path.join(ROOT, "spec", "sprites.json"), encoding="utf-8") as f:
        sprites = json.load(f)
    for name, want in sorted(sprites.items()):
        if name not in built.sprites:
            out.append(f"sprite {name}: not referenced by built bundle")
            continue
        got = built.sprite_meta(name)
        diff({k: want[k] for k in got}, got, f"sprite {name}", out)
        if want["game"]:
            continue  # a placeholder; the mod swaps in the game's sprite by name
        expected = Image.open(os.path.join(ROOT, want["file"])).convert("RGBA")
        actual = built.sprite_image(name).convert("RGBA")
        if expected.size != actual.size:
            out.append(f"sprite {name}: image {expected.size} in repo, {actual.size} built")
        elif ImageChops.difference(expected, actual).getbbox() is not None:
            out.append(f"sprite {name}: pixels differ")
    for name in sorted(set(built.sprites) - set(sprites)):
        out.append(f"sprite {name}: only in built bundle")
    for u in sorted(built.unhandled):
        out.append(f"unhandled: {u}")

    for line in out:
        print(line)
    print(f"{len(out)} difference(s)" if out else "identical to spec")
    return 1 if out else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
