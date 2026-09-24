using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlueprintsUi.Editor
{
    /// <summary>
    /// Builds Assets/UIs/*.prefab from spec/*.json.
    ///
    /// The spec is the prefab's serialized state: a GameObject tree whose components carry their
    /// fields under Unity's own serialized names (m_AnchorMin, m_Colors.m_NormalColor, ...). Those
    /// are exactly SerializedObject's property paths, so one generic walker applies every field of
    /// every component type, and a field the editor does not know is reported rather than dropped.
    ///
    /// References are tagged objects: {"$sprite": name}, {"$builtinFont": file}, and
    /// {"$ref": "0/3/1", "$type": ..., "$index": n} for an object inside the same prefab, addressed
    /// by child indices from the root because sibling names are not unique.
    /// </summary>
    public static class PrefabBuilder
    {
        public const string OutputDir = "Assets/UIs";

        public static readonly List<string> Problems = new List<string>();

        public static void BuildAll()
        {
            Problems.Clear();
            Directory.CreateDirectory(OutputDir);
            foreach (string path in Directory.GetFiles("spec", "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Path.GetFileName(path) == "sprites.json")
                    continue;
                Build(path);
            }
            AssetDatabase.SaveAssets();
        }

        sealed class Pending
        {
            public Component Component;
            public Dictionary<string, object> Props;
            public string Where;
        }

        static void Build(string specPath)
        {
            var spec = (Dictionary<string, object>)Json.Parse(File.ReadAllText(specPath));
            string name = (string)spec["name"];
            var nodes = new Dictionary<string, GameObject>();
            var pending = new List<Pending>();

            GameObject root = CreateNode((Dictionary<string, object>)spec["root"], null, "", nodes, pending);
            try
            {
                // Fields go on after the whole tree exists, so references to later siblings resolve.
                foreach (var p in pending)
                    Apply(p, nodes);
                PrefabUtility.SaveAsPrefabAsset(root, $"{OutputDir}/{name}.prefab", out bool ok);
                if (!ok)
                    Problems.Add($"{name}: SaveAsPrefabAsset failed");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject CreateNode(Dictionary<string, object> node, GameObject parent, string path,
            Dictionary<string, GameObject> nodes, List<Pending> pending)
        {
            var go = new GameObject((string)node["name"], typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
            go.layer = Convert.ToInt32(node["layer"]);
            nodes[path] = go;
            string where = Describe(go);

            var seen = new Dictionary<Type, int>();
            foreach (Dictionary<string, object> comp in (List<object>)node["components"])
            {
                string typeName = (string)comp["type"];
                Type type = ResolveType(typeName);
                if (type == null)
                {
                    Problems.Add($"{where}: unknown component type {typeName}");
                    continue;
                }
                seen.TryGetValue(type, out int index);
                seen[type] = index + 1;
                Component[] existing = go.GetComponents(type);
                Component component = index < existing.Length ? existing[index] : go.AddComponent(type);
                if (component == null)
                {
                    Problems.Add($"{where}: could not add {typeName}");
                    continue;
                }
                pending.Add(new Pending
                {
                    Component = component,
                    Props = (Dictionary<string, object>)comp["props"],
                    Where = $"{where}<{type.Name}>",
                });
            }

            var children = (List<object>)node["children"];
            for (int i = 0; i < children.Count; i++)
            {
                string childPath = path.Length == 0 ? i.ToString() : $"{path}/{i}";
                CreateNode((Dictionary<string, object>)children[i], go, childPath, nodes, pending);
            }

            go.SetActive((bool)node["active"]);
            return go;
        }

        static Type ResolveType(string name)
        {
            switch (name)
            {
                case "GameObject": return typeof(GameObject);
                case "RectTransform": return typeof(RectTransform);
                case "Transform": return typeof(Transform);
                case "CanvasRenderer": return typeof(CanvasRenderer);
                default: return Type.GetType(name);
            }
        }

        static void Apply(Pending p, Dictionary<string, GameObject> nodes)
        {
            var so = new SerializedObject(p.Component);
            foreach (var kv in p.Props)
                Set(so, kv.Key, kv.Value, nodes, p.Where);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Set(SerializedObject so, string path, object value, Dictionary<string, GameObject> nodes, string where)
        {
            SerializedProperty prop = so.FindProperty(path);
            if (prop == null)
            {
                Problems.Add($"{where}: no property {path}");
                return;
            }

            if (value is Dictionary<string, object> dict)
            {
                if (dict.Keys.Any(k => k.StartsWith("$", StringComparison.Ordinal)))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        Problems.Add($"{where}: {path} is {prop.propertyType}, not a reference");
                        return;
                    }
                    prop.objectReferenceValue = Resolve(dict, nodes, $"{where}.{path}");
                    return;
                }
                if (SetStruct(prop, dict))
                    return;
                foreach (var kv in dict)
                    Set(so, $"{path}.{kv.Key}", kv.Value, nodes, where);
                return;
            }

            if (value is List<object> list)
            {
                if (!prop.isArray)
                {
                    Problems.Add($"{where}: {path} is not an array");
                    return;
                }
                prop.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++)
                    Set(so, $"{path}.Array.data[{i}]", list[i], nodes, where);
                return;
            }

            if (value == null)
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                    prop.objectReferenceValue = null;
                else if (prop.propertyType == SerializedPropertyType.ManagedReference)
                    prop.managedReferenceValue = null;
                else
                    Problems.Add($"{where}: null for non-reference {path} ({prop.propertyType})");
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.longValue = Convert.ToInt64(value);
                    break;
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                    // intValue writes the underlying value; enumValueIndex would treat it as an index
                    prop.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value is bool b ? b : Convert.ToInt64(value) != 0;
                    break;
                case SerializedPropertyType.Float:
                    prop.doubleValue = Convert.ToDouble(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = (string)value;
                    break;
                default:
                    Problems.Add($"{where}: cannot set {path} ({prop.propertyType}) from {value.GetType().Name}");
                    break;
            }
        }

        /// <summary>Sets the value types Unity exposes as a whole rather than as children.</summary>
        static bool SetStruct(SerializedProperty prop, Dictionary<string, object> d)
        {
            float F(string k) => Convert.ToSingle(d[k]);
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Color:
                    prop.colorValue = new Color(F("r"), F("g"), F("b"), F("a"));
                    return true;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = new Vector2(F("x"), F("y"));
                    return true;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = new Vector3(F("x"), F("y"), F("z"));
                    return true;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = new Vector4(F("x"), F("y"), F("z"), F("w"));
                    return true;
                case SerializedPropertyType.Quaternion:
                    prop.quaternionValue = new Quaternion(F("x"), F("y"), F("z"), F("w"));
                    return true;
                case SerializedPropertyType.Rect:
                    prop.rectValue = new Rect(F("x"), F("y"), F("width"), F("height"));
                    return true;
                default:
                    return false;
            }
        }

        static Object Resolve(Dictionary<string, object> r, Dictionary<string, GameObject> nodes, string where)
        {
            if (r.TryGetValue("$sprite", out object sprite))
            {
                var s = SpriteSpec.Load((string)sprite);
                if (s == null)
                    Problems.Add($"{where}: sprite {sprite} not found");
                return s;
            }
            if (r.TryGetValue("$builtinFont", out object font))
                return Resources.GetBuiltinResource<Font>((string)font);
            if (r.TryGetValue("$ref", out object refPath))
            {
                if (!nodes.TryGetValue((string)refPath, out GameObject go))
                {
                    Problems.Add($"{where}: no node at {refPath}");
                    return null;
                }
                string typeName = (string)r["$type"];
                if (typeName == "GameObject")
                    return go;
                Type type = ResolveType(typeName);
                int index = r.TryGetValue("$index", out object i) ? Convert.ToInt32(i) : 0;
                Component[] comps = type == null ? Array.Empty<Component>() : go.GetComponents(type);
                if (index >= comps.Length)
                {
                    Problems.Add($"{where}: {refPath} has no {typeName}[{index}]");
                    return null;
                }
                return comps[index];
            }
            Problems.Add($"{where}: unknown reference {string.Join(",", r.Keys)}");
            return null;
        }

        static string Describe(GameObject go)
        {
            var names = new List<string>();
            for (Transform t = go.transform; t != null; t = t.parent)
                names.Insert(0, t.name);
            return string.Join("/", names);
        }
    }
}
