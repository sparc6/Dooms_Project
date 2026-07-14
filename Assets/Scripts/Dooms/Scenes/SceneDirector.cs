using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using MLA_SIM.Dooms.Registries;
using MLA_SIM.Dooms.Scenes.Nodes;
using NodeCanvas.Framework;

namespace MLA_SIM.Dooms.Scenes
{
    [AddComponentMenu("DOOMS/Scene Director")]
    public class SceneDirector : MonoBehaviour
    {
        private static SceneDirector _instance;
        public static SceneDirector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SceneDirector>();
                    if (_instance == null)
                    {
                        var go = new GameObject("DOOMS_SceneDirector");
                        _instance = go.AddComponent<SceneDirector>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        public SceneRuntimeContext CurrentContext { get; private set; }

        public event Action<string, string> OnPhaseTransition;
        public event Action<string> OnSceneEnded;

        [Header("Telemetry")]
        [Tooltip("If true, SceneDirector POSTs scene_event payloads to /api/dooms/scene_event for the debug client.")]
        public bool emitTelemetry = true;

        [Tooltip("Override for the backend base URL. Leave empty to reuse DoomsConfigPusher's resolved URL.")]
        public string backendBaseUrlOverride = "";

        private Coroutine _activeSceneCoroutine;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void ActivateScene(string sceneId, float intensity, Dictionary<string, string> overrides = null)
        {
            if (string.IsNullOrEmpty(sceneId))
            {
                Debug.LogWarning("[DOOMS][SceneDirector] Cannot activate scene with empty sceneId.");
                return;
            }

            // Deactivate any running scene first
            if (_activeSceneCoroutine != null)
            {
                DeactivateScene("New scene requested: " + sceneId);
            }

            // Find SceneSO in registry
            SceneSO sceneAsset = null;
            var registry = SceneRegistrySO.Instance;
            if (registry != null && registry.scenes != null)
            {
                sceneAsset = registry.scenes.Find(s => s != null && s.sceneId == sceneId);
            }

            if (sceneAsset == null)
            {
                Debug.LogError($"[DOOMS][SceneDirector] Could not find scene definition for '{sceneId}' in the SceneRegistry.");
                return;
            }

            if (!ValidateRequiredFactions(sceneAsset))
            {
                // ValidateRequiredFactions logs the precise reason.
                return;
            }

            Debug.Log($"[DOOMS][SceneDirector] Activating Scene: '{sceneAsset.displayName}' (ID: {sceneId}, Intensity: {intensity})");
            EmitSceneEvent("SCENE_ACTIVATED", sceneId, "", new Dictionary<string, object> { { "intensity", intensity } });
            AreaAnchorIndex.RefreshAll(sceneId);
            _activeSceneCoroutine = StartCoroutine(RunScene(sceneAsset, intensity));
        }

        private bool ValidateRequiredFactions(SceneSO scene)
        {
            if (scene == null) return false;
            if (scene.requiredFactions == null || scene.requiredFactions.Count == 0) return true;

            var allTags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in allTags)
            {
                if (t == null || !string.IsNullOrEmpty(t.reservedBySceneId)) continue;
                if (string.IsNullOrEmpty(t.factionId)) continue;
                counts.TryGetValue(t.factionId, out var c);
                counts[t.factionId] = c + 1;
            }

            int need = Mathf.Max(1, scene.minAgentsPerFaction);
            foreach (var fid in scene.requiredFactions)
            {
                counts.TryGetValue(fid, out var have);
                if (have < need)
                {
                    Debug.LogWarning($"[DOOMS][SceneDirector] Cannot activate '{scene.sceneId}': need {need} free '{fid}' agents, have {have}.");
                    return false;
                }
            }
            return true;
        }

        public void DeactivateScene(string reason)
        {
            if (_activeSceneCoroutine == null) return;

            string sceneId = CurrentContext != null && CurrentContext.scene != null ? CurrentContext.scene.sceneId : "";
            Debug.Log($"[DOOMS][SceneDirector] Deactivating active scene. Reason: {reason}");
            EmitSceneEvent("SCENE_ENDED", sceneId, "", new Dictionary<string, object> { { "reason", reason } });
            AreaAnchorIndex.RefreshAll(null);
            StopCoroutine(_activeSceneCoroutine);
            _activeSceneCoroutine = null;

            CleanupActiveScene();
        }

        private IEnumerator RunScene(SceneSO scene, float intensity)
        {
            var ctx = new SceneRuntimeContext
            {
                scene = scene,
                intensity = intensity,
                onAdvancePhase = (nextId) => { /* direct traversal is handled sequentially in loop */ }
            };
            CurrentContext = ctx;

            var freeAgentsByFaction = new Dictionary<string, List<DoomsAgentTag>>(StringComparer.OrdinalIgnoreCase);
            RefreshFreeAgentsByFaction(freeAgentsByFaction);

            // 2. Load the NodeCanvas Graph
            var graph = scene.graph;
            if (graph == null)
            {
                Debug.LogError($"[DOOMS][SceneDirector] Scene '{scene.sceneId}' has no graph defined.");
                DeactivateScene("No graph defined");
                yield break;
            }

            var primeNode = graph.primeNode as Nodes.ScenePhaseNode;
            if (primeNode == null)
            {
                Debug.LogError($"[DOOMS][SceneDirector] Scene graph prime node is missing or is not a ScenePhaseNode.");
                DeactivateScene("Invalid prime node");
                yield break;
            }

            var currentNode = primeNode;
            Debug.Log($"[DOOMS][SceneDirector] Starting Scene: {scene.sceneId} at prime node '{currentNode.phaseId}'");

            // Global watchdog: prevent a stuck scene from holding agents forever.
            float watchdogDeadline = Time.time + Mathf.Max(30f, scene.baseDurationSec * 1.5f);

            while (currentNode != null)
            {
                // Assign roles specifically for this phase
                AssignRolesForPhase(currentNode, freeAgentsByFaction, ctx);

                // Publish Directives to Board
                PublishPhaseDirectives(ctx, currentNode);

                // Enter Node
                currentNode.OnEnter(ctx);
                OnPhaseTransition?.Invoke(scene.sceneId, currentNode.phaseId);
                EmitSceneEvent("PHASE_ENTERED", scene.sceneId, currentNode.phaseId,
                    new Dictionary<string, object>
                    {
                        { "node_type", currentNode.GetType().Name },
                        { "role_count", currentNode.roles != null ? currentNode.roles.Count : 0 }
                    });
                EmitRoleAssignments(scene.sceneId, currentNode.phaseId, ctx);

                // Broadcast the ambient mood + spatial influence for this phase so
                // unassigned T4 agents stay coherent with what's happening here.
                PublishAmbientMood(ctx);

                // Phase loop
                ctx.elapsedInPhase = 0f;
                bool watchdogHit = false;
                bool timelineTriggered = false;

                while (!currentNode.ShouldAdvance(ctx))
                {
                    if (Time.time > watchdogDeadline)
                    {
                        Debug.LogWarning($"[DOOMS][SceneDirector] Watchdog timeout for scene '{scene.sceneId}' at phase '{currentNode.phaseId}'. Forcing disperse.");
                        watchdogHit = true;
                        break;
                    }

                    // Timeline logic: if any roles are Timeline roles, wait for arrival then play, and advance on completion
                    if (ctx.reservedTimelines.Count > 0)
                    {
                        if (!timelineTriggered)
                        {
                            if (AreTimelineActorsArrived(ctx))
                            {
                                TriggerTimelines(ctx);
                                timelineTriggered = true;
                            }
                        }
                        else
                        {
                            if (AreTimelinesComplete(ctx))
                            {
                                Debug.Log($"[DOOMS][SceneDirector] All choreographed timelines in phase completed. Advancing phase early.");
                                break;
                            }
                        }
                    }

                    yield return null;
                    ctx.elapsedInPhase += Time.deltaTime;
                    currentNode.OnTick(ctx, Time.deltaTime);
                }

                // Exit Node
                currentNode.OnExit(ctx);

                if (watchdogHit)
                {
                    ClearPhaseDirectives(ctx);
                    break;
                }

                // Find next node in traversal path (relation gating if applicable)
                Nodes.ScenePhaseNode nextNode = ResolveNextNode(currentNode, ctx);

                // Clear previous phase's directives for our assigned actors so they don't linger
                ClearPhaseDirectives(ctx);

                currentNode = nextNode;
                if (currentNode != null)
                {
                    Debug.Log($"[DOOMS][SceneDirector] Advancing to next phase node: '{currentNode.phaseId}'");
                }
            }

            Debug.Log($"[DOOMS][SceneDirector] Scene '{scene.sceneId}' completed traversal path.");
            EmitSceneEvent("SCENE_ENDED", scene.sceneId, "", new Dictionary<string, object> { { "reason", "normal_completion" } });
            _activeSceneCoroutine = null;
            AreaAnchorIndex.RefreshAll(null);
            CleanupActiveScene();
            OnSceneEnded?.Invoke(scene.sceneId);
        }

        private Nodes.ScenePhaseNode ResolveNextNode(Nodes.ScenePhaseNode currentNode, SceneRuntimeContext ctx)
        {
            // Relation-gated branching uses index 0 = onTrue, index 1 = onFalse.
            if (currentNode is RelationGatedTransitionNode gate)
            {
                bool matches = gate.EvaluateRelation();
                int idx = matches ? 0 : 1;
                if (currentNode.outConnections != null && idx < currentNode.outConnections.Count)
                {
                    var conn = currentNode.outConnections[idx];
                    if (conn != null && conn.targetNode is Nodes.ScenePhaseNode t) return t;
                }
                return null;
            }

            if (currentNode.outConnections != null && currentNode.outConnections.Count > 0)
            {
                var conn = currentNode.outConnections[0];
                if (conn != null && conn.targetNode is Nodes.ScenePhaseNode targetPhase) return targetPhase;
            }
            return null;
        }

        private void EmitRoleAssignments(string sceneId, string phaseId, SceneRuntimeContext ctx)
        {
            if (!emitTelemetry) return;
            foreach (var kv in ctx.roleAssignments)
            {
                foreach (var agentId in kv.Value)
                {
                    EmitSceneEvent("ROLE_ASSIGNED", sceneId, phaseId, new Dictionary<string, object>
                    {
                        { "role_id", kv.Key },
                        { "agent_id", agentId }
                    });
                }
            }
        }

        private void AssignRolesForPhase(Nodes.ScenePhaseNode node, Dictionary<string, List<DoomsAgentTag>> freeAgents, SceneRuntimeContext ctx)
        {
            // Clear prior assignments & releases
            ReleaseContextReservations(ctx);
            RefreshFreeAgentsByFaction(freeAgents);

            foreach (var role in node.roles)
            {
                if (role == null || string.IsNullOrEmpty(role.factionId)) continue;

                if (!ctx.roleAssignments.ContainsKey(role.roleId))
                {
                    ctx.roleAssignments[role.roleId] = new List<string>();
                }

                // Find matching available agents of this faction
                if (freeAgents.TryGetValue(role.factionId, out var factionPool) && factionPool.Count > 0)
                {
                    int targetCount = role.count;
                    if (role.roleKind == RoleKind.Point)
                    {
                        targetCount = CountFreePointSlots(role.pointTag, role.factionId);
                    }

                    int toAssign = Mathf.Min(targetCount, factionPool.Count);
                    for (int i = 0; i < toAssign; i++)
                    {
                        var agent = factionPool[0];
                        factionPool.RemoveAt(0);
                        string occId = !string.IsNullOrEmpty(agent.agentId) ? agent.agentId : agent.gameObject.name;
                        if (string.IsNullOrEmpty(agent.agentId))
                        {
                            agent.agentId = occId;
                            Debug.LogWarning($"[DOOMS][SceneDirector] Agent '{agent.gameObject.name}' had empty agentId during role assignment. Using fallback '{occId}'.");
                        }

                        bool assigned = false;

                        if (role.roleKind == RoleKind.Point)
                        {
                            // Find closest free interaction point of matching tag
                            var ip = InteractionPointIndex.Nearest(agent.transform.position, role.pointTag, agent.factionId);
                            if (ip != null && ip.TryOccupy(occId))
                            {
                                agent.reservedBySceneId = ctx.scene.sceneId;
                                ctx.roleAssignments[role.roleId].Add(occId);
                                ctx.reservedPoints[occId] = ip;
                                assigned = true;

                                Debug.Log($"[DOOMS][SceneDirector] Assigned Agent '{occId}' to Role '{role.roleId}' at InteractionPoint '{ip.gameObject.name}'");
                            }
                        }
                        else if (role.roleKind == RoleKind.Area)
                        {
                            // Find closest free area of matching tag
                            var area = AreaAnchorIndex.Nearest(agent.transform.position, role.areaTag, agent.factionId);
                            if (area != null && area.TryOccupy(occId))
                            {
                                agent.reservedBySceneId = ctx.scene.sceneId;
                                ctx.roleAssignments[role.roleId].Add(occId);
                                ctx.reservedAreas[occId] = area;
                                assigned = true;

                                Debug.Log($"[DOOMS][SceneDirector] Assigned Agent '{occId}' to Role '{role.roleId}' at AreaAnchor '{area.gameObject.name}'");
                            }
                        }
                        else if (role.roleKind == RoleKind.Timeline)
                        {
                            // Find timeline matching timelineAnchorId
                            var timeline = TimelineAnchorIndex.Find(role.timelineAnchorId);
                            if (timeline != null && timeline.TryOccupySlot(role.timelineSlotId, occId))
                            {
                                agent.reservedBySceneId = ctx.scene.sceneId;
                                ctx.roleAssignments[role.roleId].Add(occId);
                                ctx.reservedTimelines[occId] = timeline;
                                assigned = true;

                                Debug.Log($"[DOOMS][SceneDirector] Assigned Agent '{occId}' to Role '{role.roleId}' at TimelineAnchor '{timeline.gameObject.name}', slot '{role.timelineSlotId}'");
                            }
                        }

                        if (!assigned)
                        {
                            // Put back if we failed to get a spot
                            factionPool.Add(agent);
                        }
                    }
                }

                if (ctx.roleAssignments[role.roleId].Count < role.count && !role.optional)
                {
                    Debug.LogWarning($"[DOOMS][SceneDirector] Could not fully satisfy non-optional role '{role.roleId}' ({ctx.roleAssignments[role.roleId].Count}/{role.count} filled)");
                }
            }
        }

        private void RefreshFreeAgentsByFaction(Dictionary<string, List<DoomsAgentTag>> freeAgentsByFaction)
        {
            if (freeAgentsByFaction == null) return;
            freeAgentsByFaction.Clear();

            var allTags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            foreach (var tag in allTags)
            {
                if (tag == null || !string.IsNullOrEmpty(tag.reservedBySceneId)) continue;
                if (string.IsNullOrEmpty(tag.factionId)) continue;
                if (string.IsNullOrEmpty(tag.agentId))
                {
                    tag.agentId = tag.gameObject.name;
                    Debug.LogWarning($"[DOOMS][SceneDirector] Agent '{tag.gameObject.name}' had empty agentId while refreshing free pool. Using fallback '{tag.agentId}'.");
                }

                if (!freeAgentsByFaction.TryGetValue(tag.factionId, out var list))
                {
                    list = new List<DoomsAgentTag>();
                    freeAgentsByFaction[tag.factionId] = list;
                }
                list.Add(tag);
            }
        }

        private int CountFreePointSlots(string pointTag, string factionId)
        {
            if (string.IsNullOrEmpty(pointTag)) return 0;

            int slots = 0;
            foreach (var p in InteractionPointIndex.Query(pointTag, factionId))
            {
                if (p == null) continue;
                int cap = Mathf.Max(1, p.capacity);
                slots += Mathf.Max(0, cap - p.OccupancyCount);
            }
            return slots;
        }

        private void PublishAmbientMood(SceneRuntimeContext ctx)
        {
            if (ctx == null || ctx.scene == null) return;

            var profile = AmbientMoodProfileSO.Instance;
            SceneMood mood = profile != null
                ? profile.ClassifyMood(ctx.scene.tags, ctx.intensity)
                : SceneMood.Calm;

            float baseRadius = profile != null ? profile.baseInfluenceRadius : 12f;
            ComputeEpicenter(ctx, out Vector3 center, out float spread);
            float radius = baseRadius + spread;

            string[] factions = ctx.scene.requiredFactions != null
                ? ctx.scene.requiredFactions.ToArray()
                : new string[0];
            var moodTelemetry = new Dictionary<string, object>
            {
                { "mood", mood.ToString() },
                { "intensity", ctx.intensity },
                { "center_x", center.x },
                { "center_z", center.z },
                { "radius", radius },
                { "factions", string.Join(",", factions) }
            };

            if (mood == SceneMood.Calm)
            {
                AmbientMoodBoard.Clear();
                EmitSceneEvent("AMBIENT_MOOD", ctx.scene.sceneId, "", moodTelemetry);
                DoomsDebug.Log(DoomsDebug.Category.SceneDirector,
                    $"Mood=Calm for '{ctx.scene.sceneId}' — clearing board so extras proceed to procedural.");
                Debug.Log($"[DOOMS][SceneDirector] Ambient mood 'Calm' for '{ctx.scene.sceneId}': board cleared (non-reactive).");
                return;
            }
            AmbientMoodBoard.Set(mood, ctx.intensity, center, radius, ctx.scene.sceneId, factions);
            EmitSceneEvent("AMBIENT_MOOD", ctx.scene.sceneId, "", moodTelemetry);
            DoomsDebug.Log(DoomsDebug.Category.SceneDirector,
                $"Ambient '{mood}' published for '{ctx.scene.sceneId}' at {center} r={radius:F1} intensity={ctx.intensity:F2}.");
            Debug.Log($"[DOOMS][SceneDirector] Ambient mood '{mood}' published for '{ctx.scene.sceneId}' at {center} r={radius:F1}.");
        }

        private void ComputeEpicenter(SceneRuntimeContext ctx, out Vector3 center, out float spread)
        {
            var points = new List<Vector3>();
            foreach (var kv in ctx.reservedPoints)
                if (kv.Value != null && kv.Value.GetAnchor() != null) points.Add(kv.Value.GetAnchor().position);
            foreach (var kv in ctx.reservedAreas)
                if (kv.Value != null) points.Add(kv.Value.transform.position);
            foreach (var kv in ctx.reservedTimelines)
                if (kv.Value != null) points.Add(kv.Value.transform.position);

            if (points.Count == 0)
            {
                center = transform.position;
                spread = 0f;
                return;
            }

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            center = sum / points.Count;

            float maxD = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                float d = Vector3.Distance(center, points[i]);
                if (d > maxD) maxD = d;
            }
            spread = maxD;
        }

        private void PublishPhaseDirectives(SceneRuntimeContext ctx, Nodes.ScenePhaseNode node)
        {
            foreach (var role in node.roles)
            {
                if (role == null) continue;

                if (ctx.roleAssignments.TryGetValue(role.roleId, out var agentIds))
                {
                    foreach (var agentId in agentIds)
                    {
                        var directive = new FactionDirectiveBoard.AgentDirective
                        {
                            directiveKind = role.roleKind.ToString(),
                            pointTag = role.pointTag,
                            animationState = role.animationState,
                            
                            areaTag = role.areaTag,
                            behavior = role.behavior,
                            preferredBlendTree = role.preferredBlendTree,
                            pairWithFactionId = role.pairWithFactionId,
                            
                            timelineAnchorId = role.timelineAnchorId,
                            timelineSlotId = role.timelineSlotId,

                            sceneId = ctx.scene.sceneId,
                            phaseId = node.phaseId,
                            ttlSec = node.maxDurationSec * 1.5f
                        };
                        FactionDirectiveBoard.Publish(agentId, directive);
                    }
                }
            }
        }

        private void ClearPhaseDirectives(SceneRuntimeContext ctx)
        {
            foreach (var kv in ctx.roleAssignments)
            {
                foreach (var agentId in kv.Value)
                {
                    FactionDirectiveBoard.RemoveAgent(agentId);
                }
            }
        }

        private void ReleaseContextReservations(SceneRuntimeContext ctx)
        {
            // Release points and free agents
            foreach (var kv in ctx.reservedPoints)
            {
                var agentId = kv.Key;
                var ip = kv.Value;
                if (ip != null) ip.Release(agentId);
            }
            ctx.reservedPoints.Clear();

            // Release areas
            foreach (var kv in ctx.reservedAreas)
            {
                var agentId = kv.Key;
                var area = kv.Value;
                if (area != null) area.Release(agentId);
            }
            ctx.reservedAreas.Clear();

            // Release timeline slots
            foreach (var kv in ctx.reservedTimelines)
            {
                var agentId = kv.Key;
                var timeline = kv.Value;
                if (timeline != null) timeline.Release(agentId);
            }
            ctx.reservedTimelines.Clear();

            var allTags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            foreach (var t in allTags)
            {
                if (t != null && t.reservedBySceneId == ctx.scene.sceneId)
                {
                    t.reservedBySceneId = "";
                }
            }

            ctx.roleAssignments.Clear();
        }

        // ---- Timeline verification and triggers -----------------------------
        private bool AreTimelineActorsArrived(SceneRuntimeContext ctx)
        {
            foreach (var kv in ctx.reservedTimelines)
            {
                var agentId = kv.Key;
                var timeline = kv.Value;
                if (timeline == null) continue;

                var slot = timeline.GetSlotForAgent(agentId);
                if (slot == null || slot.anchorTransform == null) continue;

                var agentObj = FindAgentGameObject(agentId);
                if (agentObj == null) continue;

                float dist = Vector3.Distance(agentObj.transform.position, slot.anchorTransform.position);
                
                // Read tolerance: try to lookup the RoleSlot arrivalTolerance
                float tolerance = 2.0f;
                if (ctx.scene != null && ctx.scene.graph != null && ctx.scene.graph.primeNode is ScenePhaseNode primePhaseNode)
                {
                    foreach (var role in primePhaseNode.roles)
                    {
                        if (string.Equals(role.timelineSlotId, slot.slotId, StringComparison.OrdinalIgnoreCase))
                        {
                            tolerance = role.arrivalTolerance;
                            break;
                        }
                    }
                }

                if (dist > tolerance) return false;
            }
            return true;
        }

        private void TriggerTimelines(SceneRuntimeContext ctx)
        {
            var uniqueTimelines = new HashSet<TimelineAnchor>(ctx.reservedTimelines.Values);
            foreach (var timeline in uniqueTimelines)
            {
                if (timeline != null && !timeline.IsPlaying)
                {
                    timeline.Play();
                }
            }
        }

        private bool AreTimelinesComplete(SceneRuntimeContext ctx)
        {
            var uniqueTimelines = new HashSet<TimelineAnchor>(ctx.reservedTimelines.Values);
            foreach (var timeline in uniqueTimelines)
            {
                if (timeline != null && !timeline.IsComplete)
                {
                    return false;
                }
            }
            return true;
        }

        private GameObject FindAgentGameObject(string agentId)
        {
            var tags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            foreach (var t in tags)
            {
                if (t != null && string.Equals(t.agentId, agentId, StringComparison.OrdinalIgnoreCase))
                    return t.gameObject;
            }
            return null;
        }

        private void CleanupActiveScene()
        {
            if (CurrentContext != null)
            {
                ReleaseContextReservations(CurrentContext);
                CurrentContext = null;
            }
            FactionDirectiveBoard.Clear();
            AmbientMoodBoard.Clear();
        }

        private void OnDestroy()
        {
            CleanupActiveScene();
        }

        // ------------------------------------------------------------------
        // Telemetry: POST scene events to backend so the debug client can
        // visualise SCENE_ACTIVATED / PHASE_ENTERED / ROLE_ASSIGNED / SCENE_ENDED.
        // Errors are swallowed; telemetry must never break the scene loop.
        // ------------------------------------------------------------------
        private void EmitSceneEvent(string eventType, string sceneId, string phaseId, Dictionary<string, object> data)
        {
            if (!emitTelemetry) return;
            try { StartCoroutine(PostSceneEvent(eventType, sceneId, phaseId, data)); }
            catch (Exception e) { Debug.LogWarning($"[DOOMS][SceneDirector] EmitSceneEvent failed: {e.Message}"); }
        }

        private IEnumerator PostSceneEvent(string eventType, string sceneId, string phaseId, Dictionary<string, object> data)
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;

            string url = $"{baseUrl}/dooms/scene_event";
            string json = BuildSceneEventJson(eventType, sceneId, phaseId, data);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 5;
                yield return req.SendWebRequest();
                // Silently ignore failures; telemetry is best-effort.
            }
        }

        private string ResolveBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(backendBaseUrlOverride))
                return backendBaseUrlOverride.TrimEnd('/');

            var pusher = FindFirstObjectByType<DoomsConfigPusher>();
            if (pusher != null)
            {
                var url = pusher.GetResolvedBaseUrl();
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return null;
        }

        private static string BuildSceneEventJson(string eventType, string sceneId, string phaseId, Dictionary<string, object> data)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"event_type\":").Append(JsonStr(eventType)).Append(',');
            sb.Append("\"scene_id\":").Append(JsonStr(sceneId)).Append(',');
            sb.Append("\"phase_id\":").Append(JsonStr(phaseId)).Append(',');
            sb.Append("\"data\":{");
            bool first = true;
            if (data != null)
            {
                foreach (var kv in data)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonStr(kv.Key)).Append(':');
                    AppendJsonValue(sb, kv.Value);
                }
            }
            sb.Append("}}");
            return sb.ToString();
        }

        private static void AppendJsonValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            switch (v)
            {
                case string s: sb.Append(JsonStr(s)); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case float f: sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                default: sb.Append(JsonStr(v.ToString())); break;
            }
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
