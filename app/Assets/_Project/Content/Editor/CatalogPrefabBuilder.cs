using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Builds one low-poly placeholder prefab per catalog item from primitives, the shared style
    /// materials, and the CatalogPrefabs asset. Idempotent: running it again rewrites the same files.
    /// Batch mode: Unity -batchmode -nographics -projectPath app -executeMethod OwnSpace.Content.Editor.CatalogPrefabBuilder.BuildBatch -quit
    /// </summary>
    public static class CatalogPrefabBuilder
    {
        const string CatalogPath = "Assets/_Project/Content/catalog.json";
        const string MaterialDir = "Assets/_Project/Art/Materials";
        const string MapPath = "Assets/_Project/Content/CatalogPrefabs.asset";
        const float BoundsTolerance = 0.002f;

        [Serializable] class CatalogFile { public Entry[] items = Array.Empty<Entry>(); }
        [Serializable] class Entry
        {
            public string id, kind, primaryColor;
            public Size size;
            public float usableHeight;
            public string[] styles;
            public AssetRef asset;
            public string attachTo;
            public LampData light;
        }
        [Serializable] class LampData { public float kelvin, intensity, range, glowRadius, sourceHeight; }

        // Reference openings (m) a curtain or trim is built around; the app refits it to the real one at runtime.
        const float WindowW = 1.2f, WindowH = 1.2f, DoorW = 0.9f, DoorH = 2.0f;
        [Serializable] class Size { public float x, y, z; }
        [Serializable] class AssetRef { public string path; }

        // Palette slots per style, from docs/design/style-board.md. Glow slots carry a small emission.
        // 2800 K white is about (1.00, 0.70, 0.40). Shades follow the weave at 0.6, bulbs and globes sit at 0.9. Never HDR: 2.5 clipped every shade to a white block.
        static readonly Color ShadeGlow = new Color(1.00f, 0.70f, 0.40f) * 0.6f;
        static readonly Color BulbGlow = new Color(1.00f, 0.70f, 0.40f) * 0.9f;
        static readonly string[] Slots = { "main", "alt", "soft", "accent", "dark", "green", "glow", "fabric" };
        static readonly Dictionary<string, string[]> Palettes = new Dictionary<string, string[]>
        {
            { "warm-wood",  new[] { "C9A27A", "8A5A3C", "F1E6D3", "8E9B7A", "3A3532", "6E8B57", "F4E2B8", "B96A4B" } },
            { "scandi",     new[] { "DCC7A1", "B8BCBE", "EDE9E0", "8FA3B5", "2F3133", "7F9A78", "F3EBD8", "B8BCBE" } },
            { "cafe",       new[] { "4B3428", "A8693A", "E4DCCF", "B08D4A", "2B211B", "5E6B3F", "F0CF8A", "A8693A" } },
            { "night-city", new[] { "1F2A3D", "3C4A5E", "7C8896", "8FA8D8", "14181F", "4F6B5A", "F2B35E", "3C4A5E" } },
        };

        [MenuItem("OwnSpace/Content/Build Placeholder Prefabs")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e)
            {
                Debug.LogError($"[OwnSpace.Content] Build failed: {e}");
                code = 2;
            }
            EditorApplication.Exit(code);
        }

        static bool Run()
        {
            AssetDatabase.Refresh();
            var catalog = JsonUtility.FromJson<CatalogFile>(File.ReadAllText(CatalogPath));
            var entries = new List<CatalogPrefabs.Entry>();
            int bad = 0;
            foreach (var item in catalog.items)
            {
                var root = BuildItem(item);
                string error = CheckBounds(root, item) ?? CheckMaterials(root);
                if (error != null) { Debug.LogError($"[OwnSpace.Content] {item.id}: {error}"); bad++; }
                EnsureFolder(Path.GetDirectoryName(item.asset.path).Replace('\\', '/'));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, item.asset.path);
                UnityEngine.Object.DestroyImmediate(root);
                entries.Add(new CatalogPrefabs.Entry { id = item.id, prefab = prefab });
            }
            WriteMap(entries);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OwnSpace.Content] Built {entries.Count} prefabs, {bad} with wrong bounds.");
            return bad == 0;
        }

        static void WriteMap(List<CatalogPrefabs.Entry> entries)
        {
            var map = AssetDatabase.LoadAssetAtPath<CatalogPrefabs>(MapPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<CatalogPrefabs>();
                AssetDatabase.CreateAsset(map, MapPath);
            }
            var so = new SerializedObject(map);
            var list = so.FindProperty("_entries");
            list.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").stringValue = entries[i].id;
                e.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
        }

        // ---- item shapes. Local frame: pivot at the bottom center, +Z front, size = catalog size. ----

        static GameObject BuildItem(Entry item)
        {
            string style = item.styles != null && item.styles.Length > 0 && Palettes.ContainsKey(item.styles[0]) ? item.styles[0] : "warm-wood";
            var root = new GameObject(item.id);
            float w = item.size.x, h = item.size.y, d = item.size.z, u = item.usableHeight;
            Func<string, Material> m = slot => StyleMaterial(style, slot);

            if (!TryImported(item, root))
            {
                switch (item.kind)
                {
                    case "desk":
                    case "coffee-table":
                        if (item.kind == "coffee-table") u = h;
                        Part(root, "Top", PrimitiveType.Cube, m("main"), 0, u - 0.02f, 0, w, 0.04f, d);
                        foreach (var p in Corners(w, d, 0.04f))
                            Part(root, "Leg", PrimitiveType.Cube, m("alt"), p.x, (u - 0.04f) / 2f, p.y, 0.05f, u - 0.04f, 0.05f);
                        break;
                    case "bed":
                        float frame = u - 0.16f;
                        Part(root, "Frame", PrimitiveType.Cube, m("main"), 0, frame / 2f, 0, w, frame, d);
                        Part(root, "Mattress", PrimitiveType.Cube, m("soft"), 0, frame + 0.08f, 0, w - 0.04f, 0.16f, d - 0.04f);
                        Part(root, "Headboard", PrimitiveType.Cube, m("alt"), 0, h / 2f, -d / 2f + 0.025f, w, h, 0.05f);
                        break;
                    case "closet":
                    {
                        // A wardrobe: plinth, carcass, crown, two panelled doors and bar handles, in tiling PBR wood at true scale (UVs in metres, see BoxMesh).
                        const string wood = "Assets/_Project/Art/Finishes/Tex";
                        string set = style == "warm-wood" || style == "scandi" ? "floor-birch-planks" : "floor-dark-planks";
                        var carcassMat = FurniturePbr($"Wood_wardrobe_{style}_carcass", wood, set, StyleColor(style, "main"), 0.6f);
                        var doorMat = FurniturePbr($"Wood_wardrobe_{style}_door", wood, set, StyleColor(style, "alt"), 0.6f);
                        string key = $"{item.id}";
                        float fz = d / 2f;   // front plane
                        var carcass = new List<Bounds>
                        {
                            new Bounds(new Vector3(0f, 0.04f, -0.02f), new Vector3(w - 0.04f, 0.08f, d - 0.04f)),                       // plinth, set back
                            new Bounds(new Vector3(0f, (0.08f + h - 0.04f) / 2f, -0.015f), new Vector3(w, h - 0.12f, d - 0.03f)),        // carcass
                            new Bounds(new Vector3(0f, h - 0.02f, 0f), new Vector3(w, 0.04f, d)),                                         // crown, full footprint
                        };
                        var doors = new List<Bounds>(); var handles = new List<Bounds>();
                        float dw = w / 2f - 0.015f, dh = h - 0.14f, dy = 0.09f + dh / 2f;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float cx = s * (w / 4f);
                            doors.Add(new Bounds(new Vector3(cx, dy, fz - 0.02f), new Vector3(dw, dh, 0.018f)));                      // door slab
                            doors.Add(new Bounds(new Vector3(cx, dy, fz - 0.008f), new Vector3(dw - 0.12f, dh - 0.12f, 0.016f)));    // raised panel
                            handles.Add(new Bounds(new Vector3(s * 0.045f, h * 0.5f, fz - 0.004f), new Vector3(0.014f, 0.22f, 0.008f)));
                        }
                        MeshPart(root, "Carcass", BoxMesh($"{key}_carcass", carcass), carcassMat);
                        MeshPart(root, "Doors", BoxMesh($"{key}_doors", doors), doorMat);
                        MeshPart(root, "Handles", BoxMesh($"{key}_handles", handles), FurniturePbr($"Metal_handles_{style}", "Assets/_Project/Art/Surfaces/Tex", "lamp_metal", LampMetalTint(style), 0.3f));
                        break;
                    }
                    case "sofa":
                    case "armchair":
                        foreach (var p in Corners(w, d, 0.06f))
                            Part(root, "Leg", PrimitiveType.Cube, m("dark"), p.x, 0.05f, p.y, 0.04f, 0.1f, 0.04f);
                        Part(root, "Base", PrimitiveType.Cube, m("fabric"), 0, 0.25f, 0, w, 0.3f, d);
                        Part(root, "Back", PrimitiveType.Cube, m("fabric"), 0, (0.4f + h) / 2f, -d / 2f + 0.11f, w, h - 0.4f, 0.22f);
                        for (int s = -1; s <= 1; s += 2)
                            Part(root, "Arm", PrimitiveType.Cube, m("fabric"), s * (w / 2f - 0.075f), 0.525f, 0, 0.15f, 0.25f, d);
                        Part(root, "Cushion", PrimitiveType.Cube, m("soft"), 0, 0.46f, 0.11f, w - 0.3f, 0.12f, d - 0.22f);
                        break;
                    case "shelf":
                    {
                        // Sides and boards in tiling PBR wood at true scale (UVs in metres, see BoxMesh); the back panel in the second tone.
                        string woodDir = "Assets/_Project/Art/Finishes/Tex", woodSet = style == "warm-wood" || style == "scandi" ? "floor-birch-planks" : "floor-dark-planks";
                        var frameMat = FurniturePbr($"Wood_shelf_{style}_frame", woodDir, woodSet, StyleColor(style, "main"), 0.6f);
                        var backMat = FurniturePbr($"Wood_shelf_{style}_back", woodDir, woodSet, StyleColor(style, "alt"), 0.6f);
                        var shelfBoxes = new List<Bounds>();
                        for (int s = -1; s <= 1; s += 2)
                            shelfBoxes.Add(new Bounds(new Vector3(s * (w / 2f - 0.015f), h / 2f, 0f), new Vector3(0.03f, h, d)));
                        int boards = Mathf.Max(3, Mathf.RoundToInt(h / 0.4f) + 1);
                        for (int i = 0; i < boards; i++)
                            shelfBoxes.Add(new Bounds(new Vector3(0f, 0.0125f + i * (h - 0.025f) / (boards - 1), 0f), new Vector3(w - 0.06f, 0.025f, d)));
                        MeshPart(root, "Frame", BoxMesh($"{item.id}_frame", shelfBoxes), frameMat);
                        MeshPart(root, "Back", BoxMesh($"{item.id}_back", new List<Bounds> { new Bounds(new Vector3(0f, h / 2f, -d / 2f + 0.005f), new Vector3(w - 0.06f, h, 0.01f)) }), backMat);
                        break;
                    }
                    case "lamp":
                    {
                        // Brushed-metal base and stand and a linen shade that glows warm (the weave stays visible), all on the furniture shader.
                        float baseD = Mathf.Max(0.18f, w * 0.6f);
                        var metal = FurniturePbr($"Metal_lamp_{style}", "Assets/_Project/Art/Surfaces/Tex", "lamp_metal", LampMetalTint(style), 0.3f);
                        var shade = FurniturePbr($"Linen_shade_{style}", "Assets/_Project/Art/Fabrics/Tex", "curtain_linen", new Color(0.9f, 0.76f, 0.54f), 0.25f);
                        shade.SetTextureScale("_BaseMap", new Vector2(Mathf.PI * w / 0.25f, 0.28f / 0.25f));   // a cylinder's UV runs 0..1 around and along it
                        shade.SetColor("_EmissionColor", ShadeGlow); shade.SetFloat("_GlowByAlbedo", 1f); shade.SetFloat("_MeanLum", 0.7f);   // about 0.6 of a 2800 K white: the weave stays visible
                        shade.SetFloat("_NormalStrength", 0.5f);
                        Part(root, "Base", PrimitiveType.Cylinder, metal, 0, 0.015f, 0, baseD, 0.03f, baseD);
                        Part(root, "Pole", PrimitiveType.Cylinder, metal, 0, (0.03f + h - 0.28f) / 2f, 0, 0.02f, h - 0.31f, 0.02f);
                        Part(root, "Shade", PrimitiveType.Cylinder, shade, 0, h - 0.14f, 0, w, 0.28f, d);
                        break;
                    }
                    case "plant":
                    {
                        // A plaster pot, a bark stem and leaf-shaped meshes (the ambientCG Leaf001 photo) in a spiral; the foliage is then fitted to the catalog size.
                        float potD = w * 0.7f;
                        var pot = FurniturePbr($"Plaster_pot_{style}", "Assets/_Project/Art/Surfaces/Tex", "plant_pot", StyleColor(style, "alt"), 0.3f);
                        pot.SetTextureScale("_BaseMap", new Vector2(Mathf.PI * potD / 0.3f, 1f));
                        var bark = FurniturePbr("Bark_plant", "Assets/_Project/Art/Surfaces/Tex", "plant_bark", new Color(0.62f, 0.46f, 0.32f), 0.3f);
                        bark.SetTextureScale("_BaseMap", new Vector2(0.4f, 3f));
                        var leaf = FurniturePbr("Leaf_plant", "Assets/_Project/Art/Surfaces/Tex", "plant_leaf", new Color(0.7f, 0.7f, 0.7f), 1f);   // the leaf photo keeps its colour; 0.7 cancels the set's gain
                        leaf.SetTextureScale("_BaseMap", Vector2.one);
                        leaf.SetFloat("_NormalStrength", 0.6f); leaf.SetFloat("_Smoothness", 0.35f);
                        Part(root, "Pot", PrimitiveType.Cylinder, pot, 0, 0.15f, 0, potD, 0.3f, potD);
                        // The soil sits 5 mm ABOVE the pot top (its top at 0.305, the pot ends at 0.30): the old 0.30 coincided with the pot's cap and z-fought (black and white alternating on device).
                        Part(root, "Soil", PrimitiveType.Cylinder, m("dark"), 0, 0.29f, 0, potD * 0.9f, 0.03f, potD * 0.9f);
                        var fol = new GameObject("Foliage").transform; fol.SetParent(root.transform, false);
                        float leafLen = w * 0.62f;
                        int count = Mathf.RoundToInt(h * 11f);
                        float stemTop = h - leafLen * 0.9f, stemBase = 0.3f;
                        var stem = Part(root, "Stem", PrimitiveType.Cylinder, bark, 0, (stemBase + stemTop) / 2f, 0, 0.03f, stemTop - stemBase, 0.03f);
                        stem.transform.SetParent(fol, false);
                        var leafMesh = LeafMesh();
                        for (int i = 0; i < count; i++)
                        {
                            float tt = i / (count - 1f);
                            float yy = Mathf.Lerp(stemBase + 0.08f, stemTop, tt);
                            float elev = Mathf.Lerp(25f, 85f, tt);
                            float sz = leafLen * Mathf.Lerp(1f, 0.8f, tt);
                            var lg = MeshPart(root, "Leaf", leafMesh, leaf);
                            lg.transform.SetParent(fol, false);
                            lg.transform.localPosition = new Vector3(0, yy, 0);
                            lg.transform.localRotation = Quaternion.Euler(0, i * 137.5f, 0) * Quaternion.Euler(90f - elev, 0, 0);
                            lg.transform.localScale = Vector3.one * sz;
                        }
                        FitFoliage(root, fol, w, d, h);
                        break;
                    }
                    case "pendant-lamp":
                        Part(root, "Cord", PrimitiveType.Cylinder, FurniturePbr($"Metal_cord_{style}", "Assets/_Project/Art/Surfaces/Tex", "lamp_metal", LampMetalTint(style), 0.3f), 0, (0.25f + h) / 2f, 0, 0.01f, h - 0.25f, 0.01f);
                        Part(root, "Shade", PrimitiveType.Cylinder, m("glow"), 0, 0.125f, 0, w, 0.25f, d);
                        break;
                    case "wall-art":
                        Part(root, "Frame", PrimitiveType.Cube, m("alt"), 0, h / 2f, 0, w, h, d);
                        Part(root, "Canvas", PrimitiveType.Cube, m("accent"), 0, h / 2f, d / 2f - 0.005f, w - 0.06f, h - 0.06f, 0.01f);
                        break;
                    case "curtain":
                    case "door-trim":
                    {
                        bool curtain = item.kind == "curtain";
                        float ow = item.attachTo == "door" ? DoorW : WindowW, oh = item.attachTo == "door" ? DoorH : WindowH;
                        float side = (w - ow) / 2f;
                        // Curtains: folded fabric panels (a wave mesh) with a CC0 linen set; trims: a CC0 wood set. Both tinted per style.
                        var tint = curtain ? Color.Lerp(StyleColor(style, "fabric"), StyleColor(style, "soft"), 0.4f) : StyleColor(style, "main");   // a curtain is cloth, not a saturated rod: pull the colour toward the light linen
                        var mat = curtain ? PbrMaterial($"Fabric_curtain_{style}", "Assets/_Project/Art/Fabrics/Tex", "curtain_linen", tint, 0.5f, 0.5f, 0.1f)   // fabric: smoothness <= 0.1, soft normals
                                          : PbrMaterial($"Wood_trim_{style}", "Assets/_Project/Art/Finishes/Tex", "floor-birch-planks", tint, 0.9f);
                        var left = (curtain ? MeshPart(root, "Left", CurtainMesh(), mat) : MeshPart(root, "Left", MouldingMesh(false), mat)).transform;
                        var right = (curtain ? MeshPart(root, "Right", CurtainMesh(), mat) : MeshPart(root, "Right", MouldingMesh(false), mat)).transform;
                        var top = (curtain ? Part(root, "Rod", PrimitiveType.Cube, FurniturePbr($"Metal_rod_{style}", "Assets/_Project/Art/Surfaces/Tex", "lamp_metal", LampMetalTint(style), 0.3f), 0, 0, 0, 1, 1, 1) : MeshPart(root, "Top", MouldingMesh(true), mat)).transform;
                        var openingFrame = root.AddComponent<OpeningFrame>();
                        openingFrame.Configure(curtain ? OpeningFrame.Mode.Curtain : OpeningFrame.Mode.Trim, left, right, top, side, d);
                        openingFrame.Fit(ow, oh);
                        break;
                    }
                    case "rug":
                        Part(root, "Rug", PrimitiveType.Cube, PbrMaterial($"Fabric_rug_{style}", "Assets/_Project/Art/Fabrics/Tex", "rug_wool", StyleColor(style, "alt"), 0.45f), 0, h / 2f, 0, w, h, d);
                        break;
                    case "clutter-cover" when item.id.Contains("cushions"):
                    {
                        // Three stacked pillows (smooth lathe shapes) in tiling linen.
                        float t = h / 3f;
                        var cushions = new[] { ("fabric", 1f, 0f, 0f), ("accent", 0.85f, 0.02f, 0.02f), ("soft", 0.7f, -0.02f, 0f) };
                        for (int i = 0; i < 3; i++)
                        {
                            var (slot, k, ox, oz) = cushions[i];
                            var linen = FurniturePbr($"Linen_cushion_{style}_{slot}", "Assets/_Project/Art/Fabrics/Tex", "curtain_linen", StyleColor(style, slot), 0.25f);
                            linen.SetFloat("_NormalStrength", 0.6f);
                            var go = MeshPart(root, "Cushion", LatheMesh($"{item.id}_cushion{i}", w * k / 2f, d * k / 2f, t, PillowProfile), linen);
                            go.transform.localPosition = new Vector3(ox, i * t, oz);
                        }
                        break;
                    }
                    case "clutter-cover" when item.id.Contains("floor-cushion"):
                    {
                        var linen = FurniturePbr($"Linen_cushion_{style}_floor", "Assets/_Project/Art/Fabrics/Tex", "curtain_linen", StyleColor(style, "fabric"), 0.25f);
                        linen.SetFloat("_NormalStrength", 0.6f);
                        MeshPart(root, "Cushion", LatheMesh($"{item.id}_cushion", w / 2f, d / 2f, h, PillowProfile), linen);
                        break;
                    }
                    case "clutter-cover" when item.id.Contains("box"):
                    {
                        // A wooden storage box: body and lid in tiling PBR wood at true scale, two dark hand slots on the front.
                        string set = style == "warm-wood" || style == "scandi" ? "floor-birch-planks" : "floor-dark-planks";
                        var body = FurniturePbr($"Wood_box_{style}_body", "Assets/_Project/Art/Finishes/Tex", set, StyleColor(style, "main"), 0.6f);
                        var lid = FurniturePbr($"Wood_box_{style}_lid", "Assets/_Project/Art/Finishes/Tex", set, StyleColor(style, "alt"), 0.6f);
                        MeshPart(root, "Body", BoxMesh($"{item.id}_body", new List<Bounds> { new Bounds(new Vector3(0, (h - 0.03f) / 2f, 0), new Vector3(w, h - 0.03f, d)) }), body);
                        MeshPart(root, "Lid", BoxMesh($"{item.id}_lid", new List<Bounds> { new Bounds(new Vector3(0, h - 0.015f, 0), new Vector3(w, 0.03f, d)) }), lid);
                        var slots = new List<Bounds>();
                        foreach (float sx in new[] { -0.2f, 0.2f }) slots.Add(new Bounds(new Vector3(sx * w / 0.6f, h * 0.55f, d / 2f - 0.001f), new Vector3(0.12f, 0.025f, 0.004f)));
                        MeshPart(root, "Slots", BoxMesh($"{item.id}_slots", slots), m("dark"));
                        break;
                    }
                    case "clutter-cover" when item.id.Contains("screen"):
                    {
                        // Three hinged panels in a zigzag: a wood frame around a woven infill (Wicker001). Panel width solves x extent = w for the 25 degree fold.
                        const float fold = 25f, thick = 0.03f, stile = 0.05f, rail = 0.06f;
                        float cos = Mathf.Cos(fold * Mathf.Deg2Rad), sin = Mathf.Sin(fold * Mathf.Deg2Rad);
                        float panel = (w - thick * sin) / (3f * cos);
                        string set = style == "warm-wood" || style == "scandi" ? "floor-birch-planks" : "floor-dark-planks";
                        var frameMat = FurniturePbr($"Wood_screen_{style}", "Assets/_Project/Art/Finishes/Tex", set, StyleColor(style, "alt"), 0.6f);
                        var wick = FurniturePbr($"Wicker_{style}", "Assets/_Project/Art/Surfaces/Tex", "clutter_wicker", StyleColor(style, "main"), 0.4f);
                        var frameMesh = BoxMesh($"{item.id}_frame", new List<Bounds> {
                            new Bounds(new Vector3(-panel / 2f + stile / 2f, 0, 0), new Vector3(stile, h, thick)),
                            new Bounds(new Vector3(panel / 2f - stile / 2f, 0, 0), new Vector3(stile, h, thick)),
                            new Bounds(new Vector3(0, h / 2f - rail / 2f, 0), new Vector3(panel - 2f * stile, rail, thick)),
                            new Bounds(new Vector3(0, -h / 2f + rail / 2f, 0), new Vector3(panel - 2f * stile, rail, thick)) });
                        var inMesh = BoxMesh($"{item.id}_infill", new List<Bounds> { new Bounds(Vector3.zero, new Vector3(panel - 2f * stile, h - 2f * rail, 0.012f)) });
                        for (int i = -1; i <= 1; i++)
                        {
                            foreach (var (mesh, mat) in new[] { (frameMesh, frameMat), (inMesh, wick) })
                            {
                                var go = MeshPart(root, "Panel", mesh, mat);
                                go.transform.localPosition = new Vector3(i * panel * cos, h / 2f, 0);
                                go.transform.localRotation = Quaternion.Euler(0f, i == 0 ? -fold : fold, 0f);
                            }
                        }
                        break;
                    }
                    case "clutter-cover" when item.id.Contains("plant"):
                        Part(root, "Pot", PrimitiveType.Cylinder, m("alt"), 0, 0.2f, 0, w * 0.8f, 0.4f, d * 0.8f);
                        Part(root, "Stem", PrimitiveType.Cylinder, m("dark"), 0, (0.4f + h - w / 2f) / 2f, 0, 0.03f, h - w / 2f - 0.4f, 0.03f);
                        Part(root, "Leaves", PrimitiveType.Sphere, m("green"), 0, h - w / 2f, 0, w, w, d);
                        break;
                    case "clutter-cover":
                    {
                        // A woven basket: a tapered wall in tiling wicker (smooth lathe, UVs in metres), a wood rim and a dark inside.
                        string set = style == "warm-wood" || style == "scandi" ? "floor-birch-planks" : "floor-dark-planks";
                        var wick = FurniturePbr($"Wicker_{style}", "Assets/_Project/Art/Surfaces/Tex", "clutter_wicker", StyleColor(style, "main"), 0.4f);
                        var rimMat = FurniturePbr($"Wood_basket_rim_{style}", "Assets/_Project/Art/Finishes/Tex", set, StyleColor(style, "alt"), 0.4f);
                        float rx = w / 2f, rz = d / 2f;
                        MeshPart(root, "Wall", LatheMesh($"{item.id}_wall", rx, rz, h, new[] { 0.78f, 0f, 0.97f, h - 0.05f }), wick);
                        MeshPart(root, "Rim", LatheMesh($"{item.id}_rim", rx, rz, h, new[] { 0.97f, h - 0.05f, 1f, h - 0.04f, 1f, h - 0.012f, 0.98f, h, 0.93f, h }), rimMat);
                        MeshPart(root, "Inside", LatheMesh($"{item.id}_inside", rx, rz, h, new[] { 0.93f, h, 0.86f, h - 0.07f, 0f, h - 0.07f }), m("dark"));
                        break;
                    }
                    default:
                        Part(root, "Body", PrimitiveType.Cube, m("main"), 0, h / 2f, 0, w, h, d);
                        break;
                }
            }
            if (item.light != null && item.light.range > 0f)
                root.AddComponent<LampLight>().Configure(item.light.kelvin, item.light.intensity, item.light.range, item.light.glowRadius, item.light.sourceHeight);
            return root;
        }

        // ---- imported CC0 models (Art/Models/<source>/<id>.obj, written by the import script, see docs/design/asset-licenses.md) ----

        [Serializable] class ModelMat { public string name, texture, normal, rough; public float[] color, mean; public float meanLum; }
        [Serializable] class ModelInfo { public string slug; public ModelMat[] materials = Array.Empty<ModelMat>(); }

        const string ModelDir = "Assets/_Project/Art/Models";

        /// <summary>Uses the imported model for this item, if one exists (already scaled to catalog size, pivot bottom center, +Z front).</summary>
        static bool TryImported(Entry item, GameObject root)
        {
            if (!Directory.Exists(ModelDir)) return false;
            string[] found = Directory.GetFiles(ModelDir, item.id + ".model.json", SearchOption.AllDirectories);
            if (found.Length == 0) return false;
            string json = found[0].Replace('\\', '/');
            string objPath = json.Replace(".model.json", ".obj");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
            if (model == null) { Debug.LogError($"[OwnSpace.Content] {item.id}: {objPath} did not import."); return false; }
            var info = JsonUtility.FromJson<ModelInfo>(File.ReadAllText(json));
            var mats = new Material[info.materials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = ModelMaterial(Path.GetDirectoryName(json).Replace('\\', '/'), item.id, info.materials[i], item.primaryColor, item.light != null && item.light.range > 0f, item.styles != null && item.styles.Length > 0 ? item.styles[0] : "warm-wood", info.slug);
            var inst = UnityEngine.Object.Instantiate(model);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                var shared = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                // A model written with "g <material>" groups imports as one child mesh per material, named after it (Unity ignores usemtl in an OBJ).
                int byName = Array.FindIndex(info.materials, mm => mm.name == r.gameObject.name);
                for (int i = 0; i < shared.Length; i++) shared[i] = mats[byName >= 0 ? byName : Mathf.Min(i, mats.Length - 1)];
                r.sharedMaterials = shared;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return true;
        }

        static Material ModelMaterial(string dir, string id, ModelMat m, string primaryColor, bool lamp, string style, string slug)
        {
            string path = $"{dir}/{id}_{m.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(FurnitureShader()); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = FurnitureShader();
            // The same model serves several catalog items (three armchairs, four sofas): the item's primaryColor is MULTIPLIED over the albedo
            // (70% toward the colour), so the texture's grain and shading stay. A very dark albedo (black leather) cannot be lightened by a
            // multiply, so it is recoloured instead: its detail is kept and its colour comes from the item's colour.
            var color = new Color(m.color[0], m.color[1], m.color[2], 1f);
            bool textured = !string.IsNullOrEmpty(m.texture);
            bool recolor = false;
            if (ColorUtility.TryParseHtmlString(primaryColor, out var variant))
            {
                if (textured && m.meanLum > 0f && m.meanLum < 0.15f) { recolor = true; color = variant; }
                else color = textured ? Color.Lerp(Color.white, variant, 0.7f) : Color.Lerp(color, variant, 0.7f);
            }
            if (textured)
            {
                mat.SetTexture("_BaseMap", FinishBuilder.ImportTexture(m.texture, true));
                bool maps = !string.IsNullOrEmpty(m.normal) && !string.IsNullOrEmpty(m.rough);
                mat.SetFloat("_UseMaps", maps ? 1f : 0f);
                mat.SetTexture("_NormalMap", maps ? FinishBuilder.ImportTexture(m.normal, false) : null);
                mat.SetTexture("_RoughMap", maps ? FinishBuilder.ImportTexture(m.rough, false) : null);
            }
            // A lamp's shade glows warm (about 0.6 of a 2800 K white, modulated by the weave) and a bulb or globe a little more (0.9, still under 1 so nothing clips to white); xr's LampLight and the floor glow supply the light it casts.
            bool glows = lamp && (m.name == "lamp" || m.name.Contains("globe") || m.name.Contains("bulb") || m.name.Contains("shade"));
            bool fabricShade = glows && !textured;
            if (fabricShade)   // a flat-colour lamp shade (Kenney): the CC0 linen weave, so the shade is cloth and not a grey disc
            {
                const string linen = "Assets/_Project/Art/Fabrics/Tex/curtain_linen";
                mat.SetTexture("_BaseMap", FinishBuilder.ImportTexture(linen + "_albedo.jpg", true));
                mat.SetTexture("_NormalMap", FinishBuilder.ImportTexture(linen + "_normal.jpg", false));
                mat.SetTexture("_RoughMap", FinishBuilder.ImportTexture(linen + "_rough.jpg", false));
                mat.SetTextureScale("_BaseMap", new Vector2(1f / 0.25f, 1f / 0.25f));   // the shade has box-projected UVs in metres (add_box_uvs.py)
                mat.SetFloat("_UseMaps", 1f);
                color = new Color(1f, 0.86f, 0.68f);   // warm cloth whatever the style colour: a cool or green shade lit by a warm glow turned yellow-green
            }
            else if (!textured && FlatPbr(slug, m.name, style, out var set))
            {
                // A flat-colour Kenney model with box-projected UVs in metres (docs/design/add_box_uvs.py): a tiling CC0 set at true scale, tinted per style.
                mat.SetTexture("_BaseMap", FinishBuilder.ImportTexture(set.dir + "/" + set.name + "_albedo.jpg", true));
                mat.SetTexture("_NormalMap", FinishBuilder.ImportTexture(set.dir + "/" + set.name + "_normal.jpg", false));
                mat.SetTexture("_RoughMap", FinishBuilder.ImportTexture(set.dir + "/" + set.name + "_rough.jpg", false));
                mat.SetTextureScale("_BaseMap", new Vector2(1f / set.tile, 1f / set.tile));
                mat.SetFloat("_UseMaps", 1f);
                float gain = Mathf.Min(1f / 0.7f, 1f / Mathf.Max(set.tint.r, set.tint.g, set.tint.b));   // the sets are grey albedo normalised to 0.70; a bright tint keeps its hue instead of clipping
                color = new Color(set.tint.r * gain, set.tint.g * gain, set.tint.b * gain, 1f);
            }
            else if (!textured)   // a flat-colour model (Kenney): a neutral grain map, so no visible surface is a bare colour
            {
                mat.SetTexture("_BaseMap", GrainTexture()); mat.SetFloat("_UseMaps", 0f);
                color = new Color(Mathf.Min(1f, color.r / GrainMean), Mathf.Min(1f, color.g / GrainMean), Mathf.Min(1f, color.b / GrainMean), 1f);
            }
            mat.SetFloat("_GlowByAlbedo", fabricShade ? 1f : 0f);
            mat.SetColor("_EmissionColor", !glows ? Color.black : fabricShade ? ShadeGlow : BulbGlow);
            mat.SetFloat("_Recolor", recolor ? 1f : 0f);
            mat.SetFloat("_MeanLum", fabricShade ? 0.7f : Mathf.Max(0.02f, m.meanLum));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_NormalStrength", fabricShade ? 0.5f : 1f);
            mat.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        struct FlatSet { public string dir, name; public Color tint; public float tile; }

        // The tiling PBR set for one material of a flat Kenney model, by model and material name; false keeps the flat colour with a grain.
        static bool FlatPbr(string slug, string material, string style, out FlatSet set)
        {
            set = default;
            const string wood = "Assets/_Project/Art/Finishes/Tex", cloth = "Assets/_Project/Art/Fabrics/Tex", surf = "Assets/_Project/Art/Surfaces/Tex";
            bool pale = style == "warm-wood" || style == "scandi";
            if (slug == "bedSingle")
            {
                switch (material)
                {
                    case "wood": set = new FlatSet { dir = wood, name = pale ? "floor-birch-planks" : "floor-dark-planks", tint = StyleColor(style, "main"), tile = 0.6f }; return true;
                    case "carpet": set = new FlatSet { dir = cloth, name = "rug_wool", tint = StyleColor(style, "fabric"), tile = 0.4f }; return true;
                    case "carpetWhite": set = new FlatSet { dir = cloth, name = "curtain_linen", tint = new Color(0.93f, 0.91f, 0.86f), tile = 0.3f }; return true;
                }
            }
            if (slug == "bedSingle" && material == "metal")
                { set = new FlatSet { dir = surf, name = "lamp_metal", tint = LampMetalTint(style), tile = 0.3f }; return true; }
            if (slug == "lampRoundFloor" && material == "metal")
                { set = new FlatSet { dir = surf, name = "lamp_metal", tint = LampMetalTint(style), tile = 0.3f }; return true; }
            return false;
        }

        // The stand's metal colour per style: brass for the café, graphite for night-city, warm dark metal and pale steel for the rest.
        static Color LampMetalTint(string style) => style == "cafe" ? StyleColor(style, "accent") : style == "night-city" ? new Color(0.24f, 0.26f, 0.30f)
            : style == "scandi" ? StyleColor(style, "alt") : StyleColor(style, "dark") * 1.6f;

        // A small tileable grain (mean 0.85) for materials that have no photo texture: the colour is divided by the mean so it stays the palette colour.
        const float GrainMean = 0.85f;
        const string GrainPath = "Assets/_Project/Art/Materials/Grain.png";

        static Texture2D GrainTexture()
        {
            EnsureFolder(MaterialDir);
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", GrainPath))))
            {
                const int n = 128; var pix = new Color32[n * n]; var rng = new System.Random(7);
                var field = new float[n, n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) field[x, y] = (float)rng.NextDouble();
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float v = 0; for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) v += field[(x + dx + n) % n, (y + dy + n) % n];   // wraps: tileable
                        v = GrainMean + (v / 9f - 0.5f) * 0.18f; byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                        pix[y * n + x] = new Color32(g, g, g, 255);
                    }
                var t = new Texture2D(n, n, TextureFormat.RGB24, false); t.SetPixels32(pix); t.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", GrainPath)), t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(GrainPath, ImportAssetOptions.ForceUpdate);
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GrainPath);
            var imp = AssetImporter.GetAtPath(GrainPath) as TextureImporter;
            if (imp != null && (!imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Repeat)) { imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Repeat; imp.SaveAndReimport(); }
            return tex;
        }

        // The batch check: every visible renderer must have a texture in _BaseMap on every material (no bare colour on a visible surface).
        static string CheckMaterials(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m == null || !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == null) return $"{r.name} has a material without a _BaseMap texture ({(m == null ? "null" : m.name)})";
            return null;
        }

        // Catalog pieces use OwnSpace/Furniture (URP PBR lighting plus ToneMatch, see Art/Shaders), not the stock URP Lit.
        static Shader _furniture;
        static Shader FurnitureShader()
        {
            if (_furniture == null)
            {
                _furniture = Shader.Find("OwnSpace/Furniture");
                if (_furniture == null || ShaderUtil.ShaderHasError(_furniture)) throw new InvalidOperationException("OwnSpace/Furniture is missing or has a compile error.");
            }
            return _furniture;
        }

        // ---- PBR fabric and wood on catalog pieces (OwnSpace/Finish in texture mode: world-projected CC0 maps, tinted grey albedo) ----

        static Color StyleColor(string style, string slot)
        {
            ColorUtility.TryParseHtmlString("#" + Palettes[style][Array.IndexOf(Slots, slot)], out var c);
            return c;
        }

        static Material PbrMaterial(string name, string texDir, string texName, Color tint, float scaleM, float normalStrength = 1f, float maxSmooth = 0.6f)
        {
            EnsureFolder(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var shader = Shader.Find("OwnSpace/Finish");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("OwnSpace/Finish is missing or has an error.");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetFloat("_UseTex", 1f);
            mat.SetTexture("_BaseMap", FinishBuilder.ImportTexture($"{texDir}/{texName}_albedo.jpg", true));
            mat.SetTexture("_NormalMap", FinishBuilder.ImportTexture($"{texDir}/{texName}_normal.jpg", false));
            mat.SetTexture("_RoughMap", FinishBuilder.ImportTexture($"{texDir}/{texName}_rough.jpg", false));
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Gain", 1f / 0.7f);
            mat.SetFloat("_TexScale", scaleM);
            mat.SetFloat("_NormalStrength", normalStrength);
            mat.SetFloat("_MaxSmooth", maxSmooth);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A unit-size curtain panel with soft vertical folds (x -0.5..0.5, y -0.5..0.5, z within +-0.5), both sides, scaled by OpeningFrame.Fit.
        static Mesh CurtainMesh()
        {
            const string path = "Assets/_Project/Art/Furniture/_shared/CurtainPanel.asset";
            EnsureFolder("Assets/_Project/Art/Furniture/_shared");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "CurtainPanel" }; AssetDatabase.CreateAsset(mesh, path); }
            const int cols = 48, rows = 2, folds = 2;
            var v = new Vector3[(cols + 1) * (rows + 1)]; var uv = new Vector2[v.Length];
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++)
                {
                    float u = c / (float)cols, t = r / (float)rows;
                    float wave = Mathf.Sin(u * folds * 2f * Mathf.PI) * 0.5f * Mathf.Lerp(0.6f, 1f, 1f - t); // two wide folds (full depth: the catalog z is 0.1), a little less deep toward the hem
                    v[r * (cols + 1) + c] = new Vector3(u - 0.5f, t - 0.5f, wave); uv[r * (cols + 1) + c] = new Vector2(u, t);
                }
            // The back side has its own vertices: sharing them made RecalculateNormals average the two faces to near zero, which lit the folds as
            // harsh white stripes. Each side now gets its own correct normal (the back's winding is reversed, so its normals face the other way).
            int n0 = v.Length;
            var vb = new Vector3[n0 * 2]; var uvb = new Vector2[n0 * 2];
            v.CopyTo(vb, 0); v.CopyTo(vb, n0); uv.CopyTo(uvb, 0); uv.CopyTo(uvb, n0);
            var tris = new System.Collections.Generic.List<int>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int a = r * (cols + 1) + c, b = a + 1, d = a + cols + 1, e = d + 1;
                    tris.AddRange(new[] { a, d, b, b, d, e });     // front
                    tris.AddRange(new[] { a + n0, b + n0, d + n0, b + n0, e + n0, d + n0 });     // back (the curtain is seen from both sides)
                }
            mesh.Clear(); mesh.vertices = vb; mesh.uv = uvb; mesh.triangles = tris.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // A door casing profile: a wide low step and a narrower raised step (like a real moulding), unit size (x, y, z within +-0.5), scaled by
        // OpeningFrame.Fit. Vertical bars run along y with the profile across x and z; the top bar runs along x with the profile across y and z.
        static Mesh MouldingMesh(bool horizontal)
        {
            string path = $"Assets/_Project/Art/Furniture/_shared/Moulding{(horizontal ? "H" : "V")}.asset";
            EnsureFolder("Assets/_Project/Art/Furniture/_shared");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mesh, path); }
            var v = new System.Collections.Generic.List<Vector3>(); var n = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var t = new System.Collections.Generic.List<int>();
            void Box(float a0, float a1, float z0, float z1)
            {
                // a = the profile axis across the bar (x for vertical bars, y for the top bar); the bar runs the full length of the other axis.
                Vector3 P(float a, float l, float z) => horizontal ? new Vector3(l, a, z) : new Vector3(a, l, z);
                Vector3[][] faces =
                {
                    new[] { P(a0, -0.5f, z1), P(a1, -0.5f, z1), P(a1, 0.5f, z1), P(a0, 0.5f, z1) },   // front (+z)
                    new[] { P(a1, -0.5f, z0), P(a0, -0.5f, z0), P(a0, 0.5f, z0), P(a1, 0.5f, z0) },   // back
                    new[] { P(a0, -0.5f, z0), P(a0, -0.5f, z1), P(a0, 0.5f, z1), P(a0, 0.5f, z0) },   // a0 side
                    new[] { P(a1, -0.5f, z1), P(a1, -0.5f, z0), P(a1, 0.5f, z0), P(a1, 0.5f, z1) },   // a1 side
                    new[] { P(a0, -0.5f, z0), P(a1, -0.5f, z0), P(a1, -0.5f, z1), P(a0, -0.5f, z1) }, // end -l
                    new[] { P(a0, 0.5f, z1), P(a1, 0.5f, z1), P(a1, 0.5f, z0), P(a0, 0.5f, z0) },     // end +l
                };
                foreach (var f in faces)
                {
                    int b = v.Count; var nrm = Vector3.Cross(f[1] - f[0], f[3] - f[0]).normalized;
                    for (int i = 0; i < 4; i++) { v.Add(f[i]); n.Add(nrm); }
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                    t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            }
            Box(-0.5f, 0.5f, -0.5f, 0.05f);     // the wide, low casing
            Box(-0.30f, 0.30f, 0.05f, 0.5f);    // the narrower raised step
            mesh.Clear(); mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // A pillow's outline for LatheMesh: (radius as a fraction of the half width, height in metres is passed through scaled by the part height).
        static float[] PillowProfile(float h) => new[] { 0f, 0f, 0.78f, 0f, 0.78f, 0f, 0.95f, 0.18f * h, 1f, 0.5f * h, 0.95f, 0.82f * h, 0.78f, h, 0.55f, h, 0.55f, h, 0f, 0.93f * h };

        static Mesh LatheMesh(string name, float rx, float rz, float h, Func<float, float[]> profile) => LatheMesh(name, rx, rz, h, profile(h));

        // A smooth solid of revolution on an elliptical base (rx, rz in metres). `profile` is pairs of (radius fraction 0..1, y in metres) from the bottom up; a repeated pair is a
        // hard edge. Normals come from the profile's tangents, so round shapes are smooth; UVs are in metres (u around the widest radius, v along the profile) for a tiling set.
        static Mesh LatheMesh(string name, float rx, float rz, float h, float[] profile)
        {
            string path = $"Assets/_Project/Art/Furniture/_shared/Lathe_{name}.asset";
            EnsureFolder("Assets/_Project/Art/Furniture/_shared");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "Lathe_" + name }; AssetDatabase.CreateAsset(mesh, path); }
            const int seg = 48;
            int n = profile.Length / 2;
            float[] arc = new float[n]; float rAvg = (rx + rz) / 2f;
            for (int j = 1; j < n; j++)
            {
                float dr = (profile[j * 2] - profile[(j - 1) * 2]) * rAvg, dy = profile[j * 2 + 1] - profile[(j - 1) * 2 + 1];
                arc[j] = arc[j - 1] + Mathf.Sqrt(dr * dr + dy * dy);
            }
            float yMid = (profile[1] + profile[(n - 1) * 2 + 1]) / 2f;
            var v = new List<Vector3>(); var nor = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j < n; j++)
            {
                float r = profile[j * 2], y = profile[j * 2 + 1];
                // The profile tangent in metres at this point: each side's segment is normalised, a hard edge (repeated pair) uses only its own side's segment.
                bool hasPrev = j > 0 && (profile[(j - 1) * 2] != r || profile[(j - 1) * 2 + 1] != y);
                bool hasNext = j < n - 1 && (profile[(j + 1) * 2] != r || profile[(j + 1) * 2 + 1] != y);
                Vector2 tan = Vector2.zero;
                if (hasPrev) tan += new Vector2((r - profile[(j - 1) * 2]) * rAvg, y - profile[(j - 1) * 2 + 1]).normalized;
                if (hasNext) tan += new Vector2((profile[(j + 1) * 2] - r) * rAvg, profile[(j + 1) * 2 + 1] - y).normalized;
                tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : Vector2.up;
                for (int k = 0; k <= seg; k++)
                {
                    float th = k * 2f * Mathf.PI / seg, c = Mathf.Cos(th), s = Mathf.Sin(th);
                    v.Add(new Vector3(rx * r * c, y, rz * r * s));
                    // outward normal = the tangent turned a quarter turn, stretched for the ellipse
                    float horizontal = tan.y, vertical = -tan.x;
                    var nn = new Vector3(horizontal * c / rx, vertical, horizontal * s / rz);
                    nor.Add(nn.normalized);
                    uv.Add(new Vector2(k * 2f * Mathf.PI * rAvg / seg, arc[j]));
                }
            }
            for (int j = 0; j < n - 1; j++)
                for (int k = 0; k < seg; k++)
                {
                    int a = j * (seg + 1) + k, b = a + 1, c2 = (j + 1) * (seg + 1) + k, d2 = c2 + 1;
                    if (profile[j * 2] == profile[(j + 1) * 2] && profile[j * 2 + 1] == profile[(j + 1) * 2 + 1]) continue;   // a repeated pair is only a hard edge
                    tris.AddRange(new[] { a, c2, b, b, c2, d2 });
                }
            // wind every triangle to face the same way as its vertex normals
            for (int i = 0; i < tris.Count; i += 3)
            {
                var fn = Vector3.Cross(v[tris[i + 1]] - v[tris[i]], v[tris[i + 2]] - v[tris[i]]);
                var vn = nor[tris[i]] + nor[tris[i + 1]] + nor[tris[i + 2]];
                if (Vector3.Dot(fn, vn) < 0f) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
            }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetNormals(nor); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // One leaf, 1 m long with the stalk at the origin and the tip at +y: a strip following the Leaf001 left leaf's outline (rows of v, u-min, u-max measured from the photo),
        // folded along the midrib and arched, double-sided. UVs are the photo's own.
        static readonly float[] LeafRows = { 0.861f,0.241f,0.253f, 0.811f,0.240f,0.253f, 0.761f,0.134f,0.348f, 0.710f,0.114f,0.378f, 0.659f,0.090f,0.411f, 0.608f,0.068f,0.437f,
            0.559f,0.057f,0.450f, 0.508f,0.057f,0.455f, 0.457f,0.058f,0.444f, 0.406f,0.085f,0.425f, 0.356f,0.090f,0.384f, 0.306f,0.124f,0.342f, 0.255f,0.153f,0.311f,
            0.204f,0.205f,0.269f, 0.154f,0.237f,0.266f, 0.104f,0.261f,0.264f };

        static Mesh LeafMesh()
        {
            const string path = "Assets/_Project/Art/Furniture/_shared/Leaf.asset";
            EnsureFolder("Assets/_Project/Art/Furniture/_shared");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "Leaf" }; AssetDatabase.CreateAsset(mesh, path); }
            const float span = 0.757f, uc = 0.255f;   // the photo's leaf length in UV, and its axis
            int rows = LeafRows.Length / 3;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                float vi = LeafRows[r * 3], u0 = LeafRows[r * 3 + 1], u1 = LeafRows[r * 3 + 2], um = (u0 + u1) / 2f;
                float y = (0.861f - vi) / span;
                float arch = -0.25f * y * y;
                foreach (var u in new[] { u0, um, u1 })
                {
                    float x = (u - uc) / span;
                    v.Add(new Vector3(x, y, arch - Mathf.Abs(x - (um - uc) / span) * 0.35f));
                    uv.Add(new Vector2(u, 1f - vi));
                }
                if (r == 0) continue;
                for (int c = 0; c < 2; c++)
                {
                    int a = (r - 1) * 3 + c, b = a + 1, c2 = r * 3 + c, d2 = c2 + 1;
                    t.AddRange(new[] { a, c2, b, b, c2, d2 });
                }
            }
            int n = v.Count;
            var front = new Mesh { vertices = v.ToArray(), triangles = t.ToArray() }; front.RecalculateNormals();
            var normals = new List<Vector3>(front.normals);
            UnityEngine.Object.DestroyImmediate(front);
            for (int i = 0; i < n; i++) { v.Add(v[i]); uv.Add(uv[i]); normals.Add(-normals[i]); }
            int tc = t.Count;
            for (int i = 0; i < tc; i += 3) { t.Add(t[i] + n); t.Add(t[i + 2] + n); t.Add(t[i + 1] + n); }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // Scales and shifts the foliage group so the whole plant (pot included) is exactly w x h x d, centred, standing on y = 0.
        static void FitFoliage(GameObject root, Transform fol, float w, float d, float h)
        {
            Bounds Measure()
            {
                bool any = false; var b = new Bounds();
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    var mb = mesh.bounds;   // CheckBounds uses Renderer.bounds, the box of the transformed mesh box, so measure the same way
                    for (int k = 0; k < 8; k++)
                    {
                        var p = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) * 2 - 1, ((k >> 1) & 1) * 2 - 1, ((k >> 2) & 1) * 2 - 1));
                        var q = root.transform.InverseTransformPoint(mf.transform.TransformPoint(p));
                        if (!any) { b = new Bounds(q, Vector3.zero); any = true; } else b.Encapsulate(q);
                    }
                }
                return b;
            }
            var bb = Measure();
            float sx = w / bb.size.x, sz = d / bb.size.z, sy = h / bb.max.y;
            fol.localScale = new Vector3(sx, sy, sz);
            bb = Measure();
            fol.localPosition = new Vector3(-bb.center.x, 0f, -bb.center.z);
        }

        // A tiling PBR set on the furniture shader (UV-based, so the texture moves with the piece), tinted; the sets are grey albedo normalised to 0.70.
        static Material FurniturePbr(string name, string texDir, string texName, Color tint, float tileM)
        {
            EnsureFolder(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(FurnitureShader()); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = FurnitureShader();
            mat.SetTexture("_BaseMap", FinishBuilder.ImportTexture($"{texDir}/{texName}_albedo.jpg", true));
            mat.SetTexture("_NormalMap", FinishBuilder.ImportTexture($"{texDir}/{texName}_normal.jpg", false));
            mat.SetTexture("_RoughMap", FinishBuilder.ImportTexture($"{texDir}/{texName}_rough.jpg", false));
            mat.SetTextureScale("_BaseMap", new Vector2(1f / tileM, 1f / tileM));
            mat.SetFloat("_UseMaps", 1f);
            mat.SetFloat("_Recolor", 0f);
            mat.SetColor("_EmissionColor", Color.black);
            float gain = Mathf.Min(1f / 0.7f, 1f / Mathf.Max(tint.r, tint.g, tint.b));
            mat.SetColor("_BaseColor", new Color(tint.r * gain, tint.g * gain, tint.b * gain, 1f));
            mat.SetFloat("_NormalStrength", 1f);
            mat.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // One mesh of axis-aligned boxes with box-projected UVs in metres (sides use (horizontal, y), tops use (x, z)), so a tiling set is at true scale.
        static Mesh BoxMesh(string name, List<Bounds> boxes)
        {
            string path = $"Assets/_Project/Art/Furniture/_shared/Box_{name}.asset";
            EnsureFolder("Assets/_Project/Art/Furniture/_shared");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "Box_" + name }; AssetDatabase.CreateAsset(mesh, path); }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            foreach (var b in boxes)
            {
                Vector3 lo = b.min, hi = b.max;
                void Face(Vector3 normal, Vector3 a, Vector3 bb, Vector3 c, Vector3 dd)
                {
                    int i = v.Count;
                    foreach (var p in new[] { a, bb, c, dd })
                    {
                        v.Add(p); n.Add(normal);
                        uv.Add(Mathf.Abs(normal.y) > 0.5f ? new Vector2(p.x, p.z) : Mathf.Abs(normal.x) > 0.5f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                    }
                    t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });   // Unity front faces are clockwise as seen from outside
                }
                Face(Vector3.forward, new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, lo.y, hi.z));
                Face(Vector3.back, new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, lo.y, lo.z));
                Face(Vector3.right, new Vector3(hi.x, lo.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, lo.y, lo.z));
                Face(Vector3.left, new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, lo.y, hi.z));
                Face(Vector3.up, new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, hi.y, hi.z));
                Face(Vector3.down, new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, lo.y, hi.z), new Vector3(hi.x, lo.y, lo.z));
            }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static GameObject MeshPart(GameObject root, string name, Mesh mesh, Material mat)
        {
            int same = 0;
            foreach (Transform c in root.transform) if (c.name == name || c.name.StartsWith(name + "_")) same++;
            var go = new GameObject(same == 0 ? name : name + "_" + same, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        static IEnumerable<Vector2> Corners(float w, float d, float inset)
        {
            float x = w / 2f - inset, z = d / 2f - inset;
            yield return new Vector2(-x, -z);
            yield return new Vector2(x, -z);
            yield return new Vector2(-x, z);
            yield return new Vector2(x, z);
        }

        /// <summary>One primitive. Size is the final extent in meters; cylinders are 2 m tall at scale 1.</summary>
        static GameObject Part(GameObject root, string name, PrimitiveType type, Material mat,
            float cx, float cy, float cz, float sx, float sy, float sz, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            // Unique sibling names keep the prefab file ids, and so the files, identical on every run.
            int same = 0;
            foreach (Transform c in root.transform) if (c.name == name || c.name.StartsWith(name + "_")) same++;
            go.name = same == 0 ? name : name + "_" + same;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(cx, cy, cz);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(sx, type == PrimitiveType.Cylinder ? sy / 2f : sy, sz);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static string CheckBounds(GameObject root, Entry item)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return "no parts";
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (Mathf.Abs(b.size.x - item.size.x) > BoundsTolerance || Mathf.Abs(b.size.y - item.size.y) > BoundsTolerance ||
                Mathf.Abs(b.size.z - item.size.z) > BoundsTolerance)
                return $"bounds {b.size} differ from catalog size ({item.size.x}, {item.size.y}, {item.size.z})";
            if (item.kind == "curtain") return null; // hangs 0.10 m below the opening's bottom; pivot is the opening, not the floor
            if (Mathf.Abs(b.min.y) > BoundsTolerance || Mathf.Abs(b.center.x) > BoundsTolerance || Mathf.Abs(b.center.z) > BoundsTolerance)
                return $"pivot is not bottom center (bounds {b.min}..{b.max})";
            return null;
        }

        // ---- shared materials: one URP Lit per style and slot, no textures ----

        static Material StyleMaterial(string style, string slot)
        {
            EnsureFolder(MaterialDir);
            string path = $"{MaterialDir}/Style_{style}_{slot}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(FurnitureShader());
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = FurnitureShader();
            ColorUtility.TryParseHtmlString("#" + Palettes[style][Array.IndexOf(Slots, slot)], out var color);
            mat.SetTexture("_BaseMap", GrainTexture());
            mat.SetFloat("_UseMaps", 0f);
            color = new Color(Mathf.Min(1f, color.r / GrainMean), Mathf.Min(1f, color.g / GrainMean), Mathf.Min(1f, color.b / GrainMean), 1f);
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color); // legacy twin; Unity syncs it on reimport, so set it to keep reruns identical
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.15f); // matte, see style-board.md
            if (slot == "glow")
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 0.5f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
