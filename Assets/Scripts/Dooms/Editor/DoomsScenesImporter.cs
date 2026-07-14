#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MLA_SIM.Dooms.Scenes;
using MLA_SIM.Dooms.Scenes.Nodes;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.Dooms.EditorTools
{
    // ── JSON data classes ────────────────────────────────────────────────────
    [Serializable]
    internal class AnimationSequenceData
    {
        public string id;
        public string start;
        public string loop;
        public string end;
        public float startCrossfade = 0.15f;
        public float endCrossfade = 0.15f;
    }

    [Serializable]
    internal class DoomsImportRoot
    {
        public List<string> areaTags;
        public List<AnimationSequenceData> animationSequences;
        public List<SceneDefData> scenes;
    }

    [Serializable]
    internal class SceneDefData
    {
        public string sceneId;
        public string displayName;
        public string description;
        public string narrativePhase;
        public List<string> requiredFactions;
        public int minAgentsPerFaction = 2;
        public float baseDurationSec   = 90f;
        public List<string> tags;
        public List<PhaseDefData> phases;
    }

    [Serializable]
    internal class PhaseDefData
    {
        public string phaseId;
        public string type;
        public float  minDurationSec    = 5f;
        public float  maxDurationSec    = 60f;
        public float  requiredPercentage;      // 0 means "not set in JSON" — handled below
        public List<RoleSlotData> roles;
    }

    [Serializable]
    internal class RoleSlotData
    {
        public string roleId;
        public string roleKind = "Point"; // Point | Area | Timeline
        public string factionId;
        public string pointTag;
        public string animationState = "Idle";
        public string areaTag;
        public string behavior = "Loiter";
        public string preferredBlendTree;
        public string pairWithFactionId;
        public string timelineAnchorId;
        public string timelineAssetId;
        public string timelineSlotId;
        public int    count          = 1;
        public float  arrivalTolerance = 2f;
        public bool   optional;
    }

    // ── Importer ─────────────────────────────────────────────────────────────
    public static class DoomsScenesImporter
    {
        private const string OUTPUT_FOLDER    = "Assets/Dooms/Scenes";
        // A1.1: single canonical registry location under Resources/Dooms. The
        // former Assets/Dooms/Registries duplicate-write paths were removed so the
        // importer no longer recreates a second copy that drifts out of sync.
        private const string REGISTRY_RES     = "Assets/Resources/Dooms/DoomsSceneRegistry.asset";
        private const string AREA_REGISTRY_RES  = "Assets/Resources/Dooms/DoomsAreaTagRegistry.asset";
        private const string ANIM_REG_RES = "Assets/Resources/Dooms/AnimationSequenceRegistry.asset";
        private const float  NODE_X_STEP      = 290f;

        [MenuItem("DOOMS/Import Scenes from JSON", priority = 100)]
        public static void ImportScenes()
        {
            // ── 1. Locate JSON ───────────────────────────────────────────────
            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "..",
                             "configs", "scenarios", "dooms", "dooms_scenes.json"));

            if (!File.Exists(jsonPath))
            {
                EditorUtility.DisplayDialog("DOOMS Import",
                    $"dooms_scenes.json not found at:\n{jsonPath}", "OK");
                return;
            }

            string rawJson = File.ReadAllText(jsonPath);
            var root = JsonUtility.FromJson<DoomsImportRoot>(rawJson);

            if (root == null)
            {
                EditorUtility.DisplayDialog("DOOMS Import",
                    "Parsed null root. Check JSON format.", "OK");
                return;
            }

            // ── Import Area Tags ──
            if (root.areaTags != null && root.areaTags.Count > 0)
            {
                EnsureFolder(Path.GetDirectoryName(AREA_REGISTRY_RES).Replace('\\', '/'));
                var areaReg = AssetDatabase.LoadAssetAtPath<AreaTagRegistrySO>(AREA_REGISTRY_RES);
                if (areaReg == null)
                {
                    areaReg = ScriptableObject.CreateInstance<AreaTagRegistrySO>();
                    AssetDatabase.CreateAsset(areaReg, AREA_REGISTRY_RES);
                }
                if (areaReg != null)
                {
                    // A4.2 merge semantics: union, do not overwrite existing tags.
                    if (areaReg.areaTags == null) areaReg.areaTags = new List<string>();
                    foreach (var tag in root.areaTags)
                        if (!string.IsNullOrWhiteSpace(tag) && !areaReg.areaTags.Contains(tag))
                            areaReg.areaTags.Add(tag);
                    EditorUtility.SetDirty(areaReg);
                }
            }

            // ── Import Animation Sequences ──
            if (root.animationSequences != null && root.animationSequences.Count > 0)
            {
                var animReg = AssetDatabase.LoadAssetAtPath<AnimationSequenceRegistry>(ANIM_REG_RES);
                if (animReg == null)
                {
                    EnsureFolder("Assets/Resources/Dooms");
                    animReg = ScriptableObject.CreateInstance<AnimationSequenceRegistry>();
                    AssetDatabase.CreateAsset(animReg, ANIM_REG_RES);
                }
                if (animReg != null)
                {
                    // A4.2 merge semantics: DO NOT clear. The animator's Scan &
                    // Populate output (incl. n-step holdSteps + props authored in
                    // the registry) must survive a scene import. We update an
                    // existing sequence by id, or add it if absent. Existing
                    // holdSteps/props on a matched sequence are preserved unless the
                    // JSON explicitly provides start/loop/end states.
                    if (animReg.sequences == null) animReg.sequences = new List<ActionAnimSequence>();
                    foreach (var seqData in root.animationSequences)
                    {
                        if (seqData == null || string.IsNullOrWhiteSpace(seqData.id)) continue;
                        var existing = animReg.sequences.Find(s =>
                            s != null && string.Equals(s.sequenceId, seqData.id, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            animReg.sequences.Add(new ActionAnimSequence
                            {
                                sequenceId = seqData.id,
                                startState = seqData.start,
                                loopState = seqData.loop,
                                endState = seqData.end,
                                startCrossfade = seqData.startCrossfade > 0f ? seqData.startCrossfade : 0.15f,
                                endCrossfade = seqData.endCrossfade > 0f ? seqData.endCrossfade : 0.15f
                            });
                        }
                        else
                        {
                            // Update only the fields the JSON provides; leave
                            // holdSteps/props (authored in Unity) intact.
                            if (!string.IsNullOrEmpty(seqData.start)) existing.startState = seqData.start;
                            if (!string.IsNullOrEmpty(seqData.loop))  existing.loopState  = seqData.loop;
                            if (!string.IsNullOrEmpty(seqData.end))   existing.endState   = seqData.end;
                            if (seqData.startCrossfade > 0f) existing.startCrossfade = seqData.startCrossfade;
                            if (seqData.endCrossfade > 0f)   existing.endCrossfade   = seqData.endCrossfade;
                        }
                    }
                    EditorUtility.SetDirty(animReg);
                }
            }

            if (root.scenes == null || root.scenes.Count == 0)
            {
                EditorUtility.DisplayDialog("DOOMS Import",
                    "Parsed 0 scenes from JSON.", "OK");
                return;
            }

            // ── 2. Ensure output folder exists ───────────────────────────────
            EnsureFolder(OUTPUT_FOLDER);

            // ── 3. Process scenes ────────────────────────────────────────────
            var registeredScenes = new List<SceneSO>();
            int created = 0, updated = 0;

            foreach (var def in root.scenes)
            {
                if (string.IsNullOrWhiteSpace(def.sceneId))
                {
                    Debug.LogWarning("[DOOMS][Importer] Skipping scene with empty sceneId.");
                    continue;
                }

                string assetPath = $"{OUTPUT_FOLDER}/{def.sceneId}.asset";
                bool exists = File.Exists(Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..",
                                 assetPath.Replace("Assets/", ""))));

                SceneSO so;
                if (exists)
                {
                    so = AssetDatabase.LoadAssetAtPath<SceneSO>(assetPath);
                    if (so == null)
                    {
                        // File exists but isn't imported yet — force re-import
                        AssetDatabase.ImportAsset(assetPath);
                        so = AssetDatabase.LoadAssetAtPath<SceneSO>(assetPath);
                    }
                }
                else
                {
                    so = ScriptableObject.CreateInstance<SceneSO>();
                    AssetDatabase.CreateAsset(so, assetPath);
                }

                if (so == null)
                {
                    Debug.LogError($"[DOOMS][Importer] Could not create/load asset at {assetPath}");
                    continue;
                }

                // ── 4. Remove old embedded graph ─────────────────────────────
                if (so.graph != null)
                {
                    var old = so.graph;
                    so.graph = null;
                    AssetDatabase.RemoveObjectFromAsset(old);
                    UnityEngine.Object.DestroyImmediate(old, true);
                }

                // ── 5. Populate SceneSO fields ───────────────────────────────
                so.sceneId             = def.sceneId;
                so.displayName         = string.IsNullOrEmpty(def.displayName) ? def.sceneId : def.displayName;
                so.description         = def.description ?? "";
                so.requiredFactions    = def.requiredFactions ?? new List<string>();
                so.minAgentsPerFaction = def.minAgentsPerFaction;
                so.baseDurationSec     = def.baseDurationSec;
                so.tags                = def.tags ?? new List<string>();

                // ── 6. Build SceneGraph ──────────────────────────────────────
                var graph = ScriptableObject.CreateInstance<SceneGraph>();
                graph.name       = def.sceneId + "_Graph";
                graph.hideFlags  = HideFlags.HideInHierarchy;

                if (def.phases != null && def.phases.Count > 0)
                    BuildGraph(graph, def.phases);

                so.graph = graph;
                AssetDatabase.AddObjectToAsset(graph, so);

                EditorUtility.SetDirty(graph);
                EditorUtility.SetDirty(so);

                registeredScenes.Add(so);
                if (exists) updated++; else created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── 7. Register in SceneRegistrySO ───────────────────────────────
            RegisterAll(registeredScenes);

            // ── 8. Run Validation & Auto-Generate Scene_Universe.md ──
            GenerateSceneUniverseDocs(root.scenes);

            Debug.Log($"[DOOMS][Importer] Done — {created} created, {updated} updated in {OUTPUT_FOLDER}.");
            EditorUtility.DisplayDialog("DOOMS Import Complete",
                $"{created} scenes created, {updated} updated.\n" +
                $"Total: {registeredScenes.Count} scenes registered in SceneRegistry.", "OK");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Graph builder
        // ─────────────────────────────────────────────────────────────────────

        static void BuildGraph(SceneGraph graph, List<PhaseDefData> phases)
        {
            ScenePhaseNode prev = null;
            float x = 0f;

            for (int i = 0; i < phases.Count; i++)
            {
                var pd = phases[i];
                if (string.IsNullOrWhiteSpace(pd.type)) continue;

                ScenePhaseNode node = AddPhaseNode(graph, pd.type);
                if (node == null) continue;

                // Position horizontally
                node.position = new Vector2(x, 0f);
                x += NODE_X_STEP;

                // Base fields
                node.phaseId        = string.IsNullOrEmpty(pd.phaseId) ? pd.type : pd.phaseId;
                node.minDurationSec = pd.minDurationSec;
                node.maxDurationSec = pd.maxDurationSec > 0f ? pd.maxDurationSec : pd.minDurationSec + 10f;

                // Type-specific fields
                if (node is AssemblePhaseNode aNode)
                    aNode.requiredPercentage = pd.requiredPercentage > 0f ? pd.requiredPercentage : 0.8f;

                // Roles
                node.roles = BuildRoles(pd.roles);

                // Prime node = first
                if (graph.primeNode == null)
                    graph.primeNode = node;

                // Connect from previous (guard against Disperse having no outputs)
                if (prev != null && !(prev is DispersePhaseNode))
                    graph.ConnectNodes(prev, node);

                prev = node;
            }
        }

        static ScenePhaseNode AddPhaseNode(SceneGraph graph, string type)
        {
            switch (type)
            {
                case "Assemble": return graph.AddNode<AssemblePhaseNode>();
                case "Hold":     return graph.AddNode<HoldPhaseNode>();
                case "Confront": return graph.AddNode<ConfrontPhaseNode>();
                case "Disperse": return graph.AddNode<DispersePhaseNode>();
                default:
                    Debug.LogWarning($"[DOOMS][Importer] Unknown phase type '{type}', defaulting to Hold.");
                    return graph.AddNode<HoldPhaseNode>();
            }
        }

        static List<RoleSlot> BuildRoles(List<RoleSlotData> defs)
        {
            var list = new List<RoleSlot>();
            if (defs == null) return list;

            foreach (var r in defs)
            {
                RoleKind kind = RoleKind.Point;
                if (!string.IsNullOrEmpty(r.roleKind))
                {
                    Enum.TryParse(r.roleKind, true, out kind);
                }

                list.Add(new RoleSlot
                {
                    roleId           = r.roleId ?? "",
                    roleKind         = kind,
                    factionId        = r.factionId ?? "",
                    pointTag         = r.pointTag ?? "",
                    animationState   = string.IsNullOrEmpty(r.animationState) ? "Idle" : r.animationState,
                    areaTag          = r.areaTag ?? "",
                    behavior         = string.IsNullOrEmpty(r.behavior) ? "Loiter" : r.behavior,
                    preferredBlendTree = r.preferredBlendTree ?? "",
                    pairWithFactionId = r.pairWithFactionId ?? "",
                    timelineAnchorId = !string.IsNullOrEmpty(r.timelineAnchorId) ? r.timelineAnchorId : r.timelineAssetId ?? "",
                    timelineSlotId   = r.timelineSlotId ?? "",
                    count            = Mathf.Max(1, r.count),
                    arrivalTolerance = r.arrivalTolerance > 0f ? r.arrivalTolerance : 2f,
                    optional         = r.optional
                });
            }
            return list;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Registry
        // ─────────────────────────────────────────────────────────────────────

        static void RegisterAll(List<SceneSO> scenes)
        {
            foreach (string regPath in new[] { REGISTRY_RES })
            {
                var reg = AssetDatabase.LoadAssetAtPath<SceneRegistrySO>(regPath);
                if (reg == null)
                {
                    Debug.LogWarning($"[DOOMS][Importer] SceneRegistrySO not found at {regPath}");
                    continue;
                }

                if (reg.scenes == null)
                    reg.scenes = new List<SceneSO>();

                // Add any scene not already in the list
                foreach (var so in scenes)
                {
                    if (so != null && !reg.scenes.Contains(so))
                        reg.scenes.Add(so);
                }

                EditorUtility.SetDirty(reg);
            }

            AssetDatabase.SaveAssets();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Utilities
        // ─────────────────────────────────────────────────────────────────────

        static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = folderPath.Contains("/")
                ? folderPath.Substring(0, folderPath.LastIndexOf('/'))
                : "Assets";
            string name = folderPath.Contains("/")
                ? folderPath.Substring(folderPath.LastIndexOf('/') + 1)
                : folderPath;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        static void GenerateSceneUniverseDocs(List<SceneDefData> scenes)
        {
            try
            {
                string docsPath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "..", "..", "docs", "Dooms", "Scene_Universe.md"));

                string dir = Path.GetDirectoryName(docsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# Scene Universe Reference");
                sb.AppendLine();
                sb.AppendLine("> *Auto-generated from dooms_scenes.json. Do not hand-edit.*");
                sb.AppendLine();
                sb.AppendLine("| Scene ID | Phase ID | Role ID | Role Kind | Faction | Target/Tag |");
                sb.AppendLine("|---|---|---|---|---|---|");

                foreach (var s in scenes)
                {
                    if (s.phases == null) continue;
                    foreach (var p in s.phases)
                    {
                        if (p.roles == null) continue;
                        foreach (var r in p.roles)
                        {
                            string target = r.pointTag;
                            if (string.Equals(r.roleKind, "Area", StringComparison.OrdinalIgnoreCase))
                                target = r.areaTag;
                            else if (string.Equals(r.roleKind, "Timeline", StringComparison.OrdinalIgnoreCase))
                                target = $"{r.timelineAssetId ?? r.timelineAnchorId} [{r.timelineSlotId}]";

                            sb.AppendLine($"| `{s.sceneId}` | `{p.phaseId}` | `{r.roleId}` | `{r.roleKind ?? "Point"}` | `{r.factionId}` | `{target}` |");
                        }
                    }
                }

                File.WriteAllText(docsPath, sb.ToString(), System.Text.Encoding.UTF8);
                Debug.Log($"[DOOMS][Importer] Generated Scene_Universe.md at: {docsPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DOOMS][Importer] Failed to auto-generate Scene_Universe.md: {e.Message}");
            }
        }
    }
}
#endif
