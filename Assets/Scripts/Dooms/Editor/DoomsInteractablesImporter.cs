#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MLA_SIM;
using MLA_SIM.ModularInteractions;
using V = MLA_SIM.Dooms.EditorTools.VocabularyValidator;

namespace MLA_SIM.Dooms.EditorTools
{
    // ── JSON DTOs (JsonUtility-compatible: classes, Lists, arrays — no dicts) ──
    [Serializable] internal class IODep_New
    {
        public List<string> contextTags = new List<string>();
        public List<string> actions = new List<string>();
        public List<string> items = new List<string>();
        public List<string> archetypes = new List<string>();
        public List<string> sequences = new List<string>();
        public List<string> props = new List<string>();
    }

    [Serializable] internal class IODep_ReqItem { public string itemId; public int quantity = 1; public bool consumed = false; }
    [Serializable] internal class IODep_YieldItem { public string itemId; public int minQuantity = 1; public int maxQuantity = 1; public float dropChance = 1f; public bool dropInWorld = false; }

    [Serializable] internal class IODep_Affordance
    {
        public string actionName = "InteractWith";
        public string description = "";
        public List<IODep_ReqItem> requiredItems = new List<IODep_ReqItem>();
        public List<string> requiredStates = new List<string>();
        public string resultingState = "Usable";
        public bool consumeItems = false;
        public List<IODep_YieldItem> yieldItems = new List<IODep_YieldItem>();
        public float estimatedDuration = 2f;
    }

    [Serializable] internal class IODep_Edge
    {
        public string actionName = "InteractWith";
        public List<string> requiredItemIds = new List<string>();
        public List<string> yieldItemIds = new List<string>();
        public int[] allowedTiers = new int[0];
        public string sequenceId = "";
        public string animatorStateName = "";
        public float holdSeconds = 2f;
        public string resultingStateId = "";
        public string conditionObjectName = "";
        public string conditionStateId = "";
        public string successMessage = "";
        public string failureMessage = "";
    }

    [Serializable] internal class IODep_State { public string stateId = "Usable"; public List<IODep_Edge> edges = new List<IODep_Edge>(); }
    [Serializable] internal class IODep_Graph { public string initialStateId = ""; public List<IODep_State> states = new List<IODep_State>(); }
    [Serializable] internal class IODep_Vec3 { public float x, y, z; }

    [Serializable] internal class IODep_Object
    {
        public string objectName = "";
        public string archetypeId = "";
        public string description = "";
        public string environmentHint = "";
        public List<string> contextTags = new List<string>();
        public float interactionRange = 2f;
        public float holdSeconds = 2f;
        public string sequenceId = "";
        public string animatorStateName = "";
        public int[] allowedTiers = new int[] { 1, 2 };
        public string currentState = "Usable";
        public List<IODep_Affordance> affordances = new List<IODep_Affordance>();
        public IODep_Graph graph;            // optional (opt-in)
        public IODep_Vec3 placement;         // optional
    }

    [Serializable] internal class IODep_Root
    {
        public IODep_New @new;
        public List<IODep_Object> objects = new List<IODep_Object>();
    }

    /// <summary>
    /// AREA 04 — A4.4. Imports dooms_interactables.json into scene InteractableObjects,
    /// mirroring DoomsScenesImporter. Uses the A4.2 validation gate (closed by
    /// default) and merges new vocabulary into the catalog/registries only when the
    /// author opts in. Builds an opt-in InteractionGraph asset when the object
    /// declares a graph. Auto-writes docs/Dooms/Interactable_Universe.md.
    /// </summary>
    public static class DoomsInteractablesImporter
    {
        private const string GRAPH_FOLDER = "Assets/Dooms/InteractionGraphs";

        [MenuItem("DOOMS/Import Interactables from JSON", priority = 101)]
        public static void ImportInteractables()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string jsonPath = Path.Combine(projectRoot, "configs", "scenarios", "dooms", "dooms_interactables.json");

            if (!File.Exists(jsonPath))
            {
                EditorUtility.DisplayDialog("DOOMS Import Interactables",
                    $"dooms_interactables.json not found at:\n{jsonPath}", "OK");
                return;
            }

            var root = JsonUtility.FromJson<IODep_Root>(File.ReadAllText(jsonPath));
            if (root == null || root.objects == null || root.objects.Count == 0)
            {
                EditorUtility.DisplayDialog("DOOMS Import Interactables", "Parsed 0 objects from JSON.", "OK");
                return;
            }

            // ── 1. Validate references against registries (closed by default) ──
            var declaredNew = BuildDeclaredNew(root.@new);
            var refs = CollectReferences(root);
            var report = V.Validate(refs, declaredNew);

            // Unknown references always block.
            if (report.HasBlockers)
            {
                EditorUtility.DisplayDialog("DOOMS Import Blocked",
                    report.Summarize() +
                    "\n\nUnknown references are blocked. Fix the typo, reuse an existing id, " +
                    "or declare it in the JSON 'new' block, then re-import.", "OK");
                return;
            }

            // Declared-new refs (no unknowns): offer to generate them or cancel.
            bool generateMissing = false;
            if (report.newDeclared.Count > 0)
            {
                bool ok = EditorUtility.DisplayDialog("DOOMS Import — Validation",
                    report.Summarize() + "\n\nGenerate the declared-new vocabulary and import?",
                    "Generate Missing & Import", "Cancel");
                if (!ok) return;
                generateMissing = true;
            }

            if (generateMissing)
            {
                int merged = V.MergeNew(report);
                Debug.Log($"[DOOMS][Interactables] Merged {merged} new vocabulary entries into registries/catalog.");
            }

            // ── 2. Materialize objects ──
            EnsureFolder(GRAPH_FOLDER);
            var catalog = InteractableCatalog.Instance;
            int created = 0, updated = 0;

            foreach (var def in root.objects)
            {
                if (string.IsNullOrWhiteSpace(def.objectName)) continue;

                var io = FindInteractableByName(def.objectName);
                bool isNew = io == null;
                if (isNew)
                {
                    var go = new GameObject(def.objectName);
                    io = go.AddComponent<InteractableObject>();
                    Undo.RegisterCreatedObjectUndo(go, "Import Interactable");
                }

                ApplyObject(io, def, catalog);
                EditorUtility.SetDirty(io);
                if (isNew) created++; else updated++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GenerateInteractableUniverseDoc(projectRoot, root.objects);

            EditorUtility.DisplayDialog("DOOMS Import Interactables Complete",
                $"{created} created, {updated} updated.\n" +
                $"Resolved {report.resolved.Count} refs · Generated {report.Created} new vocabulary entries.", "OK");
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static Dictionary<V.Kind, HashSet<string>> BuildDeclaredNew(IODep_New n)
        {
            var d = new Dictionary<V.Kind, HashSet<string>>();
            if (n == null) return d;
            d[V.Kind.ContextTag]        = new HashSet<string>(n.contextTags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            d[V.Kind.Action]            = new HashSet<string>(n.actions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            d[V.Kind.Item]              = new HashSet<string>(n.items ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            d[V.Kind.ObjectArchetype]   = new HashSet<string>(n.archetypes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            d[V.Kind.AnimationSequence] = new HashSet<string>(n.sequences ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            d[V.Kind.Prop]              = new HashSet<string>(n.props ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return d;
        }

        private static List<(V.Kind, string)> CollectReferences(IODep_Root root)
        {
            var refs = new List<(V.Kind, string)>();
            foreach (var o in root.objects)
            {
                if (!string.IsNullOrEmpty(o.archetypeId)) refs.Add((V.Kind.ObjectArchetype, o.archetypeId));
                if (!string.IsNullOrEmpty(o.sequenceId)) refs.Add((V.Kind.AnimationSequence, o.sequenceId));
                if (!string.IsNullOrEmpty(o.animatorStateName)) refs.Add((V.Kind.AnimationState, o.animatorStateName));
                foreach (var t in o.contextTags ?? new List<string>()) refs.Add((V.Kind.ContextTag, t));
                foreach (var a in o.affordances ?? new List<IODep_Affordance>())
                {
                    if (!string.IsNullOrEmpty(a.actionName)) refs.Add((V.Kind.Action, a.actionName));
                    foreach (var ri in a.requiredItems ?? new List<IODep_ReqItem>()) if (!string.IsNullOrEmpty(ri.itemId)) refs.Add((V.Kind.Item, ri.itemId));
                    foreach (var yi in a.yieldItems ?? new List<IODep_YieldItem>()) if (!string.IsNullOrEmpty(yi.itemId)) refs.Add((V.Kind.Item, yi.itemId));
                }
                if (o.graph != null)
                {
                    foreach (var st in o.graph.states ?? new List<IODep_State>())
                        foreach (var e in st.edges ?? new List<IODep_Edge>())
                        {
                            if (!string.IsNullOrEmpty(e.actionName)) refs.Add((V.Kind.Action, e.actionName));
                            if (!string.IsNullOrEmpty(e.sequenceId)) refs.Add((V.Kind.AnimationSequence, e.sequenceId));
                            if (!string.IsNullOrEmpty(e.animatorStateName)) refs.Add((V.Kind.AnimationState, e.animatorStateName));
                            foreach (var id in e.requiredItemIds ?? new List<string>()) refs.Add((V.Kind.Item, id));
                            foreach (var id in e.yieldItemIds ?? new List<string>()) refs.Add((V.Kind.Item, id));
                        }
                }
            }
            return refs;
        }

        private static InteractableObject FindInteractableByName(string name)
        {
            return UnityEngine.Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None)
                .FirstOrDefault(o => o != null &&
                    (string.Equals(o.objectName, name, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(o.gameObject.name, name, StringComparison.OrdinalIgnoreCase)));
        }

        private static void ApplyObject(InteractableObject io, IODep_Object def, InteractableCatalog catalog)
        {
            io.objectName = def.objectName;
            if (!string.IsNullOrEmpty(def.archetypeId)) io.archetypeId = def.archetypeId;
            if (!string.IsNullOrEmpty(def.description)) io.description = def.description;
            if (!string.IsNullOrEmpty(def.environmentHint)) io.environmentHint = def.environmentHint;
            if (def.contextTags != null && def.contextTags.Count > 0) io.contextTags = def.contextTags.ToArray();
            io.interactionRange = def.interactionRange > 0 ? def.interactionRange : io.interactionRange;
            io.holdSeconds = def.holdSeconds > 0 ? def.holdSeconds : io.holdSeconds;
            if (!string.IsNullOrEmpty(def.sequenceId)) io.sequenceId = def.sequenceId;
            if (!string.IsNullOrEmpty(def.animatorStateName)) io.animatorStateName = def.animatorStateName;
            if (def.allowedTiers != null && def.allowedTiers.Length > 0) io.allowedTiers = DoomsTierUtil.FromIntArray(def.allowedTiers);
            if (catalog != null) io.sharedCatalog = catalog;
            io.currentState = ParseState(def.currentState, InteractableObject.ObjectState.Usable);

            if (def.placement != null)
                io.transform.position = new Vector3(def.placement.x, def.placement.y, def.placement.z);

            // Affordances (merge by actionName)
            if (def.affordances != null)
            {
                foreach (var a in def.affordances)
                {
                    var existing = io.actionAffordances.FirstOrDefault(x =>
                        string.Equals(x.actionName, a.actionName, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) { existing = new ActionAffordance(); io.actionAffordances.Add(existing); }
                    existing.actionName = a.actionName;
                    existing.description = a.description ?? "";
                    existing.requiredItems = (a.requiredItems ?? new List<IODep_ReqItem>())
                        .Select(ri => new RequiredItem { itemId = ri.itemId, quantity = ri.quantity, consumed = ri.consumed }).ToArray();
                    existing.requiredStates = (a.requiredStates != null && a.requiredStates.Count > 0)
                        ? a.requiredStates.Select(s => ParseState(s, InteractableObject.ObjectState.Usable)).ToArray()
                        : new[] { InteractableObject.ObjectState.Usable };
                    existing.resultingState = ParseState(a.resultingState, InteractableObject.ObjectState.Usable);
                    existing.consumeItems = a.consumeItems;
                    existing.yieldItems = (a.yieldItems ?? new List<IODep_YieldItem>())
                        .Select(yi => new YieldItem { itemId = yi.itemId, minQuantity = yi.minQuantity, maxQuantity = yi.maxQuantity, dropChance = yi.dropChance, dropInWorld = yi.dropInWorld }).ToArray();
                    existing.estimatedDuration = a.estimatedDuration > 0 ? a.estimatedDuration : 2f;
                }
            }

            // Opt-in graph
            if (def.graph != null && def.graph.states != null && def.graph.states.Count > 0)
                io.interactionGraph = BuildGraph(def);
        }

        private static InteractionGraph BuildGraph(IODep_Object def)
        {
            string path = $"{GRAPH_FOLDER}/{Sanitize(def.objectName)}_Graph.asset";
            var graph = AssetDatabase.LoadAssetAtPath<InteractionGraph>(path);
            if (graph == null)
            {
                graph = ScriptableObject.CreateInstance<InteractionGraph>();
                AssetDatabase.CreateAsset(graph, path);
            }
            graph.nodes = new List<InteractionStateNode>();
            graph.initialStateId = def.graph.initialStateId ?? "";
            float x = 0f;
            foreach (var st in def.graph.states)
            {
                var node = graph.AddNode(st.stateId);
                node.position = new Vector2(x, 0f);
                x += 240f;
                foreach (var e in st.edges ?? new List<IODep_Edge>())
                {
                    node.edges.Add(new InteractionEdge
                    {
                        actionName = e.actionName,
                        requiredItemIds = (e.requiredItemIds ?? new List<string>()).ToArray(),
                        yieldItemIds = (e.yieldItemIds ?? new List<string>()).ToArray(),
                        allowedTiers = DoomsTierUtil.FromIntArray(e.allowedTiers ?? new int[0]),
                        sequenceId = e.sequenceId ?? "",
                        animatorStateName = e.animatorStateName ?? "",
                        holdSeconds = e.holdSeconds > 0 ? e.holdSeconds : 2f,
                        resultingStateId = e.resultingStateId ?? "",
                        conditionObjectName = e.conditionObjectName ?? "",
                        conditionStateId = e.conditionStateId ?? "",
                        successMessage = e.successMessage ?? "",
                        failureMessage = e.failureMessage ?? ""
                    });
                }
            }
            EditorUtility.SetDirty(graph);
            return graph;
        }

        private static InteractableObject.ObjectState ParseState(string s, InteractableObject.ObjectState fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return Enum.TryParse<InteractableObject.ObjectState>(s, true, out var v) ? v : fallback;
        }

        private static string Sanitize(string s) => string.Join("_", (s ?? "Object").Split(Path.GetInvalidFileNameChars()));

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string parent = folderPath.Contains("/") ? folderPath.Substring(0, folderPath.LastIndexOf('/')) : "Assets";
            string name = folderPath.Contains("/") ? folderPath.Substring(folderPath.LastIndexOf('/') + 1) : folderPath;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void GenerateInteractableUniverseDoc(string projectRoot, List<IODep_Object> objects)
        {
            try
            {
                string docsPath = Path.Combine(projectRoot, "docs", "Dooms", "Interactable_Universe.md");
                var sb = new StringBuilder();
                sb.AppendLine("# Interactable Universe Reference");
                sb.AppendLine();
                sb.AppendLine("> *Auto-generated from dooms_interactables.json. Do not hand-edit.*");
                sb.AppendLine();
                sb.AppendLine("| Object | Archetype | Sequence | Hold | Graph | Actions |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var o in objects)
                {
                    string actions = string.Join(", ", (o.affordances ?? new List<IODep_Affordance>()).Select(a => a.actionName));
                    string graph = (o.graph != null && o.graph.states != null && o.graph.states.Count > 0)
                        ? $"{o.graph.states.Count} states" : "—";
                    sb.AppendLine($"| `{o.objectName}` | `{o.archetypeId}` | `{o.sequenceId}` | {o.holdSeconds:0.#}s | {graph} | {actions} |");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(docsPath));
                File.WriteAllText(docsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DOOMS][Interactables] Failed to write Interactable_Universe.md: {e.Message}");
            }
        }
    }
}
#endif
