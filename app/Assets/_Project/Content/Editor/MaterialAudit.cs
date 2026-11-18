using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Audits every catalog prefab: each renderer part and its material (shader, base / normal / roughness maps, size),
    /// with a verdict, and writes docs/design/material-audit.md. Also flags one material shared by every part of a
    /// multi-part model (the #249 importer failure) and parts whose material name does not fit the part
    /// (glass, fabric or metal parts with a wood material). Read only: it changes no asset; a rerun gives the same file.
    /// Batch: Unity -batchmode -projectPath app -executeMethod OwnSpace.Content.Editor.MaterialAudit.RunBatch -quit
    /// </summary>
    public static class MaterialAudit
    {
        const string CatalogPath = "Assets/_Project/Content/catalog.json";
        const string OutPath = "../../docs/design/material-audit.md";

        [Serializable] class CatalogFile { public Entry[] items = Array.Empty<Entry>(); }
        [Serializable] class Entry { public string id, kind, name; public AssetRef asset; }
        [Serializable] class AssetRef { public string path; }

        [MenuItem("OwnSpace/Content/Audit Materials")]
        public static void Run() => Write();

        public static void RunBatch()
        {
            int code;
            try { code = Write() ? 0 : 1; }
            catch (Exception e) { Debug.LogError($"[OwnSpace.Content] Audit failed: {e}"); code = 2; }
            EditorApplication.Exit(code);
        }

        // Parts where a flat colour is the right answer.
        static readonly string[] FlatOk = { "glass", "bulb", "screen", "light", "emiss", "paper", "soil", "lens", "artwork", "glow", "inside", "slots" };
        static readonly string[] Wood = { "wood", "oak", "birch", "walnut", "plank", "timber" };
        static readonly string[] SoftParts = { "cushion", "pillow", "fabric", "linen", "cloth", "shade", "curtain", "mattress", "upholster", "seat", "sofa", "throw", "blanket" };
        static readonly string[] SoftMats = { "linen", "fabric", "cloth", "leather", "velvet", "wool", "cushion" };
        static readonly string[] MetalParts = { "metal", "steel", "leg", "handle", "knob", "hinge", "lamp_base", "stand" };
        static readonly string[] MetalMats = { "metal", "steel", "chrome", "brass", "iron", "alu" };

        static bool Has(string s, string[] keys) { s = s.ToLowerInvariant(); foreach (var k in keys) if (s.Contains(k)) return true; return false; }

        struct Row { public string Id, Part, Mat, Shader, Maps, Size, Verdict; }

        static bool Write()
        {
            var items = new List<Entry>(JsonUtility.FromJson<CatalogFile>(File.ReadAllText(CatalogPath)).items);
            items.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            var rows = new List<Row>();
            foreach (var item in items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.asset.path);
                if (prefab == null) { rows.Add(new Row { Id = item.id, Part = "(prefab)", Mat = "-", Shader = "-", Maps = "-", Size = "-", Verdict = "FAIL: prefab missing" }); continue; }
                var parts = new List<(string path, Renderer r)>();
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    parts.Add((PathOf(prefab.transform, r.transform), r));
                }
                var distinct = new HashSet<Material>();
                foreach (var p in parts) foreach (var mt in p.r.sharedMaterials) if (mt != null) distinct.Add(mt);
                int modelParts = 0; foreach (var p in parts) modelParts += p.path.StartsWith("Model/") ? Math.Max(1, p.r.sharedMaterials.Length) : 0;
                bool oneForAll = modelParts > 2 && distinct.Count == 1;   // only an imported model can lose its materials
                foreach (var p in parts)
                {
                    var mats = p.r.sharedMaterials;
                    if (mats.Length == 0) rows.Add(new Row { Id = item.id, Part = p.path, Mat = "(none)", Shader = "-", Maps = "-", Size = "-", Verdict = "FAIL: no material" });
                    for (int s = 0; s < mats.Length; s++)
                    {
                        var row = Inspect(item.id, mats.Length > 1 ? $"{p.path}#{s}" : p.path, mats[s]);
                        if (oneForAll && mats[s] != null) row.Verdict += row.Verdict.StartsWith("OK") ? "; WARN: one material on every part" : "";
                        if (oneForAll && mats[s] != null && !row.Verdict.Contains("one material")) row.Verdict += "; WARN: one material on every part";
                        rows.Add(row);
                    }
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine("# Material audit");
            sb.AppendLine();
            sb.AppendLine("Written by `Content/Editor/MaterialAudit.cs` (menu `OwnSpace/Content/Audit Materials`, batch `MaterialAudit.RunBatch`). One row per renderer part and material of every catalog prefab. **Maps**: `B` base colour, `N` normal, `R` roughness (the shader has no AO slot, so AO is baked into the base colour or not used). **Size** is the base map in pixels.");
            sb.AppendLine();
            int ok = 0, warn = 0, fail = 0;
            foreach (var r in rows) { if (r.Verdict.StartsWith("FAIL")) fail++; else if (r.Verdict.Contains("WARN") || r.Verdict.StartsWith("REVIEW")) warn++; else ok++; }
            sb.AppendLine($"Items: {items.Count}. Rows: {rows.Count}: **{ok} ok, {warn} warn or review, {fail} fail.**");
            sb.AppendLine();
            sb.AppendLine("| Item | Part | Material | Shader | Maps | Size | Verdict |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var r in rows) sb.AppendLine($"| {r.Id} | {r.Part} | {r.Mat} | {r.Shader} | {r.Maps} | {r.Size} | {r.Verdict} |");
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, OutPath));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string text = sb.ToString();
            if (!File.Exists(full) || File.ReadAllText(full) != text) File.WriteAllText(full, text);
            Debug.Log($"[OwnSpace.Content] Material audit: {items.Count} items, {rows.Count} rows, {ok} ok, {warn} warn, {fail} fail.");
            return true;
        }

        static int SubCount(List<(string path, Renderer r)> parts) { int n = 0; foreach (var p in parts) n += Math.Max(0, p.r.sharedMaterials.Length - 1); return n; }

        static string PathOf(Transform root, Transform t)
        {
            if (t == root) return "(root)";
            var parts = new List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Texture Tex(Material m, params string[] names) { foreach (var n in names) if (m.HasProperty(n)) { var t = m.GetTexture(n); if (t != null) return t; } return null; }

        static Row Inspect(string id, string part, Material m)
        {
            var row = new Row { Id = id, Part = part };
            if (m == null) { row.Mat = "(missing)"; row.Shader = "-"; row.Maps = "-"; row.Size = "-"; row.Verdict = "FAIL: material missing"; return row; }
            row.Mat = m.name; row.Shader = m.shader != null ? m.shader.name : "(null)";
            if (m.shader == null || row.Shader.StartsWith("Hidden/InternalError")) { row.Maps = "-"; row.Size = "-"; row.Verdict = "FAIL: pink / broken shader"; return row; }
            bool own = row.Shader.StartsWith("OwnSpace/");
            var b = Tex(m, "_BaseMap", "_MainTex");
            var n = own ? Tex(m, "_NormalMap") : Tex(m, "_BumpMap");
            var r = own ? Tex(m, "_RoughMap") : Tex(m, "_MetallicGlossMap");
            bool useMaps = !own || (m.HasProperty("_UseMaps") ? m.GetFloat("_UseMaps") > 0.5f : m.HasProperty("_UseTex") && m.GetFloat("_UseTex") > 0.5f);
            string maps = (b != null ? "B" : "") + (n != null && useMaps ? "N" : "") + (r != null && useMaps ? "R" : "");
            row.Maps = maps.Length == 0 ? "none" : maps;
            row.Size = b != null ? $"{b.width}x{b.height}" : "-";
            string mname = m.name.Replace(id, "");   // the item id and the style name (warm-wood ...) must not count as material names
            foreach (var st in new[] { "warm-wood", "night-city", "scandi", "cafe" }) mname = mname.Replace(st, "");
            bool flatOk = Has(part, FlatOk) || Has(mname, FlatOk);
            if (b == null)
                row.Verdict = flatOk ? "OK: flat colour (glass, light or screen)" : row.Shader == "Universal Render Pipeline/Lit" && m.name.StartsWith("Default") ? "FAIL: default material" : "FAIL: flat colour, no maps";
            else if (!maps.Contains("N")) row.Verdict = "REVIEW: base colour only, no normal map";
            else row.Verdict = "OK";
            if (b != null && !maps.Contains("N") && flatOk) row.Verdict = "OK: plain colour by design (soil, print, glow or dark recess)";
            // part and material that do not fit
            if (Has(part, SoftParts) && Has(mname, Wood) && !Has(mname, SoftMats)) row.Verdict += "; WARN: soft part with a wood material";
            if (Has(part, MetalParts) && Has(mname, Wood) && !Has(part, new[] { "wood", "oak", "birch" })) row.Verdict += "; WARN: metal part with a wood material";
            if (Has(part, new[] { "glass" }) && !Has(mname, new[] { "glass" })) row.Verdict += "; WARN: glass part with a non-glass material";
            return row;
        }
    }
}
