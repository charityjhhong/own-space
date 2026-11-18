using OwnSpace.Interaction;
using OwnSpace.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Builds <c>XrRoot.prefab</c> (the xr lane's one root prefab) and places it once in Main.unity.
    /// Safe to run any number of times: existing components are kept, missing ones are added, and the
    /// scene gets the prefab only if it has no <c>XrRoot</c> yet.
    /// Run: Unity -batchmode -quit -projectPath app -executeMethod OwnSpace.MR.Editor.XrRootBuilder.Build
    /// </summary>
    public static class XrRootBuilder
    {
        public const string PrefabPath = "Assets/_Project/MR/XrRoot.prefab";
        const string MaterialPath = "Assets/_Project/MR/Materials/M_XrOutline.mat";
        const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        const string RootName = "XrRoot";
        const string LogTag = "[OwnSpace.MR.Editor]";

        /// <summary>Project settings the xr lane owns. Idempotent.</summary>
        [MenuItem("OwnSpace/xr/Apply Project Settings")]
        public static void ApplyProjectSettings()
        {
            // build's DiagnosticsRoot overlay reads FrameTimingManager, which needs this on.
            if (!PlayerSettings.enableFrameTimingStats)
            {
                PlayerSettings.enableFrameTimingStats = true;
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("OwnSpace/xr/Build XrRoot")]
        public static void Build()
        {
            BuildPrefab();
            PlaceInMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogTag} XrRoot built and placed.");
        }

        static void BuildPrefab()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(RootName);
            try
            {
                Configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        /// <summary>Adds what XrRoot needs. Each step must be a no-op when already done.</summary>
        static void Configure(GameObject root)
        {
            var room = Ensure<MrukRoomInfo>(root);
            var hands = Ensure<HandRig>(root);
            // Mouse mode starts off: the simulator window's Point & click drives the hand rays.
            var handProps = new SerializedObject(hands);
            handProps.FindProperty("_editorMouse").boolValue = false;
            handProps.ApplyModifiedPropertiesWithoutUndo();
            var store = Ensure<DesignStore>(root);
            Wire(store, ("_room", room), ("_catalogJson", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/catalog.json")));

            var zone = Ensure<ZoneSetup>(EnsureChild(root, "Zone"));
            Wire(zone, ("_room", room), ("_store", store), ("_hands", hands), ("_material", OutlineMaterial()));

            var shell = Ensure<OutsideShell>(EnsureChild(root, "Outside"));
            Wire(shell, ("_room", room), ("_store", store), ("_zone", zone),
                ("_hole", ShaderMaterial("M_PassthroughHole", "OwnSpace/PassthroughHole")),
                ("_seam", ShaderMaterial("M_SeamFade", "OwnSpace/SeamFade")),
                ("_finishes", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/FinishSet.asset")),
                ("_prefabs", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/CatalogPrefabs.asset")));
            WireArray(shell, "_surfaces",
                SurfaceMaterial("M_ShellFloor", new Color(0.62f, 0.56f, 0.5f)),
                SurfaceMaterial("M_ShellWall", new Color(0.86f, 0.84f, 0.8f)),
                SurfaceMaterial("M_ShellCeiling", new Color(0.93f, 0.92f, 0.9f)));

            var edit = Ensure<EditMode>(EnsureChild(root, "Edit"));
            Wire(edit, ("_room", room), ("_store", store), ("_hands", hands), ("_zone", zone),
                ("_material", OutlineMaterial()),
                ("_catalogJson", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/catalog.json")),
                ("_prefabs", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/CatalogPrefabs.asset")));

            Object finishes = AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/FinishSet.asset");
            Object moods = AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/MoodPresets.asset");
            var mood = Ensure<MoodApplier>(EnsureChild(root, "Mood"));
            Wire(mood, ("_store", store), ("_room", room), ("_moods", moods));
            Wire(Ensure<LightGlows>(EnsureChild(root, "Glows")), ("_room", room), ("_store", store), ("_zone", zone), ("_mood", mood),
                ("_glowPrefab", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Art/Lighting/LightGlow.prefab")),
                ("_prefabs", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/CatalogPrefabs.asset")));
            // The Style tab replaced the style orb and its rows.
            Transform oldStyle = root.transform.Find("Style");
            if (oldStyle != null) Object.DestroyImmediate(oldStyle.gameObject);
            GameObject askRoot = EnsureChild(root, "Ask");
            var ask = Ensure<AskMenu>(askRoot);
            var freeText = Ensure<FreeTextRequest>(askRoot);
            Wire(freeText, ("_ask", ask), ("_hands", hands));
            Wire(ask, ("_store", store), ("_room", room), ("_hands", hands), ("_freeText", freeText),
                ("_zone", zone), ("_material", OutlineMaterial()), ("_finishes", finishes), ("_moods", moods));

            var hide = Ensure<HideBoxes>(EnsureChild(root, "Hide"));
            Wire(Ensure<HideTool>(EnsureChild(root, "HideTool")), ("_room", room), ("_store", store), ("_hands", hands),
                ("_zone", zone), ("_boxes", hide), ("_material", OutlineMaterial()));
            Wire(hide, ("_store", store), ("_hands", hands),
                ("_defaultFloor", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/MR/Materials/M_ShellFloor.mat")),
                ("_outline", OutlineMaterial()), ("_finishes", finishes),
                ("_prefabs", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/CatalogPrefabs.asset")));

            var place = Ensure<OutsidePlacer>(EnsureChild(root, "Place"));
            Wire(place, ("_room", room), ("_store", store), ("_hands", hands), ("_zone", zone),
                ("_material", OutlineMaterial()),
                ("_prefabs", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/CatalogPrefabs.asset")));

            Wire(Ensure<GazeReticle>(EnsureChild(root, "Reticle")), ("_room", room), ("_hands", hands), ("_material", OutlineMaterial()));
            RealOcclusionSetup.Configure(root, RealOcclusionSetup.LoadRenderers()); // real things in front of the virtual room

            // The tool panel (UI/ToolPanel.prefab) and its host: the four tabs drive the tools above, so their own
            // orbs, chips and tile rows are switched off (panel mode); their world input stays.
            ToolPanel panel = Panel(root, hands);
            var host = Ensure<PanelHost>(EnsureChild(root, "PanelHost"));
            Wire(host, ("_panel", panel), ("_store", store), ("_room", room), ("_zone", zone), ("_edit", edit), ("_placer", place),
                ("_ask", ask), ("_freeText", freeText), ("_hide", root.transform.Find("HideTool").GetComponent<HideTool>()),
                ("_finishes", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/finishes.json")),
                ("_moods", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/mood-presets.json")),
                ("_thumbnails", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Content/thumbnails.json")),
                ("_font", AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Art/Fonts/Inter-Regular SDF.asset")));
            foreach (Object tool in new Object[] { ask, edit, place, root.transform.Find("HideTool").GetComponent<HideTool>() })
            {
                var so = new SerializedObject(tool);
                so.FindProperty("_panelMode").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Wire(Ensure<OnboardingGuide>(EnsureChild(root, "Onboarding")), ("_room", room), ("_store", store), ("_hands", hands),
                ("_material", OutlineMaterial()));
        }

        // One instance of the panel prefab under XrRoot (nested prefab), with the scene's HandRig.
        static ToolPanel Panel(GameObject root, HandRig hands)
        {
            Transform existing = root.transform.Find("Panel");
            GameObject go = existing != null ? existing.gameObject : null;
            if (go == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
                if (prefab == null) throw new System.IO.FileNotFoundException("Run OwnSpace.UI.Editor.ToolPanelBuilder.Build first.");
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.name = "Panel";
            }
            var panel = go.GetComponent<ToolPanel>();
            Wire(panel, ("_hands", hands));
            return panel;
        }

        /// <summary>Sets object references by serialized field name.</summary>
        internal static void Wire(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null) throw new System.InvalidOperationException($"{target.GetType().Name} has no field {field}");
                prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void WireArray(Object target, string field, params Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A material asset in MR/Materials for one of our shaders; created once.</summary>
        static Material ShaderMaterial(string name, string shader)
        {
            string path = $"Assets/_Project/MR/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find(shader)) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>An opaque, double-sided URP Unlit surface for the outside shell until a finish is chosen.</summary>
        static Material SurfaceMaterial(string name, Color color)
        {
            string path = $"Assets/_Project/MR/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Cull", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>One opaque URP Unlit material for all placeholder visuals; colors come from property blocks.</summary>
        internal static Material OutlineMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/MR/Materials"))
                AssetDatabase.CreateFolder("Assets/_Project/MR", "Materials");
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static void PlaceInMainScene()
        {
            // Never drop someone's unsaved work: use Main as-is if it is open, ask about other dirty scenes.
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (scene.isDirty)
                {
                    Debug.LogWarning($"{LogTag} Main.unity has unsaved changes; save it, then run Build again.");
                    return;
                }
            }
            else
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning($"{LogTag} Placement skipped: unsaved scenes were kept open.");
                    return;
                }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            bool placed = false;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName) placed = true;
                HideDebugEffectMesh(go, scene);
            }
            // build's root prefab (request on #1); placed once, then only build edits it.
            PlaceOnce(scene, "DiagnosticsRoot", "Assets/_Project/Diagnostics/DiagnosticsRoot.prefab");
            if (placed)
            {
                if (scene.isDirty) EditorSceneManager.SaveScene(scene);
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = RootName;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Places another lane's root prefab once (by root name); marks the scene dirty when it does.</summary>
        static void PlaceOnce(Scene scene, string rootName, string prefabPath)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == rootName) return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"{LogTag} {prefabPath} not found; {rootName} not placed.");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = rootName;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // The P0 EffectMesh building block paints a debug grid on every room surface, which would
        // cover passthrough in my zone. Keep the component (MRUK data, colliders) but hide its mesh.
        static void HideDebugEffectMesh(GameObject go, Scene scene)
        {
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "EffectMesh") continue;
                var so = new SerializedObject(mb);
                SerializedProperty hide = so.FindProperty("hideMesh");
                if (hide == null || hide.boolValue) continue;
                hide.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        internal static T Ensure<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        internal static GameObject EnsureChild(GameObject parent, string name)
        {
            Transform found = parent.transform.Find(name);
            if (found != null) return found.gameObject;
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }
    }
}
