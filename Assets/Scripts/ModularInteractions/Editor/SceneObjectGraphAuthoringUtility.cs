#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using MLA_SIM.Interactions;
using MLA_SIM.ModularInteractions;

namespace MLA_SIM.EditorTools
{
    [Serializable]
    internal class SceneObjectGraphCatalogExport
    {
        public List<string> registeredObjectIds = new List<string>();
        public List<SceneObjectGraphArchetypeExport> archetypes = new List<SceneObjectGraphArchetypeExport>();
        public List<SceneObjectGraphItemExport> items = new List<SceneObjectGraphItemExport>();
        public List<string> contextTags = new List<string>();
    }

    [Serializable]
    internal class SceneObjectGraphArchetypeExport
    {
        public string id = "";
        public string defaultName = "";
        public string defaultDescription = "";
        public string defaultState = "";
        public List<string> defaultContextTags = new List<string>();
        public string interactionHint = "";
    }

    [Serializable]
    internal class SceneObjectGraphItemExport
    {
        public string id = "";
        public string displayName = "";
        public string description = "";
        public List<string> defaultContextTags = new List<string>();
        public string interactionHint = "";
        public string category = "";
    }

    [Serializable]
    internal class SceneObjectGraphDoomsSceneExport
    {
        public string sceneId = "";
        public string displayName = "";
        public string description = "";
        public string narrativePhase = "";
        public List<string> requiredFactions = new List<string>();
        public List<string> optionalFactions = new List<string>();
        public List<string> tags = new List<string>();
    }

    [Serializable]
    internal class SceneObjectGraphExportRoot
    {
        public string _comment = "";
        public string generatedUtc = "";
        public string graphAsset = "";
        public string activeSceneName = "";
        public SceneObjectGraphCatalogExport catalog = new SceneObjectGraphCatalogExport();
        public SceneObjectGraphDoomsSceneExport doomsScene = new SceneObjectGraphDoomsSceneExport();
        public List<SceneObjectGraphObjectExport> objects = new List<SceneObjectGraphObjectExport>();
    }

    [Serializable]
    internal class SceneObjectGraphObjectExport
    {
        public string objectId = "";
        public string displayName = "";
        public string sourceObjectName = "";
        public string archetypeId = "";
        public string description = "";
        public string environmentHint = "";
        public List<string> contextTags = new List<string>();
        public Vector3 sceneWorldPosition;
        public string sourceSceneName = "";
        public string initialStateId = "";
        public List<SceneObjectGraphStateExport> states = new List<SceneObjectGraphStateExport>();
    }

    [Serializable]
    internal class SceneObjectGraphStateExport
    {
        public string stateId = "";
        public List<SceneObjectGraphEdgeExport> edges = new List<SceneObjectGraphEdgeExport>();
    }

    [Serializable]
    internal class SceneObjectGraphEdgeExport
    {
        public string id = "";
        public string actionName = "";
        public int allowedTierMask = 0;
        public List<string> requiredItemIds = new List<string>();
        public List<string> yieldItemIds = new List<string>();
        public string sequenceId = "";
        public string animatorStateName = "";
        public float holdSeconds = 0f;
        public string resultingStateId = "";
        public string conditionObjectName = "";
        public string conditionStateId = "";
        public RuleHints hints = new RuleHints();
        public List<string> preconditions = new List<string>();
        public List<string> effects = new List<string>();
        public string successMessage = "";
        public string failureMessage = "";
    }

    [Serializable]
    internal class SceneObjectGraphRelationSpec
    {
        public string sourceObjectId = "";
        public string sourceStateId = "";
        public string targetObjectId = "";
        public string targetStateId = "";
        public string actionName = "";
        public string gatedActionName = "";
        public string gatedFromStateId = "";
        public int allowedTierMask = 0;
        public List<string> requiredItemIds = new List<string>();
        public List<string> yieldItemIds = new List<string>();
        public string sequenceId = "";
        public string animatorStateName = "";
        public float holdSeconds = 0f;
        public string resultingStateId = "";
        public string conditionObjectName = "";
        public string conditionStateId = "";
        public RuleHints hints = new RuleHints();
        public string successMessage = "";
        public string failureMessage = "";
    }

    [Serializable]
    internal class SceneObjectGraphAiResponseRoot
    {
        public List<SceneObjectGraphRelationSpec> relations = new List<SceneObjectGraphRelationSpec>();
        public List<SceneObjectGraphObjectExport> objects = new List<SceneObjectGraphObjectExport>();
    }

    [Serializable]
    internal class DoomsSceneFileRoot
    {
        public List<DoomsSceneFileEntry> scenes = new List<DoomsSceneFileEntry>();
    }

    [Serializable]
    internal class DoomsSceneFileEntry
    {
        public string sceneId = "";
        public string displayName = "";
        public string description = "";
        public string narrativePhase = "";
        public List<string> requiredFactions = new List<string>();
        public List<string> optionalFactions = new List<string>();
        public List<string> tags = new List<string>();
    }

    [Serializable]
    internal class OpenAiChatRequest
    {
        public string model = "";
        public List<OpenAiChatMessage> messages = new List<OpenAiChatMessage>();
        public int max_tokens = 1024;
        public float temperature = 0.2f;
    }

    [Serializable]
    internal class OpenAiChatMessage
    {
        public string role = "";
        public string content = "";
    }

    [Serializable]
    internal class OpenAiChatResponse
    {
        public List<OpenAiChoice> choices = new List<OpenAiChoice>();
    }

    [Serializable]
    internal class OpenAiChoice
    {
        public OpenAiMessage message = new OpenAiMessage();
    }

    [Serializable]
    internal class OpenAiMessage
    {
        public string role = "";
        public string content = "";
    }

    [Serializable]
    internal class SceneObjectGraphSequenceEntryExport
    {
        public string sequenceId = "";
        public string startState = "";
        public string loopState = "";
        public string endState = "";
    }

    [Serializable]
    internal class SceneObjectGraphAnimationExport
    {
        public List<SceneObjectGraphSequenceEntryExport> sequences = new List<SceneObjectGraphSequenceEntryExport>();
        public List<string> animatorStateNames = new List<string>();
    }

    [Serializable]
    internal class SceneObjectGraphWorldContextExport
    {
        public string worldSetting = "";
        public string timePeriod = "";
        public string narrativeTone = "";
        public string weather = "";
        public string threatLevel = "";
        public string timeOfDay = "";
        public float resourceAvailability = 0f;
        public List<string> worldRules = new List<string>();
        public List<string> keyLocations = new List<string>();
        public List<string> historicalEvents = new List<string>();
    }

    [Serializable]
    internal class SceneObjectGraphFullExportRoot
    {
        public string _instructions = "";
        public string _outputFormat = "";
        public string _exampleOutput = "";
        public string generatedUtc = "";
        public string graphAsset = "";
        public string activeSceneName = "";
        public SceneObjectGraphWorldContextExport worldContext = new SceneObjectGraphWorldContextExport();
        public List<SceneObjectGraphDoomsSceneExport> allScenes = new List<SceneObjectGraphDoomsSceneExport>();
        public List<string> availableObjectIds = new List<string>();
        public List<string> availableItemIds = new List<string>();
        public List<string> availableSequenceIds = new List<string>();
        public List<string> availableAnimatorStateNames = new List<string>();
        public List<SceneObjectGraphObjectExport> sceneObjects = new List<SceneObjectGraphObjectExport>();
        public List<SceneObjectGraphItemExport> items = new List<SceneObjectGraphItemExport>();
        public List<SceneObjectGraphArchetypeExport> archetypes = new List<SceneObjectGraphArchetypeExport>();
        public SceneObjectGraphAnimationExport animation = new SceneObjectGraphAnimationExport();
    }

    internal sealed class PopulateReport
    {
        public int scannedObjects;
        public int matchedNodes;
        public int createdNodes;
        public int updatedNodes;
        public int addedStates;
        public int addedEdges;
        public int syncedCatalogIds;
        public List<string> warnings = new List<string>();
    }

    internal sealed class ExportReport
    {
        public string json = "";
        public string path = "";
        public int objectCount;
    }

    internal sealed class ValidationIssue
    {
        public string scope = "";
        public string message = "";
        public string suggestion = "";
    }

    internal sealed class ValidationReport
    {
        public int syncedCatalogIds;
        public int checkedNodes;
        public List<ValidationIssue> issues = new List<ValidationIssue>();

        public string Summary => issues.Count == 0
            ? $"Validation passed ({checkedNodes} node(s) checked, catalogIds={syncedCatalogIds})."
            : $"Validation found {issues.Count} issue(s) across {checkedNodes} node(s), catalogIds={syncedCatalogIds}.";
    }

    internal sealed class AiPromptReport
    {
        public string prompt = "";
        public string jsonPreview = "";
    }

    internal static class SceneObjectGraphAuthoringUtility
    {
        private const string DefaultExportPath = "configs/scenarios/dooms/dooms_scene_objects.json";
        private const string DoomsScenesPath = "configs/scenarios/dooms/dooms_scenes.json";

        public static InteractableCatalog ResolveCatalog(InteractableCatalog explicitCatalog = null)
        {
            if (explicitCatalog != null) return explicitCatalog;
            return InteractableCatalog.Instance;
        }

        public static SceneObjectGraph FindSelectedGraph()
        {
            return Selection.activeObject as SceneObjectGraph;
        }

        public static List<InteractableObject> GetSceneInteractables()
        {
            return UnityEngine.Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None)
                .Where(io => io != null)
                .OrderBy(io => io.GetObjectName(), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static PopulateReport PopulateGraphFromScene(
            SceneObjectGraph graph,
            InteractableCatalog catalog,
            bool syncCatalog = true,
            bool includeLegacyInteractionGraph = true)
        {
            var report = new PopulateReport();
            if (graph == null)
            {
                report.warnings.Add("No SceneObjectGraph selected.");
                return report;
            }

            var sceneObjects = GetSceneInteractables();
            report.scannedObjects = sceneObjects.Count;

            Undo.RegisterCompleteObjectUndo(graph, "Populate Scene Object Graph");

            int layoutIndex = CountSceneObjectNodes(graph);
            string activeSceneName = SceneManager.GetActiveScene().name;

            foreach (var io in sceneObjects)
            {
                if (io == null) continue;

                string objectId = ResolveObjectId(io);
                if (string.IsNullOrWhiteSpace(objectId))
                {
                    report.warnings.Add($"Skipped '{io.gameObject.name}' because no object id could be resolved.");
                    continue;
                }

                var node = graph.FindObjectNode(objectId);
                if (node == null)
                {
                    node = graph.AddNode<SceneObjectNode>();
                    node.position = new Vector2(layoutIndex * 280f, 0f);
                    layoutIndex++;
                    node.objectId = objectId;
                    report.createdNodes++;
                }
                else
                {
                    report.matchedNodes++;
                }

                MergeNodeMetadata(node, io, activeSceneName, catalog);

                int beforeStates = CountStates(node);
                int beforeEdges = CountEdges(node);

                MergeAffordanceStatesAndEdges(node, io, catalog);
                if (includeLegacyInteractionGraph && io.interactionGraph != null)
                {
                    MergeLegacyGraph(node, io.interactionGraph);
                }

                report.addedStates += Math.Max(0, CountStates(node) - beforeStates);
                report.addedEdges += Math.Max(0, CountEdges(node) - beforeEdges);
                report.updatedNodes++;
            }

            if (syncCatalog)
            {
                report.syncedCatalogIds = SyncCatalogFromGraph(graph, catalog);
            }

            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return report;
        }

        public static int SyncCatalogFromGraph(SceneObjectGraph graph, InteractableCatalog catalog = null)
        {
            if (graph == null) return 0;
            catalog = ResolveCatalog(catalog);
            if (catalog == null) return 0;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in graph.allNodes)
            {
                if (node is SceneObjectNode objNode && !string.IsNullOrWhiteSpace(objNode.objectId))
                {
                    ids.Add(objNode.objectId.Trim());
                }
            }

            Undo.RecordObject(catalog, "Sync Scene Object Graph Catalog IDs");
            if (catalog.registeredObjectIds == null) catalog.registeredObjectIds = new List<string>();
            foreach (var existing in catalog.registeredObjectIds)
            {
                if (!string.IsNullOrWhiteSpace(existing)) ids.Add(existing.Trim());
            }

            catalog.registeredObjectIds = ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog.registeredObjectIds.Count;
        }

        public static ValidationReport ValidateGraph(
            SceneObjectGraph graph,
            InteractableCatalog catalog = null,
            bool syncCatalog = true)
        {
            var report = new ValidationReport();
            if (graph == null)
            {
                report.issues.Add(new ValidationIssue
                {
                    scope = "Graph",
                    message = "No SceneObjectGraph selected.",
                    suggestion = "Select a graph asset before validating."
                });
                return report;
            }

            catalog = ResolveCatalog(catalog);
            if (syncCatalog)
                report.syncedCatalogIds = SyncCatalogFromGraph(graph, catalog);

            var validObjectIds = new HashSet<string>(SogRegistryProvider.GetOptions(RegistryType.ObjectId), StringComparer.OrdinalIgnoreCase);
            var validStates = new HashSet<string>(SogRegistryProvider.GetOptions(RegistryType.ObjectState), StringComparer.OrdinalIgnoreCase);
            var validActions = new HashSet<string>(SogRegistryProvider.GetOptions(RegistryType.Action), StringComparer.OrdinalIgnoreCase);
            var validItems = new HashSet<string>(SogRegistryProvider.GetOptions(RegistryType.Item), StringComparer.OrdinalIgnoreCase);
            var validSequences = new HashSet<string>(SogRegistryProvider.GetOptions(RegistryType.AnimationSequence), StringComparer.OrdinalIgnoreCase);
            var validAnimatorStates = new HashSet<string>(SogRegistryProvider.GetOptions(RegistryType.AnimationState), StringComparer.OrdinalIgnoreCase);

            foreach (var node in graph.allNodes)
            {
                if (node is not SceneObjectNode objNode) continue;
                report.checkedNodes++;

                ValidateValue(report, "Node.objectId", objNode.objectId, validObjectIds, "object id");
                ValidateValue(report, $"Node[{objNode.objectId}].initialStateId", objNode.initialStateId, validStates, "object state");

                if (objNode.states == null) continue;

                foreach (var state in objNode.states)
                {
                    if (state == null) continue;
                    ValidateValue(report, $"Node[{objNode.objectId}].stateId", state.stateId, validStates, "object state");

                    if (state.edges == null) continue;
                    foreach (var edge in state.edges)
                    {
                        if (edge == null) continue;

                        ValidateValue(report, $"Edge[{objNode.objectId}:{state.stateId}].actionName", edge.actionName, validActions, "action");
                        ValidateValue(report, $"Edge[{objNode.objectId}:{state.stateId}].resultingStateId", edge.resultingStateId, validStates, "object state");
                        ValidateValue(report, $"Edge[{objNode.objectId}:{state.stateId}].conditionStateId", edge.conditionStateId, validStates, "object state");
                        ValidateValue(report, $"Edge[{objNode.objectId}:{state.stateId}].sequenceId", edge.sequenceId, validSequences, "animation sequence");
                        ValidateValue(report, $"Edge[{objNode.objectId}:{state.stateId}].animatorStateName", edge.animatorStateName, validAnimatorStates, "animation state");

                        if (edge.requiredItemIds != null)
                        {
                            foreach (var item in edge.requiredItemIds.Where(s => !string.IsNullOrWhiteSpace(s)))
                                ValidateValue(report, $"Edge[{objNode.objectId}:{state.stateId}].requiredItemIds", item, validItems, "item");
                        }

                        if (edge.yieldItemIds != null)
                        {
                            foreach (var item in edge.yieldItemIds.Where(s => !string.IsNullOrWhiteSpace(s)))
                                ValidateValue(report, $"Edge[{objNode.objectId}:{state.stateId}].yieldItemIds", item, validItems, "item");
                        }
                    }
                }

                foreach (var conn in objNode.inConnections)
                {
                    if (conn is not ObjectDependencyConnection depConn) continue;
                    var sourceNode = depConn.sourceNode as SceneObjectNode;
                    if (sourceNode == null)
                    {
                        report.issues.Add(new ValidationIssue
                        {
                            scope = $"Connection[{objNode.objectId}]",
                            message = "Dependency connection has no source SceneObjectNode.",
                            suggestion = "Reconnect the line to a valid source node."
                        });
                        continue;
                    }

                    ValidateValue(report, $"Connection[{sourceNode.objectId}->{objNode.objectId}].requiredSourceStateId", depConn.requiredSourceStateId, validStates, "object state");

                    if (!string.IsNullOrWhiteSpace(depConn.gatedActionName) && !string.IsNullOrWhiteSpace(depConn.gatedFromStateId))
                    {
                        var edge = objNode.FindEdge(depConn.gatedFromStateId, depConn.gatedActionName);
                        if (edge == null)
                        {
                            report.issues.Add(new ValidationIssue
                            {
                                scope = $"Connection[{sourceNode.objectId}->{objNode.objectId}]",
                                message = $"No edge found for gated action '{depConn.gatedActionName}' from state '{depConn.gatedFromStateId}'.",
                                suggestion = "Pick an existing edge or update the gate metadata to match the target node."
                            });
                        }
                    }
                }
            }

            return report;
        }

        private static void ValidateValue(
            ValidationReport report,
            string scope,
            string value,
            HashSet<string> validValues,
            string valueKind)
        {
            if (string.IsNullOrWhiteSpace(value) || validValues == null || validValues.Count == 0)
                return;

            if (validValues.Contains(value))
                return;

            report.issues.Add(new ValidationIssue
            {
                scope = scope,
                message = $"Unknown {valueKind}: '{value}'.",
                suggestion = $"Rename it to a known {valueKind} or clear the field."
            });
        }

        public static ExportReport ExportGraphToJson(
            SceneObjectGraph graph,
            InteractableCatalog catalog,
            bool includeCatalogContext,
            bool includeDoomsContext,
            string exportPath = DefaultExportPath,
            string sceneHint = "")
        {
            var report = new ExportReport();
            if (graph == null)
            {
                report.json = "";
                report.path = exportPath;
                return report;
            }

            var root = BuildExportRoot(graph, catalog, includeCatalogContext, includeDoomsContext, sceneHint);
            report.json = JsonUtility.ToJson(root, true);
            report.path = ResolveProjectPath(exportPath);
            report.objectCount = root.objects.Count;

            Directory.CreateDirectory(Path.GetDirectoryName(report.path));
            File.WriteAllText(report.path, report.json, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            return report;
        }

        public static SceneObjectGraphExportRoot BuildExportRoot(
            SceneObjectGraph graph,
            InteractableCatalog catalog,
            bool includeCatalogContext,
            bool includeDoomsContext,
            string sceneHint = "")
        {
            var root = new SceneObjectGraphExportRoot
            {
                _comment = "Auto-generated by MLA SIM SceneObjectGraph tools. Safe to use as AI context or export artifact.",
                generatedUtc = DateTime.UtcNow.ToString("O"),
                graphAsset = graph != null ? graph.name : "",
                activeSceneName = SceneManager.GetActiveScene().name,
                catalog = includeCatalogContext ? BuildCatalogExport(catalog) : new SceneObjectGraphCatalogExport(),
                doomsScene = includeDoomsContext ? BuildDoomsSceneExport(sceneHint, graph) : new SceneObjectGraphDoomsSceneExport(),
                objects = BuildObjectExports(graph)
            };
            return root;
        }

        public static ExportReport ExportFullContext(
            SceneObjectGraph graph,
            InteractableCatalog catalog,
            string exportPath = DefaultExportPath,
            string sceneHint = "")
        {
            var report = new ExportReport();
            report.path = ResolveProjectPath(exportPath);
            if (graph == null) return report;

            catalog = ResolveCatalog(catalog);

            var worldContext = BuildWorldContextExport();
            var allScenes = BuildAllDoomsSceneExports(graph);
            var catalogExport = BuildCatalogExport(catalog);
            var sceneObjects = BuildObjectExports(graph);

            var animExport = new SceneObjectGraphAnimationExport();
            var seqReg = MLA_SIM.AnimationSequenceRegistry.Instance;
            if (seqReg?.sequences != null)
            {
                foreach (var seq in seqReg.sequences)
                {
                    if (seq == null || string.IsNullOrWhiteSpace(seq.sequenceId)) continue;
                    animExport.sequences.Add(new SceneObjectGraphSequenceEntryExport
                    {
                        sequenceId = seq.sequenceId,
                        startState = seq.startState ?? "",
                        loopState = seq.loopState ?? "",
                        endState = seq.endState ?? ""
                    });
                }
            }
            var stateReg = MLA_SIM.Dooms.Registries.AnimationStateRegistrySO.Instance;
            if (stateReg?.states != null)
                animExport.animatorStateNames = stateReg.states.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            var availableObjectIds = sceneObjects
                .Select(o => o.objectId).Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            var availableItemIds = catalogExport.items
                .Select(i => i.id).Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            var availableSequenceIds = animExport.sequences
                .Select(s => s.sequenceId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            var availableAnimatorStateNames = animExport.animatorStateNames.ToList();

            string instructions =
                $"You are a DOOMS narrative designer authoring a Scene Object Graph (SOG) for '{graph.name}'. " +
                "Your task: analyse ALL provided sceneObjects, their states and existing edges, the items, archetypes, animation, " +
                "and narrative context, then output a RICH '{\"relations\":[...]}' JSON that defines every meaningful cross-object " +
                "interaction. Cover: power chains, repair sequences, crafting, narrative unlocks, environmental triggers. " +
                "For every relation, populate sequenceId and animatorStateName wherever a plausible animation exists. " +
                "Do NOT invent objectIds or itemIds — use ONLY the ids listed in availableObjectIds and availableItemIds.";

            string outputFormat =
                "Return ONLY a raw JSON object with a single 'relations' array. No markdown fences. No explanation. No extra keys. " +
                "Each element has exactly: sourceObjectId, sourceStateId, targetObjectId, targetStateId, actionName, " +
                "conditionObjectName, conditionStateId, resultingStateId, requiredItemIds (array), yieldItemIds (array), " +
                "sequenceId, animatorStateName, holdSeconds (number), successMessage, failureMessage.";

            string exampleOutput =
                "{\"relations\":[{\"sourceObjectId\":\"Car Battery\",\"sourceStateId\":\"Usable\"," +
                "\"targetObjectId\":\"Generator\",\"targetStateId\":\"Broken\",\"actionName\":\"Jumpstart\"," +
                "\"conditionObjectName\":\"Car Battery\",\"conditionStateId\":\"Usable\",\"resultingStateId\":\"Running\"," +
                "\"requiredItemIds\":[\"ToolBox\"],\"yieldItemIds\":[],\"sequenceId\":\"RepairSequence\"," +
                "\"animatorStateName\":\"TendingMachines\",\"holdSeconds\":4.0," +
                "\"successMessage\":\"Generator started!\",\"failureMessage\":\"Couldn't start the generator.\"}]}";

            var root = new SceneObjectGraphFullExportRoot
            {
                _instructions = instructions,
                _outputFormat = outputFormat,
                _exampleOutput = exampleOutput,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                graphAsset = graph.name,
                activeSceneName = SceneManager.GetActiveScene().name,
                worldContext = worldContext,
                allScenes = allScenes,
                availableObjectIds = availableObjectIds,
                availableItemIds = availableItemIds,
                availableSequenceIds = availableSequenceIds,
                availableAnimatorStateNames = availableAnimatorStateNames,
                sceneObjects = sceneObjects,
                items = catalogExport.items,
                archetypes = catalogExport.archetypes,
                animation = animExport
            };

            report.json = JsonUtility.ToJson(root, true);
            report.objectCount = sceneObjects.Count;

            Directory.CreateDirectory(Path.GetDirectoryName(report.path));
            File.WriteAllText(report.path, report.json, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            return report;
        }

        public static AiPromptReport BuildAiPrompt(
            SceneObjectGraph graph,
            InteractableCatalog catalog,
            bool includeCatalogContext,
            bool includeDoomsContext,
            string sceneHint,
            out SceneObjectGraphExportRoot exportRoot)
        {
            exportRoot = BuildExportRoot(graph, catalog, includeCatalogContext, includeDoomsContext, sceneHint);
            string exportJson = JsonUtility.ToJson(exportRoot, true);
            string compactContext = BuildCompactPromptContext(exportRoot);

            var sb = new StringBuilder();
            sb.AppendLine("You are a DOOMS scene-object graph assistant.");
            sb.AppendLine("Your task is to add plausible IO-to-IO relations and state-machine edges.");
            sb.AppendLine("Use only the ids that already appear in the provided catalog, scene objects, and DOOMS context.");
            sb.AppendLine("Return raw JSON only.");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- Do not rename existing objects or states.");
            sb.AppendLine("- Do not delete anything.");
            sb.AppendLine("- Prefer adding missing relations over rewriting the graph.");
            sb.AppendLine("- If you add a relation, keep it physically and narratively plausible for the scene.");
            sb.AppendLine("- Output either a full updated graph root or a {\"relations\": [...]} patch.");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: use ONLY these exact objectIds (copy verbatim, case-sensitive):");
            if (exportRoot.objects != null && exportRoot.objects.Count > 0)
                sb.AppendLine(string.Join(", ", exportRoot.objects.Select(o => o.objectId)));
            sb.AppendLine();
            sb.AppendLine("GRAPH_CONTEXT_SUMMARY:");
            sb.AppendLine(compactContext);
            sb.AppendLine();
            sb.AppendLine("Suggested relation schema when using a patch:");
            sb.AppendLine("{\"relations\":[{\"sourceObjectId\":\"Generator\",\"sourceStateId\":\"Running\",\"targetObjectId\":\"Transformer\",\"targetStateId\":\"Off\",\"actionName\":\"Fix\",\"conditionObjectName\":\"Generator\",\"conditionStateId\":\"Running\",\"resultingStateId\":\"Powered\",\"requiredItemIds\":[\"ToolBox\"],\"yieldItemIds\":[],\"sequenceId\":\"\",\"animatorStateName\":\"\",\"holdSeconds\":2.0}]}" );

            return new AiPromptReport
            {
                prompt = sb.ToString(),
                jsonPreview = exportJson
            };
        }

        public static bool TryCallOpenAiCompatibleApi(
            string baseUrl,
            string model,
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            float temperature,
            out string assistantContent,
            out string error)
        {
            assistantContent = "";
            error = "";

            try
            {
                string url = CombineUrl(baseUrl, "/chat/completions");
                var requestBody = new OpenAiChatRequest
                {
                    model = model,
                    max_tokens = maxTokens,
                    temperature = temperature,
                    messages = new List<OpenAiChatMessage>
                    {
                        new OpenAiChatMessage { role = "system", content = systemPrompt },
                        new OpenAiChatMessage { role = "user", content = userPrompt }
                    }
                };

                string payload = JsonUtility.ToJson(requestBody, false);
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Timeout = 120000;

                byte[] bodyBytes = Encoding.UTF8.GetBytes(payload);
                request.ContentLength = bodyBytes.Length;
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string raw = reader.ReadToEnd();
                    var parsed = JsonUtility.FromJson<OpenAiChatResponse>(raw);
                    if (parsed == null || parsed.choices == null || parsed.choices.Count == 0 || parsed.choices[0].message == null)
                    {
                        error = "AI response did not contain a valid choices[0].message payload.";
                        return false;
                    }

                    assistantContent = ExtractAssistantContent(parsed.choices[0].message.content);
                    return true;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public static bool ApplyAiResponse(SceneObjectGraph graph, string rawJson, out string summary)
        {
            summary = "";
            if (graph == null)
            {
                summary = "No graph selected.";
                return false;
            }

            string json = ExtractJson(rawJson);
            if (string.IsNullOrWhiteSpace(json))
            {
                summary = "AI response did not contain JSON.";
                return false;
            }

            var patch = JsonUtility.FromJson<SceneObjectGraphAiResponseRoot>(json);
            int addedRelations = 0;
            int updatedObjects = 0;

            Undo.RegisterCompleteObjectUndo(graph, "Apply Scene Object Graph AI Response");

            if (patch != null && patch.relations != null && patch.relations.Count > 0)
            {
                foreach (var relation in patch.relations)
                {
                    if (ApplyRelation(graph, relation))
                        addedRelations++;
                }
            }
            else
            {
                var root = JsonUtility.FromJson<SceneObjectGraphExportRoot>(json);
                if (root != null && root.objects != null && root.objects.Count > 0)
                {
                    foreach (var obj in root.objects)
                    {
                        if (MergeExportObject(graph, obj))
                            updatedObjects++;
                    }
                }
            }

            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            summary = patch != null && patch.relations != null && patch.relations.Count > 0
                ? $"Applied {addedRelations} AI relation(s)."
                : $"Merged {updatedObjects} AI object update(s).";
            return true;
        }

        public static bool ImportGraphFromFile(SceneObjectGraph graph, string filePath, out string summary)
        {
            summary = "";
            if (graph == null)
            {
                summary = "No graph selected.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                summary = $"File not found: {filePath}";
                return false;
            }

            string rawJson = File.ReadAllText(filePath, new UTF8Encoding(false));
            return ApplyAiResponse(graph, rawJson, out summary);
        }

        public static string ResolveSceneHint(SceneObjectGraph graph)
        {
            string graphName = graph != null ? graph.name : "";
            string activeScene = SceneManager.GetActiveScene().name;
            return NormalizeHint(graphName, activeScene);
        }

        private static SceneObjectGraphCatalogExport BuildCatalogExport(InteractableCatalog catalog)
        {
            catalog = ResolveCatalog(catalog);
            var export = new SceneObjectGraphCatalogExport();
            if (catalog == null) return export;

            export.registeredObjectIds = catalog.GetRegisteredObjectIds().ToList();
            export.contextTags = catalog.GetContextTags().ToList();

            if (catalog.archetypes != null)
            {
                foreach (var arch in catalog.archetypes)
                {
                    if (arch == null || string.IsNullOrWhiteSpace(arch.archetypeId)) continue;
                    export.archetypes.Add(new SceneObjectGraphArchetypeExport
                    {
                        id = arch.archetypeId,
                        defaultName = arch.defaultName,
                        defaultDescription = arch.defaultDescription,
                        defaultState = arch.defaultState.ToString(),
                        defaultContextTags = arch.defaultContextTags != null ? arch.defaultContextTags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() : new List<string>(),
                        interactionHint = arch.interactionHint ?? ""
                    });
                }
            }

            if (catalog.items != null)
            {
                foreach (var item in catalog.items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId)) continue;
                    export.items.Add(new SceneObjectGraphItemExport
                    {
                        id = item.itemId,
                        displayName = item.displayName,
                        description = item.description,
                        defaultContextTags = item.defaultContextTags != null ? item.defaultContextTags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() : new List<string>(),
                        interactionHint = item.interactionHint ?? "",
                        category = item.category ?? ""
                    });
                }
            }

            return export;
        }

        private static SceneObjectGraphDoomsSceneExport BuildDoomsSceneExport(string sceneHint, SceneObjectGraph graph)
        {
            string hint = NormalizeHint(sceneHint, ResolveSceneHint(graph));
            string projectRoot = GetProjectRoot();
            string jsonPath = Path.Combine(projectRoot, DoomsScenesPath);
            if (!File.Exists(jsonPath)) return new SceneObjectGraphDoomsSceneExport();

            try
            {
                var root = JsonUtility.FromJson<DoomsSceneFileRoot>(File.ReadAllText(jsonPath));
                if (root == null || root.scenes == null || root.scenes.Count == 0)
                    return new SceneObjectGraphDoomsSceneExport();

                DoomsSceneFileEntry match = null;
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    match = root.scenes.FirstOrDefault(s => MatchesHint(s, hint));
                }

                if (match == null)
                    match = root.scenes.FirstOrDefault();

                if (match == null) return new SceneObjectGraphDoomsSceneExport();

                return new SceneObjectGraphDoomsSceneExport
                {
                    sceneId = match.sceneId,
                    displayName = match.displayName,
                    description = match.description,
                    narrativePhase = match.narrativePhase,
                    requiredFactions = match.requiredFactions != null ? match.requiredFactions.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>(),
                    optionalFactions = match.optionalFactions != null ? match.optionalFactions.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>(),
                    tags = match.tags != null ? match.tags.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>()
                };
            }
            catch
            {
                return new SceneObjectGraphDoomsSceneExport();
            }
        }

        private static SceneObjectGraphWorldContextExport BuildWorldContextExport()
        {
            var export = new SceneObjectGraphWorldContextExport();
            var worldContextType = ResolveType("MLA_SIM.WorldContextManager", "WorldContextManager");
            if (worldContextType == null) return export;

            var findMethod = typeof(UnityEngine.Object).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "FindAnyObjectByType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            if (findMethod == null) return export;

            var genericFind = findMethod.MakeGenericMethod(worldContextType);
            var wcm = genericFind.Invoke(null, null);
            if (wcm == null) return export;

            export.worldSetting = GetStringField(wcm, "worldSetting");
            export.timePeriod = GetStringField(wcm, "timePeriod");
            export.narrativeTone = GetStringField(wcm, "narrativeTone");

            var currentState = GetFieldValue(wcm, "currentState");
            export.weather = currentState != null ? GetStringField(currentState, "weather") : "";
            export.threatLevel = currentState != null ? GetStringField(currentState, "threatLevel") : "";
            export.timeOfDay = currentState != null ? GetStringField(currentState, "timeOfDay") : "";
            export.resourceAvailability = currentState != null ? GetFloatField(currentState, "resourceAvailability") : 0f;

            var worldRules = GetFieldValue(wcm, "worldRules") as IEnumerable<string>;
            export.worldRules = worldRules != null
                ? worldRules.Where(r => !string.IsNullOrWhiteSpace(r)).ToList()
                : new List<string>();

            var historicalEvents = GetFieldValue(wcm, "historicalEvents") as IEnumerable<string>;
            export.historicalEvents = historicalEvents != null
                ? historicalEvents.Where(e => !string.IsNullOrWhiteSpace(e)).ToList()
                : new List<string>();

            var keyLocations = GetFieldValue(wcm, "keyLocations") as System.Collections.IEnumerable;
            if (keyLocations != null)
            {
                export.keyLocations = new List<string>();
                foreach (var location in keyLocations)
                {
                    if (location == null) continue;
                    var name = GetStringField(location, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var safety = GetFloatField(location, "safetyLevel");
                    var description = GetStringField(location, "description");
                    export.keyLocations.Add($"{name} (safety={safety:F1}): {description}");
                }
            }
            return export;
        }

        private static System.Type ResolveType(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var type = System.Type.GetType($"{candidate}, Assembly-CSharp") ?? System.Type.GetType(candidate);
                if (type != null) return type;
            }

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var candidate in candidates)
                {
                    var type = asm.GetType(candidate);
                    if (type != null) return type;
                }
            }

            return null;
        }

        private static object GetFieldValue(object instance, string fieldName)
        {
            if (instance == null) return null;
            var field = instance.GetType().GetField(fieldName);
            return field?.GetValue(instance);
        }

        private static string GetStringField(object instance, string fieldName)
        {
            return GetFieldValue(instance, fieldName) as string ?? "";
        }

        private static float GetFloatField(object instance, string fieldName)
        {
            var value = GetFieldValue(instance, fieldName);
            return value is float f ? f : 0f;
        }

        private static List<SceneObjectGraphDoomsSceneExport> BuildAllDoomsSceneExports(SceneObjectGraph graph)
        {
            var result = new List<SceneObjectGraphDoomsSceneExport>();
            string projectRoot = GetProjectRoot();
            string jsonPath = Path.Combine(projectRoot, DoomsScenesPath);
            if (!File.Exists(jsonPath)) return result;

            try
            {
                var root = JsonUtility.FromJson<DoomsSceneFileRoot>(File.ReadAllText(jsonPath));
                if (root?.scenes == null) return result;

                foreach (var scene in root.scenes)
                {
                    if (scene == null || string.IsNullOrWhiteSpace(scene.sceneId)) continue;
                    result.Add(new SceneObjectGraphDoomsSceneExport
                    {
                        sceneId = scene.sceneId,
                        displayName = scene.displayName ?? "",
                        description = scene.description ?? "",
                        narrativePhase = scene.narrativePhase ?? "",
                        requiredFactions = scene.requiredFactions?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>(),
                        optionalFactions = scene.optionalFactions?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>(),
                        tags = scene.tags?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>()
                    });
                }
            }
            catch { }

            return result;
        }

        private static string BuildCompactPromptContext(SceneObjectGraphExportRoot root)
        {
            if (root == null) return "(no context)";

            var sb = new StringBuilder();
            sb.AppendLine($"scene: {root.activeSceneName}");
            if (!string.IsNullOrWhiteSpace(root.graphAsset))
                sb.AppendLine($"graph: {root.graphAsset}");

            if (root.doomsScene != null && !string.IsNullOrWhiteSpace(root.doomsScene.sceneId))
            {
                sb.AppendLine($"dooms_scene: {root.doomsScene.sceneId} | {root.doomsScene.displayName} | phase={root.doomsScene.narrativePhase}");
                if (!string.IsNullOrWhiteSpace(root.doomsScene.description))
                    sb.AppendLine($"dooms_desc: {root.doomsScene.description}");
                if (root.doomsScene.requiredFactions != null && root.doomsScene.requiredFactions.Count > 0)
                    sb.AppendLine($"dooms_required: {string.Join(", ", root.doomsScene.requiredFactions)}");
                if (root.doomsScene.optionalFactions != null && root.doomsScene.optionalFactions.Count > 0)
                    sb.AppendLine($"dooms_optional: {string.Join(", ", root.doomsScene.optionalFactions)}");
                if (root.doomsScene.tags != null && root.doomsScene.tags.Count > 0)
                    sb.AppendLine($"dooms_tags: {string.Join(", ", root.doomsScene.tags)}");
            }

            if (root.catalog != null)
            {
                if (root.catalog.registeredObjectIds != null && root.catalog.registeredObjectIds.Count > 0)
                    sb.AppendLine($"catalog_object_ids: {string.Join(", ", TakeFirst(root.catalog.registeredObjectIds, 30))}{MaybeTruncated(root.catalog.registeredObjectIds.Count, 30)}");
                if (root.catalog.contextTags != null && root.catalog.contextTags.Count > 0)
                    sb.AppendLine($"catalog_context_tags: {string.Join(", ", TakeFirst(root.catalog.contextTags, 20))}{MaybeTruncated(root.catalog.contextTags.Count, 20)}");

                if (root.catalog.archetypes != null && root.catalog.archetypes.Count > 0)
                {
                    sb.AppendLine("catalog_archetypes:");
                    foreach (var arch in TakeFirst(root.catalog.archetypes, 12))
                    {
                        if (arch == null) continue;
                        string tags = arch.defaultContextTags != null && arch.defaultContextTags.Count > 0
                            ? string.Join(", ", TakeFirst(arch.defaultContextTags, 6))
                            : "";
                        sb.AppendLine($"- {arch.id} | name={arch.defaultName} | state={arch.defaultState} | tags={tags} | hint={TrimLine(arch.interactionHint, 120)}");
                    }
                }

                if (root.catalog.items != null && root.catalog.items.Count > 0)
                {
                    sb.AppendLine("catalog_items:");
                    foreach (var item in TakeFirst(root.catalog.items, 12))
                    {
                        if (item == null) continue;
                        string tags = item.defaultContextTags != null && item.defaultContextTags.Count > 0
                            ? string.Join(", ", TakeFirst(item.defaultContextTags, 4))
                            : "";
                        sb.AppendLine($"- {item.id} | name={item.displayName} | category={item.category} | tags={tags} | hint={TrimLine(item.interactionHint, 120)}");
                    }
                }
            }

            if (root.objects != null && root.objects.Count > 0)
            {
                sb.AppendLine("scene_objects:");
                foreach (var obj in TakeFirst(root.objects, 24))
                {
                    if (obj == null) continue;
                    string tags = obj.contextTags != null && obj.contextTags.Count > 0
                        ? string.Join(", ", TakeFirst(obj.contextTags, 6))
                        : "";
                    string stateSummary = obj.states != null && obj.states.Count > 0
                        ? string.Join("; ", obj.states.Take(4).Select(SummarizeState))
                        : "(no states)";
                    sb.AppendLine($"- {obj.objectId} | display={obj.displayName} | arch={obj.archetypeId} | state={obj.initialStateId} | tags={tags} | pos={obj.sceneWorldPosition} | states={stateSummary}");
                }
                if (root.objects.Count > 24)
                    sb.AppendLine($"... {root.objects.Count - 24} more objects omitted");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildAnimationContext()
        {
            var sb = new StringBuilder();

            var sequenceRegistry = MLA_SIM.AnimationSequenceRegistry.Instance;
            if (sequenceRegistry != null && sequenceRegistry.sequences != null && sequenceRegistry.sequences.Count > 0)
            {
                sb.AppendLine("animation_sequences:");
                foreach (var seq in TakeFirst(sequenceRegistry.sequences, 24))
                {
                    if (seq == null || string.IsNullOrWhiteSpace(seq.sequenceId)) continue;
                    sb.AppendLine($"- {seq.sequenceId} | start={seq.startState} | loop={seq.loopState} | end={seq.endState}");
                }
            }

            var stateRegistry = MLA_SIM.Dooms.Registries.AnimationStateRegistrySO.Instance;
            if (stateRegistry != null && stateRegistry.states != null && stateRegistry.states.Count > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("animation_states:");
                sb.AppendLine(string.Join(", ", TakeFirst(stateRegistry.states, 64)));
            }

            return sb.ToString().TrimEnd();
        }

        private static string SummarizeState(SceneObjectGraphStateExport state)
        {
            if (state == null) return "";
            if (state.edges == null || state.edges.Count == 0)
                return $"{state.stateId}[]";

            var edgeBits = state.edges.Take(4).Select(e =>
            {
                if (e == null) return "";
                string cond = !string.IsNullOrWhiteSpace(e.conditionObjectName)
                    ? $" on {e.conditionObjectName}:{e.conditionStateId}"
                    : "";
                return $"{e.actionName}->{e.resultingStateId}{cond}";
            }).Where(s => !string.IsNullOrWhiteSpace(s));

            string suffix = state.edges.Count > 4 ? "; ..." : "";
            return $"{state.stateId}[{string.Join("; ", edgeBits)}{suffix}]";
        }

        private static string TrimLine(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= maxLength) return text;
            return text.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private static List<T> TakeFirst<T>(List<T> values, int count)
        {
            if (values == null) return new List<T>();
            return values.Take(Math.Max(0, count)).ToList();
        }

        private static string MaybeTruncated(int totalCount, int shownCount)
        {
            return totalCount > shownCount ? $" ...(+{totalCount - shownCount} more)" : "";
        }

        private static List<SceneObjectGraphObjectExport> BuildObjectExports(SceneObjectGraph graph)
        {
            var result = new List<SceneObjectGraphObjectExport>();
            if (graph == null) return result;

            foreach (var node in graph.allNodes)
            {
                if (node is not SceneObjectNode objNode) continue;
                result.Add(BuildObjectExport(objNode));
            }

            result.Sort((a, b) => string.Compare(a.objectId, b.objectId, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static SceneObjectGraphObjectExport BuildObjectExport(SceneObjectNode node)
        {
            var export = new SceneObjectGraphObjectExport
            {
                objectId = node.objectId ?? "",
                displayName = node.displayName ?? "",
                sourceObjectName = node.sourceObjectName ?? "",
                archetypeId = node.archetypeId ?? "",
                description = node.description ?? "",
                environmentHint = node.environmentHint ?? "",
                contextTags = node.contextTags != null ? node.contextTags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() : new List<string>(),
                sceneWorldPosition = node.sceneWorldPosition,
                sourceSceneName = node.sourceSceneName ?? "",
                initialStateId = node.initialStateId ?? ""
            };

            if (node.states != null)
            {
                foreach (var state in node.states)
                {
                    if (state == null || string.IsNullOrWhiteSpace(state.stateId)) continue;
                    var stateExport = new SceneObjectGraphStateExport { stateId = state.stateId };
                    if (state.edges != null)
                    {
                        foreach (var edge in state.edges)
                        {
                            if (edge == null || string.IsNullOrWhiteSpace(edge.actionName)) continue;
                            edge.EnsureId();
                            stateExport.edges.Add(BuildEdgeExport(edge));
                        }
                    }
                    export.states.Add(stateExport);
                }
            }

            return export;
        }

        private static SceneObjectGraphEdgeExport BuildEdgeExport(InteractionEdge edge)
        {
            return new SceneObjectGraphEdgeExport
            {
                id = edge.id ?? "",
                actionName = edge.actionName ?? "",
                allowedTierMask = (int)edge.allowedTiers,
                requiredItemIds = edge.requiredItemIds != null ? edge.requiredItemIds.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>(),
                yieldItemIds = edge.yieldItemIds != null ? edge.yieldItemIds.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>(),
                sequenceId = edge.sequenceId ?? "",
                animatorStateName = edge.animatorStateName ?? "",
                holdSeconds = edge.holdSeconds,
                resultingStateId = edge.resultingStateId ?? "",
                conditionObjectName = edge.conditionNode != null ? edge.conditionNode.objectId : edge.conditionObjectName ?? "",
                conditionStateId = edge.conditionStateId ?? "",
                hints = edge.hints ?? new RuleHints(),
                preconditions = edge.preconditions != null ? edge.preconditions.Select(p => p != null ? p.Describe() : "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>(),
                effects = edge.effects != null ? edge.effects.Select(e => e != null ? e.Describe() : "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>(),
                successMessage = edge.successMessage ?? "",
                failureMessage = edge.failureMessage ?? ""
            };
        }

        private static void MergeNodeMetadata(SceneObjectNode node, InteractableObject io, string activeSceneName, InteractableCatalog catalog)
        {
            node.displayName = string.IsNullOrWhiteSpace(io.objectName) ? io.gameObject.name : io.objectName;
            node.name = node.displayName;
            node.sourceObjectName = io.gameObject.name;
            node.archetypeId = string.IsNullOrWhiteSpace(io.archetypeId) ? ResolveCatalogArchetypeId(io, catalog) : io.archetypeId;
            node.description = io.description ?? "";
            node.environmentHint = io.environmentHint ?? "";
            node.contextTags = io.contextTags != null ? io.contextTags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList() : new List<string>();
            node.sceneWorldPosition = io.transform.position;
            node.sourceSceneName = activeSceneName;
            if (string.IsNullOrWhiteSpace(node.initialStateId))
                node.initialStateId = io.currentState.ToString();
            if (node.states == null)
                node.states = new List<InteractionStateNode>();
        }

        private static void MergeAffordanceStatesAndEdges(SceneObjectNode node, InteractableObject io, InteractableCatalog catalog)
        {
            if (io.actionAffordances == null || io.actionAffordances.Count == 0) return;

            foreach (var aff in io.actionAffordances)
            {
                if (aff == null || string.IsNullOrWhiteSpace(aff.actionName)) continue;

                string sourceStateId = DetermineSourceStateId(aff, io);
                string resultingStateId = aff.resultingState.ToString();
                var state = EnsureState(node, sourceStateId);
                var edge = FindOrCreateEdge(state, aff.actionName, "", "", resultingStateId);
                edge.actionName = aff.actionName;
                edge.allowedTiers = io.allowedTiers;
                edge.requiredItemIds = aff.requiredItems != null ? aff.requiredItems.Where(r => r != null && !string.IsNullOrWhiteSpace(r.itemId)).Select(r => r.itemId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
                edge.yieldItemIds = aff.yieldItems != null ? aff.yieldItems.Where(y => y != null && !string.IsNullOrWhiteSpace(y.itemId)).Select(y => y.itemId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
                edge.sequenceId = string.IsNullOrWhiteSpace(edge.sequenceId) ? io.sequenceId : edge.sequenceId;
                edge.animatorStateName = string.IsNullOrWhiteSpace(edge.animatorStateName) ? io.animatorStateName : edge.animatorStateName;
                edge.holdSeconds = aff.estimatedDuration > 0f ? aff.estimatedDuration : (io.holdSeconds > 0f ? io.holdSeconds : 2f);
                edge.resultingStateId = resultingStateId;
                edge.conditionObjectName = string.IsNullOrWhiteSpace(edge.conditionObjectName) ? "" : edge.conditionObjectName;
                edge.conditionStateId = string.IsNullOrWhiteSpace(edge.conditionStateId) ? "" : edge.conditionStateId;
                edge.hints = BuildHints(node.displayName, aff, resultingStateId);
                edge.successMessage = string.IsNullOrWhiteSpace(edge.successMessage) ? "" : edge.successMessage;
                edge.failureMessage = string.IsNullOrWhiteSpace(edge.failureMessage) ? "" : edge.failureMessage;
                edge.EnsureId();
            }
        }

        private static void MergeLegacyGraph(SceneObjectNode node, InteractionGraph legacyGraph)
        {
            if (legacyGraph == null || legacyGraph.nodes == null) return;

            if (!string.IsNullOrWhiteSpace(legacyGraph.initialStateId) && string.IsNullOrWhiteSpace(node.initialStateId))
                node.initialStateId = legacyGraph.initialStateId;

            foreach (var legacyState in legacyGraph.nodes)
            {
                if (legacyState == null || string.IsNullOrWhiteSpace(legacyState.stateId)) continue;
                var state = EnsureState(node, legacyState.stateId);
                if (legacyState.edges == null) continue;

                foreach (var legacyEdge in legacyState.edges)
                {
                    if (legacyEdge == null || string.IsNullOrWhiteSpace(legacyEdge.actionName)) continue;
                    var edge = FindOrCreateEdge(state, legacyEdge.actionName, legacyEdge.conditionObjectName ?? "", legacyEdge.conditionStateId ?? "", legacyEdge.resultingStateId ?? "");
                    CopyEdgeData(edge, legacyEdge);
                    edge.EnsureId();
                }
            }
        }

        private static bool MergeExportObject(SceneObjectGraph graph, SceneObjectGraphObjectExport obj)
        {
            if (graph == null || obj == null || string.IsNullOrWhiteSpace(obj.objectId)) return false;
            var node = graph.FindObjectNode(obj.objectId);
            if (node == null)
            {
                node = graph.AddNode<SceneObjectNode>();
                node.objectId = obj.objectId;
                node.position = new Vector2(CountSceneObjectNodes(graph) * 280f, 0f);
            }

            node.displayName = string.IsNullOrWhiteSpace(obj.displayName) ? node.displayName : obj.displayName;
            node.sourceObjectName = string.IsNullOrWhiteSpace(obj.sourceObjectName) ? node.sourceObjectName : obj.sourceObjectName;
            node.archetypeId = string.IsNullOrWhiteSpace(obj.archetypeId) ? node.archetypeId : obj.archetypeId;
            node.description = string.IsNullOrWhiteSpace(obj.description) ? node.description : obj.description;
            node.environmentHint = string.IsNullOrWhiteSpace(obj.environmentHint) ? node.environmentHint : obj.environmentHint;
            node.contextTags = obj.contextTags != null && obj.contextTags.Count > 0 ? obj.contextTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : node.contextTags;
            node.sceneWorldPosition = obj.sceneWorldPosition;
            node.sourceSceneName = string.IsNullOrWhiteSpace(obj.sourceSceneName) ? node.sourceSceneName : obj.sourceSceneName;
            node.initialStateId = string.IsNullOrWhiteSpace(obj.initialStateId) ? node.initialStateId : obj.initialStateId;
            if (node.states == null) node.states = new List<InteractionStateNode>();

            if (obj.states != null)
            {
                foreach (var stateData in obj.states)
                {
                    if (stateData == null || string.IsNullOrWhiteSpace(stateData.stateId)) continue;
                    var state = EnsureState(node, stateData.stateId);
                    if (stateData.edges == null) continue;
                    foreach (var edgeData in stateData.edges)
                    {
                        if (edgeData == null || string.IsNullOrWhiteSpace(edgeData.actionName)) continue;
                        var edge = FindOrCreateEdge(state, edgeData.actionName, edgeData.conditionObjectName ?? "", edgeData.conditionStateId ?? "", edgeData.resultingStateId ?? "");
                        CopyEdgeData(edge, edgeData);
                        edge.EnsureId();
                    }
                }
            }

            return true;
        }

        private static bool ApplyRelation(SceneObjectGraph graph, SceneObjectGraphRelationSpec relation)
        {
            if (graph == null || relation == null) return false;
            if (string.IsNullOrWhiteSpace(relation.targetObjectId)) return false;

            var targetNode = graph.FindObjectNode(relation.targetObjectId);
            if (targetNode == null)
            {
                return false;
            }

            string stateId = !string.IsNullOrWhiteSpace(relation.targetStateId) ? relation.targetStateId : targetNode.initialStateId;
            var state = EnsureState(targetNode, stateId);
            string conditionObject = !string.IsNullOrWhiteSpace(relation.conditionObjectName) ? relation.conditionObjectName : relation.sourceObjectId;
            string conditionState = !string.IsNullOrWhiteSpace(relation.conditionStateId) ? relation.conditionStateId : relation.sourceStateId;
            string resultingState = relation.resultingStateId ?? "";
            var edge = FindOrCreateEdge(state, relation.actionName, conditionObject ?? "", conditionState ?? "", resultingState);
            edge.actionName = relation.actionName ?? edge.actionName;
            edge.allowedTiers = (DoomsTier)relation.allowedTierMask;
            edge.requiredItemIds = relation.requiredItemIds != null ? relation.requiredItemIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
            edge.yieldItemIds = relation.yieldItemIds != null ? relation.yieldItemIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
            edge.sequenceId = relation.sequenceId ?? edge.sequenceId;
            edge.animatorStateName = relation.animatorStateName ?? edge.animatorStateName;
            edge.holdSeconds = relation.holdSeconds > 0f ? relation.holdSeconds : edge.holdSeconds;
            edge.resultingStateId = string.IsNullOrWhiteSpace(resultingState) ? edge.resultingStateId : resultingState;
            edge.conditionObjectName = conditionObject ?? "";
            edge.conditionStateId = conditionState ?? "";
            edge.hints = relation.hints ?? edge.hints ?? new RuleHints();
            edge.successMessage = relation.successMessage ?? edge.successMessage;
            edge.failureMessage = relation.failureMessage ?? edge.failureMessage;
            edge.EnsureId();

            if (!string.IsNullOrWhiteSpace(relation.sourceObjectId))
            {
                var sourceNode = graph.FindObjectNode(relation.sourceObjectId);
                if (sourceNode != null && sourceNode != targetNode)
                {
                    EnsureDependencyConnection(
                        sourceNode,
                        targetNode,
                        !string.IsNullOrWhiteSpace(relation.conditionStateId) ? relation.conditionStateId : relation.sourceStateId,
                        !string.IsNullOrWhiteSpace(relation.gatedActionName) ? relation.gatedActionName : relation.actionName,
                        !string.IsNullOrWhiteSpace(relation.gatedFromStateId) ? relation.gatedFromStateId : (!string.IsNullOrWhiteSpace(relation.targetStateId) ? relation.targetStateId : stateId));
                }
            }
            return true;
        }

        private static void EnsureDependencyConnection(SceneObjectNode sourceNode, SceneObjectNode targetNode, string requiredSourceStateId, string gatedActionName, string gatedFromStateId)
        {
            if (sourceNode == null || targetNode == null) return;

            foreach (var existing in targetNode.inConnections)
            {
                if (existing is ObjectDependencyConnection dependency && existing.sourceNode == sourceNode)
                {
                    if (!string.IsNullOrWhiteSpace(requiredSourceStateId))
                        dependency.requiredSourceStateId = requiredSourceStateId;
                    if (!string.IsNullOrWhiteSpace(gatedActionName))
                        dependency.gatedActionName = gatedActionName;
                    if (!string.IsNullOrWhiteSpace(gatedFromStateId))
                        dependency.gatedFromStateId = gatedFromStateId;
                    EditorUtility.SetDirty(sourceNode.graph);
                    return;
                }
            }

            var created = NodeCanvas.Framework.Connection.Create(sourceNode, targetNode) as ObjectDependencyConnection;
            if (created != null)
            {
                created.requiredSourceStateId = requiredSourceStateId ?? "";
                created.gatedActionName = gatedActionName ?? "";
                created.gatedFromStateId = gatedFromStateId ?? "";
                EditorUtility.SetDirty(sourceNode.graph);
            }
        }

        private static void CopyEdgeData(InteractionEdge edge, SceneObjectGraphEdgeExport data)
        {
            if (edge == null || data == null) return;
            edge.id = string.IsNullOrWhiteSpace(data.id) ? edge.id : data.id;
            edge.actionName = string.IsNullOrWhiteSpace(data.actionName) ? edge.actionName : data.actionName;
            edge.allowedTiers = (DoomsTier)data.allowedTierMask;
            edge.requiredItemIds = data.requiredItemIds != null ? data.requiredItemIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : edge.requiredItemIds;
            edge.yieldItemIds = data.yieldItemIds != null ? data.yieldItemIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : edge.yieldItemIds;
            edge.sequenceId = string.IsNullOrWhiteSpace(data.sequenceId) ? edge.sequenceId : data.sequenceId;
            edge.animatorStateName = string.IsNullOrWhiteSpace(data.animatorStateName) ? edge.animatorStateName : data.animatorStateName;
            edge.holdSeconds = data.holdSeconds > 0f ? data.holdSeconds : edge.holdSeconds;
            edge.resultingStateId = string.IsNullOrWhiteSpace(data.resultingStateId) ? edge.resultingStateId : data.resultingStateId;
            edge.conditionObjectName = string.IsNullOrWhiteSpace(data.conditionObjectName) ? edge.conditionObjectName : data.conditionObjectName;
            edge.conditionStateId = string.IsNullOrWhiteSpace(data.conditionStateId) ? edge.conditionStateId : data.conditionStateId;
            edge.hints = data.hints ?? edge.hints ?? new RuleHints();
            edge.successMessage = string.IsNullOrWhiteSpace(data.successMessage) ? edge.successMessage : data.successMessage;
            edge.failureMessage = string.IsNullOrWhiteSpace(data.failureMessage) ? edge.failureMessage : data.failureMessage;
        }

        private static void CopyEdgeData(InteractionEdge edge, InteractionEdge data)
        {
            if (edge == null || data == null) return;
            edge.id = string.IsNullOrWhiteSpace(data.id) ? edge.id : data.id;
            edge.actionName = string.IsNullOrWhiteSpace(data.actionName) ? edge.actionName : data.actionName;
            edge.allowedTiers = data.allowedTiers;
            edge.requiredItemIds = data.requiredItemIds != null && data.requiredItemIds.Length > 0 ? data.requiredItemIds : edge.requiredItemIds;
            edge.yieldItemIds = data.yieldItemIds != null && data.yieldItemIds.Length > 0 ? data.yieldItemIds : edge.yieldItemIds;
            edge.sequenceId = string.IsNullOrWhiteSpace(data.sequenceId) ? edge.sequenceId : data.sequenceId;
            edge.animatorStateName = string.IsNullOrWhiteSpace(data.animatorStateName) ? edge.animatorStateName : data.animatorStateName;
            edge.holdSeconds = data.holdSeconds > 0f ? data.holdSeconds : edge.holdSeconds;
            edge.resultingStateId = string.IsNullOrWhiteSpace(data.resultingStateId) ? edge.resultingStateId : data.resultingStateId;
            edge.conditionObjectName = string.IsNullOrWhiteSpace(data.conditionObjectName) ? edge.conditionObjectName : data.conditionObjectName;
            edge.conditionStateId = string.IsNullOrWhiteSpace(data.conditionStateId) ? edge.conditionStateId : data.conditionStateId;
            edge.hints = data.hints ?? edge.hints ?? new RuleHints();
            edge.successMessage = string.IsNullOrWhiteSpace(data.successMessage) ? edge.successMessage : data.successMessage;
            edge.failureMessage = string.IsNullOrWhiteSpace(data.failureMessage) ? edge.failureMessage : data.failureMessage;
        }

        private static RuleHints BuildHints(string objectLabel, ActionAffordance aff, string resultingStateId)
        {
            var hints = new RuleHints();
            string label = string.IsNullOrWhiteSpace(objectLabel) ? "object" : objectLabel;
            hints.actionLabel = string.IsNullOrWhiteSpace(aff.actionName) ? label : $"{aff.actionName} {label}";
            hints.preconditionHint = BuildPreconditionText(aff);
            hints.worldImpactHint = string.IsNullOrWhiteSpace(resultingStateId) ? "" : $"Transitions to {resultingStateId}.";
            hints.successMessage = "";
            hints.failureTemplate = "{reason}";
            return hints;
        }

        private static string BuildPreconditionText(ActionAffordance aff)
        {
            var parts = new List<string>();
            if (aff.requiredItems != null && aff.requiredItems.Length > 0)
            {
                parts.Add("Requires " + string.Join(", ", aff.requiredItems.Where(r => r != null && !string.IsNullOrWhiteSpace(r.itemId)).Select(r => r.itemId)));
            }
            if (aff.requiredStates != null && aff.requiredStates.Length > 0)
            {
                parts.Add("States: " + string.Join(", ", aff.requiredStates.Select(s => s.ToString())));
            }
            return string.Join(" | ", parts);
        }

        private static InteractionEdge FindOrCreateEdge(InteractionStateNode state, string actionName, string conditionObjectName, string conditionStateId, string resultingStateId)
        {
            if (state.edges == null) state.edges = new List<InteractionEdge>();
            var existing = state.edges.FirstOrDefault(e =>
                e != null &&
                string.Equals(e.actionName, actionName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.conditionObjectName ?? "", conditionObjectName ?? "", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.conditionStateId ?? "", conditionStateId ?? "", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.resultingStateId ?? "", resultingStateId ?? "", StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var created = new InteractionEdge
            {
                id = Guid.NewGuid().ToString("N"),
                actionName = actionName ?? "InteractWith",
                conditionObjectName = conditionObjectName ?? "",
                conditionStateId = conditionStateId ?? "",
                resultingStateId = resultingStateId ?? ""
            };
            state.edges.Add(created);
            return created;
        }

        private static InteractionStateNode EnsureState(SceneObjectNode node, string stateId)
        {
            if (node.states == null) node.states = new List<InteractionStateNode>();
            var state = node.states.FirstOrDefault(s => s != null && string.Equals(s.stateId, stateId, StringComparison.OrdinalIgnoreCase));
            if (state != null) return state;

            state = new InteractionStateNode { stateId = string.IsNullOrWhiteSpace(stateId) ? "Usable" : stateId };
            node.states.Add(state);
            return state;
        }

        private static int CountSceneObjectNodes(SceneObjectGraph graph)
        {
            if (graph == null || graph.allNodes == null) return 0;
            return graph.allNodes.Count(n => n is SceneObjectNode);
        }

        private static int CountStates(SceneObjectNode node)
        {
            return node?.states?.Count ?? 0;
        }

        private static int CountEdges(SceneObjectNode node)
        {
            if (node?.states == null) return 0;
            int total = 0;
            foreach (var state in node.states)
                total += state?.edges?.Count ?? 0;
            return total;
        }

        private static string ResolveObjectId(InteractableObject io)
        {
            if (io == null) return "";
            if (!string.IsNullOrWhiteSpace(io.objectName)) return io.objectName.Trim();
            return io.gameObject != null ? io.gameObject.name.Trim() : "";
        }

        private static string ResolveCatalogArchetypeId(InteractableObject io, InteractableCatalog catalog)
        {
            if (io == null) return "";
            catalog = ResolveCatalog(catalog);
            if (catalog == null) return string.IsNullOrWhiteSpace(io.objectName) ? io.gameObject.name : io.objectName;

            string target = !string.IsNullOrWhiteSpace(io.archetypeId) ? io.archetypeId : ResolveObjectId(io);
            var arch = catalog.GetArchetype(target);
            if (arch != null && !string.IsNullOrWhiteSpace(arch.archetypeId)) return arch.archetypeId;
            return target;
        }

        private static string DetermineSourceStateId(ActionAffordance aff, InteractableObject io)
        {
            if (aff != null && aff.requiredStates != null && aff.requiredStates.Length > 0)
                return aff.requiredStates[0].ToString();
            return io != null ? io.currentState.ToString() : "Usable";
        }

        private static string NormalizeHint(params string[] values)
        {
            var joined = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
            return NormalizeKey(joined);
        }

        private static bool MatchesHint(DoomsSceneFileEntry entry, string hint)
        {
            if (entry == null || string.IsNullOrWhiteSpace(hint)) return false;
            string sceneId = NormalizeKey(entry.sceneId);
            string display = NormalizeKey(entry.displayName);
            string normalizedHint = NormalizeKey(hint);
            return sceneId == normalizedHint || display == normalizedHint || sceneId.Contains(normalizedHint) || display.Contains(normalizedHint) || normalizedHint.Contains(sceneId) || normalizedHint.Contains(display);
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ExtractAssistantContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            string trimmed = content.Trim();
            if (trimmed.StartsWith("```"))
            {
                int firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    trimmed = trimmed.Substring(firstNewline + 1);
                    int fence = trimmed.LastIndexOf("```");
                    if (fence >= 0) trimmed = trimmed.Substring(0, fence);
                }
            }
            return trimmed.Trim();
        }

        private static string ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = ExtractAssistantContent(raw);
            int firstBrace = text.IndexOf('{');
            int firstBracket = text.IndexOf('[');
            int start = -1;
            if (firstBrace >= 0 && firstBracket >= 0) start = Math.Min(firstBrace, firstBracket);
            else start = Math.Max(firstBrace, firstBracket);
            if (start < 0) return text;

            int lastBrace = text.LastIndexOf('}');
            int lastBracket = text.LastIndexOf(']');
            int end = Math.Max(lastBrace, lastBracket);
            if (end < start) return text.Substring(start);
            return text.Substring(start, end - start + 1);
        }

        private static string CombineUrl(string baseUrl, string suffix)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return suffix;
            string left = baseUrl.Trim().TrimEnd('/');
            string right = suffix.TrimStart('/');
            return $"{left}/{right}";
        }

        private static string ResolveProjectPath(string relativePath)
        {
            string projectRoot = GetProjectRoot();
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        }
    }
}
#endif
