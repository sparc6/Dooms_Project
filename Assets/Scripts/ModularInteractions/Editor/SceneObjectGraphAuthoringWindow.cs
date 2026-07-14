#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using MLA_SIM;
using MLA_SIM.ModularInteractions;

namespace MLA_SIM.EditorTools
{
    [CustomEditor(typeof(SceneObjectGraph))]
    public class SceneObjectGraphAuthoringInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Scene Object Graph Authoring", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use the tools window to populate the graph from the open scene, export JSON, and optionally run AI autofill.",
                MessageType.Info);

            if (GUILayout.Button("Open SceneObjectGraph Tools", GUILayout.Height(28)))
            {
                SceneObjectGraphAuthoringWindow.Open();
            }

            var graph = target as SceneObjectGraph;
            if (graph != null)
            {
                EditorGUILayout.Space(6);
                if (GUILayout.Button("Populate From Open Scene"))
                {
                    var report = SceneObjectGraphAuthoringUtility.PopulateGraphFromScene(
                        graph,
                        SceneObjectGraphAuthoringUtility.ResolveCatalog(),
                        syncCatalog: true,
                        includeLegacyInteractionGraph: true);

                    Debug.Log($"[SceneObjectGraph] Populate complete: created={report.createdNodes}, matched={report.matchedNodes}, addedStates={report.addedStates}, addedEdges={report.addedEdges}, catalogIds={report.syncedCatalogIds}");
                }

                if (GUILayout.Button("Sync Catalog IDs From Graph"))
                {
                    int count = SceneObjectGraphAuthoringUtility.SyncCatalogFromGraph(graph);
                    Debug.Log($"[SceneObjectGraph] Synced {count} catalog object ids from '{graph.name}'.");
                }

                if (GUILayout.Button("Export JSON"))
                {
                    var export = SceneObjectGraphAuthoringUtility.ExportGraphToJson(
                        graph,
                        SceneObjectGraphAuthoringUtility.ResolveCatalog(),
                        includeCatalogContext: true,
                        includeDoomsContext: true,
                        sceneHint: SceneObjectGraphAuthoringUtility.ResolveSceneHint(graph));
                    Debug.Log($"[SceneObjectGraph] Exported {export.objectCount} objects to {export.path}");
                }
            }
        }
    }

    public class SceneObjectGraphAuthoringWindow : EditorWindow
    {
        private SceneObjectGraph graph;
        private InteractableCatalog catalog;
        private bool includeCatalogContext = true;
        private bool includeDoomsContext = true;
        private bool syncCatalogOnPopulate = true;
        private bool includeLegacyInteractionGraph = true;
        private bool autoApplyAiResponse = true;
        private bool syncCatalogOnValidate = true;
        private string sceneHint = "";
        private string exportPath = "configs/scenarios/dooms/dooms_scene_objects.json";
        private string aiBaseUrl = "http://127.0.0.1:8001/v1";
        private string aiModel = "gemma-4-26b-a4b-it";
        private int aiMaxTokens = 3072;
        private float aiTemperature = 0.2f;
        private Vector2 scroll;
        private string status = "Select a SceneObjectGraph to begin.";
        private string promptPreview = "";
        private string lastAiResponse = "";
        private string lastAiError = "";
        private bool showAiPanel = true;
        private bool showExportPanel = true;
        private bool showPopulatePanel = true;
        private bool showValidationPanel = true;

        [MenuItem("MLA SIM/Scene Object Graph Tools")]
        public static void Open()
        {
            GetWindow<SceneObjectGraphAuthoringWindow>("Scene Object Graph Tools");
        }

        private void OnEnable()
        {
            SyncSelection();
            if (string.IsNullOrWhiteSpace(exportPath))
                exportPath = "configs/scenarios/dooms/dooms_scene_objects.json";
        }

        private void OnSelectionChange()
        {
            SyncSelection();
            Repaint();
        }

        private void SyncSelection()
        {
            if (Selection.activeObject is SceneObjectGraph selectedGraph)
            {
                graph = selectedGraph;
                sceneHint = SceneObjectGraphAuthoringUtility.ResolveSceneHint(graph);
            }

            if (Selection.activeObject is InteractableCatalog selectedCatalog)
            {
                catalog = selectedCatalog;
            }

            if (catalog == null)
                catalog = SceneObjectGraphAuthoringUtility.ResolveCatalog();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Scene Object Graph Authoring", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            graph = (SceneObjectGraph)EditorGUILayout.ObjectField("SceneObjectGraph", graph, typeof(SceneObjectGraph), false);
            catalog = (InteractableCatalog)EditorGUILayout.ObjectField("InteractableCatalog", catalog, typeof(InteractableCatalog), false);
            sceneHint = EditorGUILayout.TextField("DOOMS Scene Hint", sceneHint);
            exportPath = EditorGUILayout.TextField("Export Path", exportPath);

            EditorGUILayout.Space(4);
            includeCatalogContext = EditorGUILayout.ToggleLeft("Include InteractableCatalog context in export / AI prompt", includeCatalogContext);
            includeDoomsContext = EditorGUILayout.ToggleLeft("Include DOOMS scene context in export / AI prompt", includeDoomsContext);
            syncCatalogOnPopulate = EditorGUILayout.ToggleLeft("Sync catalog object ids after populate", syncCatalogOnPopulate);
            includeLegacyInteractionGraph = EditorGUILayout.ToggleLeft("Migrate legacy per-object InteractionGraph data", includeLegacyInteractionGraph);
            autoApplyAiResponse = EditorGUILayout.ToggleLeft("Auto-apply AI response", autoApplyAiResponse);
            syncCatalogOnValidate = EditorGUILayout.ToggleLeft("Sync catalog object ids before validation", syncCatalogOnValidate);

            EditorGUILayout.Space(8);
            DrawPopulateSection();
            DrawExportSection();
            DrawValidationSection();
            DrawAiSection();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(status, MessageType.None);

            if (!string.IsNullOrWhiteSpace(lastAiError))
                EditorGUILayout.HelpBox(lastAiError, MessageType.Error);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Prompt Preview", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(promptPreview, GUILayout.MinHeight(160));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Last AI Response", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(lastAiResponse, GUILayout.MinHeight(160));

            EditorGUILayout.EndScrollView();
        }

        private void DrawPopulateSection()
        {
            showPopulatePanel = EditorGUILayout.Foldout(showPopulatePanel, "Populate Graph From Scene", true);
            if (!showPopulatePanel) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Creates or updates SceneObjectNode entries from the open scene's InteractableObject components, then optionally syncs catalog object ids.",
                MessageType.Info);

            EditorGUI.BeginDisabledGroup(graph == null);
            if (GUILayout.Button("Populate From Open Scene", GUILayout.Height(30)))
            {
                var report = SceneObjectGraphAuthoringUtility.PopulateGraphFromScene(
                    graph,
                    catalog,
                    syncCatalogOnPopulate,
                    includeLegacyInteractionGraph);

                status = $"Populate complete: scanned={report.scannedObjects}, created={report.createdNodes}, matched={report.matchedNodes}, addedStates={report.addedStates}, addedEdges={report.addedEdges}, catalogIds={report.syncedCatalogIds}.";
                if (report.warnings.Count > 0)
                    status += $" Warnings: {string.Join(" | ", report.warnings)}";
                promptPreview = "";
                lastAiResponse = "";
                lastAiError = "";
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;
        }

        private void DrawValidationSection()
        {
            showValidationPanel = EditorGUILayout.Foldout(showValidationPanel, "Validate & Sync Graph", true);
            if (!showValidationPanel) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Checks graph ids, states, actions, items, animation references, and typed dependency gates. Can also sync catalog object ids first.",
                MessageType.Info);

            EditorGUI.BeginDisabledGroup(graph == null);
            if (GUILayout.Button("Validate & Sync Graph", GUILayout.Height(32)))
            {
                var report = SceneObjectGraphAuthoringUtility.ValidateGraph(
                    graph,
                    catalog,
                    syncCatalogOnValidate);

                status = report.Summary;
                if (report.issues.Count > 0)
                {
                    status += " " + string.Join(" | ", report.issues.Select(i => $"[{i.scope}] {i.message} {i.suggestion}"));
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;
        }

        private void DrawExportSection()
        {
            showExportPanel = EditorGUILayout.Foldout(showExportPanel, "Export Graph JSON", true);
            if (!showExportPanel) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Exports the current graph to a stable JSON file that can be used for AI prompting or external tooling.",
                MessageType.Info);

            EditorGUI.BeginDisabledGroup(graph == null);
            if (GUILayout.Button("Export JSON", GUILayout.Height(30)))
            {
                var export = SceneObjectGraphAuthoringUtility.ExportGraphToJson(
                    graph,
                    catalog,
                    includeCatalogContext,
                    includeDoomsContext,
                    exportPath,
                    sceneHint);

                status = $"Exported {export.objectCount} object(s) to {export.path}.";
                promptPreview = export.json;
                lastAiResponse = "";
                lastAiError = "";
            }

            if (GUILayout.Button("Export Full AI Context (IOs + Items + Anim + Narrative)", GUILayout.Height(34)))
            {
                var full = SceneObjectGraphAuthoringUtility.ExportFullContext(
                    graph,
                    catalog,
                    exportPath,
                    sceneHint);

                status = full.objectCount > 0
                    ? $"Full context exported: {full.objectCount} object(s) → {full.path}"
                    : $"Export failed or graph is empty.";
                promptPreview = full.json;
                lastAiResponse = "";
                lastAiError = "";
            }

            if (GUILayout.Button("Import JSON from File", GUILayout.Height(30)))
            {
                string path = EditorUtility.OpenFilePanel("Import Scene Object Graph JSON", "", "json");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (SceneObjectGraphAuthoringUtility.ImportGraphFromFile(graph, path, out string importSummary))
                    {
                        status = importSummary;
                        lastAiError = "";
                    }
                    else
                    {
                        status = "Import failed.";
                        lastAiError = importSummary;
                    }
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;
        }

        private void DrawAiSection()
        {
            showAiPanel = EditorGUILayout.Foldout(showAiPanel, "AI Autofill", true);
            if (!showAiPanel) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Builds a compact prompt from the graph, catalog, scene context, and optional DOOMS scene context, then sends it to an OpenAI-compatible endpoint.",
                MessageType.Info);

            aiBaseUrl = EditorGUILayout.TextField("AI Base URL", aiBaseUrl);
            aiModel = EditorGUILayout.TextField("AI Model", aiModel);
            aiMaxTokens = EditorGUILayout.IntField("Max Tokens", aiMaxTokens);
            aiTemperature = EditorGUILayout.Slider("Temperature", aiTemperature, 0f, 1f);

            EditorGUI.BeginDisabledGroup(graph == null);
            if (GUILayout.Button("Build Prompt Preview", GUILayout.Height(28)))
            {
                var report = SceneObjectGraphAuthoringUtility.BuildAiPrompt(
                    graph,
                    catalog,
                    includeCatalogContext,
                    includeDoomsContext,
                    sceneHint,
                    out _);

                promptPreview = report.prompt;
                lastAiResponse = report.jsonPreview;
                lastAiError = "";
                status = "Prompt preview built.";
            }

            if (GUILayout.Button("Run AI Autofill", GUILayout.Height(32)))
            {
                RunAiAutofill();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;
        }

        private void RunAiAutofill()
        {
            if (graph == null)
            {
                status = "Select a SceneObjectGraph first.";
                return;
            }

            var prompt = SceneObjectGraphAuthoringUtility.BuildAiPrompt(
                graph,
                catalog,
                includeCatalogContext,
                includeDoomsContext,
                sceneHint,
                out _);

            promptPreview = prompt.prompt;
            lastAiError = "";
            lastAiResponse = "";
            status = "Calling AI endpoint...";
            EditorUtility.DisplayProgressBar("Scene Object Graph AI", "Sending prompt to AI endpoint...", 0.4f);

            try
            {
                bool ok = SceneObjectGraphAuthoringUtility.TryCallOpenAiCompatibleApi(
                    aiBaseUrl,
                    aiModel,
                    "You are a DOOMS scene-object graph assistant. Return raw JSON only.",
                    prompt.prompt,
                    aiMaxTokens,
                    aiTemperature,
                    out string assistantContent,
                    out string error);

                if (!ok)
                {
                    lastAiError = error;
                    status = "AI request failed.";
                    return;
                }

                lastAiResponse = assistantContent;
                status = "AI response received.";

                if (autoApplyAiResponse)
                {
                    if (SceneObjectGraphAuthoringUtility.ApplyAiResponse(graph, assistantContent, out string summary))
                    {
                        status = summary;
                    }
                    else
                    {
                        status = "AI response received but could not be applied.";
                        lastAiError = summary;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
